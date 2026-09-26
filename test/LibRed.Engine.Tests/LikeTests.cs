using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// ANSI-92 LIKE wildcards, including the bracket char class [ ... ] / [! ... ]. EF escapes special chars by
// bracketing them (Contains("C#") -> LIKE '%C[#]%'), so [#] must match a literal '#', not the three characters "[#]".
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
    public void Hash_star_and_question_mark_are_plain_characters()
    {
        var e = Fresh("A5", "A#", "A*", "A?", "AB");
        Assert.Equal(["A#"], Match(e, "A#"));
        Assert.Equal(["A*"], Match(e, "A*"));
        Assert.Equal(["A?"], Match(e, "A?"));
    }

    // ACE's answers (LikeCollationProbeTests), the same in every collation: case folds by ACE's own table, which
    // leaves out what a runtime's casing adds; four letters are spelt out; nothing the collation folds besides.
    [Theory]
    [InlineData("é", "É", true)]
    [InlineData("σ", "Σ", true)]
    [InlineData("ж", "Ж", true)]
    [InlineData("ǆ", "Ǆ", true)]
    [InlineData("ÿ", "Ÿ", true)]
    [InlineData("ς", "Σ", false)]      // final sigma
    [InlineData("ς", "σ", false)]
    [InlineData("µ", "Μ", false)]      // micro sign against capital mu
    [InlineData("ſ", "S", false)]      // long s
    [InlineData("ǅ", "ǆ", false)]      // titlecase digraph
    [InlineData("ı", "I", false)]      // dotless i, in every collation — Turkish too
    [InlineData("i", "İ", false)]
    [InlineData("ß", "ss", true)]
    [InlineData("ss", "ß", true)]
    [InlineData("ß", "SS", true)]
    [InlineData("æ", "AE", true)]
    [InlineData("œ", "oe", true)]
    [InlineData("Œ", "OE", true)]
    [InlineData("œ", "OE", true)]
    [InlineData("þ", "th", true)]
    [InlineData("Þ", "TH", true)]
    [InlineData("ẞ", "ss", false)]     // capital sharp s is not spelt out
    [InlineData("ĳ", "ij", false)]
    [InlineData("ﬁ", "fi", false)]
    [InlineData("é", "e", false)]      // accents count
    [InlineData("Ａ", "A", false)]     // so does width, which the collation folds
    [InlineData("co-op", "coop", false)]
    [InlineData("a ", "a", false)]     // and trailing spaces, which '=' ignores
    [InlineData("Z", "[a-z]", true)]
    [InlineData("é", "[a-z]", false)]  // a range runs in character order, not the collation's
    [InlineData("_", "[A-z]", false)]
    public void Case_and_spelling_fold_as_ace_folds_them(string value, string pattern, bool matches)
        => Assert.Equal(matches ? [value] : [], Match(Fresh(value), pattern));

    [Fact]
    public void Bracket_class_and_negation_and_ranges()
    {
        var e = Fresh("cat", "bat", "hat", "rat");
        Assert.Equal(["cat", "bat"], Match(e, "[bc]at"));      // char list
        Assert.Equal(["hat", "rat"], Match(e, "[!bc]at"));     // negated list
        Assert.Equal(["cat", "bat"], Match(e, "[a-c]at"));     // range
    }
}
