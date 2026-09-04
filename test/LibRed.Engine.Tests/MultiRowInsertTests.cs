using System.Linq;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// The table value constructor in an INSERT — <c>VALUES (…), (…), (…)</c>. Access documents only a single row
/// after VALUES; the standard takes a comma-separated list of them and EF Core batches inserts that way, so
/// LibRed accepts the list as a superset of Access.
/// </summary>
public class MultiRowInsertTests : TempDatabaseTest
{
    private static QueryEngine Seeded()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "multirow-");
        var engine = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        engine.ExecuteNonQuery("CREATE TABLE `MR` (`Id` LONG NOT NULL PRIMARY KEY, `S` TEXT(20), `N` LONG)");
        return engine;
    }

    private static int[] Ids(QueryEngine e)
        => e.ExecuteQuery("SELECT `Id` FROM `MR` ORDER BY `Id`").Rows.Select(r => Convert.ToInt32(r[0])).ToArray();

    [Fact]
    public void Inserts_every_row_in_the_constructor()
    {
        QueryEngine e = Seeded();
        int affected = e.ExecuteNonQuery(
            "INSERT INTO `MR` (`Id`, `S`, `N`) VALUES (1, 'a', 10), (2, 'b', 20), (3, 'c', 30)");

        Assert.Equal(3, affected);
        Assert.Equal([1, 2, 3], Ids(e));
    }

    [Fact]
    public void Values_land_on_the_right_columns_for_every_row()
    {
        QueryEngine e = Seeded();
        e.ExecuteNonQuery("INSERT INTO `MR` (`Id`, `S`, `N`) VALUES (1, 'a', 10), (2, 'b', 20)");

        var rows = e.ExecuteQuery("SELECT `Id`, `S`, `N` FROM `MR` ORDER BY `Id`").Rows.ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal("a", rows[0][1]);
        Assert.Equal(10, Convert.ToInt32(rows[0][2]));
        Assert.Equal("b", rows[1][1]);
        Assert.Equal(20, Convert.ToInt32(rows[1][2]));
    }

    [Fact]
    public void A_single_row_still_works()
    {
        QueryEngine e = Seeded();
        Assert.Equal(1, e.ExecuteNonQuery("INSERT INTO `MR` (`Id`, `S`, `N`) VALUES (1, 'a', 10)"));
        Assert.Equal([1], Ids(e));
    }

    [Fact]
    public void Rowcount_reports_every_inserted_row()
    {
        QueryEngine e = Seeded();
        e.ExecuteNonQuery("INSERT INTO `MR` (`Id`, `S`, `N`) VALUES (1, 'a', 1), (2, 'b', 2), (3, 'c', 3), (4, 'd', 4)");
        Assert.Equal(4, Convert.ToInt32(e.ExecuteQuery("SELECT @@ROWCOUNT").Rows.Single()[0]));
    }

    [Fact]
    public void Without_a_column_list_the_rows_fill_every_column_in_order()
    {
        QueryEngine e = Seeded();
        e.ExecuteNonQuery("INSERT INTO `MR` VALUES (1, 'a', 10), (2, 'b', 20)");

        var rows = e.ExecuteQuery("SELECT `Id`, `S`, `N` FROM `MR` ORDER BY `Id`").Rows.ToList();
        Assert.Equal("a", rows[0][1]);
        Assert.Equal("b", rows[1][1]);
    }

    [Fact]
    public void NULL_is_allowed_as_a_row_value()
    {
        QueryEngine e = Seeded();
        e.ExecuteNonQuery("INSERT INTO `MR` (`Id`, `S`, `N`) VALUES (1, NULL, 10), (2, 'b', NULL)");

        var rows = e.ExecuteQuery("SELECT `S`, `N` FROM `MR` ORDER BY `Id`").Rows.ToList();
        Assert.Null(rows[0][0]);
        Assert.Null(rows[1][1]);
    }

    [Fact]
    public void Row_values_may_be_expressions_not_just_literals()
    {
        QueryEngine e = Seeded();
        e.ExecuteNonQuery("INSERT INTO `MR` (`Id`, `S`, `N`) VALUES (1, UCASE('a'), 5 + 5), (2, 'b', 3 * 7)");

        var rows = e.ExecuteQuery("SELECT `S`, `N` FROM `MR` ORDER BY `Id`").Rows.ToList();
        Assert.Equal("A", rows[0][0]);
        Assert.Equal(10, Convert.ToInt32(rows[0][1]));
        Assert.Equal(21, Convert.ToInt32(rows[1][1]));
    }

    [Fact]
    public void A_row_with_the_wrong_value_count_is_rejected()
        // "The number of values specified in each list must be the same and the values must be in the same
        // order as the columns" — the check is per row, so a ragged constructor fails rather than silently
        // shifting values.
        => Assert.ThrowsAny<Exception>(() => Seeded().ExecuteNonQuery(
            "INSERT INTO `MR` (`Id`, `S`, `N`) VALUES (1, 'a', 10), (2, 'b')"));

    [Fact]
    public void A_duplicate_key_within_one_constructor_is_rejected()
    {
        QueryEngine e = Seeded();
        Assert.ThrowsAny<Exception>(() => e.ExecuteNonQuery(
            "INSERT INTO `MR` (`Id`, `S`, `N`) VALUES (1, 'a', 1), (1, 'b', 2)"));
    }

    // DEFAULT as a row value. "Forces the Database Engine to insert the default value defined for a column.
    // If a default does not exist for the column and the column allows null values, NULL is inserted."

    private static QueryEngine WithDefaults()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "rowdefault-");
        var engine = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        engine.ExecuteNonQuery(
            "CREATE TABLE `RD` (`Id` LONG NOT NULL PRIMARY KEY, `S` TEXT(20) DEFAULT 'dflt', `N` LONG)");
        return engine;
    }

    [Fact]
    public void Default_takes_the_columns_declared_default()
    {
        QueryEngine e = WithDefaults();
        e.ExecuteNonQuery("INSERT INTO `RD` (`Id`, `S`, `N`) VALUES (1, DEFAULT, 10)");

        Assert.Equal("dflt", e.ExecuteQuery("SELECT `S` FROM `RD` WHERE `Id` = 1").Rows.Single()[0]);
    }

    [Fact]
    public void Default_on_a_column_without_one_stores_null()
    {
        QueryEngine e = WithDefaults();
        e.ExecuteNonQuery("INSERT INTO `RD` (`Id`, `S`, `N`) VALUES (1, 'x', DEFAULT)");

        Assert.Null(e.ExecuteQuery("SELECT `N` FROM `RD` WHERE `Id` = 1").Rows.Single()[0]);
    }

    [Fact]
    public void Default_is_distinct_from_an_explicit_null()
    {
        // The whole reason DEFAULT needs a marker rather than a value: NULL stores NULL even on a column that
        // has a default, while DEFAULT takes the default.
        QueryEngine e = WithDefaults();
        e.ExecuteNonQuery("INSERT INTO `RD` (`Id`, `S`, `N`) VALUES (1, DEFAULT, 1), (2, NULL, 2)");

        var got = e.ExecuteQuery("SELECT `S` FROM `RD` ORDER BY `Id`").Rows.Select(r => r[0]).ToArray();
        Assert.Equal("dflt", got[0]);
        Assert.Null(got[1]);
    }

    [Fact]
    public void Default_may_be_mixed_across_rows_of_one_constructor()
    {
        QueryEngine e = WithDefaults();
        e.ExecuteNonQuery("INSERT INTO `RD` (`Id`, `S`, `N`) VALUES (1, DEFAULT, 10), (2, 'given', 20), (3, DEFAULT, 30)");

        var got = e.ExecuteQuery("SELECT `S` FROM `RD` ORDER BY `Id`").Rows.Select(r => r[0]?.ToString() ?? "").ToArray();
        Assert.Equal(["dflt", "given", "dflt"], got);
    }

    [Fact]
    public void Default_on_a_required_column_without_one_is_rejected()
        // No default and NOT NULL leaves nothing to store, so the required-column check refuses it rather
        // than writing a NULL.
        => Assert.ThrowsAny<Exception>(() => WithDefaults().ExecuteNonQuery(
            "INSERT INTO `RD` (`Id`, `S`, `N`) VALUES (DEFAULT, 'x', 1)"));

    [Fact]
    public void Default_is_rejected_outside_an_insert()
        // The standard allows DEFAULT as a row value only in an INSERT; the grammar admits `rowValue` nowhere
        // else, so it is a parse error rather than a runtime one.
        => Assert.ThrowsAny<Exception>(() => WithDefaults().ExecuteQuery("SELECT DEFAULT"));

    [Fact]
    public void Many_rows_in_one_constructor()
    {
        // SQL Server caps INSERT ... VALUES at 1,000 rows; LibRed imposes no limit, so well past it works.
        QueryEngine e = Seeded();
        string values = string.Join(", ", Enumerable.Range(1, 1500).Select(i => $"({i}, 's{i}', {i})"));
        Assert.Equal(1500, e.ExecuteNonQuery($"INSERT INTO `MR` (`Id`, `S`, `N`) VALUES {values}"));
        Assert.Equal(1500, Convert.ToInt32(e.ExecuteQuery("SELECT COUNT(*) FROM `MR`").Rows.Single()[0]));
    }
}
