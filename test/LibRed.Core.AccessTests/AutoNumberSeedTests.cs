using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

// KB 884185 ground truth: after an explicit INSERT of a LOWER value into an AutoNumber column, Access sets the
// 0x14 high-water to the *last inserted value* (not the max), so the next auto id re-derives from it and
// collides with an existing row — the "duplicate values in the index/primary key" error. LibRed diverges (it
// advances 0x14 monotonically and is immune — see AutoNumberSeedImmunityTests in LibRed.Engine.Tests).
public class AutoNumberSeedTests
{
    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    private static int HighWater(JetDatabase db, string table)
    {
        var col = db.Catalog.FindTable(table)!.Columns.First(c => c.IsAutoNumber);
        return col.Seed - col.Increment;   // Seed = lastAuto (0x14) + increment
    }

    [Fact]
    public void Ace_seeds_the_high_water_from_the_last_inserted_value()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "anb-");
        try
        {
            using (var conn = OpenOleDb(path))
            {
                void Exec(string sql) { using var c = conn.CreateCommand(); c.CommandText = sql; c.ExecuteNonQuery(); }
                Exec("CREATE TABLE Table1 (Field1 COUNTER CONSTRAINT PK_T1 PRIMARY KEY, Field2 TEXT(10))");
                for (char ch = 'A'; ch <= 'F'; ch++) Exec($"INSERT INTO Table1 (Field2) VALUES ('{ch}')");   // Field1 → 1..6
                Exec("DELETE FROM Table1 WHERE Field1 = 3");
                Exec("INSERT INTO Table1 (Field1, Field2) VALUES (3, 'C')");   // explicit lower value
            }

            using (var db = JetDatabase.Open(path, readOnly: true))
                Assert.Equal(3, HighWater(db, "Table1"));   // ACE took the last-inserted value, not the max (6)

            // The next auto insert re-derives 3+1=4 → collides with the existing row 4.
            using var conn2 = OpenOleDb(path);
            using var bad = conn2.CreateCommand();
            bad.CommandText = "INSERT INTO Table1 (Field2) VALUES ('G')";
            Assert.ThrowsAny<OleDbException>(() => bad.ExecuteNonQuery());
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Byte-faithful: LibRed's in-place counter reseed (metadata-only 0x14/0x18 edit, no rebuild) is read
    // correctly by ACE — ACE's next auto id is the LibRed-written seed.
    [Fact]
    public void Ace_reads_a_libred_in_place_counter_reseed()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "lrr-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("T",
                    [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true, IsAutoNumber: true),
                     new ColumnSpec("V", JetDataType.Text, 10, IsFixedLength: false)],
                    primaryKey: ["Id"]);
                db.OpenTable("T");
                // Reseed in place to 100 (routes through the metadata-only path, not RewriteColumn).
                db.AlterColumn("T", "Id",
                    new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true, IsAutoNumber: true, Seed: 100, Increment: 1));
            }

            using var conn = OpenOleDb(path);
            using (var c = conn.CreateCommand()) { c.CommandText = "INSERT INTO T (V) VALUES ('a')"; c.ExecuteNonQuery(); }
            using var q = conn.CreateCommand();
            q.CommandText = "SELECT Id FROM T WHERE V = 'a'";
            Assert.Equal(100, Convert.ToInt32(q.ExecuteScalar()));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Symmetric to promotion but NOT a divergence: ACE allows demoting a counter to a plain integer. LibRed
    // does it in place (clears the 0x04 flag); ACE reads the result as a plain int and accepts explicit ids.
    [Fact]
    public void Ace_reads_a_libred_counter_demoted_to_int()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "c2i-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("T",
                    [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true, IsAutoNumber: true),
                     new ColumnSpec("V", JetDataType.Text, 5, IsFixedLength: false)],
                    primaryKey: ["Id"]);
                var t = db.OpenTable("T"); t.Insert([null, "a"]); t.Insert([null, "b"]);   // auto → 1, 2
                db.AlterColumn("T", "Id", new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true, IsAutoNumber: false));
            }

            using var conn = OpenOleDb(path);
            // No longer auto-assigning: ACE requires an explicit Id (a plain int PK), and takes it.
            using (var c = conn.CreateCommand()) { c.CommandText = "INSERT INTO T (Id, V) VALUES (50, 'c')"; c.ExecuteNonQuery(); }
            using var q = conn.CreateCommand();
            q.CommandText = "SELECT COUNT(*) FROM T";
            Assert.Equal(3, Convert.ToInt32(q.ExecuteScalar()));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Divergence (deliberate): ACE refuses to promote an existing plain integer column to AutoNumber via
    // ALTER COLUMN … COUNTER(...) ("Invalid field data type"), like SQL Server. LibRed allows it (like
    // PostgreSQL's ADD GENERATED AS IDENTITY and MySQL's MODIFY … AUTO_INCREMENT), rebuilding the column and
    // preserving the data — and the result is a valid counter ACE reads and uses (next id = seed). Both facts
    // pinned here.
    [Fact]
    public void Ace_refuses_but_libred_allows_promoting_an_int_column_to_a_counter()
    {
        // ACE: reject.
        string acePath = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "i2c-ace-");
        try
        {
            using var conn = OpenOleDb(acePath);
            void Exec(string s) { using var c = conn.CreateCommand(); c.CommandText = s; c.ExecuteNonQuery(); }
            Exec("CREATE TABLE T (Id LONG CONSTRAINT PK PRIMARY KEY, V TEXT(5))");
            Exec("INSERT INTO T (Id, V) VALUES (1, 'a')");
            using var bad = conn.CreateCommand();
            bad.CommandText = "ALTER TABLE T ALTER COLUMN Id COUNTER(100, 1)";
            var ex = Assert.ThrowsAny<OleDbException>(() => bad.ExecuteNonQuery());
            Assert.Contains("Invalid field data type", ex.Message);
        }
        finally { TemporaryDatabase.Delete(acePath); }

        // LibRed: allow, and the converted counter round-trips through ACE (next auto id = seed).
        string libPath = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "i2c-lib-");
        try
        {
            using (var db = JetDatabase.Open(libPath, readOnly: false))
            {
                db.CreateTable("T",
                    [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),   // plain int, not a counter
                     new ColumnSpec("V", JetDataType.Text, 5, IsFixedLength: false)],
                    primaryKey: ["Id"]);
                var t = db.OpenTable("T"); t.Insert([1, "a"]); t.Insert([2, "b"]); t.Insert([3, "c"]);
                db.AlterColumn("T", "Id",
                    new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true, IsAutoNumber: true, Seed: 100, Increment: 1));
            }

            using var conn = OpenOleDb(libPath);
            using (var c = conn.CreateCommand()) { c.CommandText = "INSERT INTO T (V) VALUES ('d')"; c.ExecuteNonQuery(); }
            using var q = conn.CreateCommand();
            q.CommandText = "SELECT Id FROM T WHERE V = 'd'";
            Assert.Equal(100, Convert.ToInt32(q.ExecuteScalar()));   // data preserved (1,2,3), next auto = seed
            using var q2 = conn.CreateCommand();
            q2.CommandText = "SELECT COUNT(*) FROM T";
            Assert.Equal(4, Convert.ToInt32(q2.ExecuteScalar()));
        }
        finally { TemporaryDatabase.Delete(libPath); }
    }

    // Ground truth for the reseed fix (KB 884185 resolution): ALTER COLUMN c COUNTER(seed, 1) sets the next id
    // to `seed`. LibRed matches this (see AutoNumberReseedTests in LibRed.Engine.Tests).
    [Fact]
    public void Ace_reseeds_the_next_id_via_alter_column_counter()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "anr-");
        try
        {
            using var conn = OpenOleDb(path);
            void Exec(string sql) { using var c = conn.CreateCommand(); c.CommandText = sql; c.ExecuteNonQuery(); }
            Exec("CREATE TABLE Table1 (Field1 COUNTER CONSTRAINT PK_T1 PRIMARY KEY, Field2 TEXT(10))");
            for (char ch = 'A'; ch <= 'F'; ch++) Exec($"INSERT INTO Table1 (Field2) VALUES ('{ch}')");   // 1..6
            Exec("ALTER TABLE Table1 ALTER COLUMN Field1 COUNTER(100, 1)");
            Exec("INSERT INTO Table1 (Field2) VALUES ('G')");
            using var q = conn.CreateCommand();
            q.CommandText = "SELECT Field1 FROM Table1 WHERE Field2 = 'G'";
            Assert.Equal(100, Convert.ToInt32(q.ExecuteScalar()));
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
