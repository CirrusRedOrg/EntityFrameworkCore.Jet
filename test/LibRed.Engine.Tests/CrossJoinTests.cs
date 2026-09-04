using System.Linq;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// Explicit <c>CROSS JOIN</c>. Access has no such keyword — a cartesian product is written there as
/// comma-separated sources in the FROM clause — but EF Core's base generator emits the explicit form, so
/// LibRed accepts both spellings for the same thing. They build the identical tree
/// (<c>JoinKind.Cross</c> with no condition), which is what these tests pin.
/// </summary>
public class CrossJoinTests : TempDatabaseTest
{
    private static QueryEngine Seeded()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "crossjoin-");
        var engine = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        engine.ExecuteNonQuery("CREATE TABLE `L` (`Id` LONG NOT NULL PRIMARY KEY, `S` TEXT(10))");
        engine.ExecuteNonQuery("CREATE TABLE `R` (`Id` LONG NOT NULL PRIMARY KEY, `T` TEXT(10))");
        engine.ExecuteNonQuery("INSERT INTO `L` (`Id`, `S`) VALUES (1, 'a'), (2, 'b'), (3, 'c')");
        engine.ExecuteNonQuery("INSERT INTO `R` (`Id`, `T`) VALUES (1, 'x'), (2, 'y')");
        return engine;
    }

    private static int Count(QueryEngine e, string sql)
        => Convert.ToInt32(e.ExecuteQuery(sql).Rows.Single()[0]);

    [Fact]
    public void Produces_the_cartesian_product()
        // 3 left rows x 2 right rows.
        => Assert.Equal(6, Count(Seeded(), "SELECT COUNT(*) FROM `L` CROSS JOIN `R`"));

    [Fact]
    public void Matches_the_comma_spelling_exactly()
    {
        QueryEngine e = Seeded();
        var viaKeyword = e.ExecuteQuery("SELECT `L`.`S`, `R`.`T` FROM `L` CROSS JOIN `R` ORDER BY `L`.`Id`, `R`.`Id`")
            .Rows.Select(r => $"{r[0]}{r[1]}").ToArray();
        var viaComma = e.ExecuteQuery("SELECT `L`.`S`, `R`.`T` FROM `L`, `R` ORDER BY `L`.`Id`, `R`.`Id`")
            .Rows.Select(r => $"{r[0]}{r[1]}").ToArray();

        Assert.Equal(viaComma, viaKeyword);
        Assert.Equal(["ax", "ay", "bx", "by", "cx", "cy"], viaKeyword);
    }

    [Fact]
    public void Takes_an_alias()
        => Assert.Equal(6, Count(Seeded(), "SELECT COUNT(*) FROM `L` AS `l` CROSS JOIN `R` AS `r`"));

    [Fact]
    public void Combines_with_a_where_clause()
        => Assert.Equal(2, Count(Seeded(),
            "SELECT COUNT(*) FROM `L` CROSS JOIN `R` WHERE `L`.`Id` = 1"));

    [Fact]
    public void Chains_with_a_conditional_join()
        // A CROSS JOIN in the middle of a join chain: every L paired with every R, then joined back to L on
        // its key, which selects one row per original pair.
        => Assert.Equal(6, Count(Seeded(),
            "SELECT COUNT(*) FROM `L` CROSS JOIN `R` INNER JOIN `L` AS `l2` ON `L`.`Id` = `l2`.`Id`"));

    [Fact]
    public void An_on_clause_is_rejected()
        // CROSS JOIN pairs every row with every row, so there is nothing to condition on — the grammar has no
        // shape for it and this is a parse error.
        => Assert.ThrowsAny<Exception>(() => Seeded().ExecuteQuery(
            "SELECT COUNT(*) FROM `L` CROSS JOIN `R` ON `L`.`Id` = `R`.`Id`"));

    [Theory]
    [InlineData("cross join")]
    [InlineData("Cross Join")]
    public void Keyword_is_case_insensitive(string spelling)
        => Assert.Equal(6, Count(Seeded(), $"SELECT COUNT(*) FROM `L` {spelling} `R`"));

    [Fact]
    public void A_conditional_join_still_requires_its_on()
        // The counterpart: adding the CROSS alternative must not make ON optional everywhere else.
        => Assert.ThrowsAny<Exception>(() => Seeded().ExecuteQuery(
            "SELECT COUNT(*) FROM `L` INNER JOIN `R`"));
}
