using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// Access statistical aggregates StDev/StDevP/Var/VarP — verified byte-identical to ACE over the dataset
// {2,4,4,4,5,5,7,9} (mean 5). Sample forms divide by n-1 (NULL for a single value); population forms by n.
public class StatisticalAggregatesTests
{
    private sealed class SeededDatabase : IDisposable
    {
        private readonly TemporaryDatabase _temporary = TemporaryDatabase.CopyOf(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "stat-");

        public SeededDatabase()
        {
            Engine = new QueryEngine(_temporary.Open());
            Engine.ExecuteNonQuery("CREATE TABLE S ( K LONG PRIMARY KEY, V DOUBLE )");
            int k = 1;
            foreach (var v in new[] { 2, 4, 4, 4, 5, 5, 7, 9 })
                Engine.ExecuteNonQuery($"INSERT INTO S (K, V) VALUES ({k++}, {v})");
        }

        public QueryEngine Engine { get; }
        public void Dispose() => _temporary.Dispose();
    }

    private static object? Agg(QueryEngine e, string expr) => e.ExecuteQuery($"SELECT {expr} FROM S").Rows.Single()[0];

    [Theory]
    [InlineData("StDev(V)", 2.138089935299395)]   // sample stddev
    [InlineData("StDevP(V)", 2.0)]                // population stddev
    [InlineData("Var(V)", 4.571428571428571)]     // sample variance (32/7)
    [InlineData("VarP(V)", 4.0)]                  // population variance
    public void Statistical_aggregates_match_ace(string expr, double expected)
    {
        using var seeded = new SeededDatabase();
        Assert.Equal(expected, Convert.ToDouble(Agg(seeded.Engine, expr)), 10);
    }

    [Theory]
    [InlineData("StdDev(V)", 2.138089935299395)]   // "StdDev" alias of StDev (sample stddev)
    [InlineData("StdDevP(V)", 2.0)]                // "StdDevP" alias of StDevP
    public void Alias_spellings_work(string expr, double expected)
    {
        using var seeded = new SeededDatabase();
        Assert.Equal(expected, Convert.ToDouble(Agg(seeded.Engine, expr)), 10);
    }

    [Fact]
    public void Sample_forms_are_null_for_a_single_value()
    {
        using var seeded = new SeededDatabase();
        Assert.Null(seeded.Engine.ExecuteQuery("SELECT Var(V) FROM S WHERE K = 1").Rows.Single()[0]);
        Assert.Null(seeded.Engine.ExecuteQuery("SELECT StDev(V) FROM S WHERE K = 1").Rows.Single()[0]);
    }

    [Fact]
    public void Population_forms_are_zero_for_a_single_value()
    {
        using var seeded = new SeededDatabase();
        Assert.Equal(0.0, Convert.ToDouble(
            seeded.Engine.ExecuteQuery("SELECT VarP(V) FROM S WHERE K = 1").Rows.Single()[0]), 10);
    }

    [Fact]
    public void Grouped_statistical_aggregate()
    {
        using var seeded = new SeededDatabase();
        var rows = seeded.Engine.ExecuteQuery("SELECT V, StDevP(V) FROM S GROUP BY V ORDER BY V").Rows
            .Select(r => (Value: Convert.ToDouble(r[0]), Deviation: Convert.ToDouble(r[1]))).ToList();
        Assert.Equal([(2d, 0d), (4d, 0d), (5d, 0d), (7d, 0d), (9d, 0d)], rows);
    }
}
