using LibRed;
using LibRed.Engine;
using LibRed.Engine.Execution;
using LibRed.Sql.Ast;
using LibRed.Sql.Parsing;
using Xunit;

namespace LibRed.Engine.Tests;

// A correlated scalar aggregate — `(SELECT COUNT(*) FROM I WHERE I.K = o.K)` — is computed by one pass grouped by
// the correlation column instead of once per outer row. The semantics that need pinning are all about the outer
// row with NO partner: the correlated body still returns a row there, so absence from the grouped result is not
// null but the aggregate's own empty-input value, which differs per aggregate (COUNT 0, SUM/MIN/MAX null).
public class CorrelatedScalarAggregateTests
{
    private static QueryEngine Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"scagg-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));

        e.ExecuteNonQuery("CREATE TABLE O ( Id LONG PRIMARY KEY, K LONG, Tag TEXT(10) )");
        e.ExecuteNonQuery("INSERT INTO O (Id, K, Tag) VALUES (1, 10, 'a')"); // two partners
        e.ExecuteNonQuery("INSERT INTO O (Id, K, Tag) VALUES (2, 20, 'b')"); // none
        e.ExecuteNonQuery("INSERT INTO O (Id, K, Tag) VALUES (3, 30, 'c')"); // one, with V NULL
        e.ExecuteNonQuery("INSERT INTO O (Id, K, Tag) VALUES (4, NULL, 'd')"); // null key ⇒ none
        e.ExecuteNonQuery("INSERT INTO O (Id, K, Tag) VALUES (5, 50, 'e')"); // one

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

    /// <summary>The scalar's value per outer row, keyed by O.Id, so the values themselves can be asserted.</summary>
    private static Dictionary<long, object?> Values(QueryEngine e, string scalar)
        => e.ExecuteQuery($"SELECT o.Id, {scalar} FROM O AS o")
            .Rows.ToDictionary(r => Convert.ToInt64(r[0]), r => r[1]);

    private static bool Decorrelates(QueryEngine e, string sql)
    {
        var select = (SelectStatement)new AntlrSqlParser().ParseStatement(sql);
        var scalar = (ScalarSubquery)select.Projection[1].Value;

        OutputColumn[] outer =
        [
            new("o", "Id", typeof(long)), new("o", "K", typeof(long)), new("o", "Tag", typeof(string)),
        ];

        return ScalarAggregateSemiJoin.TryBuild(
            (SelectStatement)scalar.Query, outer, new(StringComparer.OrdinalIgnoreCase) { "o" },
            e.Database.Catalog) is not null;
    }

    [Fact]
    public void Count_over_no_partners_is_zero_not_null()
    {
        // The whole point. Rows 2 and 4 have no partner, and their key is simply absent from the grouped pass —
        // but COUNT(*) over nothing is 0, so treating absence as null would report the wrong value AND make the
        // `= 0` predicate select nothing.
        Dictionary<long, object?> counts = Values(Fresh(), "(SELECT COUNT(*) FROM I AS i WHERE i.K = o.K)");
        Assert.Equal(2, Convert.ToInt32(counts[1]));
        Assert.Equal(0, Convert.ToInt32(counts[2]));
        Assert.Equal(1, Convert.ToInt32(counts[3]));
        Assert.Equal(0, Convert.ToInt32(counts[4])); // null correlation value ⇒ empty body ⇒ 0
        Assert.Equal(1, Convert.ToInt32(counts[5]));

        Assert.Equal([2, 4],
            Ids(Fresh(), "SELECT o.Id FROM O AS o WHERE (SELECT COUNT(*) FROM I AS i WHERE i.K = o.K) = 0"));
    }

    [Fact]
    public void Sum_over_no_partners_is_null()
    {
        // Same absence, opposite value: SUM of nothing is NULL. Row 3 is null for a different reason — it HAS a
        // partner, whose V is null — and the two must agree, since the correlated form can't tell them apart.
        Dictionary<long, object?> sums = Values(Fresh(), "(SELECT SUM(i.V) FROM I AS i WHERE i.K = o.K)");
        Assert.Equal(4L, Convert.ToInt64(sums[1]));
        Assert.Null(sums[2]);
        Assert.Null(sums[3]);
        Assert.Null(sums[4]);
        Assert.Equal(5L, Convert.ToInt64(sums[5]));
    }

    [Fact]
    public void Max_over_no_partners_is_null()
    {
        Dictionary<long, object?> maxes = Values(Fresh(), "(SELECT MAX(i.V) FROM I AS i WHERE i.K = o.K)");
        Assert.Equal(3L, Convert.ToInt64(maxes[1]));
        Assert.Null(maxes[2]);
        Assert.Equal(5L, Convert.ToInt64(maxes[5]));
    }

    [Fact]
    public void A_predicate_over_the_aggregate_selects_the_right_rows()
        => Assert.Equal([1],
            Ids(Fresh(), "SELECT o.Id FROM O AS o WHERE (SELECT COUNT(*) FROM I AS i WHERE i.K = o.K) > 1"));

    [Fact]
    public void A_residual_predicate_still_applies()
    {
        // Keep = 0 on the partner of row 5, so its count drops to 0 while row 1's stays at 2.
        Dictionary<long, object?> counts =
            Values(Fresh(), "(SELECT COUNT(*) FROM I AS i WHERE i.K = o.K AND i.Keep = 1)");
        Assert.Equal(2, Convert.ToInt32(counts[1]));
        Assert.Equal(0, Convert.ToInt32(counts[5]));
    }

    [Fact]
    public void A_multi_column_correlation_key_works()
    {
        Dictionary<long, object?> counts =
            Values(Fresh(), "(SELECT COUNT(*) FROM I AS i WHERE i.K = o.K AND i.Id = o.Id)");
        Assert.Equal(1, Convert.ToInt32(counts[1])); // I row 1: Id 1, K 10
        Assert.Equal(0, Convert.ToInt32(counts[3])); // I row 2 has K 30 but Id 2
        Assert.Equal(1, Convert.ToInt32(counts[5])); // I row 5: Id 5, K 50
    }

    [Fact]
    public void Count_distinct_is_per_key()
    {
        // Both of row 1's partners are counted distinctly by V (1 and 3); a DISTINCT inside the aggregate is
        // unaffected by the grouping, since the group holds exactly the rows the correlation admitted.
        Dictionary<long, object?> counts =
            Values(Fresh(), "(SELECT COUNT(DISTINCT i.V) FROM I AS i WHERE i.K = o.K)");
        Assert.Equal(2, Convert.ToInt32(counts[1]));
        Assert.Equal(0, Convert.ToInt32(counts[3])); // its only partner's V is null, which COUNT(col) skips
    }

    [Fact]
    public void A_non_aggregate_scalar_body_falls_back_and_keeps_first_row_semantics()
    {
        // TOP 1 with an ORDER BY is "the first row", which depends on an ordering this rewrite would discard, so
        // it declines. Row 1's partners are I rows 1 and 3; ordered by Id descending the first is 3, whose V is 3.
        Dictionary<long, object?> values =
            Values(Fresh(), "(SELECT TOP 1 i.V FROM I AS i WHERE i.K = o.K ORDER BY i.Id DESC)");
        Assert.Equal(3L, Convert.ToInt64(values[1]));
        Assert.Null(values[2]); // no rows ⇒ a scalar subquery is NULL
    }

    [Theory]
    [InlineData(true, "SELECT o.Id, (SELECT COUNT(*) FROM I AS i WHERE i.K = o.K) FROM O AS o")]
    [InlineData(true, "SELECT o.Id, (SELECT SUM(i.V) FROM I AS i WHERE i.K = o.K) FROM O AS o")]
    [InlineData(true, "SELECT o.Id, (SELECT COUNT(DISTINCT i.V) FROM I AS i WHERE i.K = o.K) FROM O AS o")]
    // First-row semantics, not an aggregate: the answer depends on an ordering the rewrite discards.
    [InlineData(false, "SELECT o.Id, (SELECT TOP 1 i.V FROM I AS i WHERE i.K = o.K ORDER BY i.Id) FROM O AS o")]
    [InlineData(false, "SELECT o.Id, (SELECT i.V FROM I AS i WHERE i.K = o.K) FROM O AS o")]
    // An expression around the aggregate would need its own empty-input evaluation, so only a lone call is taken.
    [InlineData(false, "SELECT o.Id, (SELECT COUNT(*) + 1 FROM I AS i WHERE i.K = o.K) FROM O AS o")]
    // A GROUP BY body yields several rows for the outer row to take the first of.
    [InlineData(false, "SELECT o.Id, (SELECT COUNT(*) FROM I AS i WHERE i.K = o.K GROUP BY i.V) FROM O AS o")]
    // Uncorrelated: already hoisted to once per statement.
    [InlineData(false, "SELECT o.Id, (SELECT COUNT(*) FROM I AS i WHERE i.Keep = 1) FROM O AS o")]
    // An outer reference the correlation split didn't consume.
    [InlineData(false, "SELECT o.Id, (SELECT COUNT(*) FROM I AS i WHERE i.K = o.K AND o.Tag <> 'zz') FROM O AS o")]
    public void The_analysis_accepts_exactly_these_shapes(bool expected, string sql)
        => Assert.Equal(expected, Decorrelates(Fresh(), sql));

    // As elsewhere, the correctness tests pass whether or not the rewrite fires, so this pins that it DOES.
    [Fact]
    public void The_rewrite_engages_on_a_correlated_count_over_northwind()
    {
        string path = Path.Combine(Path.GetTempPath(), $"scaggperf-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        int rows = e.ExecuteQuery(
            """
            SELECT `o`.`OrderID`
            FROM `Order Details` AS `o`
            WHERE (
                SELECT COUNT(*)
                FROM `Orders` AS `o1`
                INNER JOIN `Customers` AS `c` ON `o1`.`CustomerID` = `c`.`CustomerID`
                WHERE `o1`.`OrderID` = `o`.`OrderID`) > 0
            """).Rows.Count();
        sw.Stop();

        Assert.Equal(2155, rows);
        Assert.True(sw.ElapsedMilliseconds < 5000, $"took {sw.ElapsedMilliseconds} ms — the aggregate was not decorrelated");
    }
}
