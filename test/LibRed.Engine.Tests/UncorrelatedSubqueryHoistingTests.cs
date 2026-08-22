using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// A subquery whose result doesn't depend on the outer row is evaluated once per statement instead of once per
// row. These pin the semantics: hoisting must never change an answer, and must NOT engage where the result does
// depend on the outer row — including when the dependence is written as a BARE column name, which only the
// evaluator's own resolver can settle.
public class UncorrelatedSubqueryHoistingTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "hoist-");
        var e = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));

        // Extra exists ONLY on O, so a bare `Extra` inside a subquery over P must resolve outward.
        e.ExecuteNonQuery("CREATE TABLE O ( Id LONG PRIMARY KEY, K LONG, Extra LONG )");
        foreach (int i in new[] { 1, 2, 3, 4 })
            e.ExecuteNonQuery($"INSERT INTO O (Id, K, Extra) VALUES ({i}, {i * 10}, {i * 10})");

        // P also has a K, so a bare `K` inside a subquery over P binds INWARD to P.K — the mirror of Extra.
        e.ExecuteNonQuery("CREATE TABLE P ( Id LONG PRIMARY KEY, K LONG )");
        e.ExecuteNonQuery("INSERT INTO P (Id, K) VALUES (1, 20)");
        e.ExecuteNonQuery("INSERT INTO P (Id, K) VALUES (2, 30)");
        return e;
    }

    private static long[] Ids(QueryEngine e, string sql)
        => e.ExecuteQuery(sql).Rows.Select(r => Convert.ToInt64(r[0])).OrderBy(x => x).ToArray();

    [Fact]
    public void An_uncorrelated_scalar_subquery_gives_the_same_answer()
        // (SELECT MAX(K) FROM P) is 30 for every row of O, so rows with K < 30 are 1 and 2.
        => Assert.Equal([1, 2],
            Ids(Fresh(), "SELECT o.Id FROM O AS o WHERE o.K < (SELECT MAX(p.K) FROM P AS p)"));

    [Fact]
    public void An_uncorrelated_in_subquery_gives_the_same_answer()
        => Assert.Equal([2, 3],
            Ids(Fresh(), "SELECT o.Id FROM O AS o WHERE o.K IN (SELECT p.K FROM P AS p)"));

    [Fact]
    public void An_uncorrelated_exists_gives_the_same_answer()
        => Assert.Equal([1, 2, 3, 4],
            Ids(Fresh(), "SELECT o.Id FROM O AS o WHERE EXISTS (SELECT 1 FROM P AS p WHERE p.K = 20)"));

    [Fact]
    public void An_uncorrelated_exists_that_is_empty_excludes_everything()
        => Assert.Empty(
            Ids(Fresh(), "SELECT o.Id FROM O AS o WHERE EXISTS (SELECT 1 FROM P AS p WHERE p.K = 999)"));

    [Fact]
    public void A_qualified_correlation_still_varies_per_row()
        // Must NOT hoist: the subquery's value depends on o.K.
        => Assert.Equal([1, 2],
            Ids(Fresh(), "SELECT o.Id FROM O AS o WHERE EXISTS (SELECT 1 FROM P AS p WHERE p.K > o.K)"));

    [Fact]
    public void A_correlation_hidden_in_a_conditional_still_varies_per_row()
        // The static check exists for this: IIF may not evaluate o.K on the row a trial run happens to see, so a
        // trial alone could wrongly conclude the subquery is outer-independent.
        => Assert.Equal([1, 2],
            Ids(Fresh(),
                "SELECT o.Id FROM O AS o WHERE (SELECT MAX(IIF(p.Id > 0, o.K, 0)) FROM P AS p) < 30"));

    [Fact]
    public void An_unqualified_column_that_binds_inward_is_hoisted()
        // Bare `K` inside a subquery over P binds to P.K, so the subquery is outer-independent: MAX is 30.
        => Assert.Equal([1, 2],
            Ids(Fresh(), "SELECT o.Id FROM O AS o WHERE o.K < (SELECT MAX(K) FROM P AS p)"));

    [Fact]
    public void An_unqualified_column_that_binds_outward_is_not_hoisted()
        // `Extra` exists only on O, so the bare name resolves OUTWARD and the subquery is correlated: its value
        // is the current row's Extra (10, 20, 30, 40). Only the evaluator's resolver can tell the difference
        // between this and the inward-binding case above; the two are textually identical in shape.
        => Assert.Equal([1, 2],
            Ids(Fresh(), "SELECT o.Id FROM O AS o WHERE (SELECT MAX(Extra) FROM P AS p) < 25"));

    [Fact]
    public void Delete_with_an_uncorrelated_subquery_removes_the_right_rows()
    {
        QueryEngine e = Fresh();
        int deleted = e.ExecuteNonQuery("DELETE FROM O WHERE O.K < (SELECT MAX(p.K) FROM P AS p)");
        Assert.Equal(2, deleted);
        Assert.Equal([3, 4], Ids(e, "SELECT Id FROM O"));
    }
}
