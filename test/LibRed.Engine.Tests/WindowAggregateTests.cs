using System.Globalization;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// Aggregates as window functions, over the standard's default frame: with an ORDER BY, the partition's rows up to
/// the current row and its peers; without one, the whole partition. The values and types are the grouped
/// aggregate's. Access has no window functions; this is a LibRed extension.
/// </summary>
public class WindowAggregateTests(WindowAggregateTests.Database database)
    : TempDatabaseTest, IClassFixture<WindowAggregateTests.Database>
{
    // Partition 'a' has a tie on V (Ids 2 and 3); partition 'b' has a Null V. T holds an empty text.
    private static readonly string[] Setup =
    [
        "CREATE TABLE W (Id LONG, G TEXT(10), V LONG, M CURRENCY, T TEXT(10))",
        "INSERT INTO W (Id, G, V, M, T) VALUES (1, 'a', 10, 1.5, 'x')",
        "INSERT INTO W (Id, G, V, M, T) VALUES (2, 'a', 20, 2.25, 'y')",
        "INSERT INTO W (Id, G, V, M, T) VALUES (3, 'a', 20, NULL, '')",
        "INSERT INTO W (Id, G, V, M, T) VALUES (4, 'b', 5, 1, 'z')",
        "INSERT INTO W (Id, G, V, M, T) VALUES (5, 'b', NULL, 3, 'w')",
    ];

    public sealed class Database() : SharedDatabase("window-aggregate-", Setup);

    private (IReadOnlyList<Type> Types, List<object?[]> Rows) Query(string sql) =>
        database.Query(sql, CultureInfo.InvariantCulture);

    private string ById(string function, string over) => string.Join(" ",
        Query($"SELECT Id, {function} OVER ({over}) AS r FROM W ORDER BY Id").Rows
            .Select(row => $"{row[0]}:{Convert.ToString(row[1], CultureInfo.InvariantCulture)}"));

    [Theory]
    [InlineData("SUM(V)", "", "1:55 2:55 3:55 4:55 5:55")]
    [InlineData("SUM(V)", "PARTITION BY G", "1:50 2:50 3:50 4:5 5:5")]
    [InlineData("SUM(V)", "ORDER BY Id", "1:10 2:30 3:50 4:55 5:55")]
    [InlineData("SUM(V)", "PARTITION BY G ORDER BY Id DESC", "1:50 2:40 3:20 4:5 5:")]
    [InlineData("COUNT(*)", "PARTITION BY G ORDER BY Id", "1:1 2:2 3:3 4:1 5:2")]
    [InlineData("COUNT(V)", "PARTITION BY G", "1:3 2:3 3:3 4:1 5:1")]
    [InlineData("AVG(V)", "PARTITION BY G", "1:16.666666666666668 2:16.666666666666668 3:16.666666666666668 4:5 5:5")]
    [InlineData("MAX(V)", "ORDER BY Id", "1:10 2:20 3:20 4:20 5:20")]
    [InlineData("MIN(M)", "PARTITION BY G ORDER BY Id", "1:1.5 2:1.5 3:1.5 4:1 5:1")]
    public void An_aggregate_runs_over_the_partition_up_to_the_current_row(string function, string over, string expected) =>
        Assert.Equal(expected, ById(function, over));

    // Rows that tie on the window order are peers: the running value includes all of them. Nulls sort first, and
    // a sum over nothing but a Null is Null.
    [Fact]
    public void Peers_share_a_running_value() =>
        Assert.Equal("1:15 2:55 3:55 4:5 5:", ById("SUM(V)", "ORDER BY V"));

    [Theory]
    [InlineData("SUM(V)")]
    [InlineData("AVG(V)")]
    [InlineData("MIN(T)")]
    [InlineData("MAX(T)")]
    [InlineData("COUNT(V)")]
    [InlineData("COUNT(*)")]
    [InlineData("VAR(V)")]
    [InlineData("VARP(V)")]
    [InlineData("STDEV(M)")]
    [InlineData("STDEVP(M)")]
    [InlineData("SUM(M)")]
    [InlineData("AVG(M)")]
    public void Over_the_whole_input_it_is_the_grouped_aggregate(string aggregate)
    {
        var (groupedTypes, grouped) = Query($"SELECT {aggregate} AS r FROM W");
        var (windowedTypes, windowed) = Query($"SELECT {aggregate} OVER () AS r FROM W");

        Assert.Equal(groupedTypes[0], windowedTypes[0]);
        Assert.All(windowed, row => Assert.Equal(grouped[0][0], row[0]));
    }

    [Fact]
    public void Over_a_partition_it_is_the_grouped_aggregate_of_that_group()
    {
        var grouped = Query("SELECT G, SUM(V), STDEVP(M) FROM W GROUP BY G").Rows
            .ToDictionary(row => (string)row[0]!, row => (row[1], row[2]));
        foreach (object?[] row in Query("SELECT G, SUM(V) OVER (PARTITION BY G), STDEVP(M) OVER (PARTITION BY G) FROM W").Rows)
            Assert.Equal(grouped[(string)row[0]!], (row[1], row[2]));
    }

    [Theory]
    [InlineData("SUM(V) OVER ()", typeof(int))]
    [InlineData("SUM(M) OVER ()", typeof(decimal))]
    [InlineData("AVG(V) OVER ()", typeof(double))]
    [InlineData("COUNT(*) OVER ()", typeof(int))]
    [InlineData("MAX(T) OVER ()", typeof(string))]
    public void The_column_is_typed_as_the_grouped_aggregate(string expression, Type expected)
    {
        var (types, rows) = Query($"SELECT {expression} AS r FROM W");
        Assert.Equal(expected, types[0]);
        Assert.All(rows, row => Assert.True(row[0] is null || row[0]!.GetType() == expected));
    }

    [Fact]
    public void A_windowed_currency_sum_is_a_currency() =>
        Assert.Equal("Currency", Query("SELECT TYPENAME(SUM(M) OVER ()) AS r FROM W").Rows[0][0]);

    [Fact]
    public void Distinct_is_not_allowed() =>
        Assert.Contains("DISTINCT", Assert.ThrowsAny<Exception>(() => Query("SELECT SUM(DISTINCT V) OVER () AS r FROM W")).Message);

    [Fact]
    public void Only_count_takes_a_star() =>
        Assert.Throws<InvalidOperationException>(() => Query("SELECT SUM(*) OVER () AS r FROM W"));
}
