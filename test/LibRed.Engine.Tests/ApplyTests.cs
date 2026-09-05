using System.Linq;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// <c>CROSS APPLY</c> / <c>OUTER APPLY</c> — lateral joins. ACE has neither, and neither does Access SQL, so
/// this is a LibRed extension that exists because EF Core's base generator emits them. What separates an APPLY
/// from an ordinary join is that its right side is evaluated once per left row with that row in scope: the
/// right side may correlate to the left, which a plain join's right side may not (pinned by
/// <see cref="A_plain_join_cannot_see_the_left_side"/>). CROSS APPLY drops a left row whose right side came
/// back empty; OUTER APPLY keeps it, null-padded.
/// </summary>
public class ApplyTests : TempDatabaseTest
{
    private static QueryEngine Seeded()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "apply-");
        var engine = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        engine.ExecuteNonQuery("CREATE TABLE `L` (`Id` LONG NOT NULL PRIMARY KEY, `S` TEXT(10))");
        engine.ExecuteNonQuery("CREATE TABLE `R` (`Id` LONG NOT NULL PRIMARY KEY, `T` TEXT(10))");
        engine.ExecuteNonQuery("CREATE TABLE `E` (`Id` LONG NOT NULL PRIMARY KEY)");
        engine.ExecuteNonQuery("INSERT INTO `L` (`Id`, `S`) VALUES (1, 'a'), (2, 'b'), (3, 'c')");
        engine.ExecuteNonQuery("INSERT INTO `R` (`Id`, `T`) VALUES (1, 'x'), (2, 'y')");
        return engine;
    }

    private static int Count(QueryEngine e, string sql)
        => Convert.ToInt32(e.ExecuteQuery(sql).Rows.Single()[0]);

    private static string[] Pairs(QueryEngine e, string sql)
        => e.ExecuteQuery(sql).Rows.Select(r => $"{r[0]}{r[1] ?? "-"}").ToArray();

    [Fact]
    public void Cross_apply_correlates_the_right_side_to_each_left_row()
        // L 1 and 2 each find their R row; L 3 finds none and is dropped.
        => Assert.Equal(["ax", "by"], Pairs(Seeded(),
            "SELECT `L`.`S`, `r`.`T` FROM `L` CROSS APPLY (SELECT * FROM `R` WHERE `R`.`Id` = `L`.`Id`) AS `r` "
            + "ORDER BY `L`.`Id`"));

    [Fact]
    public void Outer_apply_keeps_a_left_row_whose_right_side_is_empty()
        // Same query, OUTER: L 3 survives with its right-side columns null.
        => Assert.Equal(["ax", "by", "c-"], Pairs(Seeded(),
            "SELECT `L`.`S`, `r`.`T` FROM `L` OUTER APPLY (SELECT * FROM `R` WHERE `R`.`Id` = `L`.`Id`) AS `r` "
            + "ORDER BY `L`.`Id`"));

    [Fact]
    public void The_padding_an_outer_apply_emits_is_null()
    {
        object?[] row = Seeded().ExecuteQuery(
            "SELECT `r`.`Id`, `r`.`T` FROM `L` OUTER APPLY (SELECT * FROM `R` WHERE `R`.`Id` = `L`.`Id`) AS `r` "
            + "WHERE `L`.`Id` = 3").Rows.Single();

        Assert.Null(row[0]);
        Assert.Null(row[1]);
    }

    [Fact]
    public void An_uncorrelated_right_side_is_a_cross_product()
        // Nothing forces the right side to correlate; when it doesn't, APPLY degenerates to CROSS JOIN.
        => Assert.Equal(6, Count(Seeded(),
            "SELECT COUNT(*) FROM `L` CROSS APPLY (SELECT * FROM `R`) AS `r`"));

    [Fact]
    public void The_right_side_is_re_evaluated_per_left_row()
        // The shape APPLY exists for: a TOP taken *within* each left row's own result rather than once over
        // the whole right side. R.Id <= L.Id ordered descending picks the greatest R at or below each L.
        => Assert.Equal(["ax", "by", "cy"], Pairs(Seeded(),
            "SELECT `L`.`S`, `r`.`T` FROM `L` CROSS APPLY "
            + "(SELECT TOP 1 `R`.`T` FROM `R` WHERE `R`.`Id` <= `L`.`Id` ORDER BY `R`.`Id` DESC) AS `r` "
            + "ORDER BY `L`.`Id`"));

    [Fact]
    public void An_aggregate_right_side_yields_one_row_per_left_row()
        // An ungrouped aggregate always returns a row, so even the left row that matches nothing keeps its
        // place — with a count of 0, not a null.
        => Assert.Equal(["a1", "b1", "c0"], Pairs(Seeded(),
            "SELECT `L`.`S`, `r`.`C` FROM `L` CROSS APPLY "
            + "(SELECT COUNT(*) AS `C` FROM `R` WHERE `R`.`Id` = `L`.`Id`) AS `r` ORDER BY `L`.`Id`"));

    [Fact]
    public void An_empty_left_side_yields_no_rows()
        // The joined schema has to be known before any left row is read, so the right side is probed for its
        // columns even when the left never produces one.
        => Assert.Empty(Seeded().ExecuteQuery(
            "SELECT `E`.`Id`, `r`.`T` FROM `E` CROSS APPLY (SELECT * FROM `R` WHERE `R`.`Id` = `E`.`Id`) AS `r`").Rows);

    [Fact]
    public void Chains_after_a_join()
        => Assert.Equal(2, Count(Seeded(),
            "SELECT COUNT(*) FROM `L` INNER JOIN `L` AS `l2` ON `L`.`Id` = `l2`.`Id` "
            + "CROSS APPLY (SELECT * FROM `R` WHERE `R`.`Id` = `l2`.`Id`) AS `r`"));

    [Fact]
    public void A_plain_join_cannot_see_the_left_side()
        // The counterpart that makes the point: the identical derived table as an ordinary join's right side
        // is not lateral, so `L`.`Id` resolves to nothing and the query fails.
        => Assert.ThrowsAny<Exception>(() => Seeded().ExecuteQuery(
            "SELECT COUNT(*) FROM `L` INNER JOIN (SELECT * FROM `R` WHERE `R`.`Id` = `L`.`Id`) AS `r` "
            + "ON `r`.`Id` = `L`.`Id`"));

    [Fact]
    public void An_on_clause_is_rejected()
        // APPLY carries no ON — the correlation inside the right side is the condition.
        => Assert.ThrowsAny<Exception>(() => Seeded().ExecuteQuery(
            "SELECT COUNT(*) FROM `L` CROSS APPLY (SELECT * FROM `R`) AS `r` ON `r`.`Id` = `L`.`Id`"));

    [Fact]
    public void A_named_table_right_side_is_accepted()
        // T-SQL allows any table source on the right, not only a derived table; an uncorrelated named one is
        // pointless but legal, and rejecting it would be a grammar quirk of ours.
        => Assert.Equal(6, Count(Seeded(), "SELECT COUNT(*) FROM `L` CROSS APPLY `R`"));

    [Theory]
    [InlineData("cross apply")]
    [InlineData("Cross Apply")]
    [InlineData("outer apply")]
    [InlineData("Outer Apply")]
    public void Keywords_are_case_insensitive(string spelling)
        => Assert.Equal(6, Count(Seeded(), $"SELECT COUNT(*) FROM `L` {spelling} (SELECT * FROM `R`) AS `r`"));
}
