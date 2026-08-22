using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// Mid(string, start, [length]) and Replace(string1, find, replacement, [start], [count], [compare]) edge cases,
// all verified byte-identical to ACE — including where ACE ERRORS rather than clamping/propagating: start < 1,
// negative length, start=0, and a null Replace argument. (Mid propagates null on the string; Replace does not.)
public class MidReplaceEdgeCasesTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "mr-");
        var e = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE T ( K LONG PRIMARY KEY )");
        e.ExecuteNonQuery("INSERT INTO T (K) VALUES (1)");
        return e;
    }

    private static object? Eval(string expr) => Fresh().ExecuteQuery($"SELECT {expr} FROM T").Rows.Single()[0];

    [Theory]
    [InlineData("Mid('abcdef', 2, 3)", "bcd")]
    [InlineData("Mid('abcdef', 2)", "bcdef")]          // length omitted → to end
    [InlineData("Mid('abcdef', 4, 10)", "def")]        // length past end → clamped
    [InlineData("Mid('abcdef', 10)", "")]              // start past end → empty
    [InlineData("Mid('abcdef', 1, 0)", "")]            // zero length
    [InlineData("Replace('abcabc', 'b', 'X')", "aXcaXc")]
    [InlineData("Replace('abcabc', 'b', 'X', 1, 1)", "aXcabc")]  // count = 1
    [InlineData("Replace('abcabc', 'b', 'X', 3)", "caXc")]       // start=3 truncates the prefix
    [InlineData("Replace('ABCabc', 'b', 'X')", "AXCaXc")]        // case-insensitive default
    [InlineData("Replace('abcabc', '', 'X')", "abcabc")]         // empty find → unchanged
    [InlineData("Replace('abcabc', 'b', '')", "acac")]           // empty replacement
    [InlineData("Replace('abcabc', 'b', 'X', 10)", "")]          // start past end
    [InlineData("Replace('abcabc', 'B', 'X', 1, -1, 0)", "abcabc")] // binary (case-sensitive)
    [InlineData("Replace('abcabc', 'B', 'X', 1, -1, 1)", "aXcaXc")] // textual (case-insensitive)
    [InlineData("Replace('abcabc', 'b', 'X', 1, 0)", "abcabc")]  // count = 0
    public void Matches_ace(string expr, string expected)
        => Assert.Equal(expected, Convert.ToString(Eval(expr)));

    [Fact]
    public void Mid_null_string_propagates()
        => Assert.Null(Eval("Mid(Null, 2)"));

    // Where ACE raises an error instead of clamping/propagating.
    [Theory]
    [InlineData("Mid('abcdef', 0, 2)")]                // start < 1 → Invalid procedure call
    [InlineData("Mid('abcdef', 3, -1)")]               // negative length → Invalid procedure call
    [InlineData("Replace(Null, 'b', 'X')")]            // null arg → Data type mismatch
    [InlineData("Replace('abcabc', 'b', 'X', 0)")]     // start < 1 → Invalid procedure call
    public void Error_cases(string expr)
        => Assert.Throws<InvalidOperationException>(() => Eval(expr));
}
