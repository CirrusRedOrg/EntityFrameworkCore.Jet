using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// Partition / StrConv / WeekdayName — implemented after probing their exact ACE semantics. Expected values are
// what ACE returned, except WeekdayName's omitted first-day (OS-locale-dependent) which is asserted only with an
// explicit first-day argument.
public class DeferredFunctionsTests
{
    private static QueryEngine Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"dff-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE T ( K LONG PRIMARY KEY )");
        e.ExecuteNonQuery("INSERT INTO T (K) VALUES (1)");
        return e;
    }

    private static string Eval(string expr) => Fresh().ExecuteQuery($"SELECT {expr} FROM T").Rows.Single()[0]?.ToString()!;

    [Theory]
    [InlineData("Partition(5, 1, 100, 10)", "  1: 10")]
    [InlineData("Partition(11, 1, 100, 10)", " 11: 20")]
    [InlineData("Partition(100, 1, 100, 10)", " 91:100")]
    [InlineData("Partition(0, 1, 100, 10)", "   :  0")]
    [InlineData("Partition(-5, 1, 100, 10)", "   :  0")]
    [InlineData("Partition(150, 1, 100, 10)", "101:   ")]
    [InlineData("Partition(50, 0, 99, 25)", " 50: 74")]
    public void Partition_matches_ace(string expr, string expected)
        => Assert.Equal(expected, Eval(expr));

    [Theory]
    [InlineData("StrConv('hello world', 1)", "HELLO WORLD")]
    [InlineData("StrConv('HELLO WORLD', 2)", "hello world")]
    [InlineData("StrConv('hello world', 3)", "Hello World")]
    [InlineData("StrConv('mixed CASE text', 3)", "Mixed Case Text")]
    public void StrConv_case_modes_match_ace(string expr, string expected)
        => Assert.Equal(expected, Eval(expr));

    [Fact]
    public void StrConv_unsupported_mode_is_rejected()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Eval("StrConv('hello', 4)"));
        Assert.Contains("Invalid procedure call", ex.Message);
    }

    [Theory]
    // explicit firstDayOfWeek → deterministic, matches ACE
    [InlineData("WeekdayName(1, False, 1)", "Sunday")]
    [InlineData("WeekdayName(2, False, 1)", "Monday")]
    [InlineData("WeekdayName(7, False, 1)", "Saturday")]
    [InlineData("WeekdayName(1, False, 2)", "Monday")]
    [InlineData("WeekdayName(7, False, 2)", "Sunday")]
    [InlineData("WeekdayName(1, True, 1)", "Sun")]
    [InlineData("WeekdayName(3, True, 2)", "Wed")]
    public void WeekdayName_with_explicit_first_day_matches_ace(string expr, string expected)
        => Assert.Equal(expected, Eval(expr));

    [Fact]
    public void WeekdayName_omitted_first_day_defaults_to_sunday()
        // ACE's omitted default follows the OS regional first day; LibRed fixes it to vbSunday for determinism.
        => Assert.Equal("Sunday", Eval("WeekdayName(1)"));
}
