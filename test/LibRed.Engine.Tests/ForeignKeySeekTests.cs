using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// Referential integrity finds a parent row, and a cascade its children, through an index on the key where
/// there is one, rather than scanning the table. A seek is only a faster way to the same rows: the key
/// comparison folds case and trailing spaces, so the index — whose keys fold them too — has to reach every row
/// the scan's comparison would have accepted. These hold it to that.
/// </summary>
public class ForeignKeySeekTests : TempDatabaseTest
{
    private static QueryEngine Seeded()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "fk-seek-");
        var e = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));

        e.ExecuteNonQuery("CREATE TABLE Par (Code TEXT(10) PRIMARY KEY, Nm TEXT(20))");
        e.ExecuteNonQuery("CREATE TABLE Ch (Id LONG PRIMARY KEY, Code TEXT(10), Info TEXT(20), " +
            "CONSTRAINT fk FOREIGN KEY (Code) REFERENCES Par (Code) ON UPDATE CASCADE ON DELETE CASCADE)");
        e.ExecuteNonQuery("BEGIN TRANSACTION");
        for (int i = 0; i < 50; i++)
            e.ExecuteNonQuery($"INSERT INTO Par (Code, Nm) VALUES ('P{i}', 'parent {i}')");
        e.ExecuteNonQuery("INSERT INTO Par (Code, Nm) VALUES ('ABC', 'mixed')");
        e.ExecuteNonQuery("COMMIT");
        return e;
    }

    private static int Count(QueryEngine e, string sql) => Convert.ToInt32(e.ExecuteQuery(sql).Rows.Single()[0]);

    [Fact]
    public void A_child_finds_its_parent_whatever_the_case_and_trailing_spaces_of_its_key()
    {
        QueryEngine e = Seeded();
        e.ExecuteNonQuery("INSERT INTO Ch (Id, Code, Info) VALUES (1, 'abc', 'lower')");
        e.ExecuteNonQuery("INSERT INTO Ch (Id, Code, Info) VALUES (2, 'Abc  ', 'padded')");
        e.ExecuteNonQuery("INSERT INTO Ch (Id, Code, Info) VALUES (3, 'P7', 'plain')");
        Assert.Equal(3, Count(e, "SELECT COUNT(*) FROM Ch"));
    }

    [Fact]
    public void A_child_with_no_parent_is_still_refused()
        => Assert.ThrowsAny<Exception>(() => Seeded().ExecuteNonQuery("INSERT INTO Ch (Id, Code, Info) VALUES (1, 'P99', 'x')"));

    [Fact]
    public void A_cascade_reaches_every_child_its_key_comparison_matches()
    {
        QueryEngine e = Seeded();
        e.ExecuteNonQuery("INSERT INTO Ch (Id, Code, Info) VALUES (1, 'abc', 'lower')");
        e.ExecuteNonQuery("INSERT INTO Ch (Id, Code, Info) VALUES (2, 'ABC ', 'padded')");
        e.ExecuteNonQuery("INSERT INTO Ch (Id, Code, Info) VALUES (3, 'P1', 'other')");

        e.ExecuteNonQuery("DELETE FROM Par WHERE Code = 'ABC'");
        Assert.Equal(["P1"], e.ExecuteQuery("SELECT Code FROM Ch").Rows.Select(r => (string)r[0]!));
    }

    [Fact]
    public void An_update_cascade_rewrites_the_children_it_finds_by_seek_from_all_their_values()
    {
        QueryEngine e = Seeded();
        e.ExecuteNonQuery("INSERT INTO Ch (Id, Code, Info) VALUES (1, 'P3', 'first')");
        e.ExecuteNonQuery("INSERT INTO Ch (Id, Code, Info) VALUES (2, 'p3', 'second')");

        e.ExecuteNonQuery("UPDATE Par SET Code = 'Q3' WHERE Code = 'P3'");
        Assert.Equal([("Q3", "first"), ("Q3", "second")],
            e.ExecuteQuery("SELECT Code, Info FROM Ch ORDER BY Id").Rows.Select(r => ((string)r[0]!, (string)r[1]!)));
    }
}
