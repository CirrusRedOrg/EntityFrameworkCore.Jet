using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// DATEDIFF("ms", a, b) — a LibRed extension. ACE's interval list stops at "s", but LibRed stores the full OA
// double instead of truncating to whole seconds, so a millisecond difference is both meaningful and exact.
//
// It returns Int64, unlike every other interval: a millisecond count overflows Int32 after about 25 days, and
// DateTimeOffset.ToUnixTimeMilliseconds — which is what emits this — spans decades.
public class DateDiffMillisecondTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "ddms-");
        return new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
    }

    private static object? Eval(QueryEngine engine, string expression) =>
        engine.ExecuteQuery($"SELECT {expression} FROM `Shippers` WHERE `ShipperID` = 1").Rows.Single()[0];

    [Theory]
    [InlineData("#2020-01-01 00:00:00#", "#2020-01-01 00:00:01#", 1_000L)]
    [InlineData("#2020-01-01 00:00:00#", "#2020-01-01 00:01:00#", 60_000L)]
    [InlineData("#2020-01-01 00:00:00#", "#2020-01-02 00:00:00#", 86_400_000L)]
    [InlineData("#2020-01-02 00:00:00#", "#2020-01-01 00:00:00#", -86_400_000L)]
    [InlineData("#2020-01-01 00:00:00#", "#2020-01-01 00:00:00#", 0L)]
    public void Counts_whole_milliseconds(string from, string to, long expected)
        => Assert.Equal(expected, Convert.ToInt64(Eval(Fresh(), $"DATEDIFF('ms', {from}, {to})")));

    // The reason it is Int64: a millisecond span overflows Int32 after ~25 days, and the caller that emits
    // this interval measures from 1970.
    [Fact]
    public void Spans_beyond_int32()
    {
        long value = Convert.ToInt64(Eval(Fresh(), "DATEDIFF('ms', #1970-01-01 00:00:00#, #2020-01-01 00:00:00#)"));

        Assert.Equal((long)(new DateTime(2020, 1, 1) - new DateTime(1970, 1, 1)).TotalMilliseconds, value);
        Assert.True(value > int.MaxValue, "a 50-year millisecond span must not be truncated to Int32");
    }

    [Fact]
    public void Returns_a_long_not_an_int()
        => Assert.IsType<long>(Eval(Fresh(), "DATEDIFF('ms', #2020-01-01 00:00:00#, #2020-01-01 00:00:01#)"));

    // Adding the arm must not have widened the other intervals, which stay Access's Long Integer.
    [Theory]
    [InlineData("s")]
    [InlineData("n")]
    [InlineData("h")]
    [InlineData("d")]
    [InlineData("yyyy")]
    public void Other_intervals_still_return_int(string interval)
        => Assert.IsType<int>(Eval(Fresh(), $"DATEDIFF('{interval}', #2020-01-01 00:00:00#, #2021-03-04 05:06:07#)"));

    // Only the abbreviation is accepted, matching DatePart and the rest of the interval table. The full word
    // is what EF used to emit and what the Jet translators now no longer send.
    [Fact]
    public void The_full_word_is_not_an_interval()
        => Assert.Throws<NotSupportedException>(
            () => Eval(Fresh(), "DATEDIFF('millisecond', #2020-01-01 00:00:00#, #2020-01-01 00:00:01#)"));
}
