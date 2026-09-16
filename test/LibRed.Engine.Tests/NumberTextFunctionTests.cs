using System.Globalization;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// <c>Str</c> <c>Val</c> <c>Hex</c> <c>Oct</c> <c>StrConv</c>. The expected values were measured against ACE under
/// en-AU, except that a Null argument gives Null where ACE raises an error.
/// </summary>
public class NumberTextFunctionTests(NumberTextFunctionTests.Database database)
    : TempDatabaseTest, IClassFixture<NumberTextFunctionTests.Database>
{
    private static readonly string[] Setup =
    [
        "CREATE TABLE T (Id LONG, NT TEXT(60), DT DATETIME, G GUID, B BINARY(4))",
        "INSERT INTO T (Id, DT) VALUES (1, #2020-01-02 12:00:00#)",
        "UPDATE T SET G = {00112233-4455-6677-8899-AABBCCDDEEFF}",
        "UPDATE T SET B = 0x41004200",
    ];

    public sealed class Database() : SharedDatabase("numbertext-", Setup);

    // Dates are written in the regional format, so each query runs under en-AU whatever the machine's culture. .NET's
    // en-AU short date is d/M/yyyy, where Windows' (and so ACE's) is d/MM/yyyy.
    private object? Scalar(string expression) =>
        database.Scalar($"SELECT {expression} FROM T", CultureInfo.GetCultureInfo("en-AU"));

    [Theory]
    [InlineData("STR(1)", " 1")]
    [InlineData("STR(-1)", "-1")]
    [InlineData("STR(TRUE)", "-1")]
    [InlineData("STR(FALSE)", " 0")]
    [InlineData("STR(0.5)", " .5")]
    [InlineData("STR(-0.5)", "-.5")]
    [InlineData("STR(CCUR(-0.25))", "-.25")]
    [InlineData("STR(CCUR(1.5))", " 1.5")]
    [InlineData("STR(CINT(-5))", "-5")]
    [InlineData("STR(CDBL(1) / 3)", " .333333333333333")]
    [InlineData("STR(CDBL(2) / 3 * 10 ^ 14)", " 66666666666666.7")]
    [InlineData("STR(CDBL(2) / 3 / 10 ^ 5)", " 6.66666666666667E-06")]
    [InlineData("STR(CDBL(1) / 10 ^ 15)", " .000000000000001")]
    [InlineData("STR(CDBL(1) / 10 ^ 16)", " 1E-16")]
    [InlineData("STR(CDBL(123456789012345))", " 123456789012345")]
    [InlineData("STR(CDBL(1234567890123456))", " 1.23456789012346E+15")]
    [InlineData("STR(CDBL(-1.5) / 10 ^ 20)", "-1.5E-20")]
    [InlineData("STR(CDBL(5E-324))", " 4.94065645841247E-324")]
    [InlineData("STR(CSNG(0.1))", " .1")]
    [InlineData("STR(CSNG(1) / CSNG(10 ^ 8))", " 1E-08")]
    [InlineData("STR(CSNG(2) / CSNG(3))", " .6666667")]
    [InlineData("STR(CSNG(2) / CSNG(3) * CSNG(1000000))", " 666666.7")]
    [InlineData("STR(CSNG(1E-5))", " .00001")]
    [InlineData("STR(CSNG(1E-10))", " 1E-10")]
    [InlineData("STR(CSNG(1234567))", " 1234567")]
    [InlineData("STR(CSNG(12345678))", " 1.234568E+07")]
    [InlineData("STR(CSNG(-1E+15))", "-1E+15")]
    [InlineData("STR(CSNG(3.4E+38))", " 3.4E+38")]
    [InlineData("STR('&HFF')", " 255")]
    [InlineData("STR('$5')", " 5")]
    [InlineData("STR('1,000')", " 1000")]
    [InlineData("STR(#2020-01-02#)", "2/1/2020")]
    [InlineData("STR(DT)", "2/1/2020 12:00:00 pm")]
    public void Str_writes_a_period_and_a_sign_column(string expression, string expected) =>
        Assert.Equal(expected, Scalar(expression) as string, ignoreCase: true);

    [Theory]
    [InlineData("VAL('  -  5')", -5)]
    [InlineData("VAL('3 .1 4')", 3.14)]
    [InlineData("VAL(CHR(13) & '12')", 12)]
    [InlineData("VAL(CHR(11) & '12')", 0)]
    [InlineData("VAL(CHR(160) & '12')", 0)]
    [InlineData("VAL('1' & CHR(160) & '2')", 1)]
    [InlineData("VAL('1' & CHR(0) & '2')", 1)]
    [InlineData("VAL('1d2')", 100)]
    [InlineData("VAL('1D2')", 100)]
    [InlineData("VAL('1d')", 1)]
    [InlineData("VAL('1.5e2.5')", 150)]
    [InlineData("VAL('1e-400')", 0)]
    [InlineData("VAL('&HFF')", 255)]
    [InlineData("VAL('&H 1F')", 31)]
    [InlineData("VAL('&HFFg')", 255)]
    [InlineData("VAL('&H7FFF')", 32767)]
    [InlineData("VAL('&H8000')", -32768)]
    [InlineData("VAL('&HFFFF')", -1)]
    [InlineData("VAL('&H0000FFFF')", -1)]
    [InlineData("VAL('&H10000')", 65536)]
    [InlineData("VAL('&HFFFF1')", 1048561)]
    [InlineData("VAL('&HFFFFFFFF')", -1)]
    [InlineData("VAL('&H123456789')", 591751049)]
    [InlineData("VAL('&HFFFFFFFFF')", -1)]
    [InlineData("VAL('&O177777')", -1)]
    [InlineData("VAL('&O100000')", -32768)]
    [InlineData("VAL('&O77777777777')", -1)]
    [InlineData("VAL('-&HFF')", 0)]
    [InlineData("VAL('&B101')", 0)]
    [InlineData("VAL(TRUE)", -1)]
    [InlineData("VAL(G)", 0)]
    [InlineData("VAL(DT)", 2)]
    [InlineData("VAL(0.1)", 0.1)]
    public void Val_reads_the_number_at_the_start(string expression, double expected) =>
        Assert.Equal(expected, Assert.IsType<double>(Scalar(expression)), 12);

    [Theory]
    [InlineData("HEX(TRUE)", "FFFF")]
    [InlineData("OCT(TRUE)", "177777")]
    [InlineData("HEX(CINT(-1))", "FFFF")]
    [InlineData("OCT(CINT(-1))", "177777")]
    [InlineData("HEX(-1)", "FFFFFFFF")]
    [InlineData("OCT(-1)", "37777777777")]
    [InlineData("HEX(-32769)", "FFFF7FFF")]
    [InlineData("HEX(CBYTE(255))", "FF")]
    [InlineData("HEX(CCUR(-1))", "FFFFFFFFFFFFFFFF")]
    [InlineData("HEX(CSNG(-1))", "FFFFFFFFFFFFFFFF")]
    [InlineData("HEX('-1')", "FFFFFFFFFFFFFFFF")]
    [InlineData("HEX('-1.5')", "FFFFFFFFFFFFFFFE")]
    [InlineData("HEX('2.5')", "2")]
    [InlineData("HEX(-0.5)", "0")]
    [InlineData("HEX(4294967296)", "100000000")]
    [InlineData("HEX(#2020-01-02#)", "AB38")]
    [InlineData("OCT(DT)", "125470")]
    [InlineData("HEX(-#2020-01-02#)", "FFFFFFFFFFFF54C8")]
    [InlineData("HEX('&O17')", "F")]
    [InlineData("OCT('1e3')", "1750")]
    [InlineData("HEX('1,000')", "3E8")]
    [InlineData("HEX('$5')", "5")]
    public void Hex_and_oct_write_the_bits_of_the_type(string expression, string expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("STRCONV('hello wORLD', 0)", "hello wORLD")]
    [InlineData("STRCONV(CHRW(454), 0)", "ǆ")]
    [InlineData("STRCONV(TRUE, 1)", "-1")]
    [InlineData("STRCONV(#2020-01-02#, 2)", "2/1/2020")]
    [InlineData("STRCONV(B, 2)", "ab")]
    [InlineData("STRCONV(B, 3)", "Ab")]
    [InlineData("STRCONV('ß', 1)", "ß")]
    [InlineData("STRCONV('İSTANBUL', 2)", "istanbul")]
    [InlineData("STRCONV(CHRW(1044) & CHRW(1076), 2)", "??")]
    [InlineData("STRCONV('ǆa', 3)", "?a")]
    [InlineData("STRCONV('ABC' & CHRW(8364) & 'É', 2)", "abc€é")]
    [InlineData("STRCONV('ÉCOLE élan', 3)", "École Élan")]
    [InlineData("STRCONV('o''neil mc-donald', 3)", "O'neil Mc-donald")]
    [InlineData("STRCONV('hello_world', 3)", "Hello_world")]
    [InlineData("STRCONV('123abc def', 3)", "123abc Def")]
    [InlineData("STRCONV('a' & CHRW(160) & 'b', 3)", "A b")]
    [InlineData("STRCONV('a' & CHR(0) & 'b c' & CHR(13) & 'd' & CHR(10) & 'e' & CHR(11) & 'f' & CHR(12) & 'g.h,i(j' & CHR(9) & 'k-l/m', 3)",
        "A\0B C\rD\nE\vF\fG.h,i(j\tK-l/m")]
    [InlineData("STRCONV('abc', 3, 1055)", "Abc")]
    [InlineData("STRCONV('iii', 1, 1055)", "III")]
    [InlineData("STRCONV('abc', 1, 0)", "ABC")]
    [InlineData("STRCONV(TRUE, 64)", "-\01\0")]
    [InlineData("STRCONV(TRUE, 128)", "ㄭ")]
    [InlineData("STRCONV(B, 128)", "䉁")]
    [InlineData("STRCONV(STRCONV('abcd', 128), 64)", "abcd")]
    public void Strconv_converts_in_the_ansi_code_page(string expression, string expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("ASCW(STRCONV('€a', 128))", 24960)]
    [InlineData("ASCW(MID(STRCONV(CHRW(8364), 64), 1, 1))", 172)]
    [InlineData("ASCW(MID(STRCONV(CHRW(8364), 64), 2, 1))", 32)]
    [InlineData("LEN(STRCONV('abc', 128))", 1)]
    public void Strconv_unicode_conversions_work_on_bytes(string expression, int expected) =>
        Assert.Equal(expected, Convert.ToInt32(Scalar(expression), CultureInfo.InvariantCulture));

    [Theory]
    [InlineData("STRCONV('abc', 4)")]
    [InlineData("STRCONV('abc', 5)")]
    [InlineData("STRCONV('abc', 65)")]
    [InlineData("STRCONV('abc', 256)")]
    [InlineData("STRCONV('abc', -1)")]
    [InlineData("STRCONV(NULL, 256)")]
    [InlineData("STRCONV(NULL, -1)")]
    [InlineData("STRCONV('abc', 1, 20000)")]
    [InlineData("STRCONV('abc', 1, -1)")]
    public void Unsupported_conversions_are_an_invalid_procedure_call(string expression) =>
        Assert.Throws<ArgumentException>(() => Scalar(expression));

    [Theory]
    [InlineData("STR('abc')")]
    [InlineData("HEX('abc')")]
    [InlineData("STR(G)")]
    public void Text_that_is_not_a_number_is_a_type_mismatch(string expression) =>
        Assert.Throws<InvalidCastException>(() => Scalar(expression));

    [Theory]
    [InlineData("VAL('1e400')")]
    [InlineData("HEX(1E+20)")]
    public void Values_past_their_type_overflow(string expression) =>
        Assert.Throws<OverflowException>(() => Scalar(expression));

    [Theory]
    [InlineData("STR(NULL)")]
    [InlineData("VAL(NULL)")]
    [InlineData("VAL(NT)")]
    [InlineData("HEX(NULL)")]
    [InlineData("STRCONV(NULL, 1)")]
    [InlineData("STRCONV(NULL, 4)")]
    [InlineData("STRCONV(NULL, 65)")]
    [InlineData("STRCONV('abc', NULL)")]
    [InlineData("STRCONV('abc', 1, NULL)")]
    public void A_null_argument_gives_null(string expression) =>
        Assert.Null(Scalar(expression));

    // ACE keeps an odd trailing byte inside an expression; LibRed text has none, so it is dropped.
    [Fact]
    public void Strconv_from_unicode_drops_an_odd_byte() =>
        Assert.Equal("ab", Scalar("STRCONV(STRCONV('abc', 128), 64)"));
}
