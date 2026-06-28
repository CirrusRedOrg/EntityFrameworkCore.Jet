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
    public void Correlated_aggregate_subquery_with_round_and_iif()
    {
        // Per order (< 10300), SUM of ROUND(UnitPrice^2, 2) over its details; 0.0 when none.
        const string sql = """
            SELECT `o`.`OrderID`, (
                SELECT IIF(SUM(ROUND(`o0`.`UnitPrice` * `o0`.`UnitPrice`, 2)) IS NULL, 0.0, SUM(ROUND(`o0`.`UnitPrice` * `o0`.`UnitPrice`, 2)))
                FROM `Order Details` AS `o0`
                WHERE `o`.`OrderID` = `o0`.`OrderID`) AS `Sum`
            FROM `Orders` AS `o`
            WHERE `o`.`OrderID` < 10300
            """;

        var rows = Query(sql, out var columns);
        Assert.Equal(["OrderID", "Sum"], columns);
        Assert.Equal(52, rows.Count); // orders 10248..10299

        // Independent cross-check straight from Order Details.
        using var db = JetDatabase.Open(Northwind);
        var details = db.OpenTable("Order Details");
        int oid = details.Definition.Columns.Single(c => c.Name == "OrderID").Index;
        int price = details.Definition.Columns.Single(c => c.Name == "UnitPrice").Index;
        var expected = details.Rows()
            .GroupBy(r => Convert.ToInt32(r[oid]))
            .ToDictionary(
                g => g.Key,
                g => g.Sum(r => Math.Round(Convert.ToDecimal(r[price]) * Convert.ToDecimal(r[price]), 2, MidpointRounding.ToEven)));

        foreach (var row in rows)
        {
            int id = Convert.ToInt32(row[0]);
            decimal got = Convert.ToDecimal(row[1]);
            Assert.Equal(expected.TryGetValue(id, out var sum) ? sum : 0m, got);
        }
        Assert.Equal(1503.08m, Convert.ToDecimal(rows.Single(r => Convert.ToInt32(r[0]) == 10248)[1]));
    }

    [Fact]
    public void Correlated_aggregate_subquery_with_fix_truncation()
    {
        // Like the ROUND test, but FIX truncates each term toward zero before summing.
        const string sql = """
            SELECT `o`.`OrderID`, (
                SELECT IIF(SUM(FIX(`o0`.`UnitPrice` * `o0`.`UnitPrice`)) IS NULL, 0.0, SUM(FIX(`o0`.`UnitPrice` * `o0`.`UnitPrice`)))
                FROM `Order Details` AS `o0`
                WHERE `o`.`OrderID` = `o0`.`OrderID`) AS `Sum`
            FROM `Orders` AS `o`
            WHERE `o`.`OrderID` < 10300
            """;

        var rows = Query(sql, out _);
        Assert.Equal(52, rows.Count);

        using var db = JetDatabase.Open(Northwind);
        var details = db.OpenTable("Order Details");
        int oid = details.Definition.Columns.Single(c => c.Name == "OrderID").Index;
        int price = details.Definition.Columns.Single(c => c.Name == "UnitPrice").Index;
        var expected = details.Rows()
            .GroupBy(r => Convert.ToInt32(r[oid]))
            .ToDictionary(
                g => g.Key,
                g => g.Sum(r => Math.Truncate(Convert.ToDecimal(r[price]) * Convert.ToDecimal(r[price]))));

        foreach (var row in rows)
            Assert.Equal(expected.TryGetValue(Convert.ToInt32(row[0]), out var sum) ? sum : 0m, Convert.ToDecimal(row[1]));
        Assert.Equal(1503m, Convert.ToDecimal(rows.Single(r => Convert.ToInt32(r[0]) == 10248)[1]));
    }

    [Fact]
    public void Exists_with_having_count_threshold()
    {
        // Orders whose customer has more than 30 orders, via a correlated EXISTS over a
        // GROUP BY ... HAVING COUNT(*) > 30 subquery.
        const string sql = """
            SELECT `o`.`OrderID`, `o`.`CustomerID`
            FROM `Orders` AS `o`
            WHERE EXISTS (
                SELECT 1
                FROM `Orders` AS `o0`
                GROUP BY `o0`.`CustomerID`
                HAVING COUNT(*) > 30 AND (`o0`.`CustomerID` = `o`.`CustomerID` OR (`o0`.`CustomerID` IS NULL AND `o`.`CustomerID` IS NULL)))
            """;

        var rows = Query(sql, out var columns);
        Assert.Equal(["OrderID", "CustomerID"], columns);

        // Independent check: which customers have > 30 orders, and how many orders total.
        using var db = JetDatabase.Open(Northwind);
        var orders = db.OpenTable("Orders");
        int cust = orders.Definition.Columns.Single(c => c.Name == "CustomerID").Index;
        var bigCustomers = orders.Rows()
            .GroupBy(r => r[cust]?.ToString())
            .Where(g => g.Count() > 30)
            .Select(g => g.Key)
            .ToHashSet();

        Assert.Equal(["SAVEA"], bigCustomers.OrderBy(x => x)); // only Save-a-lot Markets has > 30
        Assert.All(rows, r => Assert.Contains((string)r[1]!, bigCustomers!));
        Assert.Equal(31, rows.Count); // SAVEA's 31 orders
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
