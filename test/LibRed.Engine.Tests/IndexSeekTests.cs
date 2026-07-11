using System.Linq;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// A WHERE equality on a single-column-indexed column is served by an index seek (IndexSelection →
/// IndexSeekNode) instead of a full scan, with the original predicate kept as a residual re-check. These
/// verify the results are identical to a scan — the seek must be a pure speedup, never change the answer.
/// </summary>
public class IndexSeekTests
{
    private static QueryEngine Seeded()
    {
        string path = Path.Combine(Path.GetTempPath(), $"seek-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE B (Id LONG PRIMARY KEY, K LONG, V TEXT(40))");
        e.ExecuteNonQuery("CREATE INDEX IX_K ON B (K)");
        for (int i = 0; i < 500; i++) e.ExecuteNonQuery($"INSERT INTO B (Id, K, V) VALUES ({i}, {i % 10}, 'v{i}')");
        return e;
    }

    private static object?[] Single(QueryEngine e, string sql) => e.ExecuteQuery(sql).Rows.Single();
    private static int Count(QueryEngine e, string sql) => e.ExecuteQuery(sql).Rows.Count();

    [Fact]
    public void Primary_key_equality_returns_the_one_row()
    {
        var r = Single(Seeded(), "SELECT V FROM B WHERE Id = 123");
        Assert.Equal("v123", r[0]);
    }

    [Fact]
    public void Secondary_index_equality_returns_all_matches()
        => Assert.Equal(50, Count(Seeded(), "SELECT Id FROM B WHERE K = 7")); // 500 rows, i%10==7

    [Fact]
    public void Equality_with_a_parameter_seeks()
    {
        var r = Seeded().ExecuteQuery("SELECT V FROM B WHERE Id = @p",
            new Dictionary<string, object?> { ["@p"] = 200 }).Rows.Single();
        Assert.Equal("v200", r[0]);
    }

    [Fact]
    public void A_non_indexed_column_equality_still_works_via_scan()
        => Assert.Equal("v321", Single(Seeded(), "SELECT V FROM B WHERE V = 'v321'")[0]);

    [Fact]
    public void The_residual_predicate_still_applies_with_extra_conjuncts()
        // K=7 seeks (50 candidates), Id>250 is the residual filter over them.
        => Assert.Equal(25, Count(Seeded(), "SELECT Id FROM B WHERE K = 7 AND Id > 250"));

    [Fact]
    public void No_match_returns_empty()
        => Assert.Equal(0, Count(Seeded(), "SELECT Id FROM B WHERE Id = 99999"));

    // --- index-nested-loop join: the inner side is seeked per outer row, not scanned ---
    private static QueryEngine TwoTables()
    {
        string path = Path.Combine(Path.GetTempPath(), $"nlj-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE P (Id LONG PRIMARY KEY, Nm TEXT(20))");
        e.ExecuteNonQuery("CREATE TABLE C (Id LONG PRIMARY KEY, Pid LONG, Amt LONG)");
        e.ExecuteNonQuery("CREATE INDEX IX_Pid ON C (Pid)");
        for (int i = 0; i < 100; i++) e.ExecuteNonQuery($"INSERT INTO P (Id, Nm) VALUES ({i}, 'p{i}')");
        for (int i = 0; i < 500; i++) e.ExecuteNonQuery($"INSERT INTO C (Id, Pid, Amt) VALUES ({i}, {i % 100}, {i})");
        return e;
    }

    [Fact]
    public void Index_nested_loop_join_matches_a_scan_join()
    {
        // C.Pid is indexed → the inner (C) is seeked per P row. Each P has 5 children (500/100).
        var e = TwoTables();
        Assert.Equal(500, Count(e, "SELECT P.Nm, C.Amt FROM P INNER JOIN C ON P.Id = C.Pid"));
        Assert.Equal(5, Count(e, "SELECT C.Amt FROM P INNER JOIN C ON P.Id = C.Pid WHERE P.Id = 7"));
    }

    [Fact]
    public void Index_nested_loop_join_honours_a_residual_on_condition()
    {
        // The join seeks C by Pid, then the extra ON conjunct (C.Amt > 200) is the residual check.
        var e = TwoTables();
        int viaJoin = Count(e, "SELECT C.Amt FROM P INNER JOIN C ON P.Id = C.Pid AND C.Amt > 200");
        int viaScan = Count(e, "SELECT Amt FROM C WHERE Amt > 200"); // every C has a P, so equivalent
        Assert.Equal(viaScan, viaJoin);
    }

    [Fact]
    public void Join_on_an_unindexed_inner_column_still_works()
    {
        // Joining on C.Amt (no index) falls back to the scan-based nested loop — must still be correct.
        var e = TwoTables();
        Assert.Equal(1, Count(e, "SELECT P.Nm FROM P INNER JOIN C ON P.Id = C.Amt WHERE C.Amt = 50"));
    }
}
