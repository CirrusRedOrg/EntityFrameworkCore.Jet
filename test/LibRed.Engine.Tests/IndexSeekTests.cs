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
}
