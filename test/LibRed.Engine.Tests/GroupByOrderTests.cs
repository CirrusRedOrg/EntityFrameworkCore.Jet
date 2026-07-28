using System.Linq;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// Access returns GROUP BY output ascending by the grouping columns when there is no explicit ORDER BY. LibRed
/// matches this — which also makes a TOP-1-over-a-GROUP-BY deterministic (e.g. a scalar subquery that picks the
/// "first" group), as SQL Server and Access do.
/// </summary>
public class GroupByOrderTests
{
    private static QueryEngine Seeded()
    {
        string path = Path.Combine(Path.GetTempPath(), $"gbo-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE T (Id LONG PRIMARY KEY, K TEXT(5), N LONG)");
        // Insert with keys deliberately out of order (D, A, C, B) so insertion order != sorted order.
        (string k, int n)[] rows = [("D", 1), ("A", 2), ("C", 3), ("B", 4), ("A", 5), ("C", 6)];
        for (int i = 0; i < rows.Length; i++) e.ExecuteNonQuery($"INSERT INTO T (Id, K, N) VALUES ({i}, '{rows[i].k}', {rows[i].n})");
        return e;
    }

    [Fact]
    public void Group_by_output_is_ascending_by_key()
    {
        var e = Seeded();
        var keys = e.ExecuteQuery("SELECT K, SUM(N) FROM T GROUP BY K").Rows.Select(r => (string)r[0]!).ToList();
        Assert.Equal(new[] { "A", "B", "C", "D" }, keys);
    }

    [Fact]
    public void Top_1_over_a_group_by_picks_the_smallest_key_group()
    {
        var e = Seeded();
        // Without this deterministic ordering, TOP 1 could return any group; it must be the smallest key ("A").
        Assert.Equal("A", e.ExecuteQuery("SELECT TOP 1 K FROM T GROUP BY K").Rows.Single()[0]);
    }

    [Fact]
    public void Explicit_order_by_still_wins()
    {
        var e = Seeded();
        var keys = e.ExecuteQuery("SELECT K, SUM(N) FROM T GROUP BY K ORDER BY SUM(N) DESC").Rows.Select(r => (string)r[0]!).ToList();
        // Sums: A=7, B=4, C=9, D=1 → descending by sum: C, A, B, D.
        Assert.Equal(new[] { "C", "A", "B", "D" }, keys);
    }
}
