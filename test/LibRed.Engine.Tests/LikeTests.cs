using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// Access/Jet LIKE wildcards, including the bracket char class [ ... ] / [! ... ] and the # digit wildcard.
// EF escapes literal special chars by bracketing them (Contains("C#") -> LIKE '%C[#]%'), so [#] must match
// a literal '#', not the three characters "[#]".
public class LikeTests : TempDatabaseTest
{
    private static QueryEngine Fresh(params string[] values)
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "like-");
        var e = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE T (Id long PRIMARY KEY, V text(50))");
        for (int i = 0; i < values.Length; i++)
            e.ExecuteNonQuery("INSERT INTO T (Id, V) VALUES (@id, @v)",
                new Dictionary<string, object?> { ["id"] = i + 1, ["v"] = values[i] });
        return e;
    }

    private static string[] Match(QueryEngine e, string pattern) =>
        e.ExecuteQuery("SELECT V FROM T WHERE V LIKE @p ORDER BY Id",
            new Dictionary<string, object?> { ["p"] = pattern }).Rows.Select(r => (string)r[0]!).ToArray();

    [Fact]
    public void Bracketed_hash_matches_a_literal_hash()
    {
        // The ConferencePlanner "C#" search: Contains("C#") -> LIKE '%C[#]%'.
        var e = Fresh("Intro to C#", "C++ basics", "C# advanced", "Csharp");
        Assert.Equal(["Intro to C#", "C# advanced"], Match(e, "%C[#]%"));
    }

    [Fact]
    public void Hash_is_a_digit_wildcard_when_not_bracketed()
    {
        var e = Fresh("A5", "AB", "A0", "A");
        Assert.Equal(["A5", "A0"], Match(e, "A#"));
    }

    [Fact]
    public void Bracket_class_and_negation_and_ranges()
    {
        var e = Fresh("cat", "bat", "hat", "rat");
        Assert.Equal(["cat", "bat"], Match(e, "[bc]at"));      // char list
        Assert.Equal(["hat", "rat"], Match(e, "[!bc]at"));     // negated list
        Assert.Equal(["cat", "bat"], Match(e, "[a-c]at"));     // range
    }
}
