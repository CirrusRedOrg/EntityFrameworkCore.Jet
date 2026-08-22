using System.Linq;
using System.Data.Common;
using LibRed;
using LibRed.Engine;
using LibRed.Data;
using Xunit;

namespace LibRed.Engine.Tests;

// SQL BEGIN/COMMIT/ROLLBACK [TRANSACTION|WORK] drive the same transaction as the ADO API, and nest onto the
// savepoint stack (Jet/DAO semantics: commit/rollback act on the innermost level).
public class SqlTransactionControlTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "txnctl-");
        var e = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
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
        e.ExecuteNonQuery("BEGIN WORK");  // the WORK object keyword
        e.ExecuteNonQuery("INSERT INTO t (id) VALUES (1)");
        e.ExecuteNonQuery("COMMIT WORK");
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

    [Fact]
    public void Sql_outer_transaction_and_ado_inner_transaction_share_one_controller()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "txnctl-ado-");
        using var connection = new LibRedConnection($"Data Source={path}");
        connection.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE t (id LONG PRIMARY KEY); BEGIN TRANSACTION";
            command.ExecuteNonQuery();
        }

        using (var inner = connection.BeginTransaction())
        {
            using var command = connection.CreateCommand();
            command.Transaction = inner;
            command.CommandText = "INSERT INTO t (id) VALUES (1)";
            command.ExecuteNonQuery();
            inner.Commit();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "ROLLBACK; SELECT COUNT(*) FROM t";
            Assert.Equal(0, Convert.ToInt32(command.ExecuteScalar()));
        }
    }

    [Fact]
    public void Sql_commit_completes_the_active_ado_handle()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "txnctl-ado-");
        using var connection = new LibRedConnection($"Data Source={path}");
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "COMMIT TRANSACTION";
        command.ExecuteNonQuery();
        Assert.Throws<InvalidOperationException>(() => transaction.Commit());

        using DbTransaction next = connection.BeginTransaction();
        next.Rollback();
    }
}
