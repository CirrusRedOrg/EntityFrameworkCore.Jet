using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// VBA string-function variants, verified against ACE's expression service:
//   $ = String-returning alias (same value; the grammar allows a trailing '$', the evaluator strips it)
//   B = byte-based (UTF-16, 2 bytes/char): LenB('abc')=6, InStrB(1,'abc','b')=3, LeftB('abc',2)='a'
//   W = wide/Unicode code point: ChrW(233)='é'
// Also covers base Asc(), which needed a grammar fix (ASC is a reserved keyword).
public class FunctionVariantTests
{
    private static QueryEngine Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"fv-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE T ( K LONG PRIMARY KEY )");
        e.ExecuteNonQuery("INSERT INTO T (K) VALUES (1)");
        return e;
    }

    private static string Eval(string expr) => Fresh().ExecuteQuery($"SELECT {expr} FROM T").Rows.Single()[0]?.ToString()!;

    [Theory]
    // base / grammar-unblocked
    [InlineData("Asc('A')", "65")]
    // byte variants
    [InlineData("AscB('A')", "65")]
    [InlineData("LenB('abc')", "6")]
    [InlineData("LeftB('abc', 2)", "a")]
    [InlineData("RightB('abc', 2)", "c")]
    [InlineData("MidB('abc', 1, 2)", "a")]
    [InlineData("InStrB(1, 'abc', 'b')", "3")]
    // wide variants
    [InlineData("AscW('A')", "65")]
    [InlineData("ChrW(65)", "A")]
    [InlineData("ChrW(233)", "é")]
    // $ variants (string-returning aliases)
    [InlineData("Chr$(65)", "A")]
    [InlineData("UCase$('ab')", "AB")]
    [InlineData("Left$('abc', 2)", "ab")]
    [InlineData("Mid$('abc', 2)", "bc")]
    [InlineData("Space$(3)", "   ")]
    [InlineData("String$(3, 'x')", "xxx")]
    [InlineData("Hex$(255)", "FF")]
    public void Variant_matches_ace(string expr, string expected)
        => Assert.Equal(expected, Eval(expr));

    // The '$' grammar tweak must not disturb ORDER BY ... ASC (ASC is now also a function name, but only when
    // followed by '(').
    [Fact]
    public void Order_by_asc_still_parses()
    {
        var e = Fresh();
        e.ExecuteNonQuery("INSERT INTO T (K) VALUES (2)");
        var ks = e.ExecuteQuery("SELECT K FROM T ORDER BY K ASC").Rows.Select(r => Convert.ToInt32(r[0])).ToArray();
        Assert.Equal(new[] { 1, 2 }, ks);
    }
}
