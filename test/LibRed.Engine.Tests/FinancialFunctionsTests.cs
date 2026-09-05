using System.Globalization;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// Financial, FormatX, and colour functions — all exposed by the ACE JES and now implemented in LibRed. Expected
// values are exactly what ACE returned. Culture pinned to en-US for the locale-sensitive FormatX cases.
public class FinancialFunctionsTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "fin-");
        var e = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE T ( K LONG PRIMARY KEY )");
        e.ExecuteNonQuery("INSERT INTO T (K) VALUES (1)");
        return e;
    }

    private static object? Eval(string expr) => Fresh().ExecuteQuery($"SELECT {expr} FROM T").Rows.Single()[0];

    private static object? EvalEnUs(string expr)
    {
        var prev = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try { return Eval(expr); } finally { CultureInfo.CurrentCulture = prev; }
    }

    [Theory]
    [InlineData("Pmt(0.05/12, 60, 10000)", -188.7123364401099)]
    [InlineData("FV(0.05/12, 60, -100)", 6800.608284084284)]
    [InlineData("PV(0.05/12, 60, -100)", 5299.070632392715)]
    [InlineData("IPmt(0.05/12, 1, 60, 10000)", -41.666666666666664)]
    [InlineData("PPmt(0.05/12, 1, 60, 10000)", -147.04566977344325)]
    [InlineData("NPer(0.05/12, -200, 10000)", 56.18429076143198)]
    [InlineData("Rate(60, -200, 10000)", 0.006183413161266263)]
    [InlineData("DDB(10000, 1000, 5, 1)", 4000)]
    [InlineData("SLN(10000, 1000, 5)", 1800)]
    [InlineData("SYD(10000, 1000, 5, 1)", 3000)]
    public void Financial_matches_ace(string expr, double expected)
        => Assert.Equal(expected, Convert.ToDouble(Eval(expr)), 6);

    [Theory]
    [InlineData("RGB(255, 0, 0)", 255)]
    [InlineData("RGB(0, 255, 0)", 65280)]
    [InlineData("RGB(0, 0, 255)", 16711680)]
    [InlineData("QBColor(4)", 128)]
    [InlineData("QBColor(1)", 8388608)]
    [InlineData("QBColor(0)", 0)]
    public void Colour_matches_ace(string expr, int expected)
        => Assert.Equal(expected, Convert.ToInt32(Eval(expr)));

    [Theory]
    [InlineData("FormatCurrency(1234.5)", "$1,234.50")]
    [InlineData("FormatNumber(1234.5)", "1,234.50")]
    [InlineData("FormatPercent(0.25)", "25.00%")]
    [InlineData("FormatDateTime(#2020-06-15#)", "6/15/2020")]   // en-US General Date (date-only at midnight)
    public void FormatX_matches_ace_under_en_us(string expr, string expected)
        => Assert.Equal(expected, Convert.ToString(EvalEnUs(expr)));
}
