using System.Linq;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// A comparison must answer the same whether or not the column happens to be indexed. An index can only be
/// searched in its own column's kind, so a seek keyed by a value of another kind either cannot be encoded at
/// all or would find a different set of rows than the comparison defines — <c>S = 1</c> on text compares as a
/// number (<see cref="ComparisonOperatorTests"/>), matching <c>' 1 '</c> and <c>'1.0'</c> as well as
/// <c>'1'</c>, which are nowhere near each other in a text index. Such a seek falls back to a scan; the
/// filter the seek was planned under re-checks every row regardless, so only the reading strategy changes.
/// </summary>
public class IndexSeekKindTests : TempDatabaseTest
{
    /// <summary>The same two rows in a table with an index on each column and one with none.</summary>
    private static QueryEngine Seeded()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "seek-kind-");
        var e = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        foreach (string table in new[] { "Plain", "Indexed" })
        {
            e.ExecuteNonQuery($"CREATE TABLE {table} (Id LONG, S TEXT(10), N LONG)");
            e.ExecuteNonQuery($"INSERT INTO {table} (Id, S, N) VALUES (1, 'abc', 10)");
            e.ExecuteNonQuery($"INSERT INTO {table} (Id, S, N) VALUES (2, 'def', 20)");
        }
        e.ExecuteNonQuery("CREATE INDEX IxS ON Indexed (S)");
        e.ExecuteNonQuery("CREATE INDEX IxN ON Indexed (N)");
        return e;
    }

    private static string Run(QueryEngine e, string sql, IReadOnlyDictionary<string, object?>? p = null)
    {
        try { return string.Join(",", e.ExecuteQuery(sql, p).Rows.Select(r => $"{r[0]}")); }
        catch (Exception ex) { return $"{ex.GetType().Name}: {ex.Message}"; }
    }

    [Theory]
    // A numeric literal against a text column: compares as a number, so 'abc' is a type mismatch. The indexed
    // table used to raise the storage layer's own cast error instead.
    [InlineData("SELECT `Id` FROM `{0}` WHERE `S` = 1")]
    // A text literal against a numeric column, the same the other way round.
    [InlineData("SELECT `Id` FROM `{0}` WHERE `N` = 'abc'")]
    // Same kinds, so the seek is used on the indexed table — the control that proves these cases are reached.
    [InlineData("SELECT `Id` FROM `{0}` WHERE `S` = 'abc'")]
    [InlineData("SELECT `Id` FROM `{0}` WHERE `N` = 10")]
    [InlineData("SELECT `Id` FROM `{0}` WHERE `S` = NULL")]
    public void An_index_does_not_change_the_answer(string sql)
    {
        var e = Seeded();
        Assert.Equal(
            Run(e, string.Format(null, sql, "Plain")),
            Run(e, string.Format(null, sql, "Indexed")));
    }

    [Theory]
    // A parameter against text takes the text's type (CompareAsKinds), so a numeric parameter is compared as
    // text — '1' against 'abc' is simply no match, which is what ACE answers too.
    [InlineData("SELECT `Id` FROM `{0}` WHERE `S` = @p", 1)]
    [InlineData("SELECT `Id` FROM `{0}` WHERE `S` = @p", "abc")]
    [InlineData("SELECT `Id` FROM `{0}` WHERE `N` = @p", "10")]
    [InlineData("SELECT `Id` FROM `{0}` WHERE `N` = @p", 10)]
    public void An_index_does_not_change_the_answer_for_a_parameter(string sql, object value)
    {
        var e = Seeded();
        var p = new Dictionary<string, object?> { ["p"] = value };
        Assert.Equal(
            Run(e, string.Format(null, sql, "Plain"), p),
            Run(e, string.Format(null, sql, "Indexed"), p));
    }

    [Fact]
    public void A_numeric_parameter_against_an_indexed_text_column_matches_the_text_it_reads_as()
    {
        var e = Seeded();
        e.ExecuteNonQuery("INSERT INTO `Indexed` (`Id`, `S`, `N`) VALUES (3, '1', 30)");
        Assert.Equal("3", Run(e, "SELECT `Id` FROM `Indexed` WHERE `S` = @p",
            new Dictionary<string, object?> { ["p"] = 1 }));
    }

    [Fact]
    public void A_cross_kind_join_key_does_not_crash_the_index_nested_loop()
    {
        var e = Seeded();
        // Indexed.S is text and Plain.N is a number, so the inner seek cannot be keyed by it; the join falls
        // back to scanning the inner table and its ON — kept whole — decides the rows.
        Assert.Equal(
            Run(e, "SELECT `p`.`Id` FROM `Plain` AS `p` INNER JOIN `Plain` AS `q` ON `q`.`S` = `p`.`N`"),
            Run(e, "SELECT `p`.`Id` FROM `Plain` AS `p` INNER JOIN `Indexed` AS `q` ON `q`.`S` = `p`.`N`"));
    }
}
