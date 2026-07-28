using System.Diagnostics;
using System.Linq;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// The planner pushes WHERE conjuncts into the join tree so an Access comma-join (planned as CROSS joins)
/// filters inside the nested loop instead of materializing the full cross product. Without this a 4-table
/// comma-join is O(product of table sizes) — catastrophic for real queries like Northwind's CustOrderHist.
/// </summary>
public class PredicatePushdownTests
{
    private static QueryEngine FourTables(int rowsEach)
    {
        string path = Path.Combine(Path.GetTempPath(), $"pd-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));
        foreach (string t in new[] { "A", "B", "C", "D" })
        {
            e.ExecuteNonQuery($"CREATE TABLE {t} (k LONG PRIMARY KEY, v LONG)");
            for (int i = 0; i < rowsEach; i++) e.ExecuteNonQuery($"INSERT INTO {t} (k, v) VALUES ({i}, {i * 10})");
        }
        return e;
    }

    [Fact]
    public void Comma_join_equi_chain_is_correct_and_does_not_materialize_the_cross_product()
    {
        // 60^4 = 12.96M cross product; a pushed equi-join returns 60. If pushdown regressed this test would
        // still finish (bounded) but take orders of magnitude longer — the 2s cap guards against that.
        var e = FourTables(60);
        var sw = Stopwatch.StartNew();
        var rows = e.ExecuteQuery(
            "SELECT A.v, D.v FROM A, B, C, D WHERE A.k = B.k AND B.k = C.k AND C.k = D.k").Rows.ToList();
        sw.Stop();

        Assert.Equal(60, rows.Count);
        Assert.True(sw.ElapsedMilliseconds < 2000, $"comma-join took {sw.ElapsedMilliseconds} ms — cross product not pushed down?");
    }

    [Fact]
    public void A_single_table_predicate_is_pushed_onto_its_scan()
    {
        var e = FourTables(60);
        var rows = e.ExecuteQuery(
            "SELECT A.v FROM A, B, C, D WHERE A.k = B.k AND B.k = C.k AND C.k = D.k AND A.k = 5").Rows.ToList();
        Assert.Equal([50L], rows.Single().Select(Convert.ToInt64).ToArray());
    }
}
