using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// IIF/Choose/Switch are ordinary scalar functions — they work anywhere an expression is allowed: SELECT
// projections, WHERE, ORDER BY. Unlike in a DEFAULT (row-blind), here they can reference the row's own columns,
// because a query evaluates against a row scope. Same functions, different scope.
public class ConditionalsInSelectTests
{
    private static QueryEngine Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cis-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE T ( K LONG PRIMARY KEY, N LONG )");
        e.ExecuteNonQuery("INSERT INTO T (K, N) VALUES (1, 5)");
        e.ExecuteNonQuery("INSERT INTO T (K, N) VALUES (2, 15)");
        e.ExecuteNonQuery("INSERT INTO T (K, N) VALUES (3, 25)");
        return e;
    }

    private static string[] Col(QueryEngine e, string sql)
        => e.ExecuteQuery(sql).Rows.Select(r => r[0]?.ToString()).ToArray()!;

    [Fact]
    public void Iif_in_a_projection_reads_the_row_column()
        => Assert.Equal(new[] { "small", "big", "big" },
            Col(Fresh(), "SELECT IIF(N > 10, 'big', 'small') FROM T ORDER BY K"));

    [Fact]
    public void Choose_in_a_projection_reads_the_row_column()
        => Assert.Equal(new[] { "one", "two", "three" },
            Col(Fresh(), "SELECT Choose(K, 'one', 'two', 'three') FROM T ORDER BY K"));

    [Fact]
    public void Switch_in_a_projection_reads_the_row_column()
        => Assert.Equal(new[] { "lo", "mid", "hi" },
            Col(Fresh(), "SELECT Switch(N < 10, 'lo', N < 20, 'mid', True, 'hi') FROM T ORDER BY K"));

    [Fact]
    public void Iif_in_a_where_clause_filters()
        => Assert.Equal(new[] { "2", "3" },
            Col(Fresh(), "SELECT K FROM T WHERE IIF(N > 10, 1, 0) = 1 ORDER BY K"));

    [Fact]
    public void Switch_in_an_order_by_sorts()
        => Assert.Equal(new[] { "2", "3", "1" },
            Col(Fresh(), "SELECT K FROM T ORDER BY Switch(N < 10, 2, True, 1)"));
}
