using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// ALTER TABLE ... DROP COLUMN through LibRed's engine: a metadata-only TDEF edit. Existing rows are not
// rewritten, so the surviving columns must still read back correctly.
public class DropColumnTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "dropcol-eng-");
        var e = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE T (Id long PRIMARY KEY, A long, B text(20), C text(20))");
        e.ExecuteNonQuery("INSERT INTO T (Id, A, B, C) VALUES (1, 10, 'bee', 'see')");
        e.ExecuteNonQuery("INSERT INTO T (Id, A, B, C) VALUES (2, 20, 'buzz', 'sizz')");
        return e;
    }

    [Fact]
    public void Drops_a_variable_column_and_survivors_still_read()
    {
        var e = Fresh();
        e.ExecuteNonQuery("ALTER TABLE T DROP COLUMN B");   // middle variable column

        var rows = e.ExecuteQuery("SELECT Id, A, C FROM T ORDER BY Id").Rows
            .Select(r => (Convert.ToInt32(r[0]), Convert.ToInt32(r[1]), (string)r[2]!)).ToArray();
        Assert.Equal([(1, 10, "see"), (2, 20, "sizz")], rows);           // C still decodes correctly
        Assert.Equal(["Id", "A", "C"], e.ExecuteQuery("SELECT * FROM T").ColumnNames); // B is gone from the catalog
    }

    [Fact]
    public void Drops_a_fixed_column()
    {
        var e = Fresh();
        e.ExecuteNonQuery("ALTER TABLE T DROP COLUMN A");   // fixed column

        var rows = e.ExecuteQuery("SELECT Id, B, C FROM T ORDER BY Id").Rows
            .Select(r => (Convert.ToInt32(r[0]), (string)r[1]!, (string)r[2]!)).ToArray();
        Assert.Equal([(1, "bee", "see"), (2, "buzz", "sizz")], rows);
    }

    [Fact]
    public void Dropping_a_keyed_or_missing_column_is_rejected()
    {
        var e = Fresh();
        // ACE rejects dropping an indexed/keyed column (drop the index first) — we mirror that.
        var ex = Assert.Throws<InvalidOperationException>(() => e.ExecuteNonQuery("ALTER TABLE T DROP COLUMN Id"));
        Assert.Contains("index", ex.Message);
        Assert.Throws<InvalidOperationException>(() => e.ExecuteNonQuery("ALTER TABLE T DROP COLUMN Nope")); // no such column
    }

    [Fact]
    public void Dropping_a_column_in_a_relationship_is_rejected()
    {
        var e = Fresh();
        e.ExecuteNonQuery("CREATE TABLE Ch (Id long PRIMARY KEY, Tid long, " +
                          "CONSTRAINT FK FOREIGN KEY (Tid) REFERENCES T (Id))");
        // ACE rejects dropping either end of a relationship: "part of one or more relationships".
        Assert.Contains("relationship", Assert.Throws<InvalidOperationException>(
            () => e.ExecuteNonQuery("ALTER TABLE Ch DROP COLUMN Tid")).Message);  // child FK column
        Assert.Contains("relationship", Assert.Throws<InvalidOperationException>(
            () => e.ExecuteNonQuery("ALTER TABLE T DROP COLUMN Id")).Message);    // referenced parent key
    }
}
