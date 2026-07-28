using System.Diagnostics;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// Decorrelating a correlated subquery is sound from the first probe but not always cheaper: it runs the body once
// WITHOUT the correlation, so a body that per-row would have been one index seek pays a full pass instead. These
// pin both directions of that trade — a tiny outer over a big indexed inner must stay per-row, while a genuinely
// slow correlation must still switch. They are timing tests, so the thresholds are set an order of magnitude clear
// of the measured figures rather than close to them.
public class DecorrelationCostTests
{
    private const int InnerRows = 20_000;

    /// <summary>A small outer table and a large indexed inner one, the shape decorrelation must NOT take over.</summary>
    private static QueryEngine SmallOuterLargeInner()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cost-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));

        e.ExecuteNonQuery("CREATE TABLE Small ( Id LONG PRIMARY KEY )");
        for (var i = 1; i <= 3; i++)
        {
            e.ExecuteNonQuery($"INSERT INTO Small (Id) VALUES ({i})");
        }

        e.ExecuteNonQuery("CREATE TABLE Big ( Id LONG PRIMARY KEY, K LONG )");
        e.ExecuteNonQuery("BEGIN TRANSACTION");
        for (var i = 1; i <= InnerRows; i++)
        {
            e.ExecuteNonQuery($"INSERT INTO Big (Id, K) VALUES ({i}, {i})");
        }

        e.ExecuteNonQuery("COMMIT");
        e.ExecuteNonQuery("CREATE INDEX IX_Big_K ON Big (K)");
        return e;
    }

    private static long Time(QueryEngine e, string sql)
    {
        e.ExecuteQuery(sql).Rows.Count(); // warm the plan and the pages
        var sw = Stopwatch.StartNew();
        e.ExecuteQuery(sql).Rows.Count();
        return sw.ElapsedMilliseconds;
    }

    [Theory]
    // Three outer rows, each answered by an index seek into 20,000 rows. Decorrelating would hash all 20,000 to
    // answer three questions: measured 0.2 ms per-row against 32.8 ms decorrelated for EXISTS, 0.1 against 34.5
    // for IN, and 0.2 against 117.7 for the COUNT. The gate keeps all three per-row, since three seeks never
    // reach its budget.
    [InlineData("SELECT s.Id FROM Small AS s WHERE EXISTS (SELECT 1 FROM Big AS b WHERE b.K = s.Id)")]
    [InlineData("SELECT s.Id FROM Small AS s WHERE s.Id IN (SELECT b.K FROM Big AS b WHERE b.K = s.Id)")]
    [InlineData("SELECT s.Id FROM Small AS s WHERE (SELECT COUNT(*) FROM Big AS b WHERE b.K = s.Id) > 0")]
    public void A_tiny_outer_over_a_large_indexed_inner_stays_per_row(string sql)
    {
        QueryEngine e = SmallOuterLargeInner();
        Assert.Equal(3, e.ExecuteQuery(sql).Rows.Count()); // and still answers correctly
        long ms = Time(e, sql);
        Assert.True(ms < 15, $"took {ms} ms — the body was decorrelated when per-row was ~0.2 ms");
    }

    [Fact]
    public void The_same_body_does_switch_once_the_outer_is_large()
    {
        // Same tables, outer and inner swapped: now every one of the 20,000 outer rows asks the question, so the
        // per-row form pays 20,000 seeks and the gate switches after the first few. This is the guard that the
        // cost check didn't simply disable decorrelation — without the switch this shape is far slower.
        QueryEngine e = SmallOuterLargeInner();
        const string sql = "SELECT b.Id FROM Big AS b WHERE EXISTS (SELECT 1 FROM Small AS s WHERE s.Id = b.K)";

        Assert.Equal(3, e.ExecuteQuery(sql).Rows.Count());
        long ms = Time(e, sql);
        Assert.True(ms < 2000, $"took {ms} ms");
    }
}
