using System.Data.OleDb;
using LibRed.IO;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// What fixes the Memo CHARACTER limit, given the 30-bit descriptor length is a BYTE limit.
//
// Compressed Unicode (data-types.md §7) would store ASCII one byte per character, which would put the
// Memo ceiling at twice the byte ceiling. It does not apply here: ACE creates a text column through SQL
// DDL with the compressed-capable extended flag CLEAR, and stores long text as UTF-16 regardless of
// content. So the ceiling is the byte ceiling halved, and LibRed's always-UTF-16 writer already matches.
//
// Scope: this is the SQL-created column. A column Access's designer creates with Unicode Compression set
// to Yes carries the flag, and LibRed reads that form back (the all-compressed case) but never writes it.
public class LongTextStorageAccessTests(ITestOutputHelper output) : TempDatabaseTest
{
    [Theory]
    [InlineData('a')]
    [InlineData('一')]
    public void Ace_stores_long_text_as_utf16_and_leaves_the_column_uncompressed(char fill)
    {
        const int characters = 100_000;
        string payload = new(fill, characters);

        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "long-text-storage-");
        using (OleDbConnection connection = AceTestDatabase.Open(path))
        {
            using (OleDbCommand ddl = connection.CreateCommand())
            {
                ddl.CommandText = "CREATE TABLE MemoProbe (Id LONG PRIMARY KEY, T TEXT(50), M LONGCHAR)";
                ddl.ExecuteNonQuery();
            }
            using OleDbCommand insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO MemoProbe (Id, T, M) VALUES (1, 'abc', ?)";
            insert.Parameters.Add("m", OleDbType.LongVarWChar, characters).Value = payload;
            insert.ExecuteNonQuery();
        }

        using var database = JetDatabase.Open(path);
        var definition = database.Catalog.FindTable("MemoProbe")!;
        var memo = definition.Columns.Single(c => c.Name == "M");

        Assert.False(definition.Columns.Single(c => c.Name == "T").SupportsCompressedUnicode);
        Assert.False(memo.SupportsCompressedUnicode);

        using var channel = PageChannel.Open(path, readOnly: true);
        int stored = (int)(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(
            RawDescriptor(channel, definition, memo.ColumnId)) & LibRed.Formats.LongValueFormat.LengthMask);
        output.WriteLine($"U+{(int)fill:X4}: {characters} characters stored as {stored} bytes");

        Assert.Equal(characters * 2, stored);
        Assert.Equal(payload, Assert.IsType<string>(
            new Table(channel, definition).Rows().Single()[memo.Index]));
    }

    // Microsoft says "only instances of MEMO columns that, when compressed, will fit within 4096 bytes or
    // less, will be compressed". Measured, that is not the rule. Compression tracks the storage form: a value
    // that lands on a SINGLE LVAL page (flag 0x40) is compressed, a CHAINED one (0x00) never is — and the
    // single/chained split is decided on the UNCOMPRESSED length, so the compressed size never approaches
    // 4096. The boundary sits at 1908 characters (3816 bytes) here, well under both 4096 and the 4076-byte
    // single-page capacity; what fixes it at that value is not established.
    //
    // The consequence for the ceiling above is the same either way: anything large is chained, so never
    // compressed, so the character limit is the byte limit halved regardless of WITH COMPRESSION.
    [Theory]
    [InlineData(1000, true)]
    [InlineData(1908, true)]
    [InlineData(1909, false)]
    [InlineData(5000, false)]
    public void Ace_compresses_a_memo_only_when_it_stays_on_one_page(int characters, bool expectCompressed)
    {
        string payload = new('a', characters);

        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "memo-with-comp-");
        using (OleDbConnection connection = AceTestDatabase.Open(path))
        {
            using (OleDbCommand ddl = connection.CreateCommand())
            {
                ddl.CommandText = "CREATE TABLE CompProbe (Id LONG PRIMARY KEY, M MEMO WITH COMP)";
                ddl.ExecuteNonQuery();
            }
            using OleDbCommand insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO CompProbe (Id, M) VALUES (1, ?)";
            insert.Parameters.Add("m", OleDbType.LongVarWChar, characters).Value = payload;
            insert.ExecuteNonQuery();
        }

        using var database = JetDatabase.Open(path);
        var definition = database.Catalog.FindTable("CompProbe")!;
        var memo = definition.Columns.Single(c => c.Name == "M");
        Assert.True(memo.SupportsCompressedUnicode, "WITH COMP did not set the compressed-capable flag.");

        using var channel = PageChannel.Open(path, readOnly: true);
        byte[] raw = RawDescriptor(channel, definition, memo.ColumnId);
        uint word = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(raw);
        int stored = (int)(word & LibRed.Formats.LongValueFormat.LengthMask);
        output.WriteLine($"{characters} chars: stored={stored} ({(double)stored / characters:0.###} b/ch) "
            + $"flags=0x{(byte)(raw[3] & 0xC0):X2} descriptor={Convert.ToHexString(raw)}");

        Assert.Equal(expectCompressed ? characters + 2 : characters * 2, stored);
        // The storage form is the thing compression actually tracks: single page compresses, chained never does.
        Assert.Equal(expectCompressed ? 0x40 : 0x00, raw[3] & 0xC0);
        Assert.Equal(payload, Assert.IsType<string>(
            new Table(channel, definition).Rows().Single()[memo.Index]));
    }

    // The storage form is chosen on the UNCOMPRESSED length, identically whether or not the column can
    // compress — which is what makes 3816 a chaining boundary rather than a compression one, and is why
    // LibRed keys its own inline/single/chained decision off the uncompressed size too.
    [Theory]
    [InlineData(32, false, 0x80)]    // 64 uncompressed bytes: the last inline value
    [InlineData(33, false, 0x40)]    // 66: first to need a page
    [InlineData(1908, false, 0x40)]  // 3816: the last that stays on one page
    [InlineData(1909, false, 0x00)]  // 3818: chained
    [InlineData(2038, false, 0x00)]  // 4076, the chunk-row size, is well past it
    [InlineData(32, true, 0x80)]
    [InlineData(33, true, 0x40)]
    [InlineData(1908, true, 0x40)]
    [InlineData(1909, true, 0x00)]
    public void Ace_picks_the_storage_form_from_the_uncompressed_length(int characters, bool withComp, int flag)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "plain-memo-chain-");
        string payload = new('a', characters);
        using (OleDbConnection connection = AceTestDatabase.Open(path))
        {
            using (OleDbCommand ddl = connection.CreateCommand())
            {
                ddl.CommandText = "CREATE TABLE PlainMemo (Id LONG PRIMARY KEY, M LONGCHAR"
                    + (withComp ? " WITH COMP" : "") + ")";
                ddl.ExecuteNonQuery();
            }
            using OleDbCommand insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO PlainMemo (Id, M) VALUES (1, ?)";
            insert.Parameters.Add("m", OleDbType.LongVarWChar, characters).Value = payload;
            insert.ExecuteNonQuery();
        }

        using var database = JetDatabase.Open(path);
        var definition = database.Catalog.FindTable("PlainMemo")!;
        var memo = definition.Columns.Single(c => c.Name == "M");
        using var channel = PageChannel.Open(path, readOnly: true);
        byte[] raw = RawDescriptor(channel, definition, memo.ColumnId);
        Assert.Equal(flag, raw[3] & 0xC0);
    }

    // A memo steps two bytes at a time, so the theory above can only ever bracket the single-page edge:
    // 3816 stays, 3818 chains, and 3817 was never asked. An OLE column takes any byte length, which closes
    // it -- and the same instrument pins the inline edge exactly. Both rows live in ACE's own table, so the
    // definition is out of the picture and the two engines' choices can be compared directly. Swept whole
    // (1..100 and 3700..3899, every size): these four are where the answer changes.
    [Theory]
    [InlineData(64, 0x80)]      // the last inline value
    [InlineData(65, 0x40)]      // the first to take a page
    [InlineData(3816, 0x40)]    // the last that stays on one page
    [InlineData(3817, 0x00)]    // the first chained -- the byte the memo theory cannot reach
    public void Ace_and_libred_pick_the_same_storage_form_at_the_exact_byte_edges(int bytes, int flag)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "ole-edge-");
        using (OleDbConnection connection = AceTestDatabase.Open(path))
        {
            using (OleDbCommand ddl = connection.CreateCommand())
            {
                ddl.CommandText = "CREATE TABLE OleEdge (Id LONG, O OLEOBJECT)";
                ddl.ExecuteNonQuery();
            }
            using OleDbCommand insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO OleEdge (Id, O) VALUES (1, ?)";
            insert.Parameters.Add("o", OleDbType.LongVarBinary, bytes).Value = new byte[bytes];
            insert.ExecuteNonQuery();
        }

        using (var database = JetDatabase.Open(path, readOnly: false))
            database.OpenTable("OleEdge").Insert([2, new byte[bytes]]);

        using var read = JetDatabase.Open(path);
        var definition = read.Catalog.FindTable("OleEdge")!;
        int columnId = definition.Columns.Single(c => c.Name == "O").ColumnId;
        using var channel = PageChannel.Open(path, readOnly: true);
        List<byte[]> descriptors = RawDescriptors(channel, definition, columnId);

        Assert.Equal(2, descriptors.Count);
        output.WriteLine($"{bytes} bytes: ACE 0x{descriptors[0][3] & 0xC0:X2}, LibRed 0x{descriptors[1][3] & 0xC0:X2}");
        Assert.Equal(flag, descriptors[0][3] & 0xC0);   // ACE wrote first
        Assert.Equal(flag, descriptors[1][3] & 0xC0);
    }

    /// <summary>The raw long-value descriptor ACE wrote into the row for one column.</summary>
    private static byte[] RawDescriptor(PageChannel channel, Catalog.TableDef definition, int columnId) =>
        RawDescriptors(channel, definition, columnId).FirstOrDefault()
        ?? throw new InvalidOperationException($"No long-value descriptor found for column id {columnId}.");

    /// <summary>Every row's long-value descriptor for one column, in row order.</summary>
    private static List<byte[]> RawDescriptors(PageChannel channel, Catalog.TableDef definition, int columnId)
    {
        var found = new List<byte[]>();
        var decoder = new RowDecoder(definition.Columns, channel.Format);
        foreach (int number in new UsageMap(channel, definition).DataPages())
        {
            var page = new DataPage();
            page.Read(channel.ReadPage(number), channel.Format);
            for (int row = 0; row < page.RowCount; row++)
            {
                if (page.Rows[row].IsDeleted) continue;
                foreach (var descriptor in decoder.LongValueRaw(page.GetRow(row)))
                    if (descriptor.Key == columnId)
                        found.Add(descriptor.Value[..12]);
            }
        }
        return found;
    }
}
