using System.Linq;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// A subquery holds a full query expression, so a set operation is legal in every position that takes one:
/// <c>IN (…)</c>, <c>EXISTS (…)</c> and a scalar <c>(…)</c>. The standard reaches all three through the same
/// <c>&lt;query expression&gt;</c> nonterminal a derived table uses — which LibRed has always allowed — so this
/// closes an inconsistency rather than adding an extension. EF Core emits the IN form once its generator elides
/// the wrapping select it would otherwise put around the union.
/// </summary>
public class SetOperationSubqueryTests : TempDatabaseTest
{
    private static QueryEngine Seeded()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "setsub-");
        var engine = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        engine.ExecuteNonQuery("CREATE TABLE `A` (`Id` LONG NOT NULL PRIMARY KEY, `V` LONG)");
        engine.ExecuteNonQuery("CREATE TABLE `B` (`Id` LONG NOT NULL PRIMARY KEY, `V` LONG)");
        engine.ExecuteNonQuery("INSERT INTO `A` (`Id`, `V`) VALUES (1, 10), (2, 20), (3, 30)");
        engine.ExecuteNonQuery("INSERT INTO `B` (`Id`, `V`) VALUES (1, 30), (2, 40)");
        return engine;
    }

    private static int[] Ids(QueryEngine e, string sql)
        => e.ExecuteQuery(sql).Rows.Select(r => Convert.ToInt32(r[0])).ToArray();

    [Fact]
    public void A_union_inside_IN()
        // A.V of 10/20/30 against the union {10,20,30} ∪ {30,40} — every row qualifies.
        => Assert.Equal([1, 2, 3], Ids(Seeded(),
            "SELECT `Id` FROM `A` WHERE `V` IN ("
            + "SELECT `V` FROM `A` UNION ALL SELECT `V` FROM `B`) ORDER BY `Id`"));

    [Fact]
    public void A_union_inside_IN_narrows_as_it_should()
        // Only the B arm supplies 40, and only A row 3 has 30 — so restricting the arms restricts the result.
        => Assert.Equal([3], Ids(Seeded(),
            "SELECT `Id` FROM `A` WHERE `V` IN ("
            + "SELECT `V` FROM `B` WHERE `V` = 30 UNION ALL SELECT `V` FROM `B` WHERE `V` = 40) ORDER BY `Id`"));

    [Fact]
    public void A_union_inside_NOT_IN()
        => Assert.Equal([1, 2], Ids(Seeded(),
            "SELECT `Id` FROM `A` WHERE `V` NOT IN ("
            + "SELECT `V` FROM `B` UNION ALL SELECT 99) ORDER BY `Id`"));

    [Fact]
    public void A_union_inside_EXISTS()
        => Assert.Equal([1, 2, 3], Ids(Seeded(),
            "SELECT `Id` FROM `A` WHERE EXISTS ("
            + "SELECT `V` FROM `B` UNION SELECT `V` FROM `A`) ORDER BY `Id`"));

    [Fact]
    public void A_correlated_union_inside_EXISTS()
        // The correlation reaches into an arm, so the union is re-evaluated per outer row: only A.V = 30 matches
        // a B row, and A row 3 is the only one.
        => Assert.Equal([3], Ids(Seeded(),
            "SELECT `Id` FROM `A` WHERE EXISTS ("
            + "SELECT `B`.`V` FROM `B` WHERE `B`.`V` = `A`.`V` UNION SELECT `B`.`V` FROM `B` WHERE 1 = 0) "
            + "ORDER BY `A`.`Id`"));

    [Fact]
    public void A_union_as_a_scalar_subquery()
        // UNION dedupes and the engine orders a set operation's output, so the first row is deterministic; TOP 1
        // makes the scalar single-valued as the standard requires.
        => Assert.Equal(10, Convert.ToInt32(Seeded().ExecuteQuery(
            "SELECT (SELECT TOP 1 `V` FROM (SELECT `V` FROM `A` UNION SELECT `V` FROM `B`) AS `u` ORDER BY `V`) AS `m`")
            .Rows.Single()[0]));

    [Fact]
    public void A_values_constructor_inside_IN()
        // The subquery position takes any query statement, not only a SELECT — the table value constructor EF
        // emits for an inline collection lands here too.
        => Assert.Equal([1, 3], Ids(Seeded(),
            "SELECT `Id` FROM `A` WHERE `V` IN (VALUES (10), (30)) ORDER BY `Id`"));

    [Fact]
    public void A_plain_select_subquery_still_works()
        // The counterpart: widening the position must not disturb the ordinary form, which is the one that
        // carries the decorrelation rewrites.
        => Assert.Equal([3], Ids(Seeded(),
            "SELECT `Id` FROM `A` WHERE `V` IN (SELECT `V` FROM `B` WHERE `V` = 30) ORDER BY `Id`"));
}
