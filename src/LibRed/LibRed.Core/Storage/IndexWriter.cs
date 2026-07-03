using System.Buffers.Binary;
using LibRed.Catalog;
using LibRed.IO;
using LibRed.Pages;

namespace LibRed.Storage;

/// <summary>
/// Adds an entry to an index B-tree to keep it consistent after a row insert. Descends a multi-level
/// tree from the root to the target leaf (following node separators) and slots the new key into that
/// leaf in order, rewriting the page uncompressed. Because it descends into the first child whose
/// separator ≥ the new key, the leaf's maximum key never changes, so no parent separator needs
/// updating. Leaf <b>splitting</b> (a full leaf) is not implemented and throws, as do indexes whose
/// key columns need (unsupported) text collation.
/// </summary>
public sealed class IndexWriter(PageChannel channel)
{
    private const int FreeSpaceOffset = 0x02;
    private const int ChildTailOffset = 0x14;   // node page: the rightmost child (not referenced by an entry)
    private const int CompressedByteCountOffset = 0x18;
    private const int EntryMaskOffset = 0x1B;
    private const int EntryDataOffset = 0x1E0;

    private readonly PageChannel _channel = channel;

    public void AddEntry(IndexDef index, object?[] values, RowId rowId)
    {
        byte[] key = IndexKeyEncoder.Encode(index.Columns, values);
        int pointer = (rowId.Page << 8) | rowId.Row;

        // The full leaf-entry bytes (key + 4-byte pointer) are what node separators store, so compare
        // against them to descend to the correct leaf.
        byte[] fullKey = new byte[key.Length + 4];
        key.CopyTo(fullKey, 0);
        WriteInt32BigEndian(fullKey, key.Length, pointer);

        int leafPage = DescendToLeaf(index.RootPage, fullKey);
        byte[] page = _channel.ReadPage(leafPage).Span.ToArray();

        var entries = ParseLeafEntries(page);

        // Insert in order: by key bytes, then by the row pointer (the non-unique tiebreaker).
        int pos = 0;
        while (pos < entries.Count && CompareEntry(entries[pos], (key, pointer)) < 0) pos++;
        entries.Insert(pos, (key, pointer));

        if (!TryRewriteLeaf(page, entries))
            throw new NotSupportedException("Leaf page is full — index node splitting is not implemented yet.");

        _channel.WritePage(leafPage, page);
    }

    /// <summary>Descends from a (possibly node) root to the leaf that should hold <paramref name="fullKey"/>.</summary>
    private int DescendToLeaf(int pageNumber, byte[] fullKey)
    {
        while (true)
        {
            byte[] page = _channel.ReadPage(pageNumber).Span.ToArray();
            if ((PageType)page[0] == PageType.LeafIndexPage) return pageNumber;

            // Node page: the first entry whose separator ≥ the key owns the child; else the tail child.
            // (Entry child pointers are big-endian; the tail child at 0x14 is little-endian.)
            int child = BinaryPrimitives.ReadInt32LittleEndian(page.AsSpan(ChildTailOffset, 4));
            byte[] prefix = [];
            bool first = true;
            foreach ((int start, int end) in EntryRanges(page))
            {
                int childPage = ReadInt32BigEndian(page, EntryDataOffset + end - 4);
                ReadOnlySpan<byte> stored = page.AsSpan(EntryDataOffset + start, end - start - 4);
                byte[] separator = first ? stored.ToArray() : Concat(prefix, stored);
                if (first) { prefix = separator[..BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(CompressedByteCountOffset, 2))]; first = false; }
                if (CompareBytes(separator, fullKey) >= 0) { child = childPage; break; }
            }
            pageNumber = child;
        }
    }

    private static int CompareBytes(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++)
            if (a[i] != b[i]) return a[i] - b[i];
        return a.Length - b.Length;
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

    /// <summary>
    /// Rewrites the leaf in place with prefix compression (the first entry stored whole, the shared
    /// leading bytes omitted from the rest) — matching Access, so a near-full compressed leaf still fits.
    /// Returns false if the entries overflow the page (a split, not implemented, would be needed).
    /// </summary>
    private bool TryRewriteLeaf(byte[] page, List<(byte[] Key, int Pointer)> entries)
    {
        int pageSize = _channel.PageSize;

        // Bytes shared by every entry (entries are sorted, so the first and last bound the common prefix).
        int compress = entries.Count == 0 ? 0 : CommonPrefixLength(entries[0].Key, entries[^1].Key);

        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(CompressedByteCountOffset, 2), (ushort)compress);
        Array.Clear(page, EntryMaskOffset, EntryDataOffset - EntryMaskOffset);
        Array.Clear(page, EntryDataOffset, pageSize - EntryDataOffset);

        int pos = EntryDataOffset;
        bool first = true;
        foreach ((byte[] key, int pointer) in entries)
        {
            ReadOnlySpan<byte> stored = first ? key : key.AsSpan(compress); // omit the shared prefix
            first = false;

            int entryLength = stored.Length + 4;
            if (pos + entryLength > pageSize) return false;

            stored.CopyTo(page.AsSpan(pos));
            WriteInt32BigEndian(page, pos + stored.Length, pointer);
            pos += entryLength;

            int end = pos - EntryDataOffset; // the entry mask marks each entry's end offset
            page[EntryMaskOffset + (end >> 3)] |= (byte)(1 << (end & 7));
        }

        int used = pos - EntryDataOffset;
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(FreeSpaceOffset, 2), (ushort)(pageSize - EntryDataOffset - used));
        return true;
    }

    private static int CommonPrefixLength(byte[] a, byte[] b)
    {
        int n = Math.Min(a.Length, b.Length), i = 0;
        while (i < n && a[i] == b[i]) i++;
        return i;
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
