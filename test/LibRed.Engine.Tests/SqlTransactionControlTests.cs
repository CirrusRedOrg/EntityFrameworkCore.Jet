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

    // A statement that fails inside a transaction runs under a savepoint of its own, which is rolled back — and
    // has to be closed as well. Left open it sits above the savepoint the enclosing BEGIN pushed, so that
    // level's COMMIT is no longer releasing the innermost savepoint and refuses: one failed statement would
    // make every later nested COMMIT throw, on a transaction the failure was supposed to leave intact.
    [Fact]
    public void A_failed_statement_leaves_the_enclosing_levels_committable()
    {
        var e = Fresh();
        e.ExecuteNonQuery("BEGIN TRANSACTION");
        e.ExecuteNonQuery("INSERT INTO t (id) VALUES (1)");
        e.ExecuteNonQuery("BEGIN TRANSACTION");
        e.ExecuteNonQuery("INSERT INTO t (id) VALUES (2)");
        Assert.Throws<ConstraintViolationException>(() => e.ExecuteNonQuery("INSERT INTO t (id) VALUES (2)"));

        e.ExecuteNonQuery("COMMIT");   // inner: releases its savepoint
        e.ExecuteNonQuery("COMMIT");   // outer: commits the transaction
        Assert.Equal([1, 2], Ids(e));
    }

    [Fact]
    public void A_failed_statement_leaves_the_enclosing_levels_rollable()
    {
        var e = Fresh();
        e.ExecuteNonQuery("BEGIN TRANSACTION");
        e.ExecuteNonQuery("INSERT INTO t (id) VALUES (1)");
        e.ExecuteNonQuery("BEGIN TRANSACTION");
        e.ExecuteNonQuery("INSERT INTO t (id) VALUES (2)");
        Assert.Throws<ConstraintViolationException>(() => e.ExecuteNonQuery("INSERT INTO t (id) VALUES (1)"));

        e.ExecuteNonQuery("ROLLBACK");  // inner: undoes id=2 only
        e.ExecuteNonQuery("COMMIT");    // outer: keeps id=1
        Assert.Equal([1], Ids(e));
    }

    [Fact]
    public void Commit_with_no_transaction_open_throws()
    {
        var e = Fresh();
        Assert.Throws<InvalidOperationException>(() => e.ExecuteNonQuery("COMMIT"));
    }

    [Theory]
    [InlineData("DELETE FROM Children WHERE Id = 10", 0)]
    [InlineData("UPDATE Children SET ParentId = 2 WHERE Id = 10", 1)]
    [InlineData("UPDATE Children SET ParentId = NULL WHERE Id = 10", 1)]
    public void Commit_does_not_require_a_parent_after_its_child_reference_is_removed(string change, int children)
    {
        QueryEngine e = Fresh();
        e.ExecuteNonQuery("CREATE TABLE Parents (Id LONG PRIMARY KEY)");
        e.ExecuteNonQuery("CREATE TABLE Children (Id LONG PRIMARY KEY, ParentId LONG REFERENCES Parents (Id))");
        e.ExecuteNonQuery("INSERT INTO Parents VALUES (1), (2)");
        e.ExecuteNonQuery("BEGIN TRANSACTION");
        e.ExecuteNonQuery("INSERT INTO Children VALUES (10, 1)");
        e.ExecuteNonQuery(change);
        e.ExecuteNonQuery("DELETE FROM Parents WHERE Id = 1");
        e.ExecuteNonQuery("COMMIT");
        Assert.Equal(children, Convert.ToInt32(e.ExecuteQuery("SELECT COUNT(*) FROM Children").Rows.Single()[0]));
        Assert.Equal(2, Convert.ToInt32(e.ExecuteQuery("SELECT Id FROM Parents").Rows.Single()[0]));
    }

    [Fact]
    public void Commit_does_not_require_a_parent_after_its_relationship_is_dropped()
    {
        QueryEngine e = Fresh();
        e.ExecuteNonQuery("CREATE TABLE Parents (Id LONG PRIMARY KEY)");
        e.ExecuteNonQuery("CREATE TABLE Children (Id LONG PRIMARY KEY, ParentId LONG, CONSTRAINT FK_Child FOREIGN KEY (ParentId) REFERENCES Parents (Id))");
        e.ExecuteNonQuery("INSERT INTO Parents VALUES (1)");
        e.ExecuteNonQuery("BEGIN TRANSACTION");
        e.ExecuteNonQuery("INSERT INTO Children VALUES (10, 1)");
        e.ExecuteNonQuery("ALTER TABLE Children DROP CONSTRAINT FK_Child");
        e.ExecuteNonQuery("DELETE FROM Parents WHERE Id = 1");
        e.ExecuteNonQuery("COMMIT");
        Assert.Single(e.ExecuteQuery("SELECT * FROM Children").Rows);
        Assert.Empty(e.ExecuteQuery("SELECT * FROM Parents").Rows);
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

    // The mirror of the test above, and the one that was silently wrong: the ADO handle is the OUTER scope and
    // a batch pushes a SQL BEGIN inside it that nothing closes. Committing the ADO transaction has to unwind
    // to the depth it opened at, not one level — releasing only the stray inner savepoint retired the handle
    // while leaving the real transaction open, so Close's RollbackAll then threw the committed work away and
    // reported nothing. Committed data surviving the connection is the whole contract of Commit.
    [Fact]
    public void Ado_commit_survives_a_stray_sql_begin_inside_it()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "txnctl-stray-");
        using (var connection = new LibRedConnection($"Data Source={path}"))
        {
            connection.Open();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "CREATE TABLE t (id LONG PRIMARY KEY)";
                command.ExecuteNonQuery();
            }

            using var transaction = connection.BeginTransaction();
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "BEGIN TRANSACTION; INSERT INTO t (id) VALUES (1)";
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        }

        using var reopened = new LibRedConnection($"Data Source={path}");
        reopened.Open();
        using var count = reopened.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM t";
        Assert.Equal(1, Convert.ToInt32(count.ExecuteScalar()));
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
