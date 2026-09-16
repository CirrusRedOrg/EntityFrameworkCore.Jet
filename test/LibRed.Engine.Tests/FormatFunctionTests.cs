using System.Globalization;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// <c>Format</c> <c>FormatNumber</c> <c>FormatCurrency</c> <c>FormatPercent</c> <c>FormatDateTime</c>. The expected
/// values were measured against ACE (under en-AU, and written here for en-US, which differs only in its date order and
/// long date), except that a Null format or setting gives Null where ACE raises an error.
/// </summary>
public class FormatFunctionTests(FormatFunctionTests.Database database)
    : TempDatabaseTest, IClassFixture<FormatFunctionTests.Database>
{
    private static readonly string[] Setup =
    [
        "CREATE TABLE T (Id LONG, DT DATETIME)",
        "INSERT INTO T (Id, DT) VALUES (1, #2020-01-02 12:00:00#)",
    ];

    public sealed class Database() : SharedDatabase("format-", Setup);

    // Formats follow the regional settings, so each query runs under en-US whatever the machine's culture.
    private object? Scalar(string expression) =>
        database.Scalar($"SELECT {expression} FROM T", CultureInfo.GetCultureInfo("en-US"));

    private void AssertFormat(string value, string format, string expected) =>
        Assert.Equal(expected, Scalar($"FORMAT({value}, \"{format.Replace("\"", "\"\"")}\")"));

    [Theory]
    [InlineData("-1234.5678", "#,##0.00", "-1,234.57")]
    [InlineData("0", "#.##", ".")]
    [InlineData("0.5", "#.##", ".5")]
    [InlineData("-0.005", "0.00", "-0.01")]
    [InlineData("-0.005", "0", "0")]
    [InlineData("CDBL(0.125)", "0.00", "0.13")]
    [InlineData("CDBL(1.005)", "0.00", "1.01")]
    [InlineData("CDBL(2.5)", "0", "3")]
    [InlineData("CDBL(1) / 3", "0.00000000000000000000", "0.33333333333333300000")]
    [InlineData("CDBL(1234567890123456789)", "0", "1234567890123460000")]
    [InlineData("CSNG(0.1)", "0.0000000000", "0.1000000000")]
    [InlineData("1234.5678", "0.0#", "1234.57")]
    [InlineData("1234.5", "0.0#", "1234.5")]
    [InlineData("12.3", "0.0#0", "12.300")]
    [InlineData("12.3", "#0.#0#", "12.30")]
    [InlineData("12", "0.#", "12.")]
    [InlineData("1234.5", "0.0.0", "1234.5.0")]
    [InlineData("1234.5", "0.0 0", "1234.5 0")]
    [InlineData("1234.5678", "0.5", "1235.5")]
    [InlineData("TRUE", "0.00", "-1.00")]
    [InlineData("DT", "0.00", "43832.50")]
    [InlineData("'12'", "0.00", "12.00")]
    [InlineData("'2/1/2020'", "0.00", "43862.00")]
    [InlineData("'abc'", "0.00", "abc")]
    [InlineData("''", "0", "")]
    public void Digit_placeholders_round_half_away_from_zero(string value, string format, string expected) =>
        AssertFormat(value, format, expected);

    [Theory]
    [InlineData("123456789", "#,##0,", "123,457")]
    [InlineData("123456789", "#,##0,,", "123")]
    [InlineData("-1234.5678", "#,##0,,", "0")]
    [InlineData("1234567", "0,.0", "1234.6")]
    [InlineData("1234567", "#,##0, \"k\"", "1,235 k")]
    [InlineData("1234.5678", "0.00,", "1234.57")]
    [InlineData("1234", ",0", ",1234")]
    [InlineData("1234", "0,0", "1,234")]
    [InlineData("12345", "#,#0:0", "12,34:5")]
    [InlineData("123456789", "00:00", "1234567:89")]
    [InlineData("-1", "00:00", "-00:01")]
    [InlineData("5", "00 00", "00 05")]
    [InlineData("0", "#.#%", ".%")]
    [InlineData("1234.5678", "0.0%%", "12345678.0%%")]
    [InlineData("1234.5678", "0.00\\%", "1234.57%")]
    public void Commas_group_or_scale_and_literals_sit_among_the_digits(string value, string format, string expected) =>
        AssertFormat(value, format, expected);

    [Theory]
    [InlineData("-1234.5678", "0.00E-00", "-1.23E03")]
    [InlineData("-1234.5678", "0.00e+00", "-1.23e+03")]
    [InlineData("1234.5678", "##0.0E+0", "123.5E+1")]
    [InlineData("0", "##0.0E+0", "000.0E+0")]
    [InlineData("1234.5", "#,##0.00E+00", "1,234.50E+00")]
    [InlineData("1234.5", "0.00E+", "1.23E+3")]
    [InlineData("1234.5", "#.##E+##", "1.23E+3")]
    [InlineData("0", "#.##E+##", "0.E+0")]
    [InlineData("999.99", "0.0E+0", "1.0E+3")]
    [InlineData("0.00012", "0.0E-0", "1.2E-4")]
    [InlineData("1234.5", "0.0E+0%", "1.2E+5%")]
    [InlineData("1234.5", "E+0", "E+1235")]
    [InlineData("1234.5", "0E", "1235")]
    public void Exponent_formats_keep_the_integer_placeholders(string value, string format, string expected) =>
        AssertFormat(value, format, expected);

    [Theory]
    [InlineData("-1234.5678", "$#,##0.00;($#,##0.00)", "($1,234.57)")]
    [InlineData("0", "0;-0;\"zero\"", "zero")]
    [InlineData("-0.005", "0;-0;\"zero\"", "zero")]
    [InlineData("-1", "0;;\"z\";\"null\"", "-1")]
    [InlineData("NULL", "0;;\"z\";\"null\"", "null")]
    [InlineData("NULL", "0", "")]
    [InlineData("-1234.5678", ";;;", "")]
    [InlineData("-1", "0;", "-1")]
    [InlineData("-0.001", "0.00;(0.00)", "0.00")]
    [InlineData("0.001", "0.00;(0.00);\"z\"", "z")]
    [InlineData("-0.001", "#.##;;\"z\"", "z")]
    [InlineData("-0.001", "x;;\"z\"", "-x")]
    [InlineData("-0.4", "0;x;\"z\"", "x")]
    [InlineData("0.4", "0;;", "0")]
    [InlineData("1", ";x", "")]
    [InlineData("-1", ";x", "x")]
    [InlineData("-1", "x", "-x")]
    [InlineData("-1", "-0", "--1")]
    [InlineData("-5", "(0)", "-(5)")]
    [InlineData("-1", "\\$0", "-$1")]
    [InlineData("-1", "\"x\"0", "-x1")]
    [InlineData("5", "[Red]0", "5")]
    [InlineData("5", "*0", "")]
    public void Sections_choose_by_sign_and_rounded_zero(string value, string format, string expected) =>
        AssertFormat(value, format, expected);

    [Theory]
    [InlineData("-1234.5678", "General Number", "-1234.5678")]
    [InlineData("DT", "General Number", "43832.5")]
    [InlineData("CDBL(1) / 3", "General Number", "0.333333333333333")]
    [InlineData("-1234.5678", "Currency", "-$1,234.57")]
    [InlineData("-0.001", "Currency", "$0.00")]
    [InlineData("0.5", "Fixed", "0.50")]
    [InlineData("1234.5", "fixed", "1234.50")]
    [InlineData("-0.001", "Fixed", "0.00")]
    [InlineData("1234567.891", "Standard", "1,234,567.89")]
    [InlineData("-0.001", "Percent", "-0.10%")]
    [InlineData("-0.001", "Scientific", "-1.00E-03")]
    [InlineData("TRUE", "Yes/No", "Yes")]
    [InlineData("#1899-12-30#", "Yes/No", "No")]
    [InlineData("'0'", "On/Off", "Off")]
    [InlineData("'abc'", "True/False", "abc")]
    [InlineData("1234.5", " Fixed", " Fixe18")]
    [InlineData("DT", "General Date", "1/2/2020 12:00:00 PM")]
    [InlineData("0", "General Date", "12:00:00 AM")]
    [InlineData("TRUE", "General Date", "12/29/1899")]
    [InlineData("DT", "Long Date", "Thursday, January 2, 2020")]
    [InlineData("DT", "Medium Date", "02-Jan-20")]
    [InlineData("#0100-01-01#", "Short Date", "1/1/0100")]
    [InlineData("#13:45:30#", "Long Time", "1:45:30 PM")]
    [InlineData("#13:45:30#", "Medium Time", "01:45 PM")]
    [InlineData("#13:45:30#", "Short Time", "13:45")]
    [InlineData("DT", "", "1/2/2020 12:00:00 PM")]
    [InlineData("#1899-12-30#", "", "12:00:00 AM")]
    [InlineData("#0100-01-01#", "", "1/1/0100")]
    [InlineData("TRUE", "", "-1")]
    public void Named_formats_match_whole_and_ignore_case(string value, string format, string expected) =>
        AssertFormat(value, format, expected);

    [Theory]
    [InlineData("DT", "yyyy-mm-dd", "2020-01-02")]
    [InlineData("DT", "dddd, mmmm d, yyyy", "Thursday, January 2, 2020")]
    [InlineData("DT", "m", "1")]
    [InlineData("DT", "hh:mm:ss", "12:00:00")]
    [InlineData("DT", "mmm hh", "Jan 12")]
    [InlineData("DT", "h mmm", "12 Jan")]
    [InlineData("DT", "hh \"x\" mm", "12 x 00")]
    [InlineData("DT", "n m", "0 1")]
    [InlineData("DT", "hh:mm:ss mm", "12:00:00 01")]
    [InlineData("DT", "q/y/w/ww", "1/2/5/1")]
    [InlineData("DT", "HH:NN", "12:00")]
    [InlineData("DT", "tt", "tt")]
    [InlineData("DT", "tttttt", "12:00:00 PMt")]
    [InlineData("DT", "d\\d", "2d")]
    [InlineData("DT", "\"hh\"", "hh")]
    [InlineData("#13:45:30#", "h:n:s", "13:45:30")]
    [InlineData("#13:45:30#", "hh:mm AM/PM", "01:45 PM")]
    [InlineData("#13:45:30#", "hh am/pm", "01 pm")]
    [InlineData("#13:45:30#", "h:nn a/p", "1:45 p")]
    [InlineData("#13:45:30#", "h:nn A/P", "1:45 P")]
    [InlineData("#13:45:30#", "h AMPM", "1 PM")]
    [InlineData("#13:45:30#", "c", "1:45:30 PM")]
    [InlineData("-1234.5678", "yyy", "96226")]
    [InlineData("-1234.5678", "mmmmm", "August8")]
    [InlineData("-1234.5678", "hhh", "1313")]
    [InlineData("-1234.5678", "sss", "3838")]
    [InlineData("-1234.5678", "d.m.y", "13.8.226")]
    [InlineData("-1234.5678", "hh:nn:ss", "13:37:38")]
    [InlineData("CDATE(43832.9999999)", "yyyy-mm-dd hh:nn:ss", "2020-01-03 00:00:00")]
    [InlineData("#0100-01-01#", "yyyy", "0100")]
    [InlineData("0", "/", "/")]
    [InlineData("-1", ":", ":")]
    [InlineData("'2/1/2020'", "yyyy-mm-dd", "2020-02-01")]
    [InlineData("'abc'", "yyyy", "abc")]
    [InlineData("''", "yyyy", "")]
    [InlineData("NULL", "yyyy", "")]
    [InlineData("DT", "dd/mm/yyyy;x", "02/01/2020")]
    public void Date_symbols_are_read_longest_first(string value, string format, string expected) =>
        AssertFormat(value, format, expected);

    [Theory]
    [InlineData("'abc'", "@@@@@@", "   abc")]
    [InlineData("'abc'", "!@@@@@@", "abc   ")]
    [InlineData("-1234.5678", "!@@@@@@", "4.5678")]
    [InlineData("'abc'", ">", "ABC")]
    [InlineData("'abc'", "<>", "abc")]
    [InlineData("'abc'", "@\"x\"", "axbc")]
    [InlineData("'ab'", "@&@&", " ab")]
    [InlineData("'ab'", "!@&@&", "ab ")]
    [InlineData("'abc'", ">\"x\"", "xABC")]
    [InlineData("'abc'", "\\@@", "@abc")]
    [InlineData("''", "@;\"empty\"", "empty")]
    [InlineData("NULL", "@;\"empty\"", "empty")]
    [InlineData("NULL", "@;@", " ")]
    [InlineData("0", "(@@@) @@@-@@@@", "(   )    -   0")]
    [InlineData("-1234.5678", "(@@@) @@@-@@@@", "(-12) 34.-5678")]
    [InlineData("-1234.5678", "@@-@@", "-1-234.5678")]
    [InlineData("0", "<@@@@@@@", "      0")]
    [InlineData("TRUE", "@@@", " -1")]
    [InlineData("DT", "<", "1/2/2020 12:00:00 pm")]
    public void Text_placeholders_fill_from_the_right(string value, string format, string expected) =>
        AssertFormat(value, format, expected);

    [Theory]
    [InlineData("FORMAT(DT, 'w', 2)", "4")]
    [InlineData("FORMAT(DT, 'ww', 2)", "1")]
    [InlineData("FORMAT(#2021-01-01#, 'ww', 1, 2)", "53")]
    [InlineData("FORMAT(#2021-01-01#, 'ww', 2, 3)", "52")]
    [InlineData("FORMAT(1234.5)", "1234.5")]
    [InlineData("FORMAT(NULL)", "")]
    [InlineData("FORMAT('abc', '@@@@@', 2)", "  abc")]
    public void Format_takes_week_settings(string expression, string expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("FORMATNUMBER(1234.5)", "1,234.50")]
    [InlineData("FORMATNUMBER(0.125)", "0.13")]
    [InlineData("FORMATNUMBER(2.5, 0)", "3")]
    [InlineData("FORMATNUMBER(-0.5, 0)", "-1")]
    [InlineData("FORMATNUMBER(-0.001, 2)", "0.00")]
    [InlineData("FORMATNUMBER(0.5, 2, 0)", ".50")]
    [InlineData("FORMATNUMBER(-1234.5678, 2, -2, -1)", "(1,234.57)")]
    [InlineData("FORMATNUMBER(-1234.5678, 2, -2, -2, 0)", "-1234.57")]
    [InlineData("FORMATNUMBER(1, 2.5)", "1.00")]
    [InlineData("FORMATNUMBER(TRUE)", "-1.00")]
    [InlineData("FORMATNUMBER(DT)", "43,832.50")]
    [InlineData("FORMATNUMBER(NULL)", "")]
    [InlineData("FORMATCURRENCY(1234.5)", "$1,234.50")]
    [InlineData("FORMATCURRENCY(0, 0)", "$0")]
    [InlineData("FORMATCURRENCY(0.5, 2, 0)", "$.50")]
    [InlineData("FORMATCURRENCY(-1234.5678, 2, -2, -1)", "($1,234.57)")]
    [InlineData("FORMATCURRENCY(-1234.5678, 2, -2, -2, 0)", "-$1234.57")]
    [InlineData("FORMATCURRENCY(-0.001, 2)", "$0.00")]
    [InlineData("FORMATPERCENT(0.25)", "25.00%")]
    [InlineData("FORMATPERCENT(1234.5678)", "123,456.78%")]
    [InlineData("FORMATPERCENT(-0.5, 2, -2, -1)", "(50.00%)")]
    [InlineData("FORMATPERCENT('12.5')", "1,250.00%")]
    [InlineData("FORMATPERCENT(-0.00001)", "0.00%")]
    public void Formatnumber_currency_and_percent_round_half_away_from_zero(string expression, string expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("FORMATDATETIME(DT)", "1/2/2020 12:00:00 PM")]
    [InlineData("FORMATDATETIME(#2020-06-15#)", "6/15/2020")]
    [InlineData("FORMATDATETIME(#13:45:30#)", "1:45:30 PM")]
    [InlineData("FORMATDATETIME(#13:45:30#, 1)", "Saturday, December 30, 1899")]
    [InlineData("FORMATDATETIME(DT, 2)", "1/2/2020")]
    [InlineData("FORMATDATETIME(DT, 3)", "12:00:00 PM")]
    [InlineData("FORMATDATETIME(#13:45:30#, 4)", "13:45")]
    [InlineData("FORMATDATETIME(#13:45:30#, 1.5)", "12/30/1899")]
    [InlineData("FORMATDATETIME(43832.5, '2')", "1/2/2020")]
    [InlineData("FORMATDATETIME(#0100-01-01#, 2)", "1/1/0100")]
    [InlineData("FORMATDATETIME(NULL, 3)", "")]
    public void Formatdatetime_writes_the_named_formats(string expression, string expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("FORMAT(1, '0', 8)")]
    [InlineData("FORMAT(1, 'Currency', 2, 4)")]
    [InlineData("FORMATNUMBER(1, -2)")]
    [InlineData("FORMATNUMBER(1, 2, 1)")]
    [InlineData("FORMATCURRENCY(1, 2, 5)")]
    [InlineData("FORMATDATETIME(DT, 5)")]
    [InlineData("FORMATDATETIME(NULL, -1)")]
    public void Out_of_range_settings_are_an_invalid_procedure_call(string expression) =>
        Assert.Throws<ArgumentException>(() => Scalar(expression));

    [Theory]
    [InlineData("FORMAT(1E+300, 'yyyy')")]
    [InlineData("FORMAT(123456789, '/')")]
    public void A_number_past_a_date_overflows_a_date_format(string expression) =>
        Assert.Throws<OverflowException>(() => Scalar(expression));

    [Theory]
    [InlineData("FORMATNUMBER('abc')")]
    [InlineData("FORMATDATETIME('abc')")]
    public void Text_that_is_not_a_number_is_a_type_mismatch(string expression) =>
        Assert.Throws<InvalidCastException>(() => Scalar(expression));

    [Theory]
    [InlineData("FORMAT(DT, NULL)")]
    [InlineData("FORMAT(DT, 'w', NULL)")]
    [InlineData("FORMATNUMBER(1, NULL)")]
    [InlineData("FORMATCURRENCY(1, 2, NULL)")]
    [InlineData("FORMATDATETIME(DT, NULL)")]
    public void A_null_setting_gives_null(string expression) =>
        Assert.Null(Scalar(expression));

    [Fact]
    public void A_time_literal_is_on_day_zero() =>
        Assert.Equal(new DateTime(1899, 12, 30, 13, 45, 30), Scalar("#13:45:30#"));
}
