using System.Linq;
using LibRed;
using LibRed.Engine;
using LibRed.Engine.Execution;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// A parameter takes the type of the column it is compared with, as ACE's does: a number against a text column is
/// compared as text, text against a number column is read as a number. Expected values are ACE's, measured with
/// the same data over OLE DB parameters.
/// </summary>
public class ParameterComparedWithColumnTests : TempDatabaseTest
{
    private static QueryEngine Seeded()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "param-cmp-");
        var e = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE T (Id LONG, S TEXT(50))");
        e.ExecuteNonQuery("INSERT INTO T (Id, S) VALUES (1, '')");
        e.ExecuteNonQuery("INSERT INTO T (Id, S) VALUES (2, 'abc')");
        e.ExecuteNonQuery("INSERT INTO T (Id, S) VALUES (3, '11')");

        // Text order and number order disagree over these.
        e.ExecuteNonQuery("CREATE TABLE W (Id LONG, S TEXT(50), N LONG)");
        foreach (string row in new[] { "1, '9', 9", "2, '10', 10", "3, '100', 100", "4, '011', 11", "5, '1.5', NULL", "6, '-1', NULL" })
            e.ExecuteNonQuery($"INSERT INTO W (Id, S, N) VALUES ({row})");
        return e;
    }

    private static int[] Ids(QueryEngine e, string sql, params object[] values)
    {
        var parameters = new Dictionary<string, object?>();
        for (int i = 0; i < values.Length; i++) parameters[$"@p{i}"] = values[i];
        return [.. e.ExecuteQuery(sql, parameters).Rows.Select(r => Convert.ToInt32(r[0])).Order()];
    }

    [Fact]
    public void A_number_parameter_against_a_text_column_holding_non_numbers_does_not_throw()
    {
        var e = Seeded();
        Assert.Equal([3], Ids(e, "SELECT Id FROM T WHERE S = @p0", 11));
        Assert.Equal([3], Ids(e, "SELECT Id FROM T WHERE S IN (@p0, @p1, @p2)", 11, 18, 19));
        Assert.Equal([1, 2], Ids(e, "SELECT Id FROM T WHERE S NOT IN (@p0, @p1, @p2)", 11, 18, 19));
    }

    [Fact]
    public void A_number_parameter_against_a_text_column_compares_as_text()
    {
        var e = Seeded();
        // As numbers '011' would be 11, and only 9 and 10 would lie between 9 and 10.
        Assert.Empty(Ids(e, "SELECT Id FROM W WHERE S IN (@p0, @p1)", 11, 99));
        Assert.Equal([1, 2, 3], Ids(e, "SELECT Id FROM W WHERE S BETWEEN @p0 AND @p1", 9, 10));
        Assert.Equal([5], Ids(e, "SELECT Id FROM W WHERE S = @p0", 1.5));
        Assert.Equal([6], Ids(e, "SELECT Id FROM W WHERE S = @p0", true));
    }

    // ACE refuses this query outright; LibRed reads the column as a number row by row instead, as SQL Server does.
    [Fact]
    public void A_number_literal_against_a_text_column_is_not_converted()
    {
        var e = Seeded();
        Assert.Equal([4], Ids(e, "SELECT Id FROM W WHERE S = 11"));
    }

    [Fact]
    public void A_text_parameter_against_a_number_column_is_read_as_a_number()
    {
        var e = Seeded();
        Assert.Equal([4], Ids(e, "SELECT Id FROM W WHERE N = @p0", "11"));
        Assert.Equal([1, 4], Ids(e, "SELECT Id FROM W WHERE N IN (@p0, @p1)", "11", "9"));
        Assert.Equal([1, 2], Ids(e, "SELECT Id FROM W WHERE N BETWEEN @p0 AND @p1", "9", "10"));
        Assert.Throws<InvalidCastException>(() => Ids(e, "SELECT Id FROM W WHERE N = @p0", "abc"));
    }
}
