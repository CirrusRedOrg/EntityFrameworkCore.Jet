using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// A TimeSpan or TimeOnly parameter is the time Jet stores — the time on the 1899-12-30 epoch — wherever it is read:
// saved, compared, passed to a function. Beside a date in + or - it is a span instead, so a date less an hour is a
// date, not the day count a date less a date is. A time written into the SQL keeps Access's reading.
public class TimeSpanParameterTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "timespan-param-");
        var engine = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        engine.ExecuteNonQuery("CREATE TABLE Q (K LONG, A DATETIME, N DATETIME, B DATETIME2)");
        engine.ExecuteNonQuery("INSERT INTO Q (K, A, N, B) VALUES (1, #2020-02-29 17:55:00#, NULL, #2020-02-29 17:55:00#)");
        return engine;
    }

    private static readonly DateTime At = new(2020, 2, 29, 17, 55, 0);

    private static Dictionary<string, object?> Bind(object value) => new() { ["ts"] = value };

    [Theory]
    [InlineData("A + @ts", 1)]
    [InlineData("@ts + A", 1)]
    [InlineData("A - @ts", -1)]
    [InlineData("B + @ts", 1)]
    [InlineData("B - @ts", -1)]
    public void A_date_moves_by_the_span(string expression, int hours)
    {
        foreach (object span in new object[] { TimeSpan.FromHours(1), new TimeOnly(1, 0) })
        {
            var result = Fresh().ExecuteQuery($"SELECT {expression} FROM Q", Bind(span));
            Assert.Equal(typeof(DateTime), result.ColumnTypes[0]);
            Assert.Equal(At.AddHours(hours), result.Rows.Single()[0]);
        }
    }

    // A span below zero moves the date back. As a date it would be before the epoch, where the time counts away from
    // zero, and the sum would land 1.75 days off rather than a quarter of one.
    [Theory]
    [InlineData("A + @ts", -6)]
    [InlineData("A - @ts", 6)]
    public void A_negative_span_moves_the_other_way(string expression, int hours) =>
        Assert.Equal(At.AddHours(hours), Fresh().ExecuteQuery($"SELECT {expression} FROM Q", Bind(TimeSpan.FromHours(-6))).Rows.Single()[0]);

    // A span stays one through negation, sums and differences of spans, scaling by a number and a choice among spans,
    // so the date moves by the whole of it however the SQL groups it. @a is an hour, @b half of one.
    [Theory]
    [InlineData("A - (@a + @b)", -90)]
    [InlineData("A + (@a - @b)", 30)]
    [InlineData("A + (@b - @a)", -30)]              // a negative total moves it back
    [InlineData("A - (-@a)", 60)]
    [InlineData("A + @a * 2", 120)]
    [InlineData("A + 2 * @a", 120)]
    [InlineData("A + @a / 2", 30)]
    [InlineData("A + @a * K", 60)]                  // K is 1: a factor from the row
    [InlineData("A + IIF(K = 1, @a, @b)", 60)]
    [InlineData("A + IIF(K = 2, @a, @b)", 30)]
    [InlineData("A + COALESCE(NULL, @b)", 30)]
    [InlineData("A - CASE WHEN K = 1 THEN @a ELSE @b END", -60)]
    [InlineData("B - (@a + @b)", -90)]
    [InlineData("(@a + @b) + A", 90)]
    public void A_date_moves_by_a_span_however_it_is_built(string expression, int minutes)
    {
        var result = Fresh().ExecuteQuery($"SELECT {expression} FROM Q",
            new Dictionary<string, object?> { ["a"] = TimeSpan.FromHours(1), ["b"] = TimeSpan.FromMinutes(30) });
        Assert.Equal(typeof(DateTime), result.ColumnTypes[0]);
        Assert.Equal(At.AddMinutes(minutes), result.Rows.Single()[0]);
    }

    [Fact]
    public void A_choice_of_a_null_moves_nothing_and_is_null() =>
        Assert.Null(Fresh().ExecuteQuery("SELECT A + IIF(K = 2, @a, NULL) FROM Q",
            new Dictionary<string, object?> { ["a"] = TimeSpan.FromHours(1) }).Rows.Single()[0]);

    // A span with a number added is no span — the number is a count of days, and the sum is a date — so the date
    // less it is a day count, as Access has it.
    [Fact]
    public void A_span_plus_a_number_is_a_date_not_a_span() =>
        Assert.IsType<double>(Fresh().ExecuteQuery("SELECT A - (@a + 1) FROM Q",
            new Dictionary<string, object?> { ["a"] = TimeSpan.FromHours(1) }).Rows.Single()[0]);

    [Fact]
    public void A_span_divided_by_zero_is_a_division_by_zero() =>
        Assert.Throws<DivideByZeroException>(() => Fresh().ExecuteQuery("SELECT A + @a / 0 FROM Q",
            new Dictionary<string, object?> { ["a"] = TimeSpan.FromHours(1) }).Rows.ToList());

    [Fact]
    public void A_null_date_stays_null_and_is_still_declared_a_date()
    {
        var result = Fresh().ExecuteQuery("SELECT N - @ts FROM Q", Bind(TimeSpan.FromHours(1)));
        Assert.Equal(typeof(DateTime), result.ColumnTypes[0]);
        Assert.Null(result.Rows.Single()[0]);
    }

    [Fact]
    public void A_time_written_into_the_sql_is_still_a_date() =>
        Assert.IsType<double>(Fresh().ExecuteQuery("SELECT A - #01:00:00# FROM Q").Rows.Single()[0]);

    [Fact]
    public void On_its_own_it_is_the_time_on_the_epoch()
    {
        var result = Fresh().ExecuteQuery("SELECT @ts, HOUR(@ts) FROM Q", Bind(new TimeSpan(9, 30, 0)));
        Assert.Equal(typeof(DateTime), result.ColumnTypes[0]);
        Assert.Equal(new object?[] { new DateTime(1899, 12, 30, 9, 30, 0), 9 }, result.Rows.Single());
    }

    // Saved, it is stored as Jet stores a time — in a Date/Time column and a Date/Time Extended one alike — and a
    // comparison against it finds the row.
    [Fact]
    public void Saved_it_is_the_time_on_the_epoch()
    {
        QueryEngine engine = Fresh();
        var time = new TimeSpan(0, 13, 45, 30, 250);
        engine.ExecuteNonQuery("CREATE TABLE T (K LONG, T DATETIME, T2 DATETIME2)");
        engine.ExecuteNonQuery("INSERT INTO T (K, T, T2) VALUES (1, @ts, @ts)", Bind(time));
        engine.ExecuteNonQuery("INSERT INTO T (K, T, T2) VALUES (2, @ts, @ts)", Bind(new TimeOnly(8, 15)));

        var epoch = new DateTime(1899, 12, 30);
        var rows = engine.ExecuteQuery("SELECT T, T2 FROM T ORDER BY K").Rows.ToList();
        Assert.Equal(new object?[] { epoch + time, epoch + time }, rows[0]);
        Assert.Equal(new object?[] { epoch.AddHours(8.25), epoch.AddHours(8.25) }, rows[1]);

        Assert.Equal(1, engine.ExecuteQuery("SELECT K FROM T WHERE T = @ts AND T2 = @ts", Bind(time)).Rows.Single()[0]);
    }
}
