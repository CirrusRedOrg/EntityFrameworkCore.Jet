using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// In Jet/ACE a UNIQUE constraint IS a unique index, so DROP CONSTRAINT and DROP INDEX are interchangeable.
// LibRed's DROP CONSTRAINT now drops a same-named unique/PK index (falling through from the FK path), matching
// ACE — while still dropping FK relationships and rejecting an FK-backing index.
public class DropConstraintIndexTests
{
    private static QueryEngine Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"dci-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        return new QueryEngine(JetDatabase.Open(path, readOnly: false));
    }

    [Fact]
    public void Drop_constraint_removes_a_unique_index()
    {
        var e = Fresh();
        e.ExecuteNonQuery("CREATE TABLE T ( K LONG PRIMARY KEY, A LONG )");
        e.ExecuteNonQuery("CREATE UNIQUE INDEX ix_a ON T (A)");
        // dup rejected while the unique index exists
        e.ExecuteNonQuery("INSERT INTO T (K, A) VALUES (1, 10)");
        Assert.ThrowsAny<Exception>(() => e.ExecuteNonQuery("INSERT INTO T (K, A) VALUES (2, 10)"));

        e.ExecuteNonQuery("ALTER TABLE T DROP CONSTRAINT ix_a");   // DROP CONSTRAINT on an index
        // now the duplicate is allowed
        e.ExecuteNonQuery("INSERT INTO T (K, A) VALUES (3, 10)");
    }

    [Fact]
    public void Drop_constraint_removes_a_unique_constraint_added_via_alter()
    {
        var e = Fresh();
        e.ExecuteNonQuery("CREATE TABLE T ( K LONG PRIMARY KEY, A LONG )");
        e.ExecuteNonQuery("ALTER TABLE T ADD CONSTRAINT UQ_A UNIQUE (A)");
        e.ExecuteNonQuery("INSERT INTO T (K, A) VALUES (1, 10)");
        Assert.ThrowsAny<Exception>(() => e.ExecuteNonQuery("INSERT INTO T (K, A) VALUES (2, 10)"));

        e.ExecuteNonQuery("ALTER TABLE T DROP CONSTRAINT UQ_A");
        e.ExecuteNonQuery("INSERT INTO T (K, A) VALUES (3, 10)");  // duplicate now fine
    }

    [Fact]
    public void Drop_constraint_still_drops_a_foreign_key()
    {
        var e = Fresh();
        e.ExecuteNonQuery("CREATE TABLE P ( PID LONG PRIMARY KEY )");
        e.ExecuteNonQuery("CREATE TABLE C ( CID LONG PRIMARY KEY, PID LONG, CONSTRAINT FK_C FOREIGN KEY (PID) REFERENCES P (PID) )");
        e.ExecuteNonQuery("INSERT INTO P (PID) VALUES (1)");
        // orphan rejected while FK enforced
        Assert.ThrowsAny<Exception>(() => e.ExecuteNonQuery("INSERT INTO C (CID, PID) VALUES (10, 99)"));

        e.ExecuteNonQuery("ALTER TABLE C DROP CONSTRAINT FK_C");
        e.ExecuteNonQuery("INSERT INTO C (CID, PID) VALUES (11, 99)");  // orphan now allowed
    }

    [Fact]
    public void Drop_constraint_on_a_nonexistent_name_throws()
    {
        var e = Fresh();
        e.ExecuteNonQuery("CREATE TABLE T ( K LONG PRIMARY KEY )");
        Assert.ThrowsAny<Exception>(() => e.ExecuteNonQuery("ALTER TABLE T DROP CONSTRAINT Nope"));
    }
}
