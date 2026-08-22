using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// StrComp(a, b, [compare]) and the byte-reinterpretation StrConv modes (64/128) — verified byte-identical to ACE.
public class StrCompStrConvTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "scv-");
        var e = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE T ( K LONG PRIMARY KEY )");
        e.ExecuteNonQuery("INSERT INTO T (K) VALUES (1)");
        return e;
    }

    private static object? Eval(string expr) => Fresh().ExecuteQuery($"SELECT {expr} FROM T").Rows.Single()[0];

    [Theory]
    [InlineData("StrComp('a', 'a')", 0)]
    [InlineData("StrComp('a', 'b')", -1)]
    [InlineData("StrComp('b', 'a')", 1)]
    [InlineData("StrComp('a', 'A')", 0)]           // default = textual (case-insensitive)
    [InlineData("StrComp('a', 'A', 0)", 1)]        // binary: 'a'(97) > 'A'(65)
    [InlineData("StrComp('a', 'A', 1)", 0)]        // textual
    [InlineData("StrComp('A', 'a', 0)", -1)]       // binary
    [InlineData("StrComp('Apple', 'apple', 0)", -1)]
    [InlineData("StrComp('Apple', 'apple', 1)", 0)]
    [InlineData("StrComp('abc', 'abd')", -1)]
    public void StrComp_matches_ace(string expr, int expected)
        => Assert.Equal(expected, Convert.ToInt32(Eval(expr)));

    [Theory]
    [InlineData("StrComp(Null, 'a')")]
    [InlineData("StrComp('a', Null)")]
    public void StrComp_propagates_null(string expr)
        => Assert.Null(Eval(expr));

    [Fact]
    public void StrConv_vbUnicode_64_reinterprets_utf16_bytes()
        // each ANSI byte of 'hello' becomes one char, doubling the length with null chars
        => Assert.Equal("h\0e\0l\0l\0o\0", Convert.ToString(Eval("StrConv('hello', 64)")));

    [Fact]
    public void StrConv_vbFromUnicode_128_combines_char_pairs()
        // (h,e)->U+6568, (l,l)->U+6C6C, trailing 'o' dropped
        => Assert.Equal("敨汬", Convert.ToString(Eval("StrConv('hello', 128)")));

    [Fact]
    public void StrConv_mode_4_is_rejected() // narrow->wide errors in the JES
        => Assert.Throws<InvalidOperationException>(() => Eval("StrConv('hello', 4)"));
}
