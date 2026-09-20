using System.Globalization;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>Seven sales by five reps in three regions — the data the grouped-window, FILTER and LISTAGG tests share.
/// Totals by rep: ann 150 and bob 200 in N, cat 300 and dan 30 in S, eve 70 in E; 750 in all.</summary>
internal static class Sales
{
    public static readonly string[] Setup =
    [
        "CREATE TABLE S (Id LONG, Region TEXT(10), Rep TEXT(10), Amount LONG)",
        "INSERT INTO S (Id, Region, Rep, Amount) VALUES (1, 'N', 'ann', 100)",
        "INSERT INTO S (Id, Region, Rep, Amount) VALUES (2, 'N', 'ann', 50)",
        "INSERT INTO S (Id, Region, Rep, Amount) VALUES (3, 'N', 'bob', 200)",
        "INSERT INTO S (Id, Region, Rep, Amount) VALUES (4, 'S', 'cat', 300)",
        "INSERT INTO S (Id, Region, Rep, Amount) VALUES (5, 'S', 'dan', 10)",
        "INSERT INTO S (Id, Region, Rep, Amount) VALUES (6, 'S', 'dan', 20)",
        "INSERT INTO S (Id, Region, Rep, Amount) VALUES (7, 'E', 'eve', 70)",
    ];

    /// <summary>Every row of <paramref name="sql"/>'s result, its columns joined by ':' and the rows by ' '.</summary>
    public static string Rows(SharedDatabase database, string sql) => string.Join(" ",
        database.Query(sql, CultureInfo.InvariantCulture).Rows
            .Select(row => string.Join(":", row.Select(v => Convert.ToString(v, CultureInfo.InvariantCulture)))));
}

/// <summary>
/// Window functions over a grouped query: they run over the groups HAVING keeps, as the standard orders it, so they
/// can rank, total and compare groups by their aggregates. Access has no window functions; this is a LibRed
/// extension.
/// </summary>
public class GroupedWindowTests(GroupedWindowTests.Database database)
    : TempDatabaseTest, IClassFixture<GroupedWindowTests.Database>
{
    public sealed class Database() : SharedDatabase("grouped-window-", Sales.Setup);

    private string Rows(string sql) => Sales.Rows(database, sql);

    [Fact]
    public void Groups_rank_by_their_aggregate() =>
        Assert.Equal("ann:150:3 bob:200:2 cat:300:1 dan:30:5 eve:70:4", Rows(
            "SELECT Rep, SUM(Amount), RANK() OVER (ORDER BY SUM(Amount) DESC) FROM S GROUP BY Rep ORDER BY Rep"));

    [Fact]
    public void Groups_rank_within_a_partition_of_groups() =>
        Assert.Equal("E:eve:1 N:ann:2 N:bob:1 S:cat:1 S:dan:2", Rows(
            "SELECT Region, Rep, RANK() OVER (PARTITION BY Region ORDER BY SUM(Amount) DESC) FROM S "
            + "GROUP BY Region, Rep ORDER BY Region, Rep"));

    [Fact]
    public void An_aggregate_of_an_aggregate_totals_the_groups() =>
        Assert.Equal("ann:150:750:150 bob:200:750:350 cat:300:750:650 dan:30:750:680 eve:70:750:750", Rows(
            "SELECT Rep, SUM(Amount), SUM(SUM(Amount)) OVER (), SUM(SUM(Amount)) OVER (ORDER BY Rep) FROM S GROUP BY Rep ORDER BY Rep"));

    // The total is over the groups HAVING keeps — 150 + 300 + 30 = 480 — not all 750.
    [Fact]
    public void The_share_of_the_total_is_a_window_over_the_groups() =>
        Assert.Equal("ann:31.25 cat:62.5 dan:6.25", Rows(
            "SELECT Rep, SUM(Amount) * 100 / SUM(SUM(Amount)) OVER () FROM S GROUP BY Rep "
            + "HAVING Rep IN ('ann', 'cat', 'dan') ORDER BY Rep"));

    [Fact]
    public void Having_runs_before_the_window() =>
        Assert.Equal("ann:1 bob:2 cat:3 eve:4", Rows(
            "SELECT Rep, ROW_NUMBER() OVER (ORDER BY Rep) FROM S GROUP BY Rep HAVING SUM(Amount) > 60 ORDER BY Rep"));

    [Fact]
    public void Order_by_can_sort_on_a_window_over_the_groups() =>
        Assert.Equal("dan eve ann bob cat", Rows(
            "SELECT Rep FROM S GROUP BY Rep ORDER BY RANK() OVER (ORDER BY SUM(Amount))"));

    [Fact]
    public void A_window_can_read_the_group_key() =>
        Assert.Equal("E: N:E S:N", Rows("SELECT Region, LAG(Region) OVER (ORDER BY Region) FROM S GROUP BY Region"));

    [Fact]
    public void An_aggregate_in_a_window_alone_makes_the_query_one_group() =>
        Assert.Equal("1:750:1", Rows("SELECT RANK() OVER (ORDER BY SUM(Amount)), SUM(Amount), COUNT(*) OVER () FROM S"));

    [Fact]
    public void The_window_columns_are_typed()
    {
        var (types, rows) = database.Query(
            "SELECT RANK() OVER (ORDER BY SUM(Amount)), SUM(SUM(Amount)) OVER (), AVG(SUM(Amount)) OVER () FROM S GROUP BY Rep",
            CultureInfo.InvariantCulture);
        Assert.Equal([typeof(int), typeof(int), typeof(double)], types);
        Assert.All(rows, row => Assert.Equal(150.0, row[2]));
    }
}

/// <summary>
/// An aggregate's <c>FILTER (WHERE …)</c>: only the rows the condition is true for go in — grouped, in HAVING, over a
/// window and in a correlated subquery. Access has no FILTER; this is a LibRed extension.
/// </summary>
public class AggregateFilterTests(AggregateFilterTests.Database database)
    : TempDatabaseTest, IClassFixture<AggregateFilterTests.Database>
{
    public sealed class Database() : SharedDatabase("aggregate-filter-", Sales.Setup);

    private string Rows(string sql) => Sales.Rows(database, sql);

    [Theory]
    [InlineData("COUNT(*) FILTER (WHERE Amount > 60)", "E:1 N:2 S:1")]
    [InlineData("SUM(Amount) FILTER (WHERE Rep = 'dan')", "E: N: S:30")]
    [InlineData("MAX(Rep) FILTER (WHERE Amount < 100)", "E:eve N:ann S:dan")]
    [InlineData("PERCENTILE_DISC(0.5) WITHIN GROUP (ORDER BY Amount) FILTER (WHERE Amount > 20)", "E:70 N:100 S:300")]
    public void A_filter_narrows_each_group(string aggregate, string expected) =>
        Assert.Equal(expected, Rows($"SELECT Region, {aggregate} FROM S GROUP BY Region ORDER BY Region"));

    [Fact]
    public void A_filtered_count_of_everything_counts_the_filtered_rows() =>
        Assert.Equal("0:7", Rows("SELECT COUNT(*) FILTER (WHERE Amount > 1000), COUNT(*) FROM S"));

    [Fact]
    public void Having_can_test_a_filtered_aggregate() =>
        Assert.Equal("N", Rows("SELECT Region FROM S GROUP BY Region HAVING COUNT(*) FILTER (WHERE Amount > 60) >= 2"));

    [Theory]
    [InlineData("SUM(Amount) FILTER (WHERE Amount >= 100) OVER (ORDER BY Id)", "1:100 2:100 3:300 4:600 5:600 6:600 7:600")]
    [InlineData("COUNT(*) FILTER (WHERE Region = 'S') OVER (PARTITION BY Region)", "1:0 2:0 3:0 4:3 5:3 6:3 7:0")]
    public void A_filter_narrows_each_frame(string expression, string expected) =>
        Assert.Equal(expected, Rows($"SELECT Id, {expression} FROM S ORDER BY Id"));

    [Fact]
    public void A_filter_can_read_the_outer_query() =>
        Assert.Equal("1:2 2:4 3:1 4:0 5:6 6:5 7:3", Rows(
            "SELECT s1.Id, (SELECT COUNT(*) FILTER (WHERE s2.Amount > s1.Amount) FROM S AS s2) FROM S AS s1 ORDER BY s1.Id"));

    [Theory]
    [InlineData("SELECT UCASE(Rep) FILTER (WHERE Id > 1) FROM S")]
    [InlineData("SELECT FIRST_VALUE(Amount) FILTER (WHERE Id > 1) OVER () FROM S")]
    [InlineData("SELECT ROW_NUMBER() FILTER (WHERE Id > 1) OVER (ORDER BY Id) FROM S")]
    public void Only_an_aggregate_takes_a_filter(string sql) =>
        Assert.Throws<InvalidOperationException>(() => Rows(sql));
}
