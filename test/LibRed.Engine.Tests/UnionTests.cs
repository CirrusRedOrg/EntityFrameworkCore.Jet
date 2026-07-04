using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class UnionTests
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
    public void Union_of_two_cities()
    {
        const string sql = """
            SELECT `c`.`CustomerID`, `c`.`City`
            FROM `Customers` AS `c`
            WHERE `c`.`City` = 'Berlin'
            UNION
            SELECT `c0`.`CustomerID`, `c0`.`City`
            FROM `Customers` AS `c0`
            WHERE `c0`.`City` = 'London'
            """;

        var rows = Query(sql, out var columns);
        Assert.Equal(["CustomerID", "City"], columns); // names from the leading query
        Assert.Equal(7, rows.Count); // 1 Berlin + 6 London, all distinct
        Assert.Contains(rows, r => (string)r[0]! == "ALFKI" && (string)r[1]! == "Berlin");
        Assert.Equal(6, rows.Count(r => (string)r[1]! == "London"));
    }

    [Fact]
    public void Union_removes_duplicate_rows()
    {
        // Same query on both sides (6 distinct London customers): UNION dedupes the two
        // identical sets back to 6, UNION ALL keeps all 12.
        const string one = "SELECT CustomerID FROM Customers WHERE City = 'London'";
        var distinct = Query($"{one} UNION {one}", out _);
        var all = Query($"{one} UNION ALL {one}", out _);

        Assert.Equal(6, distinct.Count); // duplicates removed
        Assert.Equal(12, all.Count);     // UNION ALL keeps both copies
    }

    [Fact]
    public void Intersect_keeps_rows_in_both()
    {
        // Customers in (London or Berlin) INTERSECT customers in (London or Madrid) = London only.
        var rows = Query(
            "SELECT CustomerID, City FROM Customers WHERE City = 'London' OR City = 'Berlin' " +
            "INTERSECT " +
            "SELECT CustomerID, City FROM Customers WHERE City = 'London' OR City = 'Madrid'", out _);

        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal("London", r[1])); // only the shared London rows survive
        Assert.Equal(6, rows.Count);
    }

    [Fact]
    public void Except_keeps_left_rows_not_in_right()
    {
        // (London or Berlin) EXCEPT (London) = Berlin only.
        var rows = Query(
            "SELECT CustomerID, City FROM Customers WHERE City = 'London' OR City = 'Berlin' " +
            "EXCEPT " +
            "SELECT CustomerID, City FROM Customers WHERE City = 'London'", out _);

        Assert.All(rows, r => Assert.Equal("Berlin", r[1]));
        Assert.Equal("ALFKI", Assert.Single(rows)[0]); // Berlin has a single customer
    }

    [Fact]
    public void Union_with_correlated_count_subquery_per_customer()
    {
        // Correlated COUNT(*) of each customer's orders, unioned with the same query.
        // Exercises the empty-group case: customers with zero orders must yield COUNT 0,
        // not crash (an aggregate with no GROUP BY over zero rows forms an empty group).
        const string side = """
            SELECT `c`.`CustomerID`, (
                SELECT COUNT(*) FROM `Orders` AS `o`
                WHERE `c`.`CustomerID` = `o`.`CustomerID`) AS `Orders`
            FROM `Customers` AS `c`
            """;

        var rows = Query($"{side} UNION {side.Replace("`c`", "`c0`").Replace("`o`", "`o0`")}", out var columns);
        Assert.Equal(["CustomerID", "Orders"], columns);
        Assert.Equal(91, rows.Count); // all customers; UNION dedupes the identical sides
        Assert.Equal(830, rows.Sum(r => (int)r[1]!)); // total Northwind orders
        Assert.Equal(2, rows.Count(r => (int)r[1]! == 0)); // FISSA and PARIS have no orders
    }

    [Fact]
    public void Union_collapses_identical_values_to_one()
    {
        // Selecting only City, all 6 London rows are the same value 'London'.
        var rows = Query("SELECT City FROM Customers WHERE City = 'London' UNION " +
            "SELECT City FROM Customers WHERE City = 'London'", out _);
        Assert.Equal("London", Assert.Single(rows)[0]);
    }
}
