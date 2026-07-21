using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// Every DML/DDL statement is atomic on its own, even with no user transaction open: a failure partway
// through must leave the database exactly as it was before the statement ran (no half-written rows, index
// entries, or catalog metadata). The engine wraps each writing statement in an implicit transaction.
public class StatementAtomicityTests
{
    private static QueryEngine Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"atomic-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        return new QueryEngine(JetDatabase.Open(path, readOnly: false));
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
        Assert.ThrowsAny<Exception>(() => e.ExecuteNonQuery("UPDATE t SET amt = amt - 10"));

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
        Assert.ThrowsAny<Exception>(() => e.ExecuteNonQuery("INSERT INTO t (id, name) VALUES (2, 'x')"));

        Assert.Equal(1, e.ExecuteQuery("SELECT COUNT(*) FROM t").Rows.Single()[0]);
        Assert.Single(e.ExecuteQuery("SELECT id FROM t").Rows);
    }

    [Fact]
    public void A_committed_statement_persists_across_a_later_failure()
    {
        var e = Fresh();
        e.ExecuteNonQuery("CREATE TABLE t ( id LONG PRIMARY KEY, amt DOUBLE, CONSTRAINT ck CHECK (amt > 0) )");
        e.ExecuteNonQuery("INSERT INTO t (id, amt) VALUES (1, 50)");   // its own implicit transaction, committed

        Assert.ThrowsAny<Exception>(() => e.ExecuteNonQuery("INSERT INTO t (id, amt) VALUES (2, -5)")); // rolled back

        // The first insert's autocommit is independent of the second's rollback.
        Assert.Equal(1, e.ExecuteQuery("SELECT COUNT(*) FROM t").Rows.Single()[0]);
        Assert.Equal(50.0, Convert.ToDouble(e.ExecuteQuery("SELECT amt FROM t WHERE id = 1").Rows.Single()[0]));
    }
}
