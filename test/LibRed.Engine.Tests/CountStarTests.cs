using LibRed;
using LibRed.Engine;
using LibRed.Engine.Execution;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// A bare <c>SELECT COUNT(*)</c> counts its input lazily instead of materialising every row to take the length
/// of the list (<c>QueryExecutor.IsBareCountStar</c>). The answer is unchanged by construction — it is the same
/// sequence, counted — so what needs pinning is the <b>guard</b>: every shape that genuinely needs the rows
/// must still take the general path, because the shortcut discards them.
/// </summary>
public class CountStarTests : TempDatabaseTest
{
    private static QueryEngine Seeded()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "count-star-");
        var e = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));

        e.ExecuteNonQuery("CREATE TABLE T (Id LONG PRIMARY KEY, Grp LONG, V LONG)");
        e.ExecuteNonQuery("CREATE TABLE Empty (Id LONG PRIMARY KEY)");
        e.ExecuteNonQuery("BEGIN TRANSACTION");
        for (int i = 1; i <= 20; i++)
        {
            // V is null on every fourth row, so COUNT(V) and COUNT(*) must differ.
            string v = i % 4 == 0 ? "NULL" : $"{i % 3}";
            e.ExecuteNonQuery($"INSERT INTO T (Id, Grp, V) VALUES ({i}, {i % 5}, {v})");
        }

        e.ExecuteNonQuery("COMMIT");
        return e;
    }

    private static object?[] Row(QueryEngine e, string sql) => e.ExecuteQuery(sql).Rows.Single();
    private static int Scalar(QueryEngine e, string sql) => Convert.ToInt32(Row(e, sql)[0]);

    [Fact]
    public void Counts_every_row()
        => Assert.Equal(20, Scalar(Seeded(), "SELECT COUNT(*) FROM T"));

    [Fact]
    public void An_empty_table_counts_zero_and_still_returns_one_row()
    {
        QueryEngine e = Seeded();
        Assert.Equal(0, Scalar(e, "SELECT COUNT(*) FROM Empty"));
        Assert.Single(e.ExecuteQuery("SELECT COUNT(*) FROM Empty").Rows);
    }

    [Fact]
    public void A_where_narrows_the_count()
        // The filter is below the aggregate, so the shortcut counts the filtered sequence, not the table.
        => Assert.Equal(4, Scalar(Seeded(), "SELECT COUNT(*) FROM T WHERE Grp = 1"));

    [Fact]
    public void A_join_is_counted_over_the_joined_rows()
        => Assert.Equal(20, Scalar(Seeded(), "SELECT COUNT(*) FROM T AS a INNER JOIN T AS b ON a.Id = b.Id"));

    [Fact]
    public void The_count_is_an_int_and_keeps_its_alias()
    {
        QueryEngine e = Seeded();
        ResultSet result = e.ExecuteQuery("SELECT COUNT(*) AS N FROM T");
        Assert.Equal("N", result.ColumnNames.Single());
        Assert.IsType<int>(result.Rows.Single()[0]);
    }

    // --- shapes the guard must exclude, because they need the rows ---------------------------------------

    [Fact]
    public void Count_of_a_column_counts_non_null_values_not_rows()
        // Five of the twenty rows hold NULL in V.
        => Assert.Equal(15, Scalar(Seeded(), "SELECT COUNT(V) FROM T"));

    [Fact]
    public void Count_distinct_counts_distinct_values()
        => Assert.Equal(3, Scalar(Seeded(), "SELECT COUNT(DISTINCT V) FROM T"));

    [Fact]
    public void Group_by_still_partitions_the_rows()
    {
        var counts = Seeded().ExecuteQuery("SELECT Grp, COUNT(*) FROM T GROUP BY Grp").Rows
            .Select(r => (Grp: Convert.ToInt32(r[0]), N: Convert.ToInt32(r[1]))).ToArray();
        Assert.Equal(5, counts.Length);
        Assert.All(counts, c => Assert.Equal(4, c.N));
    }

    [Fact]
    public void A_having_over_the_count_still_filters_groups()
        => Assert.Empty(Seeded().ExecuteQuery("SELECT Grp, COUNT(*) FROM T GROUP BY Grp HAVING COUNT(*) > 4").Rows);

    [Fact]
    public void A_second_projection_item_takes_the_general_path()
    {
        // COUNT(*) beside another aggregate: the shortcut must not fire, or SUM would have no rows to add.
        object?[] row = Row(Seeded(), "SELECT COUNT(*), SUM(V) FROM T");
        Assert.Equal(20, Convert.ToInt32(row[0]));
        Assert.Equal(15, Convert.ToInt32(row[1])); // 15 non-null values of 0/1/2
    }

    [Fact]
    public void An_expression_over_the_count_takes_the_general_path()
        => Assert.Equal(21, Scalar(Seeded(), "SELECT COUNT(*) + 1 FROM T"));

    [Fact]
    public void An_order_by_over_the_count_still_works()
        => Assert.Equal(20, Scalar(Seeded(), "SELECT COUNT(*) FROM T ORDER BY COUNT(*)"));

    [Fact]
    public void A_count_star_in_a_subquery_still_counts_that_subquerys_rows()
        => Assert.Equal(4, Scalar(Seeded(),
            "SELECT COUNT(*) FROM (SELECT Id FROM T WHERE Grp = 1) AS d"));

    [Fact]
    public void A_correlated_count_star_is_evaluated_per_outer_row()
    {
        var counts = Seeded().ExecuteQuery(
            "SELECT t.Grp, (SELECT COUNT(*) FROM T AS i WHERE i.Grp = t.Grp) FROM T AS t WHERE t.Id <= 3").Rows
            .Select(r => Convert.ToInt32(r[1])).ToArray();
        Assert.Equal([4, 4, 4], counts);
    }
}
