using System.Linq;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// ORDER BY and OFFSET/FETCH belong to a query expression, so on a set operation they order and page the
/// <b>combined result</b> — not the last operand, which is what the grammar used to make them mean and which
/// silently returned rows in the wrong order. Measured against ACE, which agrees
/// (<c>UnionOrderByShapeProbeTest</c>). An operand that wants its own ordering must be parenthesised.
/// </summary>
public class SetOperationOrderByTests : TempDatabaseTest
{
    // Neither table is stored in sorted order, so "ordered as a whole" and "only the last operand ordered" give
    // visibly different answers. B repeats A's 50 so UNION's dedupe is observable.
    private static QueryEngine Seeded()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "setop-ob-");
        var engine = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        engine.ExecuteNonQuery("CREATE TABLE `A` (`Id` LONG NOT NULL PRIMARY KEY, `V` LONG)");
        engine.ExecuteNonQuery("CREATE TABLE `B` (`Id` LONG NOT NULL PRIMARY KEY, `V` LONG)");
        engine.ExecuteNonQuery("INSERT INTO `A` (`Id`, `V`) VALUES (1, 50), (2, 10)");
        engine.ExecuteNonQuery("INSERT INTO `B` (`Id`, `V`) VALUES (1, 40), (2, 20), (3, 50)");
        return engine;
    }

    private static int[] V(QueryEngine e, string sql)
        => e.ExecuteQuery(sql).Rows.Select(r => Convert.ToInt32(r[0])).ToArray();

    [Fact]
    public void A_trailing_order_by_orders_the_whole_union()
        // Binding it to the last operand would give A unsorted (50,10) followed by B sorted.
        => Assert.Equal([10, 20, 40, 50, 50], V(Seeded(),
            "SELECT `V` FROM `A` UNION ALL SELECT `V` FROM `B` ORDER BY `V`"));

    [Fact]
    public void A_trailing_order_by_honours_a_direction()
        => Assert.Equal([50, 50, 40, 20, 10], V(Seeded(),
            "SELECT `V` FROM `A` UNION ALL SELECT `V` FROM `B` ORDER BY `V` DESC"));

    [Fact]
    public void The_dedupe_happens_before_the_ordering()
        // UNION's duplicate 50 collapses, and the survivor is still placed by the ordering.
        => Assert.Equal([10, 20, 40, 50], V(Seeded(),
            "SELECT `V` FROM `A` UNION SELECT `V` FROM `B` ORDER BY `V`"));

    [Fact]
    public void Paging_applies_to_the_combined_result()
        // Over 10,20,40,50,50: skip one, take two.
        => Assert.Equal([20, 40], V(Seeded(),
            "SELECT `V` FROM `A` UNION ALL SELECT `V` FROM `B` ORDER BY `V` "
            + "OFFSET 1 ROWS FETCH NEXT 2 ROWS ONLY"));

    [Fact]
    public void An_order_by_on_a_non_final_operand_is_a_parse_error()
        // The grammar states where the clause may appear, so this is refused rather than silently applied to
        // one arm. ACE parses and ignores it, which means such a query never depended on it anyway.
        => Assert.ThrowsAny<Exception>(() => Seeded().ExecuteQuery(
            "SELECT `V` FROM `A` ORDER BY `V` UNION ALL SELECT `V` FROM `B`"));

    [Fact]
    public void A_parenthesised_operand_may_carry_its_own_ordering()
    {
        // The load-bearing case, and the reason an operand's ORDER BY is not simply banned: parentheses make it
        // a nested query expression, where the ordering is what makes the TOP deterministic. Ordering A
        // ascending and taking one gives 10, not the 50 that storage order would.
        Assert.Equal([10, 20, 40, 50], V(Seeded(),
            "(SELECT TOP 1 `V` FROM `A` ORDER BY `V`) UNION ALL SELECT `V` FROM `B` ORDER BY `V`"));
    }

    [Fact]
    public void A_leading_TOP_still_belongs_to_its_own_operand()
        // TOP stays on selectCore, so each side is limited separately and only the ordering is shared:
        // TOP 1 of A in storage order is 50, TOP 1 of B is 40.
        => Assert.Equal([40, 50], V(Seeded(),
            "SELECT TOP 1 `V` FROM `A` UNION ALL SELECT TOP 1 `V` FROM `B` ORDER BY `V`"));

    [Fact]
    public void An_ordinary_select_is_unchanged()
        // The regression guard for the fold: with no set operation the clauses go back onto the SELECT, so the
        // AST — and everything downstream of it — is exactly what it was before the grammar moved them.
        => Assert.Equal([10, 50], V(Seeded(), "SELECT `V` FROM `A` ORDER BY `V`"));

    [Fact]
    public void An_ordinary_select_still_pages()
        => Assert.Equal([50], V(Seeded(),
            "SELECT `V` FROM `A` ORDER BY `V` OFFSET 1 ROWS FETCH NEXT 1 ROWS ONLY"));
}
