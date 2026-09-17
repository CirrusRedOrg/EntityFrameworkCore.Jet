using System.Globalization;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// <c>NTILE</c>, <c>PERCENT_RANK</c>, <c>CUME_DIST</c>, <c>LAG</c> and <c>LEAD</c>, as the SQL standard defines them.
/// Access has no window functions; this is a LibRed extension.
/// </summary>
public class WindowRankAndOffsetTests(WindowRankAndOffsetTests.Database database)
    : TempDatabaseTest, IClassFixture<WindowRankAndOffsetTests.Database>
{
    // Partition 'a' has a tie on V (Ids 2 and 3); partition 'b' has a Null V.
    private static readonly string[] Setup =
    [
        "CREATE TABLE W (Id LONG, G TEXT(10), V LONG, M CURRENCY)",
        "INSERT INTO W (Id, G, V, M) VALUES (1, 'a', 10, 1.5)",
        "INSERT INTO W (Id, G, V, M) VALUES (2, 'a', 20, 2.25)",
        "INSERT INTO W (Id, G, V, M) VALUES (3, 'a', 20, 3)",
        "INSERT INTO W (Id, G, V, M) VALUES (4, 'a', 40, 4)",
        "INSERT INTO W (Id, G, V, M) VALUES (5, 'b', 5, 5)",
        "INSERT INTO W (Id, G, V, M) VALUES (6, 'b', NULL, 6)",
    ];

    public sealed class Database() : SharedDatabase("window-rank-offset-", Setup);

    private (IReadOnlyList<Type> Types, List<object?[]> Rows) Query(string expression) =>
        database.Query($"SELECT Id, {expression} AS r FROM W ORDER BY Id", CultureInfo.InvariantCulture);

    private string ById(string expression) => string.Join(" ",
        Query(expression).Rows.Select(row => $"{row[0]}:{Convert.ToString(row[1], CultureInfo.InvariantCulture)}"));

    [Theory]
    [InlineData("NTILE(4) OVER (ORDER BY Id)", "1:1 2:1 3:2 4:2 5:3 6:4")]
    [InlineData("NTILE(2) OVER (PARTITION BY G ORDER BY Id)", "1:1 2:1 3:2 4:2 5:1 6:2")]
    [InlineData("NTILE(10) OVER (ORDER BY Id)", "1:1 2:2 3:3 4:4 5:5 6:6")]
    [InlineData("NTILE(2.5) OVER (ORDER BY Id)", "1:1 2:1 3:1 4:2 5:2 6:2")]
    [InlineData("NTILE(4) OVER (ORDER BY Id DESC)", "1:4 2:3 3:2 4:2 5:1 6:1")]
    [InlineData("NTILE(NULL) OVER (ORDER BY Id)", "1: 2: 3: 4: 5: 6:")]
    public void Ntile_cuts_the_partition_into_buckets(string expression, string expected) =>
        Assert.Equal(expected, ById(expression));

    [Theory]
    [InlineData("PERCENT_RANK() OVER (PARTITION BY G ORDER BY V)", "1:0 2:0.3333333333333333 3:0.3333333333333333 4:1 5:1 6:0")]
    [InlineData("PERCENT_RANK() OVER (PARTITION BY G)", "1:0 2:0 3:0 4:0 5:0 6:0")]
    [InlineData("CUME_DIST() OVER (PARTITION BY G ORDER BY V)", "1:0.25 2:0.75 3:0.75 4:1 5:1 6:0.5")]
    [InlineData("CUME_DIST() OVER ()", "1:1 2:1 3:1 4:1 5:1 6:1")]
    public void Distribution_functions_count_peers_together(string expression, string expected) =>
        Assert.Equal(expected, ById(expression));

    [Fact]
    public void A_partition_of_one_has_a_percent_rank_of_zero() =>
        Assert.Equal("1:0 2:0 3:0 4:0 5:0 6:0", ById("PERCENT_RANK() OVER (PARTITION BY Id)"));

    [Theory]
    [InlineData("LAG(V) OVER (PARTITION BY G ORDER BY Id)", "1: 2:10 3:20 4:20 5: 6:5")]
    [InlineData("LEAD(V) OVER (PARTITION BY G ORDER BY Id)", "1:20 2:20 3:40 4: 5: 6:")]
    [InlineData("LAG(V, 2, 0) OVER (ORDER BY Id)", "1:0 2:0 3:10 4:20 5:20 6:40")]
    [InlineData("LEAD(V, 0) OVER (ORDER BY Id)", "1:10 2:20 3:20 4:40 5:5 6:")]
    [InlineData("LEAD(V, 1, -1) OVER (ORDER BY V)", "1:20 2:20 3:40 4:-1 5:10 6:5")]
    [InlineData("LAG(V, Id - 1) OVER (ORDER BY Id)", "1:10 2:10 3:10 4:10 5:10 6:10")]
    [InlineData("LAG(V, NULL) OVER (ORDER BY Id)", "1: 2: 3: 4: 5: 6:")]
    [InlineData("LAG(V, 1, Id * 100) OVER (PARTITION BY G ORDER BY Id)", "1:100 2:10 3:20 4:20 5:500 6:5")]
    public void Lag_and_lead_read_another_row_of_the_partition(string expression, string expected) =>
        Assert.Equal(expected, ById(expression));

    [Theory]
    [InlineData("NTILE(4) OVER (ORDER BY Id)", typeof(int))]
    [InlineData("PERCENT_RANK() OVER (ORDER BY Id)", typeof(double))]
    [InlineData("CUME_DIST() OVER (ORDER BY Id)", typeof(double))]
    [InlineData("LAG(V) OVER (ORDER BY Id)", typeof(int))]
    [InlineData("LEAD(M) OVER (ORDER BY Id)", typeof(decimal))]
    [InlineData("LAG(V, 1, 0.5) OVER (ORDER BY Id)", typeof(double))]
    [InlineData("LAG(M, 1, 0) OVER (ORDER BY Id)", typeof(decimal))]
    [InlineData("LAG(V, 1, NULL) OVER (ORDER BY Id)", typeof(int))]
    public void Every_value_has_the_declared_type(string expression, Type expected)
    {
        var (types, rows) = Query(expression);
        Assert.Equal(expected, types[1]);
        Assert.All(rows, row => Assert.True(row[1] is null || row[1]!.GetType() == expected));
    }

    [Fact]
    public void A_default_of_another_kind_leaves_the_column_untyped()
    {
        var (types, rows) = Query("LAG(V, 1, 'none') OVER (ORDER BY Id)");
        Assert.Equal(typeof(object), types[1]);
        Assert.Equal("none", rows[0][1]);
        Assert.Equal(10, rows[1][1]);
    }

    [Theory]
    [InlineData("NTILE(0) OVER (ORDER BY Id)")]
    [InlineData("NTILE(-1) OVER (ORDER BY Id)")]
    [InlineData("LAG(V, -1) OVER (ORDER BY Id)")]
    [InlineData("LEAD(V, -2, 0) OVER (ORDER BY Id)")]
    public void A_count_or_offset_out_of_range_is_an_invalid_procedure_call(string expression) =>
        Assert.Throws<ArgumentException>(() => Query(expression));

    [Theory]
    [InlineData("NTILE() OVER (ORDER BY Id)")]
    [InlineData("LAG() OVER (ORDER BY Id)")]
    [InlineData("LAG(V, 1, 0, 0) OVER (ORDER BY Id)")]
    [InlineData("PERCENT_RANK(V) OVER (ORDER BY Id)")]
    public void The_wrong_number_of_arguments_is_refused(string expression) =>
        Assert.Throws<InvalidOperationException>(() => Query(expression));
}
