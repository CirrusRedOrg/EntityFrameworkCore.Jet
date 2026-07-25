using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// A correlated EXISTS is decorrelated into a hash semi-join: the body runs once and its correlation values are
// hashed, rather than re-running the body per outer row. These pin the SEMANTICS of that rewrite — every case
// here must give the same answer whether or not the optimisation engages, so they are written to fail if the
// rewrite ever changes meaning. (The speedup itself is measured separately; correctness is what needs guarding.)
public class CorrelatedExistsSemiJoinTests
{
    private static QueryEngine Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"exsemi-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));

        // Outer: 1..5, with a NULL key to exercise the null side of the correlation.
        e.ExecuteNonQuery("CREATE TABLE O ( Id LONG PRIMARY KEY, K LONG, Tag TEXT(10) )");
        e.ExecuteNonQuery("INSERT INTO O (Id, K, Tag) VALUES (1, 10, 'a')");
        e.ExecuteNonQuery("INSERT INTO O (Id, K, Tag) VALUES (2, 20, 'b')");
        e.ExecuteNonQuery("INSERT INTO O (Id, K, Tag) VALUES (3, 30, 'c')");
        e.ExecuteNonQuery("INSERT INTO O (Id, K, Tag) VALUES (4, NULL, 'd')");
        e.ExecuteNonQuery("INSERT INTO O (Id, K, Tag) VALUES (5, 50, 'e')");

        // Inner: matches 10 and 30, plus a NULL key of its own, plus a duplicate of 10.
        e.ExecuteNonQuery("CREATE TABLE I ( Id LONG PRIMARY KEY, K LONG, Keep LONG )");
        e.ExecuteNonQuery("INSERT INTO I (Id, K, Keep) VALUES (1, 10, 1)");
        e.ExecuteNonQuery("INSERT INTO I (Id, K, Keep) VALUES (2, 30, 1)");
        e.ExecuteNonQuery("INSERT INTO I (Id, K, Keep) VALUES (3, 10, 1)");
        e.ExecuteNonQuery("INSERT INTO I (Id, K, Keep) VALUES (4, NULL, 1)");
        e.ExecuteNonQuery("INSERT INTO I (Id, K, Keep) VALUES (5, 50, 0)");
        return e;
    }

    private static long[] Ids(QueryEngine e, string sql)
        => e.ExecuteQuery(sql).Rows.Select(r => Convert.ToInt64(r[0])).OrderBy(x => x).ToArray();

    [Fact]
    public void Matches_only_rows_with_a_correlated_partner()
        => Assert.Equal([1, 3, 5],
            Ids(Fresh(), "SELECT o.Id FROM O AS o WHERE EXISTS (SELECT 1 FROM I AS i WHERE i.K = o.K)"));

    [Fact]
    public void A_null_outer_key_never_matches()
        // Row 4 has K = NULL. NULL = anything is never true, so it must not appear — including against the
        // inner row that also has K = NULL.
        => Assert.DoesNotContain(4L,
            Ids(Fresh(), "SELECT o.Id FROM O AS o WHERE EXISTS (SELECT 1 FROM I AS i WHERE i.K = o.K)"));

    [Fact]
    public void A_residual_predicate_still_applies()
        // Keep = 0 on the inner row holding K = 50, so outer row 5 must not match even though the keys line up.
        => Assert.Equal([1, 3],
            Ids(Fresh(),
                "SELECT o.Id FROM O AS o WHERE EXISTS (SELECT 1 FROM I AS i WHERE i.K = o.K AND i.Keep = 1)"));

    [Fact]
    public void Duplicate_inner_matches_yield_the_outer_row_once()
        // Two inner rows have K = 10; EXISTS is existence, not a join, so row 1 appears exactly once.
        => Assert.Single(
            Ids(Fresh(), "SELECT o.Id FROM O AS o WHERE EXISTS (SELECT 1 FROM I AS i WHERE i.K = o.K)")
                .Where(id => id == 1));

    [Fact]
    public void Not_exists_is_the_complement()
        => Assert.Equal([2, 4],
            Ids(Fresh(), "SELECT o.Id FROM O AS o WHERE NOT EXISTS (SELECT 1 FROM I AS i WHERE i.K = o.K)"));

    [Fact]
    public void A_multi_column_correlation_key_works()
        => Assert.Equal([1, 5],
            Ids(Fresh(),
                """
                SELECT o.Id FROM O AS o
                WHERE EXISTS (SELECT 1 FROM I AS i WHERE i.K = o.K AND i.Id = o.Id)
                """));

    [Fact]
    public void An_inner_left_join_is_preserved()
        // The shape ExecuteDelete generates for a predicate over a navigation: the EXISTS body is itself a join,
        // and the LEFT JOIN must keep inner rows whose right side is absent.
        => Assert.Equal([1, 3, 5],
            Ids(Fresh(),
                """
                SELECT o.Id FROM O AS o
                WHERE EXISTS (
                    SELECT 1 FROM I AS i
                    LEFT JOIN O AS o2 ON i.Id = o2.Id
                    WHERE i.K = o.K)
                """));

    [Fact]
    public void A_top_in_the_body_is_not_decorrelated()
        // TOP makes the body's result depend on which rows the correlation admitted, so the rewrite must decline
        // and fall back. Per outer row the body yields at most one row, so each key still matches on its own.
        => Assert.Equal([1, 3, 5],
            Ids(Fresh(),
                "SELECT o.Id FROM O AS o WHERE EXISTS (SELECT TOP 1 1 FROM I AS i WHERE i.K = o.K)"));

    [Fact]
    public void A_residual_referencing_the_outer_scope_falls_back()
        // o.Tag is an OUTER reference in a residual conjunct, so removing the key equalities would not leave an
        // outer-independent body. The rewrite must decline rather than drop the residual.
        => Assert.Equal([1, 3, 5],
            Ids(Fresh(),
                "SELECT o.Id FROM O AS o WHERE EXISTS (SELECT 1 FROM I AS i WHERE i.K = o.K AND o.Tag <> 'zz')"));

    [Fact]
    public void An_uncorrelated_exists_is_unaffected()
        => Assert.Equal([1, 2, 3, 4, 5],
            Ids(Fresh(), "SELECT o.Id FROM O AS o WHERE EXISTS (SELECT 1 FROM I AS i WHERE i.Keep = 1)"));

    [Fact]
    public void Delete_with_a_correlated_exists_removes_exactly_the_matching_rows()
    {
        QueryEngine e = Fresh();
        int deleted = e.ExecuteNonQuery("DELETE FROM O WHERE EXISTS (SELECT 1 FROM I AS i WHERE i.K = O.K)");
        Assert.Equal(3, deleted);
        Assert.Equal([2, 4], Ids(e, "SELECT Id FROM O"));
    }

    [Fact]
    public void A_residual_holding_an_outer_independent_subquery_is_decorrelated()
        // MAX(i2.Keep) over I is 1 regardless of the outer row, so the residual `i.Keep = (…)` doesn't vary and the
        // rewrite is sound. The residual test has to descend INTO the subquery to see that: treating a nested
        // subquery as opaque declined this shape, which is why GroupBy_aggregate_2 stayed slow.
        => Assert.Equal([1, 3],
            Ids(Fresh(),
                """
                SELECT o.Id FROM O AS o
                WHERE EXISTS (
                    SELECT 1 FROM I AS i
                    WHERE i.K = o.K AND i.Keep = (SELECT MAX(i2.Keep) FROM I AS i2))
                """));

    [Fact]
    public void A_residual_holding_a_correlated_subquery_falls_back()
        // Here the residual's subquery DOES reference the outer row, so the body varies and the rewrite must
        // decline. Per outer row the MAX is taken over that row's own K, which admits row 5 as well.
        => Assert.Equal([1, 3, 5],
            Ids(Fresh(),
                """
                SELECT o.Id FROM O AS o
                WHERE EXISTS (
                    SELECT 1 FROM I AS i
                    WHERE i.K = o.K AND i.Keep = (SELECT MAX(i2.Keep) FROM I AS i2 WHERE i2.K = o.K))
                """));

    [Fact]
    public void An_unqualified_column_in_the_residual_falls_back()
        // Bare `Keep` binds to I.Keep here, but only the evaluator's resolver could establish that, and this
        // rewrite commits before the body ever runs — so it declines and stays correct.
        => Assert.Equal([1, 3],
            Ids(Fresh(),
                "SELECT o.Id FROM O AS o WHERE EXISTS (SELECT 1 FROM I AS i WHERE i.K = o.K AND Keep = 1)"));

    // Every test above passes whether or not the rewrite engages, because falling back gives the same answer —
    // which is precisely how an earlier version of this optimisation appeared "correct" while never firing at
    // all (SubtreeAliases returned no aliases for a projected plan, so every subquery was declined). This guard
    // fails if that happens again: the real shape EF generates for a predicate over a navigation takes ~92 s
    // per-row and ~0.4 s decorrelated, so the threshold is ~30x clear of the fast path and ~6x clear of the slow
    // one. It is a performance guard, not a correctness test.
    [Fact]
    public void The_rewrite_actually_engages_on_the_shape_ExecuteDelete_generates()
    {
        string path = Path.Combine(Path.GetTempPath(), $"exsemiperf-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        int deleted = e.ExecuteNonQuery(
            """
            DELETE FROM `Order Details` AS `o`
            WHERE EXISTS (
                SELECT 1
                FROM (`Order Details` AS `o0`
                INNER JOIN `Orders` AS `o1` ON `o0`.`OrderID` = `o1`.`OrderID`)
                LEFT JOIN `Customers` AS `c` ON `o1`.`CustomerID` = `c`.`CustomerID`
                WHERE (`c`.`CustomerID` LIKE 'F%') AND `o0`.`OrderID` = `o`.`OrderID` AND `o0`.`ProductID` = `o`.`ProductID`)
            """);
        sw.Stop();

        Assert.Equal(164, deleted);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15),
            $"correlated EXISTS took {sw.Elapsed.TotalSeconds:F1}s — the decorrelation is no longer engaging");
    }
}
