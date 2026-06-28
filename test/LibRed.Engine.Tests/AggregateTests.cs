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
