using System.Linq;
using LibRed;
using LibRed.Engine;
using LibRed.Engine.Plan;
using LibRed.Sql.Ast;
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

    private static QueryEngine Lateral()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "pd-lat-");
        var e = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE `L` (`Id` LONG NOT NULL PRIMARY KEY, `S` TEXT(10))");
        e.ExecuteNonQuery("CREATE TABLE `R` (`Id` LONG NOT NULL PRIMARY KEY, `T` TEXT(10))");
        e.ExecuteNonQuery("INSERT INTO `L` (`Id`, `S`) VALUES (1, 'a'), (2, 'b'), (3, 'c')");
        e.ExecuteNonQuery("INSERT INTO `R` (`Id`, `T`) VALUES (1, 'x'), (2, 'y')");
        return e;
    }

    /// <summary>The lateral (APPLY) joins in a plan, outermost first.</summary>
    private static IEnumerable<JoinNode> Laterals(PlanNode node)
    {
        if (node is JoinNode { Kind: JoinKind.CrossApply or JoinKind.OuterApply } j)
            yield return j;
        foreach (JoinNode found in node.Children.SelectMany(Laterals))
            yield return found;
    }

    /// <summary>Whether a filter sits anywhere above the first lateral join — i.e. a conjunct that failed to
    /// sink into it. The walk stops at the lateral, so filters inside it don't count.</summary>
    private static bool FilterAboveLateral(PlanNode node)
        => node is not JoinNode { Kind: JoinKind.CrossApply or JoinKind.OuterApply }
            && (node is FilterNode || node.Children.Any(FilterAboveLateral));

    private static bool HasFilter(PlanNode node) => node is FilterNode || node.Children.Any(HasFilter);

    [Fact]
    public void A_predicate_confined_to_an_applys_left_side_is_pushed_into_it()
    {
        // Both APPLY kinds preserve the left, so dropping a left row up front removes exactly the output rows
        // the WHERE would have removed afterwards — and saves running the whole right side for it.
        var e = Lateral();
        const string sql = "SELECT `L`.`S`, `r`.`T` FROM `L` "
            + "OUTER APPLY (SELECT * FROM `R` WHERE `R`.`Id` = `L`.`Id`) AS `r` WHERE `L`.`Id` = 2";
        PlanNode plan = e.PlanFor(sql);

        Assert.False(FilterAboveLateral(plan), "expected `L`.`Id` = 2 to sink into the APPLY's left side");
        Assert.True(HasFilter(Laterals(plan).Single().Left));
        Assert.Equal(["by"], e.ExecuteQuery(sql).Rows.Select(r => $"{r[0]}{r[1]}").ToArray());
    }

    [Fact]
    public void A_predicate_on_an_outer_applys_right_side_stays_above_it()
    {
        // It must NOT sink: filtering inside the right side can empty an otherwise non-empty result, which
        // OUTER APPLY then reports as a null-padded row — the very row the WHERE was there to drop.
        var e = Lateral();
        const string sql = "SELECT `L`.`S`, `r`.`T` FROM `L` "
            + "OUTER APPLY (SELECT * FROM `R` WHERE `R`.`Id` = `L`.`Id`) AS `r` WHERE `r`.`T` = 'x'";
        PlanNode plan = e.PlanFor(sql);

        Assert.True(FilterAboveLateral(plan));
        Assert.False(HasFilter(Laterals(plan).Single().Left));
        Assert.Equal(["ax"], e.ExecuteQuery(sql).Rows.Select(r => $"{r[0]}{r[1]}").ToArray());
    }

    [Fact]
    public void A_correlated_predicate_sinks_even_though_it_names_an_outer_alias()
    {
        // The nested-APPLY shape EF emits for a collection inside a collection. The inner query's
        // `R`.`Id` = `L`.`Id` qualifies `L`, which belongs to the enclosing query — an outer column is readable
        // at any depth, so it must not stop the conjunct from reaching the `R` scan. Left above the inner
        // APPLY it would pair every R row with the whole of R before filtering: the cross product this test
        // exists to prevent.
        var e = Lateral();
        const string sql = "SELECT `L`.`S`, `r`.`T` FROM `L` OUTER APPLY ("
            + "  SELECT `R`.`T` FROM `R` CROSS APPLY (SELECT `R2`.`Id` FROM `R` AS `R2`) AS `x` "
            + "  WHERE `R`.`Id` = `L`.`Id`) AS `r` ORDER BY `L`.`Id`, `r`.`T`";
        JoinNode inner = Laterals(e.PlanFor(sql)).ElementAt(1);

        Assert.True(HasFilter(inner.Left), "expected the correlated predicate to sink onto the inner scan");
        Assert.Equal(["ax", "ax", "by", "by", "c-"],
            e.ExecuteQuery(sql).Rows.Select(r => $"{r[0]}{r[1] ?? "-"}").ToArray());
    }
}
