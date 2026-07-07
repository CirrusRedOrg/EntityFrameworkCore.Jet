using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// InStr([start,] string1, string2 [, compare]) — the documented edge cases, each verified byte-identical to ACE:
// case-insensitive default, not-found → 0, empty/null args, start beyond length, and the compare modes.
public class InstrEdgeCasesTests
{
    private static QueryEngine Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"instr-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE T ( K LONG PRIMARY KEY )");
        e.ExecuteNonQuery("INSERT INTO T (K) VALUES (1)");
        return e;
    }

    private static object? Eval(string expr) => Fresh().ExecuteQuery($"SELECT {expr} FROM T").Rows.Single()[0];

    [Theory]
    [InlineData("InStr('abc', 'b')", 2)]
    [InlineData("InStr('ABC', 'b')", 2)]           // case-insensitive by default
    [InlineData("InStr('abc', 'x')", 0)]           // not found
    [InlineData("InStr('', 'a')", 0)]              // string1 zero-length
    [InlineData("InStr('abc', '')", 1)]            // string2 zero-length → start (default 1)
    [InlineData("InStr(2, 'abc', '')", 2)]         // string2 zero-length → start
    [InlineData("InStr(10, 'abc', 'a')", 0)]       // start > len(string1)
    [InlineData("InStr(2, 'abcabc', 'a')", 4)]     // from a start position
    [InlineData("InStr(1, 'aXbXc', 'x', 0)", 0)]   // binary (case-sensitive)
    [InlineData("InStr(1, 'aXbXc', 'x', 1)", 2)]   // textual (case-insensitive)
    public void Instr_matches_ace(string expr, int expected)
        => Assert.Equal(expected, Convert.ToInt32(Eval(expr)));

    [Theory]
    [InlineData("InStr(Null, 'a')")]               // string1 null → null
    [InlineData("InStr('abc', Null)")]             // string2 null → null
    public void Instr_propagates_null(string expr)
        => Assert.Null(Eval(expr));
}
