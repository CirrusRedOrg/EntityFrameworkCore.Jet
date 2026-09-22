using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// DATEDIFF("ms", a, b) — a LibRed extension. ACE's interval list stops at "s", but LibRed stores the full OA
// double instead of truncating to whole seconds, so a millisecond difference is both meaningful and exact.
//
// It counts into a Long Integer like every other interval, and gives Null for a span that will not fit — a
// millisecond count passes Int32 after about 25 days. DATEDIFF_BIG is the one that counts the same span in an
// Int64, and is what DateTimeOffset.ToUnixTimeMilliseconds emits.
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

    // A millisecond span passes Int32 after ~25 days, and the caller that emits this interval measures from
    // 1970: past the Long Integer, DATEDIFF is Null and DATEDIFF_BIG carries the count.
    [Fact]
    public void Spans_beyond_int32()
    {
        const string span = "#1970-01-01 00:00:00#, #2020-01-01 00:00:00#";
        QueryEngine engine = Fresh();

        Assert.Null(Eval(engine, $"DATEDIFF('ms', {span})"));

        long value = Convert.ToInt64(Eval(engine, $"DATEDIFF_BIG('ms', {span})"));
        Assert.Equal((long)(new DateTime(2020, 1, 1) - new DateTime(1970, 1, 1)).TotalMilliseconds, value);
        Assert.True(value > int.MaxValue, "a 50-year millisecond span must not be truncated to Int32");
    }

    [Fact]
    public void Returns_an_int_and_DateDiff_Big_a_long()
    {
        const string span = "#2020-01-01 00:00:00#, #2020-01-01 00:00:01#";
        QueryEngine engine = Fresh();
        Assert.IsType<int>(Eval(engine, $"DATEDIFF('ms', {span})"));
        Assert.IsType<long>(Eval(engine, $"DATEDIFF_BIG('ms', {span})"));
    }

    // Adding the arm must not have widened the other intervals, which stay Access's Long Integer.
    [Theory]
    [InlineData("s")]
    [InlineData("n")]
    [InlineData("h")]
    [InlineData("d")]
    [InlineData("yyyy")]
    public void Other_intervals_still_return_int(string interval)
        => Assert.IsType<int>(Eval(Fresh(), $"DATEDIFF('{interval}', #2020-01-01 00:00:00#, #2021-03-04 05:06:07#)"));

    // The column is declared as the values are — what a reader's GetFieldType reports, and what EF reads with.
    [Theory]
    [InlineData("DATEDIFF", "ms", typeof(int))]
    [InlineData("DATEDIFF", "MS", typeof(int))]
    [InlineData("DATEDIFF", "s", typeof(int))]
    [InlineData("DATEDIFF", "d", typeof(int))]
    [InlineData("DATEDIFF_BIG", "ms", typeof(long))]
    [InlineData("DATEDIFF_BIG", "s", typeof(long))]
    public void The_column_is_declared_as_its_values(string function, string interval, Type expected)
    {
        var result = Fresh().ExecuteQuery(
            $"SELECT {function}('{interval}', #2020-01-01 00:00:00#, #2020-01-02 00:00:00#) FROM `Shippers` WHERE `ShipperID` = 1");
        Assert.Equal(expected, result.ColumnTypes[0]);
        Assert.IsType(expected, result.Rows.Single()[0]);
    }

    // Only the abbreviation is accepted, matching DatePart and the rest of the interval table. The full word
    // is what EF used to emit and what the Jet translators now no longer send.
    [Fact]
    public void The_full_word_is_not_an_interval()
        => Assert.Throws<ArgumentException>(
            () => Eval(Fresh(), "DATEDIFF('millisecond', #2020-01-01 00:00:00#, #2020-01-01 00:00:01#)"));
}
