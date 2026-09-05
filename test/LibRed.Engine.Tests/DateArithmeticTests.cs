using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// Date/time arithmetic on the OLE Automation serial (days since 1899-12-30, fractional part = time), verified
// vs ACE: date+time and date±N days yield a DateTime; date−date yields a plain day count.
public class DateArithmeticTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "datearith-");
        return new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
    }

    private static object? Scalar(string expr) =>
        Fresh().ExecuteQuery($"SELECT TOP 1 ({expr}) AS V FROM Shippers").Rows.First()[0];

    [Fact]
    public void Date_plus_time_combines_into_a_datetime()
        => Assert.Equal(new DateTime(2020, 1, 1, 21, 5, 19),
            Scalar("DateSerial(2020, 1, 1) + TimeSerial(21, 5, 19)"));

    [Fact]
    public void Date_minus_date_is_a_day_count()
        => Assert.Equal(1.0, Convert.ToDouble(Scalar("#2020-01-02# - #2020-01-01#")));

    [Fact]
    public void Date_plus_and_minus_whole_days_shifts_the_date()
    {
        Assert.Equal(new DateTime(2020, 1, 2), Scalar("#2020-01-01# + 1"));
        Assert.Equal(new DateTime(2019, 12, 31), Scalar("#2020-01-01# - 1"));
    }

    [Fact]
    public void Time_plus_time_adds_on_the_epoch_date()
        => Assert.Equal(new DateTime(1899, 12, 30, 3, 0, 0),
            Scalar("TimeSerial(1, 0, 0) + TimeSerial(2, 0, 0)"));
}
