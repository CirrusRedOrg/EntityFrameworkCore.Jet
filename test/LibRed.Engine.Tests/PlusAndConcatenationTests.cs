using System.Globalization;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// The <c>+</c> and <c>&amp;</c> operators as ACE evaluates them. <c>+</c> concatenates only two texts and
/// otherwise adds, reading text as a number; <c>&amp;</c> writes each operand as text and binds looser than
/// <c>+</c>. The expected values were measured against ACE.
/// </summary>
public class PlusAndConcatenationTests : TempDatabaseTest
{
    private const string Guid = "{00112233-4455-6677-8899-AABBCCDDEEFF}";

    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "plus-concat-");
        var engine = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        engine.ExecuteNonQuery(
            "CREATE TABLE T (Id LONG, N LONG, S TEXT(60), NT TEXT(60), D DATETIME, G GUID, B BINARY(4), M DECIMAL(18,4))");
        engine.ExecuteNonQuery("INSERT INTO T (Id, N, S, D, M) VALUES (1, 3, 'a', #2020-01-02 12:00:00#, 4.5)");
        engine.ExecuteNonQuery($"UPDATE T SET G = {Guid}");
        engine.ExecuteNonQuery("UPDATE T SET B = 0x41004200");
        return engine;
    }

    private static object? Scalar(string expression) => Scalar(Fresh(), expression);

    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    // Both operators follow the regional settings (date format, separators, currency symbol), so each
    // expression is evaluated under en-US whatever the machine's culture.
    private static object? Scalar(QueryEngine engine, string expression)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = EnUs;
        try
        {
            return engine.ExecuteQuery($"SELECT {expression} FROM T").Rows.First()[0];
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("'1' + 1", 2d)]
    [InlineData("1 + '1'", 2d)]
    [InlineData("N + '2.5'", 5.5d)]
    [InlineData("' 7 ' + 0", 7d)]
    [InlineData("'  -  5' + 0", -5d)]
    [InlineData("'+5' + 0", 5d)]
    [InlineData("'5-' + 0", -5d)]
    [InlineData("'5 -' + 0", -5d)]
    [InlineData("'(5)' + 0", -5d)]
    [InlineData("'( 5 )' + 0", -5d)]
    [InlineData("'($5)' + 0", -5d)]
    [InlineData("'$(5)' + 0", -5d)]
    [InlineData("'-$5' + 0", -5d)]
    [InlineData("'$-5' + 0", -5d)]
    [InlineData("'5$' + 0", 5d)]
    [InlineData("'$ 5' + 0", 5d)]
    [InlineData("'.5' + 0", 0.5d)]
    [InlineData("'1.' + 0", 1d)]
    [InlineData("'1,000.5' + 0", 1000.5d)]
    [InlineData("'1,5' + 0", 15d)]
    [InlineData("'1,,000' + 0", 1000d)]
    [InlineData("'1.000,5' + 0", 1.0005d)]
    [InlineData("'1e2' + 0", 100d)]
    [InlineData("'1E-2' + 0", 0.01d)]
    [InlineData("'1d2' + 0", 100d)]
    [InlineData("'1D-2' + 0", 0.01d)]
    [InlineData("'1.e2' + 0", 100d)]
    [InlineData("'1e-400' + 0", 0d)]
    [InlineData("'-0' + 0", 0d)]
    [InlineData("'1234567890123456789' + 0", 1234567890123456789d)]
    [InlineData("'&H10' + 0", 16d)]
    [InlineData("'&h10' + 0", 16d)]
    [InlineData("'&HFFFF' + 0", 65535d)]
    [InlineData("'&H7FFFFFFF' + 0", 2147483647d)]
    [InlineData("'&H80000000' + 0", -2147483648d)]
    [InlineData("'&HFFFFFFFF' + 0", -1d)]
    [InlineData("'&H100000000' + 0", 4294967296d)]
    [InlineData("'&O17' + 0", 15d)]
    [InlineData("'&O177777' + 0", 65535d)]
    [InlineData("TRUE + '1'", 0d)]
    [InlineData("'1' + '1' + 1", 12d)]
    [InlineData("'1' + 1 + '1'", 3d)]
    [InlineData("'1' + ('1' + 1)", 3d)]
    public void Plus_reads_text_with_anything_but_text_as_a_number(string expression, double expected) =>
        Assert.Equal(expected, Assert.IsType<double>(Scalar(expression)));

    [Theory]
    [InlineData("CHR(9)")]
    [InlineData("CHR(10)")]
    [InlineData("CHR(160)")]
    [InlineData("CHRW(12288)")]
    public void Plus_skips_whitespace_around_a_text_number(string whitespace)
    {
        QueryEngine engine = Fresh();
        engine.ExecuteNonQuery($"UPDATE T SET S = {whitespace} & '5' & {whitespace}");
        Assert.Equal(6d, Scalar(engine, "S + 1"));
    }

    [Theory]
    [InlineData("'abc' + 1")]
    [InlineData("1 + 'abc'")]
    [InlineData("'' + 1")]
    [InlineData("' ' + 1")]
    [InlineData("S + N")]
    [InlineData("UCASE('abc') + 1")]
    [InlineData("'1 2' + 0")]
    [InlineData("'3 .1 4' + 0")]
    [InlineData("'--5' + 0")]
    [InlineData("'+5-' + 0")]
    [InlineData("'(-5)' + 0")]
    [InlineData("'-(5)' + 0")]
    [InlineData("'-' + 0")]
    [InlineData("'$' + 0")]
    [InlineData("'()' + 0")]
    [InlineData("'.' + 0")]
    [InlineData("',' + 0")]
    [InlineData("',5' + 0")]
    [InlineData("'1.5.2' + 0")]
    [InlineData("'1e' + 0")]
    [InlineData("'1e 2' + 0")]
    [InlineData("'.e2' + 0")]
    [InlineData("'1E2.5' + 0")]
    [InlineData("'&H' + 0")]
    [InlineData("'&HG' + 0")]
    [InlineData("'-&H10' + 0")]
    [InlineData("'0x10' + 0")]
    [InlineData("'12abc' + 0")]
    [InlineData("'5%' + 0")]
    [InlineData("'True' + 0")]
    [InlineData("'#5' + 0")]
    [InlineData("'2020-01-01' + 0")]
    [InlineData("'12:00' + 0")]
    [InlineData("'５' + 0")]
    [InlineData("'abc' + D")]
    public void Plus_with_text_that_is_not_a_number_is_a_type_mismatch(string expression) =>
        Assert.Throws<InvalidCastException>(() => Scalar(expression));

    [Theory]
    [InlineData("1 + G")]
    [InlineData("G + 1")]
    [InlineData("B + N")]
    [InlineData("D + G")]
    [InlineData("TRUE + B")]
    public void Plus_with_a_guid_or_binary_value_and_a_non_text_value_is_a_type_mismatch(string expression) =>
        Assert.Throws<InvalidCastException>(() => Scalar(expression));

    [Theory]
    [InlineData("'1e400' + 0")]
    [InlineData("2147483647 + 1")]
    [InlineData("N + 2147483647")]
    public void Plus_past_the_result_type_is_an_overflow(string expression) =>
        Assert.Throws<OverflowException>(() => Scalar(expression));

    [Theory]
    [InlineData("'1' + D", "2020-01-03T12:00:00")]
    [InlineData("D + '2.5'", "2020-01-05T00:00:00")]
    [InlineData("' 1 ' + #2020-01-02#", "2020-01-03T00:00:00")]
    public void Plus_with_text_and_a_date_is_a_date(string expression, string expected) =>
        Assert.Equal(DateTime.Parse(expected, CultureInfo.InvariantCulture), Assert.IsType<DateTime>(Scalar(expression)));

    [Theory]
    [InlineData("'1' + '1'", "11")]
    [InlineData("S + '1'", "a1")]
    [InlineData("'1' + B", "1AB")]
    [InlineData("'abc' + G", "abc" + Guid)]
    [InlineData("G + 'abc'", Guid + "abc")]
    [InlineData("G + G", Guid + Guid)]
    [InlineData("B + G", "AB" + Guid)]
    public void Plus_concatenates_two_texts(string expression, string expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("'1' + NT")]
    [InlineData("NT + 1")]
    [InlineData("NULL + 'a'")]
    public void Plus_propagates_null(string expression) =>
        Assert.Null(Scalar(expression));

    [Theory]
    [InlineData("1 & NT", "1")]
    [InlineData("NULL & 'a'", "a")]
    [InlineData("'' & TRUE", "-1")]
    [InlineData("FALSE & ''", "0")]
    [InlineData("'' & N", "3")]
    [InlineData("'' & M", "4.5")]
    [InlineData("'' & CCUR(1.5)", "1.5")]
    [InlineData("'' & G", Guid)]
    [InlineData("'' & B", "AB")]
    [InlineData("'' & 1/3", "0.333333333333333")]
    [InlineData("0.1 + 0.2 & ''", "0.3")]
    [InlineData("'' & -0.5", "-0.5")]
    [InlineData("'' & 1E300", "1E+300")]
    [InlineData("'' & -1E-300", "-1E-300")]
    [InlineData("'' & CSNG(1/3)", "0.3333333")]
    [InlineData("'' & CSNG(10000000)", "1E+07")]
    [InlineData("'' & CSNG(1E-5)", "0.00001")]
    [InlineData("'' & #2020-01-02#", "1/2/2020")]
    [InlineData("'' & #0100-01-01#", "1/1/100")]
    public void Ampersand_writes_each_operand_as_text(string expression, string expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Fact]
    public void Ampersand_writes_a_time_alone_on_the_epoch_day_and_both_otherwise()
    {
        // The long time pattern is taken from the culture rather than written out: ICU versions differ on the
        // space before AM/PM.
        string longTime = EnUs.DateTimeFormat.LongTimePattern;
        Assert.Equal(new DateTime(1899, 12, 30, 13, 0, 0).ToString(longTime, EnUs),
            Scalar("'' & #1899-12-30 13:00:00#"));
        Assert.Equal("1/2/2020 " + new DateTime(2020, 1, 2, 12, 0, 0).ToString(longTime, EnUs),
            Scalar("D & ''"));
    }

    [Theory]
    [InlineData("NT & NT")]
    [InlineData("NULL & NULL")]
    public void Ampersand_is_null_when_both_sides_are(string expression) =>
        Assert.Null(Scalar(expression));

    [Theory]
    [InlineData("1 & 2 + 3", "15")]
    [InlineData("'a' & 1 + 2", "a3")]
    [InlineData("1 + 2 & 3", "33")]
    [InlineData("1 & 2 & 3", "123")]
    public void Ampersand_binds_looser_than_plus(string expression, string expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("(1 & 2) + 3", 15d)]
    [InlineData("1 + (2 & 3)", 24d)]
    public void A_bracketed_ampersand_result_adds_as_a_number(string expression, double expected) =>
        Assert.Equal(expected, Assert.IsType<double>(Scalar(expression)));
}
