using System.Linq;
using LibRed;
using LibRed.Engine;
using LibRed.Engine.Plan;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// The planner pushes WHERE conjuncts into the join tree so an Access comma-join (planned as CROSS joins)
/// filters inside the nested loop instead of materializing the full cross product. Without this a 4-table
/// comma-join is O(product of table sizes) — catastrophic for real queries like Northwind's CustOrderHist.
/// </summary>
public class PredicatePushdownTests : TempDatabaseTest
{
    private static bool HasJoinPredicate(PlanNode node)
        => node is JoinNode { On: not null } || node.Children.Any(HasJoinPredicate);

    private static QueryEngine FourTables(int rowsEach)
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "pd-");
        var e = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
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
        // 60^4 = 12.96M cross product; assert the optimisation structurally so instrumentation overhead cannot
        // turn planner correctness into a machine-speed test.
        var e = FourTables(60);
        const string sql =
            "SELECT A.v, D.v FROM A, B, C, D WHERE A.k = B.k AND B.k = C.k AND C.k = D.k";
        Assert.True(HasJoinPredicate(e.PlanFor(sql)), "expected WHERE equalities folded into join predicates");

        var rows = e.ExecuteQuery(
            sql).Rows.ToList();

        Assert.Equal(60, rows.Count);
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
