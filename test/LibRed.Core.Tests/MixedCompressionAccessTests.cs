using System.Data.OleDb;
using LibRed.Catalog;
using LibRed.IO;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// Whether one non-Latin1 character costs a value its compression depends entirely on WHICH character.
//
// This file used to conclude that ACE never writes the MIXED form (compressed runs toggled by an embedded
// 0x00), and it was wrong in an instructive way: every case used 一, and U+4E00 is stored 00 4E, whose low
// byte is indistinguishable from the mode switch. ACE avoids the mixed form for exactly such a character —
// so the file had picked the one input that cannot show it, and read "ACE will not" off "ACE cannot here".
//
// Swap in 中 (2D 4E, no 0x00 low byte) and the same 1001-character shape comes back MIXED: 1005 bytes,
// FF FE + 1000 compressed + a switch + the 2-byte character. So the form appears on LVAL pages, not only
// inline, and "one incompressible character forfeits the whole value" holds only for the ambiguous ones.
//
// Position still makes no difference, which rules out compressing greedily until blocked. The emit rule
// and the inline cases are in MemoCompressionAccessTests (Engine suite); data-types.md §7 has the summary.
//
// Scope: whatever ACE is installed. The provider is logged, since the scheme dates from Jet 4.0 and an
// older engine may differ; 12.0 and 16.0 were checked by hand and agree byte for byte, but only one ACE is
// installed at a time so the test cannot assert both.
public class MixedCompressionAccessTests(ITestOutputHelper output) : TempDatabaseTest
{
    // name, payload, stored bytes, mode switches in the payload
    public static TheoryData<string, string, int, int> Cases => new()
    {
        // The control: the saving is available and taken, so a refusal below is a decision, not an inability.
        { "all ascii (control)", new string('a', 1000), 1002, 0 },
        // 一 is 00 4E — ambiguous with the switch, so ACE will not mix and the whole value goes UTF-16,
        // throwing away about 1000 bytes for one character. Position makes no difference.
        { "ambiguous last", new string('a', 1000) + '一', 2002, 0 },
        { "ambiguous first", '一' + new string('a', 1000), 2002, 0 },
        { "ambiguous middle", new string('a', 500) + '一' + new string('a', 500), 2002, 0 },
        // 中 is 2D 4E — unambiguous, so the same shape mixes instead: 2 + 1000 + 1 + 2.
        { "unambiguous last", new string('a', 1000) + '中', 1005, 1 },
        // Leading 2-byte run costs an extra switch to get into it: marker, switch, 中, switch, 1000.
        { "unambiguous first", '中' + new string('a', 1000), 1006, 2 },
        { "unambiguous middle", new string('a', 500) + '中' + new string('a', 500), 1006, 2 },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Only_an_ambiguous_character_forfeits_compression(
        string name, string payload, int expectedBytes, int expectedSwitches)
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

                    Assert.Equal(expectedBytes, stored);
                    Assert.Equal(expectedBytes < payload.Length * 2,
                        body.Length >= 2 && body[0] == 0xFF && body[1] == 0xFE);
                    Assert.Equal(expectedSwitches, Toggles(body));

                    // And whatever form it chose, LibRed must read the value back intact — the decoder is
                    // the half of this that can silently return wrong data.
                    Assert.Equal(payload, Storage.Types.JetTypeCodec.DecodeText(body));
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
