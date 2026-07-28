using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// First/Last aggregates — the argument's value from the first/last row of the group in scan order, NOT
// null-filtered (verified vs ACE: First over a leading NULL row returns NULL).
public class FirstLastAggregatesTests
{
    private static QueryEngine Seeded()
    {
        string path = Path.Combine(Path.GetTempPath(), $"fl-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE F ( K LONG PRIMARY KEY, V TEXT(20) )");
        e.ExecuteNonQuery("INSERT INTO F (K, V) VALUES (1, NULL)");
        e.ExecuteNonQuery("INSERT INTO F (K, V) VALUES (2, 'beta')");
        e.ExecuteNonQuery("INSERT INTO F (K, V) VALUES (3, 'gamma')");
        e.ExecuteNonQuery("INSERT INTO F (K, V) VALUES (4, 'delta')");
        return e;
    }

    private static object? Agg(QueryEngine e, string expr) => e.ExecuteQuery($"SELECT {expr} FROM F").Rows.Single()[0];

    [Fact]
    public void First_returns_the_first_rows_value_including_null()
        => Assert.Null(Agg(Seeded(), "First(V)"));   // first row's V is NULL — not skipped

    [Fact]
    public void Last_returns_the_last_rows_value()
        => Assert.Equal("delta", Convert.ToString(Agg(Seeded(), "Last(V)")));

    [Theory]
    [InlineData("First(K)", 1)]
    [InlineData("Last(K)", 4)]
    public void First_last_over_key(string expr, int expected)
        => Assert.Equal(expected, Convert.ToInt32(Agg(Seeded(), expr)));

    [Fact]
    public void First_last_are_grouped()
    {
        var e = Seeded();
        var rows = e.ExecuteQuery("SELECT K, First(V), Last(V) FROM F GROUP BY K ORDER BY K").Rows;
        Assert.NotEmpty(rows);
    }
}
