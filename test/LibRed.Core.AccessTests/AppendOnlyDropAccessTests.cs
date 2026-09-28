using System.Data.OleDb;
using System.Reflection;
using LibRed;
using LibRed.Catalog;
using LibRed.Formats;
using Xunit;

namespace LibRed.Core.Tests;

// DROP COLUMN of an append-only memo takes the memo's version history with it (system-catalog, "Version history").
// A table keeps the history of all its append-only memos in one hidden complex column, whose per-table template and
// flat table carry a value column per memo: dropping one memo of several takes only its value columns; dropping the
// last takes the whole history, clears the table's AppendOnly, and clears its complex-column flag unless another
// complex column remains — while leaving the dropped tables' MSysACEs rows, as ACE leaves them. Only DAO can make an
// append-only memo, so the file is built through it, then ACE and LibRed drop the same columns on their own copies.
[Collection(AceCollection.Name)]
public class AppendOnlyDropAccessTests
{
    [Fact]
    public void Dropping_append_only_memos_leaves_the_catalog_ACE_leaves()
    {
        object? engine = AceTestDatabase.CreateDaoEngine();
        Assert.SkipWhen(engine is null, "DAO is not available in this process.");
        string acePath = TemporaryDatabase.CreatePath("append-only-ace-");
        string libredPath = TemporaryDatabase.CreatePath("append-only-libred-");
        try
        {
            Build(engine!, acePath);
            File.Copy(acePath, libredPath);

            foreach ((string table, string column) in new[] { ("T", "M1"), ("T", "M2"), ("A", "M") })
            {
                using (OleDbConnection connection = AceTestDatabase.Open(acePath))
                using (OleDbCommand command = connection.CreateCommand())
                {
                    command.CommandText = $"ALTER TABLE {table} DROP COLUMN {column}";
                    command.ExecuteNonQuery();
                }
                OleDbConnection.ReleaseObjectPool();
                using (var db = JetDatabase.Open(libredPath, readOnly: false))
                    Assert.True(db.DropColumn(table, column));

                Assert.Equal(Catalog(acePath), Catalog(libredPath));
            }

            using var result = JetDatabase.Open(libredPath, readOnly: true);
            TableDef t = result.Catalog.FindTable("T")!;
            TableDef a = result.Catalog.FindTable("A")!;
            Assert.Equal(["ID"], t.Columns.Select(c => c.Name));
            Assert.Equal(0u, t.ObjectFlags & (uint)CatalogFormat.ObjectFlagOwnsComplexColumns);
            Assert.Equal(["ID", "Files"], a.Columns.Select(c => c.Name));
            Assert.NotEqual(0u, a.ObjectFlags & (uint)CatalogFormat.ObjectFlagOwnsComplexColumns);   // the attachment remains
            Assert.DoesNotContain(result.Catalog.Tables, x => x.Name.StartsWith("MSysComplexTypeVH_", StringComparison.Ordinal));
        }
        finally
        {
            TemporaryDatabase.Delete(acePath);
            TemporaryDatabase.Delete(libredPath);
        }
    }

    // T: two append-only memos, sharing one history. A: one append-only memo beside an attachment.
    private static void Build(object engine, string path)
    {
        object db = Invoke(engine, "CreateDatabase", path, ";LANGID=0x0409;CP=1252;COUNTRY=0")!;
        try
        {
            foreach ((string table, (string Name, int Type)[] fields) in new[]
                     {
                         ("T", new[] { ("ID", 4), ("M1", 12), ("M2", 12) }),
                         ("A", new[] { ("ID", 4), ("M", 12), ("Files", 101) }),   // 12 dbMemo, 101 dbAttachment
                     })
            {
                object td = Invoke(db, "CreateTableDef", table)!;
                foreach ((string name, int type) in fields)
                    Invoke(Get(td, "Fields"), "Append", Invoke(td, "CreateField", name, type));
                Invoke(Get(db, "TableDefs"), "Append", td);
            }
            foreach ((string table, string field) in new[] { ("T", "M1"), ("T", "M2"), ("A", "M") })
            {
                object f = Item(Get(Item(Get(db, "TableDefs"), table), "Fields"), field);
                f.GetType().InvokeMember("AppendOnly", BindingFlags.SetProperty, null, f, [true]);
            }
        }
        finally { Invoke(db, "Close"); }
        AceTestDatabase.ReleaseAbandonedComObjects();
    }

    // What the drop is expected to change: each table's columns, indexes, complex-column flag and AppendOnly
    // properties; every complex column with its template's and flat table's columns; the hidden tables that exist;
    // and how many MSysACEs rows name each object.
    private static string Catalog(string path)
    {
        var lines = new List<string>();
        using var db = JetDatabase.Open(path, readOnly: true);
        var objects = db.OpenTable("MSysObjects");
        var od = objects.Definition;
        var rows = objects.Rows().ToList();
        foreach (string table in (string[])["T", "A"])
        {
            TableDef t = db.Catalog.FindTable(table)!;
            object?[] row = rows.Single(r => (string)r[od.FindColumn("Name")!.Index]! == table);
            var props = row[od.FindColumn("LvProp")!.Index] is byte[] { Length: > 0 } lv ? PropertyBlob.Read(lv) : [];
            lines.Add($"{table}: [{string.Join(",", t.Columns.Select(c => c.Name))}] indexes [{string.Join(",", t.Indexes.Select(i => i.Name))}] " +
                      $"flags 0x{t.ObjectFlags:X8} AppendOnly [{string.Join(",", props.Where(p => p.Name == "AppendOnly").Select(p => p.Owner))}]");
        }
        foreach (ComplexColumn c in db.Catalog.ComplexColumns)
            lines.Add($"complex {c.OwnerTable.Name}.{c.ColumnName} [{string.Join(",", db.Catalog.FindTable(c.ElementTypeName!)!.Columns.Select(x => x.Name))}] " +
                      $"flat [{string.Join(",", c.FlatTable.Columns.Select(x => x.Name))}]");
        lines.AddRange(db.Catalog.Tables.Select(t => t.Name)
            .Where(n => n.StartsWith("f_", StringComparison.Ordinal) || n.StartsWith("MSysComplexTypeVH_", StringComparison.Ordinal)).Order());
        var aces = db.OpenTable("MSysACEs");
        int objectId = aces.Definition.FindColumn("ObjectId")!.Index;
        lines.Add("MSysACEs rows per object: " + string.Join(",", aces.Rows().GroupBy(r => (int)r[objectId]!).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}")));
        return string.Join("\n", lines);
    }

    private static object Get(object target, string member) =>
        target.GetType().InvokeMember(member, BindingFlags.GetProperty, null, target, null)!;

    private static object Item(object collection, string name) =>
        collection.GetType().InvokeMember("Item", BindingFlags.GetProperty, null, collection, [name])!;

    private static object? Invoke(object target, string member, params object?[] args) =>
        target.GetType().InvokeMember(member, BindingFlags.InvokeMethod, null, target, args);
}
