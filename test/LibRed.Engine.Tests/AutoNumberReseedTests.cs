using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// ALTER TABLE t ALTER COLUMN c COUNTER(seed, increment) reseeds an AutoNumber column (the KB 884185 fix
// syntax). LibRed accepts it and sets the next id to `seed`, including negative seeds and a negative
// (descending) increment. Also guards the counter against being reset from a stale cached seed on a rebuild.
public class AutoNumberReseedTests
{
    private static QueryEngine Fresh(out JetDatabase db)
    {
        string path = Path.Combine(Path.GetTempPath(), $"reseed-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        db = JetDatabase.Open(path, readOnly: false);
        return new QueryEngine(db);
    }

    private static int NextId(QueryEngine e, string v)
    {
        e.ExecuteNonQuery($"INSERT INTO T (V) VALUES ('{v}')");
        return Convert.ToInt32(e.ExecuteQuery($"SELECT Id FROM T WHERE V = '{v}'").Rows.Single()[0]);
    }

    [Fact]
    public void Reseed_sets_the_next_id_to_the_seed()
    {
        var e = Fresh(out var db);
        try
        {
            e.ExecuteNonQuery("CREATE TABLE T (Id COUNTER CONSTRAINT PK PRIMARY KEY, V TEXT(10))");
            for (int i = 0; i < 6; i++) e.ExecuteNonQuery($"INSERT INTO T (V) VALUES ('r{i}')");   // 1..6
            e.ExecuteNonQuery("ALTER TABLE T ALTER COLUMN Id COUNTER(100, 1)");
            Assert.Equal(100, NextId(e, "a"));
            Assert.Equal(101, NextId(e, "b"));
        }
        finally { db.Dispose(); }
    }

    [Fact]
    public void Reseed_supports_a_negative_seed_and_descending_increment()
    {
        var e = Fresh(out var db);
        try
        {
            e.ExecuteNonQuery("CREATE TABLE T (Id COUNTER CONSTRAINT PK PRIMARY KEY, V TEXT(10))");
            e.ExecuteNonQuery("INSERT INTO T (V) VALUES ('x')");                 // Id 1
            e.ExecuteNonQuery("ALTER TABLE T ALTER COLUMN Id COUNTER(-5, -1)");  // descending from -5
            Assert.Equal(-5, NextId(e, "a"));
            Assert.Equal(-6, NextId(e, "b"));
            Assert.Equal(-7, NextId(e, "c"));
        }
        finally { db.Dispose(); }
    }

    // Matches ACE: reseeding a counter that participates in a relationship is rejected ("Cannot change field
    // 'X'. It is part of one or more relationships.") — ACE-verified it throws the same way.
    [Fact]
    public void Reseed_rejects_a_counter_in_a_relationship()
    {
        var e = Fresh(out var db);
        try
        {
            e.ExecuteNonQuery("CREATE TABLE P (Id COUNTER CONSTRAINT PK PRIMARY KEY, V TEXT(5))");
            e.ExecuteNonQuery("CREATE TABLE C (Cid COUNTER PRIMARY KEY, Pid LONG, CONSTRAINT FK FOREIGN KEY (Pid) REFERENCES P(Id))");
            e.ExecuteNonQuery("INSERT INTO P (V) VALUES ('a')");
            var ex = Assert.ThrowsAny<Exception>(() => e.ExecuteNonQuery("ALTER TABLE P ALTER COLUMN Id COUNTER(100, 1)"));
            Assert.Contains("part of one or more relationships", ex.Message);
        }
        finally { db.Dispose(); }
    }

    // Regression: altering a column rebuilds the table; the AutoNumber counter must keep the on-disk
    // high-water, not reset to its create-time seed. Exposed only when the high rows are deleted (a populated
    // rebuild re-advances the counter as it re-inserts).
    [Fact]
    public void Rebuild_after_deleting_high_rows_keeps_the_counter()
    {
        var e = Fresh(out var db);
        try
        {
            e.ExecuteNonQuery("CREATE TABLE T (Id COUNTER CONSTRAINT PK PRIMARY KEY, N LONG, V TEXT(10))");
            for (int i = 0; i < 6; i++) e.ExecuteNonQuery($"INSERT INTO T (N, V) VALUES ({i}, 'r{i}')");   // 1..6
            e.ExecuteNonQuery("DELETE FROM T");                     // rows gone, disk 0x14 stays 6
            e.ExecuteNonQuery("ALTER TABLE T ALTER COLUMN N SHORT"); // rebuild — no rows to re-advance
            Assert.Equal(7, NextId(e, "g"));                        // continues at 7, not reset to 1
        }
        finally { db.Dispose(); }
    }
}
