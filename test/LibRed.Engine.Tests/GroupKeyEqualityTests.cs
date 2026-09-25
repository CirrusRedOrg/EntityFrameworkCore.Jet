using System.Linq;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// What makes two grouping keys one key. Access folds them on the same terms it compares values on, so text
/// folds case and trailing spaces and a number folds across its types — measured against ACE, which returns a
/// single group for each case here. A column alone never mixes numeric types, so the numeric fold only shows
/// up through an expression.
/// </summary>
public class GroupKeyEqualityTests : TempDatabaseTest
{
    private static QueryEngine Seeded()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "gke-");
        var e = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE GK (Id LONG PRIMARY KEY, L LONG, D DOUBLE, C CURRENCY, S TEXT(10))");
        e.ExecuteNonQuery("INSERT INTO GK (Id, L, D, C, S) VALUES (1, 1, 1.0, 1.0, 'abc')");
        e.ExecuteNonQuery("INSERT INTO GK (Id, L, D, C, S) VALUES (2, 2, 2.0, 2.0, 'ABC  ')");
        return e;
    }

    [Fact]
    public void An_integer_and_a_float_of_the_same_value_are_one_group()
    {
        var e = Seeded();
        // Row 1 yields LONG 1, row 2 yields DOUBLE 1.0 — different CLR types, one key.
        var rows = e.ExecuteQuery("SELECT IIF(`L`=1, 1, 1.0) AS `E`, COUNT(*) FROM `GK` GROUP BY IIF(`L`=1, 1, 1.0)").Rows;
        Assert.Equal(2, Convert.ToInt32(Assert.Single(rows)[1]));
    }

    [Fact]
    public void A_double_and_a_currency_of_the_same_value_are_one_group()
    {
        var e = Seeded();
        var rows = e.ExecuteQuery(
            "SELECT `V`, COUNT(*) FROM (SELECT `D` AS `V` FROM `GK` UNION ALL SELECT `C` FROM `GK`) AS `U` GROUP BY `V`").Rows;
        Assert.Equal([2, 2], rows.Select(r => Convert.ToInt32(r[1])).ToArray());
    }

    [Fact]
    public void Text_folds_case_and_trailing_spaces()
    {
        var e = Seeded();
        Assert.Equal(2, Convert.ToInt32(
            Assert.Single(e.ExecuteQuery("SELECT `S`, COUNT(*) FROM `GK` GROUP BY `S`").Rows)[1]));
    }

    [Fact]
    public void Distinct_folds_the_same_way()
    {
        var e = Seeded();
        // DISTINCT shares the grouping key, so the fold has to reach it too.
        Assert.Single(e.ExecuteQuery("SELECT DISTINCT IIF(`L`=1, 1, 1.0) AS `E` FROM `GK`").Rows);
    }
}
