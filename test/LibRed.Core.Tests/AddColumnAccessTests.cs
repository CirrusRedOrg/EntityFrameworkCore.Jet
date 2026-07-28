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
    public void Access_reads_a_libred_row_inserted_after_adding_a_fixed_column_to_a_populated_table()
    {
        string path = Path.Combine(Path.GetTempPath(), $"addcol-fx-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            Ace(path,
                "CREATE TABLE T (Id LONG PRIMARY KEY, A LONG)",
                "INSERT INTO T (Id, A) VALUES (1, 10)",
                "INSERT INTO T (Id, A) VALUES (2, 20)");

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                Assert.True(db.AddColumn("T", new ColumnSpec("C", JetDataType.Int32, 4, IsFixedLength: true)));
                db.OpenTable("T").Insert([3, 30, 99]); // LibRed writes a row with the newly-added fixed column
            }

            // ACE reads all three rows: the pre-existing ones with C NULL, the LibRed-written one with C = 99.
            using var conn = Open(path);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, A, C FROM T ORDER BY Id";
            using var r = cmd.ExecuteReader();
            Assert.True(r.Read()); Assert.Equal(1, r.GetInt32(0)); Assert.Equal(10, r.GetInt32(1)); Assert.True(r.IsDBNull(2));
            Assert.True(r.Read()); Assert.Equal(2, r.GetInt32(0)); Assert.Equal(20, r.GetInt32(1)); Assert.True(r.IsDBNull(2));
            Assert.True(r.Read()); Assert.Equal(3, r.GetInt32(0)); Assert.Equal(30, r.GetInt32(1)); Assert.Equal(99, r.GetInt32(2));
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void Add_a_memo_column_falls_back_to_a_dedicated_usage_map_page_when_the_primary_is_full()
    {
        string path = Path.Combine(Path.GetTempPath(), $"addmemo-ded-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            string memo = new string('z', 200);
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                // 30 memo columns fill the table's primary usage-map page (~27 fit).
                var cols = new List<ColumnSpec> { new("Id", JetDataType.Int32, 4, IsFixedLength: true) };
                for (int i = 0; i < 30; i++) cols.Add(new($"M{i}", JetDataType.Memo, 0, IsFixedLength: false));
                db.CreateTable("W", cols, primaryKey: ["Id"]);

                // Adding another memo column must fall back to its own usage-map page (the primary is full).
                Assert.True(db.AddColumn("W", new ColumnSpec("Extra", JetDataType.Memo, 0, IsFixedLength: false)));

                var t = db.Catalog.FindTable("W")!;
                var tdef = db.ReadTableDefinition(t.DefinitionPage);
                int m0Page = tdef.LongValueOwnedMaps[t.Columns.Single(c => c.Name == "M0").ColumnId].Page;
                int extraPage = tdef.LongValueOwnedMaps[t.Columns.Single(c => c.Name == "Extra").ColumnId].Page;
                Assert.NotEqual(m0Page, extraPage); // Extra's maps are on a dedicated page, not the (full) primary

                var row = new object?[t.Columns.Count];
                row[0] = 1; row[t.Columns.Single(c => c.Name == "Extra").Index] = memo;
                db.OpenTable("W").Insert(row);
                Assert.Equal(memo, db.OpenTable("W").Rows().Single()[t.Columns.Single(c => c.Name == "Extra").Index]);
            }

            using var conn = Open(path);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Extra FROM W WHERE Id = 1";
            Assert.Equal(memo, cmd.ExecuteScalar());
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void Add_and_drop_column_work_on_a_multi_page_tdef()
    {
        string path = Path.Combine(Path.GetTempPath(), $"multipage-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                // A wide table whose TDEF spans continuation pages.
                var cols = new List<ColumnSpec> { new("Id", JetDataType.Int32, 4, IsFixedLength: true) };
                for (int i = 0; i < 150; i++) cols.Add(new($"C{i}", JetDataType.Int32, 4, IsFixedLength: true));
                db.CreateTable("Wide", cols, primaryKey: ["Id"]);
                Assert.NotEqual(0, db.ReadTableDefinition(db.Catalog.FindTable("Wide")!.DefinitionPage).NextDefinitionPage); // multi-page

                var row = new object?[cols.Count];
                row[0] = 1; row[1] = 42; // Id + C0
                db.OpenTable("Wide").Insert(row);

                Assert.True(db.AddColumn("Wide", new ColumnSpec("Extra", JetDataType.Int32, 4, IsFixedLength: true)));
                Assert.Contains("Extra", db.Catalog.FindTable("Wide")!.Columns.Select(c => c.Name));

                Assert.True(db.DropColumn("Wide", "C5"));
                Assert.DoesNotContain("C5", db.Catalog.FindTable("Wide")!.Columns.Select(c => c.Name));

                // The surviving row still reads (C0 = 42, Extra = NULL).
                var read = db.OpenTable("Wide").Rows().Single();
                Assert.Equal(1, Convert.ToInt32(read[db.Catalog.FindTable("Wide")!.Columns.Single(c => c.Name == "Id").Index]));
            }

            // ACE opens the multi-page-TDEF-edited file and counts the row.
            using var conn = Open(path);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Wide";
            Assert.Equal(1, Convert.ToInt32(cmd.ExecuteScalar()));
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void Access_reads_a_libred_memo_column_added_to_a_populated_table()
    {
        string path = Path.Combine(Path.GetTempPath(), $"addcol-memo-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            Ace(path,
                "CREATE TABLE T (Id LONG PRIMARY KEY, A LONG)",
                "INSERT INTO T (Id, A) VALUES (1, 10)");

            string memo = new string('y', 300); // > 64 bytes → an LVAL page, exercising the added usage maps
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                Assert.True(db.AddColumn("T", new ColumnSpec("M", JetDataType.Memo, 0, IsFixedLength: false)));
                db.OpenTable("T").Insert([2, 20, memo]);
            }

            // ACE opens the LibRed-edited file and reads the memo (old row NULL, new row the long value).
            using var conn = Open(path);
            using (var c = conn.CreateCommand())
            { c.CommandText = "SELECT M FROM T WHERE Id = 1"; Assert.Equal(DBNull.Value, c.ExecuteScalar()); }
            using (var c = conn.CreateCommand())
            { c.CommandText = "SELECT M FROM T WHERE Id = 2"; Assert.Equal(memo, c.ExecuteScalar()); }
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
