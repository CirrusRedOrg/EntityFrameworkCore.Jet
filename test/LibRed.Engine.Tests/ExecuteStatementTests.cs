using System.Linq;
using LibRed;
using LibRed.Engine;
using LibRed.Engine.Execution;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// The Access <c>EXECUTE|EXEC procedure arg, …</c> statement: invokes a stored procedure/query by name,
/// binding positional argument values to its declared parameters. A stored SELECT returns rows; a stored
/// action query returns its rows-affected count.
/// </summary>
public class ExecuteStatementTests
{
    private static QueryEngine Seeded()
    {
        string path = Path.Combine(Path.GetTempPath(), $"exec-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE T (Id LONG PRIMARY KEY, Nm TEXT(50), Amt LONG)");
        foreach (var (id, nm, amt) in new[] { (1, "a", 10), (2, "b", 20), (3, "c", 30) })
            e.ExecuteNonQuery($"INSERT INTO T (Id, Nm, Amt) VALUES ({id}, '{nm}', {amt})");
        return e;
    }

    private static string[] Names(ResultSet r) => r.Rows.Select(row => (string)row[0]!).ToArray();

    [Fact]
    public void Execute_a_parameterized_select_procedure()
    {
        var e = Seeded();
        e.ExecuteNonQuery("CREATE PROCEDURE pByMin threshold LONG AS SELECT Nm FROM T WHERE Amt >= threshold");

        Assert.Equal(["b", "c"], Names(e.ExecuteQuery("EXECUTE pByMin 20")));
        Assert.Empty(Names(e.ExecuteQuery("EXECUTE pByMin 100")));
    }

    [Fact]
    public void Exec_is_the_accepted_short_form()
    {
        var e = Seeded();
        e.ExecuteNonQuery("CREATE PROCEDURE pByMin threshold LONG AS SELECT Nm FROM T WHERE Amt >= threshold");
        Assert.Equal(["a", "b", "c"], Names(e.ExecuteQuery("EXEC pByMin 0")));
    }

    [Fact]
    public void Execute_binds_a_caller_parameter_as_an_argument()
    {
        var e = Seeded();
        e.ExecuteNonQuery("CREATE PROCEDURE pByMin threshold LONG AS SELECT Nm FROM T WHERE Amt >= threshold");
        var r = e.ExecuteQuery("EXECUTE pByMin @p0", new Dictionary<string, object?> { ["@p0"] = 25 });
        Assert.Equal(["c"], Names(r));
    }

    [Fact]
    public void Execute_a_plain_view_with_no_parameters()
    {
        var e = Seeded();
        e.ExecuteNonQuery("CREATE VIEW vAll AS SELECT Nm FROM T");
        Assert.Equal(["a", "b", "c"], Names(e.ExecuteQuery("EXECUTE vAll")));
    }

    [Fact]
    public void Execute_an_action_query_returns_rows_affected()
    {
        var e = Seeded();
        e.ExecuteNonQuery("CREATE PROCEDURE pAdd AS INSERT INTO T (Id, Nm, Amt) VALUES (4, 'd', 40)");
        Assert.Equal(1, e.ExecuteNonQuery("EXEC pAdd"));
        Assert.Equal(4, Convert.ToInt32(e.ExecuteQuery("SELECT COUNT(*) FROM T").Rows.First()[0]));
    }

    [Fact]
    public void Execute_rejects_a_wrong_argument_count()
    {
        var e = Seeded();
        e.ExecuteNonQuery("CREATE PROCEDURE pByMin threshold LONG AS SELECT Nm FROM T WHERE Amt >= threshold");
        Assert.Throws<InvalidOperationException>(() => e.ExecuteQuery("EXECUTE pByMin"));
        Assert.Throws<InvalidOperationException>(() => e.ExecuteQuery("EXECUTE pByMin 1, 2"));
    }

    [Fact]
    public void Execute_an_unknown_procedure_throws()
        => Assert.Throws<InvalidOperationException>(() => Seeded().ExecuteQuery("EXECUTE nope 1"));
}
