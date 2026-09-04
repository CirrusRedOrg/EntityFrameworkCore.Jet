using System.Linq;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// Standard SQL <c>COALESCE(a, b, …)</c> — the first argument that is not NULL. Access/ACE has no COALESCE
/// at all (its nearest relative, <c>Nz</c>, takes only two arguments), so this is reachable from LibRed's
/// extended SQL mode and from hand-written SQL, never from the Jet-compatible generator.
/// </summary>
public class CoalesceTests
{
    private static QueryEngine Northwind()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "coalesce-");
        return new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
    }

    private static object? Scalar(string expr)
        => Northwind().ExecuteQuery($"SELECT {expr} FROM Employees WHERE EmployeeID = 1").Rows.First()[0];

    private static Type ColumnType(string expr)
        => Northwind().ExecuteQuery($"SELECT {expr} FROM Employees WHERE EmployeeID = 1").ColumnTypes[0];

    [Fact]
    public void Returns_the_first_non_null_argument()
        => Assert.Equal("third_value", Scalar("COALESCE(NULL, NULL, 'third_value', 'fourth_value')"));

    [Fact]
    public void Returns_the_first_argument_when_it_is_not_null()
        => Assert.Equal("first", Scalar("COALESCE('first', 'second')"));

    [Fact]
    public void All_null_arguments_yield_null()
        => Assert.Null(Scalar("COALESCE(NULL, NULL, NULL)"));

    [Fact]
    public void Takes_a_variable_number_of_arguments()
        => Assert.Equal(7, Convert.ToInt32(Scalar("COALESCE(NULL, NULL, NULL, NULL, NULL, 7)")));

    [Fact]
    public void A_single_argument_is_accepted()
        => Assert.Equal("only", Scalar("COALESCE('only')"));

    [Fact]
    public void Zero_arguments_is_an_error()
        => Assert.ThrowsAny<Exception>(() => Scalar("COALESCE()"));

    [Fact]
    public void Later_arguments_are_not_evaluated_once_a_value_is_found()
        // The standard defines COALESCE as shorthand for a CASE, and SQL Server implements it by literally
        // rewriting to one — which is why its own docs warn arguments can be evaluated more than once. Here
        // each argument is evaluated at most once and evaluation stops at the first non-NULL, so a later
        // argument that would throw is never reached.
        => Assert.Equal("found", Scalar("COALESCE('found', CSTR(1 / 0))"));

    [Fact]
    public void Works_over_a_nullable_column()
    {
        // Region is NULL for some Northwind employees and set for others, so this exercises both branches
        // against real data rather than literals.
        var e = Northwind();
        var got = e.ExecuteQuery(
            "SELECT COALESCE(Region, 'none') FROM Employees ORDER BY EmployeeID")
            .Rows.Select(r => r[0]?.ToString()).ToArray();

        Assert.All(got, v => Assert.False(string.IsNullOrEmpty(v)));
        Assert.Contains("none", got);
    }

    [Fact]
    public void Nests_and_composes_like_any_expression()
        => Assert.Equal("inner", Scalar("COALESCE(NULL, COALESCE(NULL, 'inner'))"));

    [Fact]
    public void May_be_used_as_an_operand()
        => Assert.Equal(11, Convert.ToInt32(Scalar("10 + COALESCE(NULL, 1)")));

    [Fact]
    public void Works_in_a_where_clause()
    {
        var e = Northwind();
        var got = e.ExecuteQuery(
            "SELECT EmployeeID FROM Employees WHERE COALESCE(Region, 'none') = 'none' ORDER BY EmployeeID")
            .Rows.Select(r => Convert.ToInt32(r[0])).ToArray();

        Assert.NotEmpty(got);
    }

    [Theory]
    [InlineData("coalesce(NULL, 'x')")]
    [InlineData("Coalesce(NULL, 'x')")]
    public void Name_is_case_insensitive(string expr)
        => Assert.Equal("x", Scalar(expr));

    // The standard gives COALESCE the same return-type rule as CASE: the highest precedence type among the
    // arguments. Without it a projected COALESCE comes back as object and a consumer reading typed values
    // gets nothing to work with.

    [Fact]
    public void Uniform_argument_types_declare_that_type()
        => Assert.Equal(typeof(string), ColumnType("COALESCE('a', 'b')"));

    [Fact]
    public void A_null_argument_does_not_erase_the_declared_type()
        => Assert.Equal(typeof(string), ColumnType("COALESCE(NULL, 'a')"));

    [Fact]
    public void Numeric_arguments_widen_to_the_larger_type()
        => Assert.Equal(typeof(double), ColumnType("COALESCE(1, 2.5E0)"));

    [Fact]
    public void Irreconcilable_arguments_declare_nothing()
        => Assert.Equal(typeof(object), ColumnType("COALESCE('a', 1)"));
}
