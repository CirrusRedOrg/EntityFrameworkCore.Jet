using System.Data.OleDb;
using LibRed;
using LibRed.Crypto;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

[Collection(AceCollection.Name)]
public class ConcurrentAllocationTests
{
    [Fact]
    public void Concurrent_autonumber_and_lval_allocations_conflict_then_retry_without_duplicate_ids_or_pages()
    {
        string path = Fresh("concurrent-allocation-");
        try
        {
            CreateTable(path);
            RunConflictAndRetry(path);
            VerifyWithLibRed(path);
            VerifyWithAce(path);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Encrypted_concurrent_allocations_conflict_then_retry_and_remain_readable_by_ace()
    {
        const string password = "Concurrent-S3cret!";
        string path = Fresh("concurrent-encrypted-");
        try
        {
            CreateTable(path);
            DatabaseEncryption.SetPassword(path, password, AccessEncryption.Agile);

            RunConflictAndRetry(path, password);
            VerifyWithLibRed(path, password);
            VerifyWithAce(path, password);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static void RunConflictAndRetry(string path, string? password = null)
    {
        using var firstDb = JetDatabase.Open(path, readOnly: false, password: password);
        using var secondDb = JetDatabase.Open(path, readOnly: false, password: password);
        var first = new QueryEngine(firstDb);
        var second = new QueryEngine(secondDb);
        int committedPages = firstDb.OpenTable("ConcurrentAlloc").Channel.PageCount;
        string firstMemo = new('A', 24000);
        string staleMemo = new('B', 28000);
        string retryMemo = new('C', 32000);

        first.ExecuteNonQuery("BEGIN TRANSACTION");
        second.ExecuteNonQuery("BEGIN TRANSACTION");
        first.ExecuteNonQuery(
            $"INSERT INTO ConcurrentAlloc (K, M) VALUES ('first', '{firstMemo}')");
        second.ExecuteNonQuery(
            $"INSERT INTO ConcurrentAlloc (K, M) VALUES ('stale', '{staleMemo}')");

        Assert.Equal(1, Identity(first));
        Assert.Equal(1, Identity(second));
        Assert.True(firstDb.OpenTable("ConcurrentAlloc").Channel.PageCount > committedPages);
        Assert.True(secondDb.OpenTable("ConcurrentAlloc").Channel.PageCount > committedPages);

        first.ExecuteNonQuery("COMMIT");
        var conflict = Assert.Throws<InvalidOperationException>(() => second.ExecuteNonQuery("COMMIT"));
        Assert.Contains("write conflict", conflict.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(secondDb.InTransaction);
        second.ExecuteNonQuery("ROLLBACK");

        Assert.Single(second.ExecuteQuery("SELECT Id FROM ConcurrentAlloc WHERE K = 'first'").Rows);
        Assert.Empty(second.ExecuteQuery("SELECT Id FROM ConcurrentAlloc WHERE K = 'stale'").Rows);

        second.ExecuteNonQuery("BEGIN TRANSACTION");
        second.ExecuteNonQuery(
            $"INSERT INTO ConcurrentAlloc (K, M) VALUES ('retry', '{retryMemo}')");
        Assert.Equal(2, Identity(second));
        second.ExecuteNonQuery("COMMIT");

        var rows = first.ExecuteQuery("SELECT Id, K FROM ConcurrentAlloc ORDER BY Id").Rows.ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Equal((1, "first"), (Convert.ToInt32(rows[0][0]), (string)rows[0][1]!));
        Assert.Equal((2, "retry"), (Convert.ToInt32(rows[1][0]), (string)rows[1][1]!));
    }

    private static void CreateTable(string path)
    {
        using var db = JetDatabase.Open(path, readOnly: false);
        var e = new QueryEngine(db);
        e.ExecuteNonQuery("CREATE TABLE ConcurrentAlloc (Id COUNTER PRIMARY KEY, K TEXT(30), M MEMO)");
        e.ExecuteNonQuery("CREATE UNIQUE INDEX UX_ConcurrentAlloc_K ON ConcurrentAlloc (K)");
    }

    private static void VerifyWithLibRed(string path, string? password = null)
    {
        using var db = JetDatabase.Open(path, password: password);
        var e = new QueryEngine(db);
        Assert.Equal(2, Convert.ToInt32(e.ExecuteQuery("SELECT COUNT(*) FROM ConcurrentAlloc").Rows.Single()[0]));
        Assert.Equal(24000, ((string)e.ExecuteQuery("SELECT M FROM ConcurrentAlloc WHERE Id = 1").Rows.Single()[0]!).Length);
        Assert.Equal(32000, ((string)e.ExecuteQuery("SELECT M FROM ConcurrentAlloc WHERE Id = 2").Rows.Single()[0]!).Length);
        Assert.Empty(e.ExecuteQuery("SELECT Id FROM ConcurrentAlloc WHERE K = 'stale'").Rows);
    }

    private static void VerifyWithAce(string path, string? password = null)
    {
        using var connection = AceTestDatabase.Open(path, password);
        AssertScalar(connection, "SELECT COUNT(*) FROM ConcurrentAlloc", 2);
        AssertScalar(connection, "SELECT COUNT(*) FROM ConcurrentAlloc WHERE Id = 1 AND K = 'first'", 1);
        AssertScalar(connection, "SELECT COUNT(*) FROM ConcurrentAlloc WHERE Id = 2 AND K = 'retry'", 1);
        AssertScalar(connection, "SELECT COUNT(*) FROM ConcurrentAlloc WHERE K = 'stale'", 0);
        Assert.Equal(24000, ((string)ExecuteScalar(connection, "SELECT M FROM ConcurrentAlloc WHERE Id = 1")!).Length);
        Assert.Equal(32000, ((string)ExecuteScalar(connection, "SELECT M FROM ConcurrentAlloc WHERE Id = 2")!).Length);
    }

    private static int Identity(QueryEngine engine) =>
        Convert.ToInt32(engine.ExecuteQuery("SELECT @@IDENTITY").Rows.Single()[0]);

    private static string Fresh(string prefix) =>
        TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), prefix);

    private static object? ExecuteScalar(OleDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static void AssertScalar(OleDbConnection connection, string sql, int expected) =>
        Assert.Equal(expected, Convert.ToInt32(ExecuteScalar(connection, sql)));
}
