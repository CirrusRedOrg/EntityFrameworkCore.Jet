using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// Jet/ACE multi-table UPDATE/DELETE over a join: SET may touch columns in more than one joined table, and
// DELETE target.* names which table to remove rows from. Semantics verified against ACE (see the probe):
// a "one"-side row is updated once per matched join row, so a self-referencing SET accumulates.
public class MultiTableUpdateDeleteTests
{
    private static string Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"mtud-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        return path;
    }

    private static QueryEngine Seed(JetDatabase db)
    {
        var e = new QueryEngine(db);
        e.ExecuteNonQuery("CREATE TABLE P (Id long PRIMARY KEY, PName text(20), Hits long)");
        e.ExecuteNonQuery("CREATE TABLE C (Id long PRIMARY KEY, ParentId long, CName text(20))");
        e.ExecuteNonQuery("INSERT INTO P (Id, PName, Hits) VALUES (1, 'p1', 0)");
        e.ExecuteNonQuery("INSERT INTO P (Id, PName, Hits) VALUES (2, 'p2', 0)");
        e.ExecuteNonQuery("INSERT INTO C (Id, ParentId, CName) VALUES (10, 1, 'c10')");
        e.ExecuteNonQuery("INSERT INTO C (Id, ParentId, CName) VALUES (11, 1, 'c11')"); // P#1 has TWO children
        e.ExecuteNonQuery("INSERT INTO C (Id, ParentId, CName) VALUES (12, 2, 'c12')");
        return e;
    }

    [Fact]
    public void Multi_table_update_touches_both_tables_and_accumulates_on_the_one_side()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = Seed(db);

            int affected = e.ExecuteNonQuery(
                "UPDATE P INNER JOIN C ON P.Id = C.ParentId " +
                "SET P.PName = 'hit', P.Hits = P.Hits + 1, C.CName = 'child' WHERE P.Id = 1");
            Assert.Equal(2, affected); // one join row per child of P#1

            var p1 = e.ExecuteQuery("SELECT PName, Hits FROM P WHERE Id = 1").Rows.Single();
            Assert.Equal("hit", p1[0]);
            Assert.Equal(2, Convert.ToInt32(p1[1]));          // P#1 incremented once per matched child → 2
            Assert.Equal(0, Convert.ToInt32(e.ExecuteQuery("SELECT Hits FROM P WHERE Id = 2").Rows.Single()[0])); // untouched
            Assert.All(e.ExecuteQuery("SELECT CName FROM C WHERE ParentId = 1").Rows, r => Assert.Equal("child", r[0]));
            Assert.Equal("c12", e.ExecuteQuery("SELECT CName FROM C WHERE Id = 12").Rows.Single()[0]);            // untouched
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void Multi_table_delete_removes_only_the_targeted_table()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = Seed(db);

            // Delete the children of parents named 'p1' — only C rows go, P stays.
            int affected = e.ExecuteNonQuery("DELETE C.* FROM C INNER JOIN P ON C.ParentId = P.Id WHERE P.PName = 'p1'");
            Assert.Equal(2, affected);

            Assert.Equal(new[] { 12 }, e.ExecuteQuery("SELECT Id FROM C").Rows.Select(r => Convert.ToInt32(r[0])).OrderBy(x => x));
            Assert.Equal(2, e.ExecuteQuery("SELECT Id FROM P").Rows.Count()); // both parents remain
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    // `DELETE *` (bare star) is fine for a single table, but a join DELETE without a `table.*` target is
    // ambiguous — Access rejects it ("specify the table"), and so does LibRed.
    [Fact]
    public void Delete_star_needs_a_table_target_on_a_join()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = Seed(db);

            // Single table: bare `*` deletes matching rows.
            Assert.Equal(2, e.ExecuteNonQuery("DELETE * FROM C WHERE ParentId = 1"));
            Assert.Equal(new[] { 12 }, e.ExecuteQuery("SELECT Id FROM C").Rows.Select(r => Convert.ToInt32(r[0])));

            // Join with a bare `*` (or no target) is ambiguous → rejected.
            Assert.Throws<InvalidOperationException>(() =>
                e.ExecuteNonQuery("DELETE * FROM C INNER JOIN P ON C.ParentId = P.Id WHERE P.PName = 'p2'"));
            Assert.Throws<InvalidOperationException>(() =>
                e.ExecuteNonQuery("DELETE FROM C INNER JOIN P ON C.ParentId = P.Id WHERE P.PName = 'p2'"));
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
