using System.Globalization;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// What a number written with a decimal point or an exponent is: a whole number a Long holds is a Long, a number a
/// Decimal holds exactly is that Decimal, and anything else is a Double. Every expectation was measured against ACE
/// (AceDecimalPrecisionProbeTest.Expression_type_probe and Literal_type_probe).
/// </summary>
public class DecimalLiteralTests(DecimalLiteralTests.Database database)
    : TempDatabaseTest, IClassFixture<DecimalLiteralTests.Database>
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    private static readonly string[] Setup =
    [
        "CREATE TABLE T (Id LONG)",
        "INSERT INTO T (Id) VALUES (1)",
    ];

    public sealed class Database() : SharedDatabase("decimal-literal-", Setup);

    private object? Scalar(string expression) => database.Scalar($"SELECT {expression} FROM T", EnUs);

    [Theory]
    [InlineData("1.5", "1.5")]
    [InlineData("1.5E0", "1.5")]
    [InlineData("15E-1", "1.5")]
    [InlineData(".5", "0.5")]
    [InlineData("-1.5", "-1.5")]
    [InlineData("1.50", "1.5")]
    [InlineData("1E-28", "0.0000000000000000000000000001")]
    [InlineData("1.23456E-20", "0.0000000000000000000123456")]
    [InlineData("3E9", "3000000000")]
    public void A_number_a_decimal_holds_exactly_is_a_decimal(string literal, string expected)
    {
        Assert.Equal(14, Convert.ToInt32(Scalar($"VarType({literal})"), CultureInfo.InvariantCulture));
        Assert.Equal(expected, Scalar($"CStr({literal})"));
    }

    [Theory]
    [InlineData("1E5", 100000)]
    [InlineData("3.0", 3)]
    public void A_whole_number_a_long_holds_is_a_long(string literal, int expected) =>
        Assert.Equal(expected, Assert.IsType<int>(Scalar(literal)));

    [Theory]
    [InlineData("5E-324", 5E-324)]
    [InlineData("1.5E-28", 1.5E-28)]
    [InlineData("1E-29", 1E-29)]
    [InlineData("1E29", 1E29)]
    [InlineData("1E300", 1E300)]
    [InlineData("3000000000.0", 3000000000d)]
    public void Anything_else_is_a_double(string literal, double expected) =>
        Assert.Equal(expected, Assert.IsType<double>(Scalar(literal)));
}
