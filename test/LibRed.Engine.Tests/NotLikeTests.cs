using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class NotLikeTests
{
    private static string Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"notlike-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        return path;
    }

    [Fact]
    public void Not_like_is_the_complement_of_like()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path);
            var e = new QueryEngine(db);
            int total = e.ExecuteQuery("SELECT CustomerID FROM Customers WHERE ContactName IS NOT NULL").Rows.Count();
            int like = e.ExecuteQuery("SELECT CustomerID FROM Customers WHERE ContactName LIKE 'A%'").Rows.Count();
            int notLike = e.ExecuteQuery("SELECT CustomerID FROM Customers WHERE ContactName NOT LIKE 'A%'").Rows.Count();
            Assert.True(like > 0);
            Assert.Equal(total - like, notLike); // NOT LIKE = the rest (non-null names)
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    // The .All() shape: NOT EXISTS (… WHERE ContactName NOT LIKE 'A%' OR ContactName IS NULL).
    [Fact]
    public void All_top_level_shape_with_not_like()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path);
            var e = new QueryEngine(db);
            object? all = e.ExecuteQuery(
                "SELECT NOT EXISTS (SELECT 1 FROM Customers AS c " +
                "WHERE c.ContactName NOT LIKE 'A%' OR c.ContactName IS NULL) AS r " +
                "FROM (SELECT COUNT(*) FROM Orders)").Rows.First()[0];
            // Not all contact names start with 'A' → All(...) is false.
            Assert.Equal(false, all);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
