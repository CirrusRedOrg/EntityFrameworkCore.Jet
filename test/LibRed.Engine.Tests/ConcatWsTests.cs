using System.Globalization;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// SQL Server's <c>CONCAT_WS</c>: values joined by a separator, Null values left out. Access has no such function;
/// this is a LibRed extension.
/// </summary>
public class ConcatWsTests(ConcatWsTests.Database database)
    : TempDatabaseTest, IClassFixture<ConcatWsTests.Database>
{
    private static readonly string[] Setup =
    [
        "CREATE TABLE T (K LONG PRIMARY KEY)",
        "INSERT INTO T (K) VALUES (1)",
    ];

    public sealed class Database() : SharedDatabase("concatws-", Setup);

    private object? Scalar(string expression) =>
        database.Scalar($"SELECT {expression} FROM T", CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("CONCAT_WS(', ', 'a', 'b', 'c')", "a, b, c")]
    [InlineData("CONCAT_WS('', 'a', 'b')", "ab")]
    [InlineData("CONCAT_WS('-', 1, 2)", "1-2")]         // a value that is not text is written as & writes it
    [InlineData("CONCAT_WS('-', K, 'x')", "1-x")]
    public void The_values_are_joined_by_the_separator(string expression, string expected) =>
        Assert.Equal(expected, Scalar(expression));

    // SQL Server's own example: a Null value is left out, and no separator is added for it.
    [Theory]
    [InlineData("CONCAT_WS(',', '1 Microsoft Way', Null, Null, 'Redmond', 'WA', 98052)", "1 Microsoft Way,Redmond,WA,98052")]
    [InlineData("CONCAT_WS(',', Null, 'a', Null)", "a")]
    [InlineData("CONCAT_WS(',', Null, Null)", "")]     // all of them Null: an empty text
    public void Null_values_are_left_out(string expression, string expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Fact]
    public void A_null_separator_is_an_empty_one() =>
        Assert.Equal("ParisFrance", Scalar("CONCAT_WS(Null, 'Paris', Null, 'France')"));

    [Fact]
    public void Fewer_than_two_values_are_rejected() =>
        Assert.Throws<InvalidOperationException>(() => Scalar("CONCAT_WS(',', 'a')"));

    [Fact]
    public void The_column_is_text() =>
        Assert.Equal(typeof(string), database.Query("SELECT CONCAT_WS(',', K, K) FROM T", CultureInfo.InvariantCulture).ColumnTypes[0]);
}
