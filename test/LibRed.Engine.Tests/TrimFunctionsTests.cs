using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// Trim/LTrim/RTrim are single-argument and remove ONLY spaces — the space and the ideographic space U+3000, in any
// mixture; not tabs or other whitespace, and no trim-char parameter — verified vs ACE. NULL-propagating.
public class TrimFunctionsTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "trim-");
        var e = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
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

    // A space, U+3000, a space, 'a', U+3000, a space, U+3000: ACE strips the whole run either side.
    [Theory]
    [InlineData("Trim", "a")]
    [InlineData("LTrim", "a　 　")]
    [InlineData("RTrim", " 　 a")]
    public void The_ideographic_space_is_trimmed_as_a_space(string function, string expected)
        => Assert.Equal(expected, Convert.ToString(Eval(
            $"{function}(' ' & ChrW(12288) & ' ' & 'a' & ChrW(12288) & ' ' & ChrW(12288))")));

    // No other space is: a no-break space, an en space, a zero-width space.
    [Theory]
    [InlineData(160)]
    [InlineData(8194)]
    [InlineData(8203)]
    public void Other_unicode_spaces_are_kept(int codePoint)
        => Assert.Equal(3, Convert.ToString(Eval($"Trim(ChrW({codePoint}) & 'a' & ChrW({codePoint}))"))!.Length);

    [Theory]
    [InlineData("Trim(Null)")]
    [InlineData("LTrim(Null)")]
    [InlineData("RTrim(Null)")]
    public void Trim_propagates_null(string expr)
        => Assert.Null(Eval(expr));
}
