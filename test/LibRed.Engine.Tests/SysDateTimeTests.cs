using System.Globalization;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// SQL Server's <c>GetUtcDate</c>, <c>SysDateTime</c> and <c>SysUtcDateTime</c>: the current date and time. Access has
/// none of them; they are LibRed extensions. GetUtcDate is SQL Server's datetime, a Date/Time here; the other two are
/// its datetime2.
/// </summary>
public class SysDateTimeTests(SysDateTimeTests.Database database)
    : TempDatabaseTest, IClassFixture<SysDateTimeTests.Database>
{
    private static readonly string[] Setup =
    [
        "CREATE TABLE T (K LONG PRIMARY KEY)",
        "INSERT INTO T (K) VALUES (1)",
    ];

    public sealed class Database() : SharedDatabase("sysdatetime-", Setup);

    private DateTime Scalar(string expression) =>
        (DateTime)database.Scalar($"SELECT {expression} FROM T", CultureInfo.InvariantCulture)!;

    [Fact]
    public void SysDateTime_is_the_local_time() =>
        Assert.InRange(Scalar("SysDateTime()"), DateTime.Now.AddMinutes(-1), DateTime.Now.AddMinutes(1));

    [Theory]
    [InlineData("GetUtcDate()")]
    [InlineData("SysUtcDateTime()")]
    public void The_utc_functions_are_the_utc_time(string expression) =>
        Assert.InRange(Scalar(expression), DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));

    // GetUtcDate is a Date/Time, whose serial holds whole milliseconds.
    [Fact]
    public void GetUtcDate_is_whole_milliseconds() =>
        Assert.Equal(0, Scalar("GetUtcDate()").Ticks % TimeSpan.TicksPerMillisecond);

    [Theory]
    [InlineData("GetUtcDate(1)")]
    [InlineData("SysDateTime(1)")]
    [InlineData("SysUtcDateTime(1)")]
    public void They_take_no_argument(string expression) =>
        Assert.Throws<InvalidOperationException>(() => Scalar(expression));

    [Theory]
    [InlineData("GetUtcDate()")]
    [InlineData("SysDateTime()")]
    [InlineData("SysUtcDateTime()")]
    public void The_column_is_a_date(string expression) =>
        Assert.Equal(typeof(DateTime), database.Query($"SELECT {expression} FROM T", CultureInfo.InvariantCulture).ColumnTypes[0]);
}
