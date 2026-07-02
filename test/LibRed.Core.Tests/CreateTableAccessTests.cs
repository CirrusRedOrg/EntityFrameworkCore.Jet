using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

public class CreateTableAccessTests
{
    private static string CopyToTemp()
    {
        string path = Path.Combine(Path.GetTempPath(), $"libred-createaccess-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        return path;
    }

    private static OleDbConnection OpenOleDb(string path)
    {
        foreach (string provider in new[] { "Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0" })
        {
            try
            {
                // "OLE DB Services=-4" disables connection pooling so the file is released on
                // Dispose and the temp copy can be deleted.
                var conn = new OleDbConnection($"Provider={provider};Data Source={path};OLE DB Services=-4;");
                conn.Open();
                return conn;
            }
            catch (Exception ex) when (ex is OleDbException or InvalidOperationException) { }
        }
        throw new InvalidOperationException("No Microsoft.ACE.OLEDB provider (12.0/16.0) is available.");
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { /* lock lingered; temp file, ignore */ }
    }

    [Fact]
    public void Access_lists_a_libred_created_table()
    {
        string path = CopyToTemp();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                db.CreateTable("Widgets", [
                    new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                    new ColumnSpec("Name", JetDataType.Text, 510, IsFixedLength: false),
                ]);

            using var conn = OpenOleDb(path);
            var tables = conn.GetSchema("Tables");
            var names = tables.Rows.Cast<System.Data.DataRow>()
                .Select(r => r["TABLE_NAME"]?.ToString())
                .ToList();

            Assert.Contains("Widgets", names);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void Msysobjects_index_update_does_not_corrupt_the_database()
    {
        // Maintaining MSysObjects' indexes when creating a table must not break the file:
        // Access still resolves and queries the pre-existing tables.
        string path = CopyToTemp();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                db.CreateTable("Widgets", [
                    new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                    new ColumnSpec("Name", JetDataType.Text, 510, IsFixedLength: false),
                ]);

            using var conn = OpenOleDb(path);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Shippers";
            Assert.Equal(3, Convert.ToInt32(cmd.ExecuteScalar())); // existing table still queryable
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void Access_reads_rows_that_libred_inserted_into_a_created_table()
    {
        // End to end through LibRed's own write path: create the table, then insert rows with
        // LibRed (which allocates the table's first data page on demand and records it in the
        // owned/free usage maps). Access must then read those rows back.
        string path = CopyToTemp();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("Widgets", [
                    new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                ], primaryKey: ["Id"]);
                var table = db.OpenTable("Widgets");
                table.Insert([7]);
                table.Insert([11]);
            }

            using var conn = OpenOleDb(path);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT SUM(Id) FROM Widgets";
            Assert.Equal(18, Convert.ToInt32(cmd.ExecuteScalar()));
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void Access_reads_a_libred_created_memo_column()
    {
        // A memo (long-text) column with a short value stored inline: LibRed creates the column and
        // writes the inline long-value descriptor, and Access reads the text back.
        string path = CopyToTemp();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("Memos", [
                    new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                    new ColumnSpec("Note", JetDataType.Memo, 0, IsFixedLength: false),
                ], primaryKey: ["Id"]);
                var table = db.OpenTable("Memos");
                table.Insert([1, "hello memo"]);
                table.Insert([2, null]);
            }

            using var conn = OpenOleDb(path);
            using (var read = conn.CreateCommand())
            {
                read.CommandText = "SELECT Note FROM Memos WHERE Id = 1";
                Assert.Equal("hello memo", read.ExecuteScalar());
            }
            using (var readNull = conn.CreateCommand())
            {
                readNull.CommandText = "SELECT COUNT(*) FROM Memos WHERE Note IS NULL";
                Assert.Equal(1, Convert.ToInt32(readNull.ExecuteScalar()));
            }
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void Access_reads_a_multi_page_libred_table_and_can_insert_after()
    {
        // LibRed inserts enough rows to spill across many data pages (allocate-on-overflow). Access
        // must read the whole table (all pages, via the owned-pages map) and still be able to insert.
        string path = CopyToTemp();
        const int n = 200;
        string pad = new string('x', 180);
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("Big", [
                    new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                    new ColumnSpec("T", JetDataType.Text, 400, IsFixedLength: false),
                ], primaryKey: ["Id"]);
                var table = db.OpenTable("Big");
                for (int i = 1; i <= n; i++) table.Insert([i, $"{i:D4}-{pad}"]);
            }

            using var conn = OpenOleDb(path);
            using (var read = conn.CreateCommand())
            {
                read.CommandText = "SELECT COUNT(*), SUM(Id) FROM Big";
                using var r = read.ExecuteReader();
                Assert.True(r.Read());
                Assert.Equal(n, Convert.ToInt32(r.GetValue(0)));
                Assert.Equal((long)n * (n + 1) / 2, Convert.ToInt64(r.GetValue(1)));
            }
            using (var ins = conn.CreateCommand())
            {
                ins.CommandText = "INSERT INTO Big (Id, T) VALUES (99999, 'ace')";
                Assert.Equal(1, ins.ExecuteNonQuery());
            }
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void Access_continues_autonumber_after_libred_populates_the_table()
    {
        // When LibRed writes rows into an AutoNumber table it must advance the TDEF high-water mark
        // (0x14) so Access issues the *next* id, not one that already exists. Without it Access
        // reuses id 1 and rejects the insert as a duplicate primary key.
        string path = CopyToTemp();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("Auto", [
                    new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true, IsAutoNumber: true),
                    new ColumnSpec("V", JetDataType.Text, 20, IsFixedLength: false),
                ], primaryKey: ["Id"]);
                var table = db.OpenTable("Auto");
                table.Insert([1, "a"]);
                table.Insert([2, "b"]);
                table.Insert([3, "c"]);
            }

            using var conn = OpenOleDb(path);
            using (var insert = conn.CreateCommand())
            {
                insert.CommandText = "INSERT INTO Auto (V) VALUES ('d')"; // Access assigns the Id
                Assert.Equal(1, insert.ExecuteNonQuery());
            }
            using (var read = conn.CreateCommand())
            {
                read.CommandText = "SELECT Id FROM Auto WHERE V = 'd'";
                Assert.Equal(4, Convert.ToInt32(read.ExecuteScalar())); // continued from 3, not reused 1
            }
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void Access_opens_and_round_trips_a_created_table_with_primary_key()
    {
        // The created table's TDEF and per-table pages are now fully ACE-valid: Access resolves it
        // by name, opens it (empty COUNT = 0), accepts an INSERT into it, and reads the value back.
        string path = CopyToTemp();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                db.CreateTable("Widgets", [
                    new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                ], primaryKey: ["Id"]);

            using var conn = OpenOleDb(path);
            using (var count = conn.CreateCommand())
            {
                count.CommandText = "SELECT COUNT(*) FROM Widgets";
                Assert.Equal(0, Convert.ToInt32(count.ExecuteScalar()));
            }
            using (var insert = conn.CreateCommand())
            {
                insert.CommandText = "INSERT INTO Widgets (Id) VALUES (42)";
                Assert.Equal(1, insert.ExecuteNonQuery());
            }
            using (var read = conn.CreateCommand())
            {
                read.CommandText = "SELECT Id FROM Widgets";
                Assert.Equal(42, Convert.ToInt32(read.ExecuteScalar()));
            }
        }
        finally { TryDelete(path); }
    }
}
