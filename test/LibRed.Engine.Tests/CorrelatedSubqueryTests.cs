using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class CorrelatedSubqueryTests
{
    private static readonly string Northwind = Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");

    private static List<object?[]> Query(string sql, out IReadOnlyList<string> columns)
    {
        using var db = JetDatabase.Open(Northwind);
        var rs = new QueryEngine(db).ExecuteQuery(sql);
        columns = rs.ColumnNames;
        return rs.Rows.ToList();
    }

    [Fact]
    public void Like_filter()
    {
        var rows = Query("SELECT CustomerID FROM Customers WHERE CustomerID LIKE 'W%'", out _);
        Assert.Equal(
            ["WANDK", "WARTH", "WELLI", "WHITC", "WILMK", "WOLZA"],
            rows.Select(r => (string)r[0]!).OrderBy(s => s));
    }

    [Fact]
    public void Correlated_scalar_subquery_in_select_list()
    {
        var rows = Query(
            "SELECT c.CustomerID, (SELECT TOP 1 o.OrderDate FROM Orders AS o " +
            "WHERE c.CustomerID = o.CustomerID ORDER BY o.OrderDate DESC) AS Latest " +
            "FROM Customers AS c WHERE c.CustomerID = 'WILMK'",
            out var columns);

        Assert.Equal(["CustomerID", "Latest"], columns);
        var only = Assert.Single(rows);
        Assert.Equal("WILMK", only[0]);
        Assert.Equal(new DateTime(1998, 10, 2), only[1]); // Wilman Kala's most recent order
    }

    [Fact]
    public void Nested_derived_tables_with_correlation_and_like_and_left_join()
    {
        const string sql = """
            SELECT `c0`.`CustomerID`, `c0`.`CompanyName`, `o0`.`OrderID`, `o0`.`OrderDate`
            FROM (
                SELECT TOP 1 `c1`.`CustomerID`, `c1`.`CompanyName`, `c1`.`c`
                FROM (
                    SELECT `c`.`CustomerID`, `c`.`CompanyName`, (
                        SELECT TOP 1 `o`.`OrderDate`
                        FROM `Orders` AS `o`
                        WHERE `c`.`CustomerID` = `o`.`CustomerID`
                        ORDER BY `o`.`OrderDate` DESC) AS `c`
                    FROM `Customers` AS `c`
                    WHERE `c`.`CustomerID` LIKE 'W%'
                ) AS `c1`
                ORDER BY `c1`.`c` DESC
            ) AS `c0`
            LEFT JOIN `Orders` AS `o0` ON `c0`.`CustomerID` = `o0`.`CustomerID`
            ORDER BY `c0`.`c` DESC, `c0`.`CustomerID`
            """;

        var rows = Query(sql, out var columns);

        Assert.Equal(["CustomerID", "CompanyName", "OrderID", "OrderDate"], columns);
        // The W% customer with the most-recent latest order is Wilman Kala; all rows are its orders.
        Assert.All(rows, r => Assert.Equal("WILMK", r[0]));
        Assert.All(rows, r => Assert.Equal("Wilman Kala", r[1]));
        Assert.Equal(7, rows.Count);
        Assert.Contains(rows, r => (DateTime)r[3]! == new DateTime(1998, 10, 2));
    }
}
