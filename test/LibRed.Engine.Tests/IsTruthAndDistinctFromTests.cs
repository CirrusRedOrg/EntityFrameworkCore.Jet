using System.Linq;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// The standard's <c>IS [NOT] TRUE</c>, <c>IS [NOT] FALSE</c> and <c>IS [NOT] DISTINCT FROM</c>, which ACE rejects.
/// Each is never Null.
/// </summary>
public class IsTruthAndDistinctFromTests : TempDatabaseTest
{
    private static QueryEngine Seeded()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "is-truth-");
        var e = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE T (Id LONG, X LONG, Y LONG)");
        foreach (string row in new[] { "1, 1, 1", "2, 1, 2", "3, NULL, 1", "4, NULL, NULL", "5, 2, 2" })
            e.ExecuteNonQuery($"INSERT INTO T (Id, X, Y) VALUES ({row})");
        return e;
    }

    private static int[] Ids(QueryEngine e, string where) =>
        [.. e.ExecuteQuery($"SELECT Id FROM T WHERE {where}").Rows.Select(r => Convert.ToInt32(r[0])).Order()];

    // X = 1 is True, True, Null, Null, False over the five rows.
    [Theory]
    [InlineData("(X = 1) IS TRUE", new[] { 1, 2 })]
    [InlineData("(X = 1) IS NOT TRUE", new[] { 3, 4, 5 })]
    [InlineData("(X = 1) IS FALSE", new[] { 5 })]
    [InlineData("(X = 1) IS NOT FALSE", new[] { 1, 2, 3, 4 })]
    [InlineData("X IS DISTINCT FROM Y", new[] { 2, 3 })]
    [InlineData("X IS NOT DISTINCT FROM Y", new[] { 1, 4, 5 })]
    public void Truth_tests_and_distinct_from_treat_null_as_a_value(string where, int[] expected) =>
        Assert.Equal(expected, Ids(Seeded(), where));

    [Fact]
    public void They_are_never_null()
    {
        object?[] row = Seeded().ExecuteQuery(
            "SELECT (X = 1) IS TRUE, (X = 1) IS FALSE, X IS DISTINCT FROM Y FROM T WHERE Id = 4").Rows.Single();
        Assert.Equal<object?>([false, false, false], row);
    }
}
