using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// Verifies single-target UPDATE/DELETE with a correlated WHERE EXISTS(SELECT 1 ...) — the shape EF Core's
// bulk ExecuteUpdate/ExecuteDelete emits. (If this passes, EXISTS-based bulk ops already work.)
public class ExistsUpdateDeleteTests
{
    private static string Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"exists-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        return path;
    }

    [Fact]
    public void Exists_correlated_where_on_update_and_delete()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery("CREATE TABLE P (Id long PRIMARY KEY, N long)");
            e.ExecuteNonQuery("CREATE TABLE C (Id long PRIMARY KEY, ParentId long)");
            e.ExecuteNonQuery("INSERT INTO P (Id, N) VALUES (1, 0)");
            e.ExecuteNonQuery("INSERT INTO P (Id, N) VALUES (2, 0)");
            e.ExecuteNonQuery("INSERT INTO C (Id, ParentId) VALUES (10, 1)"); // only P#1 has a child

            // UPDATE only the parents that have a child.
            int updated = e.ExecuteNonQuery("UPDATE P SET N = 99 WHERE EXISTS (SELECT 1 FROM C WHERE C.ParentId = P.Id)");

            // DELETE the parents with no child.
            int deleted = e.ExecuteNonQuery("DELETE FROM P WHERE NOT EXISTS (SELECT 1 FROM C WHERE C.ParentId = P.Id)");

            Assert.Equal(1, updated);
            Assert.Equal(99, System.Convert.ToInt32(e.ExecuteQuery("SELECT N FROM P WHERE Id = 1").Rows.Single()[0]));
            Assert.Equal(1, deleted);
            Assert.Equal(new object?[] { 1 }, e.ExecuteQuery("SELECT Id FROM P").Rows.Select(r => System.Convert.ToInt32(r[0])).Cast<object?>());
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
