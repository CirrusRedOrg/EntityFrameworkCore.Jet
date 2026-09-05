using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// Access Switch(cond-1, value-1, cond-2, value-2, …): returns the value of the first true condition, NULL if none
// match, and requires an even argument count. Semantics verified against ACE. Exercised via DEFAULT expressions.
public class SwitchFunctionTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "switch-");
        return new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
    }

    private static object? DefaultOf(string type, string def)
    {
        var e = Fresh();
        e.ExecuteNonQuery($"CREATE TABLE T ( K LONG PRIMARY KEY, V {type} DEFAULT {def} )");
        e.ExecuteNonQuery("INSERT INTO T (K) VALUES (1)");
        return e.ExecuteQuery("SELECT V FROM T").Rows.Single()[0];
    }

    [Theory]
    [InlineData("Switch(1=1, 10, 1=2, 20)", 10)]                     // first condition true
    [InlineData("Switch(1=2, 10, 1=1, 20)", 20)]                     // second condition true
    [InlineData("Switch(Now() > #1/1/2000#, 111, True, 222)", 111)] // first true wins (env-function condition)
    public void Switch_returns_the_first_true_conditions_value(string def, int expected)
        => Assert.Equal(expected, Convert.ToInt32(DefaultOf("LONG", def)));

    [Fact]
    public void Switch_returns_null_when_no_condition_matches()
        => Assert.Null(DefaultOf("LONG", "Switch(1=2, 10, 2=3, 20)"));

    [Fact]
    public void Switch_returns_string_values()
        => Assert.Equal("b", DefaultOf("TEXT(10)", "Switch(1=2, 'a', 1=1, 'b')"));

    [Fact]
    public void Switch_with_an_odd_argument_count_is_rejected()
    {
        var e = Fresh();
        e.ExecuteNonQuery("CREATE TABLE T ( K LONG PRIMARY KEY, V LONG DEFAULT Switch(1=1, 10, 1=2) )");
        var ex = Assert.Throws<InvalidOperationException>(() => e.ExecuteNonQuery("INSERT INTO T (K) VALUES (1)"));
        Assert.Contains("Wrong number of arguments", ex.Message);
    }
}
