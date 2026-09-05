using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// Val (whitespace-stripping + &H/&O prefixes) and Rnd (VBA 24-bit LCG) — verified byte-identical to ACE.
public class RndValFunctionTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "rv-");
        var e = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE T ( K LONG PRIMARY KEY )");
        e.ExecuteNonQuery("INSERT INTO T (K) VALUES (1)");
        return e;
    }

    private static object? Eval(string expr) => Fresh().ExecuteQuery($"SELECT {expr} FROM T").Rows.Single()[0];

    [Theory]
    [InlineData("Val('12abc')", 12)]
    [InlineData("Val('  12 34 ')", 1234)]   // internal whitespace stripped
    [InlineData("Val('&HFF')", 255)]        // hex prefix
    [InlineData("Val('&O10')", 8)]          // octal prefix
    [InlineData("Val('1E2')", 100)]         // exponent
    [InlineData("Val('   ')", 0)]
    [InlineData("Val('abc')", 0)]
    [InlineData("Val('3 .1 4')", 3.14)]     // spaces inside the number
    [InlineData("Val('  -  5')", -5)]       // spaces around the sign
    [InlineData("Val('12.3.4')", 12.3)]     // stops at the second dot
    [InlineData("Val('-3.14')", -3.14)]
    public void Val_matches_ace(string expr, double expected)
        => Assert.Equal(expected, Convert.ToDouble(Eval(expr)), 10);

    [Fact]
    public void Rnd_negative_reseeds_to_aces_exact_value()
        // VBA's LCG: Rnd(-1) reseeds deterministically to the same value ACE produces.
        => Assert.Equal(0.2240070104598999, Convert.ToDouble(Eval("Rnd(-1)")), 15);

    [Fact]
    public void Rnd_negative_is_deterministic_within_a_query()
    {
        var e = Fresh();
        var row = e.ExecuteQuery("SELECT Rnd(-1) AS a, Rnd(-1) AS b FROM T").Rows.Single();
        Assert.Equal(Convert.ToDouble(row[0]), Convert.ToDouble(row[1]));
    }

    [Theory]
    [InlineData("Rnd()")]
    [InlineData("Rnd(1)")]
    public void Rnd_is_in_the_unit_interval(string expr)
    {
        double r = Convert.ToDouble(Eval(expr));
        Assert.InRange(r, 0.0, 1.0);
    }
}
