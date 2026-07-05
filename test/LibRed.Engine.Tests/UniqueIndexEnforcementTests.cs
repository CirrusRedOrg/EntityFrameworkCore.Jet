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

    [Fact]
    public void Dropping_the_unique_index_lifts_enforcement()
    {
        var e = Fresh();
        e.ExecuteNonQuery("INSERT INTO T (Id, Code) VALUES (1, 10)");
        e.ExecuteNonQuery("DROP INDEX UX_Code ON T");
        Assert.Equal(1, e.ExecuteNonQuery("INSERT INTO T (Id, Code) VALUES (2, 10)")); // dup now allowed
    }
}
