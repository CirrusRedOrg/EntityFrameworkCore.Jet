using System.Linq;
using LibRed;
using LibRed.Engine;
using LibRed.Engine.Plan;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// Executing window functions. Access has none of these — they are a LibRed extension, and the shape EF Core
/// emits (<see cref="The_shape_EF_Core_emits"/>) is a ROW_NUMBER in a derived table filtered from outside.
/// </summary>
public class WindowFunctionTests : TempDatabaseTest
{
    // G is the partition key (with a null partition), V the ordering key. Ids 3, 4 and 7 make one partition
    // with a tie in it, which is what separates ROW_NUMBER from RANK from DENSE_RANK. Id 7 is inserted last but
    // belongs to the middle partition, so partitioning cannot quietly depend on input order.
    private static QueryEngine Seeded()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "window-");
        var engine = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        engine.ExecuteNonQuery("CREATE TABLE `W` (`Id` LONG NOT NULL PRIMARY KEY, `G` TEXT(10), `V` LONG)");
        engine.ExecuteNonQuery(
            "INSERT INTO `W` (`Id`, `G`, `V`) VALUES "
            + "(1, 'a', 10), (2, 'a', 20), (3, 'b', 30), (4, 'b', 30), (5, NULL, 50), (6, NULL, 60), (7, 'b', 40)");
        return engine;
    }

    private static string Numbering(QueryEngine e, string function, string over)
        => string.Join(
            " ",
            e.ExecuteQuery($"SELECT `Id`, {function} OVER ({over}) AS `r` FROM `W` ORDER BY `Id`")
                .Rows.Select(r => $"{r[0]}:{r[1]}"));

    [Fact]
    public void Numbering_restarts_in_each_partition()
        // 'a' → 1,2; 'b' → 1,2,3 by V (30, 30, 40); NULL → 1,2.
        => Assert.Equal(
            "1:1 2:2 3:1 4:2 5:1 6:2 7:3",
            Numbering(Seeded(), "ROW_NUMBER()", "PARTITION BY `G` ORDER BY `V`"));

    [Fact]
    public void A_descending_window_order_is_honoured()
        => Assert.Equal(
            "1:2 2:1 3:2 4:3 5:2 6:1 7:1",
            Numbering(Seeded(), "ROW_NUMBER()", "PARTITION BY `G` ORDER BY `V` DESC"));

    [Fact]
    public void Ties_break_on_input_order()
        // Ids 3 and 4 both have V = 30. ROW_NUMBER has to give them different numbers, and which row gets
        // which is otherwise arbitrary — it is settled here by input position so the result is reproducible.
        => Assert.Equal(
            "3:1 4:2 7:3",
            string.Join(" ", Numbering(Seeded(), "ROW_NUMBER()", "PARTITION BY `G` ORDER BY `V`")
                .Split(' ').Where(s => s.StartsWith('3') || s.StartsWith('4') || s.StartsWith('7'))));

    [Fact]
    public void Null_partition_keys_form_one_partition()
        // The same rule GROUP BY follows: nulls group together rather than each being its own partition.
        => Assert.Equal("5:1 6:2", string.Join(" ",
            Numbering(Seeded(), "ROW_NUMBER()", "PARTITION BY `G` ORDER BY `V`")
                .Split(' ').Where(s => s.StartsWith('5') || s.StartsWith('6'))));

    [Fact]
    public void With_no_partition_the_whole_input_is_one_partition()
        => Assert.Equal(
            "1:1 2:2 3:3 4:4 5:5 6:6 7:7",
            Numbering(Seeded(), "ROW_NUMBER()", "ORDER BY `Id`"));

    [Fact]
    public void With_no_window_order_rows_are_numbered_in_input_order()
        // Every row is then a peer, so the standard leaves the numbering unspecified; input order makes it
        // deterministic instead of arbitrary.
        => Assert.Equal(
            "1:1 2:2 3:1 4:2 5:1 6:2 7:3",
            Numbering(Seeded(), "ROW_NUMBER()", "PARTITION BY `G`"));

    [Fact]
    public void The_window_does_not_reorder_the_rows()
    {
        // Load-bearing: the sort sits ABOVE the window, so a node that emitted rows in partition order would
        // silently reorder every query that uses a window and has its own ORDER BY. Compared against the same
        // query without the window rather than against a literal, so it tests the property and not the storage.
        QueryEngine e = Seeded();
        var withWindow = e.ExecuteQuery(
            "SELECT `Id`, ROW_NUMBER() OVER (PARTITION BY `G` ORDER BY `V` DESC) AS `r` FROM `W`")
            .Rows.Select(r => r[0]).ToArray();
        var plain = e.ExecuteQuery("SELECT `Id` FROM `W`").Rows.Select(r => r[0]).ToArray();

        Assert.Equal(plain, withWindow);
    }

    [Fact]
    public void The_shape_EF_Core_emits()
    {
        // A window in a derived table, filtered from outside on the column it publishes: "the first row of each
        // group". This is what all 410 OVER clauses in the extended suite come down to.
        var rows = Seeded().ExecuteQuery(
            """
            SELECT `t`.`Id` FROM (
                SELECT `W`.`Id`, `W`.`G`, ROW_NUMBER() OVER (PARTITION BY `W`.`G` ORDER BY `W`.`V`) AS `row`
                FROM `W`
            ) AS `t`
            WHERE `t`.`row` <= 1
            ORDER BY `t`.`Id`
            """).Rows.Select(r => Convert.ToInt32(r[0])).ToArray();

        Assert.Equal([1, 3, 5], rows);
    }

    [Fact]
    public void An_outer_order_by_can_sort_on_the_window_column()
        // Numbering is 1:2 2:1 3:2 4:3 5:2 6:1 7:1, so the rank-1 rows come first (2, 6, 7 by Id), then the
        // rank-2 rows (1, 3, 5), then the single rank-3 row.
        => Assert.Equal(
            [2, 6, 7, 1, 3, 5, 4],
            Seeded().ExecuteQuery(
                "SELECT `Id` FROM `W` ORDER BY ROW_NUMBER() OVER (PARTITION BY `G` ORDER BY `V` DESC), `Id`")
                .Rows.Select(r => Convert.ToInt32(r[0])).ToArray());

    [Theory]
    // The registry entries beyond ROW_NUMBER, and the reason the peer-group machinery exists: ids 3 and 4 tie,
    // so RANK gives them both 1 and resumes at 3, while DENSE_RANK resumes at 2.
    [InlineData("RANK()", "3:1 4:1 7:3")]
    [InlineData("DENSE_RANK()", "3:1 4:1 7:2")]
    public void Ranking_functions_share_a_rank_between_peers(string function, string expected)
        => Assert.Equal(expected, string.Join(" ",
            Numbering(Seeded(), function, "PARTITION BY `G` ORDER BY `V`")
                .Split(' ').Where(s => s.StartsWith('3') || s.StartsWith('4') || s.StartsWith('7'))));

    [Fact]
    public void An_unknown_window_function_is_reported_by_name()
    {
        var ex = Assert.ThrowsAny<Exception>(() => Seeded().ExecuteQuery(
            "SELECT NTILE(4) OVER (ORDER BY `Id`) AS `r` FROM `W`"));

        Assert.Contains("NTILE", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_window_over_a_grouped_query_is_refused()
        // Not supported rather than silently wrong: AggregateNode owns the projection and collapses rows, so
        // the window would have to be computed above it. Nothing EF Core emits needs this.
        => Assert.ThrowsAny<NotSupportedException>(() => Seeded().ExecuteQuery(
            "SELECT COUNT(*) AS `c`, ROW_NUMBER() OVER (ORDER BY `G`) AS `r` FROM `W` GROUP BY `G`"));

    [Fact]
    public void Index_selection_still_reaches_below_the_window()
    {
        // The regression guard for the one dangerous optimizer site: IndexSelection.Apply's default arm stops
        // DESCENDING, so a missing WindowNode case silently turns every windowed query into a full scan.
        // Correct results, catastrophic plans — exactly the failure mode that made nested APPLY look like a hang.
        PlanNode plan = Seeded().PlanFor(
            """
            SELECT `t`.`Id` FROM (
                SELECT `W`.`Id`, ROW_NUMBER() OVER (ORDER BY `W`.`Id`) AS `row`
                FROM `W` WHERE `W`.`Id` = 3
            ) AS `t`
            """);

        Assert.True(Contains<IndexSeekNode>(plan), "expected the PK filter under the window to become a seek");
    }

    private static bool Contains<T>(PlanNode node) where T : PlanNode
        => node is T || node.Children.Any(Contains<T>);
}
