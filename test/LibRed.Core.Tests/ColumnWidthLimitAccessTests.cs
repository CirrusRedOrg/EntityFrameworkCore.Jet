using System.Data.OleDb;
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
// DECLARED RECORD SIZE. TdefBuilder checked only that the fixed region fits the TDEF's 2-byte offset
// fields (65535). ACE's real limit is the 4060-byte record cap applied to the widest row the declaration
// permits, and a table over it makes the whole database unopenable - "Unrecognized database format", the
// same damage as a 100-character rename. A plain CreateTable of 252 GUID columns did exactly that.
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
        var specs = Enumerable.Range(0, guids)
            .Select(i => new ColumnSpec($"G{i}", JetDataType.Guid, 16, IsFixedLength: true))
            .Append(new ColumnSpec("T", JetDataType.Text, textWidth, IsFixedLength: true))
            .ToList();

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
