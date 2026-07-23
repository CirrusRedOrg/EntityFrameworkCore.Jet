using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// UNIQUE / PRIMARY index uniqueness is enforced on insert: a duplicate non-null key is rejected, but Jet
// treats NULLs as distinct so a unique index allows multiple nulls (both verified vs ACE). Dropping the
// index lifts the constraint.
public class UniqueIndexEnforcementTests
{
    private static QueryEngine Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"uq-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE T (Id long PRIMARY KEY, Code long)");
        e.ExecuteNonQuery("CREATE UNIQUE INDEX UX_Code ON T (Code)");
        return e;
    }

    [Fact]
    public void Duplicate_unique_value_is_rejected_but_nulls_are_allowed()
    {
        var e = Fresh();
        Assert.Equal(1, e.ExecuteNonQuery("INSERT INTO T (Id, Code) VALUES (1, 10)"));
        Assert.Throws<InvalidOperationException>(() => e.ExecuteNonQuery("INSERT INTO T (Id, Code) VALUES (2, 10)"));
        // NULLs are distinct — two are fine.
        Assert.Equal(1, e.ExecuteNonQuery("INSERT INTO T (Id, Code) VALUES (3, NULL)"));
        Assert.Equal(1, e.ExecuteNonQuery("INSERT INTO T (Id, Code) VALUES (4, NULL)"));
        // The row that violated uniqueness was not written.
        Assert.Equal(3, e.ExecuteQuery("SELECT Id FROM T").Rows.Count());
    }

    [Fact]
    public void Duplicate_primary_key_is_rejected()
    {
        var e = Fresh();
        Assert.Equal(1, e.ExecuteNonQuery("INSERT INTO T (Id, Code) VALUES (1, 100)"));
        Assert.Throws<InvalidOperationException>(() => e.ExecuteNonQuery("INSERT INTO T (Id, Code) VALUES (1, 200)"));
    }

    [Fact]
    public void Update_to_a_duplicate_unique_value_is_rejected()
    {
        var e = Fresh();
        e.ExecuteNonQuery("INSERT INTO T (Id, Code) VALUES (1, 10)");
        e.ExecuteNonQuery("INSERT INTO T (Id, Code) VALUES (2, 20)");

        // Updating row 2's Code to 10 collides with row 1 → rejected, row unchanged.
        Assert.Throws<InvalidOperationException>(() => e.ExecuteNonQuery("UPDATE T SET Code = 10 WHERE Id = 2"));
        Assert.Equal(20, Convert.ToInt32(e.ExecuteQuery("SELECT Code FROM T WHERE Id = 2").Rows.Single()[0]));

        // Updating a row's unique column to its OWN current value is fine (no self-collision), as is a free value.
        Assert.Equal(1, e.ExecuteNonQuery("UPDATE T SET Code = 20 WHERE Id = 2"));
        Assert.Equal(1, e.ExecuteNonQuery("UPDATE T SET Code = 30 WHERE Id = 2"));
        Assert.Equal(30, Convert.ToInt32(e.ExecuteQuery("SELECT Code FROM T WHERE Id = 2").Rows.Single()[0]));
    }

    // Creating a unique index over rows that are already duplicated must fail — and must fail cleanly. The
    // check runs before the TDEF is touched, so the rejected statement leaves no half-built index behind:
    // the table keeps working, and the same CREATE succeeds once the duplicate is gone.
    [Fact]
    public void Creating_a_unique_index_over_existing_duplicates_is_rejected_and_changes_nothing()
    {
        var e = Fresh();
        e.ExecuteNonQuery("CREATE TABLE U (Id long PRIMARY KEY, Code long)");
        e.ExecuteNonQuery("INSERT INTO U (Id, Code) VALUES (1, 10)");
        e.ExecuteNonQuery("INSERT INTO U (Id, Code) VALUES (2, 10)");
        e.ExecuteNonQuery("INSERT INTO U (Id, Code) VALUES (3, 20)");

        Assert.Throws<InvalidOperationException>(() => e.ExecuteNonQuery("CREATE UNIQUE INDEX UX_U ON U (Code)"));

        // No index was created, and nothing about the table moved.
        Assert.Throws<InvalidOperationException>(() => e.ExecuteNonQuery("DROP INDEX UX_U ON U"));
        Assert.Equal(3, e.ExecuteQuery("SELECT Id FROM U").Rows.Count());
        Assert.Equal(1, e.ExecuteNonQuery("INSERT INTO U (Id, Code) VALUES (4, 10)")); // still unconstrained

        // Once the duplicates go, the very same statement works and then enforces.
        e.ExecuteNonQuery("DELETE FROM U WHERE Id IN (2, 4)");
        e.ExecuteNonQuery("CREATE UNIQUE INDEX UX_U ON U (Code)");
        Assert.Throws<InvalidOperationException>(() => e.ExecuteNonQuery("INSERT INTO U (Id, Code) VALUES (5, 10)"));
    }

    // Nulls are exempt from the back-fill check too, just as they are on insert.
    [Fact]
    public void Creating_a_unique_index_over_existing_nulls_is_allowed()
    {
        var e = Fresh();
        e.ExecuteNonQuery("CREATE TABLE V (Id long PRIMARY KEY, Code long)");
        e.ExecuteNonQuery("INSERT INTO V (Id, Code) VALUES (1, NULL)");
        e.ExecuteNonQuery("INSERT INTO V (Id, Code) VALUES (2, NULL)");
        e.ExecuteNonQuery("INSERT INTO V (Id, Code) VALUES (3, 30)");

        e.ExecuteNonQuery("CREATE UNIQUE INDEX UX_V ON V (Code)");
        Assert.Equal(1, e.ExecuteNonQuery("INSERT INTO V (Id, Code) VALUES (4, NULL)"));
        Assert.Throws<InvalidOperationException>(() => e.ExecuteNonQuery("INSERT INTO V (Id, Code) VALUES (5, 30)"));
    }

    [Fact]
    public void Dropping_the_unique_index_lifts_enforcement()
    {
        var e = Fresh();
        e.ExecuteNonQuery("INSERT INTO T (Id, Code) VALUES (1, 10)");
        e.ExecuteNonQuery("DROP INDEX UX_Code ON T");
        Assert.Equal(1, e.ExecuteNonQuery("INSERT INTO T (Id, Code) VALUES (2, 10)")); // dup now allowed
    }
}
