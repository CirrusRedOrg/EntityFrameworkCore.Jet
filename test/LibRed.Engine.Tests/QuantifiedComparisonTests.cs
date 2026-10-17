using LibRed.Sql.Parsing;
using Xunit;

namespace LibRed.Engine.Tests;

// x op ANY | SOME | ALL (subquery). Each expectation is what ACE gave for the same statement over the same rows: X.V is
// 0, 5, 7, 12 and Null (Ids 1-5), compared with S1 = {5, 10}, S2 = {5, 10, Null}, S3 = {} and S4 = {Null}; T, F and N are
// True, False and Null. ANY and SOME are one quantifier.
public class QuantifiedComparisonTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        var engine = new QueryEngine(TemporaryDatabase.OpenTracked(
            TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "quantified-"), readOnly: false));
        engine.ExecuteNonQuery("CREATE TABLE X (Id LONG, V LONG)");
        engine.ExecuteNonQuery("INSERT INTO X VALUES (1, 0), (2, 5), (3, 7), (4, 12), (5, NULL)");
        foreach (string table in new[] { "S1", "S2", "S3", "S4" })
            engine.ExecuteNonQuery($"CREATE TABLE {table} (V LONG)");
        engine.ExecuteNonQuery("INSERT INTO S1 VALUES (5), (10)");
        engine.ExecuteNonQuery("INSERT INTO S2 VALUES (5), (10), (NULL)");
        engine.ExecuteNonQuery("INSERT INTO S4 VALUES (NULL)");
        return engine;
    }

    [Theory]
    [InlineData("= ANY", "FTFFN", "NTNNN", "FFFFF", "NNNNN")]
    [InlineData("= SOME", "FTFFN", "NTNNN", "FFFFF", "NNNNN")]
    [InlineData("= ALL", "FFFFN", "FFFFN", "TTTTT", "NNNNN")]
    [InlineData("<> ANY", "TTTTN", "TTTTN", "FFFFF", "NNNNN")]
    [InlineData("<> ALL", "TFTTN", "NFNNN", "TTTTT", "NNNNN")]
    [InlineData("< ANY", "TTTFN", "TTTNN", "FFFFF", "NNNNN")]
    [InlineData("< ALL", "TFFFN", "NFFFN", "TTTTT", "NNNNN")]
    [InlineData("<= SOME", "TTTFN", "TTTNN", "FFFFF", "NNNNN")]
    [InlineData("<= ALL", "TTFFN", "NNFFN", "TTTTT", "NNNNN")]
    [InlineData("> ANY", "FFTTN", "NNTTN", "FFFFF", "NNNNN")]
    [InlineData("> ALL", "FFFTN", "FFFNN", "TTTTT", "NNNNN")]
    [InlineData(">= ANY", "FTTTN", "NTTTN", "FFFFF", "NNNNN")]
    [InlineData(">= ALL", "FFFTN", "FFFNN", "TTTTT", "NNNNN")]
    public void A_quantified_comparison_is_true_false_or_null_as_ace_has_it(string comparison, string s1, string s2, string s3, string s4)
    {
        QueryEngine engine = Fresh();
        foreach ((string set, string expected) in new[] { ("S1", s1), ("S2", s2), ("S3", s3), ("S4", s4) })
        {
            string condition = $"V {comparison} (SELECT V FROM {set})";
            Assert.Equal(expected, string.Concat(engine.ExecuteQuery($"SELECT {condition} FROM X ORDER BY Id").Rows
                .Select(r => r[0] switch { true => 'T', false => 'F', null => 'N', var v => throw new InvalidOperationException($"{v}") })));

            // WHERE keeps the rows it is true for (verified vs ACE), and WHERE NOT those it is false for. ACE's NOT turns
            // x < ALL into x >= ALL, which is not the negation, for every comparison but =: LibRed keeps the logic.
            Assert.Equal(Ids(expected, 'T'), Ids(engine, $"SELECT Id FROM X WHERE {condition} ORDER BY Id"));
            Assert.Equal(Ids(expected, 'F'), Ids(engine, $"SELECT Id FROM X WHERE NOT ({condition}) ORDER BY Id"));
        }
    }

    [Theory]
    [InlineData("IN", "FTFFN", "NTNNN", "FFFFF", "NNNNN")]
    [InlineData("NOT IN", "TFTTN", "NFNNN", "TTTTT", "NNNNN")]
    public void In_is_equal_to_any_and_a_null_value_is_in_no_empty_subquery(string membership, string s1, string s2, string s3, string s4)
    {
        QueryEngine engine = Fresh();
        foreach ((string set, string expected) in new[] { ("S1", s1), ("S2", s2), ("S3", s3), ("S4", s4) })
        {
            string condition = $"V {membership} (SELECT V FROM {set})";
            Assert.Equal(Ids(expected, 'T'), Ids(engine, $"SELECT Id FROM X WHERE {condition} ORDER BY Id"));
            Assert.Equal(Ids(expected, 'F'), Ids(engine, $"SELECT Id FROM X WHERE NOT ({condition}) ORDER BY Id"));
        }
    }

    [Fact]
    public void A_quantified_comparison_correlates_and_takes_a_top()
    {
        QueryEngine engine = Fresh();
        Assert.Equal([4, 5], Ids(engine, "SELECT Id FROM X WHERE V > ALL (SELECT V FROM S1 WHERE S1.V <> X.V) ORDER BY Id"));
        Assert.Equal([3, 4], Ids(engine, "SELECT Id FROM X WHERE V > ANY (SELECT TOP 1 V FROM S1 ORDER BY V) ORDER BY Id"));
        Assert.Equal([1, 2, 3, 4, 5], Ids(engine, "SELECT Id FROM X WHERE 'a' < ANY (SELECT CompanyName FROM Shippers) ORDER BY Id"));
        Assert.Equal([1, 2, 3, 4, 5], Ids(engine, "SELECT Id FROM X WHERE (SELECT Max(V) FROM S1) > ANY (SELECT V FROM S1) ORDER BY Id"));
    }

    [Theory]
    [InlineData("SELECT Id FROM X WHERE V = ANY (5, 10)")]
    [InlineData("SELECT Id FROM X WHERE V > ANY SELECT V FROM S1")]
    [InlineData("SELECT Id FROM X WHERE V IN ANY (SELECT V FROM S1)")]
    [InlineData("CREATE TABLE K1 (Any LONG)")]
    [InlineData("CREATE TABLE K2 (Some LONG)")]
    [InlineData("SELECT 1 AS Any FROM X")]
    [InlineData("SELECT 1 AS Some FROM X")]
    public void What_ace_refuses_is_a_syntax_error(string sql) =>
        Assert.Throws<SqlParseException>(() => Fresh().ExecuteNonQuery(sql));

    [Fact]
    public void Any_and_some_name_a_column_bracketed_or_after_a_dot()
    {
        QueryEngine engine = Fresh();
        engine.ExecuteNonQuery("CREATE TABLE K3 ([Any] LONG, [Some] LONG)");
        engine.ExecuteNonQuery("INSERT INTO K3 VALUES (1, 2)");
        Assert.Equal([1, 2], engine.ExecuteQuery("SELECT [Any], [Some] FROM K3").Rows.Single().Select(v => Convert.ToInt32(v)));
        Assert.Equal([1, 2], engine.ExecuteQuery("SELECT K3.Any, K3.Some FROM K3").Rows.Single().Select(v => Convert.ToInt32(v)));
    }

    private static List<int> Ids(string values, char wanted) => [.. values.Select((v, i) => (v, i)).Where(x => x.v == wanted).Select(x => x.i + 1)];

    private static List<int> Ids(QueryEngine engine, string sql) => [.. engine.ExecuteQuery(sql).Rows.Select(r => Convert.ToInt32(r[0]))];
}
