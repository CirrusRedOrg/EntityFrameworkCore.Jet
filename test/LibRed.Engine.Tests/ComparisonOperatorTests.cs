using System.Globalization;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// The comparison operators <c>=</c> <c>&lt;&gt;</c> <c>&lt;</c> <c>&gt;</c> <c>&lt;=</c> <c>&gt;=</c>: how each kind of
/// value compares with another, the truth test against the literal <c>True</c> or <c>False</c>, and where
/// <c>NOT</c> binds. The expected values were measured against ACE, except that a text literal or text column
/// compared with a number reads as a number here, as in SQL Server, where ACE refuses it.
/// </summary>
public class ComparisonOperatorTests(ComparisonOperatorTests.Database database)
    : TempDatabaseTest, IClassFixture<ComparisonOperatorTests.Database>
{
    private const string Guid = "{00112233-4455-6677-8899-AABBCCDDEEFF}";
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    private static readonly string[] Setup =
    [
        "CREATE TABLE T (Id LONG, N LONG, S TEXT(60), NT TEXT(60), D DATETIME, G GUID, B BINARY(4), YN YESNO)",
        "INSERT INTO T (Id, N, S, D, YN) VALUES (1, 3, '7', #2020-01-02 12:00:00#, TRUE)",
        $"UPDATE T SET G = {Guid}",
        "UPDATE T SET B = 0x41004200",
    ];

    public sealed class Database() : SharedDatabase("compare-ops-", Setup);

    private object? Scalar(string expression) => Query($"SELECT {expression} FROM T");

    // Text is read as a number in the regional separators, so each query runs under en-US whatever the machine's
    // culture.
    private object? Query(string sql) => database.Scalar(sql, EnUs);

    [Theory]
    [InlineData("CASE WHEN @p = '' THEN 0 ELSE 1 END", 'e', 1)]
    [InlineData("@p = 'E'", 'e', true)]
    [InlineData("@p < 'f'", 'e', true)]
    [InlineData("@p + 'x'", 'e', "ex")]
    [InlineData("@p LIKE 'E'", 'e', true)]
    [InlineData("@p = 7", '7', true)]
    [InlineData("S = @p", '7', true)]
    [InlineData("@p * 2", '7', 14.0)]
    [InlineData("NOT @p", '0', true)]
    public void A_char_parameter_is_one_character_of_text(string expression, char value, object expected)
    {
        var parameters = new Dictionary<string, object?> { ["p"] = value };
        Assert.Equal(expected, database.Engine.ExecuteQuery($"SELECT {expression} FROM T", parameters).Rows.First()[0]);
    }

    [Theory]
    [InlineData("2 = TRUE", true)]
    [InlineData("N = TRUE", true)]
    [InlineData("1.5 = TRUE", true)]
    [InlineData("'1' = TRUE", true)]
    [InlineData("'abc' = TRUE", true)]
    [InlineData("'' = TRUE", true)]
    [InlineData("'False' = TRUE", true)]
    [InlineData("D = TRUE", true)]
    [InlineData("G = TRUE", true)]
    [InlineData("TRUE = 'abc'", true)]
    [InlineData("0 = TRUE", false)]
    [InlineData("'0' = TRUE", false)]
    [InlineData("'0' = FALSE", true)]
    [InlineData("0.0 = FALSE", true)]
    [InlineData("LEFT('0', 1) = FALSE", true)]
    [InlineData("'False' = FALSE", false)]
    [InlineData("'abc' = FALSE", false)]
    [InlineData("FALSE = 2", false)]
    [InlineData("1 <> TRUE", false)]
    [InlineData("'abc' <> FALSE", true)]
    [InlineData("1 = TRUE = TRUE", true)]
    [InlineData("(1 = 2) = FALSE", true)]
    public void Equality_with_true_or_false_tests_whether_the_value_reads_as_zero(string expression, bool expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("1 < TRUE", false)]
    [InlineData("TRUE < D", true)]
    [InlineData("YN = 1", false)]
    [InlineData("YN = -1", true)]
    public void Only_equality_with_the_literal_is_a_truth_test(string expression, bool expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("NULL = TRUE")]
    [InlineData("NT = FALSE")]
    [InlineData("NT <> 'x'")]
    [InlineData("N < NULL")]
    public void A_null_side_makes_the_comparison_null(string expression) =>
        Assert.Null(Scalar(expression));

    [Theory]
    [InlineData("LEFT('10', 2) < 9", false)]
    [InlineData("LEFT('10', 2) > 9", true)]
    [InlineData("CSTR(10) < 9", false)]
    [InlineData("LEFT(' 1 ', 3) = 1", true)]
    [InlineData("TRUE < CSTR(5)", true)]
    [InlineData("LEFT('12', 2) < TRUE", false)]
    [InlineData("'10' > 9", true)]
    [InlineData("'1' = 1", true)]
    [InlineData("S = 7", true)]
    [InlineData("7 = S", true)]
    [InlineData("S < 10", true)]
    [InlineData("S = 7.0", true)]
    public void Text_against_a_number_compares_as_the_number_it_reads_as(string expression, bool expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("UCASE('abc') = 1")]
    [InlineData("LEFT('abc', 3) = 1")]
    [InlineData("'abc' = 1")]
    [InlineData("'' = 0")]
    [InlineData("'abc' > FALSE")]
    [InlineData("D > '2020-01-01'")]
    [InlineData("1 = G")]
    [InlineData("B < N")]
    [InlineData("D = B")]
    public void Text_that_is_not_a_number_or_a_guid_or_binary_value_against_a_number_is_a_type_mismatch(string expression) =>
        Assert.Throws<InvalidCastException>(() => Scalar(expression));

    [Theory]
    [InlineData("D = 43832.5", true)]
    [InlineData("D > 43832", true)]
    [InlineData("40000 < D", true)]
    [InlineData("D < N", false)]
    [InlineData("D = #2020-01-02 12:00:00#", true)]
    public void A_date_against_a_number_compares_as_its_serial(string expression, bool expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("G = '" + Guid + "'", true)]
    [InlineData("B = 'AB'", true)]
    [InlineData("B < 'abc'", true)]
    [InlineData("'abc' < B", false)]
    [InlineData("G = G", true)]
    [InlineData("B = 0x41004200", true)]
    [InlineData("B = 0x61004200", false)]
    public void A_guid_or_binary_value_against_text_compares_as_text(string expression, bool expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("'abc' = 'ABC'", true)]
    [InlineData("'a ' = 'a'", true)]
    [InlineData("' a' = 'a'", false)]
    [InlineData("'Z' < 'a'", false)]
    [InlineData("'é' < 'f'", true)]
    [InlineData("'café' = 'cafe'", false)]
    [InlineData("'7.0' = S", false)]
    public void Text_against_text_compares_as_text(string expression, bool expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("NOT 1 = 2", true)]
    [InlineData("NOT 1 > 2 = TRUE", true)]
    [InlineData("1 < 2 < 3", true)]
    [InlineData("3 > 2 > 1", false)]
    [InlineData("'b' > 'a' & 'z'", true)]
    [InlineData("1 + 1 = 2", true)]
    [InlineData("1 & 2 = 12", true)]
    public void Comparisons_bind_below_concatenation_and_above_not_left_to_right(string expression, bool expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("SELECT COUNT(*) FROM T WHERE S = 7", 1)]
    [InlineData("SELECT COUNT(*) FROM T WHERE 2 = TRUE", 1)]
    [InlineData("SELECT COUNT(*) FROM T WHERE '0' = TRUE", 0)]
    [InlineData("SELECT COUNT(*) FROM T WHERE NOT N = 4", 1)]
    [InlineData("SELECT COUNT(*) FROM T WHERE D > 43832", 1)]
    public void A_criteria_compares_the_same_way(string sql, int expected) =>
        Assert.Equal(expected, Query(sql));
}
