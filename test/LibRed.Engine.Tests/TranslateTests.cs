using System.Globalization;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// SQL Server's <c>TRANSLATE</c>: character-for-character replacement. Access has no such function; this is a LibRed
/// extension.
/// </summary>
public class TranslateTests(TranslateTests.Database database)
    : TempDatabaseTest, IClassFixture<TranslateTests.Database>
{
    private static readonly string[] Setup =
    [
        "CREATE TABLE T (K LONG PRIMARY KEY)",
        "INSERT INTO T (K) VALUES (1)",
    ];

    public sealed class Database() : SharedDatabase("translate-", Setup);

    private object? Scalar(string expression) =>
        database.Scalar($"SELECT {expression} FROM T", CultureInfo.InvariantCulture);

    // SQL Server's documented examples.
    [Theory]
    [InlineData("TRANSLATE('2*[3+4]/{7-2}', '[]{}', '()()')", "2*(3+4)/(7-2)")]
    [InlineData("TRANSLATE('[137.4,72.3]', '[,]', '( )')", "(137.4 72.3)")]
    [InlineData("TRANSLATE('abcdef', 'abc', 'bcd')", "bcddef")]  // each character translated once
    public void Each_character_is_replaced_by_its_translation(string expression, string expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("TRANSLATE('abc', 'x', 'y')", "abc")]
    [InlineData("TRANSLATE('abc', '', '')", "abc")]
    [InlineData("TRANSLATE('aA', 'a', 'x')", "xx")]      // matched as Replace matches, in the database sort order
    public void Characters_match_as_replace_matches_them(string expression, string expected) =>
        Assert.Equal(expected, Scalar(expression));

    // A surrogate pair is one character: U+1F600 against the single 'x' is a pair of lists of one.
    [Fact]
    public void A_surrogate_pair_is_one_character() =>
        Assert.Equal("axb", Scalar("TRANSLATE('a' & ChrW(-10179) & ChrW(-8704) & 'b', ChrW(-10179) & ChrW(-8704), 'x')"));

    [Theory]
    [InlineData("TRANSLATE(Null, 'a', 'b')")]
    [InlineData("TRANSLATE('a', Null, 'b')")]
    [InlineData("TRANSLATE('a', 'a', Null)")]
    public void Any_null_argument_gives_null(string expression) => Assert.Null(Scalar(expression));

    [Fact]
    public void The_two_lists_must_have_as_many_characters() =>
        Assert.Throws<ArgumentException>(() => Scalar("TRANSLATE('abc', 'ab', 'x')"));

    [Fact]
    public void The_column_is_text() =>
        Assert.Equal(typeof(string), database.Query("SELECT TRANSLATE('a', 'a', 'b') FROM T", CultureInfo.InvariantCulture).ColumnTypes[0]);
}
