using LibRed.Data;
using Xunit;

namespace LibRed.Ado.Tests;

// Concurrent behaviour of EF Core's migration lock on LibRed.
//
// Written while diagnosing why MigrationsInfrastructureLibRedTest hangs. The hang is real, and it is in
// JetHistoryRepository.AcquireDatabaseLock's unbounded `while (true)` retry — but these tests establish that
// the engine underneath it is NOT the cause: N connections contending for the lock all acquire and release
// cleanly, and a lock released by one connection is immediately takeable by another.
//
// They matter because that retry loop has no timeout: anything that stops the guarded INSERT reporting 1
// turns Migrate() into a permanent hang rather than a test failure. These pin the engine side of that
// contract so a future regression shows up here, bounded, instead of as a hung suite.
public class MigrationLockConcurrencyTests
{
    private const string Acquire = """
        INSERT INTO `__EFMigrationsLock` (`Id`, `Timestamp`)
        SELECT 1, '2024-01-02 03:04:05+00:00' FROM (SELECT COUNT(*) FROM `#Dual`)
        WHERE NOT EXISTS (SELECT * FROM `__EFMigrationsLock` WHERE `Id` = 1);
        SELECT @@ROWCOUNT;
        """;

    private const string Release = "DELETE FROM `__EFMigrationsLock`;";

    private static string NewStore(string tag)
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), tag);
        using LibRedConnection c = Open(path);
        Exec(c, "CREATE TABLE `#Dual` (`ID` LONG NOT NULL PRIMARY KEY)");
        Exec(c, "INSERT INTO `#Dual` (`ID`) VALUES (1)");
        Exec(c, """
            CREATE TABLE `__EFMigrationsLock` (
                `Id` INTEGER NOT NULL CONSTRAINT `PK___EFMigrationsLock` PRIMARY KEY,
                `Timestamp` TEXT NOT NULL
            );
            """);
        return path;
    }

    // The shape of Can_apply_second_migration_in_parallel: several connections racing for one lock. Every one
    // must eventually win — a thread that can never acquire is exactly what hangs Migrate() forever.
    [Fact]
    public void Every_contending_connection_eventually_acquires()
    {
        const int threads = 8, attempts = 40;
        string path = NewStore("conc-");

        var won = new bool[threads];
        var failure = new string?[threads];
        var start = new Barrier(threads);

        var workers = new Thread[threads];
        for (int t = 0; t < threads; t++)
        {
            int id = t;
            workers[t] = new Thread(() =>
            {
                try
                {
                    using LibRedConnection c = Open(path);
                    start.SignalAndWait();
                    for (int a = 0; a < attempts && !won[id]; a++)
                    {
                        if (Convert.ToInt32(Scalar(c, Acquire)) == 1)
                        {
                            won[id] = true;
                            Exec(c, Release);   // release for the next contender
                        }
                        else
                        {
                            Thread.Sleep(15);
                        }
                    }
                }
                catch (Exception ex) { failure[id] = $"{ex.GetType().Name}: {ex.Message}"; }
            });
            workers[t].Start();
        }

        foreach (Thread w in workers)
            Assert.True(w.Join(TimeSpan.FromMinutes(2)), "a contending thread never finished");

        Assert.All(failure, f => Assert.Null(f));
        Assert.All(won, w => Assert.True(w, "a connection never acquired the lock"));

        using LibRedConnection after = Open(path);
        Assert.Equal(0, Convert.ToInt32(Scalar(after, "SELECT COUNT(*) FROM `__EFMigrationsLock`")));
    }

    // A lock released by one connection must be visible as free to a connection that was ALREADY OPEN when
    // the release happened — a stale view here would spin the retry loop forever against an empty table.
    [Fact]
    public void A_release_is_visible_to_an_already_open_connection()
    {
        string path = NewStore("rel-");
        using LibRedConnection a = Open(path);
        using LibRedConnection b = Open(path);   // opened before any lock traffic

        Assert.Equal(1, Convert.ToInt32(Scalar(a, Acquire)));
        Assert.Equal(0, Convert.ToInt32(Scalar(b, Acquire)));   // b correctly sees a's lock

        Exec(a, Release);

        Assert.Equal(0, Convert.ToInt32(Scalar(b, "SELECT COUNT(*) FROM `__EFMigrationsLock`")));
        Assert.Equal(1, Convert.ToInt32(Scalar(b, Acquire)));   // and can now take it
    }

    private static LibRedConnection Open(string path)
    {
        var c = new LibRedConnection($"Data Source={path}");
        c.Open();
        return c;
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
}
