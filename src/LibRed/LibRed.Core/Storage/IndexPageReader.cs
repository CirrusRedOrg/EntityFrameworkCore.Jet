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

        var ranges = new List<(int Start, int End)>();
        int start = 0;
        for (int i = EntryMaskOffset; i < EntryDataOffset; i++)
        {
            byte mask = buffer.ReadByte(i);
            for (int bit = 0; bit < 8; bit++)
            {
                if ((mask & (1 << bit)) == 0) continue;
                int end = (i - EntryMaskOffset) * 8 + bit;
                if (end - start < 4 || EntryDataOffset + end > buffer.Length)
                    throw new InvalidDataException(
                        $"Index page {pageNumber} entry [{start}, {end}) cannot contain its 4-byte trailer.");
                ranges.Add((start, end));
                start = end;
            }
        }

        int compressed = buffer.ReadUInt16(CompressedByteCountOffset);
        if (ranges.Count == 0 && compressed != 0)
            throw new InvalidDataException($"Empty index page {pageNumber} declares a compressed prefix.");
        if (ranges.Count > 0 && compressed > ranges[0].End - ranges[0].Start - 4)
            throw new InvalidDataException(
                $"Index page {pageNumber} compressed prefix {compressed} exceeds its first key.");

        if (type == PageType.IntermediateIndexPage)
            foreach ((_, int end) in ranges)
                ValidatePageNumber(channel, ReadInt32BigEndian(buffer, EntryDataOffset + end - 4), "node child");

        return new CheckedIndexPage(buffer, type, owner, previous, next, tail, compressed, ranges);
    }

    public static int ReadInt32BigEndian(PageBuffer page, int offset) =>
        BinaryPrimitives.ReadInt32BigEndian(page.Slice(offset, 4));

    /// <summary>Decodes a checked page's entries in order, decompressing each entry's shared prefix: the first
    /// entry is stored whole and its leading <c>CompressedByteCount</c> bytes are the prefix reapplied to every
    /// following entry. Yields the full key bytes and the 4-byte big-endian trailer (a leaf entry's row pointer
    /// or a node entry's child page). Shared by the cursor's leaf enumeration and the writer's parse so the
    /// prefix rule lives in exactly one place.</summary>
    public static IEnumerable<(byte[] Key, int Trailer)> DecodeEntries(CheckedIndexPage page)
    {
        byte[] prefix = [];
        bool first = true;
        foreach ((int start, int end) in page.EntryRanges)
        {
            int trailer = ReadInt32BigEndian(page.Buffer, EntryDataOffset + end - 4);
            ReadOnlySpan<byte> stored = page.Buffer.Slice(EntryDataOffset + start, end - start - 4);
            byte[] key = first ? stored.ToArray() : Concat(prefix, stored);
            if (first) { prefix = key[..page.CompressedByteCount]; first = false; }
            yield return (key, trailer);
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
