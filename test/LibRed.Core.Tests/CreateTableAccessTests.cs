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
    public void Access_reads_a_libred_created_relationship()
    {
        // LibRed creates a parent + child with an enforced foreign key (rows in MSysRelationships plus
        // a child-side index on the FK column). Access must open the file without repair, see the
        // relationship, and — if it recognises it as enforced — reject an orphan child row.
        string path = CopyToTemp();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("Parent",
                    [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true)],
                    primaryKey: ["Id"]);
                db.CreateTable("Child",
                    [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("ParentId", JetDataType.Int32, 4, IsFixedLength: true)],
                    primaryKey: ["Id"],
                    relationships: [new RelationshipSpec("FK_Child_Parent", "Parent",
                        [("ParentId", "Id")], IsEnforced: true, CascadeUpdate: false, CascadeDelete: false)]);
                db.OpenTable("Parent").Insert([1]);
                db.OpenTable("Child").Insert([1, 1]);
            }

            using var conn = OpenOleDb(path);

            // Existing data is intact (the file opened without corruption).
            using (var count = conn.CreateCommand())
            {
                count.CommandText = "SELECT COUNT(*) FROM Shippers";
                Assert.Equal(3, Convert.ToInt32(count.ExecuteScalar()));
            }

            // Access enumerates the relationship LibRed wrote.
            var fks = conn.GetOleDbSchemaTable(System.Data.OleDb.OleDbSchemaGuid.Foreign_Keys, null!);
            var names = fks!.Rows.Cast<System.Data.DataRow>()
                .Select(r => r["FK_NAME"]?.ToString())
                .ToList();
            Assert.Contains("FK_Child_Parent", names);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void Access_opens_a_table_with_a_unique_index_and_a_no_index_relationship()
    {
        // A unique (non-primary) index and a NO INDEX foreign key are new on-disk shapes; Access must
        // still open the file without repair and enumerate the relationship.
        string path = CopyToTemp();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("Par",
                    [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true)], primaryKey: ["Id"]);
                db.CreateTable("Chi",
                    [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("Code", JetDataType.Text, 40, IsFixedLength: false),
                     new ColumnSpec("Pid", JetDataType.Int32, 4, IsFixedLength: true)],
                    primaryKey: ["Id"],
                    relationships: [new RelationshipSpec("FK_Chi_Par", "Par", [("Pid", "Id")], true, false, false, NoIndex: true)],
                    uniqueConstraints: [new UniqueIndexSpec("UQ_Code", ["Code"])]);
            }

            using var conn = OpenOleDb(path);
            using (var count = conn.CreateCommand())
            {
                count.CommandText = "SELECT COUNT(*) FROM Shippers";
                Assert.Equal(3, Convert.ToInt32(count.ExecuteScalar())); // opened without corruption
            }
            var fks = conn.GetOleDbSchemaTable(System.Data.OleDb.OleDbSchemaGuid.Foreign_Keys, null!);
            Assert.Contains(fks!.Rows.Cast<System.Data.DataRow>().Select(r => r["FK_NAME"]?.ToString()), n => n == "FK_Chi_Par");
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void Access_reads_a_libred_created_composite_relationship()
    {
        // A composite primary key on the parent and a two-column foreign key on the child: Access must
        // open the file and enumerate the relationship (both column pairs).
        string path = CopyToTemp();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("Par",
                    [new ColumnSpec("A", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("B", JetDataType.Int32, 4, IsFixedLength: true)],
                    primaryKey: ["A", "B"]);
                db.CreateTable("Chi",
                    [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("A", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("B", JetDataType.Int32, 4, IsFixedLength: true)],
                    primaryKey: ["Id"],
                    relationships: [new RelationshipSpec("FK_Chi_Par", "Par", [("A", "A"), ("B", "B")], true, false, false)]);
            }

            using var conn = OpenOleDb(path);
            using (var count = conn.CreateCommand())
            {
                count.CommandText = "SELECT COUNT(*) FROM Shippers";
                Assert.Equal(3, Convert.ToInt32(count.ExecuteScalar()));
            }
            var fks = conn.GetOleDbSchemaTable(System.Data.OleDb.OleDbSchemaGuid.Foreign_Keys, null!);
            var rows = fks!.Rows.Cast<System.Data.DataRow>().Where(r => r["FK_NAME"]?.ToString() == "FK_Chi_Par").ToList();
            Assert.Equal(2, rows.Count); // one row per column pair
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void Access_reads_an_index_libred_added_with_create_index()
    {
        // LibRed creates a table then adds indexes (CREATE INDEX surgery on the TDEF). Access must open
        // the file without repair, enumerate the added indexes, and still read/insert.
        string path = CopyToTemp();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("T",
                    [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("Name", JetDataType.Text, 100, IsFixedLength: false),
                     new ColumnSpec("Age", JetDataType.Int32, 4, IsFixedLength: true)],
                    primaryKey: ["Id"]);
                db.CreateIndex("T", "IX_Name", [("Name", false)]);
                db.CreateIndex("T", "UX_Age", [("Age", false)], isUnique: true);
            }

            using var conn = OpenOleDb(path);
            using (var count = conn.CreateCommand())
            {
                count.CommandText = "SELECT COUNT(*) FROM Shippers";
                Assert.Equal(3, Convert.ToInt32(count.ExecuteScalar())); // opened without corruption
            }
            var indexes = conn.GetOleDbSchemaTable(System.Data.OleDb.OleDbSchemaGuid.Indexes,
                [null!, null!, null!, null!, "T"]);
            var names = indexes!.Rows.Cast<System.Data.DataRow>().Select(r => r["INDEX_NAME"]?.ToString()).ToList();
            Assert.Contains("IX_Name", names);
            Assert.Contains("UX_Age", names);
            using (var insert = conn.CreateCommand())
            {
                insert.CommandText = "INSERT INTO T (Id, Name, Age) VALUES (1, 'x', 5)";
                Assert.Equal(1, insert.ExecuteNonQuery());
            }
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void Access_reads_a_libred_descending_index()
    {
        // A descending index: the slot flag is 0x00 and inserts encode reversed key bytes. Access must
        // open the file, report the column as DESCENDING (COLLATION = 2), and read the rows back.
        string path = CopyToTemp();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("T",
                    [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("Age", JetDataType.Int32, 4, IsFixedLength: true)],
                    primaryKey: ["Id"]);
                db.CreateIndex("T", "IX_AgeDesc", [("Age", true)]); // descending
                var t = db.OpenTable("T");
                t.Insert([1, 30]);
                t.Insert([2, 40]);
                t.Insert([3, 10]);
            }

            using var conn = OpenOleDb(path);
            using (var sum = conn.CreateCommand())
            {
                sum.CommandText = "SELECT COUNT(*), SUM(Age) FROM T";
                using var r = sum.ExecuteReader();
                Assert.True(r.Read());
                Assert.Equal(3, Convert.ToInt32(r.GetValue(0)));   // all rows readable
                Assert.Equal(80, Convert.ToInt32(r.GetValue(1)));
            }
            var indexes = conn.GetOleDbSchemaTable(System.Data.OleDb.OleDbSchemaGuid.Indexes,
                [null!, null!, null!, null!, "T"]);
            var row = indexes!.Rows.Cast<System.Data.DataRow>().First(x => x["INDEX_NAME"]?.ToString() == "IX_AgeDesc");
            Assert.Equal(2, Convert.ToInt32(row["COLLATION"])); // 2 = DESCENDING
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void Access_reads_a_libred_ignore_null_index()
    {
        // WITH IGNORE NULL: flag 0x02 and null-keyed rows excluded from the B-tree. Access must open the
        // file, read every row, report the index's null handling as IGNORE NULL, and return correct
        // results when the index would be used.
        string path = CopyToTemp();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("T",
                    [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("Age", JetDataType.Int32, 4, IsFixedLength: true)],
                    primaryKey: ["Id"]);
                db.CreateIndex("T", "IX_Age", [("Age", false)], ignoreNulls: true);
                var t = db.OpenTable("T");
                t.Insert([1, 30]);
                t.Insert([2, null]); // excluded from the index
                t.Insert([3, 40]);
            }

            using var conn = OpenOleDb(path);
            using (var count = conn.CreateCommand())
            {
                count.CommandText = "SELECT COUNT(*) FROM T";
                Assert.Equal(3, Convert.ToInt32(count.ExecuteScalar())); // all rows present in the table
            }
            using (var q = conn.CreateCommand())
            {
                q.CommandText = "SELECT COUNT(*) FROM T WHERE Age = 30"; // an index-eligible predicate
                Assert.Equal(1, Convert.ToInt32(q.ExecuteScalar()));
            }
            var indexes = conn.GetOleDbSchemaTable(System.Data.OleDb.OleDbSchemaGuid.Indexes,
                [null!, null!, null!, null!, "T"]);
            var row = indexes!.Rows.Cast<System.Data.DataRow>().First(x => x["INDEX_NAME"]?.ToString() == "IX_Age");
            Assert.Equal(2, Convert.ToInt32(row["NULLS"])); // 2 = DBPROPVAL_IN_IGNORENULL
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void Access_honors_a_libred_written_column_default()
    {
        // LibRed writes column DEFAULT values into the table's LvProp property blob, stored on an LVAL
        // page (the form Access's property loader reads). Access must open the file without repair and
        // APPLY the default on its own insert that omits the column.
        string path = CopyToTemp();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                db.CreateTable("T",
                    [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("Age", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("Nm", JetDataType.Text, 40, IsFixedLength: false)],
                    primaryKey: ["Id"],
                    columnDefaults: [("Age", "42"), ("Nm", "'hi'")]);

            using var conn = OpenOleDb(path);
            using (var count = conn.CreateCommand())
            {
                count.CommandText = "SELECT COUNT(*) FROM Shippers";
                Assert.Equal(3, Convert.ToInt32(count.ExecuteScalar())); // opened without corruption
            }
            using (var insert = conn.CreateCommand())
            {
                insert.CommandText = "INSERT INTO T (Id) VALUES (1)"; // omit Age/Nm — Access applies defaults
                Assert.Equal(1, insert.ExecuteNonQuery());
            }
            using (var read = conn.CreateCommand())
            {
                read.CommandText = "SELECT Age, Nm FROM T WHERE Id = 1";
                using var r = read.ExecuteReader();
                Assert.True(r.Read());
                Assert.Equal(42, Convert.ToInt32(r.GetValue(0)));
                Assert.Equal("hi", r.GetValue(1));
            }
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void Access_enforces_a_libred_written_check_constraint()
    {
        // LibRed writes a CHECK into the table's LvProp (CheckConstraints property) on an LVAL page.
        // Access must open the file and ENFORCE it — accept a valid row, reject a violating one.
        string path = CopyToTemp();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                db.CreateTable("T",
                    [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("Age", JetDataType.Int32, 4, IsFixedLength: true)],
                    primaryKey: ["Id"],
                    checkConstraints: [("CK_Age", "[Age] > 0")]);

            using var conn = OpenOleDb(path);
            using (var ok = conn.CreateCommand())
            {
                ok.CommandText = "INSERT INTO T (Id, Age) VALUES (1, 5)"; // satisfies the CHECK
                Assert.Equal(1, ok.ExecuteNonQuery());
            }
            using (var bad = conn.CreateCommand())
            {
                bad.CommandText = "INSERT INTO T (Id, Age) VALUES (2, -1)"; // violates [Age] > 0
                Assert.ThrowsAny<Exception>(() => bad.ExecuteNonQuery());
            }
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void Access_opens_a_libred_table_whose_definition_spans_a_continuation_page()
    {
        // Enough indexes to overflow the single TDEF page: LibRed writes a continuation page. Access must
        // open the file without repair and enumerate every index.
        const int n = 30;
        string path = CopyToTemp();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("Wide",
                    Enumerable.Range(0, n)
                        .Select(i => new ColumnSpec($"C{i:D2}", JetDataType.Int32, 4, IsFixedLength: true))
                        .ToList());
                for (int i = 0; i < n; i++)
                    db.CreateIndex("Wide", $"IX{i:D2}", [($"C{i:D2}", false)]);
            }

            using var conn = OpenOleDb(path);
            using (var count = conn.CreateCommand())
            {
                count.CommandText = "SELECT COUNT(*) FROM Shippers";
                Assert.Equal(3, Convert.ToInt32(count.ExecuteScalar())); // opened without corruption
            }
            var indexes = conn.GetOleDbSchemaTable(System.Data.OleDb.OleDbSchemaGuid.Indexes,
                [null!, null!, null!, null!, "Wide"]);
            Assert.Equal(n, indexes!.Rows.Count);
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
