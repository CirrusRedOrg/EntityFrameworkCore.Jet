using System.IO.Compression;

namespace LibRed.Storage;

/// <summary>
/// The characters where ACE's version-1 weights disagree with the published table LibRed embeds.
/// </summary>
/// <remarks>
/// v1's primaries are the Windows NLS <c>(Script Member, Alphabetic Weight)</c> pair, and the table was
/// identified as <b>Windows Server 2008</b> by reconstructing measured ACE keys — 25 of 25 against every
/// published version. That held for what it was tested on, Latin and symbols, and it does not hold
/// everywhere: ACE gives Balinese and Canadian syllabics <i>Latin</i> weights, and differs on the Arabic
/// harakat and several ligature blocks. Those are scripts added or reweighted after Server 2008, so ACE's
/// real table is not exactly the file we parse.
/// <para>
/// Rather than guess which NLS revision ACE carries, the disagreements are measured and embedded:
/// <c>SortKeyTableV1OverrideGeneratorTest</c> (<c>LIBRED_GENERATE_V1=1</c>) encodes every BMP character
/// through ACE and records the weights implied wherever the result differs. 446 characters, 1.2 KB — the
/// same answer v0 needed, at a fraction of the size, because v1 is right about the other 57,594.
/// </para>
/// <para>
/// An entry is a <b>sequence</b> of weights, since one character can imply several: ACE encodes
/// <c>U+1B08</c> as <c>0E02 0E21</c>. They are appended verbatim, bypassing the Han and Hangul and zero-AW
/// rules, because an override states the finished bytes rather than a table entry to interpret.
/// </para>
/// </remarks>
internal static class JetTextCollationV1Overrides
{
    /// <summary>
    /// Suppresses the overrides, so the encoder falls back to the published table alone.
    /// </summary>
    /// <remarks>
    /// Only the generator sets this, and it must. The resource records where the encoder <i>disagrees</i> with
    /// ACE, so measuring an encoder that already consults it would find no disagreements and write an empty
    /// file — the measurement would erase its own subject. The generator therefore takes the bare table.
    /// </remarks>
    internal static bool Suppressed { get; set; }

    /// <summary>
    /// The primary and secondary bytes ACE gives this character, or false when it agrees with the table.
    /// </summary>
    public static bool TryGet(char c, out ReadOnlySpan<byte> primaries, out ReadOnlySpan<byte> secondaries)
    {
        primaries = secondaries = default;
        if (Suppressed) return false;
        Table table = Loaded.Value;
        int index = Array.BinarySearch(table.CodePoints, c);
        if (index < 0) return false;
        primaries = table.PrimaryBytes.AsSpan(table.PrimaryOffsets[index], table.PrimaryLengths[index]);
        secondaries = table.SecondaryBytes.AsSpan(table.SecondaryOffsets[index], table.SecondaryLengths[index]);
        return true;
    }

    /// <summary>
    /// Whether ACE contributes nothing at all for this character — an empty key, <c>7F 01 00</c>.
    /// </summary>
    /// <remarks>
    /// The published table has no entry for these at all, so without the measured set the encoder refuses
    /// them. They are stored as runs rather than weights, since the only fact worth keeping is membership:
    /// 5,029 characters collapse into a few hundred ranges.
    /// </remarks>
    public static bool IsIgnorable(char c)
    {
        if (Suppressed) return false;
        int[] starts = Loaded.Value.IgnorableStarts;
        int index = Array.BinarySearch(starts, (int)c);
        if (index >= 0) return true;
        index = ~index - 1;
        return index >= 0 && c < starts[index] + Loaded.Value.IgnorableLengths[index];
    }

    private sealed record Table(
        char[] CodePoints,
        byte[] PrimaryLengths, int[] PrimaryOffsets, byte[] PrimaryBytes,
        byte[] SecondaryLengths, int[] SecondaryOffsets, byte[] SecondaryBytes,
        int[] IgnorableStarts, int[] IgnorableLengths);

    private static readonly Lazy<Table> Loaded = new(Load);

    private static Table Load()
    {
        using Stream stream = typeof(JetTextCollationV1Overrides).Assembly
            .GetManifestResourceStream("LibRed.Resources.SortKeyTableV1Overrides.bin")
            ?? throw new InvalidOperationException("The v1 override resource is missing from the assembly.");

        var reader = new BinaryReader(stream);
        int count = reader.ReadInt32();
        int rangeCount = reader.ReadInt32();
        byte[] deltas = ReadStream(reader);
        byte[] primaryLengths = ReadStream(reader);
        byte[] secondaryLengths = ReadStream(reader);
        byte[] primaryBytes = ReadStream(reader);
        byte[] secondaryBytes = ReadStream(reader);
        byte[] rangeStarts = ReadStream(reader);
        byte[] rangeLengths = ReadStream(reader);

        var codePoints = new char[count];
        var primaryOffsets = new int[count];
        var secondaryOffsets = new int[count];
        int codePoint = 0, cursor = 0, primary = 0, secondary = 0;
        for (int i = 0; i < count; i++)
        {
            codePoint += ReadVarInt(deltas, ref cursor);
            codePoints[i] = (char)codePoint;
            primaryOffsets[i] = primary;
            secondaryOffsets[i] = secondary;
            primary += primaryLengths[i];
            secondary += secondaryLengths[i];
        }
        var starts = new int[rangeCount];
        var lengths = new int[rangeCount];
        int start = 0, startCursor = 0, lengthCursor = 0;
        for (int i = 0; i < rangeCount; i++)
        {
            start += ReadVarInt(rangeStarts, ref startCursor);
            starts[i] = start;
            lengths[i] = ReadVarInt(rangeLengths, ref lengthCursor);
        }

        return new Table(
            codePoints,
            primaryLengths, primaryOffsets, primaryBytes,
            secondaryLengths, secondaryOffsets, secondaryBytes,
            starts, lengths);
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
