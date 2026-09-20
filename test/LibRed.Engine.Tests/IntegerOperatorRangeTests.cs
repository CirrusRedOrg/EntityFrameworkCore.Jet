using System.Globalization;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// <c>MOD</c> and <c>\</c> over a Double, Decimal or Currency past a Long. ACE squeezes each operand into a Long
/// first and overflows; LibRed works the answer out in Int64 and needs only the result to fit its type — Int32
/// unless an operand is an Int64 — so a remainder, always below its divisor, always does. A LibRed extension.
/// </summary>
public class IntegerOperatorRangeTests(IntegerOperatorRangeTests.Database database)
    : TempDatabaseTest, IClassFixture<IntegerOperatorRangeTests.Database>
{
    private static readonly string[] Setup =
    [
        "CREATE TABLE R (K LONG, D DOUBLE, M DECIMAL(20,0), Y CURRENCY, I LONG, L BIGINT)",
        "INSERT INTO R (K, D, M, Y, I, L) VALUES (1, 1000000000000, 1000000000000, 922337203685477, 7, 1000000000000)",
    ];

    public sealed class Database() : SharedDatabase("integer-range-", Setup);

    private (Type Declared, object? Value) Query(string expression)
    {
        var (types, rows) = database.Query($"SELECT {expression} FROM R", CultureInfo.InvariantCulture);
        return (types[0], rows.Single()[0]);
    }

    [Theory]
    [InlineData("D MOD 7", 1)]
    [InlineData("M MOD 7", 1)]
    [InlineData("Y MOD 7", (int)(922337203685477L % 7))]
    [InlineData("D MOD I", 1)]
    [InlineData("7 MOD D", 7)]                      // the divisor past a Long, too
    [InlineData("-D MOD 7", -1)]                    // the remainder takes the dividend's sign
    [InlineData("D \\ 1000000", 1000000)]           // a quotient that fits
    [InlineData("Y \\ 1000000000", 922337)]
    public void The_answer_is_an_int32_when_it_fits_one(string expression, int expected)
    {
        var (declared, value) = Query(expression);
        Assert.Equal(typeof(int), declared);
        Assert.Equal(expected, value);
    }

    // The column's type is settled before any value is read, so a quotient past a Long cannot become an Int64.
    [Theory]
    [InlineData("D \\ 7")]
    [InlineData("M \\ 2")]
    public void A_quotient_past_a_long_is_an_overflow(string expression) =>
        Assert.Throws<OverflowException>(() => Query(expression));

    [Theory]
    [InlineData("L MOD 7", 1L)]
    [InlineData("D MOD 864000000000", 136000000000L)]
    [InlineData("L \\ 7", 142857142857L)]
    public void With_an_int64_operand_it_is_an_int64(string expression, long expected)
    {
        var (declared, value) = Query(expression);
        Assert.Equal(typeof(long), declared);
        Assert.Equal(expected, value);
    }
}
