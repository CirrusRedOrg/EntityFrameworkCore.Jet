using System.Data.OleDb;
using Xunit;

namespace LibRed.Core.Tests;

// The descriptor's length is 30 bits, not the 24 the spec long claimed, with the storage flags in the top
// two. LibRed read and wrote it as 24-bit, so above 16 MiB the length spilled into the flag byte and
// produced a descriptor Access rejects outright ("Unrecognized database format").
//
// ACE authors the database here; LibRed only reads. That direction is the point — it fails on the old
// 24-bit reader and passes on the fixed one. Separately measured against ACE: 0x3FFFFFFF bytes are
// accepted and 0x40000000 rejected, fixing the ceiling at 1 GiB — see long-values.md.
public class LongValueLengthAccessTests : TempDatabaseTest
{
    [Fact]
    public void LibRed_reads_an_ACE_authored_value_above_16_MiB()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "ace-length-read-");
        byte[] payload = new byte[16777217];
        new Random(1729).NextBytes(payload);

        using (var connection = AceTestDatabase.Open(path))
        {
            using var ddl = connection.CreateCommand();
            ddl.CommandText = "CREATE TABLE BoundaryProbe (Id LONG PRIMARY KEY, Payload LONGBINARY)";
            ddl.ExecuteNonQuery();
        }

        using (var connection = AceTestDatabase.Open(path))
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO BoundaryProbe (Id, Payload) VALUES (?, ?)";
            insert.Parameters.Add("id", OleDbType.Integer).Value = 1;
            insert.Parameters.Add("payload", OleDbType.LongVarBinary, payload.Length).Value = payload;
            Assert.Equal(1, insert.ExecuteNonQuery());
        }

        using var database = JetDatabase.Open(path);
        var table = database.OpenTable("BoundaryProbe");
        int column = table.Definition.Columns.Single(c => c.Name == "Payload").Index;
        byte[] actual = Assert.IsType<byte[]>(table.Rows().Single()[column]);
        Assert.True(payload.AsSpan().SequenceEqual(actual), "LibRed did not read back the bytes ACE wrote.");
    }
}
