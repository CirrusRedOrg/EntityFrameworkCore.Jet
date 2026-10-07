using System.Data.OleDb;
using LibRed.Catalog;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// The descriptor's length is 30 bits, not the 24 the spec long claimed, with the storage flags in the top
// two. LibRed read and wrote it as 24-bit, so above 16 MiB the length spilled into the flag byte and
// produced a descriptor Access rejects outright ("Unrecognized database format").
//
// ACE authors the database here; LibRed only reads. That direction is the point — it fails on the old
// 24-bit reader and passes on the fixed one. Separately measured against ACE: 0x3FFFFFFF bytes are
// accepted and 0x40000000 rejected, fixing the ceiling at 1 GiB — see long-values.md.
[Collection(AceCollection.Name)]
public class LongValueLengthAccessTests(ITestOutputHelper output) : TempDatabaseTest
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

    // The other end of the same length field: zero. A long value's length has to be carried somewhere, and an
    // empty one has a choice of places to be - a 12-byte descriptor declaring 0 bytes, or no slot at all, the
    // variable-column offset table giving the column a zero-width chunk. LibRed writes the first
    // (JetTypeCodec.EncodeInlineLongValue always emits the 12 bytes) and its reader requires it, refusing a
    // descriptor shorter than 12. If ACE writes the second, LibRed cannot read an empty memo ACE authored.
    //
    // ACE authors here and LibRed reads, the same direction and for the same reason as the test above.
    [Fact]
    public void LibRed_reads_an_ACE_authored_empty_long_value()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "ace-empty-lval-");

        using (var connection = AceTestDatabase.Open(path))
        {
            using var ddl = connection.CreateCommand();
            ddl.CommandText = "CREATE TABLE EmptyProbe (Id LONG PRIMARY KEY, M MEMO, B LONGBINARY)";
            ddl.ExecuteNonQuery();

            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO EmptyProbe (Id, M, B) VALUES (?, ?, ?)";
            insert.Parameters.Add("id", OleDbType.Integer).Value = 1;
            insert.Parameters.Add("m", OleDbType.LongVarWChar).Value = "";
            insert.Parameters.Add("b", OleDbType.LongVarBinary).Value = Array.Empty<byte>();
            Assert.Equal(1, insert.ExecuteNonQuery());
        }

        // What ACE actually put in the row's variable chunk for each column. Measured 2026-09-25: both are
        // the full 12-byte inline descriptor, 00 00 00 80 then eight zero bytes - length 0 with the inline
        // flag, and no payload after it. So the zero-width slot LibRed's reader refuses is a form ACE never
        // writes, and the two engines encode an empty long value identically.
        using (var channel = LibRed.IO.PageChannel.Open(path))
        {
            byte[] expected = [0x00, 0x00, 0x00, 0x80, 0, 0, 0, 0, 0, 0, 0, 0];
            TableDefinition definition = new JetCatalog(channel).FindTable("EmptyProbe")!;
            foreach (int number in new UsageMap(channel, definition).DataPages())
            {
                var page = new DataPage();
                page.Read(channel.ReadPage(number), channel.Format);
                for (int row = 0; row < page.RowCount; row++)
                {
                    if (page.Rows[row].IsDeleted) continue;
                    foreach ((int index, byte[] slot) in
                        RowCodec.LongValueDescriptors(definition.Columns, channel.Format, page.GetRow(row)))
                    {
                        output.WriteLine(
                            $"{definition.Columns.Single(c => c.Index == index).Name}: {slot.Length} bytes"
                            + $" [{Convert.ToHexString(slot)}]");
                        Assert.Equal(expected, slot);
                    }
                }
            }
        }

        using var database = JetDatabase.Open(path);
        var probe = database.OpenTable("EmptyProbe");
        int memo = probe.Definition.Columns.Single(c => c.Name == "M").Index;
        int binary = probe.Definition.Columns.Single(c => c.Name == "B").Index;
        object?[] only = probe.Rows().Single();

        Assert.Equal("", only[memo]);
        Assert.Empty(Assert.IsType<byte[]>(only[binary]));
    }
}