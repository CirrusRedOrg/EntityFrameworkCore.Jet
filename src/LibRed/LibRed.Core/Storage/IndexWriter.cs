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
    private readonly UsageMapWriter _usageMaps = new(channel);

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
        var visitedLeaves = new HashSet<int>();
        while (leaf != 0)
        {
            if (!visitedLeaves.Add(leaf))
                throw new InvalidDataException($"Index leaf chain contains a cycle at page {leaf}.");
            ParsedIndexPage page = ReadIndexPage(leaf);
            if (page.Type != PageType.LeafIndexPage)
                throw new InvalidDataException($"Index leaf chain points to non-leaf page {leaf}.");
            foreach (Entry e in page.Entries)
            {
                int cmp = CompareBytes(e.Key, key);
                if (cmp > 0) return false;   // sorted past where the key would be — it's absent
                // Same key held by a *different* row (for an UPDATE, the row's own entry is excluded).
                if (cmp == 0 && e.Trailer != excludePointer) return true;
            }
            leaf = page.Next; // all keys here sort below it — may continue on the next leaf
        }
        return false;
    }

    /// <summary>
    /// Seeks the index for the rows whose key equals <paramref name="values"/> (an equality lookup): descends
    /// the B-tree to the leaf where the key belongs, then walks the leaf chain yielding matching row ids until
    /// a larger key is reached. O(log n) descent + O(matches), versus a full table scan.
    /// </summary>
    /// <remarks>
    /// The key encoding is order-preserving but <b>lossy</b> for text/binary collation, so distinct values can
    /// share a key — the seek is an access path that may over-return; the caller re-applies the real predicate.
    /// </remarks>
    public IEnumerable<RowId> Seek(IndexDef index, object?[] values)
    {
        byte[] key = IndexKeyEncoder.Encode(index.Columns, values);
        int leaf = Descend(index.RootPage, WithTrailer(key, 0))[^1];
        var visitedLeaves = new HashSet<int>();
        while (leaf != 0)
        {
            if (!visitedLeaves.Add(leaf))
                throw new InvalidDataException($"Index leaf chain contains a cycle at page {leaf}.");
            ParsedIndexPage page = ReadIndexPage(leaf);
            if (page.Type != PageType.LeafIndexPage)
                throw new InvalidDataException($"Index leaf chain points to non-leaf page {leaf}.");
            foreach (Entry e in page.Entries)
            {
                int cmp = CompareBytes(e.Key, key);
                if (cmp > 0) yield break;                 // sorted past the key — no more matches
                if (cmp == 0) yield return new RowId(e.Trailer >> 8, e.Trailer & 0xFF);
            }
            leaf = page.Next;                             // matches may continue on the next leaf
        }
    }

    /// <summary>
    /// Seeks the index for the rows whose key lies in the range [<paramref name="low"/>, <paramref name="high"/>]
    /// (either bound null = open): descends to the low bound's leaf and walks the leaf chain, yielding row ids
    /// while the key does not exceed the high bound. The key encoding is order-preserving so this returns the
    /// range in order. Like <see cref="Seek"/> it may over-return at the boundaries (lossy keys / strict-vs-
    /// inclusive) — the caller re-applies the real predicate.
    /// </summary>
    public IEnumerable<RowId> SeekRange(IndexDef index, object?[]? low, object?[]? high)
    {
        byte[]? lowKey = low is null ? null : IndexKeyEncoder.Encode(index.Columns, low);
        byte[]? highKey = high is null ? null : IndexKeyEncoder.Encode(index.Columns, high);

        int leaf = Descend(index.RootPage, WithTrailer(lowKey ?? [], 0))[^1];
        var visitedLeaves = new HashSet<int>();
        while (leaf != 0)
        {
            if (!visitedLeaves.Add(leaf))
                throw new InvalidDataException($"Index leaf chain contains a cycle at page {leaf}.");
            ParsedIndexPage page = ReadIndexPage(leaf);
            if (page.Type != PageType.LeafIndexPage)
                throw new InvalidDataException($"Index leaf chain points to non-leaf page {leaf}.");
            foreach (Entry e in page.Entries)
            {
                if (lowKey is not null && CompareBytes(e.Key, lowKey) < 0) continue;     // before the low bound
                if (highKey is not null && CompareBytes(e.Key, highKey) > 0) yield break; // past the high bound
                yield return new RowId(e.Trailer >> 8, e.Trailer & 0xFF);
            }
            leaf = page.Next;
        }
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
        CheckedIndexPage page = ReadMutationPage(leafPage, PageType.LeafIndexPage);
        (List<Entry> entries, _) = Parse(page);

        int idx = entries.FindIndex(e => e.Trailer == pointer && CompareBytes(e.Key, key) == 0);
        if (idx < 0)
            throw new InvalidOperationException(
                $"Index '{index.Name}': entry for row {rowId.Page}:{rowId.Row} was not found on leaf {leafPage}.");
        entries.RemoveAt(idx);

        // Removing only shrinks the page, so Build never overflows.
        _channel.WritePage(leafPage,
            Build(PageType.LeafIndexPage, page.Previous, page.Next, tail: 0, level: 0, entries)!);
    }

    private static bool HasNullKey(IndexDef index, object?[] values) =>
        index.Columns.Any(c => values[c.Column.Index] is null or DBNull);

    /// <summary>Descends to the leaf that should hold the key, recording the path from the root.</summary>
    private List<int> Descend(int rootPage, byte[] fullKey)
    {
        var path = new List<int>();
        var visited = new HashSet<int>();
        int pageNumber = rootPage;
        while (true)
        {
            if (!visited.Add(pageNumber))
                throw new InvalidDataException($"Index descent contains a cycle at page {pageNumber}.");
            path.Add(pageNumber);
            ParsedIndexPage page = ReadIndexPage(pageNumber);
            if (page.Type == PageType.LeafIndexPage) return path;
            if (page.Type != PageType.IntermediateIndexPage)
                throw new InvalidDataException($"Index descent reached non-index page {pageNumber}.");

            int child = page.Tail;
            foreach (Entry e in page.Entries)
                if (CompareBytes(e.Key, fullKey) >= 0) { child = e.Trailer; break; }
            pageNumber = child;
        }
    }

    /// <summary>An index page decoded for the read paths (<see cref="Descend"/>/<see cref="Seek"/>/
    /// <see cref="SeekRange"/>): its type, entries, node child-tail and leaf next-pointer.</summary>
    private sealed record ParsedIndexPage(PageType Type, int Owner, List<Entry> Entries, int Tail, int Next);

    /// <summary>Reads an index page as decoded entries, served from the channel's parsed-page cache on a repeat
    /// visit — a B-tree descent re-reads its root/internal pages on every seek, so caching the decode (not just
    /// the bytes) removes both the page copy and the entry decode. Read-only: the write paths parse fresh, and
    /// their <c>WritePage</c> invalidates the cached parse, so a hit is always consistent with the bytes.</summary>
    private ParsedIndexPage ReadIndexPage(int pageNumber)
    {
        if (_channel.TryGetParsedPage(pageNumber, out object? cached) && cached is ParsedIndexPage hit)
        {
            if (hit.Owner != _table.DefinitionPage)
                throw new InvalidDataException(
                    $"Index page {pageNumber} belongs to TDEF {hit.Owner}, not TDEF {_table.DefinitionPage}.");
            return hit;
        }

        CheckedIndexPage page = IndexPageReader.Read(_channel, pageNumber, _table.DefinitionPage);
        (List<Entry> entries, int tail) = Parse(page);
        var parsed = new ParsedIndexPage(page.Type, page.Owner, entries, tail, page.Next);
        _channel.SetParsedPage(pageNumber, parsed);
        return parsed;
    }

    private void InsertIntoLeaf(IndexDef index, List<int> path, byte[] key, int pointer)
    {
        int leafPage = path[^1];
        CheckedIndexPage page = ReadMutationPage(leafPage, PageType.LeafIndexPage);
        (List<Entry> entries, _) = Parse(page);

        // Insert in key order (key then pointer tiebreaker) — the full leaf key is key ++ pointer.
        byte[] fullKey = WithTrailer(key, pointer);
        int pos = 0;
        while (pos < entries.Count && CompareBytes(WithTrailer(entries[pos].Key, entries[pos].Trailer), fullKey) < 0) pos++;
        entries.Insert(pos, new Entry(key, pointer));

        if (Build(PageType.LeafIndexPage, page.Previous, page.Next, tail: 0, level: 0, entries) is { } built)
        {
            _channel.WritePage(leafPage, built);
            return;
        }

        SplitAndPropagate(index, path, path.Count - 1, entries, PageType.LeafIndexPage,
            page.Previous, page.Next);
    }

    /// <summary>
    /// Splits the (leaf or node) page at <paramref name="level"/> into two, writes both, then promotes a
    /// separator into the parent — splitting parents in turn, or growing a new root at the top.
    /// </summary>
    private void SplitAndPropagate(IndexDef index, List<int> path, int level, List<Entry> entries,
        PageType type, int prev, int next)
    {
        int leftPage = path[level];
        int rightPage = AllocateIndexPage(index);
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
            int newRoot = AllocateIndexPage(index);
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
        CheckedIndexPage page = ReadMutationPage(parentPage, PageType.IntermediateIndexPage);
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

    /// <summary>Parses a checked page's entries, decompressing their shared prefix.</summary>
    private static (List<Entry> Entries, int Tail) Parse(CheckedIndexPage page)
    {
        var entries = new List<Entry>(page.EntryRanges.Count);
        foreach ((byte[] key, int trailer) in IndexPageReader.DecodeEntries(page))
            entries.Add(new Entry(key, trailer));
        return (entries, page.Tail);
    }

    /// <summary>Revalidates a page immediately before mutation, closing the gap between B-tree descent and
    /// the final read-modify-write operation.</summary>
    private CheckedIndexPage ReadMutationPage(int pageNumber, PageType expectedType)
    {
        CheckedIndexPage page = IndexPageReader.Read(_channel, pageNumber, _table.DefinitionPage);
        if (page.Type != expectedType)
            throw new InvalidDataException(
                $"Index mutation expected page {pageNumber} to be {expectedType}, but found {page.Type}.");
        return page;
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

        // A single entry (or a node) has no common-prefix compression — ACE writes 0 here (the whole key with
        // itself would otherwise "compress" to its full length, which ACE does not do for one entry).
        int compress = !isLeaf || entries.Count <= 1 ? 0 : CommonPrefixLength(entries[0].Key, entries[^1].Key);
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
        (_, IReadOnlyList<int> continuations, int block) = LocateIndexBlock(index);
        WriteInt32IntoDefinition(continuations, block + RootPageInBlockOffset, newRoot);
    }

    private const int IndexUsageMapRowOffset = 0x22;  // within the 52-byte block: 1-byte row + 3-byte page

    /// <summary>The (row, page) pointer to the index's own pages usage map, read from its data block.</summary>
    private (int MapRow, int MapPage) IndexUsageMapPointer(IndexDef index)
    {
        (byte[] tdef, _, int block) = LocateIndexBlock(index);
        int row = tdef[block + IndexUsageMapRowOffset];
        int mapPage = tdef[block + IndexUsageMapRowOffset + 1]
                      | tdef[block + IndexUsageMapRowOffset + 2] << 8
                      | tdef[block + IndexUsageMapRowOffset + 3] << 16;
        return (row, mapPage);
    }

    /// <summary>Walks the stitched definition (stats → column descriptors → column names → data blocks) to
    /// the index's 52-byte data block, returning the buffer, its continuation pages, and the block's absolute
    /// offset. A wide table's blocks sit past the column names, well beyond the first page.</summary>
    private (byte[] Definition, IReadOnlyList<int> Continuations, int BlockOffset) LocateIndexBlock(IndexDef index)
    {
        JetFormatBase format = _channel.Format;
        (byte[] tdef, IReadOnlyList<int> continuations) = ReadDefinition();
        int dataCount = BinaryPrimitives.ReadInt32LittleEndian(tdef.AsSpan(format.TdefIndexCountOffset, 4));
        int colCount = BinaryPrimitives.ReadUInt16LittleEndian(tdef.AsSpan(format.TdefColumnCountOffset, 2));

        int pos = format.TdefRealIndexBlockOffset + dataCount * format.RealIndexEntrySize
                  + colCount * format.ColumnDescriptorSize;
        for (int i = 0; i < colCount; i++) pos += 2 + BinaryPrimitives.ReadUInt16LittleEndian(tdef.AsSpan(pos, 2));

        return (tdef, continuations, pos + index.RealIndexOrdinal * 52);
    }

    /// <summary>Allocates a fresh B-tree page for <paramref name="index"/> and records it in the index's own
    /// pages usage map, exactly as Access does — the map then covers every page of the index's B-tree, not
    /// just the root. (Reads use the B-tree's own links, so this is for byte-faithfulness and to feed Access's
    /// own maintenance, not for LibRed's own traversal.)</summary>
    private int AllocateIndexPage(IndexDef index)
    {
        int page = _allocator.Allocate();
        (int mapRow, int mapPage) = IndexUsageMapPointer(index);
        _usageMaps.SetBit(mapRow, mapPage, page, set: true);
        return page;
    }

    /// <summary>Reads the table definition, stitching continuation pages into one contiguous buffer, and
    /// returns the continuation page numbers in chain order.</summary>
    private (byte[] Definition, IReadOnlyList<int> ContinuationPages) ReadDefinition()
    {
        (PageBuffer buffer, IReadOnlyList<int> continuations) =
            TdefChainReader.Read(_channel, _table.DefinitionPage);
        return (buffer.Span.ToArray(), continuations);
    }

    /// <summary>Maps an absolute definition offset to the page holding it and the offset within that page.</summary>
    private (int Page, int Offset) MapDefinitionOffset(IReadOnlyList<int> continuations, int offset)
    {
        int pageSize = _channel.Format.PageSize;
        if (offset < pageSize) return (_table.DefinitionPage, offset);

        int body = pageSize - JetFormatBase.TdefContinuationHeaderSize;
        int relative = offset - pageSize;
        int index = relative / body;
        if (index >= continuations.Count)
            throw new InvalidOperationException(
                $"Definition offset {offset} lies past the end of table '{_table.Name}'s definition chain.");
        return (continuations[index], JetFormatBase.TdefContinuationHeaderSize + relative % body);
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
        CheckedIndexPage checkedPage = IndexPageReader.Read(_channel, pageNumber, _table.DefinitionPage);
        if (checkedPage.Type != PageType.LeafIndexPage)
            throw new InvalidDataException($"Leaf next-pointer targets non-leaf page {pageNumber}.");
        byte[] page = checkedPage.Buffer.Span.ToArray();
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

    private static void WriteInt32Le(byte[] page, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(offset, 4), value);
    private static void WriteInt32Be(byte[] page, int offset, int value) => BinaryPrimitives.WriteInt32BigEndian(page.AsSpan(offset, 4), value);
}
