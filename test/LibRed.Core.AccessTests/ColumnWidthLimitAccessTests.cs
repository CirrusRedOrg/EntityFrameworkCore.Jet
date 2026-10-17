using System.Data.OleDb;
using System.Reflection;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

// Two width limits ACE enforces and LibRed did not, both of the guarded-on-create / unguarded-on-modify
// shape - except that here the create path was under-guarded too.
//
// PER-FIELD WIDTH. A non-Memo/OLE column holds at most 510 bytes. ACE refuses a wider one through its own
// DDL on CREATE TABLE, ALTER COLUMN and ADD COLUMN alike ("Size of field is too long"); LibRed accepted
// Text(2000) and Binary(4000) on all three and produced a table ACE opens but cannot query.
//
// DECLARED RECORD SIZE. TableDefinition checked only that the fixed region fits the TDEF's 2-byte offset
// fields (65535). ACE's real limit is the 4060-byte record cap applied to the widest row the declaration
// permits, and a table over it makes the whole database unopenable - "Unrecognized database format", the
// same damage as a 100-character rename. A plain CreateTable of 252 GUID columns did exactly that.
[Collection(AceCollection.Name)]
public class ColumnWidthLimitAccessTests : TempDatabaseTest
{
    private static ColumnSpec Id => new("Id", JetDataType.Int32, 4, IsFixedLength: true);

    [Theory]
    [InlineData(JetDataType.Text, 511)]
    [InlineData(JetDataType.Text, 2000)]
    [InlineData(JetDataType.Binary, 511)]
    [InlineData(JetDataType.Binary, 4000)]
    public void A_column_wider_than_ace_stores_is_refused_on_every_path(JetDataType type, int width)
    {
        using var database = Fresh(out _);
        var wide = new ColumnSpec("Wide", type, width, IsFixedLength: false);

        Assert.Throws<NotSupportedException>(() => database.CreateTable("T2", [Id, wide], primaryKey: ["Id"]));
        Assert.Throws<NotSupportedException>(() => database.AddColumn("T", wide));
        Assert.Throws<NotSupportedException>(() =>
            database.AlterColumn("T", "Value", new ColumnSpec("Value", type, width, IsFixedLength: false)));
    }

    // 510 is the boundary, not a rounded-down approximation of it: ACE stores it and so must LibRed.
    [Fact]
    public void Exactly_510_bytes_is_allowed_and_ace_reads_it()
    {
        string path;
        using (var database = Fresh(out path))
        {
            database.AddColumn("T", new ColumnSpec("Wide", JetDataType.Text, 510, IsFixedLength: false));
            database.OpenTable("T").Insert([2, "x", new string('y', 255)]);
        }

        using OleDbConnection connection = AceTestDatabase.Open(path);
        using OleDbCommand read = connection.CreateCommand();
        read.CommandText = "SELECT Wide FROM T WHERE Id = 2";
        Assert.Equal(new string('y', 255), read.ExecuteScalar());
    }

    // The fixed region: 4022 bytes over 252 columns is the most ACE will open, and 4023 is not - measured
    // on both sides of the boundary at two very different column counts, since the null bitmap is part of
    // the sum (8 columns reaches 4053 for the same reason).
    [Theory]
    [InlineData(251, 6, true)]      // 4022 fixed bytes, 252 columns
    [InlineData(251, 7, false)]     // 4023
    public void A_fixed_region_past_the_record_cap_is_refused(int guids, int textWidth, bool allowed)
    {
        ColumnSpec[] specs = FixedRegion(guids, textWidth);

        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "record-cap-");
        using (var database = JetDatabase.Open(path, readOnly: false))
        {
            if (!allowed)
            {
                Assert.Throws<NotSupportedException>(() => database.CreateTable("W", specs));
                return;
            }

            database.CreateTable("W", specs);
        }

        // What is allowed has to be genuinely allowed — ACE must still open it.
        using OleDbConnection connection = AceTestDatabase.Open(path);
        using OleDbCommand read = connection.CreateCommand();
        read.CommandText = "SELECT COUNT(*) FROM W";
        Assert.Equal(0, Convert.ToInt32(read.ExecuteScalar()));
    }

    // Variable columns cost only their 2 bytes of offset table, not their declared width — so an all-Text
    // table is unconstrained while a wide fixed region is not, and the guard must not confuse the two.
    [Fact]
    public void Variable_columns_are_charged_for_their_offset_only()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "record-cap-var-");
        var wide = Enumerable.Range(0, 8)
            .Select(i => new ColumnSpec($"V{i}", JetDataType.Text, 510, IsFixedLength: false))
            .ToList();

        using (var database = JetDatabase.Open(path, readOnly: false))
        {
            // 4080 declared bytes, all variable — far past the record cap as a sum, and legal.
            database.CreateTable("W", wide);

            // But two variable columns beside a 4018-byte fixed region fit and a third does not.
            var fixedRegion = Enumerable.Range(0, 251)
                .Select(i => new ColumnSpec($"G{i}", JetDataType.Guid, 16, IsFixedLength: true))
                .Append(new ColumnSpec("T", JetDataType.Text, 2, IsFixedLength: true))
                .ToList();
            database.CreateTable("F", [.. fixedRegion, .. wide.Take(2)]);
            Assert.Throws<NotSupportedException>(() =>
                database.CreateTable("F3", [.. fixedRegion, .. wide.Take(3)]));
        }

        using OleDbConnection connection = AceTestDatabase.Open(path);
        using OleDbCommand read = connection.CreateCommand();
        read.CommandText = "SELECT COUNT(*) FROM F";
        Assert.Equal(0, Convert.ToInt32(read.ExecuteScalar()));
    }

    public enum Outcome { Unopenable, TooLarge, Fits }

    // A calculated column is charged like a variable one, 2 bytes, against the declared size; its stored value then
    // counts in the row with a 27-byte envelope for a Long. DAO is the one route ACE offers to a calculated column,
    // and it does not check the cap: past it the database is unopenable, and inside it but without room for the
    // envelope every insert is too large. LibRed refuses the first on AddColumn and the second on Insert.
    [Theory]
    [InlineData(251, 6, Outcome.Unopenable)]   // 4022 fixed bytes — at the cap before the column
    [InlineData(251, 5, Outcome.Unopenable)]   // 4021
    [InlineData(251, 4, Outcome.TooLarge)]     // 4020: the column fits the declaration
    [InlineData(249, 10, Outcome.TooLarge)]    // 3994
    [InlineData(249, 9, Outcome.Fits)]         // 3993: 27 bytes more room, and a row with its value fits
    public void A_calculated_column_is_held_to_the_record_cap_as_ace_holds_it(int guids, int textWidth, Outcome expected)
    {
        object? engine = AceTestDatabase.CreateDaoEngine();
        Assert.SkipWhen(engine is null, "DAO is not available in this process.");
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "record-cap-calc-");
        try
        {
            using (var database = JetDatabase.Open(path, readOnly: false)) database.CreateTable("W", FixedRegion(guids, textWidth));

            object db = Invoke(engine!, "OpenDatabase", path)!;
            try
            {
                object td = Item(Get(db, "TableDefs"), "W");
                object field = Invoke(td, "CreateField", "C", DaoLong)!;
                Set(field, "Expression", "1+1");
                Invoke(Get(td, "Fields"), "Append", field);
            }
            finally { Invoke(db, "Close"); AceTestDatabase.ReleaseAbandonedComObjects(); }

            Outcome ace;
            try
            {
                using OleDbConnection connection = AceTestDatabase.Open(path, attempts: 1);
                using OleDbCommand insert = connection.CreateCommand();
                insert.CommandText = "INSERT INTO W (T) VALUES ('x')";
                insert.ExecuteNonQuery();
                ace = Outcome.Fits;
            }
            catch (Exception e) when ((e.InnerException ?? e).Message.Contains("Unrecognized database format", StringComparison.Ordinal))
            {
                ace = Outcome.Unopenable;   // raised by the open or by the first statement, whichever reaches the table
            }
            catch (OleDbException e) when (e.Message.Contains("Record is too large", StringComparison.Ordinal))
            {
                ace = Outcome.TooLarge;
            }
            finally { OleDbConnection.ReleaseObjectPool(); }
            Assert.Equal(expected, ace);

            Assert.Equal(expected, CalculatedThroughLibRed(guids, textWidth));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>The same column added, and the same row inserted, through LibRed on a copy of its own.</summary>
    private static Outcome CalculatedThroughLibRed(int guids, int textWidth)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "record-cap-calc-lib-");
        try
        {
            using var database = JetDatabase.Open(path, readOnly: false);
            database.CreateTable("W", FixedRegion(guids, textWidth));
            try { database.AddColumn("W", ColumnSpec.Calculated("C", JetDataType.Int32, "1+1")); }
            catch (NotSupportedException) { return Outcome.Unopenable; }
            try
            {
                var row = new object?[guids + 2];
                row[guids] = "x";
                database.OpenTable("W").Insert(row);
                return Outcome.Fits;
            }
            catch (InvalidOperationException e) when (e.Message.Contains("Record is too large", StringComparison.Ordinal))
            {
                return Outcome.TooLarge;
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private const int DaoLong = 4;

    private static ColumnSpec[] FixedRegion(int guids, int textWidth) =>
        [.. Enumerable.Range(0, guids).Select(i => new ColumnSpec($"G{i}", JetDataType.Guid, 16, IsFixedLength: true))
            .Append(new ColumnSpec("T", JetDataType.Text, textWidth, IsFixedLength: true))];

    private static object Get(object target, string member) =>
        target.GetType().InvokeMember(member, BindingFlags.GetProperty, null, target, null)!;

    private static void Set(object target, string member, object value) =>
        target.GetType().InvokeMember(member, BindingFlags.SetProperty, null, target, [value]);

    private static object Item(object collection, object key) =>
        collection.GetType().InvokeMember("Item", BindingFlags.GetProperty, null, collection, [key])!;

    private static object? Invoke(object target, string member, params object?[] args) =>
        target.GetType().InvokeMember(member, BindingFlags.InvokeMethod, null, target, args);

    private static JetDatabase Fresh(out string path)
    {
        path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "column-width-");
        var database = JetDatabase.Open(path, readOnly: false);
        database.CreateTable("T",
            [Id, new ColumnSpec("Value", JetDataType.Text, 100, IsFixedLength: false)], primaryKey: ["Id"]);
        database.OpenTable("T").Insert([1, "hello"]);
        return database;
    }
}