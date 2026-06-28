using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class AggregateTests
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
    public void Count_star_with_no_group_by_is_a_single_group()
    {
        var rows = Query("SELECT COUNT(*) AS n FROM Orders", out var columns);
        Assert.Equal(["n"], columns);
        var only = Assert.Single(rows);
        Assert.Equal(830L, only[0]);
    }

    [Fact]
    public void Group_by_with_count_partitions_and_totals()
    {
        var rows = Query("SELECT EmployeeID, COUNT(*) AS n FROM Orders GROUP BY EmployeeID", out _);
        Assert.Equal(9, rows.Count); // 9 employees
        Assert.Equal(830L, rows.Sum(r => (long)r[1]!)); // counts sum to all orders
    }

    [Fact]
    public void Iif_function()
    {
        var rows = Query("SELECT IIF(Discontinued, 1, 0) AS d FROM Products WHERE ProductID = 5", out _);
        Assert.Equal(1, Assert.Single(rows)[0]); // product 5 is discontinued (int literal stays int)
    }

    [Fact]
    public void Fix_and_int_differ_on_negatives()
    {
        // Northwind has no negative values, so this guards the FIX-vs-INT distinction
        // directly: FIX truncates toward zero, INT floors toward -infinity.
        // (-2.7 is written as 0 - 2.7 since unary minus isn't in the grammar yet.)
        var rows = Query(
            "SELECT FIX(0 - 2.7) AS f, INT(0 - 2.7) AS i, FIX(2.7) AS fp, INT(2.7) AS ip, ABS(0 - 3.5) AS a " +
            "FROM Products WHERE ProductID = 1", out _);
        var r = Assert.Single(rows);
        Assert.Equal(-2m, Convert.ToDecimal(r[0])); // FIX(-2.7) -> -2 (toward zero)
        Assert.Equal(-3m, Convert.ToDecimal(r[1])); // INT(-2.7) -> -3 (toward -inf)
        Assert.Equal(2m, Convert.ToDecimal(r[2]));  // FIX(2.7)  -> 2
        Assert.Equal(2m, Convert.ToDecimal(r[3]));  // INT(2.7)  -> 2 (equal for positives)
        Assert.Equal(3.5m, Convert.ToDecimal(r[4])); // ABS(-3.5) -> 3.5
    }

    [Fact]
    public void Min_aggregate_with_comma_cross_join()
    {
        // Each customer's earliest order: MIN(OrderID) per customer, comma-joined back to Orders.
        const string sql = """
            SELECT `o0`.`OrderID`, `o0`.`CustomerID`, `o0`.`EmployeeID`, `o0`.`OrderDate`
            FROM (
                SELECT MIN(`o`.`OrderID`) AS `c`
                FROM `Orders` AS `o`
                GROUP BY `o`.`CustomerID`
            ) AS `o1`,
            `Orders` AS `o0`
            WHERE `o0`.`OrderID` = `o1`.`c`
            """;

        var rows = Query(sql, out var columns);
        Assert.Equal(["OrderID", "CustomerID", "EmployeeID", "OrderDate"], columns);

        using var db = JetDatabase.Open(Northwind);
        var orders = db.OpenTable("Orders");
        int id = orders.Definition.Columns.Single(c => c.Name == "OrderID").Index;
        int cust = orders.Definition.Columns.Single(c => c.Name == "CustomerID").Index;
        var expectedMinIds = orders.Rows()
            .Where(r => r[cust] is not null)
            .GroupBy(r => r[cust]!.ToString()!)
            .Select(g => g.Min(r => (int)r[id]!))
            .OrderBy(x => x)
            .ToList();

        Assert.Equal(expectedMinIds.Count, rows.Count); // 89 customers with orders
        Assert.Equal(expectedMinIds, rows.Select(r => (int)r[0]!).OrderBy(x => x).ToList());
    }

    [Fact]
    public void Datepart_extracts_year()
    {
        var rows = Query("SELECT DATEPART('yyyy', OrderDate) AS y FROM Orders WHERE OrderID = 10248", out _);
        Assert.Equal(1996, Assert.Single(rows)[0]); // first Northwind order is 1996-07-04
    }

    [Fact]
    public void Group_by_multiple_keys_with_datepart_then_count()
    {
        // Per customer, how many distinct years did they place orders in.
        const string sql = """
            SELECT `o1`.`CustomerID` AS `Key`, COUNT(*) AS `Count`
            FROM (
                SELECT `o0`.`CustomerID`
                FROM (
                    SELECT `o`.`CustomerID`, DATEPART('yyyy', `o`.`OrderDate`) AS `Year`
                    FROM `Orders` AS `o`
                ) AS `o0`
                GROUP BY `o0`.`CustomerID`, `o0`.`Year`
            ) AS `o1`
            GROUP BY `o1`.`CustomerID`
            """;

        var rows = Query(sql, out var columns);
        Assert.Equal(["Key", "Count"], columns);

        var got = rows.ToDictionary(r => r[0]!.ToString()!, r => (long)r[1]!);
        Assert.Equal(89, got.Count); // 89 of 91 customers have orders

        using var db = JetDatabase.Open(Northwind);
        var orders = db.OpenTable("Orders");
        int cust = orders.Definition.Columns.Single(c => c.Name == "CustomerID").Index;
        int odate = orders.Definition.Columns.Single(c => c.Name == "OrderDate").Index;
        var expected = orders.Rows()
            .Where(r => r[cust] is not null)
            .GroupBy(r => r[cust]!.ToString()!)
            .ToDictionary(g => g.Key, g => (long)g.Select(r => ((DateTime)r[odate]!).Year).Distinct().Count());

        Assert.Equal(expected.Count, got.Count);
        foreach (var (k, v) in expected) Assert.Equal(v, got[k]);
    }

    [Fact]
    public void Nested_aggregate_with_sum_iif_and_constant_group_key()
    {
        const string sql = """
            SELECT `o1`.`Key0` AS `Key`, IIF(SUM(`o1`.`Count`) IS NULL, 0, SUM(`o1`.`Count`)) AS `Count`
            FROM (
                SELECT `o0`.`Count`, 1 AS `Key0`
                FROM (
                    SELECT COUNT(*) AS `Count`
                    FROM `Orders` AS `o`
                    GROUP BY `o`.`CustomerID`
                ) AS `o0`
            ) AS `o1`
            GROUP BY `o1`.`Key0`
            """;

        var rows = Query(sql, out var columns);
        Assert.Equal(["Key", "Count"], columns);
        var only = Assert.Single(rows);
        Assert.Equal(1, only[0]); // constant `1 AS Key0` stays int
        Assert.Equal(830m, only[1]); // SUM of per-customer counts = total orders
    }
}
