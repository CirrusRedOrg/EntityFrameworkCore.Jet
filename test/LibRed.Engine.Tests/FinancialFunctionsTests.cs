using System.Globalization;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// Financial, FormatX, and colour functions — all exposed by the ACE JES and now implemented in LibRed. Expected
// values are exactly what ACE returned. Culture pinned to en-US for the locale-sensitive FormatX cases.
public class FinancialFunctionsTests(FinancialFunctionsTests.Database database)
    : TempDatabaseTest, IClassFixture<FinancialFunctionsTests.Database>
{
    private static readonly string[] Setup =
    [
        "CREATE TABLE T ( K LONG PRIMARY KEY )",
        "INSERT INTO T (K) VALUES (1)",
    ];

    public sealed class Database() : SharedDatabase("fin-", Setup);

    private object? Eval(string expr) => database.Engine.ExecuteQuery($"SELECT {expr} FROM T").Rows.Single()[0];

    private object? EvalEnUs(string expr) => database.Scalar($"SELECT {expr} FROM T", CultureInfo.GetCultureInfo("en-US"));

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

    // Exact to the last bit, as the VBA runtime's order of arithmetic gives them. Rate(60, -100, 10000) is the one
    // probed case that still differs, in its last digits (-0.01544514669212213 vs ACE's -0.015445146692122057),
    // most likely from the platform's Pow.
    [Theory]
    [InlineData("NPER(0.05/12, -200, 10000)", 56.18429076143198)]
    [InlineData("FV(-0.05, 60, -200)", 3815.720804052192)]
    [InlineData("PV(-0.05, 60, -200)", 82824.77649040522)]
    [InlineData("FV(0.01, 60, -200, -1000, 1)", 18313.970009558736)]
    [InlineData("PV(0.01, 60, -200, 1000, 1)", 8530.468142119496)]
    [InlineData("PMT(0.01, 60, 10000, 500, 1)", -226.3036640509589)]
    [InlineData("PMT(0.01, 60, 10000, 0, 2)", -220.24205628615604)]
    [InlineData("PMT(0.01, 60, 10000, 0, -1)", -220.24205628615604)]
    [InlineData("PMT(0.01, 60, 10000, 0, '1')", -220.24205628615604)]
    [InlineData("PMT(0.01, 60, 10000, 0, 0.5)", -222.44447684901763)]
    [InlineData("PMT(0.01, -60, 10000)", 122.44447684901762)]
    [InlineData("PMT(TRUE, 60, 10000)", -0.0)]
    [InlineData("PMT('0.01', 60, 10000)", -222.44447684901763)]
    [InlineData("NPER(0.01, -200, 10000, 0, 1)", 68.67056927050619)]
    [InlineData("NPER(0.01, 200, 10000)", -40.748907156094134)]
    [InlineData("NPER(0.01, -200, 0)", 0.0)]
    [InlineData("IPMT(0.01, 2, 60, 10000, 0, 1)", -97.79757943713845)]
    [InlineData("IPMT(0.01, 1, 60, 10000, 0, 1)", 0.0)]
    [InlineData("IPMT(0.01, 60.5, 60, 10000)", -1.1039496274401608)]
    [InlineData("PPMT(0.01, 1, 60, 10000, 0, 1)", -220.24205628615604)]
    [InlineData("PPMT(0.01, 60, 60, 10000, 500, 1)", -228.96451396085524)]
    [InlineData("RATE(60, -200, 10000)", 0.006183413161266263)]
    [InlineData("RATE(60, -200, 10000, 0, 1)", 0.006407985777795751)]
    [InlineData("RATE(60, -200, 10000, 0, 0, 0)", 0.0061834131612155535)]
    [InlineData("RATE(1, -200, 100)", 0.9999999999999825)]
    [InlineData("RATE(12, -1000, 10000, 0, 0, 0.9)", 0.029228540769573003)]
    [InlineData("SLN(10000, 1000, TRUE)", -9000.0)]
    [InlineData("SYD(10000, 1000, 5.5, 1)", 2769.230769230769)]
    [InlineData("SYD(-10000, 1000, 5, 1)", -3666.6666666666665)]
    [InlineData("SYD(10000, 1000, 5, 2.5)", 2100.0)]
    [InlineData("DDB(10000, 1000, 5, 5)", 295.9999999999985)]
    [InlineData("DDB(10000, 1000, 5, 1.5)", 3098.3866769659335)]
    [InlineData("DDB(10000, 1000, 5, 0.5)", 4000.0)]
    [InlineData("DDB(10000, 1000, 3, 1)", 6666.666666666667)]
    [InlineData("DDB(10000, 20000, 5, 1)", -10000.0)]
    [InlineData("DDB(10000, 1000, 5, 1, 10)", 9000.0)]
    [InlineData("DDB(10000, 1000, 5, 2, 5)", 0.0)]
    [InlineData("DDB(-10000, 1000, 5, 1)", 0.0)]
    [InlineData("PV(-1, 60, -200)", double.PositiveInfinity)]
    [InlineData("PMT(10, 1000, 10000)", double.NaN)]
    public void Financial_matches_ace_exactly(string expr, double expected)
        => Assert.Equal(expected, Assert.IsType<double>(Eval(expr)));

    [Theory]
    [InlineData("PMT(0.01, 0, 10000)")]
    [InlineData("NPER(-1, -200, 10000)")]
    [InlineData("NPER(0, 0, 10000)")]
    [InlineData("NPER(0.01, 0, 10000)")]
    [InlineData("NPER(0.01, -50, 10000)")]
    [InlineData("NPER(0.5, -200, 10000)")]
    [InlineData("IPMT(0.01, 0, 60, 10000)")]
    [InlineData("IPMT(0.01, 61, 60, 10000)")]
    [InlineData("IPMT(0.01, 2, 0, 10000)")]
    [InlineData("PPMT(0.01, 61, 60, 10000)")]
    [InlineData("RATE(0, -200, 10000)")]
    [InlineData("RATE(60, 200, 10000)")]
    [InlineData("RATE(60, 0, 10000)")]
    [InlineData("RATE(60, -200, 10000, 0, 0, 10)")]
    [InlineData("SLN(10000, 1000, 0)")]
    [InlineData("SYD(10000, 1000, 5, 6)")]
    [InlineData("SYD(10000, 1000, 5, 0)")]
    [InlineData("SYD(10000, -1000, 5, 1)")]
    [InlineData("DDB(10000, 1000, 5, 6)")]
    [InlineData("DDB(10000, 1000, 5, 5.5)")]
    [InlineData("DDB(10000, 1000, 5, 0)")]
    [InlineData("DDB(10000, 1000, 5, 1, 0)")]
    [InlineData("DDB(10000, 1000, 0, 1)")]
    [InlineData("DDB(10000, -1000, 5, 1)")]
    public void Out_of_range_arguments_are_an_invalid_procedure_call(string expr)
        => Assert.Throws<ArgumentException>(() => Eval(expr));

    [Theory]
    [InlineData("PMT(0.01, 60, 'abc')")]
    public void Text_that_is_not_a_number_is_a_type_mismatch(string expr)
        => Assert.Throws<InvalidCastException>(() => Eval(expr));

    // ACE raises "Data type mismatch" or "Invalid use of Null"; LibRed returns Null.
    [Theory]
    [InlineData("PMT(NULL, 60, 10000)")]
    [InlineData("PMT(0.01, 60, 10000, NULL)")]
    [InlineData("IPMT(0.01, NULL, 60, 10000)")]
    [InlineData("RATE(60, -200, 10000, 0, 0, NULL)")]
    [InlineData("SLN(NULL, 1000, 5)")]
    [InlineData("DDB(10000, 1000, 5, 1, NULL)")]
    public void A_null_argument_gives_null(string expr)
        => Assert.Null(Eval(expr));

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
