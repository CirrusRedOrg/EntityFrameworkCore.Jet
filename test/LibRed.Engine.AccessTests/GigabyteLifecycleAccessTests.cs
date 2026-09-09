using System.Security.Cryptography;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// All mutations use LibRed. ACE is opened only for SELECT readback.
[Collection(AceCollection.Name)]
public class GigabyteLifecycleAccessTests(ITestOutputHelper output) : TempDatabaseTest
{
    [Fact(Explicit = true)]
    public void One_GiB_insert_and_update_are_rejected_without_changing_the_file()
    {
        string path = Create();
        byte[] oversized = new byte[1073741824];
        using (var db = JetDatabase.Open(path, readOnly: false))
        {
            var engine = new QueryEngine(db);
            Write(engine, "INSERT INTO Boundary VALUES (1, @p)", [1, 2, 3]);
            byte[] before = Hash(path);
            foreach (string sql in new[] { "INSERT INTO Boundary VALUES (2, @p)", "UPDATE Boundary SET Payload = @p WHERE Id = 1" })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => Write(engine, sql, oversized));
                Assert.Equal(before, Hash(path));
            }
        }
        VerifyAce(path, [1, 2, 3]);
        output.WriteLine("Exactly 1 GiB rejected for INSERT and UPDATE; complete file SHA-256 unchanged.");
    }

    [Fact(Explicit = true)]
    public void Maximum_binary_value_survives_rollback_replacement_deletion_and_reuse()
    {
        string path = Create();
        byte[] maximum = new byte[1073741823];
        new Random(4321).NextBytes(maximum);
        byte[] small = [7, 8, 9];
        using (var db = JetDatabase.Open(path, readOnly: false))
            Write(new QueryEngine(db), "INSERT INTO Boundary VALUES (1, @p)", small);

        // A maximum-size insert and a small-to-maximum update must both fully roll back.
        foreach (string sql in new[] { "INSERT INTO Boundary VALUES (2, @p)", "UPDATE Boundary SET Payload = @p WHERE Id = 1" })
        {
            byte[] before = Hash(path);
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.BeginTransaction();
                Write(new QueryEngine(db), sql, maximum);
                db.Rollback();
                Assert.Equal(before, Hash(path));
            }
            VerifyAce(path, small);
            output.WriteLine($"Rollback preserved complete file and ACE readback: {sql}");
        }

        Mutate(path, "UPDATE Boundary SET Payload = @p WHERE Id = 1", maximum);
        VerifyAce(path, maximum);
        VerifyLibRed(path, maximum);
        long highWater = new FileInfo(path).Length;
        output.WriteLine($"Maximum value verified through ACE and LibRed; fileBytes={highWater}");

        // Roll back reclamation too, preserving the full chain rather than just a small row.
        byte[] largeHash = Hash(path);
        using (var db = JetDatabase.Open(path, readOnly: false))
        {
            db.BeginTransaction();
            Write(new QueryEngine(db), "UPDATE Boundary SET Payload = @p WHERE Id = 1", small);
            db.Rollback();
            Assert.Equal(largeHash, Hash(path));
        }
        VerifyAce(path, maximum);

        Mutate(path, "UPDATE Boundary SET Payload = @p WHERE Id = 1", small);
        VerifyAce(path, small);
        Mutate(path, "UPDATE Boundary SET Payload = @p WHERE Id = 1", maximum);
        VerifyAce(path, maximum);
        Assert.InRange(new FileInfo(path).Length, highWater, highWater + 65536);
        output.WriteLine($"Replacement reused reclaimed pages; fileBytes={new FileInfo(path).Length}");

        using (var db = JetDatabase.Open(path, readOnly: false))
            Assert.Equal(1, new QueryEngine(db).ExecuteNonQuery("DELETE FROM Boundary WHERE Id = 1"));
        VerifyAce(path, null);
        Mutate(path, "INSERT INTO Boundary VALUES (1, @p)", maximum);
        VerifyAce(path, maximum);
        VerifyLibRed(path, maximum);
        // Allow small metadata growth, but never another payload's worth of allocation.
        Assert.InRange(new FileInfo(path).Length, highWater, highWater + 65536);
        output.WriteLine($"Deletion reused reclaimed pages; final fileBytes={new FileInfo(path).Length}");
    }

    private static string Create()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "libred-gib-lifecycle-");
        using var db = JetDatabase.Open(path, readOnly: false);
        new QueryEngine(db).ExecuteNonQuery("CREATE TABLE Boundary (Id LONG PRIMARY KEY, Payload LONGBINARY)");
        return path;
    }

    private static byte[] Hash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return SHA256.HashData(stream);
    }

    private static void Write(QueryEngine engine, string sql, byte[] payload) =>
        Assert.Equal(1, engine.ExecuteNonQuery(sql, new Dictionary<string, object?> { ["p"] = payload }));

    private static void Mutate(string path, string sql, byte[] payload)
    {
        using var db = JetDatabase.Open(path, readOnly: false);
        Write(new QueryEngine(db), sql, payload);
    }

    private static void VerifyLibRed(string path, byte[] expected)
    {
        using var db = JetDatabase.Open(path);
        var row = Assert.Single(new QueryEngine(db).ExecuteQuery("SELECT Id, Payload FROM Boundary").Rows);
        Assert.Equal(1, row[0]);
        Assert.True(expected.AsSpan().SequenceEqual(Assert.IsType<byte[]>(row[1])));
    }

    private static void VerifyAce(string path, byte[]? expected)
    {
        using var connection = AceTestDatabase.Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Payload FROM Boundary ORDER BY Id";
        using var reader = command.ExecuteReader();
        if (expected is not null)
        {
            Assert.True(reader.Read());
            Assert.Equal(1, reader.GetInt32(0));
            Assert.True(expected.AsSpan().SequenceEqual(Assert.IsType<byte[]>(reader.GetValue(1))));
        }
        Assert.False(reader.Read());
    }
}
