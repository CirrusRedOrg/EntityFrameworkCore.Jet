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

    // Promoting a plain integer column to a counter is an in-place metadata edit (0x04 flag + seed), not a
    // rebuild: existing values stay and the next id is the seed.
    [Fact]
    public void Promote_plain_int_to_counter_preserves_data_and_seeds()
    {
        var e = Fresh(out var db);
        try
        {
            e.ExecuteNonQuery("CREATE TABLE T (Id LONG CONSTRAINT PK PRIMARY KEY, V TEXT(10))");   // plain int PK
            e.ExecuteNonQuery("INSERT INTO T (Id, V) VALUES (1, 'a')");
            e.ExecuteNonQuery("INSERT INTO T (Id, V) VALUES (2, 'b')");
            e.ExecuteNonQuery("ALTER TABLE T ALTER COLUMN Id COUNTER(100, 1)");   // promote

            var col = db.Catalog.FindTable("T")!.Columns.First(c => c.Name == "Id");
            Assert.True(col.IsAutoNumber);
            Assert.Equal(2, e.ExecuteQuery("SELECT COUNT(*) FROM T").Rows.Single()[0]);   // data preserved
            Assert.Equal(100, NextId(e, "c"));                                            // next auto = seed
        }
        finally { db.Dispose(); }
    }

    // Demoting a counter back to a plain integer is an in-place metadata edit (clear the flag): existing values
    // stay, and the column stops auto-assigning so explicit ids can be inserted. ACE allows this too.
    [Fact]
    public void Demote_counter_to_int_keeps_data_and_stops_auto_assigning()
    {
        var e = Fresh(out var db);
        try
        {
            e.ExecuteNonQuery("CREATE TABLE T (Id COUNTER CONSTRAINT PK PRIMARY KEY, V TEXT(10))");
            e.ExecuteNonQuery("INSERT INTO T (V) VALUES ('a')");   // Id 1
            e.ExecuteNonQuery("INSERT INTO T (V) VALUES ('b')");   // Id 2
            e.ExecuteNonQuery("ALTER TABLE T ALTER COLUMN Id LONG");   // demote

            var col = db.Catalog.FindTable("T")!.Columns.First(c => c.Name == "Id");
            Assert.False(col.IsAutoNumber);
            Assert.Equal(2, e.ExecuteQuery("SELECT COUNT(*) FROM T").Rows.Single()[0]);   // data preserved
            e.ExecuteNonQuery("INSERT INTO T (Id, V) VALUES (50, 'c')");                  // explicit id now allowed
            Assert.Equal(50, Convert.ToInt32(e.ExecuteQuery("SELECT Id FROM T WHERE V = 'c'").Rows.Single()[0]));
        }
        finally { db.Dispose(); }
    }

    // Promoting an int that carried a GenUniqueID() default to a sequential COUNTER(seed) must clear that
    // default — otherwise it would be read as a "Random" AutoNumber and ignore the seed.
    [Fact]
    public void Promote_clears_a_genuniqueid_default_so_the_counter_is_sequential()
    {
        var e = Fresh(out var db);
        try
        {
            e.ExecuteNonQuery("CREATE TABLE T (Id LONG CONSTRAINT PK PRIMARY KEY DEFAULT GenUniqueID(), V TEXT(10))");
            e.ExecuteNonQuery("ALTER TABLE T ALTER COLUMN Id COUNTER(100, 1)");
            Assert.False(db.Catalog.FindTable("T")!.Columns.First(c => c.Name == "Id").IsRandomAutoNumber);
            Assert.Equal(100, NextId(e, "a"));   // sequential from the seed, not random
            Assert.Equal(101, NextId(e, "b"));
        }
        finally { db.Dispose(); }
    }

    // A literal default is inert on a counter (the insert path skips defaults for AutoNumber columns), so
    // promotion leaves it and the counter still runs sequentially from the seed.
    [Fact]
    public void Promote_leaves_a_literal_default_dormant()
    {
        var e = Fresh(out var db);
        try
        {
            e.ExecuteNonQuery("CREATE TABLE T (Id LONG CONSTRAINT PK PRIMARY KEY DEFAULT 5, V TEXT(10))");
            e.ExecuteNonQuery("ALTER TABLE T ALTER COLUMN Id COUNTER(100, 1)");
            Assert.Equal(100, NextId(e, "a"));   // sequential from the seed, the '5' default ignored
        }
        finally { db.Dispose(); }
    }

    // Demotion preserves the default (matches ACE — ALTER type keeps the default): a random AutoNumber demoted
    // to a plain int keeps generating random values via its surviving GenUniqueID() default.
    [Fact]
    public void Demote_preserves_a_genuniqueid_default_so_it_stays_random()
    {
        var e = Fresh(out var db);
        try
        {
            e.ExecuteNonQuery("CREATE TABLE T (Id COUNTER CONSTRAINT PK PRIMARY KEY DEFAULT GenUniqueID(), V TEXT(10))");
            e.ExecuteNonQuery("INSERT INTO T (V) VALUES ('seed')");
            e.ExecuteNonQuery("ALTER TABLE T ALTER COLUMN Id LONG");
            e.ExecuteNonQuery("INSERT INTO T (V) VALUES ('a')");   // no Id → default GenUniqueID() still fires
            var id = Convert.ToInt64(e.ExecuteQuery("SELECT Id FROM T WHERE V = 'a'").Rows.Single()[0]);
            Assert.NotEqual(0, id);   // a random non-zero id was generated by the surviving default
        }
        finally { db.Dispose(); }
    }

    // Jet allows only one AutoNumber per table — promoting a second column is rejected.
    [Fact]
    public void Promote_rejects_a_second_counter()
    {
        var e = Fresh(out var db);
        try
        {
            e.ExecuteNonQuery("CREATE TABLE T (Id COUNTER CONSTRAINT PK PRIMARY KEY, N LONG)");
            var ex = Assert.ThrowsAny<Exception>(() => e.ExecuteNonQuery("ALTER TABLE T ALTER COLUMN N COUNTER(1, 1)"));
            Assert.Contains("already has one", ex.Message);
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
