using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class InClauseTests
{
    private static string Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "in-");
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
        finally { TemporaryDatabase.Delete(path); }
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
        finally { TemporaryDatabase.Delete(path); }
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
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void In_subquery_non_correlated()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path);
            var e = new QueryEngine(db);
            int viaIn = e.ExecuteQuery(
                "SELECT ProductID FROM Products WHERE CategoryID IN " +
                "(SELECT CategoryID FROM Categories WHERE CategoryName = 'Beverages')").Rows.Count();
            int direct = e.ExecuteQuery(
                "SELECT p.ProductID FROM Products AS p INNER JOIN Categories AS c ON p.CategoryID = c.CategoryID " +
                "WHERE c.CategoryName = 'Beverages'").Rows.Count();
            Assert.True(direct > 0);
            Assert.Equal(direct, viaIn);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Not_in_subquery()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path);
            var e = new QueryEngine(db);
            int total = e.ExecuteQuery("SELECT ProductID FROM Products").Rows.Count();
            int inCount = e.ExecuteQuery(
                "SELECT ProductID FROM Products WHERE CategoryID IN " +
                "(SELECT CategoryID FROM Categories WHERE CategoryName = 'Beverages')").Rows.Count();
            int notIn = e.ExecuteQuery(
                "SELECT ProductID FROM Products WHERE CategoryID NOT IN " +
                "(SELECT CategoryID FROM Categories WHERE CategoryName = 'Beverages')").Rows.Count();
            Assert.Equal(total - inCount, notIn);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The exact failing shape: a correlated IN subquery nested inside EXISTS (Where_contains_on_navigation).
    [Fact]
    public void Correlated_in_subquery_inside_exists()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path);
            var e = new QueryEngine(db);
            // Outer constrained to a handful of orders — the correlated triple-nesting is O(n^3) naive, so
            // keep the row count small; it still exercises the correlated IN-inside-EXISTS path.
            int viaExists = e.ExecuteQuery(
                "SELECT o.OrderID FROM Orders AS o WHERE o.OrderID < 10260 AND EXISTS (" +
                "SELECT 1 FROM Customers AS c WHERE o.OrderID IN (" +
                "SELECT o0.OrderID FROM Orders AS o0 WHERE c.CustomerID = o0.CustomerID))").Rows.Count();
            // Those orders whose CustomerID matches a real customer — all Northwind orders do.
            int total = e.ExecuteQuery("SELECT OrderID FROM Orders WHERE OrderID < 10260").Rows.Count();
            Assert.True(total > 0);
            Assert.Equal(total, viaExists);
        }
        finally { TemporaryDatabase.Delete(path); }
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
        finally { TemporaryDatabase.Delete(path); }
    }
}
