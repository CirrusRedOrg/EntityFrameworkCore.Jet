using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// UPDATE/DELETE reclaim a memo's old chained LVAL pages (freed to the global map and reused), so repeated
// memo churn keeps the file compact — matching ACE, which also reclaims. An unchanged memo is not
// re-materialised at all when another column is updated.
public class LvalReclamationTests
{
    private static string Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "lval-");
        return path;
    }

    private static string Big(char c) => new(c, 20000); // > one LVAL page → chained (dedicated pages)

    [Fact]
    public void Repeated_memo_updates_reclaim_old_chained_pages()
    {
        string path = Fresh();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var e = new QueryEngine(db);
                e.ExecuteNonQuery("CREATE TABLE T (Id counter PRIMARY KEY, M memo)");
                e.ExecuteNonQuery($"INSERT INTO T (M) VALUES ('{Big('a')}')");
            }
            long afterInsert = new FileInfo(path).Length;

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var e = new QueryEngine(db);
                for (int i = 0; i < 30; i++)
                    e.ExecuteNonQuery($"UPDATE T SET M = '{Big((char)('b' + i % 20))}' WHERE Id = 1");
            }
            long afterUpdates = new FileInfo(path).Length;

            // If old pages leaked, 30 × ~20 KB ≈ 600 KB of growth. Reclaimed, it stays tiny.
            Assert.True(afterUpdates - afterInsert < 150_000,
                $"file grew {afterUpdates - afterInsert} bytes over 30 memo updates — old LVAL pages not reclaimed?");

            using (var db = JetDatabase.Open(path))
                Assert.Equal(Big((char)('b' + 29 % 20)), new QueryEngine(db).ExecuteQuery("SELECT M FROM T").Rows.Single()[0]);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Updating_another_column_does_not_re_materialise_the_memo()
    {
        string path = Fresh();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var e = new QueryEngine(db);
                e.ExecuteNonQuery("CREATE TABLE T (Id counter PRIMARY KEY, N long, M memo)");
                e.ExecuteNonQuery($"INSERT INTO T (N, M) VALUES (0, '{Big('a')}')");
            }
            long afterInsert = new FileInfo(path).Length;

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var e = new QueryEngine(db);
                for (int i = 1; i <= 20; i++) e.ExecuteNonQuery($"UPDATE T SET N = {i} WHERE Id = 1"); // memo untouched
            }
            long afterUpdates = new FileInfo(path).Length;

            Assert.True(afterUpdates - afterInsert < 20_000,
                $"file grew {afterUpdates - afterInsert} bytes updating a non-memo column — the memo was re-materialised?");

            using (var db = JetDatabase.Open(path))
            {
                var e = new QueryEngine(db);
                Assert.Equal(20, Convert.ToInt32(e.ExecuteQuery("SELECT N FROM T").Rows.Single()[0]));
                Assert.Equal(Big('a'), e.ExecuteQuery("SELECT M FROM T").Rows.Single()[0]); // memo intact
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Deleting_a_row_reclaims_its_memo_pages()
    {
        string path = Fresh();
        try
        {
            long afterInserts;
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var e = new QueryEngine(db);
                e.ExecuteNonQuery("CREATE TABLE T (Id counter PRIMARY KEY, M memo)");
                e.ExecuteNonQuery($"INSERT INTO T (M) VALUES ('{Big('a')}')"); // will be deleted + re-added repeatedly
            }
            afterInserts = new FileInfo(path).Length;

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var e = new QueryEngine(db);
                for (int i = 0; i < 20; i++)
                {
                    e.ExecuteNonQuery("DELETE FROM T");                                    // frees the memo pages
                    e.ExecuteNonQuery($"INSERT INTO T (M) VALUES ('{Big((char)('b' + i % 20))}')"); // reuses them
                }
            }
            long afterChurn = new FileInfo(path).Length;

            Assert.True(afterChurn - afterInserts < 150_000,
                $"file grew {afterChurn - afterInserts} bytes over 20 delete+insert cycles — deleted memo pages not reclaimed?");
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
