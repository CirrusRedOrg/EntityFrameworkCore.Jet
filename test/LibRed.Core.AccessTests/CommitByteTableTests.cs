using System.Data.OleDb;
using Xunit;

namespace LibRed.Core.Tests;

// What the 2-byte user commit slots at 0xE00 actually hold (measured 2026-08-26 against
// Microsoft.ACE.OLEDB.16.0). The format docs had described them two different ways — page-00 §2.2 as
// "per-file last-commit states", page-05 as an undecoded "counter" — so this pins it down.
//
// It is a little-endian 16-bit COUNTER of the user's committed writes. The two bytes are one value: driving
// the low byte past 0xFF carries into the high byte, which a pair of independent bytes would not do.
//
// ONE connection for the whole sequence — ACE heap-corrupts under connection churn (0xC0000374) and takes the
// test process with it.
public class CommitByteTableTests
{
    private const int Slot1 = 0xE02;   // 0xE00 is slot 0 (exclusive mode); slot 1 is the first shared user

    // The counter on disk LAGS the last write by one: its final increment is not flushed until the next write
    // (or until the connection closes, which lands the pending one plus its own). So the count is taken
    // between two mid-burst samples, where both ends carry the same one-write lag and it cancels.
    //
    // 280 also carries the low byte past 0xFF from any starting value, which is the part that proves the two
    // bytes are one 16-bit value rather than two independent ones. Northwind's slot 1 starts around 0x02D2.
    private const int Lead = 10;
    private const int Measured = 280;

    [Fact]
    public void The_commit_slot_is_a_little_endian_counter_of_committed_writes()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "libred-commitbyte-");
        try
        {
            using var connection = AceTestDatabase.Open(path);
            Exec(connection, "CREATE TABLE CB (Id LONG, T TEXT(20))");

            // Reads do not touch it — only committed writes do.
            int beforeRead = Counter(path, Slot1);
            Scalar(connection, "SELECT COUNT(*) FROM Customers");
            Scalar(connection, "SELECT COUNT(*) FROM CB");
            Assert.Equal(beforeRead, Counter(path, Slot1));

            // One increment per committed write — DDL included; a CREATE INDEX measured in isolation costs
            // exactly one, like anything else. (The lag makes that easy to misread: in a mixed run a statement
            // can appear to move it by 0 or 2 while the sequence as a whole is exactly one per statement.)
            for (int i = 0; i < Lead; i++)
                Exec(connection, $"INSERT INTO CB (Id, T) VALUES ({i}, 'lead')");

            int before = Counter(path, Slot1);
            for (int i = 0; i < Measured; i++)
                Exec(connection, $"INSERT INTO CB (Id, T) VALUES ({Lead + i}, 'row')");

            Assert.Equal(before + Measured, Counter(path, Slot1));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Only_the_active_user_slot_moves()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "libred-commitbyte-slots-");
        try
        {
            byte[] before = Slots(path);

            using (var connection = AceTestDatabase.Open(path))
            {
                Exec(connection, "CREATE TABLE CB (Id LONG)");
                Exec(connection, "INSERT INTO CB (Id) VALUES (1)");
            }

            byte[] after = Slots(path);

            // Slot 0 is the exclusive-mode state and stays put for a shared open; slots 2.. stay at the
            // neutral 00 01 because no second user ever registered.
            Assert.Equal(before[0..2], after[0..2]);
            Assert.Equal(before[4..16], after[4..16]);
            Assert.All(Enumerable.Range(2, 6), i => Assert.Equal([0x00, 0x01], after[(i * 2)..(i * 2 + 2)]));

            // And the one that did move only went up.
            Assert.True(Counter(path, Slot1) > (before[2] | (before[3] << 8)));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Reopening carries the count; compacting starts a new one. The second half is what makes 256 meaningful:
    // it is not a magic "idle" constant but where a genuinely fresh file's counter begins.
    [Fact]
    public void Compacting_restarts_the_counter_but_reopening_carries_it()
    {
        object? engine = DaoEngine();
        if (engine is null) return;   // DAO absent - as the other ACE probes do

        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "libred-commitbyte-compact-");
        string compacted = path.Replace(".accdb", "-compacted.accdb");
        try
        {
            using (var connection = AceTestDatabase.Open(path))
            {
                Exec(connection, "CREATE TABLE CB (Id LONG)");
                for (int i = 0; i < 20; i++) Exec(connection, $"INSERT INTO CB (Id) VALUES ({i})");
            }
            int afterWrites = Counter(path, Slot1);
            Assert.True(afterWrites > 256, $"expected the fixture's counter to be well past a fresh 256, got {afterWrites}");

            // Reopening does not reset it.
            using (var connection = AceTestDatabase.Open(path))
                Exec(connection, "INSERT INTO CB (Id) VALUES (999)");
            Assert.True(Counter(path, Slot1) >= afterWrites);

            // Compacting writes a whole new file, whose slot starts at the idle value.
            File.Delete(compacted);
            Invoke(engine, "CompactDatabase", path, compacted);
            Assert.Equal(256, Counter(compacted, Slot1));
        }
        finally
        {
            TemporaryDatabase.Delete(path);
            try { File.Delete(compacted); } catch (Exception) { }
        }
    }

    private static object? DaoEngine()
    {
        foreach (int n in new[] { 170, 160, 150, 140, 130, 120 })
        {
            Type? type = Type.GetTypeFromProgID($"DAO.DBEngine.{n}");
            if (type is null) continue;
            try { return Activator.CreateInstance(type); } catch (Exception) { }
        }
        return null;
    }

    private static object? Invoke(object target, string member, params object?[] args) =>
        target.GetType().InvokeMember(member, System.Reflection.BindingFlags.InvokeMethod, null, target, args);

    /// <summary>The slot's 16-bit little-endian value, read while ACE still holds the file open.</summary>
    private static int Counter(string path, int offset)
    {
        byte[] pair = new byte[2];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(offset, SeekOrigin.Begin);
        stream.ReadExactly(pair);
        return pair[0] | (pair[1] << 8);
    }

    /// <summary>The first eight slots (0xE00..0xE0F).</summary>
    private static byte[] Slots(string path)
    {
        byte[] slots = new byte[16];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(0xE00, SeekOrigin.Begin);
        stream.ReadExactly(slots);
        return slots;
    }

    private static void Exec(OleDbConnection connection, string sql)
    {
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void Scalar(OleDbConnection connection, string sql)
    {
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteScalar();
    }
}
