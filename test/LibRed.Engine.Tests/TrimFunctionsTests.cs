using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// With one argument Trim/LTrim/RTrim remove ONLY spaces — the space and the ideographic space U+3000, in any
// mixture; not tabs or other whitespace — verified vs ACE. NULL-propagating. LTrim and RTrim also take SQL
// Server 2022's second argument, a set of characters to strip; that one is a LibRed extension, since ACE takes
// no such parameter.
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

    // The second argument is a SET of characters, not a substring: every leading (or trailing) character that
    // appears anywhere in it is removed, and the stripping stops at the first character that does not.
    [Theory]
    [InlineData("LTrim('xxhixx', 'x')", "hixx")]
    [InlineData("RTrim('xxhixx', 'x')", "xxhi")]
    [InlineData("LTrim('xyxhiyx', 'xy')", "hiyx")]
    [InlineData("RTrim('xyhixyx', 'xy')", "xyhi")]
    [InlineData("LTrim('.,.hi', '.,')", "hi")]
    [InlineData("LTrim('xyz', 'zyx')", "")]           // every character stripped
    [InlineData("LTrim('hi', 'x')", "hi")]            // nothing to strip
    [InlineData("LTrim('hi', '')", "hi")]             // an empty set strips nothing
    [InlineData("LTrim('  hi  ', ' ')", "hi  ")]      // spaces, said explicitly
    [InlineData("RTrim(Chr(9) & 'hi' & Chr(9), Chr(9))", "\thi")] // a tab, which the one-argument form keeps
    public void Two_argument_trim_strips_the_characters_given(string expr, string expected)
        => Assert.Equal(expected, Convert.ToString(Eval(expr)));

    [Theory]
    [InlineData("LTrim(Null, 'x')")]
    [InlineData("LTrim('xhi', Null)")]
    [InlineData("RTrim(Null, 'x')")]
    [InlineData("RTrim('hix', Null)")]
    public void Two_argument_trim_propagates_null(string expr)
        => Assert.Null(Eval(expr));

    // Access has no third argument, and Trim itself takes only the one.
    [Theory]
    [InlineData("LTrim('hi', 'x', 'y')")]
    [InlineData("RTrim('hi', 'x', 'y')")]
    [InlineData("Trim('hi', 'x')")]
    public void A_third_argument_is_rejected(string expr)
        => Assert.Throws<InvalidOperationException>(() => Eval(expr));
}
