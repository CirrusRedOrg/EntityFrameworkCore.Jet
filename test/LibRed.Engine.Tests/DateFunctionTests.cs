using System.Globalization;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// The date functions <c>Year</c> <c>Month</c> <c>Day</c> <c>Hour</c> <c>Minute</c> <c>Second</c> <c>Weekday</c>
/// <c>DatePart</c> <c>DateAdd</c> <c>DateDiff</c> <c>DateSerial</c> <c>TimeSerial</c> <c>MonthName</c>
/// <c>WeekdayName</c> <c>DateValue</c> <c>TimeValue</c> <c>IsDate</c>. The expected values were measured against ACE
/// under en-AU (day before month, weeks starting on Monday), except that a Null argument gives Null where ACE raises
/// an error.
/// </summary>
public class DateFunctionTests(DateFunctionTests.Database database)
    : TempDatabaseTest, IClassFixture<DateFunctionTests.Database>
{
    private static readonly CultureInfo EnAu = CultureInfo.GetCultureInfo("en-AU");

    private static readonly string[] Setup =
    [
        "CREATE TABLE T (Id LONG, NT TEXT(60), DT DATETIME)",
        "INSERT INTO T (Id, DT) VALUES (1, #2020-01-02 12:00:00#)",
    ];

    public sealed class Database() : SharedDatabase("date-", Setup);

    // Text and the system week settings follow the regional format, so each query runs under a fixed culture.
    private object? Scalar(string expression, CultureInfo? culture = null) =>
        database.Scalar($"SELECT {expression} FROM T", culture ?? EnAu);

    private static string? Text(object? value) => value switch
    {
        null => null,
        DateTime date => date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    [Theory]
    [InlineData("MONTH('2/01/2020')", "1")]
    [InlineData("DAY('2/01/2020')", "2")]
    [InlineData("WEEKDAY('2/01/2020')", "5")]
    [InlineData("YEAR(43832.5)", "2020")]
    [InlineData("HOUR(43832.5)", "12")]
    [InlineData("WEEKDAY(43832.5)", "5")]
    [InlineData("YEAR('43832.5')", "2020")]
    [InlineData("DAY('43832.5')", "2")]
    [InlineData("YEAR(TRUE)", "1899")]
    [InlineData("DAY(TRUE)", "29")]
    [InlineData("WEEKDAY(TRUE)", "6")]
    [InlineData("DAY(1)", "31")]
    [InlineData("WEEKDAY(1)", "1")]
    [InlineData("DAY(-1.25)", "29")]
    [InlineData("HOUR(-1.25)", "6")]
    [InlineData("YEAR('$5')", "1900")]
    [InlineData("DAY('$5')", "4")]
    [InlineData("YEAR('12:30')", "1899")]
    [InlineData("DAY('12:30')", "30")]
    [InlineData("WEEKDAY('12:30')", "7")]
    [InlineData("HOUR(CCUR(43832.25))", "6")]
    [InlineData("YEAR('2.5')", "1899")]
    [InlineData("HOUR('2.5')", "2")]
    [InlineData("MINUTE('2.5')", "5")]
    [InlineData("HOUR(1.75)", "18")]
    [InlineData("HOUR('1.75')", "18")]
    [InlineData("DAY('2020')", "12")]
    [InlineData("MONTH('1,000')", "1")]
    public void Date_parts_read_text_as_a_date_and_numbers_as_a_serial(string expression, string expected) =>
        Assert.Equal(expected, Text(Scalar(expression)));

    [Theory]
    [InlineData("DATEPART('yyyy', 43832.75)", "2020")]
    [InlineData("DATEPART('m', '2/01/2020')", "1")]
    [InlineData("DATEPART('y', '2/01/2020')", "2")]
    [InlineData("DATEPART('w', '2/01/2020')", "5")]
    [InlineData("DATEPART('ww', '2/01/2020')", "1")]
    [InlineData("DATEPART('h', 43832.75)", "18")]
    [InlineData("WEEKDAY(#2020-01-02#, 0)", "4")]
    [InlineData("WEEKDAY(#2020-01-02#, 2)", "4")]
    [InlineData("WEEKDAY(#2020-01-02#, 4)", "2")]
    [InlineData("WEEKDAY(#2020-01-02#, 7)", "6")]
    [InlineData("WEEKDAY(DT, '2')", "4")]
    [InlineData("WEEKDAY(DT, 1.5)", "4")]
    [InlineData("DATEPART('w', #2020-01-02#, 0)", "4")]
    [InlineData("DATEPART('ww', #2021-01-03#, 0)", "1")]
    [InlineData("DATEPART('ww', #2021-01-03#, 1)", "2")]
    [InlineData("DATEPART('ww', #2021-01-03#, 2)", "1")]
    [InlineData("DATEPART('ww', #2021-01-03#, 7)", "2")]
    public void Datepart_and_weekday_count_from_the_first_day_of_the_week(string expression, string expected) =>
        Assert.Equal(expected, Text(Scalar(expression)));

    [Theory]
    [InlineData("#2021-01-01#, 1, 1", 1)]
    [InlineData("#2020-12-31#, 1, 0", 53)]
    [InlineData("#2021-01-01#, 0, 2", 53)]
    [InlineData("#2019-12-30#, 0, 2", 1)]
    [InlineData("#2024-12-30#, 0, 2", 1)]
    [InlineData("#2021-01-01#, 0, 3", 52)]
    [InlineData("#2020-12-31#, 0, 3", 52)]
    [InlineData("#2019-12-30#, 0, 3", 52)]
    [InlineData("#2024-12-30#, 2, 3", 53)]
    [InlineData("#2021-01-01#, 1, 2", 53)]
    [InlineData("#2019-12-30#, 1, 2", 1)]
    [InlineData("#2020-12-31#, 1, 2", 53)]
    [InlineData("#2021-01-01#, 1, 3", 52)]
    [InlineData("#2024-12-30#, 1, 3", 52)]
    [InlineData("#2020-12-31#, 4, 2", 1)]
    [InlineData("#2019-12-30#, 4, 2", 52)]
    [InlineData("#2021-01-01#, 4, 2", 1)]
    [InlineData("#2021-01-01#, 4, 3", 53)]
    [InlineData("#2021-01-01#, 7, 2", 52)]
    [InlineData("#2019-12-30#, 7, 2", 53)]
    [InlineData("#2024-12-30#, 7, 3", 52)]
    [InlineData("#2003-12-29#, 2, 2", 1)]
    [InlineData("#2003-12-28#, 1, 2", 53)]
    public void Week_of_year_follows_the_first_week_rule(string arguments, int expected) =>
        Assert.Equal(expected, Scalar($"DATEPART('ww', {arguments})"));

    [Theory]
    [InlineData("DATEADD('yyyy', '2', '2/01/2020')", "2022-01-02 00:00:00")]
    [InlineData("DATEADD('q', '2', '2/01/2020')", "2020-07-02 00:00:00")]
    [InlineData("DATEADD('y', '2', '2/01/2020')", "2020-01-04 00:00:00")]
    [InlineData("DATEADD('ww', '2', '2/01/2020')", "2020-01-16 00:00:00")]
    [InlineData("DATEADD('h', '2', '2/01/2020')", "2020-01-02 02:00:00")]
    [InlineData("DATEADD('d', TRUE, DT)", "2020-01-01 12:00:00")]
    [InlineData("DATEADD('d', 1, 43832)", "2020-01-03 00:00:00")]
    [InlineData("DATEADD('m', 1, #2020-01-30#)", "2020-02-29 00:00:00")]
    [InlineData("DATEADD('yyyy', 1, #2020-02-29#)", "2021-02-28 00:00:00")]
    [InlineData("DATEADD('m', 15, #2020-01-31#)", "2021-04-30 00:00:00")]
    [InlineData("DATEADD('m', -15, #2020-01-31#)", "2018-10-31 00:00:00")]
    [InlineData("DATEADD('s', 90, #2020-01-01#)", "2020-01-01 00:01:30")]
    [InlineData("DATEADD('s', -90, #2020-01-01#)", "2019-12-31 23:58:30")]
    [InlineData("DATEADD('d', 2.5, #2020-01-01#)", "2020-01-03 00:00:00")]
    [InlineData("DATEADD('d', 0.5, DT)", "2020-01-02 12:00:00")]
    [InlineData("DATEADD('h', -30, #1899-12-31 06:00#)", "1899-12-30 00:00:00")]
    [InlineData("DATEADD('s', 1, #1899-12-29 23:59:59#)", "1899-12-30 00:00:00")]
    [InlineData("DATEADD('yyyy', -1920, DT)", "0100-01-02 12:00:00")]
    [InlineData("DATEADD('yyyy', 7979, DT)", "9999-01-02 12:00:00")]
    public void Dateadd_truncates_the_number(string expression, string expected) =>
        Assert.Equal(expected, Text(Scalar(expression)));

    [Theory]
    [InlineData("DATEDIFF('yyyy', 43832.25, '2/02/2020')", 0)]
    [InlineData("DATEDIFF('m', 43832.25, '2/02/2020')", 1)]
    [InlineData("DATEDIFF('d', 43832.25, '2/02/2020')", 31)]
    [InlineData("DATEDIFF('w', 43832.25, '2/02/2020')", 4)]
    [InlineData("DATEDIFF('ww', 43832.25, '2/02/2020')", 5)]
    [InlineData("DATEDIFF('h', 43832.25, '2/02/2020')", 738)]
    [InlineData("DATEDIFF('n', 43832.25, '2/02/2020')", 44280)]
    [InlineData("DATEDIFF('s', 43832.25, '2/02/2020')", 2656800)]
    [InlineData("DATEDIFF('w', #2020-12-31 23:59:59#, #2021-01-01 00:00:00#)", 0)]
    [InlineData("DATEDIFF('w', #2020-01-04#, #2020-01-19#)", 2)]
    [InlineData("DATEDIFF('w', #2020-01-05#, #2020-01-18#)", 1)]
    [InlineData("DATEDIFF('w', #2020-01-19#, #2020-01-04#)", -2)]
    [InlineData("DATEDIFF('w', #1899-12-29 18:00#, #1899-12-30 06:00#)", 0)]
    [InlineData("DATEDIFF('ww', #2020-01-04#, #2020-01-19#)", 3)]
    [InlineData("DATEDIFF('ww', #2020-01-19#, #2020-01-04#)", -3)]
    [InlineData("DATEDIFF('h', #2020-12-31 23:59:59#, #2021-01-01 00:00:00#)", 1)]
    [InlineData("DATEDIFF('h', #2020-01-01 10:59:59#, #2020-01-01 11:00:01#)", 1)]
    [InlineData("DATEDIFF('h', #2020-01-01 11:00:59#, #2020-01-01 10:59:00#)", -1)]
    [InlineData("DATEDIFF('n', #2020-12-31 23:59:59#, #2021-01-01 00:00:00#)", 1)]
    [InlineData("DATEDIFF('n', #2020-01-01 10:59:59#, #2020-01-01 11:00:01#)", 1)]
    [InlineData("DATEDIFF('d', #1899-12-29 18:00#, #1899-12-30 06:00#)", 1)]
    [InlineData("DATEDIFF('d', DT, DT + 0.99)", 1)]
    [InlineData("DATEDIFF('w', #2020-01-01#, #2020-02-01#, 8)", 4)]
    [InlineData("DATEDIFF('ww', #2020-01-01#, #2020-02-01#, 7)", 5)]
    [InlineData("DATEDIFF('ww', #2020-01-01#, #2020-02-01#, 0)", 4)]
    [InlineData("DATEDIFF('d', #2020-01-01#, #2020-01-02#, 1, 4)", 1)]
    public void Datediff_counts_boundaries(string expression, int expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("DATESERIAL(29, 1, 1)", "2029-01-01 00:00:00")]
    [InlineData("DATESERIAL(49, 1, 1)", "2049-01-01 00:00:00")]
    [InlineData("DATESERIAL(50, 1, 1)", "1950-01-01 00:00:00")]
    [InlineData("DATESERIAL(99, 1, 1)", "1999-01-01 00:00:00")]
    [InlineData("DATESERIAL(0, 1, 1)", "2000-01-01 00:00:00")]
    [InlineData("DATESERIAL(-1, 1, 1)", "1999-01-01 00:00:00")]
    [InlineData("DATESERIAL(99, 12, 32)", "2000-01-01 00:00:00")]
    [InlineData("DATESERIAL(100, 0, 1)", "1999-12-01 00:00:00")]
    [InlineData("DATESERIAL(100, 1, 1)", "0100-01-01 00:00:00")]
    [InlineData("DATESERIAL(2020, 0, 0)", "2019-11-30 00:00:00")]
    [InlineData("DATESERIAL(2020, -1, -1)", "2019-10-30 00:00:00")]
    [InlineData("DATESERIAL(2020, 1, 32767)", "2109-09-17 00:00:00")]
    [InlineData("DATESERIAL(2020, 1.5, 1.5)", "2020-02-02 00:00:00")]
    [InlineData("DATESERIAL(2020, 2.5, 2.5)", "2020-02-02 00:00:00")]
    [InlineData("DATESERIAL('2020', '1', '2')", "2020-01-02 00:00:00")]
    [InlineData("DATESERIAL(2020, TRUE, 1)", "2019-11-01 00:00:00")]
    [InlineData("DATESERIAL(2020, 15, 1)", "2021-03-01 00:00:00")]
    [InlineData("DATESERIAL(2020, -15, 1)", "2018-09-01 00:00:00")]
    [InlineData("DATESERIAL(2020, 1, -40)", "2019-11-21 00:00:00")]
    [InlineData("TIMESERIAL(0, 0, 90)", "1899-12-30 00:01:30")]
    [InlineData("TIMESERIAL(0, 0, -90)", "1899-12-30 00:01:30")]
    [InlineData("TIMESERIAL(0, -90, 0)", "1899-12-30 01:30:00")]
    [InlineData("TIMESERIAL(10, 0, -90)", "1899-12-30 09:58:30")]
    [InlineData("TIMESERIAL(-1, 0, 0)", "1899-12-30 01:00:00")]
    [InlineData("TIMESERIAL(0, 0, -1)", "1899-12-30 00:00:01")]
    [InlineData("TIMESERIAL(-12, 0, 0)", "1899-12-30 12:00:00")]
    [InlineData("TIMESERIAL(-24, 0, 0)", "1899-12-29 00:00:00")]
    [InlineData("TIMESERIAL(-25, 0, 0)", "1899-12-29 01:00:00")]
    [InlineData("TIMESERIAL(25, 0, 0)", "1899-12-31 01:00:00")]
    [InlineData("TIMESERIAL(0, 0, 32767)", "1899-12-30 09:06:07")]
    [InlineData("TIMESERIAL(1.5, 1.5, 1.5)", "1899-12-30 02:02:02")]
    [InlineData("TIMESERIAL('1', '2', '3')", "1899-12-30 01:02:03")]
    public void Dateserial_and_timeserial_carry_out_of_range_parts(string expression, string expected) =>
        Assert.Equal(expected, Text(Scalar(expression)));

    [Theory]
    [InlineData("MONTHNAME(1.5)", "February")]
    [InlineData("MONTHNAME('2')", "February")]
    [InlineData("MONTHNAME(12, 1)", "Dec")]
    [InlineData("WEEKDAYNAME(1)", "Monday")]
    [InlineData("WEEKDAYNAME(7, TRUE)", "Sun")]
    [InlineData("WEEKDAYNAME(1.5)", "Tuesday")]
    [InlineData("WEEKDAYNAME('3')", "Wednesday")]
    [InlineData("WEEKDAYNAME(1, FALSE, 0)", "Monday")]
    [InlineData("WEEKDAYNAME(2, TRUE, 3)", "Wed")]
    [InlineData("DATEVALUE('12:30')", "1899-12-30 00:00:00")]
    [InlineData("TIMEVALUE('2020-01-02')", "1899-12-30 00:00:00")]
    [InlineData("ISDATE('1,000')", "True")]
    [InlineData("ISDATE('Feb 30')", "True")]
    [InlineData("ISDATE('2020-01-02T12:00')", "False")]
    public void Names_and_date_text(string expression, string expected) =>
        Assert.Equal(expected, Text(Scalar(expression)));

    [Theory]
    [InlineData("WEEKDAYNAME(1)", "Sunday")]
    [InlineData("WEEKDAY(#2020-01-02#, 0)", "5")]
    public void The_system_first_day_follows_the_culture(string expression, string expected) =>
        Assert.Equal(expected, Text(Scalar(expression, CultureInfo.GetCultureInfo("en-US"))));

    [Theory]
    [InlineData("WEEKDAY(DT, 8)")]
    [InlineData("WEEKDAY(DT, -1)")]
    [InlineData("DATEPART('ww', DT, 8)")]
    [InlineData("DATEPART('ww', DT, 1, 4)")]
    [InlineData("DATEPART('x', DT)")]
    [InlineData("DATEADD('x', 1, DT)")]
    [InlineData("DATEADD('yyyy', -1921, DT)")]
    [InlineData("DATEDIFF('ww', #2020-01-01#, #2020-02-01#, 8)")]
    [InlineData("DATESERIAL(10000, 1, 1)")]
    [InlineData("DATESERIAL(-8100, 1, 1)")]
    [InlineData("MONTHNAME(13)")]
    [InlineData("MONTHNAME(TRUE)")]
    [InlineData("WEEKDAYNAME(0)")]
    [InlineData("WEEKDAYNAME(8)")]
    [InlineData("WEEKDAYNAME(TRUE)")]
    [InlineData("WEEKDAYNAME(1, FALSE, 8)")]
    public void Out_of_range_arguments_are_an_invalid_procedure_call(string expression) =>
        Assert.Throws<ArgumentException>(() => Scalar(expression));

    [Theory]
    [InlineData("DATESERIAL(2020, 1, 32768)")]
    [InlineData("TIMESERIAL(0, 0, 32768)")]
    [InlineData("DATEDIFF('s', #0100-01-01#, #9999-12-31#)")]
    [InlineData("DATEDIFF('n', #0100-01-01#, #9999-12-31#)")]
    public void Values_past_their_type_overflow(string expression) =>
        Assert.Throws<OverflowException>(() => Scalar(expression));

    [Theory]
    [InlineData("DATEVALUE(43832.5)")]
    [InlineData("TIMEVALUE(43832.5)")]
    [InlineData("DATEVALUE('43832.5')")]
    [InlineData("DATEVALUE(TRUE)")]
    [InlineData("DATEVALUE(CCUR(43832.25))")]
    [InlineData("TIMEVALUE('$5')")]
    [InlineData("MONTHNAME(1, 'x')")]
    public void Datevalue_and_timevalue_take_only_dates_and_date_text(string expression) =>
        Assert.Throws<InvalidCastException>(() => Scalar(expression));

    [Theory]
    [InlineData("WEEKDAY(DT, NULL)")]
    [InlineData("DATEPART('ww', DT, NULL)")]
    [InlineData("DATEADD('m', NULL, DT)")]
    [InlineData("DATEADD('m', 1, NULL)")]
    [InlineData("DATEDIFF('yyyy', NULL, DT)")]
    [InlineData("DATESERIAL(NULL, 1, 1)")]
    [InlineData("TIMESERIAL(NULL, 1, 1)")]
    [InlineData("MONTHNAME(NULL)")]
    [InlineData("MONTHNAME(1, NULL)")]
    [InlineData("WEEKDAYNAME(1, NULL)")]
    [InlineData("WEEKDAYNAME(1, FALSE, NULL)")]
    [InlineData("DATEVALUE(NULL)")]
    [InlineData("TIMEVALUE(NT)")]
    public void A_null_argument_gives_null(string expression) =>
        Assert.Null(Scalar(expression));

    // ACE rounds a date to the second; LibRed keeps milliseconds, so 0.864 seconds is still second 0.
    [Fact]
    public void Second_keeps_the_fraction_of_a_second() =>
        Assert.Equal(0, Scalar("SECOND(0.00001)"));
}
