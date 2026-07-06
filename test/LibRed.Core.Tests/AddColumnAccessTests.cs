using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

// ADD COLUMN is a metadata TDEF edit (inverse of DROP COLUMN): the descriptor/name are appended, the new id
// comes from the 0x29 high-water, counts bump, rows are untouched (read NULL). Verify ACE opens a
// LibRed-column-added file, reads existing rows with the new column NULL, and can insert using it.
public class AddColumnAccessTests
{
    private static OleDbConnection Open(string path)
    {
        foreach (string p in new[] { "Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0" })
            try { var c = new OleDbConnection($"Provider={p};Data Source={path};OLE DB Services=-4;"); c.Open(); return c; }
            catch (Exception ex) when (ex is OleDbException or InvalidOperationException) { }
        throw new InvalidOperationException("no ace");
    }
    private static void Ace(string path, params string[] sqls)
    { using var c = Open(path); foreach (var s in sqls) { using var m = c.CreateCommand(); m.CommandText = s; m.ExecuteNonQuery(); } }

    [Fact]
    public void Access_reads_and_extends_a_libred_column_added_table()
    {
        string path = Path.Combine(Path.GetTempPath(), $"addcol-lr-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            // ACE creates the table + rows so the TDEF/rows are authentic.
            Ace(path,
                "CREATE TABLE T (Id LONG PRIMARY KEY, A LONG)",
                "INSERT INTO T (Id, A) VALUES (1, 10)",
                "INSERT INTO T (Id, A) VALUES (2, 20)");

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                Assert.True(db.AddColumn("T", new ColumnSpec("B", JetDataType.Text, 40, IsFixedLength: false)));
                Assert.True(db.AddColumn("T", new ColumnSpec("C", JetDataType.Int32, 4, IsFixedLength: true)));
                Assert.False(db.AddColumn("T", new ColumnSpec("A", JetDataType.Int32, 4, IsFixedLength: true))); // dup → false
                Assert.Equal(["Id", "A", "B", "C"], db.Catalog.FindTable("T")!.Columns.Select(c => c.Name));
            }

            using var conn = Open(path);
            // Existing rows read the new columns as NULL.
            using (var c = conn.CreateCommand())
            {
                c.CommandText = "SELECT A, B, C FROM T WHERE Id = 1";
                using var r = c.ExecuteReader();
                Assert.True(r.Read());
                Assert.Equal(10, r.GetInt32(0));
                Assert.True(r.IsDBNull(1));
                Assert.True(r.IsDBNull(2));
            }
            // ACE can insert a row using the added columns and read it back.
            using (var c = conn.CreateCommand())
            { c.CommandText = "INSERT INTO T (Id, A, B, C) VALUES (3, 30, 'new', 99)"; Assert.Equal(1, c.ExecuteNonQuery()); }
            using (var c = conn.CreateCommand())
            {
                c.CommandText = "SELECT B, C FROM T WHERE Id = 3";
                using var r = c.ExecuteReader();
                Assert.True(r.Read());
                Assert.Equal("new", r.GetString(0));
                Assert.Equal(99, r.GetInt32(1));
            }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void Adding_columns_with_default_and_required_preserves_existing_props_and_access_applies_them()
    {
        string path = Path.Combine(Path.GetTempPath(), $"addcol-lv-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            // ACE creates a table that already has a column DEFAULT (A DEFAULT 5) in its LvProp blob.
            Ace(path, "CREATE TABLE T (Id LONG PRIMARY KEY, A LONG DEFAULT 5)");

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                Assert.True(db.AddColumn("T", new ColumnSpec("Qty", JetDataType.Int32, 4, IsFixedLength: true), defaultValue: "1"));
                Assert.True(db.AddColumn("T", new ColumnSpec("Nm", JetDataType.Text, 40, IsFixedLength: false, IsNullable: false)));
            }

            // LibRed reads the pre-existing default AND the appended ones (the append didn't disturb A's block).
            using (var db2 = JetDatabase.Open(path))
            {
                var t = db2.Catalog.FindTable("T")!;
                Assert.Equal("5", t.Columns.Single(c => c.Name == "A").DefaultValue);
                Assert.Equal("1", t.Columns.Single(c => c.Name == "Qty").DefaultValue);
                Assert.False(t.Columns.Single(c => c.Name == "Nm").IsNullable);
            }

            // ACE opens the file and applies the LibRed-added Qty default on an omit-insert.
            using var conn = Open(path);
            using (var c = conn.CreateCommand())
            { c.CommandText = "INSERT INTO T (Id, A, Nm) VALUES (1, 10, 'x')"; c.ExecuteNonQuery(); }
            using (var c = conn.CreateCommand())
            { c.CommandText = "SELECT Qty FROM T WHERE Id = 1"; Assert.Equal(1, Convert.ToInt32(c.ExecuteScalar())); }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
