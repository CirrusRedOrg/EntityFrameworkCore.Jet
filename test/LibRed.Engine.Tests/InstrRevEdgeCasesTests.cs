using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// InStrRev(string1, string2, [start=-1], [compare]) — note the argument order differs from InStr (start is 3rd,
// default -1 = end) and start bounds the search to Left(string1, start). All values verified byte-identical to
// ACE, including its quirks (empty needle → start position; NULL → error, not NULL; start=0 → error).
public class InstrRevEdgeCasesTests
{
    private static QueryEngine Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"irev-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE T ( K LONG PRIMARY KEY )");
        e.ExecuteNonQuery("INSERT INTO T (K) VALUES (1)");
        return e;
    }

    private static object? Eval(string expr) => Fresh().ExecuteQuery($"SELECT {expr} FROM T").Rows.Single()[0];

    [Theory]
    [InlineData("InStrRev('abcabc', 'a')", 4)]          // last occurrence
    [InlineData("InStrRev('abcabc', 'a', -1)", 4)]      // explicit end
    [InlineData("InStrRev('abcabc', 'a', 6)", 4)]
    [InlineData("InStrRev('abcabc', 'a', 4)", 4)]       // window = first 4 chars
    [InlineData("InStrRev('abcabc', 'a', 3)", 1)]       // window = first 3 chars
    [InlineData("InStrRev('abcabc', 'bc', 5)", 2)]      // 'bc' at 5-6 ends past 5 → excluded
    [InlineData("InStrRev('abcabc', 'bc', 4)", 2)]
    [InlineData("InStrRev('ABCABC', 'a')", 4)]          // case-insensitive default
    [InlineData("InStrRev('abc', 'x')", 0)]             // not found
    [InlineData("InStrRev('', 'a')", 0)]                // string1 empty
    [InlineData("InStrRev('abc', '')", 3)]              // empty needle → effective start (len)
    [InlineData("InStrRev('aXbXc', 'x', -1, 0)", 0)]    // binary (case-sensitive)
    [InlineData("InStrRev('aXbXc', 'x', -1, 1)", 4)]    // textual (case-insensitive)
    public void InstrRev_matches_ace(string expr, int expected)
        => Assert.Equal(expected, Convert.ToInt32(Eval(expr)));

    // ACE raises errors here rather than propagating NULL (unlike InStr) or returning a position.
    [Theory]
    [InlineData("InStrRev(Null, 'a')")]        // Data type mismatch
    [InlineData("InStrRev('abc', Null)")]      // Data type mismatch
    [InlineData("InStrRev('abcabc', 'a', 0)")] // Invalid procedure call (start must be -1 or >= 1)
    public void InstrRev_error_cases(string expr)
        => Assert.Throws<InvalidOperationException>(() => Eval(expr));
}
