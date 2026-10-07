using System.IO.Compression;

namespace LibRed.Storage;

/// <summary>
/// The measured General v0 weights for the whole Basic Multilingual Plane — 63,105 code points, of which
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
        int index = table.Slots[c] - 1;
        if (index < 0) { weight = null; return false; }
        if (table.Lengths[index] == IgnorableLength) { weight = null; return true; }

        // Each character's primary bytes are sliced out once and kept: a text comparison weighs every character
        // of both sides, and a fresh slice each time was an allocation per character. A race only builds the
        // same slice twice.
        byte[]? primaries = table.PrimarySlices[index];
        if (primaries is null)
        {
            int start = table.PrimaryOffsets[index];
            table.PrimarySlices[index] = primaries = table.Primaries[start..(start + table.Lengths[index])];
        }
        weight = new TailoredWeight(primaries, table.Secondaries[index]);
        return true;
    }

    /// <summary>The inline code for a word-sort ignorable — a character that adds no weight at all and
    /// records <c>80 &lt;pos&gt; 06 &lt;code&gt;</c> in the trailing section instead. This table holds the 40
    /// measured ones; with the 20 hand-verified in <see cref="JetTextCollation"/> that is 60 across the BMP —
    /// every dash and quotation form, the Arabic harakat, and the CJK and fullwidth punctuation.</summary>
    public static bool TryGetInlineCode(char c, out byte code)
    {
        Table table = Loaded.Value;
        int index = table.InlineSlots[c] - 1;
        code = index < 0 ? (byte)0 : table.InlineCodes[index];
        return index >= 0;
    }

    /// <summary>A kana's sound index, voicing secondary and small-form flag. Kana take the two-byte primary
    /// <c>7F &lt;sound&gt;</c>; hiragana, katakana and halfwidth katakana share a sound, so they encode
    /// identically. Voicing is an ordinary secondary — <c>03</c> dakuten, <c>04</c> handakuten — though a few
    /// characters carry other values. Small forms are recorded in the kana section instead of the
    /// primary.</summary>
    /// <param name="c">The character to look up.</param>
    /// <param name="sound">Its sound index, the second byte of the primary.</param>
    /// <param name="secondary">Its voicing secondary.</param>
    /// <param name="small">Whether it is a small form.</param>
    /// <param name="vowel">The sound a following prolonged mark takes: <c>ー</c> lengthens the preceding
    /// kana's VOWEL, not its sound, so <c>がー</c> is <c>7F 0A</c> then <c>7F 02</c> — "ga" lengthened by
    /// "a". Zero where it could not be measured, in which case a following <c>ー</c> must be refused.</param>
    public static bool TryGetKana(char c, out byte sound, out byte secondary, out bool small, out byte vowel)
    {
        Table table = Loaded.Value;
        int index = table.KanaSlots[c] - 1;
        if (index < 0) { sound = 0; secondary = 0; small = false; vowel = 0; return false; }
        sound = table.KanaSounds[index];
        secondary = table.KanaSecondaries[index];
        small = table.KanaSmall[index] != 0;
        vowel = table.KanaVowels[index];
        return true;
    }

    /// <remarks>Each of the three sets is reached through a slot per BMP code point — its entry's index plus one,
    /// zero for none — rather than a binary search: every character of every text compared is looked up in all
    /// three. 128 KB apiece, where the searches were most of a comparison's time.</remarks>
    private sealed record Table(
        ushort[] Slots, byte[] Lengths, int[] PrimaryOffsets, byte[] Primaries, byte[] Secondaries,
        ushort[] InlineSlots, byte[] InlineCodes,
        ushort[] KanaSlots, byte[] KanaSounds, byte[] KanaSecondaries, byte[] KanaSmall,
        byte[] KanaVowels)
    {
        public byte[]?[] PrimarySlices { get; } = new byte[Lengths.Length][];
    }

    private static ushort[] SlotsOf(char[] codePoints)
    {
        var slots = new ushort[char.MaxValue + 1];
        for (int i = 0; i < codePoints.Length; i++)
            slots[codePoints[i]] = checked((ushort)(i + 1));
        return slots;
    }

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

        return new Table(SlotsOf(codePoints), lengths, offsets, primaries, secondaries, SlotsOf(inlineCodePoints), inlineCodes,
                         SlotsOf(kanaCodePoints), kanaSounds, kanaSecondaries, kanaSmall, kanaVowels);
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