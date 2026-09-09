using System.Buffers.Binary;
using System.Data.OleDb;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;
using Xunit;

namespace LibRed.Engine.Tests;

// When ACE compresses a Memo value, and what the WITH COMPRESSION flag actually gates.
//
// LibRed required the compressed-Unicode capable flag before compressing anything, which made every short
// ASCII memo on an ordinary LONGTEXT column twice the size ACE stores — found by diffing row bytes. The
// flag is not the gate for inline values:
//
//   * the storage form is chosen on the UNCOMPRESSED length — 33 ASCII characters are 66 bytes and go to a
//     page even though they would compress to 35 and fit inline;
//   * an INLINE value is compressed whether the flag is set or not;
//   * a value on a single LVAL page is compressed ONLY when the flag is set (40 ASCII characters store 80
//     bytes on a plain column, 42 on a WITH COMPRESSION one);
//   * a chained value is never compressed (long-values.md).
//
// A plain Text column does honour the flag, so the rule is specific to long values.
[Collection(AceCollection.Name)]
public class MemoCompressionAccessTests(ITestOutputHelper output) : TempDatabaseTest
{
    // declaration, characters, expected form, expected on-disk length
    public static TheoryData<string, int, string, int> Cases => new()
    {
        { "LONGTEXT", 5, "inline", 7 },          // 2-byte marker + 5
        { "LONGTEXT", 30, "inline", 32 },
        { "LONGTEXT", 32, "inline", 34 },        // 64 uncompressed — the last that inlines
        { "LONGTEXT", 33, "page", 66 },          // 66 uncompressed: a page, and NOT compressed
        { "LONGTEXT", 40, "page", 80 },
        { "LONGTEXT WITH COMPRESSION", 5, "inline", 7 },
        { "LONGTEXT WITH COMPRESSION", 30, "inline", 32 },
        { "LONGTEXT WITH COMPRESSION", 40, "page", 42 },     // compressed on the page
        { "LONGTEXT WITH COMPRESSION", 100, "page", 102 },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Ace_and_libred_store_the_same_form_and_length(
        string declaration, int characters, string form, int length)
    {
        string ddl = $"CREATE TABLE W (A LONG, M {declaration})";
        string insert = $"INSERT INTO W (A, M) VALUES (1, '{new string('x', characters)}')";

        string? ace = Describe(ddl, insert, AceRun);
        Assert.SkipWhen(ace is null, $"This ACE build does not accept {declaration}.");

        output.WriteLine($"{declaration} x{characters}: {ace}");
        Assert.Equal($"{form} len={length}", ace);
        Assert.Equal(ace, Describe(ddl, insert, LibRedRun));
    }

    // "Always compresses inline" holds only where compression is POSSIBLE, and these are the two edges of
    // that. A character above 0xFF forfeits the whole value, and the 2-byte marker has to pay for itself,
    // so 1- and 2-character values stay UTF-16 and compression starts at 3. Both edges matter now that the
    // inline path no longer consults the capable flag: without them a "compress inline unconditionally"
    // implementation would pass everything above.
    [Theory]
    [InlineData("x", "inline len=2")]                 // 1 char: 2 + 1 > 2, not worth it
    [InlineData("xx", "inline len=4")]                // 2 chars: 2 + 2 == 4, no saving
    [InlineData("xxx", "inline len=5")]               // 3 chars: 2 + 3 < 6, compressed
    [InlineData("café", "inline len=6")]              // Latin1, not ASCII — still one byte per character
    [InlineData("中中中", "inline len=6")]
    public void Compression_needs_latin1_and_a_saving(string value, string expected)
    {
        const string ddl = "CREATE TABLE W (A LONG, M LONGTEXT)";
        string insert = $"INSERT INTO W (A, M) VALUES (1, '{value}')";

        string? ace = Describe(ddl, insert, AceRun);
        Assert.SkipWhen(ace is null, "ACE would not run this insert.");
        Assert.Equal(expected, ace);
        Assert.Equal(ace, Describe(ddl, insert, LibRedRun));
    }

    // The same edges on a WITH COMPRESSION column, so the flag is not quietly doing the work.
    [Fact]
    public void Non_ascii_is_never_compressed_even_on_a_capable_column()
    {
        const string ddl = "CREATE TABLE W (A LONG, M LONGTEXT WITH COMPRESSION)";
        string insert = $"INSERT INTO W (A, M) VALUES (1, '{new string('中', 30)}')";

        string? ace = Describe(ddl, insert, AceRun);
        Assert.SkipWhen(ace is null, "This ACE build does not accept WITH COMPRESSION.");
        Assert.Equal("inline len=60", ace);
        Assert.Equal(ace, Describe(ddl, insert, LibRedRun));
    }

    // THE MIXED FORM, which data-types.md recorded as technically possible but never produced by ACE. It is
    // produced readily; the earlier attempt used 1,000 ASCII plus one CJK character, far too long to stay
    // inline, so it went to a page and was not compressed at all.
    //
    // ACE emits it only when it strictly SAVES space and no character in a 2-byte run has a 0x00 low byte —
    // the latter because such a character is indistinguishable from the mode switch. Both conditions are
    // what make the decoder unambiguous, so both are asserted.
    [Theory]
    [InlineData("café中", "FFFE636166E9002D4E")]              // mixed: saves one byte
    [InlineData("aaa中bbb中ccc", "FFFE616161002D4E00626262002D4E00636363")]
    [InlineData("中aaaaa", "FFFE002D4E006161616161")]          // opens with a switch
    [InlineData("abcdef", "FFFE616263646566")]                // all Latin1: no switches
    [InlineData("abc中", "6100620063002D4E")]                  // mixed would also be 8 bytes: no saving
    [InlineData("ab中cd中ef", "610062002D4E630064002D4E65006600")]
    [InlineData("中", "2D4E")]
    [InlineData("aaaaa一", "61006100610061006100004E")]        // U+4E00 is 00 4E — ACE avoids mixed entirely
    [InlineData("aaaaaĀ", "610061006100610061000001")]         // U+0100 is 00 01, likewise
    [InlineData("aaaaa一中", "61006100610061006100004E2D4E")]
    public void The_mixed_form_is_written_when_it_saves_and_stays_unambiguous(string value, string expected)
    {
        const string ddl = "CREATE TABLE W (A LONG, M LONGTEXT)";
        string insert = $"INSERT INTO W (A, M) VALUES (1, '{value}')";

        byte[]? ace = Payload(ddl, insert, AceRun);
        Assert.SkipWhen(ace is null, "ACE would not run this insert.");
        Assert.Equal(expected, Convert.ToHexString(ace!));
    }

    // LibRed writes the mixed form too, byte-for-byte. The tie rule differs between the two paths and is
    // measured both ways: a long value takes the compressed form only when it is strictly smaller, an
    // ordinary Text column takes it when it is no larger — "ab中cd" is 10 bytes either way and comes back
    // UTF-16 from a Memo, mixed from a Text column. Both are asserted, since a writer that got the tie
    // wrong in either direction would still pass every non-tie case.
    [Theory]
    [InlineData("café中", "FFFE636166E9002D4E")]        // strictly smaller: 9 < 10
    [InlineData("ab中cd", "610062002D4E63006400")]      // exact tie on a Memo: UTF-16 wins
    [InlineData("abc中de", "FFFE616263002D4E006465")]   // 11 < 12
    [InlineData("中aaaaa", "FFFE002D4E006161616161")]
    public void Libred_writes_the_mixed_form_as_ace_does(string value, string expected)
    {
        const string ddl = "CREATE TABLE W (A LONG, M LONGTEXT)";
        string insert = $"INSERT INTO W (A, M) VALUES (1, '{value}')";

        byte[]? ace = Payload(ddl, insert, AceRun);
        Assert.SkipWhen(ace is null, "ACE would not run this insert.");

        Assert.Equal(expected, Convert.ToHexString(ace!));
        Assert.Equal(expected, Convert.ToHexString(Payload(ddl, insert, LibRedRun)!));
    }

    // The same on a Text column, where the tie goes the other way.
    [Theory]
    [InlineData("ab中cd", "FFFE6162002D4E006364")]      // exact tie on Text: the compressed form wins
    [InlineData("abc中", "FFFE616263002D4E")]
    [InlineData("ab", "61006200")]                     // under the three-character floor
    [InlineData("abc", "FFFE616263")]
    [InlineData("aaaaa一", "61006100610061006100004E")] // ambiguous character forfeits it
    public void A_text_column_breaks_the_tie_the_other_way(string value, string expected)
    {
        const string ddl = "CREATE TABLE W (A LONG, T TEXT(100) WITH COMPRESSION)";
        string insert = $"INSERT INTO W (A, T) VALUES (1, '{value}')";

        string? ace = TextSlot(ddl, insert, AceRun);
        Assert.SkipWhen(ace is null, "This ACE build does not accept WITH COMPRESSION.");

        Assert.Equal(expected, ace);
        Assert.Equal(expected, TextSlot(ddl, insert, LibRedRun));
    }

    /// <summary>The stored bytes of the row's single variable column — the tail before the offset table,
    /// which for one variable column is seven bytes from the row's end.</summary>
    private static string? TextSlot(string ddl, string insert, Action<string, string[]> run)
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "textslot-");
        try
        {
            try { run(path, [ddl, insert]); }
            catch (OleDbException) { return null; }

            int definitionPage;
            using (var database = JetDatabase.Open(path, readOnly: true))
                definitionPage = database.Catalog.FindTable("W")!.DefinitionPage;

            using var channel = PageChannel.Open(path, readOnly: true);
            JetFormatBase format = channel.Format;
            for (int page = 1; page < channel.PageCount; page++)
            {
                byte[] bytes = channel.ReadPage(page).Span.ToArray();
                if (bytes[0] != 0x01) continue;
                if (BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4, 4)) != definitionPage) continue;
                if (BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(format.DataRowCountOffset, 2)) == 0)
                    continue;

                int start = BinaryPrimitives.ReadUInt16LittleEndian(
                    bytes.AsSpan(format.DataRowDirectoryOffset, 2)) & 0x1FFF;
                int at = start + 2 + 4;
                return Convert.ToHexString(bytes.AsSpan(at, format.PageSize - 7 - at));
            }
            return null;
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The consequence of getting the decoder wrong is silent: LibRed used to read the whole payload as one
    // Latin1 run and return "café\0-N" for a value Access wrote. Access defaults Unicode Compression to Yes,
    // so this is ordinary mixed-script text, not an exotic case.
    [Theory]
    [InlineData("café中")]
    [InlineData("abc中def")]
    [InlineData("aaaaa中")]
    [InlineData("中aaaaa")]
    [InlineData("aaa中bbb中ccc")]
    [InlineData("aaaaa一")]
    [InlineData("aaaaa一中")]
    public void Libred_reads_back_what_ace_wrote(string value)
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "mixedread-");
        try
        {
            AceRun(path, ["CREATE TABLE W (A LONG, M LONGTEXT)",
                $"INSERT INTO W (A, M) VALUES (1, '{value}')"]);

            using var database = JetDatabase.Open(path, readOnly: true);
            Assert.Equal(value, database.OpenTable("W").Rows().Single()[1] as string);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>The inline long value's payload — the bytes after its 12-byte descriptor.</summary>
    private static byte[]? Payload(string ddl, string insert, Action<string, string[]> run)
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "mixed-");
        try
        {
            try { run(path, [ddl, insert]); }
            catch (OleDbException) { return null; }

            int definitionPage;
            using (var database = JetDatabase.Open(path, readOnly: true))
                definitionPage = database.Catalog.FindTable("W")!.DefinitionPage;

            using var channel = PageChannel.Open(path, readOnly: true);
            JetFormatBase format = channel.Format;
            for (int page = 1; page < channel.PageCount; page++)
            {
                byte[] bytes = channel.ReadPage(page).Span.ToArray();
                if (bytes[0] != 0x01) continue;
                if (BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4, 4)) != definitionPage) continue;
                if (BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(format.DataRowCountOffset, 2)) == 0)
                    continue;

                int start = BinaryPrimitives.ReadUInt16LittleEndian(
                    bytes.AsSpan(format.DataRowDirectoryOffset, 2)) & 0x1FFF;
                int at = start + 2 + 4;
                int length = (int)(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at, 4)) & 0x3FFFFFFF);
                return bytes.AsSpan(at + 12, length).ToArray();
            }
            return null;
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static void AceRun(string path, string[] statements)
    {
        using OleDbConnection connection = AceTestDatabase.Open(path);
        foreach (string s in statements)
        {
            using OleDbCommand command = connection.CreateCommand();
            command.CommandText = s;
            command.ExecuteNonQuery();
        }
    }

    private static void LibRedRun(string path, string[] statements)
    {
        using var database = JetDatabase.Open(path, readOnly: false);
        var engine = new QueryEngine(database);
        foreach (string s in statements) engine.ExecuteNonQuery(s);
    }

    /// <summary>The long value's storage form and declared length, read from the row's descriptor.</summary>
    private static string? Describe(string ddl, string insert, Action<string, string[]> run)
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "memocomp-");
        try
        {
            try { run(path, [ddl, insert]); }
            catch (OleDbException) { return null; }

            int definitionPage;
            using (var database = JetDatabase.Open(path, readOnly: true))
                definitionPage = database.Catalog.FindTable("W")!.DefinitionPage;

            using var channel = PageChannel.Open(path, readOnly: true);
            JetFormatBase format = channel.Format;
            for (int page = 1; page < channel.PageCount; page++)
            {
                byte[] bytes = channel.ReadPage(page).Span.ToArray();
                if (bytes[0] != 0x01) continue;
                if (BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4, 4)) != definitionPage) continue;
                if (BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(format.DataRowCountOffset, 2)) == 0)
                    continue;

                int start = BinaryPrimitives.ReadUInt16LittleEndian(
                    bytes.AsSpan(format.DataRowDirectoryOffset, 2)) & 0x1FFF;
                int at = start + 2 + 4;   // past the row's column count and the LONG column
                uint header = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at, 4));
                byte flags = (byte)(bytes[at + 3] & 0xC0);
                return $"{(flags == 0x80 ? "inline" : flags == 0x40 ? "page" : "chained")} "
                    + $"len={header & 0x3FFFFFFF}";
            }
            return null;
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
