using LibRed.Storage.Types;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// A CURRENCY is decoded by building the decimal from its scaled integer, not by dividing it by 10,000 — and
/// must come out as exactly what the division gave, scale included. The scale is invisible to <c>==</c> but not
/// to the value's text, so these compare the decimals' bits.
/// </summary>
public class CurrencyDecodeTests
{
    public static TheoryData<long> Edges =>
    [
        0, 1, -1, 9, 10, 99, 100, 999, 1000, 9999, 10000, 10001, 15000, -15000, 123450000, -123450000,
        100000000, 1234567, -1234567, 922337203685477, long.MaxValue, long.MinValue, long.MaxValue - 1,
        long.MinValue + 1, long.MaxValue / 10 * 10, long.MinValue / 10 * 10, 4_294_967_296, -4_294_967_296,
    ];

    [Theory]
    [MemberData(nameof(Edges))]
    public void Matches_division_at_the_edges(long raw) => AssertSameAsDivision(raw);

    [Fact]
    public void Matches_division_across_a_dense_range_and_a_random_sweep()
    {
        for (long raw = -200_000; raw <= 200_000; raw++)
            AssertSameAsDivision(raw);

        var random = new Random(20260927);
        for (int i = 0; i < 200_000; i++)
            AssertSameAsDivision(random.NextInt64(long.MinValue, long.MaxValue));
    }

    private static void AssertSameAsDivision(long raw)
    {
        decimal expected = raw / 10000m;
        decimal actual = JetTypeCodec.CurrencyFromScaled(raw);
        Assert.Equal(decimal.GetBits(expected), decimal.GetBits(actual));
    }
}
