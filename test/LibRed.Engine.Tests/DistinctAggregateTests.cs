using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// The ANSI intra-aggregate DISTINCT (<c>COUNT(DISTINCT col)</c>, <c>SUM(DISTINCT col)</c>, …) aggregates over
/// the distinct set of the argument's VALUES — distinct on the column values, NOT distinct rows (see DISTINCTROW).
/// </summary>
public class DistinctAggregateTests
{
    private static QueryEngine Seeded()
    {
        string path = Path.Combine(Path.GetTempPath(), $"da-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE T (Id LONG PRIMARY KEY, Grp TEXT(5), V LONG)");
        // Group 'a': V = 10, 10, 20, 20, 20  → distinct {10,20}; all {10,10,20,20,20}
        int id = 0;
        foreach (int v in new[] { 10, 10, 20, 20, 20 }) e.ExecuteNonQuery($"INSERT INTO T (Id, Grp, V) VALUES ({id++}, 'a', {v})");
        return e;
    }

    private static object? Scalar(QueryEngine e, string sql) => e.ExecuteQuery(sql).Rows.First()[0];

    [Fact]
    public void Count_distinct_counts_distinct_values()
    {
        var e = Seeded();
        Assert.Equal(5, Convert.ToInt32(Scalar(e, "SELECT COUNT(V) FROM T")));           // all non-null values
        Assert.Equal(2, Convert.ToInt32(Scalar(e, "SELECT COUNT(DISTINCT V) FROM T")));  // {10, 20}
    }

    [Fact]
    public void Sum_and_avg_distinct_use_the_distinct_value_set()
    {
        var e = Seeded();
        Assert.Equal(80, Convert.ToInt32(Scalar(e, "SELECT SUM(V) FROM T")));            // 10+10+20+20+20
        Assert.Equal(30, Convert.ToInt32(Scalar(e, "SELECT SUM(DISTINCT V) FROM T")));   // 10+20
        Assert.Equal(15.0, Convert.ToDouble(Scalar(e, "SELECT AVG(DISTINCT V) FROM T"))); // (10+20)/2
    }

    [Fact]
    public void Distinct_in_grouped_aggregate_projection()
    {
        var e = Seeded();
        var row = e.ExecuteQuery("SELECT Grp, COUNT(DISTINCT V) AS C, SUM(DISTINCT V) AS S FROM T GROUP BY Grp").Rows.Single();
        Assert.Equal("a", row[0]);
        Assert.Equal(2, Convert.ToInt32(row[1]));
        Assert.Equal(30, Convert.ToInt32(row[2]));
    }
}
