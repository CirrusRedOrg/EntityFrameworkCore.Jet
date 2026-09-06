using System.Data.OleDb;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

[Collection(AceCollection.Name)]
public class AllocationGrowthAccessTests(ITestOutputHelper output) : TempDatabaseTest
{
    [Fact(Explicit = true)]
    public void ACE_reads_a_LibRed_written_value_at_the_measured_one_GiB_boundary()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "libred-gigabyte-");
        byte[] payload = new byte[1073741823];
        new Random(23).NextBytes(payload);
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        using (var database = JetDatabase.Open(path, readOnly: false))
        {
            var engine = new QueryEngine(database);
            engine.ExecuteNonQuery("CREATE TABLE GrowthInterop (Id LONG PRIMARY KEY, Payload LONGBINARY)");
            Assert.Equal(1, engine.ExecuteNonQuery("INSERT INTO GrowthInterop (Id, Payload) VALUES (1, @payload)",
                new Dictionary<string, object?> { ["payload"] = payload }));
        }
        output.WriteLine($"LibRed wrote {payload.Length} bytes in {elapsed.Elapsed}; fileBytes={new FileInfo(path).Length}");
        Verify(path, payload, 1);
        output.WriteLine($"ACE reopened and verified every byte; total elapsed={elapsed.Elapsed}");
    }

    [Theory]
    [InlineData(16777215)]
    [InlineData(16777216)]
    [InlineData(16777217)]
    [InlineData(134217728)]
    public void Large_value_lengths_survive_ACE_readback_and_LibRed_replacement(int length)
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "libred-value-length-");
        byte[] payload = new byte[length];
        new Random(23).NextBytes(payload);
        using (var database = JetDatabase.Open(path, readOnly: false))
        {
            var engine = new QueryEngine(database);
            engine.ExecuteNonQuery("CREATE TABLE GrowthInterop (Id LONG PRIMARY KEY, Payload LONGBINARY)");
            engine.ExecuteNonQuery("INSERT INTO GrowthInterop (Id, Payload) VALUES (1, @payload)",
                new Dictionary<string, object?> { ["payload"] = payload });
        }
        Verify(path, payload, 1);
        using (var database = JetDatabase.Open(path, readOnly: false))
        {
            var engine = new QueryEngine(database);
            byte[] actual = Assert.IsType<byte[]>(Assert.Single(engine.ExecuteQuery("SELECT Payload FROM GrowthInterop").Rows)[0]);
            Assert.True(payload.AsSpan().SequenceEqual(actual));
            engine.ExecuteNonQuery("UPDATE GrowthInterop SET Payload = @payload", new Dictionary<string, object?> { ["payload"] = new byte[] { 4, 5, 6 } });
        }
        Verify(path, [4, 5, 6], 1);
    }

    [Fact(Explicit = true)]
    public void Database_full_failure_preserves_prior_values_for_ACE()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "libred-full-");
        byte[] payload = new byte[1048576];
        new Random(20260906).NextBytes(payload);
        int count = 0;
        using (var database = JetDatabase.Open(path, readOnly: false))
        {
            var engine = new QueryEngine(database);
            engine.ExecuteNonQuery("CREATE TABLE GrowthInterop (Id LONG PRIMARY KEY, Payload LONGBINARY)");
            for (int id = 1; id <= 2100; id++)
            {
                long before = new FileInfo(path).Length;
                try
                {
                    engine.ExecuteNonQuery("INSERT INTO GrowthInterop (Id, Payload) VALUES (@id, @payload)",
                        new Dictionary<string, object?> { ["id"] = id, ["payload"] = payload });
                    count++;
                }
                catch (InvalidOperationException exception)
                {
                    Assert.Contains("2 GiB", exception.Message);
                    Assert.Equal(before, new FileInfo(path).Length);
                    output.WriteLine($"Rejected id={id}; preserved fileBytes={before}");
                    break;
                }
            }
        }
        Assert.InRange(count, 1, 2099);
        Verify(path, payload, count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(136)]
    public void ACE_can_append_scan_and_update_after_LibRed_grows_the_file(int count)
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "allocation-growth-");
        byte[] payload = new byte[1048576];
        new Random(20260906).NextBytes(payload);
        using (var database = JetDatabase.Open(path, readOnly: false))
        {
            var engine = new QueryEngine(database);
            engine.ExecuteNonQuery("CREATE TABLE GrowthInterop (Id LONG PRIMARY KEY, Payload LONGBINARY)");
            for (int id = 1; id <= count; id++)
                Assert.Equal(1, engine.ExecuteNonQuery("INSERT INTO GrowthInterop (Id, Payload) VALUES (@id, @payload)",
                    new Dictionary<string, object?> { ["id"] = id, ["payload"] = payload }));
        }
        using (var connection = AceTestDatabase.Open(path))
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO GrowthInterop (Id, Payload) VALUES (?, ?)";
            insert.Parameters.Add("id", OleDbType.Integer).Value = count + 1;
            insert.Parameters.Add("payload", OleDbType.LongVarBinary, payload.Length).Value = payload;
            Assert.Equal(1, insert.ExecuteNonQuery());
        }
        Verify(path, payload, count + 1);
        using (var database = JetDatabase.Open(path, readOnly: false))
        {
            var engine = new QueryEngine(database);
            Assert.Equal(count + 1, engine.ExecuteNonQuery("UPDATE GrowthInterop SET Payload = @payload",
                new Dictionary<string, object?> { ["payload"] = new byte[] { 1, 2, 3 } }));
        }
        Verify(path, [1, 2, 3], count + 1);
    }

    private static void Verify(string path, byte[] expected, int count)
    {
        using var connection = AceTestDatabase.Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Payload FROM GrowthInterop ORDER BY Id";
        using var reader = command.ExecuteReader();
        for (int id = 1; id <= count; id++)
        {
            Assert.True(reader.Read());
            Assert.Equal(id, reader.GetInt32(0));
            byte[] actual = Assert.IsType<byte[]>(reader.GetValue(1));
            Assert.Equal(expected.Length, actual.Length);
            Assert.True(expected.AsSpan().SequenceEqual(actual));
        }
        Assert.False(reader.Read());
    }
}
