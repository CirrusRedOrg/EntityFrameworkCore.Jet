using System.Globalization;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// A Currency result is a Currency at every step: four places, half to even, and past ±922,337,203,685,477.5807 an
/// overflow — as CCur works it (verified vs ACE). What counts as a Currency result is ACE's: Currency with Currency or a
/// whole number; not Currency with a written decimal, a Double, or a whole number too big for a Long, which ACE reads
/// as a Decimal.
/// </summary>
public class CurrencyArithmeticTests(CurrencyArithmeticTests.Database database)
    : TempDatabaseTest, IClassFixture<CurrencyArithmeticTests.Database>
{
    // Z is the largest whole Currency.
    private static readonly string[] Setup =
    [
        "CREATE TABLE C (K LONG, A CURRENCY, B CURRENCY, Z CURRENCY, Y CURRENCY, I LONG, D DOUBLE, L BIGINT)",
        "INSERT INTO C (K, A, B, Z, Y, I, D, L) VALUES (1, 1.2345, 0.0003, 922337203685477, 1000000000.5, 3, 1.5, 1000000000000)",
    ];

    public sealed class Database() : SharedDatabase("currency-arith-", Setup);

    private object? Scalar(string expression) =>
        database.Scalar($"SELECT {expression} FROM C", CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("A * A", "1.524")]
    [InlineData("A * B", "0.0004")]
    [InlineData("(A * A) * 1000", "1524")]          // the product is rounded before it is multiplied
    [InlineData("A * A + B", "1.5243")]
    [InlineData("A * I", "3.7035")]
    [InlineData("A - B", "1.2342")]
    public void A_currency_result_keeps_four_places(string expression, string expected) =>
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), Convert.ToDecimal(Scalar(expression)));

    [Theory]
    [InlineData("A + 0.00005", "1.23455")]                          // with a written decimal it is a Decimal
    [InlineData("Z + 0.5808", "922337203685477.5808")]              // ... so past the Currency range is no overflow
    [InlineData("Y * 864000000000", "864000000432000000000")]       // a literal past a Long is a Decimal too
    [InlineData("Z + 1000000000000", "923337203685477")]
    public void A_currency_with_a_decimal_is_not_a_currency(string expression, string expected) =>
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), Convert.ToDecimal(Scalar(expression)));

    [Theory]
    [InlineData("Z + 1")]
    [InlineData("-Z - 1")]
    [InlineData("Z * I")]
    [InlineData("Z + Z")]
    [InlineData("Y * Y")]
    [InlineData("Y * L")]
    [InlineData("L * Y")]
    public void Past_the_currency_range_is_an_overflow(string expression) =>
        Assert.Throws<OverflowException>(() => Scalar(expression));

    // The literal's type decides the places as well: ACE takes 864000000000 / 7 to a Decimal of none.
    [Fact]
    public void A_literal_past_a_long_has_no_places() =>
        Assert.Equal(123428571428m, Convert.ToDecimal(Scalar("864000000000 / 7")));

    // Its value is still the Int64 LibRed reads it as, so MOD and \ stay exact over it.
    [Fact]
    public void A_literal_past_a_long_is_still_an_int64() =>
        Assert.Equal(136000000000L, Scalar("L MOD 864000000000"));
}
