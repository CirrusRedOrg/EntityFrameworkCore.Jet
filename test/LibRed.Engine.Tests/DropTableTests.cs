using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// DROP TABLE table through LibRed's engine.
public class DropTableTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "droptab-eng-");
        var e = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE T (Id long PRIMARY KEY, Name text(20))");
        e.ExecuteNonQuery("CREATE INDEX IX_Name ON T (Name)");
        e.ExecuteNonQuery("INSERT INTO T (Id, Name) VALUES (1, 'a')");
        e.ExecuteNonQuery("CREATE TABLE Other (Id long PRIMARY KEY)");
        e.ExecuteNonQuery("INSERT INTO Other (Id) VALUES (7)");
        return e;
    }

    [Fact]
    public void Drops_the_table_leaving_others_intact()
    {
        var e = Fresh();
        e.ExecuteNonQuery("DROP TABLE T");

        Assert.Null(e.Database.Catalog.FindTable("T"));                     // gone from the catalog
        Assert.Throws<LibRed.Sql.Binding.SqlBindException>(() => e.ExecuteQuery("SELECT * FROM T"));
        // A same-named table can be created again.
        Assert.Equal(0, e.ExecuteNonQuery("CREATE TABLE T (Id long PRIMARY KEY)"));
        // The other table is untouched.
        Assert.Equal(7, Convert.ToInt32(e.ExecuteQuery("SELECT Id FROM Other").Rows.Single()[0]));
    }

    [Fact]
    public void Dropping_a_missing_table_is_rejected()
    {
        var e = Fresh();
        Assert.Throws<InvalidOperationException>(() => e.ExecuteNonQuery("DROP TABLE Nope"));
    }

    [Fact]
    public void Dropping_a_referenced_parent_is_rejected_but_the_child_can_be_dropped()
    {
        var e = Fresh();
        e.ExecuteNonQuery("CREATE TABLE P (Id long PRIMARY KEY)");
        e.ExecuteNonQuery("CREATE TABLE C (Id long PRIMARY KEY, Pid long, " +
                          "CONSTRAINT FK FOREIGN KEY (Pid) REFERENCES P (Id))");
        // The parent is referenced by a surviving child → rejected.
        Assert.Contains("relationship", Assert.Throws<InvalidOperationException>(
            () => e.ExecuteNonQuery("DROP TABLE P")).Message);
        // The child (referencing) table drops directly — the relationship goes with it (ACE allows this).
        e.ExecuteNonQuery("DROP TABLE C");
        // With the child gone, the parent is no longer referenced and drops too.
        e.ExecuteNonQuery("DROP TABLE P");
    }
}
