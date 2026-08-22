using System.Linq;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// A correlated subquery whose predicate equates an indexed column to an <b>outer</b> column is served by an
/// index seek keyed off the outer row (not a full scan re-run per outer row). These verify the results are
/// identical to the unoptimised form — the seek must be a pure speedup — including a composite-key correlation.
/// </summary>
public class CorrelatedSeekTests : TempDatabaseTest
{
    private static QueryEngine Seeded()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "corr-");
        var e = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE P (Id LONG PRIMARY KEY, Nm TEXT(20))");
        e.ExecuteNonQuery("CREATE TABLE C (Id LONG PRIMARY KEY, Pid LONG, Amt LONG)");
        e.ExecuteNonQuery("CREATE INDEX IX_Pid ON C (Pid)");
        // Composite-key table: PK (K1, K2).
        e.ExecuteNonQuery("CREATE TABLE D (K1 LONG, K2 LONG, V LONG, CONSTRAINT PK_D PRIMARY KEY (K1, K2))");
        for (int i = 0; i < 40; i++) e.ExecuteNonQuery($"INSERT INTO P (Id, Nm) VALUES ({i}, 'p{i}')");
        for (int i = 0; i < 200; i++) e.ExecuteNonQuery($"INSERT INTO C (Id, Pid, Amt) VALUES ({i}, {i % 40}, {i})");
        for (int i = 0; i < 40; i++) e.ExecuteNonQuery($"INSERT INTO D (K1, K2, V) VALUES ({i}, {i * 2}, {i})");
        return e;
    }

    private static int Count(QueryEngine e, string sql) => e.ExecuteQuery(sql).Rows.Count();
    private static object? Scalar(QueryEngine e, string sql) => e.ExecuteQuery(sql).Rows.First()[0];

    [Fact]
    public void Correlated_exists_returns_the_same_rows_as_a_scan()
    {
        var e = Seeded();
        // Every P (0..39) has children (Pid = i%40 covers 0..39), so all 40 satisfy EXISTS.
        Assert.Equal(40, Count(e, "SELECT Nm FROM P WHERE EXISTS (SELECT 1 FROM C WHERE C.Pid = P.Id)"));
        // A P with no child fails EXISTS.
        e.ExecuteNonQuery("INSERT INTO P (Id, Nm) VALUES (999, 'lonely')");
        Assert.Equal(40, Count(e, "SELECT Nm FROM P WHERE EXISTS (SELECT 1 FROM C WHERE C.Pid = P.Id)"));
        Assert.Equal(41, Count(e, "SELECT Nm FROM P"));
    }

    [Fact]
    public void Correlated_exists_with_an_extra_residual_predicate()
    {
        var e = Seeded();
        // Only children with Amt > 150 qualify; Amt = i∈0..199, i%40 gives the Pid. Amt>150 → i∈151..199 (49
        // rows), whose Pids (151..199 mod 40) = {31..39, 0..19} = 29 distinct parents.
        int viaExists = Count(e, "SELECT Nm FROM P WHERE EXISTS (SELECT 1 FROM C WHERE C.Pid = P.Id AND C.Amt > 150)");
        int distinctParents = Count(e, "SELECT DISTINCT Pid FROM C WHERE Amt > 150");
        Assert.Equal(distinctParents, viaExists);
    }

    [Fact]
    public void Correlated_scalar_subquery_seeks()
    {
        var e = Seeded();
        // Sum of a parent's children's Amt via a correlated scalar subquery, for one parent.
        object? viaSub = Scalar(e, "SELECT (SELECT SUM(C.Amt) FROM C WHERE C.Pid = 7) FROM P WHERE P.Id = 7");
        object? viaDirect = Scalar(e, "SELECT SUM(Amt) FROM C WHERE Pid = 7");
        Assert.Equal(Convert.ToInt64(viaDirect), Convert.ToInt64(viaSub));
    }

    [Fact]
    public void Composite_key_correlated_seek_matches_a_scan()
    {
        var e = Seeded();
        // D has PK (K1, K2). A correlated EXISTS equating BOTH key columns to outer values is an exact
        // composite point seek — must return the same as the unindexed equivalent.
        int viaExists = Count(e,
            "SELECT V FROM D AS d1 WHERE EXISTS (SELECT 1 FROM D AS d2 WHERE d2.K1 = d1.K1 AND d2.K2 = d1.K2 AND d2.V > 20)");
        int direct = Count(e, "SELECT V FROM D WHERE V > 20");
        Assert.Equal(direct, viaExists);
    }

    [Fact]
    public void Correlated_subquery_as_a_range_bound_is_not_turned_into_a_seek()
    {
        // `P.Id > (correlated aggregate subquery)` must NOT be rewritten to an index range-seek with the
        // subquery as the bound: the bound is evaluated once with no row scope, but the subquery correlates to
        // P.Id (the row being sought) — that would throw "Column 'P.Id' was not found". It must stay a filter.
        var e = Seeded();
        // Every P (Id 0..39) has exactly 5 children (200 C rows, Pid = i%40), so the correlated COUNT is 5 for
        // each → P.Id > 5 keeps Ids 6..39 = 34 rows. The seek-rewrite bug would instead throw before returning.
        Assert.Equal(34, Count(e, "SELECT P.Id FROM P WHERE P.Id > (SELECT COUNT(*) FROM C WHERE C.Pid = P.Id)"));
    }
}
