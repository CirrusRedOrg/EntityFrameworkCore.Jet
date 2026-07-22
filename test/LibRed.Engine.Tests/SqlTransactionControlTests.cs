using System.Linq;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// SQL BEGIN/COMMIT/ROLLBACK [TRANSACTION|WORK] drive the same transaction as the ADO API, and nest onto the
// savepoint stack (Jet/DAO semantics: commit/rollback act on the innermost level).
public class SqlTransactionControlTests
{
    private static QueryEngine Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"txnctl-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE t ( id LONG PRIMARY KEY )"); // autocommit, before any BEGIN
        return e;
    }

    private static int[] Ids(QueryEngine e) =>
        e.ExecuteQuery("SELECT id FROM t").Rows.Select(r => Convert.ToInt32(r[0])).OrderBy(x => x).ToArray();

    [Fact]
    public void Begin_then_rollback_undoes_the_work()
    {
        var e = Fresh();
        e.ExecuteNonQuery("BEGIN TRANSACTION");
        e.ExecuteNonQuery("INSERT INTO t (id) VALUES (1)");
        e.ExecuteNonQuery("ROLLBACK");
        Assert.Empty(Ids(e));
    }

    [Fact]
    public void Begin_then_commit_keeps_the_work()
    {
        var e = Fresh();
        e.ExecuteNonQuery("BEGIN TRANS"); // alias keyword
        e.ExecuteNonQuery("INSERT INTO t (id) VALUES (1)");
        e.ExecuteNonQuery("COMMIT WORK"); // alias keyword
        Assert.Equal([1], Ids(e));
    }

    [Fact]
    public void A_nested_rollback_undoes_only_the_inner_level()
    {
        var e = Fresh();
        e.ExecuteNonQuery("BEGIN TRANSACTION");
        e.ExecuteNonQuery("INSERT INTO t (id) VALUES (1)");
        e.ExecuteNonQuery("BEGIN TRANSACTION"); // nested (depth 2 → savepoint)
        e.ExecuteNonQuery("INSERT INTO t (id) VALUES (2)");
        e.ExecuteNonQuery("ROLLBACK");           // inner: undo id=2 only
        e.ExecuteNonQuery("COMMIT");             // outer: keep id=1
        Assert.Equal([1], Ids(e));
    }

    [Fact]
    public void A_nested_commit_leaves_its_work_under_the_outer_which_can_still_roll_back()
    {
        var e = Fresh();
        e.ExecuteNonQuery("BEGIN TRANSACTION");
        e.ExecuteNonQuery("INSERT INTO t (id) VALUES (1)");
        e.ExecuteNonQuery("BEGIN TRANSACTION");
        e.ExecuteNonQuery("INSERT INTO t (id) VALUES (2)");
        e.ExecuteNonQuery("COMMIT");    // inner: release savepoint, work stays under the outer
        e.ExecuteNonQuery("ROLLBACK");  // outer: undo everything
        Assert.Empty(Ids(e));
    }

    [Fact]
    public void Commit_with_no_transaction_open_throws()
    {
        var e = Fresh();
        Assert.Throws<InvalidOperationException>(() => e.ExecuteNonQuery("COMMIT"));
    }
}
