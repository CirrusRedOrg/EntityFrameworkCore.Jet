using System.Globalization;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// The conversion functions <c>CBool</c> <c>CByte</c> <c>CInt</c> <c>CLng</c> <c>CSng</c> <c>CDbl</c> <c>CCur</c>
/// <c>CStr</c> <c>CDate</c> <c>CVar</c>: how each reads its argument, rounds and overflows. The expected values were
/// measured against ACE, except that a Null argument gives Null where ACE raises "Invalid use of Null". <c>CLngLng</c>,
/// which ACE does not have, follows <c>CLng</c>.
/// </summary>
public class ConversionFunctionTests(ConversionFunctionTests.Database database)
    : TempDatabaseTest, IClassFixture<ConversionFunctionTests.Database>
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    private static readonly string[] Setup =
    [
        "CREATE TABLE T (Id LONG, DC DECIMAL(18,4), TN TEXT(60), NT TEXT(60), DT DATETIME, G GUID, B BINARY(4))",
        "INSERT INTO T (Id, DC, TN, DT) VALUES (1, 4.5, '7', #2020-01-02 12:00:00#)",
        "UPDATE T SET G = {00112233-4455-6677-8899-AABBCCDDEEFF}",
        "UPDATE T SET B = 0x41004200",
    ];

    public sealed class Database() : SharedDatabase("conversion-", Setup);

    // Text is read in the regional format, so each query runs under a fixed culture whatever the machine's.
    private object? Scalar(string expression, CultureInfo? culture = null) =>
        database.Scalar($"SELECT {expression} FROM T", culture ?? EnUs);

    [Theory]
    [InlineData("CINT(2.5)", 2)]
    [InlineData("CINT(1.5)", 2)]
    [InlineData("CINT(-0.5)", 0)]
    [InlineData("CINT(-1.5)", -2)]
    [InlineData("CINT(TRUE)", -1)]
    [InlineData("CINT('2.5')", 2)]
    [InlineData("CINT(' 2.5 ')", 2)]
    [InlineData("CINT('1,000')", 1000)]
    [InlineData("CINT('$5')", 5)]
    [InlineData("CINT('&H10')", 16)]
    [InlineData("CINT('1e2')", 100)]
    [InlineData("CINT('(5)')", -5)]
    [InlineData("CINT('5-')", -5)]
    [InlineData("CINT(#1899-12-30 06:00#)", 0)]
    [InlineData("CINT(TN)", 7)]
    public void Cint_reads_text_and_dates_as_numbers_and_rounds_half_to_even(string expression, short expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("CLNG(#2020-01-02#)", 43832)]
    [InlineData("CLNG(DT)", 43832)]
    [InlineData("CLNG('1.5')", 2)]
    [InlineData("CLNG('&H10')", 16)]
    [InlineData("CLNG(2147483647.4)", 2147483647)]
    public void Clng_converts_to_a_long(string expression, int expected) =>
        Assert.Equal(expected, Scalar(expression));

    // CLngLng is VBA's LongLong conversion and a LibRed extension (ACE reports it undefined): CLng's reading, into an
    // Int64. Text is read exactly, so the ends of an Int64 survive where a Double would round them.
    [Theory]
    [InlineData("CLNGLNG(2.5)", 2L)]
    [InlineData("CLNGLNG(3.5)", 4L)]
    [InlineData("CLNGLNG(-2.5)", -2L)]
    [InlineData("CLNGLNG(TRUE)", -1L)]
    [InlineData("CLNGLNG(#2020-01-02#)", 43832L)]
    [InlineData("CLNGLNG(DC)", 4L)]
    [InlineData("CLNGLNG(TN)", 7L)]
    [InlineData("CLNGLNG('1.5')", 2L)]
    [InlineData("CLNGLNG('1,000')", 1000L)]
    [InlineData("CLNGLNG('&H10')", 16L)]
    [InlineData("CLNGLNG(1E12)", 1000000000000L)]
    [InlineData("CLNGLNG(9223372036854775807)", long.MaxValue)]
    [InlineData("CLNGLNG('9223372036854775807')", long.MaxValue)]
    [InlineData("CLNGLNG('-9223372036854775808')", long.MinValue)]
    public void Clnglng_converts_to_an_int64(string expression, long expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Fact]
    public void Clnglng_is_declared_an_int64() =>
        Assert.Equal(typeof(long), database.Query("SELECT CLNGLNG(Id) FROM T", EnUs).ColumnTypes[0]);

    [Theory]
    [InlineData("CBYTE(254.5)", 254)]
    [InlineData("CBYTE('2.5')", 2)]
    [InlineData("CBYTE('1e2')", 100)]
    [InlineData("CBYTE(#1899-12-30 06:00#)", 0)]
    public void Cbyte_converts_to_a_byte(string expression, byte expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("CDBL(#2020-01-02#)", 43832)]
    [InlineData("CDBL(DT)", 43832.5)]
    [InlineData("CDBL(#1899-12-30 06:00#)", 0.25)]
    [InlineData("CDBL('$5')", 5)]
    [InlineData("CDBL('(5)')", -5)]
    [InlineData("CDBL(TRUE)", -1)]
    [InlineData("CSNG(DT)", 43832.5)]
    [InlineData("CSNG('&H10')", 16)]
    [InlineData("CSNG('5-')", -5)]
    public void Cdbl_and_csng_read_dates_as_their_serial(string expression, double expected) =>
        Assert.Equal(expected, Convert.ToDouble(Scalar(expression), CultureInfo.InvariantCulture));

    [Fact]
    public void Csng_keeps_single_precision() =>
        Assert.Equal(1f / 3, Scalar("CSNG(1/3)"));

    [Theory]
    [InlineData("CCUR(DT)", "43832.5")]
    [InlineData("CCUR('1e2')", "100")]
    [InlineData("CCUR('(5)')", "-5")]
    [InlineData("CCUR(1.23456)", "1.2346")]
    [InlineData("CCUR(TRUE)", "-1")]
    // A number written with a decimal point, and text, are read exactly rather than as a Double (verified vs ACE).
    [InlineData("CCUR('12345678901234.5678')", "12345678901234.5678")]
    [InlineData("CCUR(12345678901234.5678)", "12345678901234.5678")]
    [InlineData("CCUR('922337203685477.5807')", "922337203685477.5807")]
    [InlineData("CCUR('1234567890123.45678')", "1234567890123.4568")]
    public void Ccur_converts_to_four_places(string expression, string expected) =>
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), Scalar(expression));

    // CDec is a LibRed extension (ACE's expression service refuses it), reading its argument as CCur does.
    [Theory]
    [InlineData("CDEC(12345678901234567.123456789)", "12345678901234567.123456789")]
    [InlineData("CDEC(-12345678901234567.123456789)", "-12345678901234567.123456789")]
    [InlineData("CDEC('12345678901234567.123456789')", "12345678901234567.123456789")]
    [InlineData("CDEC('1e3')", "1000")]
    [InlineData("CDEC('&HFF')", "255")]
    [InlineData("CDEC(0.1) + CDEC(0.2)", "0.3")]
    public void Cdec_keeps_every_written_place(string expression, string expected) =>
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), Scalar(expression));

    [Theory]
    [InlineData("CBOOL(#2020-01-02#)", true)]
    [InlineData("CBOOL(DT)", true)]
    [InlineData("CBOOL(#1899-12-30 06:00#)", true)]
    [InlineData("CBOOL('$5')", true)]
    [InlineData("CBOOL('&H10')", true)]
    [InlineData("CBOOL('True')", true)]
    [InlineData("CBOOL('False')", false)]
    [InlineData("CBOOL('0')", false)]
    [InlineData("CBOOL('-0')", false)]
    [InlineData("CBOOL(0.5)", true)]
    [InlineData("CBOOL(0)", false)]
    public void Cbool_is_true_for_anything_that_reads_as_non_zero(string expression, bool expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("CSTR(TRUE)", "-1")]
    [InlineData("CSTR(DC)", "4.5")]
    [InlineData("CSTR(1/3)", "0.333333333333333")]
    [InlineData("CSTR(CSNG(1/3))", "0.3333333")]
    [InlineData("CSTR(1E-5)", "0.00001")]
    [InlineData("CSTR(1E300)", "1E+300")]
    [InlineData("CSTR(CCUR(1.23456))", "1.2346")]
    [InlineData("CSTR(#2020-01-02#)", "1/2/2020")]
    [InlineData("CSTR(DT)", "1/2/2020 12:00:00 PM")]
    [InlineData("CSTR(#1899-12-30 06:00#)", "6:00:00 AM")]
    [InlineData("CSTR(G)", "{00112233-4455-6677-8899-AABBCCDDEEFF}")]
    [InlineData("CSTR(B)", "AB")]
    public void Cstr_writes_a_value_as_ampersand_does(string expression, string expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("CDATE('1')", "1899-12-31 00:00:00")]
    [InlineData("CDATE(' 1 ')", "1899-12-31 00:00:00")]
    [InlineData("CDATE('0')", "1899-12-30 00:00:00")]
    [InlineData("CDATE('$5')", "1900-01-04 00:00:00")]
    [InlineData("CDATE('&H10')", "1900-01-15 00:00:00")]
    [InlineData("CDATE('1e2')", "1900-04-09 00:00:00")]
    [InlineData("CDATE('(5)')", "1899-12-25 00:00:00")]
    [InlineData("CDATE(TN)", "1900-01-06 00:00:00")]
    [InlineData("CDATE(TRUE)", "1899-12-29 00:00:00")]
    [InlineData("CDATE(43832.5)", "2020-01-02 12:00:00")]
    [InlineData("CDATE('12:00')", "1899-12-30 12:00:00")]
    [InlineData("CDATE('1/2/2020')", "2020-01-02 00:00:00")]
    [InlineData("CDATE('2020-01-02 13:30')", "2020-01-02 13:30:00")]
    [InlineData("CDATE('Jan 2, 2020')", "2020-01-02 00:00:00")]
    [InlineData("CDATE(DT)", "2020-01-02 12:00:00")]
    public void Cdate_reads_a_number_as_a_serial_and_other_text_as_a_date(string expression, string expected) =>
        Assert.Equal(expected, Assert.IsType<DateTime>(Scalar(expression)).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

    // Measured against ACE under en-AU (day before month). {year} is the current year.
    [Theory]
    [InlineData("2.5", "1899-12-30 02:05:00")]
    [InlineData("2.5.20", "1899-12-30 02:05:20")]
    [InlineData("0.5", "1899-12-30 00:05:00")]
    [InlineData("10.30.45", "1899-12-30 10:30:45")]
    [InlineData("1 .5", "1899-12-30 01:05:00")]
    [InlineData("13.30", "1899-12-30 13:30:00")]
    [InlineData("1.30 PM", "1899-12-30 13:30:00")]
    [InlineData("2.5 PM", "1899-12-30 14:05:00")]
    [InlineData("1:2:3", "1899-12-30 01:02:03")]
    [InlineData("13:00 PM", "1899-12-30 13:00:00")]
    [InlineData("12 AM", "1899-12-30 00:00:00")]
    [InlineData("3PM", "1899-12-30 15:00:00")]
    [InlineData("25.5", "1900-01-24 12:00:00")]
    [InlineData("2.60", "1900-01-01 14:24:00")]
    [InlineData("31.12", "1900-01-30 02:52:48")]
    [InlineData(".5", "1899-12-30 12:00:00")]
    [InlineData("1.", "1899-12-31 00:00:00")]
    [InlineData("1-", "1899-12-29 00:00:00")]
    [InlineData("2020", "1905-07-12 00:00:00")]
    [InlineData("1,000", "2000-01-01 00:00:00")]
    [InlineData("1,2020", "2020-01-01 00:00:00")]
    [InlineData("1,000.5", "1902-09-26 12:00:00")]
    [InlineData("1.000,5", "1899-12-31 00:00:43")]
    [InlineData("1,5", "{year}-05-01 00:00:00")]
    [InlineData("1 2", "{year}-02-01 00:00:00")]
    [InlineData("13/2", "{year}-02-13 00:00:00")]
    [InlineData("2/13", "{year}-02-13 00:00:00")]
    [InlineData("1/2 3:04", "{year}-02-01 03:04:00")]
    [InlineData("1-2-2020", "2020-02-01 00:00:00")]
    [InlineData("1 / 2 / 2020", "2020-02-01 00:00:00")]
    [InlineData("2020-1-2", "2020-01-02 00:00:00")]
    [InlineData("2020 1 2", "2020-01-02 00:00:00")]
    [InlineData("1/2/29", "2029-02-01 00:00:00")]
    [InlineData("1/2/30", "2030-02-01 00:00:00")]
    [InlineData("1/2/99", "1999-02-01 00:00:00")]
    [InlineData("1/2/0", "2000-02-01 00:00:00")]
    [InlineData("1/2/100", "0100-02-01 00:00:00")]
    [InlineData("12:00 1/2/2020", "2020-02-01 12:00:00")]
    [InlineData("1/2/2020 1:30 PM", "2020-02-01 13:30:00")]
    [InlineData("Jan 2", "{year}-01-02 00:00:00")]
    [InlineData("Jan 2020", "2020-01-01 00:00:00")]
    [InlineData("Feb 30", "2030-02-01 00:00:00")]
    [InlineData("Feb 29 2020", "2020-02-29 00:00:00")]
    [InlineData("2 January 2020", "2020-01-02 00:00:00")]
    [InlineData("January 2, 2020", "2020-01-02 00:00:00")]
    [InlineData("2020-01-02 12:00:00", "2020-01-02 12:00:00")]
    public void Cdate_reads_text_as_ole_automation_does(string text, string expected) =>
        Assert.Equal(
            expected.Replace("{year}", DateTime.Today.Year.ToString(CultureInfo.InvariantCulture)),
            Assert.IsType<DateTime>(Scalar($"CDATE('{text}')", CultureInfo.GetCultureInfo("en-AU")))
                .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

    [Theory]
    [InlineData("2.5.2020")]
    [InlineData("31/2/2020")]
    [InlineData("Jan")]
    [InlineData("tues")]
    [InlineData("1st Jan")]
    [InlineData("2020-01-02T12:00")]
    [InlineData("2020-13-01")]
    [InlineData("1.2.3.4")]
    [InlineData("24:00")]
    [InlineData("1:60")]
    [InlineData("1//2")]
    [InlineData("1/2/2020/")]
    [InlineData("1/2/10000")]
    [InlineData("Feb 29 2019")]
    [InlineData("noon")]
    public void Cdate_of_text_that_is_neither_a_date_nor_a_number_is_a_type_mismatch(string text) =>
        Assert.Throws<InvalidCastException>(() => Scalar($"CDATE('{text}')", CultureInfo.GetCultureInfo("en-AU")));

    [Fact]
    public void Cdate_keeps_a_fraction_of_a_second() =>
        Assert.Equal(1E-5, Scalar("CDBL(CDATE(1E-5))"));

    // A LibRed extension, which LibRed's own SQL relies on for a time with milliseconds; ACE refuses the fourth part.
    [Theory]
    [InlineData("TIMEVALUE('12:30:45.123')", "1899-12-30 12:30:45.1230000", "en-US")]
    [InlineData("TIMEVALUE('12:30:45.5')", "1899-12-30 12:30:45.5000000", "en-US")]
    [InlineData("TIMEVALUE('12:30:45.1234567')", "1899-12-30 12:30:45.1234567", "en-US")]
    [InlineData("CDATE('2020-01-02 12:30:45.007')", "2020-01-02 12:30:45.0070000", "en-US")]
    [InlineData("TIMEVALUE('12:30:45.123')", "1899-12-30 12:30:45.1230000", "de-DE")]
    public void Time_text_may_have_a_fraction_of_a_second(string expression, string expected, string culture) =>
        Assert.Equal(expected, Assert.IsType<DateTime>(Scalar(expression, CultureInfo.GetCultureInfo(culture)))
            .ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture));

    [Theory]
    [InlineData("TIMEVALUE('12.30.45.5')")]
    [InlineData("TIMEVALUE('12:30:45.12345678')")]
    [InlineData("TIMEVALUE('12:30:45.1.2')")]
    public void Only_seconds_take_a_short_fraction(string expression) =>
        Assert.Throws<InvalidCastException>(() => Scalar(expression));

    [Theory]
    [InlineData("CVAR(1)", 1)]
    [InlineData("CVAR('abc')", "abc")]
    [InlineData("CVAR(TRUE)", true)]
    public void Cvar_passes_its_argument_through(string expression, object expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("CBOOL(NULL)")]
    [InlineData("CBYTE(NULL)")]
    [InlineData("CINT(NT)")]
    [InlineData("CLNG(NULL)")]
    [InlineData("CLNGLNG(NULL)")]
    [InlineData("CSNG(NULL)")]
    [InlineData("CDBL(NULL)")]
    [InlineData("CCUR(NT)")]
    [InlineData("CSTR(NULL)")]
    [InlineData("CDATE(NT)")]
    [InlineData("CVAR(NULL)")]
    public void Null_gives_null(string expression) =>
        Assert.Null(Scalar(expression));

    [Theory]
    [InlineData("CBYTE(256)")]
    [InlineData("CBYTE(255.5)")]
    [InlineData("CBYTE(-1)")]
    [InlineData("CBYTE(TRUE)")]
    [InlineData("CINT(32767.5)")]
    [InlineData("CLNG(2147483647.5)")]
    [InlineData("CLNGLNG('9223372036854775808')")]
    [InlineData("CLNGLNG(1E19)")]
    [InlineData("CSNG(1E300)")]
    [InlineData("CCUR(1234567890123456)")]
    [InlineData("CDATE(1E300)")]
    public void A_value_past_the_type_is_an_overflow(string expression) =>
        Assert.Throws<OverflowException>(() => Scalar(expression));

    [Theory]
    [InlineData("CBOOL('yes')")]
    [InlineData("CINT('abc')")]
    [InlineData("CDBL('')")]
    [InlineData("CLNG(G)")]
    [InlineData("CLNGLNG(G)")]
    [InlineData("CLNGLNG('abc')")]
    [InlineData("CDATE('abc')")]
    [InlineData("CDATE(B)")]
    public void A_value_that_does_not_convert_is_a_type_mismatch(string expression) =>
        Assert.Throws<InvalidCastException>(() => Scalar(expression));
}
