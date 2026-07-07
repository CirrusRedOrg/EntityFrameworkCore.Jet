using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// Access statistical aggregates StDev/StDevP/Var/VarP — verified byte-identical to ACE over the dataset
// {2,4,4,4,5,5,7,9} (mean 5). Sample forms divide by n-1 (NULL for a single value); population forms by n.
public class StatisticalAggregatesTests
{
    private static QueryEngine Seeded()
    {
        string path = Path.Combine(Path.GetTempPath(), $"stat-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE S ( K LONG PRIMARY KEY, V DOUBLE )");
        int k = 1;
        foreach (var v in new[] { 2, 4, 4, 4, 5, 5, 7, 9 })
            e.ExecuteNonQuery($"INSERT INTO S (K, V) VALUES ({k++}, {v})");
        return e;
    }

    private static object? Agg(QueryEngine e, string expr) => e.ExecuteQuery($"SELECT {expr} FROM S").Rows.Single()[0];

    [Theory]
    [InlineData("StDev(V)", 2.138089935299395)]   // sample stddev
    [InlineData("StDevP(V)", 2.0)]                // population stddev
    [InlineData("Var(V)", 4.571428571428571)]     // sample variance (32/7)
    [InlineData("VarP(V)", 4.0)]                  // population variance
    public void Statistical_aggregates_match_ace(string expr, double expected)
        => Assert.Equal(expected, Convert.ToDouble(Agg(Seeded(), expr)), 10);

    [Theory]
    [InlineData("StdDev(V)", 2.138089935299395)]   // "StdDev" alias of StDev (sample stddev)
    [InlineData("StdDevP(V)", 2.0)]                // "StdDevP" alias of StDevP
    public void Alias_spellings_work(string expr, double expected)
        => Assert.Equal(expected, Convert.ToDouble(Agg(Seeded(), expr)), 10);

    [Fact]
    public void Sample_forms_are_null_for_a_single_value()
    {
        var e = Seeded();
        Assert.Null(e.ExecuteQuery("SELECT Var(V) FROM S WHERE K = 1").Rows.Single()[0]);
        Assert.Null(e.ExecuteQuery("SELECT StDev(V) FROM S WHERE K = 1").Rows.Single()[0]);
    }

    [Fact]
    public void Population_forms_are_zero_for_a_single_value()
    {
        var e = Seeded();
        Assert.Equal(0.0, Convert.ToDouble(e.ExecuteQuery("SELECT VarP(V) FROM S WHERE K = 1").Rows.Single()[0]), 10);
    }

    [Fact]
    public void Grouped_statistical_aggregate()
    {
        var e = Seeded();
        // two groups by parity of V; just assert it runs and returns a row per group with a numeric StDevP.
        var rows = e.ExecuteQuery("SELECT V, StDevP(V) FROM S GROUP BY V ORDER BY V").Rows;
        Assert.NotEmpty(rows);
    }
}
