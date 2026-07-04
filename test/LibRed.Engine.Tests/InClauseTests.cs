using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class InClauseTests
{
    private static string Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"in-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        return path;
    }

    [Fact]
    public void In_with_literals()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path);
            var e = new QueryEngine(db);
            Assert.Equal(3, e.ExecuteQuery(
                "SELECT CustomerID FROM Customers WHERE CustomerID IN ('ALFKI', 'ANATR', 'ANTON')").Rows.Count());
            // Numeric IN.
            Assert.Equal(2, e.ExecuteQuery("SELECT ProductID FROM Products WHERE ProductID IN (1, 2)").Rows.Count());
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    // The failing shape: a mix of parameters and a constant in the IN list.
    [Fact]
    public void In_with_parameters_and_a_constant()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path);
            var e = new QueryEngine(db);
            int n = e.ExecuteQuery(
                "SELECT CustomerID FROM Customers WHERE CustomerID IN (@prm1, @prm2, 'ANTON')",
                new Dictionary<string, object?> { ["prm1"] = "ALFKI", ["prm2"] = "AROUT" }).Rows.Count();
            Assert.Equal(3, n); // ALFKI, AROUT, ANTON
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void Not_in_excludes_the_list()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path);
            var e = new QueryEngine(db);
            int total = e.ExecuteQuery("SELECT CustomerID FROM Customers").Rows.Count();
            int notIn = e.ExecuteQuery(
                "SELECT CustomerID FROM Customers WHERE CustomerID NOT IN ('ALFKI', 'ANATR')").Rows.Count();
            Assert.Equal(total - 2, notIn);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    // Case-insensitive membership (Access text semantics).
    [Fact]
    public void In_is_case_insensitive()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path);
            Assert.Equal(1, new QueryEngine(db).ExecuteQuery(
                "SELECT CustomerID FROM Customers WHERE CustomerID IN ('alfki')").Rows.Count());
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
