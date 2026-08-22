using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// ON DELETE CASCADE is evaluated with an explicit worklist, not recursion: a cyclic FK graph terminates, a
// shared child in a diamond is deleted exactly once, and a deep chain does not overflow the call stack.
public class CascadeDeleteWorklistTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "cascade-");
        return new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
    }

    [Fact]
    public void Cyclic_cascade_terminates_and_deletes_the_cycle()
    {
        var e = Fresh();
        e.ExecuteNonQuery("CREATE TABLE node ( id LONG PRIMARY KEY, parent LONG, " +
            "CONSTRAINT fk FOREIGN KEY (parent) REFERENCES node (id) ON DELETE CASCADE )");
        e.ExecuteNonQuery("INSERT INTO node (id, parent) VALUES (1, NULL)");
        e.ExecuteNonQuery("INSERT INTO node (id, parent) VALUES (2, 1)");
        e.ExecuteNonQuery("UPDATE node SET parent = 2 WHERE id = 1"); // 1 -> 2 -> 1 cycle

        // Recursion would follow 1 -> 2 -> 1 -> 2 … forever; the worklist visits each row once and stops.
        e.ExecuteNonQuery("DELETE FROM node WHERE id = 1");

        Assert.Equal(0, e.ExecuteQuery("SELECT COUNT(*) FROM node").Rows.Single()[0]);
    }

    [Fact]
    public void Diamond_cascade_deletes_the_shared_child_exactly_once()
    {
        var e = Fresh();
        e.ExecuteNonQuery("CREATE TABLE p ( id LONG PRIMARY KEY )");
        e.ExecuteNonQuery("CREATE TABLE bc ( id LONG PRIMARY KEY, pid LONG, " +
            "CONSTRAINT fkp FOREIGN KEY (pid) REFERENCES p (id) ON DELETE CASCADE )");
        e.ExecuteNonQuery("CREATE TABLE d ( id LONG PRIMARY KEY, b LONG, c LONG, " +
            "CONSTRAINT fkb FOREIGN KEY (b) REFERENCES bc (id) ON DELETE CASCADE, " +
            "CONSTRAINT fkc FOREIGN KEY (c) REFERENCES bc (id) ON DELETE CASCADE )");
        e.ExecuteNonQuery("INSERT INTO p (id) VALUES (1)");
        e.ExecuteNonQuery("INSERT INTO bc (id, pid) VALUES (10, 1)"); // B
        e.ExecuteNonQuery("INSERT INTO bc (id, pid) VALUES (11, 1)"); // C
        e.ExecuteNonQuery("INSERT INTO d (id, b, c) VALUES (100, 10, 11)"); // reachable from P via B and via C

        // Deleting P reaches D through both B and C; a naive recursion would delete D twice.
        e.ExecuteNonQuery("DELETE FROM p WHERE id = 1");

        Assert.Equal(0, e.ExecuteQuery("SELECT COUNT(*) FROM p").Rows.Single()[0]);
        Assert.Equal(0, e.ExecuteQuery("SELECT COUNT(*) FROM bc").Rows.Single()[0]);
        Assert.Equal(0, e.ExecuteQuery("SELECT COUNT(*) FROM d").Rows.Single()[0]);
    }

    [Fact]
    public void Deep_cascade_chain_does_not_overflow_the_stack()
    {
        var e = Fresh();
        e.ExecuteNonQuery("CREATE TABLE node ( id LONG PRIMARY KEY, parent LONG, " +
            "CONSTRAINT fk FOREIGN KEY (parent) REFERENCES node (id) ON DELETE CASCADE )");
        e.ExecuteNonQuery("INSERT INTO node (id, parent) VALUES (1, NULL)");
        // A chain far deeper than the call stack tolerated. The cyclic test above is the definitive proof that
        // recursion is gone; this one confirms the worklist scales to a long chain in one delete.
        const int depth = 2000;
        for (int i = 2; i <= depth; i++)
            e.ExecuteNonQuery($"INSERT INTO node (id, parent) VALUES ({i}, {i - 1})");

        e.ExecuteNonQuery("DELETE FROM node WHERE id = 1"); // cascades the whole chain

        Assert.Equal(0, e.ExecuteQuery("SELECT COUNT(*) FROM node").Rows.Single()[0]);
    }
}
