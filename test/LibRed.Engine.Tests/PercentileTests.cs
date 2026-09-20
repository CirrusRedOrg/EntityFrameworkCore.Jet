using System.Globalization;
using LibRed.Sql.Parsing;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// The standard's inverse distribution functions, <c>PERCENTILE_CONT</c> and <c>PERCENTILE_DISC … WITHIN GROUP (ORDER
/// BY …)</c>, grouped and over a window. Access has neither; this is a LibRed extension.
/// </summary>
public class PercentileTests(PercentileTests.Database database)
    : TempDatabaseTest, IClassFixture<PercentileTests.Database>
{
    // Group 'a' has V 1-4, dates 2 days apart then 6, and text in reverse order; group 'b' has a Null V. S holds 1-10.
    private static readonly string[] Setup =
    [
        "CREATE TABLE P (Id LONG, G TEXT(10), V LONG, D DATETIME, T TEXT(10))",
        "INSERT INTO P (Id, G, V, D, T) VALUES (1, 'a', 1, #2020-01-01#, 'd')",
        "INSERT INTO P (Id, G, V, D, T) VALUES (2, 'a', 2, #2020-01-03#, 'c')",
        "INSERT INTO P (Id, G, V, D, T) VALUES (3, 'a', 3, #2020-01-05#, 'b')",
        "INSERT INTO P (Id, G, V, D, T) VALUES (4, 'a', 4, #2020-01-11#, 'a')",
        "INSERT INTO P (Id, G, V, D, T) VALUES (5, 'b', 10, NULL, NULL)",
        "INSERT INTO P (Id, G, V, D, T) VALUES (6, 'b', 20, #2020-02-01#, 'x')",
        "INSERT INTO P (Id, G, V, D, T) VALUES (7, 'b', NULL, #2020-02-02#, 'y')",
        "CREATE TABLE S (N LONG)",
        .. Enumerable.Range(1, 10).Select(n => $"INSERT INTO S (N) VALUES ({n})"),
    ];

    public sealed class Database() : SharedDatabase("percentile-", Setup);

    private (IReadOnlyList<Type> Types, List<object?[]> Rows) Query(string sql) =>
        database.Query(sql, CultureInfo.InvariantCulture);

    private string ByGroup(string aggregate) => string.Join(" ",
        Query($"SELECT G, {aggregate} AS r FROM P GROUP BY G ORDER BY G").Rows
            .Select(row => $"{row[0]}:{Convert.ToString(row[1], CultureInfo.InvariantCulture)}"));

    [Theory]
    [InlineData("PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY V)", "a:2.5 b:15")]
    [InlineData("PERCENTILE_DISC(0.5) WITHIN GROUP (ORDER BY V)", "a:2 b:10")]
    [InlineData("PERCENTILE_CONT(0.25) WITHIN GROUP (ORDER BY V)", "a:1.75 b:12.5")]
    [InlineData("PERCENTILE_DISC(0.25) WITHIN GROUP (ORDER BY V)", "a:1 b:10")]
    [InlineData("PERCENTILE_CONT(0) WITHIN GROUP (ORDER BY V)", "a:1 b:10")]
    [InlineData("PERCENTILE_CONT(1) WITHIN GROUP (ORDER BY V)", "a:4 b:20")]
    [InlineData("PERCENTILE_DISC(0) WITHIN GROUP (ORDER BY V)", "a:1 b:10")]
    [InlineData("PERCENTILE_CONT(0.25) WITHIN GROUP (ORDER BY V DESC)", "a:3.25 b:17.5")]
    [InlineData("PERCENTILE_DISC(0.5) WITHIN GROUP (ORDER BY V DESC)", "a:3 b:20")]
    [InlineData("PERCENTILE_DISC(0.5) WITHIN GROUP (ORDER BY T)", "a:b b:x")]
    [InlineData("PERCENTILE_CONT(NULL) WITHIN GROUP (ORDER BY V)", "a: b:")]
    public void A_percentile_reads_the_ordered_values_of_the_group(string aggregate, string expected) =>
        Assert.Equal(expected, ByGroup(aggregate));

    [Fact]
    public void Nulls_take_no_part_and_no_values_give_null()
    {
        Assert.Equal(3.5, database.Scalar("SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY V) FROM P", CultureInfo.InvariantCulture));
        Assert.Null(database.Scalar("SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY V) FROM P WHERE Id > 100", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void The_fraction_is_read_as_written()
    {
        // 0.7 × 10 is 7 exactly, where the Double product is 7.000000000000001 and would take the 8th value.
        Assert.Equal(7, database.Scalar("SELECT PERCENTILE_DISC(0.7) WITHIN GROUP (ORDER BY N) FROM S", CultureInfo.InvariantCulture));
        Assert.Equal(7.3, (double)database.Scalar("SELECT PERCENTILE_CONT(0.7) WITHIN GROUP (ORDER BY N) FROM S", CultureInfo.InvariantCulture)!, 12);
    }

    [Fact]
    public void Dates_interpolate_along_the_timeline() =>
        Assert.Equal(new DateTime(2020, 1, 4), Query(
            "SELECT PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY D) AS r FROM P WHERE G = 'a'").Rows[0][0]);

    [Fact]
    public void Having_can_test_a_percentile() =>
        Assert.Equal("b", database.Scalar(
            "SELECT G FROM P GROUP BY G HAVING PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY V) > 10", CultureInfo.InvariantCulture));

    [Theory]
    [InlineData("PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY V)", typeof(double))]
    [InlineData("PERCENTILE_DISC(0.5) WITHIN GROUP (ORDER BY V)", typeof(int))]
    [InlineData("PERCENTILE_DISC(0.5) WITHIN GROUP (ORDER BY T)", typeof(string))]
    [InlineData("PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY D)", typeof(DateTime))]
    public void The_column_type_follows_the_key(string aggregate, Type expected)
    {
        var (groupedTypes, grouped) = Query($"SELECT {aggregate} AS r FROM P");
        var (windowedTypes, windowed) = Query($"SELECT {aggregate} OVER () AS r FROM P");

        Assert.Equal(expected, groupedTypes[0]);
        Assert.Equal(expected, windowedTypes[0]);
        Assert.IsType(expected, grouped[0][0]);
        Assert.All(windowed, row => Assert.Equal(grouped[0][0], row[0]));
    }

    private string ById(string expression) => string.Join(" ",
        Query($"SELECT Id, {expression} AS r FROM P ORDER BY Id").Rows
            .Select(row => $"{row[0]}:{Convert.ToString(row[1], CultureInfo.InvariantCulture)}"));

    [Theory]
    [InlineData("PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY V) OVER (PARTITION BY G)", "1:2.5 2:2.5 3:2.5 4:2.5 5:15 6:15 7:15")]
    [InlineData("PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY V) OVER (ORDER BY Id)", "1:1 2:1.5 3:2 4:2.5 5:3 6:3.5 7:3.5")]
    [InlineData("PERCENTILE_DISC(0.5) WITHIN GROUP (ORDER BY V DESC) OVER (ORDER BY Id ROWS BETWEEN 1 PRECEDING AND 1 FOLLOWING)",
        "1:2 2:2 3:3 4:4 5:10 6:20 7:20")]
    public void Over_a_window_a_percentile_reads_each_frame(string expression, string expected) =>
        Assert.Equal(expected, ById(expression));

    [Theory]
    [InlineData("PERCENTILE_CONT(1.5) WITHIN GROUP (ORDER BY V)")]
    [InlineData("PERCENTILE_DISC(-0.1) WITHIN GROUP (ORDER BY V)")]
    public void A_fraction_outside_zero_to_one_is_an_invalid_procedure_call(string aggregate) =>
        Assert.Throws<ArgumentException>(() => ByGroup(aggregate));

    [Fact]
    public void Text_cannot_be_interpolated() =>
        Assert.Throws<InvalidCastException>(() => ByGroup("PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY T)"));

    [Theory]
    [InlineData("PERCENTILE_CONT(0.5)")]
    [InlineData("SUM(V) WITHIN GROUP (ORDER BY V)")]
    [InlineData("PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY V, Id)")]
    [InlineData("PERCENTILE_CONT(0.5, 1) WITHIN GROUP (ORDER BY V)")]
    [InlineData("PERCENTILE_CONT(DISTINCT 0.5) WITHIN GROUP (ORDER BY V)")]
    public void The_within_group_syntax_belongs_to_the_percentiles(string aggregate) =>
        Assert.Throws<SqlParseException>(() => ByGroup(aggregate));
}
