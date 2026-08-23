using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Reflection;

namespace LibRed.Storage;

/// <summary>
/// Index-key weights for the Access-2010+ <b>"General"</b> text collation (sort-order version 1).
///
/// <para>Unlike General-Legacy (<see cref="JetTextCollation"/>), whose weights are a Jet-era compaction into
/// one byte per character, v1 uses the Windows NLS weights <b>verbatim</b>: the primary is the two-byte
/// <c>(Script Member, Alphabetic Weight)</c> pair and the secondary is the <c>Diacritic Weight</c>, exactly as
/// published in Microsoft's sorting weight tables. The <c>Case Weight</c> is dropped, which is why case and
/// character width both fold (a full-width <c>Ａ</c> and <c>A</c> differ only in that discarded weight).</para>
///
/// <para>The table is the <b>Windows Server 2008</b> one, frozen: reconstructing measured ACE v1 keys scores
/// 25/25 against it and lower against every other published version. See <c>tools/sortkey-table/generate.ps1</c>
/// for provenance and how the embedded resource is built.</para>
/// </summary>
internal static class JetTextCollationV1
{
    private const byte EndPrimary = 0x01;
    private const byte EndKey = 0x00;
    private const byte InlineStart = 0x80;
    private const byte DefaultSecondary = 0x02;

    /// <summary>Script member 6 is Windows' "word sort" class: characters that carry no primary weight but are
    /// recorded positionally so <c>co-op</c> stays beside <c>coop</c>. The apostrophe and hyphen live here
    /// (their <c>0x80</c>/<c>0x82</c> inline codes are simply their Alphabetic Weights), which is why exactly
    /// those two are special — it is the platform's rule, not an Access one.</summary>
    private const byte WordSortScriptMember = 6;

    private static readonly Lazy<WeightTable> Table = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// Appends the collation key body for <paramref name="value"/> — everything after the start flag. Returns
    /// false if any character has no weight in the table (the caller reports it rather than emitting a key
    /// that would sort wrongly).
    /// </summary>
    public static bool TryEncode(string value, List<byte> output)
    {
        WeightTable table = Table.Value;
        ReadOnlySpan<char> text = value.AsSpan().TrimEnd(' ');

        var primaries = new List<byte>();
        var secondaries = new List<byte>();
        // Position is counted in primary *weights*, not bytes. In v0 the two coincide (one byte per weight);
        // here a weight is two bytes, and ACE still counts weights — verified against ACE (`O'Brien` puts the
        // apostrophe at 0x0B = 0x07 + 4x1 in both orders, though v1 has emitted twice as many bytes by then).
        var inline = new List<(int Position, byte ScriptMember, byte AlphabeticWeight)>();

        foreach (char character in text)
        {
            if (table.TryExpand(character, out char[]? sequence))
            {
                foreach (char expanded in sequence)
                    if (!Append(expanded)) return false;
            }
            else if (!Append(character))
            {
                return false;
            }
        }

        bool Append(char character)
        {
            if (!table.TryGetWeight(character, out byte scriptMember, out byte alphabetic, out byte diacritic))
                return false;

            // A wholly zero entry is an ignorable with no record at all (e.g. the soft hyphen, which v0
            // instead records inline as 0x83 — a real difference between the two weight tables).
            if (scriptMember == 0 && alphabetic == 0) return true;

            if (scriptMember == WordSortScriptMember)
            {
                inline.Add((primaries.Count / 2, scriptMember, alphabetic));
                return true;
            }

            primaries.Add(scriptMember);
            primaries.Add(alphabetic);
            secondaries.Add(diacritic);
            return true;
        }

        output.AddRange(primaries);
        output.Add(EndPrimary);

        // Secondary section: emitted up to and including the last character carrying a non-default accent.
        int lastAccent = secondaries.FindLastIndex(weight => weight != DefaultSecondary);
        for (int i = 0; i <= lastAccent; i++) output.Add(secondaries[i]);

        if (inline.Count > 0)
        {
            output.Add(EndPrimary);
            output.Add(EndPrimary);
            output.Add(EndPrimary);
            foreach ((int position, byte scriptMember, byte alphabetic) in inline)
            {
                output.Add(InlineStart);
                output.Add((byte)(0x07 + 4 * position));
                output.Add(scriptMember);
                output.Add(alphabetic);
            }
        }

        output.Add(EndKey);
        return true;
    }

    private static WeightTable Load()
    {
        using Stream stream = typeof(JetTextCollationV1).Assembly
            .GetManifestResourceStream("LibRed.Resources.SortKeyTableV1.bin")
            ?? throw new InvalidOperationException("The v1 sorting weight table resource is missing from the assembly.");

        Span<byte> header = stackalloc byte[8];
        stream.ReadExactly(header);
        int weightCount = BinaryPrimitives.ReadInt32LittleEndian(header);
        int expansionCount = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);

        byte[] deltas = ReadSection(stream);
        byte[] scriptMembers = ReadSection(stream);
        byte[] alphabetics = ReadSection(stream);
        byte[] diacritics = ReadSection(stream);
        byte[] expansionBytes = ReadSection(stream);

        if (scriptMembers.Length != weightCount || alphabetics.Length != weightCount || diacritics.Length != weightCount)
            throw new InvalidDataException("The v1 sorting weight table resource is inconsistent with its header.");

        var codePoints = new ushort[weightCount];
        int offset = 0, current = 0;
        for (int i = 0; i < weightCount; i++)
        {
            current += ReadVarInt(deltas, ref offset);
            codePoints[i] = (ushort)current;
        }

        var expansions = new Dictionary<char, char[]>(expansionCount);
        offset = 0; current = 0;
        for (int i = 0; i < expansionCount; i++)
        {
            current += ReadVarInt(expansionBytes, ref offset);
            int length = expansionBytes[offset++];
            var sequence = new char[length];
            for (int j = 0; j < length; j++)
            {
                sequence[j] = (char)BinaryPrimitives.ReadUInt16LittleEndian(expansionBytes.AsSpan(offset));
                offset += 2;
            }
            expansions[(char)current] = sequence;
        }

        return new WeightTable(codePoints, scriptMembers, alphabetics, diacritics, expansions);
    }

    private static byte[] ReadSection(Stream stream)
    {
        Span<byte> length = stackalloc byte[4];
        stream.ReadExactly(length);
        var compressed = new byte[BinaryPrimitives.ReadInt32LittleEndian(length)];
        stream.ReadExactly(compressed);

        using var source = new MemoryStream(compressed);
        using var inflate = new ZLibStream(source, CompressionMode.Decompress);
        using var target = new MemoryStream();
        inflate.CopyTo(target);
        return target.ToArray();
    }

    private static int ReadVarInt(byte[] data, ref int offset)
    {
        int value = 0, shift = 0;
        while (true)
        {
            byte b = data[offset++];
            value |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) return value;
            shift += 7;
        }
    }

    /// <summary>Code points sorted ascending with their weights in parallel arrays — a binary search over
    /// ~58k entries, rather than a dictionary, to keep the table near 300 KB resident instead of several MB.</summary>
    private sealed class WeightTable(
        ushort[] codePoints, byte[] scriptMembers, byte[] alphabetics, byte[] diacritics,
        Dictionary<char, char[]> expansions)
    {
        public bool TryGetWeight(char character, out byte scriptMember, out byte alphabetic, out byte diacritic)
        {
            int index = Array.BinarySearch(codePoints, (ushort)character);
            if (index < 0)
            {
                scriptMember = alphabetic = diacritic = 0;
                return false;
            }
            scriptMember = scriptMembers[index];
            alphabetic = alphabetics[index];
            diacritic = diacritics[index];
            return true;
        }

        /// <summary>The character's expansion (e.g. <c>ß</c> → <c>s</c>,<c>s</c>) when it has one. Deliberately
        /// not "return the character in a one-element buffer": a shared buffer would race between the
        /// concurrent readers the engine now allows, and allocating one per character would be worse.</summary>
        public bool TryExpand(char character, [NotNullWhen(true)] out char[]? sequence) =>
            expansions.TryGetValue(character, out sequence);
    }
}
