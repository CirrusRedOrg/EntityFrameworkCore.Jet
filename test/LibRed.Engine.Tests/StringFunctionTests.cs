using System.Globalization;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// The string functions: how each reads a value that is not text, reads its positions and counts, compares text,
/// and — for Asc, Chr and their W and B forms — maps characters. The expected values were measured against ACE,
/// except that a Null argument gives Null where ACE raises an error.
/// </summary>
public class StringFunctionTests(StringFunctionTests.Database database)
    : TempDatabaseTest, IClassFixture<StringFunctionTests.Database>
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    private static readonly string[] Setup =
    [
        "CREATE TABLE T (Id LONG, DC DECIMAL(18,4), NT TEXT(60), DT DATETIME, G GUID, B BINARY(4))",
        "INSERT INTO T (Id, DC, DT) VALUES (1, 4.5, #2020-01-02#)",
        "UPDATE T SET G = {00112233-4455-6677-8899-AABBCCDDEEFF}",
        "UPDATE T SET B = 0x41004200",
    ];

    public sealed class Database() : SharedDatabase("string-functions-", Setup);

    // The code page and the regional formats come from the culture, so each query runs under en-US.
    private object? Scalar(string expression) => database.Scalar($"SELECT {expression} FROM T", EnUs);

    [Theory]
    [InlineData("UCASE(TRUE)", "-1")]
    [InlineData("MID(TRUE, 2)", "1")]
    [InlineData("STRREVERSE(TRUE)", "1-")]
    [InlineData("TRIM(DT)", "1/2/2020")]
    [InlineData("RIGHT(DT, 2)", "20")]
    [InlineData("TRIM(DC)", "4.5")]
    [InlineData("RIGHT(DC, 2)", ".5")]
    [InlineData("UCASE(CSNG(1/3))", "0.3333333")]
    [InlineData("TRIM(1/3)", "0.333333333333333")]
    [InlineData("LEFT(G, 5)", "{0011")]
    [InlineData("LCASE(G)", "{00112233-4455-6677-8899-aabbccddeeff}")]
    [InlineData("RIGHT(G, 2)", "F}")]
    [InlineData("LEFT(B, 2)", "AB")]
    [InlineData("MID(B, 2)", "B")]
    [InlineData("STRREVERSE(B)", "BA")]
    [InlineData("REPLACE(TRUE, '1', '2')", "-2")]
    public void A_value_that_is_not_text_is_read_as_cstr_writes_it(string expression, string expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("LEN(TRUE)", 2)]
    [InlineData("LEN(DT)", 8)]
    [InlineData("LEN(DC)", 3)]
    [InlineData("LEN(G)", 38)]
    [InlineData("LEN(B)", 2)]
    [InlineData("LENB(TRUE)", 4)]
    [InlineData("LENB(G)", 76)]
    [InlineData("ASC(TRUE)", 45)]
    [InlineData("ASC(G)", 123)]
    [InlineData("ASC(B)", 65)]
    [InlineData("INSTR(TRUE, '1')", 2)]
    [InlineData("INSTR(G, '1')", 4)]
    [InlineData("STRCOMP(TRUE, '-1')", 0)]
    [InlineData("STRCOMP(B, 'abc')", -1)]
    public void Lengths_and_positions_count_the_cstr_text(string expression, int expected) =>
        Assert.Equal(expected, Convert.ToInt32(Scalar(expression), CultureInfo.InvariantCulture));

    [Theory]
    [InlineData("CHR(65)", "A")]
    [InlineData("CHR(128)", "€")]
    [InlineData("CHR(130)", "‚")]
    [InlineData("CHR(150)", "–")]
    [InlineData("CHR(159)", "Ÿ")]
    [InlineData("CHR(255)", "ÿ")]
    [InlineData("CHRW(8364)", "€")]
    [InlineData("CHRW(233)", "é")]
    [InlineData("CHRW(65535)", "￿")]
    [InlineData("CHRW(-1)", "￿")]
    [InlineData("CHRW(-32768)", "耀")]
    [InlineData("STRING(3, 321)", "AAA")]
    [InlineData("STRING(3, -1)", "ÿÿÿ")]
    [InlineData("STRING(3, TRUE)", "ÿÿÿ")]
    [InlineData("STRING(3, 8364)", "¬¬¬")]
    [InlineData("STRING(3, 'xyz')", "xxx")]
    public void Chr_uses_the_ansi_code_page_and_chrw_utf16(string expression, string expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("ASC('€')", 128)]
    [InlineData("ASC('Ā')", 65)]
    [InlineData("ASC('İ')", 73)]
    [InlineData("ASC('あ')", 63)]
    [InlineData("ASC('ﬁ')", 63)]
    [InlineData("ASC(CHRW(8364))", 128)]
    [InlineData("ASC(CHRW(128))", 63)]
    [InlineData("ASCW(CHR(128))", 8364)]
    [InlineData("ASCW(CHR(159))", 376)]
    [InlineData("ASCW('ﬁ')", -1279)]
    [InlineData("ASCW(CHRW(-1))", -1)]
    [InlineData("ASCB('€')", 172)]
    public void Asc_gives_the_ansi_code_and_ascw_the_signed_utf16_unit(string expression, int expected) =>
        Assert.Equal(expected, Convert.ToInt32(Scalar(expression), CultureInfo.InvariantCulture));

    [Theory]
    [InlineData("CHR(256)")]
    [InlineData("CHR(-1)")]
    [InlineData("CHR(TRUE)")]
    [InlineData("CHR(8364)")]
    [InlineData("CHRW(65536)")]
    [InlineData("CHRW(-32769)")]
    [InlineData("ASC('')")]
    [InlineData("ASCW('')")]
    [InlineData("STRING(3, '')")]
    [InlineData("STRING(-1, 'x')")]
    [InlineData("STRING(TRUE, 'x')")]
    [InlineData("SPACE(-1)")]
    [InlineData("LEFT('abcdef', TRUE)")]
    [InlineData("MID('abcdef', TRUE)")]
    [InlineData("MID('abc', 0)")]
    [InlineData("INSTR(0, 'abcabc', 'b')")]
    [InlineData("INSTR(-1, 'abcabc', 'b')")]
    [InlineData("REPLACE('abcabc', 'b', 'X', TRUE)")]
    [InlineData("MIDB('abc', 0)")]
    [InlineData("MIDB('abc', -1)")]
    [InlineData("MIDB('abc', 1, -1)")]
    [InlineData("LEFTB('abc', -1)")]
    [InlineData("RIGHTB('abc', -1)")]
    [InlineData("INSTRB(0, 'abc', 'b')")]
    [InlineData("INSTRB(-1, 'abc', 'b')")]
    [InlineData("INSTR(1, 'aBc', 'b', 2)")]
    [InlineData("INSTR(1, 'aBc', 'b', -1)")]
    [InlineData("INSTR(1, 'aBc', 'b', 1.5)")]
    [InlineData("INSTR(1, 'aBc', 'b', 20000)")]
    [InlineData("STRCOMP('a', 'A', 2)")]
    [InlineData("STRCOMP('a', 'A', TRUE)")]
    public void An_argument_out_of_range_is_an_invalid_procedure_call(string expression) =>
        Assert.Throws<ArgumentException>(() => Scalar(expression));

    [Theory]
    [InlineData("LEFT('abcdef', '$2')", "ab")]
    [InlineData("LEFT('abcdef', 2.5)", "ab")]
    [InlineData("LEFT('abcdef', 3.5)", "abcd")]
    [InlineData("LEFT('abcdef', #1899-12-31#)", "a")]
    [InlineData("MID('abcdef', 2, '$2')", "bc")]
    [InlineData("SPACE('$2') & '|'", "  |")]
    [InlineData("STRING('$2', 'x')", "xx")]
    [InlineData("REPLACE('abcabc', 'b', 'X', '$2')", "XcaXc")]
    [InlineData("REPLACE('abcabc', 'b', 'X', 1, TRUE)", "aXcaXc")]
    [InlineData("REPLACE('abcabc', 'b', 'X', 1, #1899-12-31#)", "aXcabc")]
    [InlineData("LEFTB('abc', '$2')", "a")]
    [InlineData("MIDB('abc', '$3')", "bc")]
    [InlineData("RIGHTB('abc', 2.5)", "c")]
    [InlineData("RIGHTB('abc', 0)", "")]
    public void Positions_and_counts_are_read_as_numbers(string expression, string expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("INSTR('$2', 'abcabc', 'b')", 2)]
    [InlineData("INSTR(#1899-12-31#, 'abcabc', 'b')", 2)]
    [InlineData("INSTRREV('abcabc', 'b', '$2')", 2)]
    [InlineData("INSTRREV('abcabc', 'b', TRUE)", 5)]
    [InlineData("INSTRREV('abcabc', 'b', 7)", 0)]
    [InlineData("INSTR(5, 'abc', '')", 5)]
    [InlineData("INSTR(4, 'abc', '')", 4)]
    [InlineData("INSTR('', '')", 0)]
    [InlineData("INSTR(1, 'aBc', 'b', 0)", 0)]
    [InlineData("INSTR(1, 'aBc', 'b', 1)", 2)]
    [InlineData("INSTR(1, 'aBc', 'b', 3.5)", 2)]
    [InlineData("INSTR(1, 'aBc', 'b', 1033)", 2)]
    [InlineData("INSTR(1, 'aBc', 'b', #1899-12-31#)", 2)]
    [InlineData("INSTRB('$2', 'abc', 'b')", 3)]
    [InlineData("INSTRB(7, 'abc', 'b')", 0)]
    [InlineData("INSTR(1, 'ßx', 'SS', 1033)", 1)]
    [InlineData("INSTR(1, 'ßx', 'SS', 0)", 0)]
    [InlineData("STRCOMP('a', 'A', 1031)", 0)]
    public void Positions_and_compare_modes_are_read_as_numbers(string expression, int expected) =>
        Assert.Equal(expected, Convert.ToInt32(Scalar(expression), CultureInfo.InvariantCulture));

    [Theory]
    [InlineData("INSTR('straße', 'SS')", 5)]
    [InlineData("INSTR('STRASSE', 'ß')", 5)]
    [InlineData("INSTR('ÆØ', 'ae')", 1)]
    [InlineData("INSTR('Éclair', 'e')", 0)]
    [InlineData("STRCOMP('ß', 'ss')", 0)]
    [InlineData("STRCOMP('Æ', 'AE')", 0)]
    [InlineData("STRCOMP('a-b', 'ab')", 1)]
    [InlineData("STRCOMP('ab', 'a-b')", -1)]
    [InlineData("STRCOMP('co-op', 'coop')", 1)]
    [InlineData("STRCOMP('ß', 'ss', 1033)", 0)]
    [InlineData("STRCOMP('é', 'E')", 1)]
    [InlineData("STRCOMP('é', 'É')", 0)]
    [InlineData("STRCOMP('a', 'a ')", -1)]
    [InlineData("STRCOMP('a', 'B', 0)", 1)]
    public void Text_compares_in_the_database_sort_order(string expression, int expected) =>
        Assert.Equal(expected, Convert.ToInt32(Scalar(expression), CultureInfo.InvariantCulture));

    [Theory]
    [InlineData("'ß' = 'ss'", true)]
    [InlineData("'a-b' > 'ab'", true)]
    [InlineData("'a-b' < 'ac'", true)]
    [InlineData("'é' < 'f'", true)]
    [InlineData("'café' = 'cafe'", false)]
    public void Comparison_operators_use_the_same_order(string expression, bool expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("REPLACE('straße', 'SS', '-')", "stra-e")]
    [InlineData("REPLACE('aXbxc', 'x', '-')", "a-b-c")]
    [InlineData("REPLACE('aXbxc', 'x', '-', 1, -1, 0)", "aXb-c")]
    [InlineData("REPLACE('abcabc', 'b', 'X', 3)", "caXc")]
    [InlineData("REPLACE('abcabc', 'b', 'X', 7)", "")]
    [InlineData("REPLACE('aBc', 'b', 'X', 1, -1, 3)", "aXc")]
    public void Replace_finds_text_in_the_database_sort_order(string expression, string expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("MID('abcdef', 2, NULL)")]
    [InlineData("INSTR(NULL, 'abcabc', 'b')")]
    [InlineData("INSTR(1, 'aBc', 'b', NULL)")]
    [InlineData("STRCOMP('a', 'A', NULL)")]
    [InlineData("SPACE(NULL)")]
    [InlineData("STRING(NULL, 'x')")]
    [InlineData("STRING(3, NULL)")]
    [InlineData("CHR(NULL)")]
    [InlineData("REPLACE('abcabc', 'b', 'X', 1, NULL)")]
    [InlineData("ASC(NT)")]
    [InlineData("STRREVERSE(NULL)")]
    [InlineData("LEFTB('abc', NULL)")]
    [InlineData("INSTRB(NULL, 'abc', 'b')")]
    public void Null_gives_null(string expression) =>
        Assert.Null(Scalar(expression));
}
