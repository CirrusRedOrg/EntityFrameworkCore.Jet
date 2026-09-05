using LibRed;
using LibRed.Engine;
using LibRed.Engine.Execution;
using LibRed.Tests.Shared;
using LibRed.Sql.Ast;
using LibRed.Sql.Parsing;
using Xunit;

namespace LibRed.Engine.Tests;

// A correlated EXISTS is decorrelated into a hash semi-join: the body runs once and its correlation values are
// hashed, rather than re-running the body per outer row. These pin the SEMANTICS of that rewrite — every case
// here must give the same answer whether or not the optimisation engages, so they are written to fail if the
// rewrite ever changes meaning. (The speedup itself is measured separately; correctness is what needs guarding.)
public class CorrelatedExistsSemiJoinTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "exsemi-");
        var e = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));

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

    // Whether the rewrite engages, asked of the analysis directly. Every semantics test here passes either way,
    // since falling back gives the same answer — so this is the only thing that can tell a decorrelated shape
    // from a declined one. (The timing guards below do the same job for the shape EF actually emits, but at the
    // cost of running a 92 s query when they fail.) The outer scope is O aliased `o`, as in the queries above.
    private static bool Decorrelates(QueryEngine e, string sql, IReadOnlyList<OutputColumn>? outerColumns = null)
    {
        var select = (SelectStatement)new AntlrSqlParser().ParseStatement(sql);
        Expression predicate = select.Where is UnaryExpression u ? u.Operand : select.Where!;
        var exists = (ExistsExpression)predicate;

        IReadOnlyList<OutputColumn> outer = outerColumns ??
        [
            new("o", "Id", typeof(long)), new("o", "K", typeof(long)), new("o", "Tag", typeof(string)),
        ];

        return ExistsSemiJoin.TryBuild(
            (SelectStatement)exists.Query, outer, new(StringComparer.OrdinalIgnoreCase) { "o" },
            e.Database.Catalog) is not null;
    }

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
    public void A_top_of_at_least_one_does_not_change_existence()
        // TOP n for n >= 1 cannot affect whether ANY row exists, so it is dropped and the body decorrelates.
        // EF emits this shape for Any().
        => Assert.Equal([1, 3, 5],
            Ids(Fresh(),
                "SELECT o.Id FROM O AS o WHERE EXISTS (SELECT TOP 1 1 FROM I AS i WHERE i.K = o.K)"));

    [Fact]
    public void A_top_of_zero_makes_exists_always_false()
        // TOP 0 DOES change existence: the body returns nothing whatever the correlation, so no outer row
        // qualifies. Dropping this TOP would wrongly admit rows 1, 3 and 5.
        => Assert.Empty(
            Ids(Fresh(),
                "SELECT o.Id FROM O AS o WHERE EXISTS (SELECT TOP 0 1 FROM I AS i WHERE i.K = o.K)"));

    [Fact]
    public void A_top_percent_falls_back()
        // Refused rather than reasoned about — see TopCannotChangeExistence. Falling back keeps it correct.
        => Assert.Equal([1, 3, 5],
            Ids(Fresh(),
                "SELECT o.Id FROM O AS o WHERE EXISTS (SELECT TOP 50 PERCENT 1 FROM I AS i WHERE i.K = o.K)"));

    [Fact]
    public void A_residual_referencing_the_outer_scope_falls_back()
        // o.Tag is an OUTER reference in a residual conjunct, so removing the key equalities would not leave an
        // outer-independent body. The rewrite must decline rather than drop the residual.
        => Assert.Equal([1, 3, 5],
            Ids(Fresh(),
                "SELECT o.Id FROM O AS o WHERE EXISTS (SELECT 1 FROM I AS i WHERE i.K = o.K AND o.Tag <> 'zz')"));

    [Fact]
    public void An_aggregate_body_with_no_group_by_is_always_true()
        // A bare aggregate over zero rows still yields one row (COUNT(*) = 0), so this EXISTS is true for EVERY
        // outer row — including 2 and 4, which have no partner at all. A key-existence test would say false for
        // them, so this body cannot be decorrelated.
        => Assert.Equal([1, 2, 3, 4, 5],
            Ids(Fresh(), "SELECT o.Id FROM O AS o WHERE EXISTS (SELECT COUNT(*) FROM I AS i WHERE i.K = o.K)"));

    [Fact]
    public void A_having_with_no_group_by_is_not_decorrelated()
        // The lone group again: for outer rows 2 and 4 nothing matches, COUNT(*) is 0, and `HAVING COUNT(*) < 1`
        // admits that empty group — so those rows DO qualify, while a key test would drop them. Rows 1/3/5 have
        // partners, so their count is >= 1 and they don't qualify.
        => Assert.Equal([2, 4],
            Ids(Fresh(),
                """
                SELECT o.Id FROM O AS o
                WHERE EXISTS (SELECT 1 FROM I AS i WHERE i.K = o.K HAVING COUNT(*) < 1)
                """));

    [Fact]
    public void A_group_by_body_is_decorrelated_by_grouping_on_the_key_too()
        // Grouping on i.Keep as well as the correlation column: every matching inner row has Keep = 1 except the
        // one holding K = 50, so rows 1, 3 and 5 each have a group and qualify.
        => Assert.Equal([1, 3, 5],
            Ids(Fresh(),
                """
                SELECT o.Id FROM O AS o
                WHERE EXISTS (SELECT 1 FROM I AS i WHERE i.K = o.K GROUP BY i.Keep)
                """));

    [Fact]
    public void A_having_over_a_group_by_is_applied_per_key()
        // K = 10 has two inner rows (both Keep = 1) so its group passes COUNT(*) > 1; K = 30 and K = 50 have one
        // row each and fail. Grouping by the key as well is what keeps those counts per-key rather than global —
        // grouping on i.Keep alone would count all four Keep = 1 rows and wrongly admit every matching outer row.
        => Assert.Equal([1],
            Ids(Fresh(),
                """
                SELECT o.Id FROM O AS o
                WHERE EXISTS (SELECT 1 FROM I AS i WHERE i.K = o.K GROUP BY i.Keep HAVING COUNT(*) > 1)
                """));

    [Fact]
    public void A_group_by_body_with_a_residual_still_applies_it()
        => Assert.Equal([1, 3],
            Ids(Fresh(),
                """
                SELECT o.Id FROM O AS o
                WHERE EXISTS (
                    SELECT 1 FROM I AS i WHERE i.K = o.K AND i.Keep = 1 GROUP BY i.Id HAVING COUNT(*) > 0)
                """));

    [Fact]
    public void A_group_by_key_referencing_the_outer_scope_falls_back()
        // Grouping on an outer column makes the body vary per outer row, so it must decline. Per row the group is
        // whatever matched, and `COUNT(*) > 0` then admits exactly the rows with a partner.
        => Assert.Equal([1, 3, 5],
            Ids(Fresh(),
                """
                SELECT o.Id FROM O AS o
                WHERE EXISTS (SELECT 1 FROM I AS i WHERE i.K = o.K GROUP BY o.Tag HAVING COUNT(*) > 0)
                """));

    [Fact]
    public void A_having_referencing_the_outer_scope_falls_back()
        => Assert.Equal([1],
            Ids(Fresh(),
                """
                SELECT o.Id FROM O AS o
                WHERE EXISTS (SELECT 1 FROM I AS i WHERE i.K = o.K GROUP BY i.Keep HAVING COUNT(*) > o.Id)
                """));

    [Theory]
    // Which shapes the analysis actually accepts, stated outright rather than inferred from a runtime. The
    // grouped rows are the point of this change; the rest pin the boundaries the semantics tests above cover.
    [InlineData(true, "SELECT o.Id FROM O AS o WHERE EXISTS (SELECT 1 FROM I AS i WHERE i.K = o.K)")]
    [InlineData(true, "SELECT o.Id FROM O AS o WHERE EXISTS (SELECT 1 FROM I AS i WHERE i.K = o.K GROUP BY i.Keep)")]
    [InlineData(true, "SELECT o.Id FROM O AS o WHERE EXISTS (SELECT 1 FROM I AS i WHERE i.K = o.K GROUP BY i.Keep HAVING COUNT(*) > 1)")]
    // An aggregate or HAVING with no GROUP BY: one group even over no rows, so a key test can't stand in for it.
    [InlineData(false, "SELECT o.Id FROM O AS o WHERE EXISTS (SELECT COUNT(*) FROM I AS i WHERE i.K = o.K)")]
    [InlineData(false, "SELECT o.Id FROM O AS o WHERE EXISTS (SELECT 1 FROM I AS i WHERE i.K = o.K HAVING COUNT(*) < 1)")]
    // An outer reference left anywhere the correlation split didn't consume.
    [InlineData(false, "SELECT o.Id FROM O AS o WHERE EXISTS (SELECT 1 FROM I AS i WHERE i.K = o.K GROUP BY o.Tag HAVING COUNT(*) > 0)")]
    [InlineData(false, "SELECT o.Id FROM O AS o WHERE EXISTS (SELECT 1 FROM I AS i WHERE i.K = o.K GROUP BY i.Keep HAVING COUNT(*) > o.Id)")]
    [InlineData(false, "SELECT o.Id FROM O AS o WHERE EXISTS (SELECT 1 FROM I AS i WHERE i.K = o.K AND o.Tag <> 'zz')")]
    // EF's null-safe equality is a correlation key too, in any operand arrangement.
    [InlineData(true, "SELECT o.Id FROM O AS o WHERE EXISTS (SELECT 1 FROM I AS i WHERE i.K = o.K OR (i.K IS NULL AND o.K IS NULL))")]
    [InlineData(true, "SELECT o.Id FROM O AS o WHERE EXISTS (SELECT 1 FROM I AS i WHERE (i.K IS NULL AND o.K IS NULL) OR i.K = o.K)")]
    // But only when BOTH sides' nulls are tested — otherwise it isn't null-safe equality at all.
    [InlineData(false, "SELECT o.Id FROM O AS o WHERE EXISTS (SELECT 1 FROM I AS i WHERE i.K = o.K OR i.K IS NULL)")]
    [InlineData(false, "SELECT o.Id FROM O AS o WHERE EXISTS (SELECT 1 FROM I AS i WHERE i.K = o.K OR (i.K IS NULL AND o.Id IS NULL))")]
    // A correlation in HAVING is lifted only when its subquery side is a grouping key.
    [InlineData(true, "SELECT o.Id FROM O AS o WHERE EXISTS (SELECT 1 FROM I AS i GROUP BY i.K HAVING COUNT(*) > 1 AND i.K = o.K)")]
    [InlineData(true, "SELECT o.Id FROM O AS o WHERE EXISTS (SELECT 1 FROM I AS i GROUP BY i.K HAVING COUNT(*) > 1 AND (i.K = o.K OR (i.K IS NULL AND o.K IS NULL)))")]
    [InlineData(false, "SELECT o.Id FROM O AS o WHERE EXISTS (SELECT 1 FROM I AS i GROUP BY i.K HAVING COUNT(*) >= 1 AND i.Id = o.Id)")]
    [InlineData(false, "SELECT o.Id FROM O AS o WHERE EXISTS (SELECT 1 FROM I AS i WHERE i.K = o.K HAVING COUNT(*) > 0 AND MAX(i.Keep) = 1)")]
    // No correlation to hash at all, and the shapes TOP rules out.
    [InlineData(false, "SELECT o.Id FROM O AS o WHERE EXISTS (SELECT 1 FROM I AS i WHERE i.Keep = 1)")]
    [InlineData(false, "SELECT o.Id FROM O AS o WHERE EXISTS (SELECT TOP 0 1 FROM I AS i WHERE i.K = o.K)")]
    [InlineData(false, "SELECT o.Id FROM O AS o WHERE EXISTS (SELECT TOP 50 PERCENT 1 FROM I AS i WHERE i.K = o.K)")]
    public void The_analysis_accepts_exactly_these_shapes(bool expected, string sql)
        => Assert.Equal(expected, Decorrelates(Fresh(), sql));

    [Fact]
    public void A_null_safe_correlation_matches_null_to_null()
        // EF's null-safe equality, which it emits for any correlation on a nullable column. Unlike plain `=`, row 4
        // (K NULL) DOES match — against inner row 4, whose K is also NULL. Contrast A_null_outer_key_never_matches:
        // same data, same rows, different answer, and the only difference is the null handling of the correlation.
        => Assert.Equal([1, 3, 4, 5],
            Ids(Fresh(),
                """
                SELECT o.Id FROM O AS o
                WHERE EXISTS (
                    SELECT 1 FROM I AS i
                    WHERE i.K = o.K OR (i.K IS NULL AND o.K IS NULL))
                """));

    [Fact]
    public void A_null_safe_correlation_gives_the_same_answer_per_row()
        // The same predicate in a shape that DECLINES (the residual references the outer row), so this runs the
        // per-row path. It must reach the identical answer — that is what makes the rewrite above a rewrite and
        // not a change of meaning.
        => Assert.Equal([1, 3, 4, 5],
            Ids(Fresh(),
                """
                SELECT o.Id FROM O AS o
                WHERE EXISTS (
                    SELECT 1 FROM I AS i
                    WHERE (i.K = o.K OR (i.K IS NULL AND o.K IS NULL)) AND o.Tag <> 'zz')
                """));

    [Theory]
    // The operand order of neither connective is depended on: EF writes the equality first and the inner column
    // first, but all four arrangements mean the same thing and must all be recognised.
    [InlineData("i.K = o.K OR (i.K IS NULL AND o.K IS NULL)")]
    [InlineData("i.K = o.K OR (o.K IS NULL AND i.K IS NULL)")]
    [InlineData("(i.K IS NULL AND o.K IS NULL) OR i.K = o.K")]
    [InlineData("o.K = i.K OR (o.K IS NULL AND i.K IS NULL)")]
    public void The_null_safe_form_is_recognised_whichever_way_it_is_written(string predicate)
        => Assert.Equal([1, 3, 4, 5],
            Ids(Fresh(), $"SELECT o.Id FROM O AS o WHERE EXISTS (SELECT 1 FROM I AS i WHERE {predicate})"));

    [Fact]
    public void A_half_null_safe_pattern_is_not_treated_as_null_safe()
        // Only ONE side's null is tested, so this is not null-safe equality: it is true when the keys are equal, or
        // whenever i.K is null (regardless of o.K). Row 4 (K NULL) matches because inner row 4 has K NULL, but so
        // would every other row — which is why treating it as a correlation key would be wrong. It must decline and
        // evaluate per row: for every outer row inner row 4 satisfies `i.K IS NULL`, so ALL rows qualify.
        => Assert.Equal([1, 2, 3, 4, 5],
            Ids(Fresh(),
                "SELECT o.Id FROM O AS o WHERE EXISTS (SELECT 1 FROM I AS i WHERE i.K = o.K OR i.K IS NULL)"));

    [Fact]
    public void A_null_safe_correlation_works_alongside_a_plain_one()
        // Two keys of different kinds in one correlation: Id plain, K null-safe. Each outer row meets the single I
        // row with its Id, then the K pair decides — row 1 (10 = 10) and row 5 (50 = 50) match on equality, rows 2
        // and 3 mismatch, and row 4 matches ONLY because the K pair is null-safe (both null). Written with a plain
        // `=` for K, row 4 would drop out, so the per-key flag is doing exactly what it claims.
        => Assert.Equal([1, 4, 5],
            Ids(Fresh(),
                """
                SELECT o.Id FROM O AS o
                WHERE EXISTS (
                    SELECT 1 FROM I AS i
                    WHERE i.Id = o.Id AND (i.K = o.K OR (i.K IS NULL AND o.K IS NULL)))
                """));

    [Fact]
    public void A_correlation_in_having_on_a_grouping_key_is_decorrelated()
        // EF's Contains-over-a-GroupBy shape: the correlation sits in HAVING beside an aggregate condition, and
        // its subquery side is the grouping key. K = 10 has two inner rows so it passes COUNT(*) > 1; K = 30 and
        // K = 50 have one each and fail. Only outer row 1 qualifies.
        => Assert.Equal([1],
            Ids(Fresh(),
                """
                SELECT o.Id FROM O AS o
                WHERE EXISTS (
                    SELECT 1 FROM I AS i
                    GROUP BY i.K
                    HAVING COUNT(*) > 1 AND i.K = o.K)
                """));

    [Fact]
    public void A_having_correlation_leaves_the_aggregate_over_the_same_rows()
        // The soundness point. Lifting the correlation out must not change which rows the aggregate sees: the
        // count per group is over ALL of that key's rows either way, because a grouping key is constant within
        // its group. With `>= 1` every matching key qualifies, so this is the plain existence answer.
        => Assert.Equal([1, 3, 5],
            Ids(Fresh(),
                """
                SELECT o.Id FROM O AS o
                WHERE EXISTS (
                    SELECT 1 FROM I AS i
                    GROUP BY i.K
                    HAVING COUNT(*) >= 1 AND i.K = o.K)
                """));

    [Fact]
    public void A_having_correlation_can_be_null_safe_too()
        // Both extensions at once: correlation in HAVING, written in EF's null-safe form. Row 4's key is NULL and
        // matches the inner NULL group, which has one row.
        => Assert.Equal([1, 3, 4, 5],
            Ids(Fresh(),
                """
                SELECT o.Id FROM O AS o
                WHERE EXISTS (
                    SELECT 1 FROM I AS i
                    GROUP BY i.K
                    HAVING COUNT(*) >= 1 AND (i.K = o.K OR (i.K IS NULL AND o.K IS NULL)))
                """));

    [Fact]
    public void A_having_correlation_on_a_non_grouping_column_falls_back()
        // i.Id is not a grouping key, so the predicate does NOT select whole groups: grouping by K and then
        // testing `i.Id = o.Id` reads Id from each group's FIRST row. It must decline, and the answer it falls
        // back to shows why lifting it would have been wrong. The four groups by K are 10 → rows (Id 1, Id 3),
        // 30 → (Id 2), NULL → (Id 4), 50 → (Id 5), so the Ids on offer are {1, 2, 4, 5} — note 3 is absent, being
        // the second row of its group. Every outer row whose own Id is in that set qualifies.
        => Assert.Equal([1, 2, 4, 5],
            Ids(Fresh(),
                """
                SELECT o.Id FROM O AS o
                WHERE EXISTS (
                    SELECT 1 FROM I AS i
                    GROUP BY i.K
                    HAVING COUNT(*) >= 1 AND i.Id = o.Id)
                """));

    [Fact]
    public void A_having_correlation_with_no_group_by_falls_back()
        // No grouping key at all, so nothing in HAVING can be lifted: the lone group's COUNT depends on the
        // correlation, which is precisely what a key test cannot express. Declines.
        => Assert.Equal([1, 3],
            Ids(Fresh(),
                """
                SELECT o.Id FROM O AS o
                WHERE EXISTS (SELECT 1 FROM I AS i WHERE i.K = o.K HAVING COUNT(*) > 0 AND MAX(i.Keep) = 1)
                """));

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

    // Every semantics test above passes whether or not the rewrite engages. Pin the real EF-generated shape by
    // asking the analysis directly instead of inferring it from wall-clock duration.
    [Fact]
    public void The_rewrite_actually_engages_on_the_shape_ExecuteDelete_generates()
    {
        using var temp = TemporaryDatabase.CopyOf(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "exsemiplan");
        var e = new QueryEngine(temp.Open());

        Assert.True(Decorrelates(e,
            """
            SELECT `o`.`OrderID` FROM `Order Details` AS `o`
            WHERE EXISTS (
                SELECT 1 FROM (`Order Details` AS `o0`
                INNER JOIN `Orders` AS `o1` ON `o0`.`OrderID` = `o1`.`OrderID`)
                LEFT JOIN `Customers` AS `c` ON `o1`.`CustomerID` = `c`.`CustomerID`
                WHERE (`c`.`CustomerID` LIKE 'F%') AND `o0`.`OrderID` = `o`.`OrderID`
                    AND `o0`.`ProductID` = `o`.`ProductID`)
            """, [new("o", "OrderID", typeof(int)), new("o", "ProductID", typeof(int))]));

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
        Assert.Equal(164, deleted);
    }

    // Same structural guard for the TOP form EF emits for Any().
    [Fact]
    public void The_rewrite_engages_even_when_the_body_has_a_top()
    {
        using var temp = TemporaryDatabase.CopyOf(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "exsemitop");
        var e = new QueryEngine(temp.Open());

        Assert.True(Decorrelates(e,
            """
            SELECT `o`.`OrderID` FROM `Order Details` AS `o`
            WHERE EXISTS (
                SELECT TOP 1 1 FROM (`Order Details` AS `o0`
                INNER JOIN `Orders` AS `o1` ON `o0`.`OrderID` = `o1`.`OrderID`)
                LEFT JOIN `Customers` AS `c` ON `o1`.`CustomerID` = `c`.`CustomerID`
                WHERE (`c`.`CustomerID` LIKE 'F%') AND `o0`.`OrderID` = `o`.`OrderID`
                    AND `o0`.`ProductID` = `o`.`ProductID`)
            """, [new("o", "OrderID", typeof(int)), new("o", "ProductID", typeof(int))]));

        int deleted = e.ExecuteNonQuery(
            """
            DELETE FROM `Order Details` AS `o`
            WHERE EXISTS (
                SELECT TOP 1 1
                FROM (`Order Details` AS `o0`
                INNER JOIN `Orders` AS `o1` ON `o0`.`OrderID` = `o1`.`OrderID`)
                LEFT JOIN `Customers` AS `c` ON `o1`.`CustomerID` = `c`.`CustomerID`
                WHERE (`c`.`CustomerID` LIKE 'F%') AND `o0`.`OrderID` = `o`.`OrderID` AND `o0`.`ProductID` = `o`.`ProductID`)
            """);
        Assert.Equal(164, deleted);
    }
}
