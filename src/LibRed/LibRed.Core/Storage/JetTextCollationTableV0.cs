using System.IO.Compression;

namespace LibRed.Storage;

/// <summary>
/// The measured General v0 weights for the whole Basic Multilingual Plane — 63,208 code points, of which
/// 19,186 are ignorable. Consulted by <see cref="JetTextCollation"/> only after its own hand-verified
/// tables, so nothing here can change a weight that was already proven byte for byte.
/// </summary>
/// <remarks>
/// v1's table could be embedded from a published Microsoft file, because its primaries <i>are</i> the NLS
/// weights verbatim. v0's are a Jet-specific compaction of the NT4-era order (see
/// <c>docs/format/page-03-04-index-btree.md</c> §10.4), so no published file describes them and the only
/// source of truth is ACE: <c>SortKeyTableV0GeneratorTest</c> inserts every code point into an indexed text
/// column, reads the stored index keys back, and writes this resource.
/// <para>
/// Layout mirrors the v1 resource — an entry count, then four separately-deflated streams. Splitting them
/// matters: each column is nearly constant on its own and compresses to almost nothing, where interleaved
/// records would not. A primary length of <c>0xFF</c> marks an <b>ignorable</b> character, which contributes
/// no primary and no secondary slot at all; a length of <c>0</c> is a secondary-only combining mark.
/// </para>
/// </remarks>
internal static class JetTextCollationTableV0
{
    private const byte IgnorableLength = 0xFF;

    /// <summary>The weight for a character, or null when it is ignorable. False when the table has no entry,
    /// in which case the caller must refuse the value rather than emit a guess.</summary>
    public static bool TryGet(char c, out TailoredWeight? weight)
    {
        Table table = Loaded.Value;
        int index = Array.BinarySearch(table.CodePoints, c);
        if (index < 0) { weight = null; return false; }
        if (table.Lengths[index] == IgnorableLength) { weight = null; return true; }

        int start = table.PrimaryOffsets[index];
        int length = table.Lengths[index];
        weight = new TailoredWeight(table.Primaries[start..(start + length)], table.Secondaries[index]);
        return true;
    }

    /// <summary>The inline code for a word-sort ignorable — a character that adds no weight at all and
    /// records <c>80 &lt;pos&gt; 06 &lt;code&gt;</c> in the trailing section instead. There are 296 of them
    /// across the BMP: every dash and quotation form, the Arabic harakat, and the CJK and fullwidth
    /// punctuation.</summary>
    public static bool TryGetInlineCode(char c, out byte code)
    {
        Table table = Loaded.Value;
        int index = Array.BinarySearch(table.InlineCodePoints, c);
        code = index < 0 ? (byte)0 : table.InlineCodes[index];
        return index >= 0;
    }

    /// <summary>A kana's sound index, voicing secondary and small-form flag. Kana take the two-byte primary
    /// <c>7F &lt;sound&gt;</c>; hiragana, katakana and halfwidth katakana share a sound, so they encode
    /// identically. Voicing is an ordinary secondary — <c>03</c> dakuten, <c>04</c> handakuten — though a few
    /// characters carry other values. Small forms are recorded in the kana section instead of the
    /// primary.</summary>
    /// <param name="vowel">The sound a following prolonged mark takes: <c>ー</c> lengthens the preceding
    /// kana's VOWEL, not its sound, so <c>がー</c> is <c>7F 0A</c> then <c>7F 02</c> — "ga" lengthened by
    /// "a". Zero where it could not be measured, in which case a following <c>ー</c> must be refused.</param>
    public static bool TryGetKana(char c, out byte sound, out byte secondary, out bool small, out byte vowel)
    {
        Table table = Loaded.Value;
        int index = Array.BinarySearch(table.KanaCodePoints, c);
        if (index < 0) { sound = 0; secondary = 0; small = false; vowel = 0; return false; }
        sound = table.KanaSounds[index];
        secondary = table.KanaSecondaries[index];
        small = table.KanaSmall[index] != 0;
        vowel = table.KanaVowels[index];
        return true;
    }

    private sealed record Table(
        char[] CodePoints, byte[] Lengths, int[] PrimaryOffsets, byte[] Primaries, byte[] Secondaries,
        char[] InlineCodePoints, byte[] InlineCodes,
        char[] KanaCodePoints, byte[] KanaSounds, byte[] KanaSecondaries, byte[] KanaSmall,
        byte[] KanaVowels);

    // Lazy so the cost is paid only by a database that actually reaches beyond the hand-written tables.
    private static readonly Lazy<Table> Loaded = new(Load);

    private static Table Load()
    {
        using Stream stream = typeof(JetTextCollationTableV0).Assembly
            .GetManifestResourceStream("LibRed.Resources.SortKeyTableV0.bin")
            ?? throw new InvalidOperationException("The v0 sorting weight table resource is missing from the assembly.");

        var reader = new BinaryReader(stream);
        int count = reader.ReadInt32();
        int inlineCount = reader.ReadInt32();
        int kanaCount = reader.ReadInt32();
        byte[] deltas = ReadStream(reader);
        byte[] lengths = ReadStream(reader);
        byte[] primaries = ReadStream(reader);
        byte[] secondaries = ReadStream(reader);
        byte[] inlineDeltas = ReadStream(reader);
        byte[] inlineCodes = ReadStream(reader);
        byte[] kanaDeltas = ReadStream(reader);
        byte[] kanaSounds = ReadStream(reader);
        byte[] kanaSecondaries = ReadStream(reader);
        byte[] kanaSmall = ReadStream(reader);
        byte[] kanaVowels = ReadStream(reader);

        var codePoints = new char[count];
        var offsets = new int[count];
        int codePoint = 0, cursor = 0, primaryOffset = 0;
        for (int i = 0; i < count; i++)
        {
            codePoint += ReadVarInt(deltas, ref cursor);
            codePoints[i] = (char)codePoint;
            offsets[i] = primaryOffset;
            if (lengths[i] != IgnorableLength) primaryOffset += lengths[i];
        }

        var inlineCodePoints = new char[inlineCount];
        codePoint = 0;
        cursor = 0;
        for (int i = 0; i < inlineCount; i++)
        {
            codePoint += ReadVarInt(inlineDeltas, ref cursor);
            inlineCodePoints[i] = (char)codePoint;
        }

        var kanaCodePoints = new char[kanaCount];
        codePoint = 0;
        cursor = 0;
        for (int i = 0; i < kanaCount; i++)
        {
            codePoint += ReadVarInt(kanaDeltas, ref cursor);
            kanaCodePoints[i] = (char)codePoint;
        }

        return new Table(codePoints, lengths, offsets, primaries, secondaries, inlineCodePoints, inlineCodes,
                         kanaCodePoints, kanaSounds, kanaSecondaries, kanaSmall, kanaVowels);
    }

    private static byte[] ReadStream(BinaryReader reader)
    {
        byte[] compressed = reader.ReadBytes(reader.ReadInt32());
        var output = new MemoryStream();
        using (var inflate = new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress))
            inflate.CopyTo(output);
        return output.ToArray();
    }

    private static int ReadVarInt(byte[] source, ref int offset)
    {
        int value = 0, shift = 0;
        while (true)
        {
            byte b = source[offset++];
            value |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) return value;
            shift += 7;
        }
    }
}
