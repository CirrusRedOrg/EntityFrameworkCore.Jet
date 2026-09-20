using System.Globalization;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// The standard's binary set functions — CORR, COVAR_POP, COVAR_SAMP and the REGR_ family — and its names for the
/// statistical aggregates (STDDEV_SAMP, STDDEV_POP, VAR_SAMP, VAR_POP), grouped and over a window. Access has none of
/// them; this is a LibRed extension.
/// </summary>
public class RegressionAggregateTests(RegressionAggregateTests.Database database)
    : TempDatabaseTest, IClassFixture<RegressionAggregateTests.Database>
{
    // Group a: (x, y) = (1, 2), (2, 4), (3, 5), (4, 4), (5, 5), so Sxx = 10, Syy = 6, Sxy = 6 — plus a pair missing y
    // and one missing x, which take no part. Group b has every x the same, c every y, and d a single pair.
    private static readonly string[] Setup =
    [
        "CREATE TABLE R (Id LONG, G TEXT(10), Y DOUBLE, X LONG, M CURRENCY)",
        "INSERT INTO R (Id, G, Y, X, M) VALUES (1, 'a', 2, 1, 1.25)",
        "INSERT INTO R (Id, G, Y, X, M) VALUES (2, 'a', 4, 2, 2.5)",
        "INSERT INTO R (Id, G, Y, X, M) VALUES (3, 'a', 5, 3, 3.75)",
        "INSERT INTO R (Id, G, Y, X, M) VALUES (4, 'a', 4, 4, 1)",
        "INSERT INTO R (Id, G, Y, X, M) VALUES (5, 'a', 5, 5, 2)",
        "INSERT INTO R (Id, G, Y, X, M) VALUES (6, 'a', NULL, 6, 3)",
        "INSERT INTO R (Id, G, Y, X, M) VALUES (7, 'a', 7, NULL, 4)",
        "INSERT INTO R (Id, G, Y, X, M) VALUES (8, 'b', 3, 1, 5)",
        "INSERT INTO R (Id, G, Y, X, M) VALUES (9, 'b', 5, 1, 6)",
        "INSERT INTO R (Id, G, Y, X, M) VALUES (10, 'c', 2, 1, 7)",
        "INSERT INTO R (Id, G, Y, X, M) VALUES (11, 'c', 2, 2, 8)",
        "INSERT INTO R (Id, G, Y, X, M) VALUES (12, 'c', 2, 3, 9)",
        "INSERT INTO R (Id, G, Y, X, M) VALUES (13, 'd', 1, 5, 10)",
    ];

    public sealed class Database() : SharedDatabase("regression-", Setup);

    private (IReadOnlyList<Type> Types, List<object?[]> Rows) Query(string sql) =>
        database.Query(sql, CultureInfo.InvariantCulture);

    // Rounded to ten places: the sums are exact for this data, the divisions not always.
    private static string Format(object? value) =>
        value is double d ? Math.Round(d, 10).ToString(CultureInfo.InvariantCulture) : Convert.ToString(value, CultureInfo.InvariantCulture)!;

    private string ByGroup(string aggregate) => string.Join(" ",
        Query($"SELECT G, {aggregate} AS r FROM R GROUP BY G ORDER BY G").Rows.Select(row => $"{row[0]}:{Format(row[1])}"));

    [Theory]
    [InlineData("REGR_COUNT", "a:5 b:2 c:3 d:1")]
    [InlineData("REGR_AVGX", "a:3 b:1 c:2 d:5")]
    [InlineData("REGR_AVGY", "a:4 b:4 c:2 d:1")]
    [InlineData("REGR_SXX", "a:10 b:0 c:2 d:0")]
    [InlineData("REGR_SYY", "a:6 b:2 c:0 d:0")]
    [InlineData("REGR_SXY", "a:6 b:0 c:0 d:0")]
    [InlineData("REGR_SLOPE", "a:0.6 b: c:0 d:")]
    [InlineData("REGR_INTERCEPT", "a:2.2 b: c:2 d:")]
    [InlineData("REGR_R2", "a:0.6 b: c:1 d:")]
    [InlineData("COVAR_POP", "a:1.2 b:0 c:0 d:0")]
    [InlineData("COVAR_SAMP", "a:1.5 b:0 c:0 d:")]
    [InlineData("CORR", "a:0.7745966692 b: c: d:")]
    public void A_binary_set_function_reads_the_pairs_of_the_group(string function, string expected) =>
        Assert.Equal(expected, ByGroup($"{function}(Y, X)"));

    [Theory]
    [InlineData("REGR_COUNT", 0)]
    [InlineData("REGR_SLOPE", null)]
    [InlineData("CORR", null)]
    [InlineData("COVAR_POP", null)]
    public void Over_no_pairs_only_the_count_has_a_value(string function, object? expected) =>
        Assert.Equal(expected, database.Scalar($"SELECT {function}(Y, X) FROM R WHERE Id > 100", CultureInfo.InvariantCulture));

    [Theory]
    [InlineData("REGR_COUNT", typeof(int))]
    [InlineData("REGR_SLOPE", typeof(double))]
    [InlineData("CORR", typeof(double))]
    [InlineData("COVAR_SAMP", typeof(double))]
    public void Over_a_partition_it_is_the_grouped_function_of_that_group(string function, Type expected)
    {
        var grouped = Query($"SELECT G, {function}(Y, X) FROM R GROUP BY G").Rows.ToDictionary(row => (string)row[0]!, row => row[1]);
        var (types, rows) = Query($"SELECT G, {function}(Y, X) OVER (PARTITION BY G) FROM R");

        Assert.Equal(expected, types[1]);
        Assert.All(rows, row => Assert.Equal(grouped[(string)row[0]!], row[1]));
    }

    [Fact]
    public void Over_a_window_it_runs_through_the_frame() =>
        Assert.Equal("1:1 2:2 3:3 4:4 5:5 6:5 7:5", string.Join(" ",
            Query("SELECT Id, REGR_COUNT(Y, X) OVER (ORDER BY Id) FROM R WHERE G = 'a' ORDER BY Id").Rows
                .Select(row => $"{row[0]}:{row[1]}")));

    [Theory]
    [InlineData("SELECT CORR(Y) FROM R")]
    [InlineData("SELECT CORR(Y, X, M) FROM R")]
    [InlineData("SELECT CORR(DISTINCT Y, X) OVER () FROM R")]
    public void The_pair_is_required_and_takes_no_distinct(string sql) =>
        Assert.Throws<InvalidOperationException>(() => Query(sql));

    [Fact]
    public void A_grouped_one_takes_no_distinct_either() =>
        Assert.Throws<NotSupportedException>(() => Query("SELECT CORR(DISTINCT Y, X) FROM R"));

    [Theory]
    [InlineData("STDDEV_SAMP", "STDEV")]
    [InlineData("STDDEV_POP", "STDEVP")]
    [InlineData("VAR_SAMP", "VAR")]
    [InlineData("VAR_POP", "VARP")]
    public void The_standard_names_are_the_access_statistics(string standard, string access)
    {
        foreach (string argument in new[] { "Y", "X", "M" })
        {
            Assert.Equal(Query($"SELECT G, {access}({argument}) FROM R GROUP BY G").Rows, Query($"SELECT G, {standard}({argument}) FROM R GROUP BY G").Rows);
            Assert.Equal(
                Query($"SELECT Id, {access}({argument}) OVER (ORDER BY Id) FROM R").Rows,
                Query($"SELECT Id, {standard}({argument}) OVER (ORDER BY Id) FROM R").Rows);
        }
    }
}
