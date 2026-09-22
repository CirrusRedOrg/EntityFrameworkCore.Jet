using System.Globalization;
using LibRed.Sql.Parsing;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// The standard's <c>LISTAGG(x [, separator]) WITHIN GROUP (ORDER BY …)</c>, grouped and over a window. Access has no
/// such aggregate; this is a LibRed extension.
/// </summary>
public class ListAggTests(ListAggTests.Database database)
    : TempDatabaseTest, IClassFixture<ListAggTests.Database>
{
    public sealed class Database() : SharedDatabase("listagg-", Sales.Setup);

    private string Rows(string sql) => Sales.Rows(database, sql);

    [Theory]
    [InlineData("LISTAGG(Rep, ', ') WITHIN GROUP (ORDER BY Rep)", "E:eve N:ann, ann, bob S:cat, dan, dan")]
    [InlineData("LISTAGG(DISTINCT Rep, ',') WITHIN GROUP (ORDER BY Rep)", "E:eve N:ann,bob S:cat,dan")]
    [InlineData("LISTAGG(Amount, '-') WITHIN GROUP (ORDER BY Rep DESC, Amount)", "E:70 N:200-50-100 S:10-20-300")]
    [InlineData("LISTAGG(Rep, ',') WITHIN GROUP (ORDER BY Id) FILTER (WHERE Amount > 60)", "E:eve N:ann,bob S:cat")]
    public void The_values_of_each_group_are_listed_in_order(string aggregate, string expected) =>
        Assert.Equal(expected, Rows($"SELECT Region, {aggregate} FROM S GROUP BY Region ORDER BY Region"));

    [Fact]
    public void Without_a_separator_the_values_run_together() =>
        Assert.Equal("annannbobcatdandaneve", Rows("SELECT LISTAGG(Rep) WITHIN GROUP (ORDER BY Id) FROM S"));

    [Fact]
    public void No_values_list_as_null() =>
        Assert.Equal("", Rows("SELECT LISTAGG(Rep, ',') WITHIN GROUP (ORDER BY Id) FROM S WHERE Id > 100"));

    [Fact]
    public void Nulls_are_left_out() =>
        Assert.Equal("ann,bob", Rows(
            "SELECT LISTAGG(IIF(Amount > 60 AND Region = 'N', Rep, NULL), ',') WITHIN GROUP (ORDER BY Id) FROM S"));

    [Fact]
    public void The_column_is_text()
    {
        var (types, rows) = database.Query("SELECT LISTAGG(Id, ',') WITHIN GROUP (ORDER BY Id) FROM S", CultureInfo.InvariantCulture);
        Assert.Equal(typeof(string), types[0]);
        Assert.Equal("1,2,3,4,5,6,7", rows[0][0]);
    }

    [Theory]
    [InlineData("LISTAGG(Rep, ',') WITHIN GROUP (ORDER BY Id) OVER (PARTITION BY Region)",
        "1:ann,ann,bob 2:ann,ann,bob 3:ann,ann,bob 4:cat,dan,dan 5:cat,dan,dan 6:cat,dan,dan 7:eve")]
    [InlineData("LISTAGG(Id, ',') WITHIN GROUP (ORDER BY Id) OVER (ORDER BY Id ROWS BETWEEN 1 PRECEDING AND CURRENT ROW)",
        "1:1 2:1,2 3:2,3 4:3,4 5:4,5 6:5,6 7:6,7")]
    [InlineData("LISTAGG(DISTINCT Rep, '/') WITHIN GROUP (ORDER BY Rep DESC) OVER (ORDER BY Id)",
        "1:ann 2:ann 3:bob/ann 4:cat/bob/ann 5:dan/cat/bob/ann 6:dan/cat/bob/ann 7:eve/dan/cat/bob/ann")]
    public void Over_a_window_each_frame_is_listed(string expression, string expected) =>
        Assert.Equal(expected, Rows($"SELECT Id, {expression} FROM S ORDER BY Id"));

    [Theory]
    [InlineData("LISTAGG(Rep, ',')")]
    [InlineData("LISTAGG(Rep, Region) WITHIN GROUP (ORDER BY Id)")]
    [InlineData("LISTAGG(Rep, ',', 'x') WITHIN GROUP (ORDER BY Id)")]
    [InlineData("LISTAGG(*) WITHIN GROUP (ORDER BY Id)")]
    public void The_syntax_is_the_standards(string aggregate) =>
        Assert.Throws<SqlParseException>(() => Rows($"SELECT {aggregate} FROM S"));

    // SQL Server's STRING_AGG(expression, separator) computes what LISTAGG computes, and the two differ only
    // in what each insists on: LISTAGG needs its WITHIN GROUP and lets the separator go, STRING_AGG needs the
    // separator and lets the order go.
    [Theory]
    [InlineData("STRING_AGG(Rep, ', ') WITHIN GROUP (ORDER BY Rep)", "E:eve N:ann, ann, bob S:cat, dan, dan")]
    [InlineData("STRING_AGG(DISTINCT Rep, ',') WITHIN GROUP (ORDER BY Rep)", "E:eve N:ann,bob S:cat,dan")]
    [InlineData("STRING_AGG(Rep, ',') FILTER (WHERE Amount > 60)", "E:eve N:ann,bob S:cat")]
    public void String_agg_lists_each_group(string aggregate, string expected) =>
        Assert.Equal(expected, Rows($"SELECT Region, {aggregate} FROM S GROUP BY Region ORDER BY Region"));

    [Fact]
    public void String_agg_without_within_group_lists_in_the_order_the_rows_arrive() =>
        Assert.Equal("ann,ann,bob,cat,dan,dan,eve", Rows("SELECT STRING_AGG(Rep, ',') FROM S"));

    [Fact]
    public void String_agg_leaves_nulls_out_and_lists_nothing_as_null()
    {
        Assert.Equal("ann,bob", Rows(
            "SELECT STRING_AGG(IIF(Amount > 60 AND Region = 'N', Rep, NULL), ',') FROM S"));
        Assert.Equal("", Rows("SELECT STRING_AGG(Rep, ',') FROM S WHERE Id > 100"));
    }

    [Fact]
    public void String_agg_over_a_window_lists_each_frame() =>
        Assert.Equal(
            "1:ann 2:ann,ann 3:ann,ann,bob 4:cat 5:cat,dan 6:cat,dan,dan 7:eve",
            Rows("SELECT Id, STRING_AGG(Rep, ',') OVER (PARTITION BY Region ORDER BY Id) FROM S ORDER BY Id"));

    [Theory]
    [InlineData("STRING_AGG(Rep)")]                                   // the separator is not optional
    [InlineData("STRING_AGG(Rep, Region)")]                           // and must be written as a string
    [InlineData("STRING_AGG(Rep, ',', 'x') WITHIN GROUP (ORDER BY Id)")]
    [InlineData("STRING_AGG(*)")]
    public void String_agg_takes_a_value_and_a_written_separator(string aggregate) =>
        Assert.Throws<SqlParseException>(() => Rows($"SELECT {aggregate} FROM S"));
}
