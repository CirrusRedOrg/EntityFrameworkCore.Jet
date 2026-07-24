using System.Linq;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// Two connections on one physical file must not see each other's UNCOMMITTED writes. A transactional write is
// buffered in the writer's private overlay and only published on commit (read-committed isolation), so a
// concurrent reader sees committed data — never a dirty page. This is exactly what keeps EF's parallel
// shared-store tests, each mutating inside a rolled-back transaction, from leaking into concurrent readers.
// Before the deferred-write overlay, the shared write-through page cache exposed the uncommitted page and a
// rolled-back "Updated" was dirty-read by other tests (see the 2026-07-24 cross-platform CI investigation).
public class TransactionIsolationTests
{
    private static string FreshDb()
    {
        string path = Path.Combine(Path.GetTempPath(), $"txniso-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        return path;
    }

    private static string Contact(QueryEngine e, string id) =>
        (string)e.ExecuteQuery($"SELECT ContactName FROM Customers WHERE CustomerID = '{id}'").Rows.Single()[0]!;

    [Fact]
    public void A_second_connection_does_not_see_an_uncommitted_update_and_rollback_leaks_nothing()
    {
        string path = FreshDb();
        var writerDb = JetDatabase.Open(path, readOnly: false);
        var readerDb = JetDatabase.Open(path, readOnly: false);
        try
        {
            var writer = new QueryEngine(writerDb);
            var reader = new QueryEngine(readerDb);

            string original = Contact(reader, "ALFKI");
            Assert.NotEqual("Updated", original);

            writer.ExecuteNonQuery("BEGIN TRANSACTION");
            writer.ExecuteNonQuery("UPDATE Customers SET ContactName = 'Updated' WHERE CustomerID = 'ALFKI'");

            // The writer sees its own write (read-your-writes)…
            Assert.Equal("Updated", Contact(writer, "ALFKI"));
            // …but the concurrent reader still sees the committed value — no dirty read.
            Assert.Equal(original, Contact(reader, "ALFKI"));

            writer.ExecuteNonQuery("ROLLBACK");

            // After rollback both see the original; nothing the transaction wrote survived anywhere.
            Assert.Equal(original, Contact(writer, "ALFKI"));
            Assert.Equal(original, Contact(reader, "ALFKI"));
        }
        finally { writerDb.Dispose(); readerDb.Dispose(); File.Delete(path); }
    }

    [Fact]
    public void A_committed_update_becomes_visible_to_the_other_connection()
    {
        string path = FreshDb();
        var writerDb = JetDatabase.Open(path, readOnly: false);
        var readerDb = JetDatabase.Open(path, readOnly: false);
        try
        {
            var writer = new QueryEngine(writerDb);
            var reader = new QueryEngine(readerDb);

            writer.ExecuteNonQuery("BEGIN TRANSACTION");
            writer.ExecuteNonQuery("UPDATE Customers SET ContactName = 'Committed' WHERE CustomerID = 'ALFKI'");
            Assert.NotEqual("Committed", Contact(reader, "ALFKI")); // not yet
            writer.ExecuteNonQuery("COMMIT");
            Assert.Equal("Committed", Contact(reader, "ALFKI")); // now visible
        }
        finally { writerDb.Dispose(); readerDb.Dispose(); File.Delete(path); }
    }

    [Fact]
    public void An_uncommitted_insert_is_invisible_until_commit()
    {
        string path = FreshDb();
        var writerDb = JetDatabase.Open(path, readOnly: false);
        var readerDb = JetDatabase.Open(path, readOnly: false);
        try
        {
            var writer = new QueryEngine(writerDb);
            var reader = new QueryEngine(readerDb);

            int Count(QueryEngine e) =>
                Convert.ToInt32(e.ExecuteQuery("SELECT COUNT(*) FROM Customers").Rows.Single()[0]);
            int before = Count(reader);

            writer.ExecuteNonQuery("BEGIN TRANSACTION");
            writer.ExecuteNonQuery(
                "INSERT INTO Customers (CustomerID, CompanyName) VALUES ('ZZZZZ', 'Ghost Co')");
            Assert.Equal(before + 1, Count(writer)); // writer sees its own insert
            Assert.Equal(before, Count(reader));     // reader does not

            writer.ExecuteNonQuery("ROLLBACK");
            Assert.Equal(before, Count(writer));
            Assert.Equal(before, Count(reader));
        }
        finally { writerDb.Dispose(); readerDb.Dispose(); File.Delete(path); }
    }
}
