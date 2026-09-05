using System.Linq;
using LibRed;
using LibRed.Engine;
using System.Data.OleDb;
using Xunit;

namespace LibRed.Engine.Tests;

// Two connections on one physical file must not see each other's UNCOMMITTED writes. A transactional write is
// buffered in the writer's private overlay and only published on commit (read-committed isolation), so a
// concurrent reader sees committed data — never a dirty page. This is exactly what keeps EF's parallel
// shared-store tests, each mutating inside a rolled-back transaction, from leaking into concurrent readers.
// Before the deferred-write overlay, the shared write-through page cache exposed the uncommitted page and a
// rolled-back "Updated" was dirty-read by other tests (see the 2026-07-24 cross-platform CI investigation).
[Collection(AceCollection.Name)]
public class TransactionIsolationTests : TempDatabaseTest
{
    private static string FreshDb()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "txniso-");
        return path;
    }

    private static string Contact(QueryEngine e, string id) =>
        (string)e.ExecuteQuery($"SELECT ContactName FROM Customers WHERE CustomerID = '{id}'").Rows.Single()[0]!;

    [Fact]
    public void A_second_connection_does_not_see_an_uncommitted_update_and_rollback_leaks_nothing()
    {
        string path = FreshDb();
        var writerDb = TemporaryDatabase.OpenTracked(path, readOnly: false);
        var readerDb = TemporaryDatabase.OpenTracked(path, readOnly: false);
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
        finally { writerDb.Dispose(); readerDb.Dispose(); TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void A_committed_update_becomes_visible_to_the_other_connection()
    {
        string path = FreshDb();
        var writerDb = TemporaryDatabase.OpenTracked(path, readOnly: false);
        var readerDb = TemporaryDatabase.OpenTracked(path, readOnly: false);
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
        finally { writerDb.Dispose(); readerDb.Dispose(); TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void An_uncommitted_insert_is_invisible_until_commit()
    {
        string path = FreshDb();
        var writerDb = TemporaryDatabase.OpenTracked(path, readOnly: false);
        var readerDb = TemporaryDatabase.OpenTracked(path, readOnly: false);
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
        finally { writerDb.Dispose(); readerDb.Dispose(); TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Writers_updating_different_pages_can_both_commit()
    {
        string path = FreshDb();
        try
        {
            CreateWideWriterTable(path);
            AssertRowsAreOnDifferentPages(path, 1, 12);

            using var firstDb = JetDatabase.Open(path, readOnly: false);
            using var secondDb = JetDatabase.Open(path, readOnly: false);
            var first = new QueryEngine(firstDb);
            var second = new QueryEngine(secondDb);

            first.ExecuteNonQuery("BEGIN TRANSACTION");
            second.ExecuteNonQuery("BEGIN TRANSACTION");
            first.ExecuteNonQuery("UPDATE WideWriters SET A = 'first-committed' WHERE Id = 1");
            second.ExecuteNonQuery("UPDATE WideWriters SET A = 'second-committed' WHERE Id = 12");

            first.ExecuteNonQuery("COMMIT");
            second.ExecuteNonQuery("COMMIT");

            Assert.Equal("first-committed", WideValue(first, 1, "A"));
            Assert.Equal("second-committed", WideValue(first, 12, "A"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Writers_updating_different_rows_on_the_same_page_get_a_defined_conflict_not_a_lost_update()
    {
        string path = FreshDb();
        try
        {
            using (var setupDb = JetDatabase.Open(path, readOnly: false))
            {
                var setup = new QueryEngine(setupDb);
                setup.ExecuteNonQuery("CREATE TABLE SamePageWriters (Id LONG PRIMARY KEY, A TEXT(100))");
                setup.ExecuteNonQuery("INSERT INTO SamePageWriters (Id, A) VALUES (1, 'one')");
                setup.ExecuteNonQuery("INSERT INTO SamePageWriters (Id, A) VALUES (2, 'two')");
                var table = setupDb.OpenTable("SamePageWriters");
                int id = table.Definition.FindColumn("Id")!.Index;
                var locations = table.Rows().WithIds().ToDictionary(r => Convert.ToInt32(r.Values[id]), r => r.Id.Page);
                Assert.Equal(locations[1], locations[2]);
            }

            using var firstDb = JetDatabase.Open(path, readOnly: false);
            using var secondDb = JetDatabase.Open(path, readOnly: false);
            var first = new QueryEngine(firstDb);
            var second = new QueryEngine(secondDb);
            first.ExecuteNonQuery("BEGIN TRANSACTION");
            second.ExecuteNonQuery("BEGIN TRANSACTION");
            first.ExecuteNonQuery("UPDATE SamePageWriters SET A = 'first' WHERE Id = 1");
            second.ExecuteNonQuery("UPDATE SamePageWriters SET A = 'second' WHERE Id = 2");

            first.ExecuteNonQuery("COMMIT");
            var conflict = Assert.Throws<InvalidOperationException>(() => second.ExecuteNonQuery("COMMIT"));
            Assert.Contains("write conflict", conflict.Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(secondDb.InTransaction);
            second.ExecuteNonQuery("ROLLBACK");

            Assert.Equal("first", Scalar(first, "SELECT A FROM SamePageWriters WHERE Id = 1"));
            Assert.Equal("two", Scalar(first, "SELECT A FROM SamePageWriters WHERE Id = 2"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Writers_moving_entries_in_the_same_index_leaf_get_a_defined_conflict()
    {
        string path = FreshDb();
        try
        {
            CreateWideWriterTable(path);
            AssertRowsAreOnDifferentPages(path, 1, 12);

            using var firstDb = JetDatabase.Open(path, readOnly: false);
            using var secondDb = JetDatabase.Open(path, readOnly: false);
            var first = new QueryEngine(firstDb);
            var second = new QueryEngine(secondDb);
            first.ExecuteNonQuery("BEGIN TRANSACTION");
            second.ExecuteNonQuery("BEGIN TRANSACTION");
            first.ExecuteNonQuery("UPDATE WideWriters SET K = 'first-key' WHERE Id = 1");
            second.ExecuteNonQuery("UPDATE WideWriters SET K = 'second-key' WHERE Id = 12");

            first.ExecuteNonQuery("COMMIT");
            var conflict = Assert.Throws<InvalidOperationException>(() => second.ExecuteNonQuery("COMMIT"));
            Assert.Contains("write conflict", conflict.Message, StringComparison.OrdinalIgnoreCase);
            second.ExecuteNonQuery("ROLLBACK");

            Assert.Equal("first-key", WideValue(first, 1, "K"));
            Assert.Equal("key-12", WideValue(first, 12, "K"));
            Assert.Single(first.ExecuteQuery("SELECT Id FROM WideWriters WHERE K = 'first-key'").Rows);
            Assert.Empty(first.ExecuteQuery("SELECT Id FROM WideWriters WHERE K = 'second-key'").Rows);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Writers_updating_the_same_row_preserve_the_first_commit_and_reject_the_stale_second_one()
    {
        string path = FreshDb();
        var firstDb = TemporaryDatabase.OpenTracked(path, readOnly: false);
        var secondDb = TemporaryDatabase.OpenTracked(path, readOnly: false);
        try
        {
            var first = new QueryEngine(firstDb);
            var second = new QueryEngine(secondDb);
            string original = Contact(first, "ALFKI");

            first.ExecuteNonQuery("BEGIN TRANSACTION");
            second.ExecuteNonQuery("BEGIN TRANSACTION");
            first.ExecuteNonQuery("UPDATE Customers SET ContactName = 'first-wins' WHERE CustomerID = 'ALFKI'");
            second.ExecuteNonQuery("UPDATE Customers SET ContactName = 'stale-second' WHERE CustomerID = 'ALFKI'");

            first.ExecuteNonQuery("COMMIT");
            var conflict = Assert.Throws<InvalidOperationException>(() => second.ExecuteNonQuery("COMMIT"));
            Assert.Contains("write conflict", conflict.Message, StringComparison.OrdinalIgnoreCase);
            second.ExecuteNonQuery("ROLLBACK");

            Assert.Equal("first-wins", Contact(first, "ALFKI"));
            Assert.NotEqual(original, Contact(first, "ALFKI"));
        }
        finally { firstDb.Dispose(); secondDb.Dispose(); TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Rolling_back_inner_disjoint_page_removes_its_baseline_so_outer_commit_does_not_false_conflict()
    {
        string path = FreshDb();
        try
        {
            CreateWideWriterTable(path);
            AssertRowsAreOnDifferentPages(path, 1, 12);
            using var outerDb = JetDatabase.Open(path, readOnly: false);
            using var otherDb = JetDatabase.Open(path, readOnly: false);
            var outer = new QueryEngine(outerDb);
            var other = new QueryEngine(otherDb);

            outer.ExecuteNonQuery("BEGIN TRANSACTION");
            outer.ExecuteNonQuery("UPDATE WideWriters SET A = 'outer-page' WHERE Id = 1");
            outer.ExecuteNonQuery("BEGIN TRANSACTION");
            outer.ExecuteNonQuery("UPDATE WideWriters SET A = 'discarded-inner' WHERE Id = 12");
            outer.ExecuteNonQuery("ROLLBACK");

            other.ExecuteNonQuery("BEGIN TRANSACTION");
            other.ExecuteNonQuery("UPDATE WideWriters SET A = 'other-page' WHERE Id = 12");
            other.ExecuteNonQuery("COMMIT");

            outer.ExecuteNonQuery("COMMIT");
            Assert.Equal("outer-page", WideValue(outer, 1, "A"));
            Assert.Equal("other-page", WideValue(outer, 12, "A"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Rolling_back_inner_change_to_outer_page_preserves_baseline_and_detects_later_conflict()
    {
        string path = FreshDb();
        try
        {
            CreateWideWriterTable(path);
            using var outerDb = JetDatabase.Open(path, readOnly: false);
            using var otherDb = JetDatabase.Open(path, readOnly: false);
            var outer = new QueryEngine(outerDb);
            var other = new QueryEngine(otherDb);

            outer.ExecuteNonQuery("BEGIN TRANSACTION");
            outer.ExecuteNonQuery("UPDATE WideWriters SET A = 'outer-value' WHERE Id = 1");
            outer.ExecuteNonQuery("BEGIN TRANSACTION");
            outer.ExecuteNonQuery("UPDATE WideWriters SET B = 'discarded-inner' WHERE Id = 1");
            outer.ExecuteNonQuery("ROLLBACK");

            other.ExecuteNonQuery("BEGIN TRANSACTION");
            other.ExecuteNonQuery("UPDATE WideWriters SET C = 'other-wins' WHERE Id = 1");
            other.ExecuteNonQuery("COMMIT");

            var conflict = Assert.Throws<InvalidOperationException>(() => outer.ExecuteNonQuery("COMMIT"));
            Assert.Contains("write conflict", conflict.Message, StringComparison.OrdinalIgnoreCase);
            outer.ExecuteNonQuery("ROLLBACK");
            Assert.Equal("other-wins", WideValue(other, 1, "C"));
            Assert.NotEqual("outer-value", WideValue(other, 1, "A"));
            Assert.NotEqual("discarded-inner", WideValue(other, 1, "B"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Concurrent_catalog_allocations_conflict_then_retry_without_losing_either_committed_table()
    {
        string path = FreshDb();
        try
        {
            using (var firstDb = JetDatabase.Open(path, readOnly: false))
            using (var secondDb = JetDatabase.Open(path, readOnly: false))
            {
                var first = new QueryEngine(firstDb);
                var second = new QueryEngine(secondDb);
                first.ExecuteNonQuery("BEGIN TRANSACTION");
                second.ExecuteNonQuery("BEGIN TRANSACTION");
                first.ExecuteNonQuery("CREATE TABLE FirstSchema (Id LONG PRIMARY KEY)");
                second.ExecuteNonQuery("CREATE TABLE StaleSchema (Id LONG PRIMARY KEY)");

                first.ExecuteNonQuery("COMMIT");
                var conflict = Assert.Throws<InvalidOperationException>(() => second.ExecuteNonQuery("COMMIT"));
                Assert.Contains("write conflict", conflict.Message, StringComparison.OrdinalIgnoreCase);
                second.ExecuteNonQuery("ROLLBACK");

                second.ExecuteNonQuery("BEGIN TRANSACTION");
                second.ExecuteNonQuery("CREATE TABLE SecondSchema (Id LONG PRIMARY KEY)");
                second.ExecuteNonQuery("INSERT INTO SecondSchema (Id) VALUES (2)");
                second.ExecuteNonQuery("COMMIT");

                first.ExecuteNonQuery("INSERT INTO FirstSchema (Id) VALUES (1)");
                Assert.Null(firstDb.Catalog.FindTable("StaleSchema"));
                Assert.NotNull(firstDb.Catalog.FindTable("FirstSchema"));
                Assert.NotNull(firstDb.Catalog.FindTable("SecondSchema"));
            }

            using var ace = AceTestDatabase.Open(path);
            using (var firstCount = ace.CreateCommand())
            {
                firstCount.CommandText = "SELECT COUNT(*) FROM FirstSchema";
                Assert.Equal(1, Convert.ToInt32(firstCount.ExecuteScalar()));
            }
            using (var secondCount = ace.CreateCommand())
            {
                secondCount.CommandText = "SELECT COUNT(*) FROM SecondSchema";
                Assert.Equal(1, Convert.ToInt32(secondCount.ExecuteScalar()));
            }
            using var stale = ace.CreateCommand();
            stale.CommandText = "SELECT COUNT(*) FROM StaleSchema";
            Assert.Throws<OleDbException>(() => stale.ExecuteScalar());
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Repeated_same_page_conflicts_can_be_rolled_back_and_retried_without_poisoning_either_connection()
    {
        string path = FreshDb();
        try
        {
            using var firstDb = JetDatabase.Open(path, readOnly: false);
            using var secondDb = JetDatabase.Open(path, readOnly: false);
            var first = new QueryEngine(firstDb);
            var second = new QueryEngine(secondDb);

            for (int cycle = 1; cycle <= 12; cycle++)
            {
                string winner = $"winner-{cycle}";
                string stale = $"stale-{cycle}";
                string retry = $"retry-{cycle}";
                first.ExecuteNonQuery("BEGIN TRANSACTION");
                second.ExecuteNonQuery("BEGIN TRANSACTION");
                first.ExecuteNonQuery($"UPDATE Customers SET ContactName = '{winner}' WHERE CustomerID = 'ALFKI'");
                second.ExecuteNonQuery($"UPDATE Customers SET ContactName = '{stale}' WHERE CustomerID = 'ALFKI'");

                first.ExecuteNonQuery("COMMIT");
                var conflict = Assert.Throws<InvalidOperationException>(() => second.ExecuteNonQuery("COMMIT"));
                Assert.Contains("write conflict", conflict.Message, StringComparison.OrdinalIgnoreCase);
                second.ExecuteNonQuery("ROLLBACK");
                Assert.Equal(winner, Contact(second, "ALFKI"));

                second.ExecuteNonQuery("BEGIN TRANSACTION");
                second.ExecuteNonQuery($"UPDATE Customers SET ContactName = '{retry}' WHERE CustomerID = 'ALFKI'");
                second.ExecuteNonQuery("COMMIT");
                Assert.Equal(retry, Contact(first, "ALFKI"));
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static void CreateWideWriterTable(string path)
    {
        using var db = JetDatabase.Open(path, readOnly: false);
        var e = new QueryEngine(db);
        e.ExecuteNonQuery(
            "CREATE TABLE WideWriters (Id LONG PRIMARY KEY, K TEXT(40), A TEXT(255), B TEXT(255), " +
            "C TEXT(255), D TEXT(255), E TEXT(255), F TEXT(255), G TEXT(255))");
        e.ExecuteNonQuery("CREATE UNIQUE INDEX UX_WideWriters_K ON WideWriters (K)");
        string wide = new('x', 240);
        for (int i = 1; i <= 12; i++)
            e.ExecuteNonQuery(
                $"INSERT INTO WideWriters (Id,K,A,B,C,D,E,F,G) VALUES ({i},'key-{i}','{wide}','{wide}','{wide}','{wide}','{wide}','{wide}','{wide}')");
    }

    private static void AssertRowsAreOnDifferentPages(string path, int first, int second)
    {
        using var db = JetDatabase.Open(path);
        var table = db.OpenTable("WideWriters");
        int id = table.Definition.FindColumn("Id")!.Index;
        var pages = table.Rows().WithIds().ToDictionary(r => Convert.ToInt32(r.Values[id]), r => r.Id.Page);
        Assert.NotEqual(pages[first], pages[second]);
    }

    private static object? WideValue(QueryEngine e, int id, string column) =>
        Scalar(e, $"SELECT {column} FROM WideWriters WHERE Id = {id}");

    private static object? Scalar(QueryEngine e, string sql) => e.ExecuteQuery(sql).Rows.Single()[0];
}
