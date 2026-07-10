using System.Linq;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// The Access SELECT predicates — <c>ALL</c>, <c>DISTINCT</c>, <c>DISTINCTROW</c>, and <c>TOP n [PERCENT]</c>.
/// Row counts are the ones the ACE engine itself returns for the same queries (probed against Northwind and,
/// for the DISTINCTROW edge case, a purpose-built table).
/// </summary>
public class SelectPredicateTests
{
    private static QueryEngine Northwind()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pred-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        return new QueryEngine(JetDatabase.Open(path, readOnly: false));
    }

    private static int Count(QueryEngine e, string sql) => e.ExecuteQuery(sql).Rows.Count();

    [Theory]
    // ceil(rows × n / 100): Orders has 830 rows, Employees 9. Verified against ACE.
    [InlineData("SELECT TOP 10 PERCENT * FROM Orders ORDER BY OrderID", 83)]
    [InlineData("SELECT TOP 1 PERCENT * FROM Orders ORDER BY OrderID", 9)]     // 8.3 → 9
    [InlineData("SELECT TOP 10 PERCENT * FROM Employees ORDER BY EmployeeID", 1)] // 0.9 → 1
    [InlineData("SELECT TOP 25 PERCENT * FROM Employees ORDER BY EmployeeID", 3)] // 2.25 → 3
    [InlineData("SELECT TOP 33 PERCENT * FROM Employees ORDER BY EmployeeID", 3)] // 2.97 → 3
    public void Top_percent_takes_the_ceiling(string sql, int expected)
        => Assert.Equal(expected, Count(Northwind(), sql));

    [Fact]
    public void Top_n_still_takes_an_absolute_count()
        => Assert.Equal(5, Count(Northwind(), "SELECT TOP 5 * FROM Orders ORDER BY OrderID"));

    [Fact]
    public void All_is_the_default_and_returns_every_row()
    {
        var e = Northwind();
        Assert.Equal(9, Count(e, "SELECT ALL City FROM Employees"));   // explicit ALL
        Assert.Equal(9, Count(e, "SELECT City FROM Employees"));       // omitted ⇒ same
    }

    [Fact]
    public void Distinct_dedupes_on_the_output_columns()
        => Assert.Equal(5, Count(Northwind(), "SELECT DISTINCT City FROM Employees")); // 9 rows, 5 cities

    [Fact]
    public void Distinctrow_is_ignored_for_a_single_table()
        // Access ignores DISTINCTROW when there is one source table — it returns all rows, unlike DISTINCT.
        => Assert.Equal(9, Count(Northwind(), "SELECT DISTINCTROW City FROM Employees"));

    [Fact]
    public void Distinctrow_is_ignored_when_output_covers_every_table()
    {
        var e = Northwind();
        const string join = "FROM Customers c INNER JOIN Orders o ON c.CustomerID = o.CustomerID";
        // Output columns from both tables ⇒ DISTINCTROW is a no-op, same as plain.
        Assert.Equal(830, Count(e, $"SELECT DISTINCTROW c.CompanyName, o.OrderID {join}"));
        Assert.Equal(830, Count(e, $"SELECT c.CompanyName, o.OrderID {join}"));
    }

    [Fact]
    public void Distinctrow_dedupes_on_the_contributing_tables_rows()
    {
        // Output only from Customers ⇒ one row per distinct customer that has ≥1 order.
        var e = Northwind();
        const string join = "FROM Customers c INNER JOIN Orders o ON c.CustomerID = o.CustomerID";
        Assert.Equal(89, Count(e, $"SELECT DISTINCTROW c.CompanyName {join}"));
        Assert.Equal(830, Count(e, $"SELECT c.CompanyName {join}")); // without the predicate: one per order
    }

    [Fact]
    public void Distinctrow_dedupes_on_underlying_rows_not_output_values()
    {
        // Two parents share a name but are distinct rows; DISTINCTROW keeps both (it dedupes on the whole
        // Parent row), where DISTINCT on the name collapses them. A third parent appears twice in the join
        // but is one row, so it dedupes to one. ACE returns DISTINCTROW = 3, DISTINCT = 2 (verified).
        var e = Northwind();
        foreach (string sql in new[]
        {
            "CREATE TABLE P (Id LONG PRIMARY KEY, Nm TEXT(50))",
            "CREATE TABLE Ch (Id LONG PRIMARY KEY, Pid LONG)",
            "INSERT INTO P (Id, Nm) VALUES (1, 'Acme')",
            "INSERT INTO P (Id, Nm) VALUES (2, 'Acme')",
            "INSERT INTO P (Id, Nm) VALUES (3, 'Beta')",
            "INSERT INTO Ch (Id, Pid) VALUES (10, 1)",
            "INSERT INTO Ch (Id, Pid) VALUES (11, 2)",
            "INSERT INTO Ch (Id, Pid) VALUES (12, 3)",
            "INSERT INTO Ch (Id, Pid) VALUES (13, 3)",
        })
            e.ExecuteNonQuery(sql);

        const string join = "FROM P AS p INNER JOIN Ch AS c ON p.Id = c.Pid";
        Assert.Equal(3, Count(e, $"SELECT DISTINCTROW p.Nm {join}"));
        Assert.Equal(2, Count(e, $"SELECT DISTINCT p.Nm {join}"));
    }
}
