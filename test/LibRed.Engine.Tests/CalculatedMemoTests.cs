using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>A calculated column reads a memo column's text, on insert and on an update that leaves the memo
/// unchanged, not the long-value descriptor the row stores for it.</summary>
public class CalculatedMemoTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "calculated-memo-");
        var engine = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        engine.ExecuteNonQuery(
            "CREATE TABLE FullNameBlogs (Id COUNTER NOT NULL, FirstName LONGCHAR NULL, LastName LONGCHAR NULL, "
            + "FullName LONGCHAR AS (FirstName + ' ' + LastName), CONSTRAINT PK_FullNameBlogs PRIMARY KEY (Id))");
        return engine;
    }

    private static object? FullName(QueryEngine engine) =>
        engine.ExecuteQuery("SELECT FullName FROM FullNameBlogs").Rows.Single()[0];

    [Fact]
    public void An_insert_reads_the_memo_text()
    {
        QueryEngine engine = Fresh();
        engine.ExecuteNonQuery("INSERT INTO FullNameBlogs (FirstName, LastName) VALUES ('One', 'Unicorn')");
        Assert.Equal("One Unicorn", FullName(engine));
    }

    [Fact]
    public void An_update_reads_the_unchanged_memo_text()
    {
        QueryEngine engine = Fresh();
        engine.ExecuteNonQuery("INSERT INTO FullNameBlogs (FirstName, LastName) VALUES ('One', 'Unicorn')");
        engine.ExecuteNonQuery("UPDATE FullNameBlogs SET FirstName = 'Two'");
        Assert.Equal("Two Unicorn", FullName(engine));
    }

    [Fact]
    public void A_long_memo_is_read_from_its_page()
    {
        QueryEngine engine = Fresh();
        string longName = new('x', 300);
        engine.ExecuteNonQuery($"INSERT INTO FullNameBlogs (FirstName, LastName) VALUES ('One', '{longName}')");
        engine.ExecuteNonQuery("UPDATE FullNameBlogs SET FirstName = 'Two'");
        Assert.Equal("Two " + longName, FullName(engine));
    }

    // '+' propagates the Null, and a Null text result reads back as empty text, as ACE reads it (page-02e).
    [Fact]
    public void A_null_memo_propagates_through_plus()
    {
        QueryEngine engine = Fresh();
        engine.ExecuteNonQuery("INSERT INTO FullNameBlogs (FirstName) VALUES ('One')");
        Assert.Equal("", FullName(engine));
    }
}
