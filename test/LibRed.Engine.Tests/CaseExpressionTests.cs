using System.Linq;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// Standard SQL <c>CASE</c>, in both the searched and simple forms. Access/ACE has no CASE — only
/// <c>IIF()</c> — so the Jet-compatible SQL generator rewrites a CASE into nested IIFs and only LibRed's
/// extended mode (and hand-written SQL) reaches this.
/// </summary>
public class CaseExpressionTests
{
    private static QueryEngine Northwind()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "case-");
        return new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
    }

    private static object? Scalar(string expr)
        => Northwind().ExecuteQuery($"SELECT {expr} FROM Employees WHERE EmployeeID = 1").Rows.First()[0];

    private static string[] Column(QueryEngine e, string sql)
        => e.ExecuteQuery(sql).Rows.Select(r => r[0]?.ToString() ?? "").ToArray();

    [Fact]
    public void Searched_case_returns_the_first_matching_arm()
        => Assert.Equal("one", Scalar("CASE WHEN 1 = 1 THEN 'one' WHEN 1 = 1 THEN 'two' ELSE 'none' END"));

    [Fact]
    public void Searched_case_falls_through_to_else()
        => Assert.Equal("none", Scalar("CASE WHEN 1 = 2 THEN 'one' WHEN 2 = 3 THEN 'two' ELSE 'none' END"));

    [Fact]
    public void Unmatched_case_without_else_is_null()
        => Assert.Null(Scalar("CASE WHEN 1 = 2 THEN 'one' END"));

    [Fact]
    public void Simple_case_compares_the_operand_to_each_value()
        => Assert.Equal("b", Scalar("CASE 2 WHEN 1 THEN 'a' WHEN 2 THEN 'b' ELSE 'z' END"));

    [Fact]
    public void Simple_case_falls_through_to_else()
        => Assert.Equal("z", Scalar("CASE 9 WHEN 1 THEN 'a' WHEN 2 THEN 'b' ELSE 'z' END"));

    [Fact]
    public void A_null_condition_does_not_select_its_arm()
        // Only true selects: an arm whose condition is NULL is skipped exactly as a false one is, so this
        // lands on the ELSE rather than returning 'matched'.
        => Assert.Equal("fell-through", Scalar("CASE WHEN NULL = 1 THEN 'matched' ELSE 'fell-through' END"));

    [Fact]
    public void A_null_operand_never_matches_in_the_simple_form()
        => Assert.Equal("z", Scalar("CASE NULL WHEN 1 THEN 'a' ELSE 'z' END"));

    [Fact]
    public void Arms_short_circuit_so_unselected_results_are_not_evaluated()
        // The point of the guard shape EF emits: the divisor is zero on the row the condition excludes, and
        // evaluating the unselected THEN would throw rather than return.
        => Assert.Equal("safe", Scalar("CASE WHEN 0 = 1 THEN CSTR(1 / 0) ELSE 'safe' END"));

    [Fact]
    public void Case_works_over_columns_in_a_projection()
    {
        var e = Northwind();
        var got = Column(e,
            "SELECT CASE WHEN EmployeeID <= 2 THEN 'low' WHEN EmployeeID <= 4 THEN 'mid' ELSE 'high' END " +
            "FROM Employees ORDER BY EmployeeID");

        Assert.Equal(["low", "low", "mid", "mid", "high", "high", "high", "high", "high"], got);
    }

    [Fact]
    public void Case_works_in_a_where_clause()
    {
        var e = Northwind();
        var got = e.ExecuteQuery(
            "SELECT EmployeeID FROM Employees WHERE CASE WHEN EmployeeID <= 3 THEN 1 ELSE 0 END = 1 ORDER BY EmployeeID")
            .Rows.Select(r => Convert.ToInt32(r[0])).ToArray();

        Assert.Equal([1, 2, 3], got);
    }

    [Fact]
    public void Case_may_nest_in_its_own_result()
        => Assert.Equal("inner", Scalar(
            "CASE WHEN 1 = 1 THEN CASE WHEN 2 = 2 THEN 'inner' ELSE 'no' END ELSE 'outer-else' END"));

    [Fact]
    public void Case_may_be_used_as_an_operand_of_an_expression()
        => Assert.Equal(11, Convert.ToInt32(Scalar("10 + CASE WHEN 1 = 1 THEN 1 ELSE 2 END")));

    [Theory]
    // The keywords are case-insensitive like the rest of the dialect.
    [InlineData("case when 1 = 1 then 'x' else 'y' end")]
    [InlineData("Case When 1 = 1 Then 'x' Else 'y' End")]
    public void Case_keywords_are_case_insensitive(string expr)
        => Assert.Equal("x", Scalar(expr));

    [Fact]
    public void Iif_still_works_since_ace_has_no_case()
        // The compat path must be untouched: Jet has no CASE and its generator emits IIF instead.
        => Assert.Equal("yes", Scalar("IIF(1 = 1, 'yes', 'no')"));

    // CASE is valid anywhere an expression is - the T-SQL reference names SELECT, UPDATE, DELETE and SET,
    // and the <select_list>, IN, WHERE, ORDER BY and HAVING clauses. The grammar puts it in `primary`, so
    // these all come for free; they are pinned here because "for free" is exactly the kind of claim that
    // turns out to be wrong.

    [Fact]
    public void Case_works_in_an_order_by()
    {
        var e = Northwind();
        var got = e.ExecuteQuery(
            "SELECT EmployeeID FROM Employees ORDER BY CASE WHEN EmployeeID = 5 THEN 0 ELSE 1 END, EmployeeID")
            .Rows.Select(r => Convert.ToInt32(r[0])).ToArray();

        Assert.Equal(5, got[0]);   // the arm hoists 5 to the front, the rest stay in order
        Assert.Equal([1, 2, 3, 4], got.Skip(1).Take(4));
    }

    [Fact]
    public void Case_works_in_a_having_clause()
    {
        var e = Northwind();
        var got = e.ExecuteQuery(
            "SELECT City, COUNT(*) FROM Employees GROUP BY City " +
            "HAVING CASE WHEN COUNT(*) > 1 THEN 1 ELSE 0 END = 1 ORDER BY City")
            .Rows.Select(r => Convert.ToInt32(r[1])).ToArray();

        Assert.NotEmpty(got);
        Assert.All(got, n => Assert.True(n > 1));
    }

    [Fact]
    public void Case_works_in_an_update_set()
    {
        var e = Northwind();
        e.ExecuteNonQuery("CREATE TABLE CaseUpd (Id LONG PRIMARY KEY, N LONG)");
        e.ExecuteNonQuery("INSERT INTO CaseUpd (Id, N) VALUES (1, 5)");
        e.ExecuteNonQuery("INSERT INTO CaseUpd (Id, N) VALUES (2, 50)");
        e.ExecuteNonQuery("UPDATE CaseUpd SET N = CASE WHEN N < 10 THEN N + 100 ELSE N END");

        Assert.Equal([105, 50], e.ExecuteQuery("SELECT N FROM CaseUpd ORDER BY Id")
            .Rows.Select(r => Convert.ToInt32(r[0])).ToArray());
    }

    // The standard derives a CASE's type from its result expressions - "the highest precedence type from the
    // set of types in result_expressions and the optional else_result_expression". Without that the column
    // comes back as object and a consumer reading typed values gets nothing to work with.

    private static Type ColumnType(string expr)
        => Northwind().ExecuteQuery($"SELECT {expr} FROM Employees WHERE EmployeeID = 1").ColumnTypes[0];

    [Fact]
    public void Uniform_result_types_declare_that_type()
        => Assert.Equal(typeof(string), ColumnType("CASE WHEN 1 = 1 THEN 'a' ELSE 'b' END"));

    [Fact]
    public void A_null_branch_does_not_erase_the_declared_type()
        // A NULL constant carries no type, so it contributes nothing to precedence rather than making the
        // whole expression untyped.
        => Assert.Equal(typeof(string), ColumnType("CASE WHEN 1 = 1 THEN 'a' ELSE NULL END"));

    [Fact]
    public void Numeric_branches_widen_to_the_larger_type()
        => Assert.Equal(typeof(double), ColumnType("CASE WHEN 1 = 1 THEN 1 ELSE 2.5E0 END"));

    [Fact]
    public void Irreconcilable_branches_declare_nothing()
        // A string arm and a numeric arm have no common type; declaring one would be a guess, so the column
        // stays untyped rather than claiming something wrong.
        => Assert.Equal(typeof(object), ColumnType("CASE WHEN 1 = 1 THEN 'a' ELSE 1 END"));
}
