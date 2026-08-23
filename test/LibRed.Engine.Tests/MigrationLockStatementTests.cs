using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// The statement EF Core's JetHistoryRepository uses to take the migrations lock. It is the reason
// INSERT ... SELECT mattered beyond completing Access's append family: without it this does not parse, and
// LibRed cannot run migrations at all.
//
//   INSERT INTO `__EFMigrationsLock` (`Id`, `Timestamp`)
//   SELECT 1, <timestamp> FROM (SELECT COUNT(*) FROM `#Dual`)
//   WHERE NOT EXISTS (SELECT * FROM `__EFMigrationsLock` WHERE `Id` = 1);
//
// It exercises several things at once: a multiple-record append with an explicit column list (so the
// positional rule, not the name-matching one), a DERIVED TABLE as the source, NOT EXISTS, backtick-quoted
// identifiers, and @@ROWCOUNT read afterwards.
//
// Deliberately here rather than in the migrations suite: those tests hang, and a hung test host keeps a
// handle on the .accdb that blocks the next run from deleting it. This asserts the same SQL works without
// going near them.
public class MigrationLockStatementTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "miglock-");
        return new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
    }

    private static QueryEngine WithLockTable()
    {
        QueryEngine engine = Fresh();
        // '#Dual' as LibRedConnection.CreateDualTable makes it on every provider-created database: one
        // Int32 primary-key column. The Northwind fixture is a plain file that never went through that
        // path, so the test creates it — the point is that the emitted SQL is what EF really emits.
        engine.ExecuteNonQuery("CREATE TABLE `#Dual` (`ID` LONG NOT NULL PRIMARY KEY)");
        engine.ExecuteNonQuery("CREATE TABLE `__EFMigrationsLock` (`Id` LONG PRIMARY KEY, `Timestamp` DATETIME)");
        return engine;
    }

    // Taking a free lock inserts the row and reports one row affected.
    [Fact]
    public void Acquires_the_lock_when_it_is_free()
    {
        QueryEngine engine = WithLockTable();

        int affected = engine.ExecuteNonQuery(
            "INSERT INTO `__EFMigrationsLock` (`Id`, `Timestamp`) " +
            "SELECT 1, #2024-01-02 03:04:05# FROM (SELECT COUNT(*) FROM `#Dual`) " +
            "WHERE NOT EXISTS (SELECT * FROM `__EFMigrationsLock` WHERE `Id` = 1)");

        Assert.Equal(1, affected);
        Assert.Equal(1, Convert.ToInt32(
            engine.ExecuteQuery("SELECT COUNT(*) FROM `__EFMigrationsLock`").Rows.Single()[0]));
    }

    // The point of the WHERE NOT EXISTS: a second attempt inserts nothing, which is how the caller learns
    // the lock is already held. @@ROWCOUNT reporting 0 is the signal, not an error.
    [Fact]
    public void Does_not_take_a_lock_that_is_already_held()
    {
        QueryEngine engine = WithLockTable();
        const string acquire =
            "INSERT INTO `__EFMigrationsLock` (`Id`, `Timestamp`) " +
            "SELECT 1, #2024-01-02 03:04:05# FROM (SELECT COUNT(*) FROM `#Dual`) " +
            "WHERE NOT EXISTS (SELECT * FROM `__EFMigrationsLock` WHERE `Id` = 1)";

        engine.ExecuteNonQuery(acquire);
        int second = engine.ExecuteNonQuery(acquire);

        Assert.Equal(0, second);
        Assert.Equal(1, Convert.ToInt32(
            engine.ExecuteQuery("SELECT COUNT(*) FROM `__EFMigrationsLock`").Rows.Single()[0]));
    }

    // @@ROWCOUNT after the append is how the repository reads the outcome, so it has to reflect the append
    // rather than the SELECT that fed it.
    [Fact]
    public void Publishes_rowcount_for_the_caller()
    {
        QueryEngine engine = WithLockTable();
        const string acquire =
            "INSERT INTO `__EFMigrationsLock` (`Id`, `Timestamp`) " +
            "SELECT 1, #2024-01-02 03:04:05# FROM (SELECT COUNT(*) FROM `#Dual`) " +
            "WHERE NOT EXISTS (SELECT * FROM `__EFMigrationsLock` WHERE `Id` = 1)";

        engine.ExecuteNonQuery(acquire);
        Assert.Equal(1, Convert.ToInt32(engine.ExecuteQuery("SELECT @@ROWCOUNT").Rows.Single()[0]));

        engine.ExecuteNonQuery(acquire);
        Assert.Equal(0, Convert.ToInt32(engine.ExecuteQuery("SELECT @@ROWCOUNT").Rows.Single()[0]));
    }

    // Releasing and retaking it, which is the whole lock cycle.
    [Fact]
    public void Releases_and_retakes()
    {
        QueryEngine engine = WithLockTable();
        const string acquire =
            "INSERT INTO `__EFMigrationsLock` (`Id`, `Timestamp`) " +
            "SELECT 1, #2024-01-02 03:04:05# FROM (SELECT COUNT(*) FROM `#Dual`) " +
            "WHERE NOT EXISTS (SELECT * FROM `__EFMigrationsLock` WHERE `Id` = 1)";

        engine.ExecuteNonQuery(acquire);
        engine.ExecuteNonQuery("DELETE FROM `__EFMigrationsLock`");
        int retaken = engine.ExecuteNonQuery(acquire);

        Assert.Equal(1, retaken);
    }
}
