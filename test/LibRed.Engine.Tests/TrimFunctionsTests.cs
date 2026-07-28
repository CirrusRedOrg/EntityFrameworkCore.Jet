using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// Trim/LTrim/RTrim are single-argument and remove ONLY spaces (not tabs or other whitespace, and no trim-char
// parameter) — verified vs ACE. NULL-propagating.
public class TrimFunctionsTests
{
    private static QueryEngine Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"trim-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE T ( K LONG PRIMARY KEY )");
        e.ExecuteNonQuery("INSERT INTO T (K) VALUES (1)");
        return e;
    }

    private static object? Eval(string expr) => Fresh().ExecuteQuery($"SELECT {expr} FROM T").Rows.Single()[0];

    [Theory]
    [InlineData("Trim('  hi  ')", "hi")]
    [InlineData("LTrim('  hi  ')", "hi  ")]
    [InlineData("RTrim('  hi  ')", "  hi")]
    public void Trims_spaces(string expr, string expected)
        => Assert.Equal(expected, Convert.ToString(Eval(expr)));

    [Fact]
    public void Trim_removes_only_spaces_not_tabs()
        // a tab (Chr(9)) either side is preserved — ACE Trim removes spaces only.
        => Assert.Equal("\thi\t", Convert.ToString(Eval("Trim(Chr(9) & 'hi' & Chr(9))")));

    [Theory]
    [InlineData("Trim(Null)")]
    [InlineData("LTrim(Null)")]
    [InlineData("RTrim(Null)")]
    public void Trim_propagates_null(string expr)
        => Assert.Null(Eval(expr));
}
