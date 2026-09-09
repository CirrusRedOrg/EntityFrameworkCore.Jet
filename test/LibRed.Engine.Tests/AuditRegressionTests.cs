using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// Engine-side regressions from the spec-vs-code audit. Each is a check that one statement path applied and
// its sibling did not — the shape that produced most of the findings.
public class AuditRegressionTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "audit-engine-");
        return new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
    }

    // GenUniqueID() is a LONG-only default; ACE rejects it elsewhere at DDL time. CREATE TABLE and ADD COLUMN
    // validated it, but both ALTER forms wrote the property straight through — so the default was persisted
    // and then evaluated, putting a random Int32 into a Text column on the next omit-insert.
    [Fact]
    public void Alter_column_default_rejects_GenUniqueID_on_a_text_column()
    {
        var engine = Fresh();
        engine.ExecuteNonQuery("CREATE TABLE T (K LONG, V TEXT(10))");

        Assert.Throws<InvalidOperationException>(() =>
            engine.ExecuteNonQuery("ALTER TABLE T ALTER COLUMN V TEXT(10) DEFAULT GenUniqueID()"));
        Assert.Throws<InvalidOperationException>(() =>
            engine.ExecuteNonQuery("ALTER TABLE T ALTER COLUMN V SET DEFAULT GenUniqueID()"));

        // And the legitimate case still works, so the guard is not simply refusing everything.
        engine.ExecuteNonQuery("ALTER TABLE T ALTER COLUMN K SET DEFAULT GenUniqueID()");
    }

    // A cascade rewrites a child row, so it owes that row the same invariants an UPDATE does. It applied
    // none: ON DELETE SET NULL would write NULL into a Required column, and Table.Update carries no
    // enforcement of its own, so nothing underneath caught it either.
    [Fact]
    public void On_delete_set_null_refuses_to_null_a_required_child_column()
    {
        var engine = Fresh();
        engine.ExecuteNonQuery("CREATE TABLE P (A LONG CONSTRAINT PKP PRIMARY KEY)");
        engine.ExecuteNonQuery("CREATE TABLE C (K LONG CONSTRAINT PKC PRIMARY KEY, B LONG NOT NULL)");
        engine.ExecuteNonQuery("ALTER TABLE C ADD CONSTRAINT FK FOREIGN KEY (B) REFERENCES P (A) ON DELETE SET NULL");
        engine.ExecuteNonQuery("INSERT INTO P (A) VALUES (1)");
        engine.ExecuteNonQuery("INSERT INTO C (K, B) VALUES (10, 1)");

        Assert.ThrowsAny<Exception>(() => engine.ExecuteNonQuery("DELETE FROM P WHERE A = 1"));

        // The child row is intact, not half-nulled.
        Assert.Equal(1, Convert.ToInt32(engine.ExecuteQuery("SELECT B FROM C WHERE K = 10").Rows.Single()[0]));
    }

    // ON UPDATE CASCADE can drive two children onto the same unique key — the collision the UPDATE path
    // explicitly guards against, reached by a statement that never names the child table.
    [Fact]
    public void On_update_cascade_refuses_to_create_a_duplicate_child_key()
    {
        var engine = Fresh();
        engine.ExecuteNonQuery("CREATE TABLE P (A LONG CONSTRAINT PKP PRIMARY KEY)");
        engine.ExecuteNonQuery("CREATE TABLE C (K LONG CONSTRAINT PKC PRIMARY KEY, B LONG)");
        engine.ExecuteNonQuery("CREATE UNIQUE INDEX UXB ON C (B)");
        engine.ExecuteNonQuery("ALTER TABLE C ADD CONSTRAINT FK FOREIGN KEY (B) REFERENCES P (A) ON UPDATE CASCADE");
        engine.ExecuteNonQuery("INSERT INTO P (A) VALUES (1)");
        engine.ExecuteNonQuery("INSERT INTO P (A) VALUES (2)");
        engine.ExecuteNonQuery("INSERT INTO C (K, B) VALUES (10, 1)");
        engine.ExecuteNonQuery("INSERT INTO C (K, B) VALUES (20, 2)");

        // Moving parent 1 onto 2 would cascade child 10 onto child 20's unique key.
        Assert.ThrowsAny<Exception>(() => engine.ExecuteNonQuery("UPDATE P SET A = 2 WHERE A = 1"));
    }

    // ViewExpander recursed with no visited-set and the binder never validated a CREATE VIEW body's sources,
    // so a self-referencing view was accepted and then selected from — a StackOverflowException, which .NET
    // cannot catch: it takes the host process down rather than failing the statement.
    [Fact]
    public void A_self_referencing_view_is_reported_rather_than_overflowing_the_stack()
    {
        var engine = Fresh();
        engine.ExecuteNonQuery("CREATE TABLE T (K LONG)");
        engine.ExecuteNonQuery("CREATE VIEW V AS SELECT * FROM T");
        engine.ExecuteNonQuery("DROP VIEW V");
        engine.ExecuteNonQuery("CREATE VIEW V AS SELECT * FROM V");

        var error = Assert.Throws<InvalidOperationException>(() => engine.ExecuteQuery("SELECT * FROM V"));
        Assert.Contains("defined in terms of itself", error.Message);
    }

    // Two views that reference each other — the same cycle one hop longer, which a naive "is this view the
    // one we started from" check would miss.
    [Fact]
    public void A_mutually_recursive_view_pair_is_reported_too()
    {
        var engine = Fresh();
        engine.ExecuteNonQuery("CREATE TABLE T (K LONG)");
        engine.ExecuteNonQuery("CREATE VIEW V1 AS SELECT * FROM T");
        engine.ExecuteNonQuery("CREATE VIEW V2 AS SELECT * FROM V1");
        engine.ExecuteNonQuery("DROP VIEW V1");
        engine.ExecuteNonQuery("CREATE VIEW V1 AS SELECT * FROM V2");

        Assert.Throws<InvalidOperationException>(() => engine.ExecuteQuery("SELECT * FROM V1"));
    }

    // A view referenced twice in one query is NOT a cycle, and must still expand.
    [Fact]
    public void The_same_view_used_twice_in_one_query_still_expands()
    {
        var engine = Fresh();
        engine.ExecuteNonQuery("CREATE TABLE T (K LONG)");
        engine.ExecuteNonQuery("INSERT INTO T (K) VALUES (1)");
        engine.ExecuteNonQuery("CREATE VIEW V AS SELECT * FROM T");

        var result = engine.ExecuteQuery("SELECT A.K FROM V AS A INNER JOIN V AS B ON A.K = B.K");
        Assert.Equal(1, Convert.ToInt32(result.Rows.Single()[0]));
    }

    // MonthName's second argument was accepted by the arity table and then ignored, so MonthName(1, True)
    // returned "January" where ACE returns "Jan" — a silently wrong value, and the arity check that would
    // have caught a stray argument is exactly what let it through.
    [Fact]
    public void MonthName_honours_its_abbreviate_argument()
    {
        var engine = Fresh();
        Assert.Equal("January", engine.ExecuteQuery("SELECT MonthName(1)").Rows.Single()[0]);
        Assert.Equal("Jan", engine.ExecuteQuery("SELECT MonthName(1, True)").Rows.Single()[0]);
        Assert.Equal("January", engine.ExecuteQuery("SELECT MonthName(1, False)").Rows.Single()[0]);
    }
}
