using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// First/Last aggregates — the argument's value from the first/last row of the group in scan order, NOT
// null-filtered (verified vs ACE: First over a leading NULL row returns NULL).
public class FirstLastAggregatesTests
{
    private sealed class SeededDatabase : IDisposable
    {
        private readonly TemporaryDatabase _temporary = TemporaryDatabase.CopyOf(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "fl-");

        public SeededDatabase()
        {
            Engine = new QueryEngine(_temporary.Open());
            Engine.ExecuteNonQuery("CREATE TABLE F ( K LONG PRIMARY KEY, V TEXT(20) )");
            Engine.ExecuteNonQuery("INSERT INTO F (K, V) VALUES (1, NULL)");
            Engine.ExecuteNonQuery("INSERT INTO F (K, V) VALUES (2, 'beta')");
            Engine.ExecuteNonQuery("INSERT INTO F (K, V) VALUES (3, 'gamma')");
            Engine.ExecuteNonQuery("INSERT INTO F (K, V) VALUES (4, 'delta')");
        }

        public QueryEngine Engine { get; }
        public void Dispose() => _temporary.Dispose();
    }

    private static object? Agg(QueryEngine e, string expr) => e.ExecuteQuery($"SELECT {expr} FROM F").Rows.Single()[0];

    [Fact]
    public void First_returns_the_first_rows_value_including_null()
    {
        using var seeded = new SeededDatabase();
        Assert.Null(Agg(seeded.Engine, "First(V)"));   // first row's V is NULL — not skipped
    }

    [Fact]
    public void Last_returns_the_last_rows_value()
    {
        using var seeded = new SeededDatabase();
        Assert.Equal("delta", Convert.ToString(Agg(seeded.Engine, "Last(V)")));
    }

    [Theory]
    [InlineData("First(K)", 1)]
    [InlineData("Last(K)", 4)]
    public void First_last_over_key(string expr, int expected)
    {
        using var seeded = new SeededDatabase();
        Assert.Equal(expected, Convert.ToInt32(Agg(seeded.Engine, expr)));
    }

    [Fact]
    public void First_last_are_grouped()
    {
        using var seeded = new SeededDatabase();
        var rows = seeded.Engine.ExecuteQuery("SELECT K, First(V), Last(V) FROM F GROUP BY K ORDER BY K").Rows
            .Select(r => (Key: Convert.ToInt32(r[0]), First: r[1] as string, Last: r[2] as string)).ToList();
        Assert.Equal(
            [(1, null, null), (2, "beta", "beta"), (3, "gamma", "gamma"), (4, "delta", "delta")],
            rows);
    }
}
