using LibRed;
using LibRed.Engine;
using LibRed.Engine.Execution;
using Xunit;

namespace LibRed.Engine.Tests;

public class JoinQueryTests
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
    public void Order_by_descending()
    {
        var rows = Query("SELECT CategoryName FROM Categories ORDER BY CategoryName DESC", out _);
        Assert.Equal("Seafood", rows[0][0]);
        Assert.Equal("Beverages", rows[^1][0]);
    }

    [Fact]
    public void Mod_operator_in_where()
    {
        var rows = Query("SELECT ProductID FROM Products WHERE ProductID MOD 17 = 5", out _);
        Assert.Equal([5, 22, 39, 56, 73], rows.Select(r => (int)r[0]!).OrderBy(x => x));
    }

    [Fact]
    public void Inner_join_with_aliases_and_qualified_columns()
    {
        var rows = Query(
            "SELECT p.ProductName, c.CategoryName FROM Products AS p " +
            "INNER JOIN Categories AS c ON p.CategoryID = c.CategoryID WHERE p.ProductID = 1",
            out var columns);

        Assert.Equal(["ProductName", "CategoryName"], columns);
        var only = Assert.Single(rows);
        Assert.Equal("Chai", only[0]);
        Assert.Equal("Beverages", only[1]);
    }

    [Fact]
    public void Left_join_over_a_derived_table()
    {
        // The full EF-generated query: LEFT JOIN to a subquery (Order Details INNER JOIN Orders),
        // with backtick identifiers, MOD, a projection alias, and a multi-key ORDER BY.
        const string sql = """
            SELECT `p`.`ProductID`, `p`.`ProductName`, `p`.`UnitPrice`, `s`.`OrderID`, `s`.`ProductID`, `s`.`Quantity`, `s`.`OrderID0`, `s`.`OrderDate`
            FROM `Products` AS `p`
            LEFT JOIN (
                SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Quantity`, `o0`.`OrderID` AS `OrderID0`, `o0`.`OrderDate`
                FROM `Order Details` AS `o`
                INNER JOIN `Orders` AS `o0` ON `o`.`OrderID` = `o0`.`OrderID`
            ) AS `s` ON `p`.`ProductID` = `s`.`ProductID`
            WHERE (`p`.`ProductID` MOD 17) = 5 AND `p`.`UnitPrice` < 20.0
            ORDER BY `p`.`ProductID`, `s`.`OrderID`, `s`.`ProductID`
            """;

        var rows = Query(sql, out var columns);

        // Duplicate output names (p.ProductID and s.ProductID) are preserved.
        Assert.Equal(["ProductID", "ProductName", "UnitPrice", "OrderID", "ProductID", "Quantity", "OrderID0", "OrderDate"], columns);

        // Only products 39 and 73 satisfy (id MOD 17 = 5) AND (price < 20).
        Assert.Equal([39, 73], rows.Select(r => (int)r[0]!).Distinct().OrderBy(x => x));
        Assert.All(rows, r => Assert.Equal(r[0], r[4])); // join key: p.ProductID == s.ProductID

        // Ordered by p.ProductID then s.OrderID.
        var keys = rows.Select(r => ((int)r[0]!, (int)r[3]!)).ToList();
        Assert.Equal(keys.OrderBy(k => k.Item1).ThenBy(k => k.Item2), keys);

        // OrderID0 (the alias from the inner Orders join) equals OrderID for every matched row.
        Assert.All(rows, r => Assert.Equal(r[3], r[6]));
    }
}
