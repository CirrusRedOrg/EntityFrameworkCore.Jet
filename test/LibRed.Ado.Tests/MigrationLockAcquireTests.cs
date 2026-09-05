using LibRed.Data;
using Xunit;

namespace LibRed.Ado.Tests;

// Reproduces EF Core's JetHistoryRepository.AcquireDatabaseLock at the ADO level, statement for statement,
// because that method is an UNBOUNDED retry loop:
//
//     while (true) {
//         insertCount = (int?)CreateInsertLockCommand(...).ExecuteScalar(...);
//         if ((int)insertCount! == 1) return dbLock;
//         Thread.Sleep(retryDelay);          // doubles, caps at 1 minute, never gives up
//     }
//
// There is no timeout, no cancellation and no iteration cap, so ANY condition that stops that scalar being 1
// turns Database.Migrate() into a permanent hang rather than a failure — which is why stopping the debugger
// leaves a live test host still holding the .accdb, and the next run then can't delete the file.
//
// These tests drive the same three statements with a BOUNDED loop, so a break in the sequence shows up as a
// plain assertion instead of a hang. They deliberately avoid EF and the migrations fixture entirely.
public class MigrationLockAcquireTests
{
    private const string LockTable = "__EFMigrationsLock";

    // The exact SQL the repository builds (JetHistoryRepository.CreateExistsSql / CreateLockTableCommand /
    // CreateInsertLockCommand), with only the timestamp literal pinned so the test is deterministic.
    private const string ExistsSql =
        $"SELECT * FROM `INFORMATION_SCHEMA.TABLES` WHERE `TABLE_NAME` = '{LockTable}';";

    private const string CreateLockTableSql = $"""
        CREATE TABLE `{LockTable}` (
            `Id` INTEGER NOT NULL CONSTRAINT `PK_{LockTable}` PRIMARY KEY,
            `Timestamp` TEXT NOT NULL
        );
        """;

    private const string InsertLockSql = $"""
        INSERT INTO `{LockTable}` (`Id`, `Timestamp`)
        SELECT 1, '2024-01-02 03:04:05+00:00' FROM (SELECT COUNT(*) FROM `#Dual`)
        WHERE NOT EXISTS (SELECT * FROM `{LockTable}` WHERE `Id` = 1);
        SELECT @@ROWCOUNT;
        """;

    private const string DeleteLockSql = $"DELETE FROM `{LockTable}`;";

    private static LibRedConnection OpenTemp()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "miglock-ado-");
        var conn = new LibRedConnection($"Data Source={path}");
        conn.Open();
        // '#Dual' as LibRedConnection.CreateDualTable makes it on a provider-created database; the Northwind
        // fixture is a plain file that never went through that path.
        Exec(conn, "CREATE TABLE `#Dual` (`ID` LONG NOT NULL PRIMARY KEY)");
        Exec(conn, "INSERT INTO `#Dual` (`ID`) VALUES (1)");
        return conn;
    }

    private static void Exec(LibRedConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static object? Scalar(LibRedConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    // InterpretExistsResult treats null AND DBNull as "not found"; anything else is "exists".
    private static bool Exists(object? value) => value is not null && value != DBNull.Value;

    [Fact]
    public void Exists_check_reports_absent_then_present()
    {
        using LibRedConnection c = OpenTemp();

        Assert.False(Exists(Scalar(c, ExistsSql)));
        Exec(c, CreateLockTableSql);
        Assert.True(Exists(Scalar(c, ExistsSql)));
    }

    // The whole point of the loop: 1 means "I took the lock", 0 means "someone else holds it".
    [Fact]
    public void Insert_lock_reports_one_then_zero()
    {
        using LibRedConnection c = OpenTemp();
        Exec(c, CreateLockTableSql);

        Assert.Equal(1, Convert.ToInt32(Scalar(c, InsertLockSql)));
        Assert.Equal(0, Convert.ToInt32(Scalar(c, InsertLockSql)));
    }

    // ExecuteScalar must read the trailing SELECT @@ROWCOUNT, not the INSERT — if the batch returned the
    // INSERT's (empty) result the cast (int?)null would throw, and if it returned 0 the loop would spin.
    [Fact]
    public void Insert_lock_scalar_is_never_null()
    {
        using LibRedConnection c = OpenTemp();
        Exec(c, CreateLockTableSql);

        object? first = Scalar(c, InsertLockSql);
        Assert.NotNull(first);
        Assert.NotEqual(DBNull.Value, first);
    }

    // Release then re-acquire, which is what LockReleaseBehavior.Explicit does between migrations.
    [Fact]
    public void Lock_can_be_released_and_retaken()
    {
        using LibRedConnection c = OpenTemp();
        Exec(c, CreateLockTableSql);

        Assert.Equal(1, Convert.ToInt32(Scalar(c, InsertLockSql)));
        Exec(c, DeleteLockSql);
        Assert.Equal(1, Convert.ToInt32(Scalar(c, InsertLockSql)));
    }

    // The full AcquireDatabaseLock sequence, bounded. If LibRed ever stops reporting 1 for a free lock this
    // fails here in a few iterations instead of hanging Migrate() forever.
    [Fact]
    public void Acquire_release_cycle_terminates()
    {
        using LibRedConnection c = OpenTemp();

        if (!Exists(Scalar(c, ExistsSql)))
            Exec(c, CreateLockTableSql);

        for (int cycle = 0; cycle < 5; cycle++)
        {
            int taken = 0;
            for (int attempt = 0; attempt < 10 && taken != 1; attempt++)
                taken = Convert.ToInt32(Scalar(c, InsertLockSql));

            Assert.True(taken == 1, $"cycle {cycle}: never acquired the lock within 10 attempts");
            Exec(c, DeleteLockSql);
        }
    }

    // A second connection must see the row the first one committed, else two migrators would both believe
    // they hold the lock. (Same file, separate LibRedConnection — what EF's per-context connections are.)
    [Fact]
    public void A_second_connection_sees_the_held_lock()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "miglock-ado2-");

        using var first = new LibRedConnection($"Data Source={path}");
        first.Open();
        Exec(first, "CREATE TABLE `#Dual` (`ID` LONG NOT NULL PRIMARY KEY)");
        Exec(first, "INSERT INTO `#Dual` (`ID`) VALUES (1)");
        Exec(first, CreateLockTableSql);
        Assert.Equal(1, Convert.ToInt32(Scalar(first, InsertLockSql)));

        using var second = new LibRedConnection($"Data Source={path}");
        second.Open();
        Assert.Equal(0, Convert.ToInt32(Scalar(second, InsertLockSql)));
    }
}
