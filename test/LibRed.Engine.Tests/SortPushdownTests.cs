using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// An ORDER BY whose keys all come from one side of a join is applied to that side, so the join streams in order
// instead of its product being built and sorted. These pin that the observable order is identical — including the
// tie behaviour, which is the whole reason the rewrite is sound — and that the shapes it must not touch decline.
public class SortPushdownTests
{
    private static QueryEngine Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"sortpd-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));

        // Grp deliberately ties in pairs, and is anti-correlated with Id, so an ORDER BY Grp genuinely reorders
        // and the tie order is observable.
        e.ExecuteNonQuery("CREATE TABLE L ( Id LONG PRIMARY KEY, Grp LONG )");
        foreach ((int id, int grp) in new[] { (1, 2), (2, 1), (3, 2), (4, 1) })
        {
            e.ExecuteNonQuery($"INSERT INTO L (Id, Grp) VALUES ({id}, {grp})");
        }

        // Rk matches L1 twice and L3 once; L2 and L4 have no partner.
        e.ExecuteNonQuery("CREATE TABLE R ( Id LONG PRIMARY KEY, Rk LONG )");
        foreach ((int id, int rk) in new[] { (10, 1), (20, 1), (30, 3) })
        {
            e.ExecuteNonQuery($"INSERT INTO R (Id, Rk) VALUES ({id}, {rk})");
        }

        return e;
    }

    private static long[] Col(QueryEngine e, string sql, int ordinal = 0)
        => e.ExecuteQuery(sql).Rows.Select(r => Convert.ToInt64(r[ordinal])).ToArray();

    [Fact]
    public void A_cross_join_keeps_the_order_sorting_the_product_would_give()
    {
        // L sorted by Grp is 2, 4 (Grp 1, ties in input order), then 1, 3 (Grp 2). Each contributes 3 rows.
        //
        // This is exactly what sorting the 12-row product would produce, and the reason is the tie handling: the
        // product is enumerated left-major, so tied L rows already appear in L order there — which is the order a
        // stable sort of the product preserves, and the order sorting L alone produces.
        Assert.Equal([2, 2, 2, 4, 4, 4, 1, 1, 1, 3, 3, 3],
            Col(Fresh(), "SELECT l.Id FROM L AS l, R AS r ORDER BY l.Grp"));
    }

    [Fact]
    public void A_left_join_keeps_its_unmatched_rows_in_order()
        // Sorted L is 2, 4, 1, 3; L2 and L4 have no partner (one null-padded row each), L1 has two, L3 has one.
        => Assert.Equal([2, 4, 1, 1, 3],
            Col(Fresh(), "SELECT l.Id FROM L AS l LEFT JOIN R AS r ON l.Id = r.Rk ORDER BY l.Grp"));

    [Fact]
    public void An_inner_join_keeps_only_matches_in_order()
        => Assert.Equal([1, 1, 3],
            Col(Fresh(), "SELECT l.Id FROM L AS l INNER JOIN R AS r ON l.Id = r.Rk ORDER BY l.Grp"));

    [Fact]
    public void A_three_way_join_sinks_the_sort_past_both_joins()
        // The shape that motivated this: keys on the leftmost table of a left-deep chain. 4 × 3 × 3 = 36 rows.
        => Assert.Equal(
            Enumerable.Repeat(2L, 9).Concat(Enumerable.Repeat(4L, 9))
                .Concat(Enumerable.Repeat(1L, 9)).Concat(Enumerable.Repeat(3L, 9)).ToArray(),
            Col(Fresh(), "SELECT l.Id FROM L AS l, R AS r, R AS r2 ORDER BY l.Grp"));

    [Fact]
    public void A_key_from_both_sides_stays_above_the_join()
        // Cannot be pushed to either side, so it sorts the product — and must still order it correctly:
        // by Grp, then by r.Id descending within each L row's group.
        => Assert.Equal([2, 2, 2, 4, 4, 4, 1, 1, 1, 3, 3, 3],
            Col(Fresh(), "SELECT l.Id FROM L AS l, R AS r ORDER BY l.Grp, l.Id, r.Id DESC"));

    [Fact]
    public void A_key_from_the_right_side_only_stays_above_the_join()
        // Sorting the RIGHT side would not drive the output order (the left does), so this must not be pushed.
        // Ordering by r.Rk descending puts R 30 (Rk 3) first, then the two Rk 1 rows, for each L row in scan order.
        => Assert.Equal([30, 10, 20],
            Col(Fresh(), "SELECT r.Id FROM L AS l, R AS r WHERE l.Id = 1 ORDER BY r.Rk DESC, r.Id"));

    [Fact]
    public void An_unqualified_key_stays_above_the_join()
        // A bare name could bind to either side, and only the evaluator's resolver knows which — so it declines.
        // `Grp` exists only on L, so the answer matches the qualified form; what is pinned is that it is correct.
        => Assert.Equal([2, 2, 2, 4, 4, 4, 1, 1, 1, 3, 3, 3],
            Col(Fresh(), "SELECT l.Id FROM L AS l, R AS r ORDER BY Grp"));

    [Fact]
    public void A_top_over_a_pushed_sort_returns_the_same_rows()
    {
        QueryEngine e = Fresh();
        long[] full = Col(e, "SELECT l.Id FROM L AS l, R AS r ORDER BY l.Grp");
        Assert.Equal(full.Take(4).ToArray(), Col(e, "SELECT TOP 4 l.Id FROM L AS l, R AS r ORDER BY l.Grp"));
    }

    [Fact]
    public void A_descending_pushed_sort_reverses_correctly()
        => Assert.Equal([1, 1, 1, 3, 3, 3, 2, 2, 2, 4, 4, 4],
            Col(Fresh(), "SELECT l.Id FROM L AS l, R AS r ORDER BY l.Grp DESC"));

    // The correctness tests above pass whether or not the sort is pushed, so this pins that it IS: ordering the
    // 679,770-row cross product to return one row took 1,363 ms, against 172 ms sorting the 91 customers and
    // letting the TOP stop the join early.
    [Fact]
    public void The_pushdown_engages_on_a_three_way_cross_join_over_northwind()
    {
        string path = Path.Combine(Path.GetTempPath(), $"sortpdperf-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        int rows = e.ExecuteQuery(
            """
            SELECT TOP 1 `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`
            FROM `Customers` AS `c`, `Orders` AS `o`, `Employees` AS `e`
            ORDER BY `c`.`CustomerID`
            """).Rows.Count();
        sw.Stop();

        Assert.Equal(1, rows);
        Assert.True(sw.ElapsedMilliseconds < 600, $"took {sw.ElapsedMilliseconds} ms — the sort was not pushed below the join");
    }
}
