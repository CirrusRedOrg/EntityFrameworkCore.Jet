using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// Repro for the ComplexNavigations seed failure: a REQUIRED self-referencing FK (LevelOne.Inverse1Id ->
// LevelOne.Id). EF inserts parent rows before children, so immediate FK enforcement should pass. Probing
// which order/shape our RI enforcement wrongly rejects.
public class SelfRefInsertTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "selfref-");
        var db = TemporaryDatabase.OpenTracked(path, readOnly: false);
        var e = new QueryEngine(db);
        e.ExecuteNonQuery("CREATE TABLE L1 (Id long PRIMARY KEY, Pid long, " +
                          "CONSTRAINT FK_Self FOREIGN KEY (Pid) REFERENCES L1 (Id))");
        return e;
    }

    [Fact]
    public void Parent_before_child_is_accepted()
    {
        var e = Fresh();
        e.ExecuteNonQuery("INSERT INTO L1 (Id, Pid) VALUES (1, 1)");   // root points to itself
        Assert.Equal(1, e.ExecuteNonQuery("INSERT INTO L1 (Id, Pid) VALUES (2, 1)")); // child of 1
        Assert.Equal(2, e.ExecuteQuery("SELECT Id FROM L1").Rows.Count());
    }

    [Fact]
    public void Self_pointing_root_is_accepted()
    {
        var e = Fresh();
        // The root of a required self-ref must point at an existing row; EF/Access allow it to point at
        // itself. The row being inserted must count as a candidate parent for its own FK.
        Assert.Equal(1, e.ExecuteNonQuery("INSERT INTO L1 (Id, Pid) VALUES (1, 1)"));
    }

    [Fact]
    public void Child_referencing_missing_parent_is_rejected()
    {
        var e = Fresh();
        Assert.Throws<InvalidOperationException>(() =>
            e.ExecuteNonQuery("INSERT INTO L1 (Id, Pid) VALUES (2, 99)")); // 99 does not exist
    }
}
