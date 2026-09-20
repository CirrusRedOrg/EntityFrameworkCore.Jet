using System.Linq;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// <c>GREATEST(a, b, …)</c> and <c>LEAST(a, b, …)</c> — the largest and smallest argument, NULLs ignored.
/// Access/ACE has neither, so like COALESCE they are reachable from LibRed's extended SQL mode, which translates
/// <c>Math.Max</c>/<c>Math.Min</c> to them, and from hand-written SQL.
/// </summary>
public class GreatestLeastTests
{
    private static QueryEngine Northwind()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "greatest-");
        return new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
    }

    private static object? Scalar(string expr)
        => Northwind().ExecuteQuery($"SELECT {expr} FROM Employees WHERE EmployeeID = 1").Rows.First()[0];

    private static Type ColumnType(string expr)
        => Northwind().ExecuteQuery($"SELECT {expr} FROM Employees WHERE EmployeeID = 1").ColumnTypes[0];

    [Fact]
    public void Greatest_returns_the_largest_argument()
        => Assert.Equal(9, Convert.ToInt32(Scalar("GREATEST(3, 9, -4, 7)")));

    [Fact]
    public void Least_returns_the_smallest_argument()
        => Assert.Equal(-4, Convert.ToInt32(Scalar("LEAST(3, 9, -4, 7)")));

    [Fact]
    public void Null_arguments_are_ignored()
    {
        // SQL Server's and PostgreSQL's rule, and the one EF Core translates Math.Max/Min against: a NULL does
        // not make the answer NULL while another argument has a value.
        Assert.Equal(5, Convert.ToInt32(Scalar("GREATEST(NULL, 5, NULL, 2)")));
        Assert.Equal(2, Convert.ToInt32(Scalar("LEAST(NULL, 5, NULL, 2)")));
    }

    [Fact]
    public void All_null_arguments_yield_null()
    {
        Assert.Null(Scalar("GREATEST(NULL, NULL)"));
        Assert.Null(Scalar("LEAST(NULL, NULL)"));
    }

    [Fact]
    public void A_single_argument_is_accepted()
    {
        Assert.Equal(4, Convert.ToInt32(Scalar("GREATEST(4)")));
        Assert.Equal(4, Convert.ToInt32(Scalar("LEAST(4)")));
    }

    [Fact]
    public void Zero_arguments_is_an_error()
    {
        Assert.ThrowsAny<Exception>(() => Scalar("GREATEST()"));
        Assert.ThrowsAny<Exception>(() => Scalar("LEAST()"));
    }

    [Fact]
    public void Mixed_numeric_arguments_compare_by_value()
    {
        Assert.Equal(2.5, Convert.ToDouble(Scalar("GREATEST(2, 2.5E0, 1)")));
        Assert.Equal(1, Convert.ToDouble(Scalar("LEAST(2, 2.5E0, 1)")));
    }

    [Fact]
    public void Text_compares_as_the_comparison_operators_do()
    {
        // Case-insensitively, as "=" and "<" compare text in Access's default "Compare Database" mode.
        Assert.Equal("pear", Scalar("GREATEST('apple', 'pear', 'Banana')"));
        Assert.Equal("apple", Scalar("LEAST('apple', 'pear', 'Banana')"));
    }

    [Fact]
    public void Dates_compare_by_value()
    {
        Assert.Equal(new DateTime(2024, 5, 1), Scalar("GREATEST(#2020-01-01#, #2024-05-01#, #2019-12-31#)"));
        Assert.Equal(new DateTime(2019, 12, 31), Scalar("LEAST(#2020-01-01#, #2024-05-01#, #2019-12-31#)"));
    }

    [Fact]
    public void Works_over_columns_in_a_where_clause()
    {
        var e = Northwind();
        int[] got = e.ExecuteQuery(
                "SELECT OrderID FROM [Order Details] WHERE GREATEST(Quantity, 100) = Quantity ORDER BY OrderID")
            .Rows.Select(r => Convert.ToInt32(r[0])).ToArray();
        int[] expected = e.ExecuteQuery(
                "SELECT OrderID FROM [Order Details] WHERE Quantity >= 100 ORDER BY OrderID")
            .Rows.Select(r => Convert.ToInt32(r[0])).ToArray();

        Assert.NotEmpty(expected);
        Assert.Equal(expected, got);
    }

    [Fact]
    public void Nests_and_composes_like_any_expression()
        => Assert.Equal(6, Convert.ToInt32(Scalar("1 + GREATEST(LEAST(8, 5), 2)")));

    [Theory]
    [InlineData("greatest(1, 2)", 2)]
    [InlineData("Least(1, 2)", 1)]
    public void Name_is_case_insensitive(string expr, int expected)
        => Assert.Equal(expected, Convert.ToInt32(Scalar(expr)));

    // Each returns one of its arguments, so each declares the type its arguments unify to, as COALESCE does.

    [Fact]
    public void Uniform_argument_types_declare_that_type()
        => Assert.Equal(typeof(string), ColumnType("GREATEST('a', 'b')"));

    [Fact]
    public void A_null_argument_does_not_erase_the_declared_type()
        => Assert.Equal(typeof(string), ColumnType("LEAST(NULL, 'a')"));

    [Fact]
    public void Numeric_arguments_widen_to_the_larger_type()
        => Assert.Equal(typeof(double), ColumnType("GREATEST(1, 2.5E0)"));
}
