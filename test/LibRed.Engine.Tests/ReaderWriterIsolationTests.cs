using LibRed;
using LibRed.Crypto;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class ReaderWriterIsolationTests
{
    [Fact]
    public void Reader_crossing_a_multi_page_commit_sees_one_complete_generation()
        => RunCrossingCommit(password: null);

    [Fact]
    public void Encrypted_reader_crossing_a_multi_page_commit_sees_one_complete_generation()
        => RunCrossingCommit("Reader-Commit-S3cret!");

    // The isolation above is bought with a per-file scope, so it matters that the scope is SHARED for reads:
    // if it were exclusive, statement-level isolation would come at the price of serializing every reader on
    // the file. Deterministic, not timing-based — two readers must be inside their scopes at once for the
    // barrier to release, so an exclusive scope deadlocks the barrier and the wait times out.
    [Fact]
    public void Two_readers_on_one_file_are_inside_their_scopes_at_the_same_time()
    {
        string path = CreateDatabase("reader-parallel-");
        try
        {
            using var firstDb = JetDatabase.Open(path, readOnly: true);
            using var secondDb = JetDatabase.Open(path, readOnly: true);
            using var bothInside = new Barrier(2);

            bool Read(JetDatabase db) => db.ReadConsistent(() => bothInside.SignalAndWait(TimeSpan.FromSeconds(10)));

            Task<bool> first = Task.Run(() => Read(firstDb));
            Task<bool> second = Task.Run(() => Read(secondDb));

            Assert.True(Task.WaitAll([first, second], TimeSpan.FromSeconds(20)), "a reader never entered its scope");
            Assert.True(first.Result && second.Result, "the two readers did not overlap — the read scope is exclusive");
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The other half of the contract: a writing statement's scope excludes readers for its whole duration,
    // which is what makes a multi-page write atomic to them. Sound in one direction — a correct implementation
    // always blocks, so only a regression can make this fail.
    [Fact]
    public void A_writer_holding_its_scope_blocks_a_reader_until_it_finishes()
    {
        string path = CreateDatabase("writer-excludes-");
        try
        {
            using var writerDb = JetDatabase.Open(path, readOnly: false);
            using var readerDb = JetDatabase.Open(path, readOnly: true);
            using var writerInside = new ManualResetEventSlim();
            using var releaseWriter = new ManualResetEventSlim();

            Task writer = Task.Run(() => writerDb.WriteExclusive<object?>(() =>
            {
                writerInside.Set();
                releaseWriter.Wait(TimeSpan.FromSeconds(10));
                return null;
            }));

            Assert.True(writerInside.Wait(TimeSpan.FromSeconds(10)), "the writer never entered its scope");
            Task<int> blockedReader = Task.Run(() => readerDb.ReadConsistent(() => 1));
            Assert.False(blockedReader.Wait(TimeSpan.FromMilliseconds(250)), "the reader entered while a writer held the file");

            releaseWriter.Set();
            Assert.True(Task.WaitAll([writer, blockedReader], TimeSpan.FromSeconds(10)), "the reader was not released");
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // A statement misclassified as read-only that then writes cannot upgrade the shared scope. That is a bug
    // in the classification, so it must announce itself rather than surface as a bare LockRecursionException.
    [Fact]
    public void Writing_inside_a_read_scope_is_rejected_with_a_diagnosable_error()
    {
        string path = CreateDatabase("upgrade-guard-");
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var error = Assert.Throws<InvalidOperationException>(
                () => db.ReadConsistent(() => db.WriteExclusive(() => 0)));
            Assert.Contains("exclusive scope", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Preloaded_reader_cache_sees_committed_multi_page_update_but_not_uncommitted_pages()
    {
        string path = CreateDatabase("reader-cache-");
        try
        {
            using var writerDb = JetDatabase.Open(path, readOnly: false);
            using var readerDb = JetDatabase.Open(path, readOnly: false);
            var writer = new QueryEngine(writerDb);
            var reader = new QueryEngine(readerDb);

            AssertGeneration(reader, "old"); // preload every data/index page into the shared read path
            writer.ExecuteNonQuery("BEGIN");
            writer.ExecuteNonQuery("UPDATE IsolationRows SET ValueText = 'new'");
            AssertGeneration(reader, "old");
            writer.ExecuteNonQuery("COMMIT");
            AssertGeneration(reader, "new");
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Write skew across two connections. The commit's conflict check covers the pages a transaction WROTE, and
    // a foreign-key check writes nothing — it reads the parent row. So one connection can insert a child while
    // another deletes the parent, each finding the other's precondition satisfied, and the file ends up with a
    // child referencing nothing. The insert's dependency is re-checked at commit, which is where it is caught.
    [Fact]
    public void A_child_insert_cannot_commit_after_its_parent_is_deleted_by_another_connection()
    {
        string path = CreateDatabase("write-skew-");
        try
        {
            using var childDb = JetDatabase.Open(path, readOnly: false);
            using var parentDb = JetDatabase.Open(path, readOnly: false);
            var child = new QueryEngine(childDb);
            var parent = new QueryEngine(parentDb);

            child.ExecuteNonQuery("CREATE TABLE Parents (Id LONG PRIMARY KEY)");
            child.ExecuteNonQuery(
                "CREATE TABLE Children (Id LONG PRIMARY KEY, ParentId LONG REFERENCES Parents (Id))");
            child.ExecuteNonQuery("INSERT INTO Parents (Id) VALUES (1)");
            child.ExecuteNonQuery("INSERT INTO Parents (Id) VALUES (2)");

            child.ExecuteNonQuery("BEGIN TRANSACTION");
            child.ExecuteNonQuery("INSERT INTO Children (Id, ParentId) VALUES (10, 1)");  // parent 1 is there

            parent.ExecuteNonQuery("DELETE FROM Parents WHERE Id = 1");                   // ... until now

            var conflict = Assert.Throws<InvalidOperationException>(() => child.ExecuteNonQuery("COMMIT"));
            Assert.Contains("no longer exists", conflict.Message, StringComparison.Ordinal);

            // Nothing of the transaction survived, so the file has no child row referencing the deleted parent.
            child.ExecuteNonQuery("ROLLBACK");
            Assert.Empty(parent.ExecuteQuery("SELECT Id FROM Children").Rows);

            // And the check is specific: deleting a DIFFERENT parent does not hold up the same insert.
            child.ExecuteNonQuery("BEGIN TRANSACTION");
            child.ExecuteNonQuery("INSERT INTO Children (Id, ParentId) VALUES (11, 2)");
            parent.ExecuteNonQuery("INSERT INTO Parents (Id) VALUES (3)");
            parent.ExecuteNonQuery("DELETE FROM Parents WHERE Id = 3");
            child.ExecuteNonQuery("COMMIT");
            Assert.Single(parent.ExecuteQuery("SELECT Id FROM Children").Rows);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The dependency is on the parent being needed, not on it having been checked: once the child has moved to
    // another parent, the one it was inserted against can go, and the commit has nothing to object to.
    [Fact]
    public void A_child_moved_to_another_parent_commits_after_the_first_parent_is_deleted_by_another_connection()
    {
        string path = CreateDatabase("write-skew-moved-");
        try
        {
            using var childDb = JetDatabase.Open(path, readOnly: false);
            using var parentDb = JetDatabase.Open(path, readOnly: false);
            var child = new QueryEngine(childDb);
            var parent = new QueryEngine(parentDb);

            child.ExecuteNonQuery("CREATE TABLE Parents (Id LONG PRIMARY KEY)");
            child.ExecuteNonQuery(
                "CREATE TABLE Children (Id LONG PRIMARY KEY, ParentId LONG REFERENCES Parents (Id))");
            child.ExecuteNonQuery("INSERT INTO Parents (Id) VALUES (1)");
            child.ExecuteNonQuery("INSERT INTO Parents (Id) VALUES (2)");

            child.ExecuteNonQuery("BEGIN TRANSACTION");
            child.ExecuteNonQuery("INSERT INTO Children (Id, ParentId) VALUES (10, 1)");
            child.ExecuteNonQuery("UPDATE Children SET ParentId = 2 WHERE Id = 10");

            parent.ExecuteNonQuery("DELETE FROM Parents WHERE Id = 1");

            child.ExecuteNonQuery("COMMIT");
            Assert.Equal(2, Convert.ToInt32(parent.ExecuteQuery("SELECT ParentId FROM Children").Rows.Single()[0]));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Renaming inside the transaction changes every name the dependency was registered under — the child table,
    // its column, the parent table — but not the relationship, which still joins the same tables over the same
    // columns. The parent it relied on is still needed, so its deletion elsewhere still stops the commit.
    [Fact]
    public void A_child_insert_cannot_commit_after_its_parent_is_deleted_even_when_the_tables_were_renamed()
    {
        string path = CreateDatabase("write-skew-renamed-");
        try
        {
            using var childDb = JetDatabase.Open(path, readOnly: false);
            using var parentDb = JetDatabase.Open(path, readOnly: false);
            var child = new QueryEngine(childDb);
            var parent = new QueryEngine(parentDb);

            child.ExecuteNonQuery("CREATE TABLE Parents (Id LONG PRIMARY KEY)");
            child.ExecuteNonQuery(
                "CREATE TABLE Children (Id LONG PRIMARY KEY, ParentId LONG REFERENCES Parents (Id))");
            child.ExecuteNonQuery("INSERT INTO Parents (Id) VALUES (1)");

            child.ExecuteNonQuery("BEGIN TRANSACTION");
            child.ExecuteNonQuery("INSERT INTO Children (Id, ParentId) VALUES (10, 1)");
            child.ExecuteNonQuery("ALTER TABLE Children RENAME TO Kids");
            child.ExecuteNonQuery("ALTER TABLE Kids RENAME COLUMN ParentId TO Pid");
            child.ExecuteNonQuery("ALTER TABLE Parents RENAME TO Folks");

            parent.ExecuteNonQuery("DELETE FROM Parents WHERE Id = 1");

            var conflict = Assert.Throws<InvalidOperationException>(() => child.ExecuteNonQuery("COMMIT"));
            Assert.Contains("no longer exists", conflict.Message, StringComparison.Ordinal);
            child.ExecuteNonQuery("ROLLBACK");
            Assert.Empty(parent.ExecuteQuery("SELECT Id FROM Children").Rows);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Adding a relationship checks the child rows already there against their parents, which is the same write
    // skew as an insert's check: nothing it read is written. ACE closes it by holding both tables exclusively
    // until the transaction ends; LibRed checks the rows again when the transaction commits. The parent goes by a
    // key UPDATE rather than a DELETE: a delete rewrites the parent TDEF's row count, and the ADD wrote that page
    // too (its incoming relationship block), so the page check alone already refuses that commit.
    [Fact]
    public void An_added_relationship_cannot_commit_after_a_parent_it_was_checked_against_is_gone()
    {
        string path = CreateDatabase("write-skew-add-");
        try
        {
            using var childDb = JetDatabase.Open(path, readOnly: false);
            using var parentDb = JetDatabase.Open(path, readOnly: false);
            var child = new QueryEngine(childDb);
            var parent = new QueryEngine(parentDb);

            child.ExecuteNonQuery("CREATE TABLE Parents (Id LONG PRIMARY KEY)");
            child.ExecuteNonQuery("CREATE TABLE Children (Id LONG PRIMARY KEY, ParentId LONG)");
            child.ExecuteNonQuery("INSERT INTO Parents (Id) VALUES (1)");
            child.ExecuteNonQuery("INSERT INTO Children (Id, ParentId) VALUES (10, 1)");

            child.ExecuteNonQuery("BEGIN TRANSACTION");
            child.ExecuteNonQuery(
                "ALTER TABLE Children ADD CONSTRAINT FK_Child FOREIGN KEY (ParentId) REFERENCES Parents (Id)");

            parent.ExecuteNonQuery("UPDATE Parents SET Id = 5 WHERE Id = 1");

            var conflict = Assert.Throws<InvalidOperationException>(() => child.ExecuteNonQuery("COMMIT"));
            Assert.Contains("no longer exists", conflict.Message, StringComparison.Ordinal);
            child.ExecuteNonQuery("ROLLBACK");
            Assert.Empty(childDb.Catalog.ForeignKeysOf("Children"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static void RunCrossingCommit(string? password)
    {
        string path = CreateDatabase("reader-crossing-");
        try
        {
            if (password is not null)
            {
                using var encrypt = JetDatabase.Open(path, readOnly: false, exclusive: true);
                DatabaseEncryption.SetPassword(encrypt, password, AccessEncryption.Agile);
            }

            using var writerDb = JetDatabase.Open(path, readOnly: false, password: password);
            using var readerDb = JetDatabase.Open(path, readOnly: false, password: password);
            var writer = new QueryEngine(writerDb);
            var reader = new QueryEngine(readerDb);

            AssertGeneration(reader, "old");
            writer.ExecuteNonQuery("BEGIN TRANSACTION");
            writer.ExecuteNonQuery("UPDATE IsolationRows SET ValueText = 'new'");

            using var start = new ManualResetEventSlim();
            Task commit = Task.Run(() =>
            {
                start.Set();
                writer.ExecuteNonQuery("COMMIT");
            });
            start.Wait();

            // Every SELECT is a statement-level snapshot. It may land on either side of the commit, but a
            // multi-page publish must never produce a mixture of old and new rows.
            while (!commit.IsCompleted)
                AssertSingleGeneration(reader);
            commit.GetAwaiter().GetResult();
            AssertGeneration(reader, "new");
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static string CreateDatabase(string prefix)
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), prefix);
        using var db = JetDatabase.Open(path, readOnly: false);
        var engine = new QueryEngine(db);
        engine.ExecuteNonQuery("CREATE TABLE IsolationRows (Id LONG PRIMARY KEY, ValueText TEXT(200))");
        for (int i = 1; i <= 240; i++)
            engine.ExecuteNonQuery($"INSERT INTO IsolationRows (Id, ValueText) VALUES ({i}, 'old')");
        return path;
    }

    private static void AssertSingleGeneration(QueryEngine reader)
    {
        var values = reader.ExecuteQuery("SELECT ValueText FROM IsolationRows ORDER BY Id")
            .Rows.Select(row => (string)row[0]!).Distinct().ToArray();
        Assert.Single(values);
        Assert.Contains(values[0], new[] { "old", "new" });
    }

    private static void AssertGeneration(QueryEngine reader, string expected)
    {
        var values = reader.ExecuteQuery("SELECT ValueText FROM IsolationRows ORDER BY Id").Rows;
        Assert.Equal(240, values.Count());
        Assert.All(values, row => Assert.Equal(expected, row[0]));
    }
}
