using System.Reflection;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// The <c>MSysQueries</c> <c>Attribute=1</c> row is the query KIND, not a marker that the query is an action
/// query — SELECT (Flag 1) is one of its values, and Access writes the row on plain SELECTs too. Reading the
/// row's presence as "this is an action query" made every such SELECT unreadable; across a corpus of
/// real-world databases that was 221 of 592 stored queries. These author each shape through DAO, exactly as
/// the Access UI does, and check LibRed classifies and rebuilds it.
/// </summary>
public class StoredQueryKindAccessTests
{
    [Fact]
    public void Select_carrying_an_operation_row_is_read_as_a_view_not_an_action_query()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "qkind-select-");
        try
        {
            if (!Author(path, ("PlainSelect", "SELECT Shippers.CompanyName FROM Shippers"))) return;

            // ACE cannot be asked for this shape: DAO's CreateQueryDef writes no operation row, and DAO
            // refuses to open MSysQueries at all ("no read permission on 'MSysObjects'"). Only Access's own
            // query designer writes it — and it does so constantly: 221 of the 592 stored queries in the
            // reference corpus of real databases carry it. So write the row here and make ACE the judge of
            // whether the result is still a query it will run.
            AddOperationRow(path, "PlainSelect", flag: 1);

            using (var conn = AceTestDatabase.Open(path))
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM PlainSelect";
                Assert.Equal(3, Convert.ToInt32(cmd.ExecuteScalar()));   // Northwind ships three shippers
            }

            using var db = JetDatabase.Open(path);
            Assert.Equal((short)1, OperationFlag(db, "PlainSelect"));

            // The row is present and says SELECT, so this is a view — reading its mere presence as "action
            // query" is what made 221 of those corpus queries unreadable.
            Assert.False(db.Catalog.ActionQueries.ContainsKey("PlainSelect"));
            Assert.Contains("Shippers.CompanyName", db.Catalog.Views["PlainSelect"]);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>Adds the <c>Attribute=1</c> operation row Access writes for a query authored in its designer.</summary>
    private static void AddOperationRow(string path, string queryName, short flag)
    {
        using var db = JetDatabase.Open(path, readOnly: false);
        int objectId = QueryObjectId(db, queryName);

        Storage.Table queries = db.OpenTable("MSysQueries");
        var values = new object?[queries.Definition.Columns.Count];
        void Set(string column, object? value) => values[queries.Definition.FindColumn(column)!.Index] = value;
        Set("ObjectId", objectId);
        Set("Attribute", (byte)0x01);
        Set("Flag", flag);
        Set("Order", new byte[] { 0, 0, 0, 1 });   // 4-byte big-endian per-attribute counter
        new Storage.RowInserter(queries.Channel, queries.Definition).Insert(values, updateIndexes: true);
    }

    [Fact]
    public void Record_source_shape_with_no_column_rows_is_read_as_select_star()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "qkind-star-");
        try
        {
            if (!Author(path, ("StarQuery", "SELECT DISTINCTROW * FROM Shippers"))) return;

            using var db = JetDatabase.Open(path);
            string sql = db.Catalog.Views["StarQuery"];

            // No Attribute=6 rows at all is how Access encodes "*"; and DISTINCTROW is its own option bit
            // (0x08), separate from DISTINCT (0x02) — they are different keywords with different meanings.
            Assert.Contains("*", sql);
            Assert.Contains("DISTINCTROW", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SELECT DISTINCT ", sql, StringComparison.OrdinalIgnoreCase);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Top_percent_keeps_its_percent()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "qkind-top-");
        try
        {
            if (!Author(path, ("TopPct", "SELECT TOP 10 PERCENT Products.ProductName FROM Products "
                                       + "ORDER BY Products.ProductName"))) return;

            using var db = JetDatabase.Open(path);
            string sql = db.Catalog.Views["TopPct"];

            // PERCENT is a bit (0x20) riding alongside the TOP bit (0x10) on the same option row. Reading TOP
            // without it turns "TOP 10 PERCENT" into "TOP 10" — a silently different row count.
            Assert.Contains("TOP 10 PERCENT", sql, StringComparison.OrdinalIgnoreCase);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Theory]
    [InlineData("UpdQ", "UPDATE Shippers SET Shippers.Phone = '555-0100'", 4, "UPDATE")]
    [InlineData("DelQ", "DELETE FROM Shippers WHERE Shippers.CompanyName = 'nope'", 5, "DELETE")]
    [InlineData("UniQ", "SELECT CompanyName FROM Shippers UNION SELECT CompanyName FROM Customers", 9, "UNION")]
    [InlineData("MakeQ", "SELECT Shippers.* INTO ShipCopy FROM Shippers", 2, "Make-table")]
    public void Action_query_kinds_are_named_in_the_refusal(string name, string sql, int flag, string expected)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "qkind-action-");
        try
        {
            if (!Author(path, (name, sql))) return;

            using var db = JetDatabase.Open(path);
            Assert.Equal((short)flag, OperationFlag(db, name));

            // Not executed — but the reason has to say WHICH kind. "Not supported" on its own gives a caller
            // no way to tell an unimplemented feature from a file LibRed failed to read.
            StoredActionQuery q = db.Catalog.ActionQueries[name];
            Assert.Null(q.Sql);
            Assert.Contains(expected, q.UnsupportedReason!, StringComparison.OrdinalIgnoreCase);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>The <c>MSysObjects.Id</c> of a stored query (Type 5) by name.</summary>
    private static int QueryObjectId(JetDatabase db, string queryName)
    {
        Storage.Table objects = db.OpenTable("MSysObjects");
        int idIdx = objects.Definition.FindColumn("Id")!.Index,
            typeIdx = objects.Definition.FindColumn("Type")!.Index,
            nameIdx = objects.Definition.FindColumn("Name")!.Index;
        int? objectId = null;
        foreach (object?[] row in objects.Rows())
            if (row[typeIdx] is short t && t == 5
                && string.Equals(row[nameIdx] as string, queryName, StringComparison.OrdinalIgnoreCase)
                && row[idIdx] is int id)
                objectId = id;
        Assert.NotNull(objectId);
        return objectId.Value;
    }

    /// <summary>The Flag on the query's <c>Attribute=1</c> row, or null when it has none.</summary>
    private static short? OperationFlag(JetDatabase db, string queryName)
    {
        int objectId = QueryObjectId(db, queryName);

        Storage.Table queries = db.OpenTable("MSysQueries");
        int oidIdx = queries.Definition.FindColumn("ObjectId")!.Index,
            attrIdx = queries.Definition.FindColumn("Attribute")!.Index,
            flagIdx = queries.Definition.FindColumn("Flag")!.Index;
        foreach (object?[] row in queries.Rows())
            if (row[oidIdx] is int oid && oid == objectId && row[attrIdx] is byte a && a == 0x01)
                return row[flagIdx] is short f ? f : null;
        return null;
    }

    /// <summary>Saves each query through DAO's <c>CreateQueryDef</c> — the same path the Access UI takes, and
    /// the only one that reproduces how Access itself lays out the MSysQueries rows. Returns false (test
    /// skipped) where DAO is not registered.</summary>
    private static bool Author(string path, params (string Name, string Sql)[] queries)
    {
        object? engine = null;
        foreach (int n in new[] { 170, 160, 150, 140, 130, 120 })
        {
            Type? type = Type.GetTypeFromProgID($"DAO.DBEngine.{n}");
            if (type is null) continue;
            try { engine = Activator.CreateInstance(type); break; } catch (Exception) { }
        }
        if (engine is null) return false;

        object database = Invoke(engine, "OpenDatabase", path, false, false, "")!;
        foreach ((string name, string sql) in queries)
            Invoke(database, "CreateQueryDef", name, sql);
        // ACE buffers its writes: the file on disk is not complete until the database is closed, so LibRed
        // must not open it before this returns.
        Invoke(database, "Close");
        return true;
    }

    private static object? Invoke(object target, string member, params object?[] args) =>
        target.GetType().InvokeMember(member, BindingFlags.InvokeMethod, null, target, args);
}
