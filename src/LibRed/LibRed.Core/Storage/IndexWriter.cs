using System.Buffers.Binary;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;

namespace LibRed.Storage;

/// <summary>
/// Maintains an index B-tree on row insert: descends from the root to the target leaf, inserts the key,
/// and — when a page overflows — <b>splits</b> it, promoting a separator into the parent and propagating
/// splits up the tree (growing a new root when the root itself splits, and repointing the index-data
/// block's root). Leaf pages keep their doubly-linked prev/next chain; pages are written with prefix
/// compression. Indexes whose key columns need unsupported (text/binary) collation still throw.
/// </summary>
/// <remarks>
/// A page entry is <c>[key bytes][4-byte big-endian trailer]</c>: on a leaf the trailer is the row
/// pointer (<c>page&lt;&lt;8 | row</c>) and the key is the column key; on a node the trailer is the child
/// page and the key is a full leaf key (column key ++ row pointer) used as the separator = the maximum
/// key of that child. See §10.
/// </remarks>
public sealed class IndexWriter(PageChannel channel, TableDef table)
{
    private const int FreeSpaceOffset = 0x02;
    private const int OwnerOffset = 0x04;
    private const int PrevPageOffset = 0x0C;     // leaf page: previous (lower-key) leaf
    private const int NextPageOffset = 0x10;     // leaf page: next (higher-key) leaf — Access walks this for COUNT/scan
    private const int ChildTailOffset = 0x14;
    private const int CompressedByteCountOffset = 0x18;
    private const int LevelOffset = 0x1A;       // 0 on a leaf, its height above the leaves on a node
    private const int EntryMaskOffset = 0x1B;
    private const int EntryDataOffset = 0x1E0;
    private const int RootPageInBlockOffset = 0x26; // within the 52-byte index-data block

    private readonly PageChannel _channel = channel;
    private readonly TableDef _table = table;
    private readonly PageAllocator _allocator = new(channel);

    private readonly record struct Entry(byte[] Key, int Trailer);

    public void AddEntry(IndexDef index, object?[] values, RowId rowId)
    {
        byte[] key = IndexKeyEncoder.Encode(index.Columns, values);
        int pointer = (rowId.Page << 8) | rowId.Row;
        byte[] fullKey = WithTrailer(key, pointer); // key ++ 4-byte pointer (what node separators store)

        var path = Descend(index.RootPage, fullKey); // [root, …, leaf] page numbers
        InsertIntoLeaf(index, path, key, pointer);
    }

    /// <summary>
    /// Whether the index already contains an entry with this key (ignoring the row pointer) — used to enforce
    /// a UNIQUE/PRIMARY index on insert. Descends to the leaf the key belongs in (with the smallest pointer,
    /// so we land at/just-before any equal-key entry) and scans forward while keys could still match. The
    /// caller skips null keys (Jet allows multiple nulls in a unique index — verified vs ACE).
    /// </summary>
    public bool KeyExists(IndexDef index, object?[] values, int? excludePointer = null)
    {
        byte[] key = IndexKeyEncoder.Encode(index.Columns, values);
        int leaf = Descend(index.RootPage, WithTrailer(key, 0))[^1];
        while (leaf != 0)
        {
            byte[] page = _channel.ReadPage(leaf).Span.ToArray();
            (List<Entry> entries, _) = Parse(page);
            foreach (Entry e in entries)
            {
                int cmp = CompareBytes(e.Key, key);
                if (cmp > 0) return false;   // sorted past where the key would be — it's absent
                // Same key held by a *different* row (for an UPDATE, the row's own entry is excluded).
                if (cmp == 0 && e.Trailer != excludePointer) return true;
            }
            leaf = ReadInt32Le(page, NextPageOffset); // all keys here sort below it — may continue on the next leaf
        }
        return false;
    }

    /// <summary>
    /// Moves a row's entry when its key changes: removes the old-key entry and inserts the new-key one (the
    /// row id is unchanged — Access rewrites rows in place). Honours WITH IGNORE NULL on each side (a row with
    /// a null key is simply absent from the index). Used by UPDATE of an indexed column.
    /// </summary>
    public void MoveEntry(IndexDef index, object?[] oldValues, object?[] newValues, RowId rowId)
    {
        if (!(index.IgnoreNulls && HasNullKey(index, oldValues))) RemoveEntry(index, oldValues, rowId);
        if (!(index.IgnoreNulls && HasNullKey(index, newValues))) AddEntry(index, newValues, rowId);
    }

    /// <summary>Removes a row's entry when the row is deleted — a no-op for a WITH IGNORE NULL index whose
    /// key the row was absent from (null key), otherwise <see cref="RemoveEntry"/>.</summary>
    public void DeleteEntry(IndexDef index, object?[] values, RowId rowId)
    {
        if (index.IgnoreNulls && HasNullKey(index, values)) return;
        RemoveEntry(index, values, rowId);
    }

    /// <summary>
    /// Removes a row's entry from the index. Descends to the entry's leaf, drops it, and rewrites the leaf.
    /// No rebalancing: an underfull or empty leaf is fine, and a stale separator (if the removed entry was a
    /// leaf's maximum) stays a valid upper bound, so later descents still route correctly — matching Access's
    /// lazy delete.
    /// </summary>
    public void RemoveEntry(IndexDef index, object?[] values, RowId rowId)
    {
        byte[] key = IndexKeyEncoder.Encode(index.Columns, values);
        int pointer = (rowId.Page << 8) | rowId.Row;

        List<int> path = Descend(index.RootPage, WithTrailer(key, pointer));
        int leafPage = path[^1];
        byte[] page = _channel.ReadPage(leafPage).Span.ToArray();
        (List<Entry> entries, _) = Parse(page);

        int idx = entries.FindIndex(e => e.Trailer == pointer && CompareBytes(e.Key, key) == 0);
        if (idx < 0)
            throw new InvalidOperationException(
                $"Index '{index.Name}': entry for row {rowId.Page}:{rowId.Row} was not found on leaf {leafPage}.");
        entries.RemoveAt(idx);

        int prev = ReadInt32Le(page, PrevPageOffset), next = ReadInt32Le(page, NextPageOffset);
        // Removing only shrinks the page, so Build never overflows.
        _channel.WritePage(leafPage, Build(PageType.LeafIndexPage, prev, next, tail: 0, level: 0, entries)!);
    }

    private static bool HasNullKey(IndexDef index, object?[] values) =>
        index.Columns.Any(c => values[c.Column.Index] is null or DBNull);

    /// <summary>Descends to the leaf that should hold the key, recording the path from the root.</summary>
    private List<int> Descend(int rootPage, byte[] fullKey)
    {
        var path = new List<int>();
        int pageNumber = rootPage;
        while (true)
        {
            path.Add(pageNumber);
            byte[] page = _channel.ReadPage(pageNumber).Span.ToArray();
            if ((PageType)page[0] == PageType.LeafIndexPage) return path;

            (List<Entry> entries, int tail) = Parse(page);
            int child = tail;
            foreach (Entry e in entries)
                if (CompareBytes(e.Key, fullKey) >= 0) { child = e.Trailer; break; }
            pageNumber = child;
        }
    }

    private void InsertIntoLeaf(IndexDef index, List<int> path, byte[] key, int pointer)
    {
        int leafPage = path[^1];
        byte[] page = _channel.ReadPage(leafPage).Span.ToArray();
        (List<Entry> entries, _) = Parse(page);

        // Insert in key order (key then pointer tiebreaker) — the full leaf key is key ++ pointer.
        byte[] fullKey = WithTrailer(key, pointer);
        int pos = 0;
        while (pos < entries.Count && CompareBytes(WithTrailer(entries[pos].Key, entries[pos].Trailer), fullKey) < 0) pos++;
        entries.Insert(pos, new Entry(key, pointer));

        int prev = ReadInt32Le(page, PrevPageOffset), next = ReadInt32Le(page, NextPageOffset);
        if (Build(PageType.LeafIndexPage, prev, next, tail: 0, level: 0, entries) is { } built)
        {
            _channel.WritePage(leafPage, built);
            return;
        }

        SplitAndPropagate(index, path, path.Count - 1, entries, PageType.LeafIndexPage, prev, next);
    }

    /// <summary>
    /// Splits the (leaf or node) page at <paramref name="level"/> into two, writes both, then promotes a
    /// separator into the parent — splitting parents in turn, or growing a new root at the top.
    /// </summary>
    private void SplitAndPropagate(IndexDef index, List<int> path, int level, List<Entry> entries,
        PageType type, int prev, int next)
    {
        int leftPage = path[level];
        int rightPage = _allocator.Allocate();
        int nodeLevel = path.Count - 1 - level; // height above the leaves of the page being split

        byte[] promoted;
        if (type == PageType.LeafIndexPage)
        {
            int mid = entries.Count / 2;
            var left = entries.GetRange(0, mid);
            var right = entries.GetRange(mid, entries.Count - mid);
            promoted = WithTrailer(left[^1].Key, left[^1].Trailer); // left's max full key

            WriteOrThrow(leftPage, Build(type, prev, rightPage, tail: 0, nodeLevel, left));
            WriteOrThrow(rightPage, Build(type, leftPage, next, tail: 0, nodeLevel, right));
            if (next != 0) SetPrev(next, rightPage); // fix the old next leaf's back-link
        }
        else
        {
            // Node split: the middle entry's key is promoted; its child becomes the left node's tail.
            int mid = entries.Count / 2;
            Entry middle = entries[mid];
            var left = entries.GetRange(0, mid);
            var right = entries.GetRange(mid + 1, entries.Count - mid - 1);
            promoted = middle.Key;
            int oldTail = _splitTail;

            WriteOrThrow(leftPage, Build(type, 0, 0, tail: middle.Trailer, nodeLevel, left));
            WriteOrThrow(rightPage, Build(type, 0, 0, tail: oldTail, nodeLevel, right));
        }

        if (level == 0)
        {
            // The root split: build a new root node [promoted -> old root] with the new page as its tail.
            int newRoot = _allocator.Allocate();
            WriteOrThrow(newRoot, Build(PageType.IntermediateIndexPage, 0, 0, tail: rightPage, nodeLevel + 1,
                [new Entry(promoted, leftPage)]));
            UpdateIndexRoot(index, newRoot);
            index.RootPage = newRoot; // keep the in-memory def in step for the next insert
            return;
        }

        InsertSeparator(index, path, level - 1, leftPage, promoted, rightPage);
    }

    /// <summary>Inserts a promoted separator into the parent node; repoints the old child to the new right
    /// page and splits the parent if it overflows.</summary>
    private void InsertSeparator(IndexDef index, List<int> path, int level, int oldChild, byte[] promoted, int newRight)
    {
        int parentPage = path[level];
        byte[] page = _channel.ReadPage(parentPage).Span.ToArray();
        (List<Entry> entries, int tail) = Parse(page);

        int slot = entries.FindIndex(e => e.Trailer == oldChild);
        if (slot >= 0)
        {
            entries[slot] = entries[slot] with { Trailer = newRight };
            entries.Insert(slot, new Entry(promoted, oldChild));
        }
        else // oldChild was the tail
        {
            tail = newRight;
            entries.Add(new Entry(promoted, oldChild));
        }

        int parentLevel = path.Count - 1 - level;
        if (Build(PageType.IntermediateIndexPage, 0, 0, tail, parentLevel, entries) is { } built)
        {
            _channel.WritePage(parentPage, built);
            return;
        }

        _splitTail = tail;
        SplitAndPropagate(index, path, level, entries, PageType.IntermediateIndexPage, 0, 0);
    }

    private int _splitTail; // carries a node's tail into SplitAndPropagate

    /// <summary>Parses a page's entries (decompressing the shared prefix) and its child-tail (nodes).</summary>
    private static (List<Entry> Entries, int Tail) Parse(byte[] page)
    {
        int compress = BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(CompressedByteCountOffset, 2));
        int tail = ReadInt32Le(page, ChildTailOffset);
        var entries = new List<Entry>();
        byte[] prefix = [];
        bool first = true;

        foreach ((int start, int end) in EntryRanges(page))
        {
            int trailer = ReadInt32Be(page, EntryDataOffset + end - 4);
            ReadOnlySpan<byte> stored = page.AsSpan(EntryDataOffset + start, end - start - 4);
            byte[] full = first ? stored.ToArray() : Concat(prefix, stored);
            if (first) { prefix = full[..compress]; first = false; }
            entries.Add(new Entry(full, trailer));
        }
        return (entries, tail);
    }

    /// <summary>Builds a page from entries; null if they overflow the page. Leaf pages are prefix-compressed;
    /// node pages are stored uncompressed and carry their height above the leaves at <see cref="LevelOffset"/>,
    /// both matching what Access writes. (An isolation test showed neither is strictly required — Access reads
    /// a node with <c>0x1A=0</c> and compressed just fine; they are kept purely for byte-faithfulness. The one
    /// hard requirement is a <b>leaf's</b> <c>0x1A=0</c> and the leaf-chain offsets at <c>0x0C</c>/<c>0x10</c>.)</summary>
    private byte[]? Build(PageType type, int prev, int next, int tail, int level, List<Entry> entries)
    {
        bool isLeaf = type == PageType.LeafIndexPage;
        int pageSize = _channel.PageSize;
        var page = new byte[pageSize];
        page[0] = (byte)type;
        page[1] = 0x01; // page flags (observed constant)
        page[LevelOffset] = (byte)level; // 0 on a leaf; the node's height above the leaves otherwise
        WriteInt32Le(page, OwnerOffset, _table.DefinitionPage);
        WriteInt32Le(page, PrevPageOffset, prev);
        WriteInt32Le(page, NextPageOffset, next);
        WriteInt32Le(page, ChildTailOffset, tail);

        int compress = !isLeaf || entries.Count == 0 ? 0 : CommonPrefixLength(entries[0].Key, entries[^1].Key);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(CompressedByteCountOffset, 2), (ushort)compress);

        int pos = EntryDataOffset;
        bool first = true;
        foreach (Entry e in entries)
        {
            ReadOnlySpan<byte> stored = first ? e.Key : e.Key.AsSpan(compress);
            first = false;
            int len = stored.Length + 4;
            if (pos + len > pageSize) return null; // overflow

            stored.CopyTo(page.AsSpan(pos));
            WriteInt32Be(page, pos + stored.Length, e.Trailer);
            pos += len;

            int end = pos - EntryDataOffset;
            page[EntryMaskOffset + (end >> 3)] |= (byte)(1 << (end & 7));
        }

        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(FreeSpaceOffset, 2), (ushort)(pageSize - pos));
        return page;
    }

    /// <summary>Repoints the index-data block's B-tree root (0x26) after the root grows a level. Walks
    /// stats → column descriptors → column names → data blocks to the index's block.</summary>
    /// <remarks>
    /// A wide table's definition spans continuation pages, and the data blocks sit past the column names —
    /// well beyond the first page for a 255-column table. The walk therefore runs over the <i>stitched</i>
    /// definition (the absolute coordinate space the descriptors use), and only the 4 root bytes are written
    /// back, mapped to whichever page actually holds them. Nothing changes length, so no re-split is needed.
    /// </remarks>
    private void UpdateIndexRoot(IndexDef index, int newRoot)
    {
        JetFormatBase format = _channel.Format;
        (byte[] tdef, IReadOnlyList<int> continuations) = ReadDefinition();
        int dataCount = BinaryPrimitives.ReadInt32LittleEndian(tdef.AsSpan(format.TdefIndexCountOffset, 4));
        int colCount = BinaryPrimitives.ReadUInt16LittleEndian(tdef.AsSpan(format.TdefColumnCountOffset, 2));

        int pos = format.TdefRealIndexBlockOffset + dataCount * format.RealIndexEntrySize
                  + colCount * format.ColumnDescriptorSize;
        for (int i = 0; i < colCount; i++) pos += 2 + BinaryPrimitives.ReadUInt16LittleEndian(tdef.AsSpan(pos, 2));

        int block = pos + index.RealIndexOrdinal * 52;
        WriteInt32IntoDefinition(continuations, block + RootPageInBlockOffset, newRoot);
    }

    private const int ContinuationHeaderSize = 8;

    /// <summary>Reads the table definition, stitching continuation pages into one contiguous buffer, and
    /// returns the continuation page numbers in chain order.</summary>
    private (byte[] Definition, IReadOnlyList<int> ContinuationPages) ReadDefinition()
    {
        JetFormatBase format = _channel.Format;
        PageBuffer first = _channel.ReadPage(_table.DefinitionPage);
        int next = first.ReadInt32(format.TdefNextPageOffset);
        if (next == 0) return (first.Span.ToArray(), []);

        var continuations = new List<int>();
        var assembled = new List<byte>(first.Span.ToArray());
        while (next != 0)
        {
            continuations.Add(next);
            PageBuffer cont = _channel.ReadPage(next);
            next = cont.ReadInt32(format.TdefNextPageOffset);
            assembled.AddRange(cont.Span[ContinuationHeaderSize..].ToArray());
        }
        return (assembled.ToArray(), continuations);
    }

    /// <summary>Maps an absolute definition offset to the page holding it and the offset within that page.</summary>
    private (int Page, int Offset) MapDefinitionOffset(IReadOnlyList<int> continuations, int offset)
    {
        int pageSize = _channel.Format.PageSize;
        if (offset < pageSize) return (_table.DefinitionPage, offset);

        int body = pageSize - ContinuationHeaderSize;
        int relative = offset - pageSize;
        int index = relative / body;
        if (index >= continuations.Count)
            throw new InvalidOperationException(
                $"Definition offset {offset} lies past the end of table '{_table.Name}'s definition chain.");
        return (continuations[index], ContinuationHeaderSize + relative % body);
    }

    /// <summary>Writes 4 little-endian bytes at an absolute definition offset, splitting the write when the
    /// field straddles a continuation-page boundary.</summary>
    private void WriteInt32IntoDefinition(IReadOnlyList<int> continuations, int offset, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);

        for (int i = 0; i < 4;)
        {
            int pageNumber = MapDefinitionOffset(continuations, offset + i).Page;
            byte[] page = _channel.ReadPage(pageNumber).Span.ToArray();

            int j = i;
            for (; j < 4; j++)
            {
                (int target, int within) = MapDefinitionOffset(continuations, offset + j);
                if (target != pageNumber) break;
                page[within] = bytes[j];
            }

            _channel.WritePage(pageNumber, page);
            i = j;
        }
    }

    private void SetPrev(int pageNumber, int prev)
    {
        byte[] page = _channel.ReadPage(pageNumber).Span.ToArray();
        WriteInt32Le(page, PrevPageOffset, prev);
        _channel.WritePage(pageNumber, page);
    }

    private void WriteOrThrow(int pageNumber, byte[]? page) =>
        _channel.WritePage(pageNumber, page ?? throw new NotSupportedException(
            "An index page still overflows after a split (a key wider than half a page)."));

    private static byte[] WithTrailer(byte[] key, int trailer)
    {
        var result = new byte[key.Length + 4];
        key.CopyTo(result, 0);
        WriteInt32Be(result, key.Length, trailer);
        return result;
    }

    private static int CommonPrefixLength(byte[] a, byte[] b)
    {
        int n = Math.Min(a.Length, b.Length), i = 0;
        while (i < n && a[i] == b[i]) i++;
        return i;
    }

    private static int CompareBytes(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++)
            if (a[i] != b[i]) return a[i] - b[i];
        return a.Length - b.Length;
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

    private static int ReadInt32Le(byte[] page, int offset) => BinaryPrimitives.ReadInt32LittleEndian(page.AsSpan(offset, 4));
    private static void WriteInt32Le(byte[] page, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(offset, 4), value);
    private static int ReadInt32Be(byte[] page, int offset) => BinaryPrimitives.ReadInt32BigEndian(page.AsSpan(offset, 4));
    private static void WriteInt32Be(byte[] page, int offset, int value) => BinaryPrimitives.WriteInt32BigEndian(page.AsSpan(offset, 4), value);
}
