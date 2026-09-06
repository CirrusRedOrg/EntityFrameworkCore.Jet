using System.Data.OleDb;
using LibRed.Catalog;
using LibRed.IO;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// ACE never writes the MIXED compressed form - compressed runs toggled by an embedded 0x00, the shape
// mdbtools decodes and LibRed's reader does not. It is all or nothing, and this is what makes LibRed's
// all-or-nothing writer a match rather than a simplification.
//
// Worth asserting rather than assuming, because the obvious reading of the first sample was wrong twice
// over. "mixed 一 text" came back fully UTF-16, but at 12 characters the saving is trivial, so a heuristic
// that does not bother for small values would look identical. These cases make the saving large - one
// character out of 1001 forfeits about 1000 bytes - and move the incompressible character around, which
// also rules out compressing greedily until blocked.
//
// Scope: whatever ACE is installed. The provider is logged, since the scheme dates from Jet 4.0 and an
// older engine may differ; 12.0 and 16.0 were checked by hand and agree byte for byte, but only one ACE is
// installed at a time so the test cannot assert both.
public class MixedCompressionAccessTests(ITestOutputHelper output) : TempDatabaseTest
{
    public static TheoryData<string, string, bool> Cases => new()
    {
        // The control: the saving is available and taken, so a refusal below is a decision, not an inability.
        { "all ascii (control)", new string('a', 1000), true },
        // One character out of 1001 costs the whole value its compression - about 1000 bytes thrown away.
        // Position makes no difference, which rules out "compress greedily until blocked" as well.
        { "odd char last", new string('a', 1000) + '一', false },
        { "odd char first", '一' + new string('a', 1000), false },
        { "odd char middle", new string('a', 500) + '一' + new string('a', 500), false },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Ace_never_writes_the_mixed_compressed_form(string name, string payload, bool expectCompressed)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "mixed-comp-");
        using (OleDbConnection connection = AceTestDatabase.Open(path))
        {
            // The finding is only ever about the engine that produced it: the scheme dates from Jet 4.0, and
            // an older ACE may behave differently from the one installed here.
            output.WriteLine($"provider={connection.Provider} version={connection.ServerVersion} "
                + $"processBits={IntPtr.Size * 8}");
            using (OleDbCommand ddl = connection.CreateCommand())
            {
                ddl.CommandText = "CREATE TABLE MixedProbe (Id LONG PRIMARY KEY, M LONGCHAR WITH COMP)";
                ddl.ExecuteNonQuery();
            }
            using OleDbCommand insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO MixedProbe (Id, M) VALUES (1, ?)";
            insert.Parameters.Add("m", OleDbType.LongVarWChar, payload.Length).Value = payload;
            insert.ExecuteNonQuery();
        }

        using var channel = PageChannel.Open(path, readOnly: true);
        TableDef definition = new JetCatalog(channel).FindTable("MixedProbe")!;
        ColumnDef memo = definition.Columns.Single(c => c.Name == "M");
        var decoder = new RowDecoder(definition.Columns, channel.Format);
        var reader = new LongValueReader(channel);

        foreach (int number in new UsageMap(channel, definition).DataPages())
        {
            var page = new DataPage();
            page.Read(channel.ReadPage(number), channel.Format);
            for (int row = 0; row < page.RowCount; row++)
            {
                if (page.Rows[row].IsDeleted) continue;
                foreach (var raw in decoder.LongValueRaw(page.GetRow(row)))
                {
                    if (raw.Key != memo.ColumnId) continue;
                    int stored = (int)(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(raw.Value)
                        & Formats.LongValueFormat.LengthMask);
                    byte[] body = reader.Resolve(raw.Value);
                    output.WriteLine($"{name}: {payload.Length} chars -> stored {stored} bytes "
                        + $"(utf16 would be {payload.Length * 2}); head={Convert.ToHexString(body[..Math.Min(8, body.Length)])}");

                    Assert.Equal(expectCompressed ? payload.Length + 2 : payload.Length * 2, stored);
                    Assert.Equal(expectCompressed, body.Length >= 2 && body[0] == 0xFF && body[1] == 0xFE);
                    // Whatever it wrote, no mode toggle: this engine emits one form for the whole value.
                    Assert.Equal(0, Toggles(body));
                    return;
                }
            }
        }
        Assert.Fail("No long-value descriptor was found for the memo column.");
    }

    /// <summary>Mode toggles in a compressed payload — an embedded <c>0x00</c> after the <c>FF FE</c> marker
    /// switches between compressed and uncompressed runs. Zero for an uncompressed value, which carries no
    /// marker and so has no modes to switch between.</summary>
    private static int Toggles(byte[] body)
    {
        if (body.Length < 2 || body[0] != 0xFF || body[1] != 0xFE) return 0;
        int count = 0;
        for (int i = 2; i < body.Length; i++)
            if (body[i] == 0x00) count++;
        return count;
    }
}
