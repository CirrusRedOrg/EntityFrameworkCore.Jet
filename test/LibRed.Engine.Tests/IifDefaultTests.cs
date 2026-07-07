using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// IIF(cond, truePart, falsePart) works as a DEFAULT expression — verified to match ACE (both branches, an
// environment-function condition, and string results).
public class IifDefaultTests
{
    private static QueryEngine Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"iif-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        return new QueryEngine(JetDatabase.Open(path, readOnly: false));
    }

    private static object? DefaultOf(string type, string def)
    {
        var e = Fresh();
        e.ExecuteNonQuery($"CREATE TABLE T ( K LONG PRIMARY KEY, V {type} DEFAULT {def} )");
        e.ExecuteNonQuery("INSERT INTO T (K) VALUES (1)");
        return e.ExecuteQuery("SELECT V FROM T").Rows.Single()[0];
    }

    [Theory]
    [InlineData("IIF(1=1, 10, 20)", 10)]                     // true branch
    [InlineData("IIF(1=2, 10, 20)", 20)]                     // false branch
    [InlineData("IIF(Now() > #1/1/2000#, 1, 0)", 1)]         // condition uses an environment function
    public void Iif_default_selects_the_matching_branch(string def, int expected)
        => Assert.Equal(expected, Convert.ToInt32(DefaultOf("LONG", def)));

    [Fact]
    public void Iif_default_returns_string_branches()
        => Assert.Equal("yes", DefaultOf("TEXT(10)", "IIF(2 > 1, 'yes', 'no')"));
}
