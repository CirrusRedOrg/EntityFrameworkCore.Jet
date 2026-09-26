using System.Buffers.Binary;
using System.Data.OleDb;
using System.Globalization;
using System.Reflection;
using LibRed;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// A column descriptor's id (0x05), variable-table index (0x07) and column number (0x09) come out as ACE
/// writes them through DROP COLUMN, ADD COLUMN, a retype and CREATE INDEX. The interesting case is ADD COLUMN
/// after a drop: it renumbers every column's 0x09 to its position, and an added fixed column's 0x07 counts the
/// dropped variable columns too.
/// </summary>
[Collection(AceCollection.Name)]
public class ColumnNumberingAccessTests
{
    private const string Create = "CREATE TABLE T (A LONG, B LONG, C TEXT(10), D LONG, E TEXT(10))";

    public static TheoryData<string[]> Sequences => new()
    {
        new[] { Create, "ALTER TABLE T DROP COLUMN B", "ALTER TABLE T ADD COLUMN F LONG" },
        new[] { Create, "ALTER TABLE T DROP COLUMN B", "ALTER TABLE T ALTER COLUMN D TEXT(5)" },
        new[] { Create, "ALTER TABLE T DROP COLUMN B", "CREATE INDEX ix ON T (D)" },
        new[] { Create, "ALTER TABLE T DROP COLUMN B", "ALTER TABLE T ADD COLUMN F TEXT(10)", "ALTER TABLE T DROP COLUMN C", "ALTER TABLE T ADD COLUMN G LONG" },
        new[] { Create, "ALTER TABLE T ALTER COLUMN B TEXT(5)", "ALTER TABLE T ADD COLUMN F LONG" },
        new[] { Create, "ALTER TABLE T DROP COLUMN E", "ALTER TABLE T ADD COLUMN F LONG" },
        new[] { Create, "ALTER TABLE T DROP COLUMN B", "ALTER TABLE T DROP COLUMN D" },
        new[] { Create, "ALTER TABLE T DROP COLUMN A", "ALTER TABLE T ADD COLUMN F LONG" },
        new[] { Create, "ALTER TABLE T DROP COLUMN B", "ALTER TABLE T DROP COLUMN D", "ALTER TABLE T ADD COLUMN F TEXT(10)" },
        new[] { Create, "ALTER TABLE T DROP COLUMN C", "ALTER TABLE T DROP COLUMN E", "ALTER TABLE T ADD COLUMN F LONG", "ALTER TABLE T ADD COLUMN G TEXT(10)" },
    };

    [Theory]
    [MemberData(nameof(Sequences))]
    public void Descriptor_numbers_follow_ace(string[] statements)
    {
        string ace = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "colnum-ace-");
        string libred = TemporaryDatabase.CopyPath(ace, "colnum-libred-");
        RunBoth(ace, libred, statements);
    }

    // 0x09 is DAO's Field.OrdinalPosition, which DAO sets freely — ties and gaps included — and keeps the
    // descriptors sorted by. Moving E first and B last gives ordinals A 0, E 0, B 1, C 2, D 3 and then B 7, in
    // descriptor order A E C D B. An ADD COLUMN after that ranks them, keeping the tie.
    [Fact]
    public void Descriptor_numbers_follow_ace_after_dao_reorders_the_columns()
    {
        object? engine = AceTestDatabase.CreateDaoEngine();
        Assert.SkipWhen(engine is null, "DAO is not registered in this bitness.");
        string ace = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "colnum-ace-");
        using (OleDbConnection conn = AceTestDatabase.Open(ace))
        using (OleDbCommand cmd = conn.CreateCommand())
        {
            cmd.CommandText = Create;
            cmd.ExecuteNonQuery();
            cmd.CommandText = "INSERT INTO T VALUES (1, 2, 'c1', 4, 'e1')";
            cmd.ExecuteNonQuery();
        }
        object db = Invoke(engine!, "OpenDatabase", ace)!;
        try
        {
            object fields = Get(Item(Get(db, "TableDefs")!, "T"), "Fields")!;
            Set(Item(fields, "E"), "OrdinalPosition", 0);
            Set(Item(fields, "B"), "OrdinalPosition", 7);
        }
        finally { Invoke(db, "Close"); }
        AceTestDatabase.ReleaseAbandonedComObjects();

        string libred = TemporaryDatabase.CopyPath(ace, "colnum-libred-");
        Assert.Equal("A:0/0/0  E:4/1/0  C:2/0/2  D:3/1/3  B:1/0/7", Describe(libred));
        RunBoth(ace, libred, ["ALTER TABLE T DROP COLUMN C", "ALTER TABLE T ADD COLUMN F LONG",
            "INSERT INTO T (A, B, D, E, F) VALUES (2, 3, 5, 'e2', 6)",
            "CREATE INDEX ixE ON T (E)", "ALTER TABLE T ALTER COLUMN D TEXT(5)",
            "INSERT INTO T (A, B, D, E, F) VALUES (3, 4, 'd3', 'e3', 7)"]);
    }

    private static void RunBoth(string ace, string libred, string[] statements)
    {
        try
        {
            foreach (string sql in statements)
            {
                using (OleDbConnection conn = AceTestDatabase.Open(ace))
                using (OleDbCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    cmd.ExecuteNonQuery();
                }
                using (var db = JetDatabase.Open(libred, readOnly: false))
                    new QueryEngine(db).ExecuteNonQuery(sql);

                Assert.Equal(Describe(ace), Describe(libred));
            }

            // And each engine reads the other's rows alike, whatever order the descriptors are in.
            Assert.Equal(ReadThroughAce(ace), ReadThroughAce(libred));
            Assert.Equal(ReadThroughAce(ace), ReadThroughLibRed(ace));
        }
        finally
        {
            TemporaryDatabase.Delete(ace);
            TemporaryDatabase.Delete(libred);
        }
    }

    // Unordered, since a sequence may drop any column; each reader sorts its rows after the header instead.
    private const string ReadBack = "SELECT * FROM T";

    private static string ReadThroughAce(string path)
    {
        using OleDbConnection conn = AceTestDatabase.Open(path);
        using OleDbCommand cmd = conn.CreateCommand();
        cmd.CommandText = ReadBack;
        using OleDbDataReader r = cmd.ExecuteReader();
        var rows = new List<string>();
        while (r.Read())
            rows.Add(string.Join(" ", Enumerable.Range(0, r.FieldCount).Select(i => r.IsDBNull(i) ? "null" : Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture))));
        return string.Join(" ", Enumerable.Range(0, r.FieldCount).Select(r.GetName)) + " | " + string.Join(" | ", rows.Order(StringComparer.Ordinal));
    }

    private static string ReadThroughLibRed(string path)
    {
        using var db = JetDatabase.Open(path);
        var result = new QueryEngine(db).ExecuteQuery(ReadBack);
        var rows = new List<string>();
        foreach (object?[] row in result.Rows)
            rows.Add(string.Join(" ", row.Select(v => v is null ? "null" : Convert.ToString(v, CultureInfo.InvariantCulture))));
        return string.Join(" ", result.Columns.Select(c => c.Name)) + " | " + string.Join(" | ", rows.Order(StringComparer.Ordinal));
    }

    private static object Item(object collection, object key) =>
        collection.GetType().InvokeMember("Item", BindingFlags.GetProperty, null, collection, [key])!;
    private static object? Get(object target, string name) =>
        target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null);
    private static void Set(object target, string name, object value) =>
        target.GetType().InvokeMember(name, BindingFlags.SetProperty, null, target, [value]);
    private static object? Invoke(object target, string name, params object?[] args) =>
        target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, args);

    // name:0x05/0x07/0x09 for each descriptor, in descriptor order.
    private static string Describe(string path)
    {
        using var db = JetDatabase.Open(path);
        return string.Join("  ", db.Catalog.FindTable("T")!.Columns.Select(c =>
        {
            byte[] d = c.RawDescriptor!;
            return $"{c.Name}:{BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(5))}/"
                 + $"{BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(7))}/{BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(9))}";
        }));
    }
}
