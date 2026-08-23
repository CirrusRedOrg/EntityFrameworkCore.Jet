using LibRed;
using LibRed.Engine;
using System.Data.OleDb;
using Xunit;

namespace LibRed.Engine.Tests;

// Every DML/DDL statement is atomic on its own, even with no user transaction open: a failure partway
// through must leave the database exactly as it was before the statement ran (no half-written rows, index
// entries, or catalog metadata). The engine wraps each writing statement in an implicit transaction.
[Collection(AceCollection.Name)]
public class StatementAtomicityTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "atomic-");
        return new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
    }

    [Fact]
    public void Failed_update_rolls_back_rows_it_already_changed()
    {
        var e = Fresh();
        e.ExecuteNonQuery("CREATE TABLE t ( id LONG PRIMARY KEY, amt DOUBLE, CONSTRAINT ck CHECK (amt > 0) )");
        e.ExecuteNonQuery("INSERT INTO t (id, amt) VALUES (1, 100)");
        e.ExecuteNonQuery("INSERT INTO t (id, amt) VALUES (2, 5)");

        // Row id=1 is updated to 90 first (valid, written); id=2 would become -5, which violates the check and
        // throws mid-statement. Without atomicity id=1 would be left at the partially-applied 90.
        var error = Assert.Throws<InvalidOperationException>(() => e.ExecuteNonQuery("UPDATE t SET amt = amt - 10"));
        Assert.Contains("ck", error.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(100.0, Convert.ToDouble(e.ExecuteQuery("SELECT amt FROM t WHERE id = 1").Rows.Single()[0]));
        Assert.Equal(5.0, Convert.ToDouble(e.ExecuteQuery("SELECT amt FROM t WHERE id = 2").Rows.Single()[0]));
    }

    [Fact]
    public void Failed_insert_leaves_no_row_visible_to_a_scan()
    {
        var e = Fresh();
        e.ExecuteNonQuery("CREATE TABLE t ( id LONG PRIMARY KEY, name TEXT(50) )");
        e.ExecuteNonQuery("CREATE UNIQUE INDEX ux_name ON t (name)");
        e.ExecuteNonQuery("INSERT INTO t (id, name) VALUES (1, 'x')");

        // Passes the primary key (id=2 is new) but duplicates the unique index on name — the row heap may be
        // written before the index insert rejects it, so a leaked partial row would show up in a full scan.
        var error = Assert.Throws<ConstraintViolationException>(() =>
            e.ExecuteNonQuery("INSERT INTO t (id, name) VALUES (2, 'x')"));
        Assert.Equal("ux_name", error.ConstraintName, ignoreCase: true);
        Assert.False(error.IsPrimaryKey);

        Assert.Equal(1, e.ExecuteQuery("SELECT COUNT(*) FROM t").Rows.Single()[0]);
        Assert.Single(e.ExecuteQuery("SELECT id FROM t").Rows);
    }

    [Fact]
    public void A_committed_statement_persists_across_a_later_failure()
    {
        var e = Fresh();
        e.ExecuteNonQuery("CREATE TABLE t ( id LONG PRIMARY KEY, amt DOUBLE, CONSTRAINT ck CHECK (amt > 0) )");
        e.ExecuteNonQuery("INSERT INTO t (id, amt) VALUES (1, 50)");   // its own implicit transaction, committed

        var error = Assert.Throws<InvalidOperationException>(() =>
            e.ExecuteNonQuery("INSERT INTO t (id, amt) VALUES (2, -5)")); // rolled back
        Assert.Contains("ck", error.Message, StringComparison.OrdinalIgnoreCase);

        // The first insert's autocommit is independent of the second's rollback.
        Assert.Equal(1, e.ExecuteQuery("SELECT COUNT(*) FROM t").Rows.Single()[0]);
        Assert.Equal(50.0, Convert.ToDouble(e.ExecuteQuery("SELECT amt FROM t WHERE id = 1").Rows.Single()[0]));
    }

    [Fact]
    public void Failed_multirow_update_rolls_back_grown_index_keys_byte_for_byte_and_ace_can_seek()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "atomic-split-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var e = new QueryEngine(db);
                e.ExecuteNonQuery(
                    "CREATE TABLE AtomicSplit (Id LONG PRIMARY KEY, Code TEXT(100), " +
                    "CONSTRAINT CK_Last CHECK (Id < 900 OR Code NOT LIKE 'expanded-*'))");
                e.ExecuteNonQuery("CREATE UNIQUE INDEX UX_AtomicSplit_Code ON AtomicSplit (Code)");
                for (int i = 1; i <= 900; i++)
                    e.ExecuteNonQuery($"INSERT INTO AtomicSplit (Id, Code) VALUES ({i}, 'k{i}')");
            }
            byte[] before = File.ReadAllBytes(path);

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var e = new QueryEngine(db);
                Assert.Throws<InvalidOperationException>(() =>
                    e.ExecuteNonQuery("UPDATE AtomicSplit SET Code = 'expanded-' & Code"));
                Assert.Equal(900, Convert.ToInt32(e.ExecuteQuery("SELECT COUNT(*) FROM AtomicSplit").Rows.Single()[0]));
                Assert.Equal("k1", e.ExecuteQuery("SELECT Code FROM AtomicSplit WHERE Id = 1").Rows.Single()[0]);
                Assert.Equal("k900", e.ExecuteQuery("SELECT Code FROM AtomicSplit WHERE Id = 900").Rows.Single()[0]);
            }

            Assert.Equal(before, File.ReadAllBytes(path));
            using var connection = AceTestDatabase.Open(path);
            AssertScalar(connection, "SELECT COUNT(*) FROM AtomicSplit", 900);
            AssertScalar(connection, "SELECT COUNT(*) FROM AtomicSplit WHERE Code LIKE 'expanded-*'", 0);
            AssertScalar(connection, "SELECT COUNT(*) FROM AtomicSplit WHERE Id = 900 AND Code = 'k900'", 1);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Failed_multirow_update_rolls_back_lval_allocation_byte_for_byte_and_ace_reads_original()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "atomic-lval-");
        string original = new('A', 5000);
        string allocated = new('B', 24000);
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var e = new QueryEngine(db);
                e.ExecuteNonQuery("CREATE TABLE AtomicLval (Id LONG PRIMARY KEY, Code LONG, M MEMO)");
                e.ExecuteNonQuery("CREATE UNIQUE INDEX UX_AtomicLval_Code ON AtomicLval (Code)");
                e.ExecuteNonQuery($"INSERT INTO AtomicLval (Id, Code, M) VALUES (1, 1, '{original}')");
                e.ExecuteNonQuery("INSERT INTO AtomicLval (Id, Code, M) VALUES (2, 2, 'second')");
            }
            byte[] before = File.ReadAllBytes(path);

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var e = new QueryEngine(db);
                Assert.Throws<ConstraintViolationException>(() =>
                    e.ExecuteNonQuery($"UPDATE AtomicLval SET Code = 99, M = '{allocated}'"));
                var row = e.ExecuteQuery("SELECT Id, M FROM AtomicLval");
                Assert.Equal(2, row.Rows.Count());
                Assert.Equal(original, row.Rows.Single(r => Convert.ToInt32(r[0]) == 1)[1]);
                Assert.Equal("second", row.Rows.Single(r => Convert.ToInt32(r[0]) == 2)[1]);
            }

            Assert.Equal(before, File.ReadAllBytes(path));
            using var connection = AceTestDatabase.Open(path);
            AssertScalar(connection, "SELECT COUNT(*) FROM AtomicLval", 2);
            AssertScalar(connection, "SELECT COUNT(*) FROM AtomicLval WHERE Code = 99", 0);
            Assert.Equal(original, ExecuteScalar(connection, "SELECT M FROM AtomicLval WHERE Id = 1"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static object? ExecuteScalar(OleDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static void AssertScalar(OleDbConnection connection, string sql, int expected) =>
        Assert.Equal(expected, Convert.ToInt32(ExecuteScalar(connection, sql)));
}
