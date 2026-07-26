using LibRed;
using LibRed.Engine;
using LibRed.Engine.Execution;
using LibRed.Sql.Ast;
using LibRed.Sql.Parsing;
using Xunit;

namespace LibRed.Engine.Tests;

// A correlated `x IN (subquery)` is decorrelated into a hash semi-join, like EXISTS: the body runs once and its
// values are hashed, rather than the body being re-run per outer row. IN is the harder case because it is
// three-valued — "no match" and "no match but the column held a NULL" are FALSE and UNKNOWN, and they differ once
// NOT IN is in play — so these tests are mostly about that, not about the speed.
public class CorrelatedInSemiJoinTests
{
    private static QueryEngine Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"insemi-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));

        e.ExecuteNonQuery("CREATE TABLE O ( Id LONG PRIMARY KEY, K LONG, Tag TEXT(10) )");
        e.ExecuteNonQuery("INSERT INTO O (Id, K, Tag) VALUES (1, 10, 'a')");
        e.ExecuteNonQuery("INSERT INTO O (Id, K, Tag) VALUES (2, 20, 'b')"); // no partner at all
        e.ExecuteNonQuery("INSERT INTO O (Id, K, Tag) VALUES (3, 30, 'c')"); // partner projects NULL
        e.ExecuteNonQuery("INSERT INTO O (Id, K, Tag) VALUES (4, NULL, 'd')");
        e.ExecuteNonQuery("INSERT INTO O (Id, K, Tag) VALUES (5, 50, 'e')");

        // V is the projected column and is nullable — that is what makes the UNKNOWN cases reachable.
        e.ExecuteNonQuery("CREATE TABLE I ( Id LONG PRIMARY KEY, K LONG, V LONG, Keep LONG )");
        e.ExecuteNonQuery("INSERT INTO I (Id, K, V, Keep) VALUES (1, 10, 1, 1)");
        e.ExecuteNonQuery("INSERT INTO I (Id, K, V, Keep) VALUES (2, 30, NULL, 1)");
        e.ExecuteNonQuery("INSERT INTO I (Id, K, V, Keep) VALUES (3, 10, 3, 1)");
        e.ExecuteNonQuery("INSERT INTO I (Id, K, V, Keep) VALUES (4, NULL, 4, 1)");
        e.ExecuteNonQuery("INSERT INTO I (Id, K, V, Keep) VALUES (5, 50, 5, 0)");
        return e;
    }

    private static long[] Ids(QueryEngine e, string sql)
        => e.ExecuteQuery(sql).Rows.Select(r => Convert.ToInt64(r[0])).OrderBy(x => x).ToArray();

    // Whether the rewrite engages, asked of the analysis directly: every semantics test here passes either way,
    // since falling back to the per-row loop gives the same answer.
    private static bool Decorrelates(QueryEngine e, string sql)
    {
        var select = (SelectStatement)new AntlrSqlParser().ParseStatement(sql);
        var inq = (InSubqueryExpression)select.Where!;

        OutputColumn[] outer =
        [
            new("o", "Id", typeof(long)), new("o", "K", typeof(long)), new("o", "Tag", typeof(string)),
        ];

        return ExistsSemiJoin.TryBuildForIn(
            (SelectStatement)inq.Query, inq.Value, outer, new(StringComparer.OrdinalIgnoreCase) { "o" },
            e.Database.Catalog) is not null;
    }

    [Fact]
    public void In_matches_the_correlated_values()
        // Per outer row the body yields the V values of its partners: row 1 sees {1, 3} and its Id is in it;
        // row 5 sees {5}; row 3 sees {NULL} (UNKNOWN, not a match); rows 2 and 4 see nothing.
        => Assert.Equal([1, 5],
            Ids(Fresh(), "SELECT o.Id FROM O AS o WHERE o.Id IN (SELECT i.V FROM I AS i WHERE i.K = o.K)"));

    [Fact]
    public void Not_in_is_not_the_complement_when_the_column_holds_a_null()
    {
        // The three-valued trap. Row 3's body yields {NULL}: `3 IN (NULL)` is UNKNOWN, so row 3 satisfies NEITHER
        // IN nor NOT IN and must be absent from both results. Rows 2 and 4 have an EMPTY body, which is FALSE
        // rather than UNKNOWN, so NOT IN admits them.
        QueryEngine e = Fresh();
        const string body = "(SELECT i.V FROM I AS i WHERE i.K = o.K)";
        long[] inRows = Ids(e, $"SELECT o.Id FROM O AS o WHERE o.Id IN {body}");
        long[] notInRows = Ids(e, $"SELECT o.Id FROM O AS o WHERE o.Id NOT IN {body}");

        Assert.Equal([1, 5], inRows);
        Assert.Equal([2, 4], notInRows);
        Assert.DoesNotContain(3L, inRows);
        Assert.DoesNotContain(3L, notInRows);
    }

    [Fact]
    public void Not_in_is_the_complement_when_no_null_is_seen()
        // Same shape without a null in play: the body projects Id, which is a primary key. Every row's value is
        // simply absent from its own body's set, so NOT IN holds for all of them — except row 4, whose left side
        // is NULL (see below).
        => Assert.Equal([1, 2, 3, 5],
            Ids(Fresh(), "SELECT o.Id FROM O AS o WHERE o.K NOT IN (SELECT i.Id FROM I AS i WHERE i.K = o.K)"));

    [Fact]
    public void A_null_left_side_satisfies_neither_form()
    {
        // `NULL IN (…)` is UNKNOWN whatever the set holds, so row 4 appears in neither result.
        QueryEngine e = Fresh();
        const string body = "(SELECT i.Id FROM I AS i WHERE i.K = o.K)";
        Assert.DoesNotContain(4L, Ids(e, $"SELECT o.Id FROM O AS o WHERE o.K IN {body}"));
        Assert.DoesNotContain(4L, Ids(e, $"SELECT o.Id FROM O AS o WHERE o.K NOT IN {body}"));
    }

    [Fact]
    public void An_empty_body_is_false_not_unknown()
        // Row 2 has no partner, so its body is empty. `x IN ()` is FALSE — so NOT IN is TRUE and the row survives.
        // Getting this wrong by treating "no correlation match" as UNKNOWN would drop it.
        => Assert.Contains(2L,
            Ids(Fresh(), "SELECT o.Id FROM O AS o WHERE o.Id NOT IN (SELECT i.V FROM I AS i WHERE i.K = o.K)"));

    [Fact]
    public void A_residual_predicate_still_applies()
        // Keep = 0 on the row holding K = 50, so row 5's body is empty and only row 1 matches.
        => Assert.Equal([1],
            Ids(Fresh(),
                "SELECT o.Id FROM O AS o WHERE o.Id IN (SELECT i.V FROM I AS i WHERE i.K = o.K AND i.Keep = 1)"));

    [Fact]
    public void A_multi_column_correlation_key_works()
        // Rows 1 and 5 are the ones whose inner partner agrees on BOTH Id and K, and whose V then equals their Id.
        // Row 3's partner is I row 2, which agrees on neither.
        => Assert.Equal([1, 5],
            Ids(Fresh(),
                """
                SELECT o.Id FROM O AS o
                WHERE o.Id IN (SELECT i.V FROM I AS i WHERE i.K = o.K AND i.Id = o.Id)
                """));

    [Fact]
    public void Delete_with_a_correlated_in_removes_exactly_the_matching_rows()
    {
        QueryEngine e = Fresh();
        int deleted = e.ExecuteNonQuery("DELETE FROM O WHERE O.Id IN (SELECT i.V FROM I AS i WHERE i.K = O.K)");
        Assert.Equal(2, deleted);
        Assert.Equal([2, 3, 4], Ids(e, "SELECT Id FROM O"));
    }

    [Theory]
    [InlineData(true, "SELECT o.Id FROM O AS o WHERE o.Id IN (SELECT i.V FROM I AS i WHERE i.K = o.K)")]
    [InlineData(true, "SELECT o.Id FROM O AS o WHERE o.Id NOT IN (SELECT i.V FROM I AS i WHERE i.K = o.K)")]
    // Any TOP declines: unlike EXISTS, where TOP n >= 1 cannot change whether a row exists, TOP absolutely
    // changes WHICH values are in the set, so the key query cannot drop it.
    [InlineData(false, "SELECT o.Id FROM O AS o WHERE o.Id IN (SELECT TOP 1 i.V FROM I AS i WHERE i.K = o.K)")]
    // GROUP BY/HAVING decline — see TryBuildForIn.
    [InlineData(false, "SELECT o.Id FROM O AS o WHERE o.Id IN (SELECT i.V FROM I AS i WHERE i.K = o.K GROUP BY i.V)")]
    // The output must be a plain column: a computed one can't be resolved to a type kind, and the hash has to
    // agree with the evaluator's comparison.
    [InlineData(false, "SELECT o.Id FROM O AS o WHERE o.Id IN (SELECT i.V + 1 FROM I AS i WHERE i.K = o.K)")]
    // Uncorrelated: nothing to hash, and ExecuteColumn already hoists the body to once per statement.
    [InlineData(false, "SELECT o.Id FROM O AS o WHERE o.Id IN (SELECT i.V FROM I AS i WHERE i.Keep = 1)")]
    // An outer reference the correlation split didn't consume.
    [InlineData(false, "SELECT o.Id FROM O AS o WHERE o.Id IN (SELECT i.V FROM I AS i WHERE i.K = o.K AND o.Tag <> 'zz')")]
    // A cross-kind comparison: Tag is text, V numeric. `5 = '5'` is true for the evaluator but no hash agrees
    // with that across kinds, so it stays a per-row comparison.
    [InlineData(false, "SELECT o.Id FROM O AS o WHERE o.Tag IN (SELECT i.V FROM I AS i WHERE i.K = o.K)")]
    public void The_analysis_accepts_exactly_these_shapes(bool expected, string sql)
        => Assert.Equal(expected, Decorrelates(Fresh(), sql));

    // As with EXISTS, correctness tests pass whether or not the rewrite fires, so this pins that it DOES — the
    // per-row form re-runs a two-table join for every candidate row. Northwind: 2155 Order Details rows against
    // the orders of customers whose ID starts with F. Measured ~26 s per-row against ~0.2 s decorrelated, so the
    // threshold sits well clear of both.
    [Fact]
    public void The_rewrite_engages_on_a_correlated_in_over_a_join()
    {
        string path = Path.Combine(Path.GetTempPath(), $"insemiperf-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        int deleted = e.ExecuteNonQuery(
            """
            DELETE FROM `Order Details` AS `o`
            WHERE `o`.`OrderID` IN (
                SELECT `o1`.`OrderID`
                FROM `Orders` AS `o1`
                INNER JOIN `Customers` AS `c` ON `o1`.`CustomerID` = `c`.`CustomerID`
                WHERE (`c`.`CustomerID` LIKE 'F%') AND `o1`.`OrderID` = `o`.`OrderID`)
            """);
        sw.Stop();

        Assert.Equal(164, deleted);
        Assert.True(sw.ElapsedMilliseconds < 5000, $"took {sw.ElapsedMilliseconds} ms — the IN was not decorrelated");
    }
}
