using System.Linq;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// The table value constructor used as a <em>query</em> rather than as an INSERT's VALUES clause. EF Core
/// emits this for an inline collection, as an operand of a set operation:
/// <code>
/// SELECT MAX(`v`.`Value`) FROM (SELECT CLNG(30) AS `Value` UNION ALL VALUES (`p`.`Int`)) AS `v`
/// </code>
/// Column names come from the leading query of the set operation, per SQL, so the constructor's own columns
/// stay unnamed — naming them would need the <c>AS t(a, b)</c> column alias list that derived tables do not
/// support yet.
/// </summary>
public class ValuesQueryTests : TempDatabaseTest
{
    private static QueryEngine Seeded()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "valuesq-");
        var engine = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        engine.ExecuteNonQuery("CREATE TABLE `VQ` (`Id` LONG NOT NULL PRIMARY KEY, `N` LONG)");
        engine.ExecuteNonQuery("INSERT INTO `VQ` (`Id`, `N`) VALUES (1, 10), (2, 40)");
        return engine;
    }

    private static int[] Ints(QueryEngine e, string sql)
        => e.ExecuteQuery(sql).Rows.Select(r => Convert.ToInt32(r[0])).ToArray();

    [Fact]
    public void Values_supplies_rows_to_a_union()
        => Assert.Equal([1, 2], Ints(Seeded(), "SELECT 1 AS `V` FROM `VQ` WHERE `Id` = 1 UNION ALL VALUES (2)"));

    [Fact]
    public void Multiple_rows_each_become_a_row()
        => Assert.Equal([1, 2, 3, 4], Ints(Seeded(),
            "SELECT 1 AS `V` FROM `VQ` WHERE `Id` = 1 UNION ALL VALUES (2), (3), (4)"));

    [Fact]
    public void Column_names_come_from_the_leading_query()
    {
        var result = Seeded().ExecuteQuery("SELECT 1 AS `Value` FROM `VQ` WHERE `Id` = 1 UNION ALL VALUES (2)");
        Assert.Equal("Value", result.ColumnNames[0]);
    }

    [Fact]
    public void Union_dedupes_across_a_values_operand()
        // UNION (not ALL) must still dedupe when one side is a constructor.
        => Assert.Equal([1], Ints(Seeded(), "SELECT 1 AS `V` FROM `VQ` WHERE `Id` = 1 UNION VALUES (1)"));

    [Fact]
    public void Aggregates_over_a_values_operand()
    {
        // The shape EF actually emits: the constructor feeds a derived table that an aggregate reads.
        var e = Seeded();
        Assert.Equal(30, Convert.ToInt32(e.ExecuteQuery(
            "SELECT MAX(`v`.`Value`) FROM (SELECT CLNG(30) AS `Value` FROM `VQ` WHERE `Id` = 1 UNION ALL VALUES (20)) AS `v`")
            .Rows.Single()[0]));
    }

    [Fact]
    public void Row_values_may_reference_outer_columns()
    {
        // The correlated case, and the reason the expressions are evaluated per outer row rather than once:
        // VQ has N of 10 and 40, so MAX(30, N) is 30 for the first row and 40 for the second.
        var e = Seeded();
        var got = e.ExecuteQuery(
            "SELECT (SELECT MAX(`v`.`Value`) FROM (SELECT CLNG(30) AS `Value` FROM `VQ` WHERE `Id` = 1 UNION ALL VALUES (`p`.`N`)) AS `v`) " +
            "FROM `VQ` AS `p` ORDER BY `p`.`Id`")
            .Rows.Select(r => Convert.ToInt32(r[0])).ToArray();

        Assert.Equal([30, 40], got);
    }

    [Fact]
    public void Row_values_may_be_expressions()
        => Assert.Equal([1, 7], Ints(Seeded(), "SELECT 1 AS `V` FROM `VQ` WHERE `Id` = 1 UNION ALL VALUES (3 + 4)"));

    [Fact]
    public void Ragged_rows_are_rejected()
        => Assert.ThrowsAny<Exception>(() => Seeded().ExecuteQuery(
            "SELECT 1 AS `A`, 2 AS `B` FROM `VQ` WHERE `Id` = 1 UNION ALL VALUES (3, 4), (5)"));

    [Fact]
    public void Default_is_rejected_in_a_values_query()
        // DEFAULT means "the target column's default", which only has a meaning in an INSERT — the standard
        // allows it nowhere else.
        => Assert.ThrowsAny<Exception>(() => Seeded().ExecuteQuery(
            "SELECT 1 AS `V` FROM `VQ` WHERE `Id` = 1 UNION ALL VALUES (DEFAULT)"));

    [Fact]
    public void Insert_values_still_accepts_default()
        // The counterpart to the above: the INSERT clause is unaffected by the query form rejecting it.
        => Seeded().ExecuteNonQuery("INSERT INTO `VQ` (`Id`, `N`) VALUES (3, DEFAULT)");
}
