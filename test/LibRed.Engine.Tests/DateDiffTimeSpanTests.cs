using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// A `time` column / TimeSpan parameter is surfaced by EF as a TimeSpan. The date functions (DateDiff, DatePart,
// DateAdd) coerce their arg with ToDate, which must treat a TimeSpan as a DateTime on the Jet 1899-12-30 epoch —
// otherwise Convert.ToDouble(TimeSpan) throws (TimeSpan is not IConvertible). Repro of
// BuiltInDataTypes.Can_query_using_DateDiffHour_using_TimeSpan at the engine level.
public class DateDiffTimeSpanTests
{
    private static QueryEngine Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ddts-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE `T` (`Id` INTEGER PRIMARY KEY)");
        e.ExecuteNonQuery("INSERT INTO `T` (`Id`) VALUES (1)");
        return e;
    }

    [Fact]
    public void DateDiff_hours_between_two_timespan_values()
    {
        var e = Fresh();
        object? v = e.ExecuteQuery("SELECT DATEDIFF('h', @a, @b) FROM `T`",
            new Dictionary<string, object?> { ["a"] = TimeSpan.FromHours(1), ["b"] = TimeSpan.FromHours(4.5) }).Rows.Single()[0];
        Assert.Equal(3, Convert.ToInt32(v));   // 1:00 → 4:30 = 3 whole hours (Access truncates)
    }

    [Fact]
    public void DatePart_hour_of_a_timespan_value()
    {
        var e = Fresh();
        object? v = e.ExecuteQuery("SELECT DATEPART('h', @a) FROM `T`",
            new Dictionary<string, object?> { ["a"] = new TimeSpan(14, 30, 0) }).Rows.Single()[0];
        Assert.Equal(14, Convert.ToInt32(v));
    }
}
