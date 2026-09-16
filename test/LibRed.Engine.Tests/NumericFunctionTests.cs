using System.Globalization;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// The numeric functions <c>Abs</c> <c>Int</c> <c>Fix</c> <c>Sgn</c> <c>Round</c> <c>Sqr</c> <c>Exp</c> <c>Log</c>
/// <c>Sin</c> <c>Cos</c> <c>Tan</c> <c>Atn</c> <c>Rnd</c>: how each reads its argument, its domain and its rounding.
/// The expected values were measured against ACE, except that a Null argument gives Null where some of these
/// functions raise an error in ACE, and that Sin and Cos can differ from ACE's in the last binary digit.
/// </summary>
public class NumericFunctionTests(NumericFunctionTests.Database database)
    : TempDatabaseTest, IClassFixture<NumericFunctionTests.Database>
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    private static readonly string[] Setup =
    [
        "CREATE TABLE T (Id LONG, NT TEXT(60), DT DATETIME, G GUID)",
        "INSERT INTO T (Id, DT) VALUES (1, #2020-01-02 12:00:00#)",
        "UPDATE T SET G = {00112233-4455-6677-8899-AABBCCDDEEFF}",
    ];

    // Every Rnd case here reseeds with a negative argument, so the generator state the shared database carries from
    // one test to the next never shows.
    public sealed class Database() : SharedDatabase("numeric-functions-", Setup);

    // Text is read in the regional format, so each query runs under en-US whatever the machine's culture.
    private object? Scalar(string expression) => database.Scalar($"SELECT {expression} FROM T", EnUs);

    private double Number(string expression) =>
        Convert.ToDouble(Scalar(expression), CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("INT(99.8)", 99)]
    [InlineData("FIX(99.2)", 99)]
    [InlineData("INT(-99.8)", -100)]
    [InlineData("FIX(-99.8)", -99)]
    [InlineData("INT(-8.4)", -9)]
    [InlineData("FIX(-8.4)", -8)]
    [InlineData("INT(TRUE)", -1)]
    [InlineData("FIX(TRUE)", -1)]
    [InlineData("INT('$5')", 5)]
    [InlineData("FIX('1e2')", 100)]
    [InlineData("ABS('-2.5')", 2.5)]
    [InlineData("ABS('$5')", 5)]
    [InlineData("ABS(DT)", 43832.5)]
    [InlineData("ABS(#1899-12-29 06:00#)", 1.25)]
    [InlineData("ABS(-2147483648)", 2147483648)]
    [InlineData("ABS(CLNG(-2147483648))", 2147483648)]
    [InlineData("SGN(TRUE)", -1)]
    [InlineData("SGN(DT)", 1)]
    [InlineData("SGN(#1899-12-29 06:00#)", -1)]
    [InlineData("SGN('$5')", 1)]
    [InlineData("SGN(-2.4)", -1)]
    [InlineData("SGN(0)", 0)]
    public void Abs_int_fix_and_sgn_read_their_argument_as_a_number(string expression, double expected) =>
        Assert.Equal(expected, Number(expression));

    [Theory]
    [InlineData("ABS('2.5')")]
    [InlineData("INT('$5')")]
    [InlineData("ABS(DT)")]
    [InlineData("ABS(-2147483648)")]
    public void Abs_int_and_fix_give_a_double_for_text_dates_and_a_long_past_its_range(string expression) =>
        Assert.IsType<double>(Scalar(expression));

    [Theory]
    [InlineData("INT(DT)", "2020-01-02 00:00:00")]
    [InlineData("FIX(DT)", "2020-01-02 00:00:00")]
    [InlineData("INT(#1899-12-29 06:00#)", "1899-12-28 00:00:00")]
    [InlineData("FIX(#1899-12-29 06:00#)", "1899-12-29 00:00:00")]
    public void Int_and_fix_of_a_date_give_a_date(string expression, string expected) =>
        Assert.Equal(expected, Assert.IsType<DateTime>(Scalar(expression)).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

    [Theory]
    [InlineData("ROUND(2.5)", 2)]
    [InlineData("ROUND(3.5)", 4)]
    [InlineData("ROUND(-2.5)", -2)]
    [InlineData("ROUND(0.12345, 4)", 0.1234)]
    [InlineData("ROUND(0.12355, 4)", 0.1236)]
    [InlineData("ROUND(0.12365, 4)", 0.1236)]
    [InlineData("ROUND(2.675, 2)", 2.68)]
    [InlineData("ROUND(CDBL(2.675), 2)", 2.68)]
    [InlineData("ROUND(CDBL(0.12345), 4)", 0.1234)]
    [InlineData("ROUND(CDBL(1.005), 2)", 1)]
    [InlineData("ROUND('2.55', 1)", 2.6)]
    [InlineData("ROUND(1.5, 1.6)", 1.5)]
    [InlineData("ROUND(1.25, '1')", 1.2)]
    [InlineData("ROUND(1234.5678, 20)", 1234.5678)]
    [InlineData("ROUND(1.5, 400)", 1.5)]
    [InlineData("ROUND(1.23456789012345678, 15)", 1.234567890123457)]
    [InlineData("ROUND(1E20 / 3, 2)", 1E20 / 3)]
    [InlineData("ROUND(1E300, 2)", 1E300)]
    [InlineData("ROUND(TRUE)", -1)]
    [InlineData("ROUND(DT)", 43832)]
    [InlineData("ROUND(DT, 1)", 43832.5)]
    [InlineData("ROUND(CCUR(1.23456), 2)", 1.23)]
    [InlineData("ROUND(CINT(7), 1)", 7)]
    public void Round_rounds_the_decimal_form_half_to_even(string expression, double expected) =>
        Assert.Equal(expected, Number(expression));

    [Theory]
    [InlineData("ROUND(NULL)")]
    [InlineData("ROUND(2.5, NULL)")]
    [InlineData("ROUND(NT)")]
    [InlineData("ABS(NULL)")]
    [InlineData("INT(NT)")]
    [InlineData("SGN(NULL)")]
    [InlineData("SQR(NULL)")]
    [InlineData("LOG(NT)")]
    [InlineData("SIN(NULL)")]
    [InlineData("RND(NULL)")]
    public void Null_gives_null(string expression) =>
        Assert.Null(Scalar(expression));

    [Theory]
    [InlineData("SQR(DT)", 209.3621264699038)]
    [InlineData("SQR('$5')", 2.23606797749979)]
    [InlineData("EXP(TRUE)", 0.36787944117144233)]
    [InlineData("EXP('$5')", 148.4131591025766)]
    [InlineData("EXP(#1899-12-29 06:00#)", 0.2865047968601901)]
    [InlineData("LOG(DT)", 10.688130830344283)]
    [InlineData("LOG('$5')", 1.6094379124341003)]
    [InlineData("SIN(TRUE)", -0.8414709848078965)]
    [InlineData("SIN(DT)", 0.8410910067004128)]
    [InlineData("COS(DT)", 0.5408936295129442)]
    [InlineData("TAN(DT)", 1.5550026119882128)]
    [InlineData("TAN(1.5)", 14.10141994717172)]
    [InlineData("TAN(8)", -6.799711455220378)]
    [InlineData("TAN(3000000000)", -6.142282632313941)]
    [InlineData("TAN(40000)", 2.93421082407435)]
    [InlineData("ATN(TRUE)", -0.7853981633974483)]
    [InlineData("ATN(DT)", 1.5707735126729592)]
    [InlineData("SIN(9.2233719999E18)", 0.9588847752004629)]
    public void Math_functions_read_their_argument_as_a_number(string expression, double expected) =>
        Assert.Equal(expected, Number(expression));

    [Theory]
    [InlineData("SQR(-1)")]
    [InlineData("SQR(TRUE)")]
    [InlineData("SQR(CCUR(-3.25))")]
    [InlineData("LOG(0)")]
    [InlineData("LOG(FALSE)")]
    [InlineData("LOG(TRUE)")]
    [InlineData("LOG(-0.5)")]
    [InlineData("SIN(9.223372E18)")]
    [InlineData("COS(-1E300)")]
    [InlineData("TAN(1E19)")]
    [InlineData("ROUND(15, -1)")]
    public void A_number_outside_the_domain_is_an_invalid_procedure_call(string expression) =>
        Assert.Throws<ArgumentException>(() => Scalar(expression));

    [Theory]
    [InlineData("EXP(710)")]
    [InlineData("EXP(40000)")]
    [InlineData("RND(-1E300)")]
    public void A_result_past_a_double_is_an_overflow(string expression) =>
        Assert.Throws<OverflowException>(() => Scalar(expression));

    [Theory]
    [InlineData("ABS('abc')")]
    [InlineData("SQR(G)")]
    [InlineData("ROUND('abc')")]
    [InlineData("ROUND(2.5, 'x')")]
    [InlineData("RND('abc')")]
    public void A_value_that_is_not_a_number_is_a_type_mismatch(string expression) =>
        Assert.Throws<InvalidCastException>(() => Scalar(expression));

    [Theory]
    [InlineData("RND(-1)", 0.2240070104598999)]
    [InlineData("RND(TRUE)", 0.2240070104598999)]
    [InlineData("RND('-1')", 0.2240070104598999)]
    public void Rnd_with_a_negative_argument_reseeds(string expression, double expected) =>
        Assert.Equal(expected, Number(expression));
}
