using System.Buffers.Binary;
using LibRed.IO;
using LibRed.Pages;

namespace LibRed.Storage;

internal sealed record CheckedIndexPage(
    PageBuffer Buffer, PageType Type, int Owner, int Previous, int Next, int Tail,
    int CompressedByteCount, IReadOnlyList<(int Start, int End)> EntryRanges);

/// <summary>Checks the common header, owner, pointers, and entry boundaries of an index page.</summary>
internal static class IndexPageReader
{
    internal const int OwnerOffset = 0x04;
    internal const int PrevPageOffset = 0x0C;
    internal const int NextPageOffset = 0x10;
    internal const int ChildTailOffset = 0x14;
    internal const int CompressedByteCountOffset = 0x18;
    internal const int EntryMaskOffset = 0x1B;
    internal const int EntryDataOffset = 0x1E0;

    public static CheckedIndexPage Read(PageChannel channel, int pageNumber, int? expectedOwner)
    {
        ValidatePageNumber(channel, pageNumber, "index page");
        PageBuffer buffer = channel.ReadPageShared(pageNumber);
        var type = (PageType)buffer.ReadByte(0);
        if (type is not (PageType.LeafIndexPage or PageType.IntermediateIndexPage))
            throw new InvalidDataException(
                $"Page {pageNumber} is type 0x{(byte)type:X2}, not an index page (0x03/0x04).");

        int owner = buffer.ReadInt32(OwnerOffset);
        if (expectedOwner is not null && owner != expectedOwner)
            throw new InvalidDataException(
                $"Index page {pageNumber} belongs to TDEF {owner}, not TDEF {expectedOwner}.");

        int previous = buffer.ReadInt32(PrevPageOffset);
        int next = buffer.ReadInt32(NextPageOffset);
        int tail = buffer.ReadInt32(ChildTailOffset);
        if (type == PageType.LeafIndexPage)
        {
            ValidateOptionalPageNumber(channel, previous, "previous leaf");
            ValidateOptionalPageNumber(channel, next, "next leaf");
        }
        else
        {
            ValidatePageNumber(channel, tail, "node child-tail");
        }

        // The shared prefix is measured across the WHOLE entry, trailer included — not just the key. Where
        // many rows share a key the trailer's leading bytes are common too (consecutive rows on one data
        // page), so ACE compresses those away and the stored remainder can be as little as two bytes. Size
        // limits therefore apply to the reconstructed entry, never to what is stored.
        int compressed = buffer.ReadUInt16(CompressedByteCountOffset);

        var ranges = new List<(int Start, int End)>();
        int start = 0;
        for (int i = EntryMaskOffset; i < EntryDataOffset; i++)
        {
            byte mask = buffer.ReadByte(i);
            for (int bit = 0; bit < 8; bit++)
            {
                if ((mask & (1 << bit)) == 0) continue;
                int end = (i - EntryMaskOffset) * 8 + bit;
                if (EntryDataOffset + end > buffer.Length)
                    throw new InvalidDataException(
                        $"Index page {pageNumber} entry [{start}, {end}) runs past the end of the page.");
                // The first entry is stored whole; every later one is the prefix plus what is stored.
                int length = ranges.Count == 0 ? end - start : compressed + (end - start);
                if (length < 4)
                    throw new InvalidDataException(
                        $"Index page {pageNumber} entry [{start}, {end}) reconstructs to {length} bytes, " +
                        "too few for its 4-byte trailer.");
                ranges.Add((start, end));
                start = end;
            }
        }

        if (ranges.Count == 0 && compressed != 0)
            throw new InvalidDataException($"Empty index page {pageNumber} declares a compressed prefix.");
        if (ranges.Count > 0 && compressed > ranges[0].End - ranges[0].Start)
            throw new InvalidDataException(
                $"Index page {pageNumber} compressed prefix {compressed} exceeds its first entry.");

        var page = new CheckedIndexPage(buffer, type, owner, previous, next, tail, compressed, ranges);

        // Node children have to be read from the RECONSTRUCTED entry, for the same reason.
        if (type == PageType.IntermediateIndexPage)
            foreach ((_, int child) in DecodeEntries(page))
                ValidatePageNumber(channel, child, "node child");

        return page;
    }

    public static int ReadInt32BigEndian(PageBuffer page, int offset) =>
        BinaryPrimitives.ReadInt32BigEndian(page.Slice(offset, 4));

    /// <summary>Decodes a checked page's entries in order, decompressing each entry's shared prefix: the first
    /// entry is stored whole and its leading <c>CompressedByteCount</c> bytes are the prefix reapplied to every
    /// following entry. Yields the full key bytes and the 4-byte big-endian trailer (a leaf entry's row pointer
    /// or a node entry's child page). Shared by the cursor's leaf enumeration and the writer's parse so the
    /// prefix rule lives in exactly one place.
    /// <para>
    /// The prefix covers the entry <b>whole</b>, so it can reach into the trailer — with many equal keys the
    /// rows are consecutive on one data page and share the trailer's leading bytes too. Both the key and the
    /// trailer are therefore taken from the reconstructed entry, never from the stored bytes.
    /// </para></summary>
    public static IEnumerable<(byte[] Key, int Trailer)> DecodeEntries(CheckedIndexPage page)
    {
        byte[] prefix = [];
        bool first = true;
        foreach ((int start, int end) in page.EntryRanges)
        {
            ReadOnlySpan<byte> stored = page.Buffer.Slice(EntryDataOffset + start, end - start);
            byte[] entry = first ? stored.ToArray() : Concat(prefix, stored);
            if (first) { prefix = entry[..page.CompressedByteCount]; first = false; }
            int trailer = BinaryPrimitives.ReadInt32BigEndian(entry.AsSpan(entry.Length - 4));
            yield return (entry[..^4], trailer);
        }
    }

    private static byte[] Concat(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        var result = new byte[a.Length + b.Length];
        a.CopyTo(result);
        b.CopyTo(result.AsSpan(a.Length));
        return result;
    }

    private static void ValidateOptionalPageNumber(PageChannel channel, int pageNumber, string kind)
    {
        if (pageNumber != 0) ValidatePageNumber(channel, pageNumber, kind);
    }

    private static void ValidatePageNumber(PageChannel channel, int pageNumber, string kind)
    {
        if (pageNumber <= 0 || pageNumber >= channel.PageCount)
            throw new InvalidDataException(
                $"Index {kind} pointer {pageNumber} is outside the file's 1..{channel.PageCount - 1} range.");
    }
}
