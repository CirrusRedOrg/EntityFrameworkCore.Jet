using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// DATETIME column defaults that call Access's date/time functions. Semantics verified against ACE:
//   NOW() / bare Now → current timestamp (date + time)
//   Date()           → today at midnight (date only)
//   Time()           → current time on the Jet epoch 1899-12-30 (time only)
// Bare Date / Time are NOT niladic in Jet SQL (they are reserved type keywords — ACE rejects them; they need
// parentheses), so only Now is recognised without parentheses.
public class DateTimeDefaultTests
{
    private static QueryEngine Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"dtdef-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        return new QueryEngine(JetDatabase.Open(path, readOnly: false));
    }

    private static DateTime InsertAndRead(string def)
    {
        var e = Fresh();
        e.ExecuteNonQuery($"CREATE TABLE T ( K LONG PRIMARY KEY, V DATETIME DEFAULT {def} )");
        e.ExecuteNonQuery("INSERT INTO T (K) VALUES (1)");
        return Convert.ToDateTime(e.ExecuteQuery("SELECT V FROM T").Rows.Single()[0]);
    }

    [Theory]
    [InlineData("NOW()")]
    [InlineData("Now")]     // niladic, no parentheses — the one bare form ACE accepts
    public void Now_default_is_the_current_timestamp(string def)
    {
        DateTime before = DateTime.Now.AddSeconds(-5);
        DateTime v = InsertAndRead(def);
        DateTime after = DateTime.Now.AddSeconds(5);
        Assert.InRange(v, before, after);
        Assert.NotEqual(v.Date, default);
        Assert.True(v.TimeOfDay > TimeSpan.Zero, "NOW() should carry a time component");
    }

    [Fact]
    public void Date_default_is_today_at_midnight()
    {
        DateTime v = InsertAndRead("Date()");
        Assert.Equal(DateTime.Today, v);
        Assert.Equal(TimeSpan.Zero, v.TimeOfDay);
    }

    [Fact]
    public void Time_default_is_the_current_time_on_the_jet_epoch()
    {
        DateTime v = InsertAndRead("Time()");
        Assert.Equal(new DateTime(1899, 12, 30), v.Date);   // time-only → the Jet epoch date
        Assert.InRange(v.TimeOfDay, DateTime.Now.AddMinutes(-2).TimeOfDay, DateTime.Now.AddMinutes(2).TimeOfDay);
    }

    [Fact]
    public void A_real_column_named_Now_still_wins_over_the_function()
    {
        var e = Fresh();
        e.ExecuteNonQuery("CREATE TABLE T ( K LONG PRIMARY KEY, [Now] LONG )");
        e.ExecuteNonQuery("INSERT INTO T (K, [Now]) VALUES (1, 42)");
        // The identifier resolves to the column, not the niladic Now function.
        int now = e.ExecuteQuery("SELECT [Now] FROM T").Rows.Single()[0] is { } v ? Convert.ToInt32(v) : -1;
        Assert.Equal(42, now);
    }
}
