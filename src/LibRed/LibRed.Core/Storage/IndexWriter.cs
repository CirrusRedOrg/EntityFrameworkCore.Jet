using System.Buffers.Binary;
using LibRed.Catalog;
using LibRed.IO;
using LibRed.Pages;

namespace LibRed.Storage;

/// <summary>
/// Adds an entry to an index B-tree to keep it consistent after a row insert. This first cut
/// handles a single-leaf index (root page is a leaf): it slots the new key into the leaf in
/// order and rewrites the page uncompressed. Multi-level descent and leaf splitting are not yet
/// implemented and throw, as do indexes whose key columns need (unsupported) text collation.
/// </summary>
public sealed class IndexWriter(PageChannel channel)
{
    private const int FreeSpaceOffset = 0x02;
    private const int CompressedByteCountOffset = 0x18;
    private const int EntryMaskOffset = 0x1B;
    private const int EntryDataOffset = 0x1E0;

    private readonly PageChannel _channel = channel;

    public void AddEntry(IndexDef index, object?[] values, RowId rowId)
    {
        byte[] page = _channel.ReadPage(index.RootPage).Span.ToArray();
        if ((PageType)page[0] != PageType.LeafIndexPage)
            throw new NotSupportedException("Multi-level index insertion is not implemented yet (root is a node page).");

        byte[] key = IndexKeyEncoder.Encode(index.Columns, values);
        int pointer = (rowId.Page << 8) | rowId.Row;

        var entries = ParseLeafEntries(page);

        // Insert in order: by key bytes, then by the row pointer (the non-unique tiebreaker).
        int pos = 0;
        while (pos < entries.Count && CompareEntry(entries[pos], (key, pointer)) < 0) pos++;
        entries.Insert(pos, (key, pointer));

        if (!TryRewriteLeaf(page, entries))
            throw new NotSupportedException("Leaf page is full — index node splitting is not implemented yet.");

        _channel.WritePage(index.RootPage, page);
    }

    private static List<(byte[] Key, int Pointer)> ParseLeafEntries(byte[] page)
    {
        int compress = BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(CompressedByteCountOffset, 2));
        var entries = new List<(byte[], int)>();
        byte[] prefix = [];
        bool first = true;

        foreach ((int start, int end) in EntryRanges(page))
        {
            int entryStart = EntryDataOffset + start;
            int pointer = ReadInt32BigEndian(page, EntryDataOffset + end - 4);
            ReadOnlySpan<byte> storedKey = page.AsSpan(entryStart, end - start - 4);

            byte[] key = first ? storedKey.ToArray() : Concat(prefix, storedKey);
            if (first) { prefix = key[..compress]; first = false; }

            entries.Add((key, pointer));
        }

        return entries;
    }

    /// <summary>Rewrites the leaf in place (uncompressed). Returns false if the entries overflow the page.</summary>
    private bool TryRewriteLeaf(byte[] page, List<(byte[] Key, int Pointer)> entries)
    {
        int pageSize = _channel.PageSize;

        // Preserve the header (owner, prev/next leaf links) but drop prefix compression and
        // clear the entry mask + data region before re-emitting.
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(CompressedByteCountOffset, 2), 0);
        Array.Clear(page, EntryMaskOffset, EntryDataOffset - EntryMaskOffset);
        Array.Clear(page, EntryDataOffset, pageSize - EntryDataOffset);

        int pos = EntryDataOffset;
        foreach ((byte[] key, int pointer) in entries)
        {
            int entryLength = key.Length + 4;
            if (pos + entryLength > pageSize) return false;

            key.CopyTo(page.AsSpan(pos));
            WriteInt32BigEndian(page, pos + key.Length, pointer);
            pos += entryLength;

            // The entry mask marks each entry's end offset (relative to the data region).
            int end = pos - EntryDataOffset;
            page[EntryMaskOffset + (end >> 3)] |= (byte)(1 << (end & 7));
        }

        int used = pos - EntryDataOffset;
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(FreeSpaceOffset, 2), (ushort)(pageSize - EntryDataOffset - used));
        return true;
    }

    private static int CompareEntry((byte[] Key, int Pointer) a, (byte[] Key, int Pointer) b)
    {
        int n = Math.Min(a.Key.Length, b.Key.Length);
        for (int i = 0; i < n; i++)
            if (a.Key[i] != b.Key[i]) return a.Key[i] - b.Key[i]; // unsigned bytes
        if (a.Key.Length != b.Key.Length) return a.Key.Length - b.Key.Length;
        return a.Pointer - b.Pointer;
    }

    private static IEnumerable<(int Start, int End)> EntryRanges(byte[] page)
    {
        int start = 0;
        for (int i = EntryMaskOffset; i < EntryDataOffset; i++)
        {
            byte mask = page[i];
            if (mask == 0) continue;
            for (int bit = 0; bit < 8; bit++)
            {
                if ((mask & (1 << bit)) == 0) continue;
                int end = (i - EntryMaskOffset) * 8 + bit;
                yield return (start, end);
                start = end;
            }
        }
    }

    private static byte[] Concat(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        var result = new byte[a.Length + b.Length];
        a.CopyTo(result);
        b.CopyTo(result.AsSpan(a.Length));
        return result;
    }

    private static int ReadInt32BigEndian(byte[] page, int offset) =>
        (page[offset] << 24) | (page[offset + 1] << 16) | (page[offset + 2] << 8) | page[offset + 3];

    private static void WriteInt32BigEndian(byte[] page, int offset, int value)
    {
        page[offset] = (byte)(value >> 24);
        page[offset + 1] = (byte)(value >> 16);
        page[offset + 2] = (byte)(value >> 8);
        page[offset + 3] = (byte)value;
    }
}
