using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// ALTER TABLE ... ALTER COLUMN ... NOT NULL / NULL through LibRed's engine. Sets (or clears) the column's
// Required LvProp property — the ALTER-side of what CREATE writes — and the engine enforces it on insert.
// EF emits this to "make a column required" (with a prior UPDATE to null-fill and a DEFAULT).
public class AlterColumnRequiredTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "altreq-eng-");
        var e = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE T (Id long PRIMARY KEY, V text(255))");   // V nullable
        return e;
    }

    [Fact]
    public void Make_column_required_then_nullable()
    {
        var e = Fresh();
        e.ExecuteNonQuery("INSERT INTO T (Id, V) VALUES (1, 'a')");

        // Make required: an insert omitting V is now rejected.
        e.ExecuteNonQuery("ALTER TABLE T ALTER COLUMN V text(255) NOT NULL");
        AssertRequiredViolation(e, "INSERT INTO T (Id) VALUES (2)");
        e.ExecuteNonQuery("INSERT INTO T (Id, V) VALUES (3, 'c')");   // supplying a value still works

        // Make nullable again: omitting V is accepted (LibRed clears Required, unlike ACE's DDL).
        e.ExecuteNonQuery("ALTER TABLE T ALTER COLUMN V text(255) NULL");
        e.ExecuteNonQuery("INSERT INTO T (Id) VALUES (4)");
        Assert.Null(e.ExecuteQuery("SELECT V FROM T WHERE Id = 4").Rows.Single()[0]);
    }

    [Fact]
    public void Make_required_with_default_matches_the_ef_migration_shape()
    {
        var e = Fresh();
        e.ExecuteNonQuery("INSERT INTO T (Id) VALUES (1)");   // V is null

        // The EF "make required" sequence: drop default, null-fill, retype NOT NULL DEFAULT ''.
        e.ExecuteNonQuery("ALTER TABLE T ALTER COLUMN V DROP DEFAULT");
        e.ExecuteNonQuery("UPDATE T SET V = '' WHERE V IS NULL");
        e.ExecuteNonQuery("ALTER TABLE T ALTER COLUMN V text(255) NOT NULL DEFAULT ''");

        Assert.Equal("", e.ExecuteQuery("SELECT V FROM T WHERE Id = 1").Rows.Single()[0]);   // null-filled row
        AssertRequiredViolation(e, "INSERT INTO T (Id, V) VALUES (2, NULL)"); // explicit NULL rejected
        e.ExecuteNonQuery("INSERT INTO T (Id) VALUES (3)");                                   // omit → default '' applies
        Assert.Equal("", e.ExecuteQuery("SELECT V FROM T WHERE Id = 3").Rows.Single()[0]);
    }

    private static void AssertRequiredViolation(QueryEngine engine, string sql)
    {
        var error = Assert.Throws<InvalidOperationException>(() => engine.ExecuteNonQuery(sql));
        Assert.Contains("T.V", error.Message);
        Assert.Contains("must enter a value", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
