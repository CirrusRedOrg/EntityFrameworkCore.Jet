using System.Globalization;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// Unary minus and plus, and the bitwise operators <c>BNOT</c> <c>BAND</c> <c>BOR</c> <c>BXOR</c>: how each reads its
/// operand, the widths the bits are combined at, and where the operators bind. The expected values were measured
/// against ACE, except where ACE reads the raw bytes of a value that is not a whole number and returns garbage or
/// crashes; LibRed reads such a value as a number there.
/// </summary>
public class UnaryAndBitwiseOperatorTests(UnaryAndBitwiseOperatorTests.Database database)
    : TempDatabaseTest, IClassFixture<UnaryAndBitwiseOperatorTests.Database>
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    private static readonly string[] Setup =
    [
        "CREATE TABLE T (Id LONG, BT BYTE, SI SHORT, LG LONG, DC DECIMAL(18,4), TN TEXT(60), MM MEMO, NT TEXT(60), "
            + "DT DATETIME, YN YESNO, G GUID)",
        "INSERT INTO T (Id, BT, SI, LG, DC, TN, MM, DT, YN) VALUES (1, 1, 2, 3, 4.5, '7', '8', #2020-01-02 12:00:00#, TRUE)",
        "UPDATE T SET G = {00112233-4455-6677-8899-AABBCCDDEEFF}",
    ];

    public sealed class Database() : SharedDatabase("unary-bitwise-", Setup);

    // Text is read as a number in the regional separators, so each query runs under en-US whatever the machine's
    // culture.
    private object? Query(string sql) => database.Scalar(sql, EnUs);

    private object? Scalar(string expression) => Query($"SELECT {expression} FROM T");

    [Theory]
    [InlineData("1 BOR 40000", 40001)]
    [InlineData("12 BAND 10", 8)]
    [InlineData("12 BXOR 10", 6)]
    [InlineData("BT BOR SI", 3)]
    [InlineData("40000 BAND BT", 0)]
    [InlineData("-1 BAND 3", 3)]
    [InlineData("CINT(-2) BAND CINT(-3)", -4)]
    [InlineData("TRUE BAND 1", 1)]
    [InlineData("TRUE BXOR SI", -3)]
    [InlineData("TRUE BOR BT", -1)]
    public void Whole_numbers_combine_their_bits(string expression, int expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("1 BOR TRUE", 65535)]
    [InlineData("BT BOR TRUE", 65535)]
    [InlineData("40000 BXOR TRUE", 25535)]
    [InlineData("LG BXOR TRUE", 65532)]
    [InlineData("1 BOR CINT(-2)", 65535)]
    [InlineData("CLNG(-1) BAND CINT(1)", -65535)]
    [InlineData("70000 BAND CINT(1)", 65536)]
    [InlineData("70000 BOR TRUE", 131071)]
    [InlineData("70000 BXOR TRUE", 126607)]
    [InlineData("CLNG(-40000) BAND TRUE", -40000)]
    [InlineData("CLNG(-40000) BAND FALSE", -65536)]
    [InlineData("CLNG(70000) BOR CINT(-32768)", 102768)]
    public void A_sixteen_bit_right_operand_changes_only_the_low_sixteen_bits(string expression, int expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("TRUE BAND 40000", -25536)]
    [InlineData("SI BOR 40000", -25534)]
    [InlineData("CINT(1) BOR 70000", 4465)]
    [InlineData("FALSE BOR 70000", 4464)]
    [InlineData("TRUE BAND -40000", 25536)]
    [InlineData("CINT(-2) BOR 1", -1)]
    public void A_sixteen_bit_left_operand_gives_a_sixteen_bit_result(string expression, int expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("'2.5' BOR 1", 3)]
    [InlineData("'2.5' BAND '2.5'", 2)]
    [InlineData("'1' BAND '2.5'", 0)]
    [InlineData("'2.5' BOR DC", 6)]
    [InlineData("TN BAND 1", 1)]
    [InlineData("1.5 BAND '2.5'", 2)]
    [InlineData("DT BAND 40000", 34816)]
    [InlineData("1 BOR DT", 43833)]
    [InlineData("1.5 BOR DT", 43834)]
    [InlineData("#2020-01-02# BAND MM", 8)]
    [InlineData("#2020-01-02# BOR #2020-01-02#", 43832)]
    public void Other_values_are_read_as_whole_numbers_rounding_half_to_even(string expression, int expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("NULL BAND 0")]
    [InlineData("0 BOR NULL")]
    [InlineData("NULL BXOR NULL")]
    [InlineData("'abc' BAND NULL")]
    [InlineData("NT BAND 1")]
    [InlineData("BNOT NULL")]
    [InlineData("BNOT NT")]
    public void Null_gives_null(string expression) =>
        Assert.Null(Scalar(expression));

    [Theory]
    [InlineData("'abc' BAND 1")]
    [InlineData("1 BOR ''")]
    [InlineData("G BAND 1")]
    [InlineData("BNOT 'abc'")]
    [InlineData("BNOT G")]
    public void A_value_that_is_not_a_number_is_a_type_mismatch(string expression) =>
        Assert.Throws<InvalidCastException>(() => Scalar(expression));

    [Theory]
    [InlineData("2147483647.5 BAND 1")]
    [InlineData("'2147483648' BAND 1")]
    [InlineData("BNOT 1E10")]
    [InlineData("BNOT 2147483647.5")]
    [InlineData("BNOT '-2147483649'")]
    public void A_whole_number_past_a_long_is_an_overflow(string expression) =>
        Assert.Throws<OverflowException>(() => Scalar(expression));

    [Theory]
    [InlineData("BNOT 5", -6)]
    [InlineData("BNOT TRUE", 0)]
    [InlineData("BNOT FALSE", -1)]
    [InlineData("BNOT SI", -3)]
    [InlineData("BNOT CINT(-32768)", 32767)]
    [InlineData("BNOT CLNG(-2147483648)", 2147483647)]
    [InlineData("BNOT 1.5", -3)]
    [InlineData("BNOT 2.5", -3)]
    [InlineData("BNOT -0.5", -1)]
    [InlineData("BNOT -1.5", 1)]
    [InlineData("BNOT -2147483648.5", 2147483647)]
    [InlineData("BNOT 2147483647.4", -2147483648)]
    [InlineData("BNOT '7'", -8)]
    [InlineData("BNOT '2.5'", -3)]
    [InlineData("BNOT #2020-01-02#", -43833)]
    [InlineData("BNOT DT", -43833)]
    [InlineData("BNOT BNOT 5", 5)]
    [InlineData("-BNOT 5", 6)]
    [InlineData("BNOT -5", 4)]
    public void Bnot_flips_every_bit(string expression, int expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("BNOT 1 + 1", -3)]
    [InlineData("BNOT 2 * 3", -7)]
    [InlineData("BNOT 2 ^ 2", -5)]
    [InlineData("BNOT 3 = 3 + 10", -1)]
    [InlineData("BNOT 0 IS NULL", -1)]
    [InlineData("BNOT NULL IS NULL", 0)]
    [InlineData("BNOT 0 BAND 3", 3)]
    [InlineData("NOT 0 BAND 1", 1)]
    [InlineData("NOT 1 BAND 2", 0)]
    [InlineData("2 AND 1 BAND 3", 3)]
    [InlineData("0 OR 0 BOR 4", 4)]
    [InlineData("0 XOR 0 BXOR 4", 4)]
    [InlineData("4 BOR 0 AND 0", 4)]
    [InlineData("0 AND 0 BOR 4", 4)]
    [InlineData("4 BXOR 0 OR 0", 4)]
    [InlineData("0 OR 0 BXOR 4", 4)]
    [InlineData("5 BOR 3 BAND 8", 5)]
    [InlineData("5 BXOR 3 BAND 1", 4)]
    [InlineData("5 BXOR 3 BOR 1", 6)]
    [InlineData("5 BAND 3 BOR 8", 9)]
    [InlineData("1 BAND 3 = 1", 0)]
    [InlineData("3 BAND 1 = 1", 3)]
    [InlineData("12 BAND 10 + 1", 8)]
    [InlineData("1 BAND 3 IS NULL", 0)]
    public void Bnot_sits_with_not_and_each_bitwise_operator_with_its_logical_one(string expression, int expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("3 BAND 1 AND 2", true)]
    [InlineData("4 BOR 0 OR 0", true)]
    [InlineData("4 BXOR 0 XOR 0", true)]
    [InlineData("4 EQV 4 BXOR 4", false)]
    [InlineData("0 BXOR 4 EQV 4", true)]
    public void A_logical_operator_applied_last_gives_a_boolean(string expression, bool expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("-'2.5'", -2.5)]
    [InlineData("-'1e2'", -100)]
    [InlineData("-'&H10'", -16)]
    [InlineData("-'1,000'", -1000)]
    [InlineData("-'($5)'", 5)]
    [InlineData("-' 1 '", -1)]
    [InlineData("-TN", -7)]
    [InlineData("-'2147483648'", -2147483648)]
    [InlineData("-TRUE", 1)]
    [InlineData("-FALSE", 0)]
    [InlineData("-SI", -2)]
    [InlineData("-BT", -1)]
    [InlineData("-DC", -4.5)]
    [InlineData("-CINT(-32767)", 32767)]
    [InlineData("-CLNG(-2147483647)", 2147483647)]
    [InlineData("-CBYTE(255)", -255)]
    [InlineData("-CCUR(1)", -1)]
    [InlineData("-(-32767 - 1)", 32768)]
    [InlineData("-SI - 32767", -32769)]
    [InlineData("-(SI * 16384)", -32768)]
    [InlineData("--2", 2)]
    [InlineData("---2", -2)]
    [InlineData("--2 ^ 2", -4)]
    [InlineData("--.5", 0.5)]
    [InlineData("1--2", 3)]
    [InlineData("- - -2", -2)]
    [InlineData("-(-(-2))", -2)]
    public void Minus_negates_the_value_read_as_a_number(string expression, double expected) =>
        Assert.Equal(expected, Convert.ToDouble(Scalar(expression), CultureInfo.InvariantCulture));

    [Theory]
    [InlineData("-#2020-01-02#", "1779-12-27 00:00:00")]
    [InlineData("-DT", "1779-12-27 12:00:00")]
    [InlineData("-#1899-12-30 12:00#", "1899-12-30 12:00:00")]
    public void Minus_negates_a_dates_serial_and_keeps_a_date(string expression, string expected) =>
        Assert.Equal(expected, Assert.IsType<DateTime>(Scalar(expression)).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

    [Theory]
    [InlineData("-CINT(-32768)")]
    [InlineData("-CLNG(-2147483648)")]
    public void Minus_past_the_operands_range_is_an_overflow(string expression) =>
        Assert.Throws<OverflowException>(() => Scalar(expression));

    [Theory]
    [InlineData("-'abc'")]
    [InlineData("-''")]
    [InlineData("-G")]
    public void Minus_on_a_value_that_is_not_a_number_is_a_type_mismatch(string expression) =>
        Assert.Throws<InvalidCastException>(() => Scalar(expression));

    [Theory]
    [InlineData("-NULL")]
    [InlineData("-NT")]
    [InlineData("+NULL")]
    public void Minus_and_plus_of_null_are_null(string expression) =>
        Assert.Null(Scalar(expression));

    [Theory]
    [InlineData("-2 ^ 2", 4)]
    [InlineData("-2 ^ 3", -8)]
    [InlineData("-2.5 ^ 2", 6.25)]
    [InlineData("-2E0 ^ 2", 4)]
    [InlineData("-0.5 ^ 2", 0.25)]
    [InlineData("-2 ^ 2 ^ 2", 16)]
    [InlineData("2 ^ -2 ^ 2", 0.0625)]
    [InlineData("2 * -2 ^ 2", 8)]
    [InlineData("-2 ^ -2", 0.25)]
    [InlineData("- 2 ^ 2", -4)]
    [InlineData("-(2) ^ 2", -4)]
    [InlineData("-.5 ^ 2", -0.25)]
    [InlineData("-SI ^ 2", -4)]
    [InlineData("-'2' ^ 2", -4)]
    [InlineData("-CINT(2) ^ 2", -4)]
    [InlineData("- -2 ^ 2", -4)]
    [InlineData("-(2 ^ 2)", -4)]
    [InlineData("3 - 2 ^ 2", -1)]
    [InlineData("1 - -1", 2)]
    [InlineData("1 - - 1", 2)]
    [InlineData("- 2 - 3", -5)]
    [InlineData("-2 MOD 3", -2)]
    [InlineData("-7 \\ 2", -3)]
    [InlineData("+2 ^ 2", 4)]
    [InlineData("+-2 ^ 2", 4)]
    public void A_minus_against_a_number_is_part_of_it_otherwise_it_binds_below_power(string expression, double expected) =>
        Assert.Equal(expected, Convert.ToDouble(Scalar(expression), CultureInfo.InvariantCulture));

    [Theory]
    [InlineData("+1", 1)]
    [InlineData("+(1)", 1)]
    [InlineData("+1 + +1", 2)]
    [InlineData("1 + + 1", 2)]
    [InlineData("1 - + 1", 0)]
    [InlineData("2 * +3", 6)]
    [InlineData("-+1", -1)]
    [InlineData("+-1", -1)]
    [InlineData("+-+1", -1)]
    [InlineData("+SI", 2)]
    [InlineData("+'1' + 1", 2)]
    public void Plus_leaves_a_number_as_it_is(string expression, double expected) =>
        Assert.Equal(expected, Convert.ToDouble(Scalar(expression), CultureInfo.InvariantCulture));

    [Fact]
    public void Plus_leaves_text_and_other_values_as_they_are()
    {
        Assert.Equal("abc", Scalar("+'abc'"));
        Assert.Equal("1x", Scalar("+'1' & 'x'"));
        Assert.Equal(Guid.Parse("00112233-4455-6677-8899-AABBCCDDEEFF"), Scalar("+G"));
        Assert.Equal(new DateTime(2020, 1, 2, 12, 0, 0), Scalar("+DT"));
    }

    [Theory]
    [InlineData("-- tag\nSELECT 1 FROM T")]
    [InlineData("--\tTag\r\nSELECT 1 FROM T")]
    [InlineData("--\nSELECT 1 FROM T")]
    [InlineData("SELECT 1 FROM T --")]
    [InlineData("SELECT 1 FROM T -- trailing")]
    [InlineData("--Before\nSELECT 1 FROM T")]
    [InlineData("---tag\nSELECT 1 FROM T")]
    public void Two_dashes_start_a_comment_unless_a_number_follows(string sql) =>
        Assert.Equal(1, Query(sql));

    [Theory]
    [InlineData("BNOT 0", 1)]
    [InlineData("BNOT -1", 0)]
    [InlineData("1 BAND 2", 0)]
    [InlineData("1 BAND 3", 1)]
    [InlineData("-1", 1)]
    [InlineData("-0", 0)]
    [InlineData("-'0'", 0)]
    [InlineData("-NULL", 0)]
    [InlineData("NULL BAND 0", 0)]
    public void A_where_condition_uses_the_same_values(string condition, int expected) =>
        Assert.Equal(expected, Query($"SELECT COUNT(*) FROM T WHERE {condition}"));
}
