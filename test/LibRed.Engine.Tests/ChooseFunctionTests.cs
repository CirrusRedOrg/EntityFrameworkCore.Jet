using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// Access Choose(index, choice-1, choice-2, …) — 1-based selection. Verified against ACE: out-of-range index → NULL,
// NULL index → error, any value type allowed. Exercised through DEFAULT expressions (LibRed's FROM-less SELECT is
// a separate limitation). Motivated by translating SQL Server CHOOSE/CONVERT to Jet/ACE-native Choose/CBool.
public class ChooseFunctionTests
{
    private static QueryEngine Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"choose-{Guid.NewGuid():N}.accdb");
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
    [InlineData("Choose(1, 0, 1, 2)", 0)]   // 1-based: first choice
    [InlineData("Choose(2, 0, 1, 2)", 1)]
    [InlineData("Choose(3, 0, 1, 2)", 2)]
    public void Choose_selects_the_one_based_choice(string def, int expected)
        => Assert.Equal(expected, Convert.ToInt32(DefaultOf("LONG", def)));

    [Theory]
    [InlineData("Choose(0, 0, 1, 2)")]   // below range
    [InlineData("Choose(5, 0, 1, 2)")]   // above range
    public void Choose_out_of_range_index_is_null(string def)
        => Assert.Null(DefaultOf("LONG", def));

    [Fact]
    public void Choose_works_with_string_choices()
        => Assert.Equal("b", DefaultOf("TEXT(10)", "Choose(2, 'a', 'b', 'c')"));

    // The SQL Server table's Jet/ACE-native translation:
    //   A bit DEFAULT (CHOOSE(1, 0, 1, 2))               -> A YESNO DEFAULT Choose(1, 0, 1, 2)
    //   B bit DEFAULT ((CONVERT([bit],(CHOOSE(1,0,1,2)))) -> B YESNO DEFAULT CBool(Choose(1, 0, 1, 2))
    // Both pick the 1st choice (0), so both default to False.
    [Fact]
    public void Translated_sqlserver_choose_convert_table_defaults_to_false()
    {
        var e = Fresh();
        e.ExecuteNonQuery(
            "CREATE TABLE MyTable ( Id LONG, A YESNO DEFAULT Choose(1, 0, 1, 2), " +
            "B YESNO DEFAULT CBool(Choose(1, 0, 1, 2)) )");
        e.ExecuteNonQuery("INSERT INTO MyTable (Id) VALUES (1)");
        var row = e.ExecuteQuery("SELECT A, B FROM MyTable").Rows.Single();
        Assert.Equal(false, row[0]);
        Assert.Equal(false, row[1]);
    }
}
