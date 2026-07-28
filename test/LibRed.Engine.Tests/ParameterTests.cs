using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class ParameterTests
{
    private static readonly string Northwind = Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");

    private static List<object?[]> Query(string sql, IReadOnlyDictionary<string, object?> parameters, out IReadOnlyList<string> columns)
    {
        using var db = JetDatabase.Open(Northwind);
        var rs = new QueryEngine(db).ExecuteQuery(sql, parameters);
        columns = rs.ColumnNames;
        return rs.Rows.ToList();
    }

    [Fact]
    public void Named_string_parameter_in_where()
    {
        var rows = Query(
            "SELECT CustomerID FROM Customers WHERE City = @city",
            new Dictionary<string, object?> { ["@city"] = "London" }, out _);
        Assert.Equal(6, rows.Count);
    }

    [Fact]
    public void Parameter_key_without_leading_at_still_matches()
    {
        var rows = Query(
            "SELECT CustomerID FROM Customers WHERE City = @city",
            new Dictionary<string, object?> { ["city"] = "Berlin" }, out _);
        Assert.Equal("ALFKI", Assert.Single(rows)[0]);
    }

    [Fact]
    public void Numeric_parameter_and_reuse_across_clauses()
    {
        // The same parameter referenced twice, in projection and WHERE.
        var rows = Query(
            "SELECT OrderID, @limit AS Threshold FROM Orders WHERE OrderID < @limit",
            new Dictionary<string, object?> { ["@limit"] = 10250 }, out var columns);
        Assert.Equal(["OrderID", "Threshold"], columns);
        Assert.Equal(2, rows.Count); // 10248, 10249
        Assert.All(rows, r => Assert.Equal(10250, Convert.ToInt32(r[1])));
    }

    [Fact]
    public void Null_valued_parameter_is_supported()
    {
        // A supplied NULL is a real value (not "missing"); City = NULL matches nothing.
        var rows = Query(
            "SELECT CustomerID FROM Customers WHERE City = @city",
            new Dictionary<string, object?> { ["@city"] = null }, out _);
        Assert.Empty(rows);
    }

    [Fact]
    public void Missing_parameter_value_throws()
    {
        using var db = JetDatabase.Open(Northwind);
        var engine = new QueryEngine(db);
        var ex = Assert.Throws<InvalidOperationException>(() =>
            engine.ExecuteQuery("SELECT CustomerID FROM Customers WHERE City = @city",
                new Dictionary<string, object?>()).Rows.ToList());
        Assert.Contains("@city", ex.Message);
    }
}
