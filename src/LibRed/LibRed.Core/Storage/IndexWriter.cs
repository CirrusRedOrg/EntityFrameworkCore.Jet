using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;
using System.Buffers.Binary;

namespace LibRed.Storage;

/// <summary>
/// Maintains an index B-tree on row insert: descends from the root to the target leaf, inserts the key,
/// and — when a page overflows — <b>splits</b> it, promoting a separator into the parent and propagating
/// splits up the tree (the tree grows a level when the root itself splits, the root keeping its page).
/// Leaf pages keep their doubly-linked prev/next chain; pages are written with prefix
/// compression. A key column whose collating order LibRed cannot encode still throws — see
/// <see cref="Collation.IsIndexKeyEncodable"/> for which orders it can.
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
    /// (either bound null = open): descends to the bound that sorts first in the index and walks the leaf chain,
    /// yielding row ids up to the other bound. The key encoding is order-preserving so this returns the range in
    /// index order — ascending by value on an ASC index, descending on a DESC one. Like <see cref="Seek"/> it may
    /// over-return at the boundaries (lossy keys / strict-vs-inclusive) — the caller re-applies the real predicate.
    /// </summary>
    public IEnumerable<RowId> SeekRange(IndexDef index, object?[]? low, object?[]? high)
    {
        byte[]? lowKey = low is null ? null : IndexKeyEncoder.Encode(index.Columns, low);
        byte[]? highKey = high is null ? null : IndexKeyEncoder.Encode(index.Columns, high);

        // A DESC index inverts its key bytes, so the low VALUE is the byte-greater key and the tree is walked
        // from the high bound down to it. The bounds are stated in values; here on they are byte bounds, so
        // swap them and the walk below reads the same way for either direction.
        if (!index.Columns[0].Ascending)
            (lowKey, highKey) = (highKey, lowKey);

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
    /// No rebalancing of an underfull leaf, and a stale separator (if the removed entry was a leaf's maximum)
    /// stays a valid upper bound, so later descents still route correctly — matching Access's lazy delete. A
    /// leaf the delete leaves <b>empty</b> is the exception: Access takes it out of the tree, and so does this
    /// (see <see cref="UnlinkEmptyLeaf"/>).
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

        if (entries.Count == 0 && UnlinkEmptyLeaf(index, path, page)) return;

        // Removing only shrinks the page, so Build never overflows. The page keeps the prefix length it was
        // already stored at: ACE re-compresses a leaf only when it must (see InsertIntoLeaf), and a delete
        // never must. Letting Build pick the largest prefix now available instead repacks entries ACE left
        // alone — measured as a 4-byte-shorter live region on every leaf a cascading delete touched.
        // Dropping an entry can only keep or widen what the rest share, so the stored length stays valid.
        WriteOrThrow(leafPage,
            Build(PageType.LeafIndexPage, page.Previous, page.Next, tail: 0, level: 0, entries,
                page.CompressedByteCount));
    }

    /// <summary>
    /// Takes a leaf whose last entry has just been removed out of the tree: past it in the leaf chain, its
    /// separator gone from the parent node, and the page itself back to the allocator. Returns false when the
    /// leaf has to stay, and the caller writes it back empty instead.
    /// </summary>
    /// <remarks>
    /// <para>This is what ACE does. Measured on a two-leaf tree whose low leaf was emptied by a range delete:
    /// the surviving leaf comes back with <c>prev = 0</c>, the node keeps only its child-tail pointer with no
    /// separator entries left, and the emptied page is no longer reachable from the root.</para>
    /// <para>Two shapes keep their empty leaf. A leaf that <b>is</b> the root has nowhere to go — an index with
    /// no rows is exactly one empty leaf. And a leaf that is its parent's only remaining child cannot be
    /// unlinked without leaving the parent pointing at nothing, a node shape ACE has not been observed to
    /// write; an empty leaf is a valid one, so the tree keeps it rather than inventing that.</para>
    /// </remarks>
    private bool UnlinkEmptyLeaf(IndexDef index, List<int> path, CheckedIndexPage leaf)
    {
        if (path.Count < 2) return false;

        int leafPage = path[^1];
        int parentPage = path[^2];
        CheckedIndexPage parent = ReadMutationPage(parentPage, PageType.IntermediateIndexPage);
        (List<Entry> entries, int tail) = Parse(parent);

        int slot = entries.FindIndex(e => e.Trailer == leafPage);
        if (slot >= 0)
        {
            entries.RemoveAt(slot);
        }
        else if (tail == leafPage)
        {
            // The tail has no key bound of its own, so the last separator's child takes its place and that
            // separator's key — an upper bound on the leaf now leaving — goes with it.
            if (entries.Count == 0) return false;
            tail = entries[^1].Trailer;
            entries.RemoveAt(entries.Count - 1);
        }
        else
        {
            throw new InvalidDataException(
                $"Index '{index.Name}': node {parentPage} does not point at leaf {leafPage}.");
        }

        if (leaf.Previous != 0) SetSiblingLink(leaf.Previous, NextPageOffset, leaf.Next, PageType.LeafIndexPage);
        if (leaf.Next != 0) SetSiblingLink(leaf.Next, PrevPageOffset, leaf.Previous, PageType.LeafIndexPage);

        // Dropping a separator only shrinks the node, so Build never overflows, and the node keeps its prefix
        // as a leaf does on a delete (see RemoveEntry). A leaf's parent is one level above the leaves by
        // definition.
        WriteOrThrow(parentPage, Build(PageType.IntermediateIndexPage, parent.Previous, parent.Next, tail, level: 1,
            entries, parent.CompressedByteCount));

        // The page leaves the index the way AllocateIndexPage brought it in: its bit out of the index's own
        // pages map, then released — held until this handle closes, the route ACE takes for a freed page.
        (int mapRow, int mapPage) = IndexUsageMapPointer(index);
        _usageMaps.SetBit(mapRow, mapPage, leafPage, set: false);
        _allocator.Release(leafPage);
        return true;
    }

    internal static bool HasNullKey(IndexDef index, object?[] values) =>
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

    /// <summary>An index page decoded: its type, entries, node child-tail, and the leaf header fields a
    /// rewrite of the page has to carry forward (previous/next links and the stored prefix length).</summary>
    private sealed record ParsedIndexPage(
        PageType Type, int Owner, List<Entry> Entries, int Tail, int Next, int Previous, int Compressed);

    /// <summary>Reads an index page as decoded entries, served from the channel's parsed-page cache on a repeat
    /// visit — a B-tree descent re-reads its root/internal pages on every seek, so caching the decode (not just
    /// the bytes) removes both the page copy and the entry decode. A cached parse is dropped whenever the bytes
    /// change (any channel) or the page is evicted, so a hit is always consistent with the bytes.</summary>
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
        var parsed = new ParsedIndexPage(
            page.Type, page.Owner, entries, tail, page.Next, page.Previous, page.CompressedByteCount);
        _channel.SetParsedPage(pageNumber, parsed);
        return parsed;
    }

    /// <summary>
    /// The decoded page a read-modify-write is about to rewrite, with an <b>owned</b> entry list the caller may
    /// mutate. Serves the parse the descent just made rather than reading and decoding the page a second time.
    /// </summary>
    /// <remarks>
    /// <para>Every insert descends to its leaf and then rewrites it, and the two steps each parsed the page —
    /// decoding a fresh <c>byte[]</c> for every entry on it, twice. This reuses the first parse.</para>
    /// <para>The list is <b>copied</b> before it is handed over, because the cached parse is shared with every
    /// other reader of the file and <see cref="PageChannel.SetParsedPage"/>'s contract forbids mutating it. The
    /// copy is shallow, which is the point: <see cref="Entry"/> is an immutable struct holding a reference to
    /// its key, so copying the list shares the key arrays and allocates one array of structs instead of one
    /// array per entry. Keys are never written through, only read by <see cref="Build"/>.</para>
    /// <para>This does not weaken the revalidation the write paths perform (§10.2). A cached parse exists only
    /// while the bytes behind it are unchanged — any write, from any channel, drops it, and a page buffered in
    /// an open transaction's overlay is never served — so a hit carries the same guarantee a re-read would, and
    /// the type and owner recorded in it are checked exactly as before.</para>
    /// </remarks>
    private ParsedIndexPage ReadMutablePage(int pageNumber, PageType expectedType)
    {
        ParsedIndexPage parsed = ReadIndexPage(pageNumber);
        if (parsed.Type != expectedType)
            throw new InvalidDataException(
                $"Index mutation expected page {pageNumber} to be {expectedType}, but found {parsed.Type}.");

        // Room for the one entry InsertIntoLeaf inserts: copied at its exact size, the list doubled its array on that
        // insert — about 19 KB per index per row on a full leaf, a fifth of everything an insert allocated.
        var entries = new List<Entry>(parsed.Entries.Count + 1);
        entries.AddRange(parsed.Entries);
        return parsed with { Entries = entries };
    }

    private void InsertIntoLeaf(IndexDef index, List<int> path, byte[] key, int pointer)
    {
        int leafPage = path[^1];
        ParsedIndexPage page = ReadMutablePage(leafPage, PageType.LeafIndexPage);
        List<Entry> entries = page.Entries;

        // Insert in key order (key then pointer tiebreaker) — the full leaf key is key ++ pointer. Compared
        // without materialising each entry's concatenation: this scan runs over every entry on the page for
        // every row inserted, so building one throwaway array per comparison was the write path's largest
        // single allocator.
        byte[] fullKey = WithTrailer(key, pointer);
        int pos = 0;
        while (pos < entries.Count && CompareWithTrailer(entries[pos].Key, entries[pos].Trailer, fullKey) < 0) pos++;
        entries.Insert(pos, new Entry(key, pointer));

        // ACE compresses a leaf only when it has to, and splits only when compressing is not enough. A page
        // starts uncompressed and stores whole keys; when the next entry will not fit, the shared prefix is
        // computed and the page rewritten IN PLACE, which typically frees most of it; filling then continues
        // at the shorter size; and only when the compressed page fills does it split. Watching a sequential
        // load shows the cycle twice — a leaf reaching 400 entries at 9 bytes each with 16 bytes left, then
        // reading 410 entries at 6 bytes with 1153 free, then splitting at 602 (see §10.3).
        //
        // Rebuilding at the largest available prefix on every write instead would be smaller, but it is not
        // what ACE writes, and the tail page of a sequential load is the visible difference.
        int share = entries.Count <= 1 ? 0 : CommonPrefixLength(entries[0].Key, entries[^1].Key);
        int keep = Math.Min(page.Compressed, share);            // the new key may not share the old prefix

        if (Build(PageType.LeafIndexPage, page.Previous, page.Next, tail: 0, level: 0, entries, keep) is { } asIs)
        {
            _channel.WritePage(leafPage, KeepTail(leafPage, asIs), AsBuilt(page, entries, keep));
            return;
        }

        if (share > keep
            && Build(PageType.LeafIndexPage, page.Previous, page.Next, tail: 0, level: 0, entries, share)
                is { } compressed)
        {
            _channel.WritePage(leafPage, KeepTail(leafPage, compressed), AsBuilt(page, entries, share));
            return;
        }

        // Where to cut. Splitting down the middle is right when keys arrive all over the range, because the
        // lower half's free space is room for the next key near it. When the new entry is the page's MAXIMUM
        // it is waste instead: nothing sorts below a maximum, so half the page is stranded for ever. ACE
        // splits at the right edge in that case — the page stays full and the new entry starts a fresh one —
        // which is why a sequentially loaded index of ACE's packs its leaves to capacity and LibRed's used to
        // settle near half (see docs/format/page-03-04-index-btree.md §10.5 for the measured comparison).
        //
        // AutoNumber and identity keys are ascending by construction, so this is the ordinary case. The
        // condition cannot fire on a random insert, which is why the general behaviour is unchanged.
        //
        // Compressing did not make room, but ACE still compresses first: the page's old entries are rewritten
        // in place at the prefix they share, and the split is made over that. It shows past each half's live
        // end, where the compressed entries stand. The left half then stays at that prefix — unless the new
        // entry became its first, when ACE writes it whole. (A new first entry that does not split the page
        // keeps the prefix: verified both ways.)
        var old = new List<Entry>(entries);
        old.RemoveAt(pos);
        int oldShare = old.Count <= 1 ? 0 : CommonPrefixLength(old[0].Key, old[^1].Key);
        int stored = Math.Max(page.Compressed, oldShare);
        if (oldShare > page.Compressed)
            WriteOrThrow(leafPage, Build(PageType.LeafIndexPage, page.Previous, page.Next, 0, 0, old, oldShare));

        // Everywhere else ACE cuts that compressed page at its byte midpoint — every old entry that STARTS before
        // it stays left, so the entry straddling it does too — and the new entry joins whichever half its key
        // falls in. A key landing below the cut therefore leaves one MORE entry behind than a key landing on or
        // above it. With equal entries that is the old entries halved, the odd one left: a 17-entry leaf keeps
        // 10 for a key at any position from 1 to 8 and 9 from 9 to 16, and a 602-entry one keeps 302 for a key at
        // 1 or 50 and 301 at 301, 302 or 400. It is bytes, not entries, when keys differ in length: a 91-entry
        // leaf of 38- to 40-byte entries keeps 45, not 46. A new FIRST entry is the exception: then the entries
        // including it are halved by count, rounding up — 9 of 18, 302 of 603 (§10.5).
        int splitAt;
        if (pos == entries.Count - 1) splitAt = entries.Count - 1;
        else if (pos == 0) splitAt = (entries.Count + 1) / 2;
        else
        {
            long total = -(long)stored * (old.Count - 1);
            foreach (Entry e in old) total += e.Key.Length + TrailerSize;
            int oldLeft = 0;
            for (long start = 0; oldLeft < old.Count && start * 2 < total; oldLeft++)
                start += old[oldLeft].Key.Length + TrailerSize - (oldLeft == 0 ? 0 : stored);
            splitAt = pos < oldLeft ? oldLeft + 1 : oldLeft;
        }

        SplitAndPropagate(index, path, path.Count - 1, entries, PageType.LeafIndexPage,
            page.Previous, page.Next, splitAt, leftPrefix: pos == 0 ? 0 : stored, newFirst: pos == 0);
    }

    /// <summary>
    /// Splits the (leaf or node) page at <paramref name="level"/> into two, writes both, then promotes a
    /// separator into the parent — splitting parents in turn, or turning the root into the node over both halves.
    /// </summary>
    /// <param name="index">The index whose tree is being split.</param>
    /// <param name="path">The pages from the root down to the one being split, one per level.</param>
    /// <param name="level">Which entry of <paramref name="path"/> is the page to split.</param>
    /// <param name="entries">That page's entries, in key order, including the one just inserted.</param>
    /// <param name="type">Leaf or node — what the two halves are written as.</param>
    /// <param name="prev">The split page's left sibling, for the leaf chain.</param>
    /// <param name="next">Its right sibling.</param>
    /// <param name="splitAt">How many entries stay on the left page; negative for the default half. A leaf split
    /// always sets it (see InsertIntoLeaf); a node split sets it for its right-edge case (see InsertSeparator),
    /// where the entry at <paramref name="splitAt"/> is the one promoted.</param>
    /// <param name="leftPrefix">The prefix a leaf's left half is written at; null for the largest available,
    /// which is what the right half and a node's halves are written at.</param>
    /// <param name="newFirst">Whether the entry just inserted is a leaf's first.</param>
    private void SplitAndPropagate(IndexDef index, List<int> path, int level, List<Entry> entries,
        PageType type, int prev, int next, int splitAt = -1, int? leftPrefix = null, bool newFirst = false)
    {
        // The root never moves: when it splits, both halves go to new pages, left first, and the root page is
        // rewritten as the node over them — so the index-data block's root pointer stays as it is. The left half
        // is the root's page carried over, so what lies past its live end is what the root held there.
        int leftPage = level == 0 ? AllocateIndexPage(index) : path[level];
        int rightPage = AllocateIndexPage(index);
        int nodeLevel = path.Count - 1 - level; // height above the leaves of the page being split

        byte[] promoted;
        if (type == PageType.LeafIndexPage)
        {
            // The left page always fits: at worst it is the page as it stood before the insert that
            // overflowed it, and that fitted.
            int mid = splitAt < 0 ? entries.Count / 2 : splitAt;
            var left = entries.GetRange(0, mid);
            var right = entries.GetRange(mid, entries.Count - mid);
            promoted = WithTrailer(left[^1].Key, left[^1].Trailer); // left's max full key

            // Right first, so both halves read the page as it stood: the right half takes its dead bytes from it
            // when the new entry became the left half's first, a new page's zeros otherwise (verified both ways).
            WriteOrThrow(rightPage, Build(type, leftPage, next, tail: 0, nodeLevel, right),
                tailFrom: newFirst ? path[level] : null);
            WriteOrThrow(leftPage, Build(type, prev, rightPage, tail: 0, nodeLevel, left, leftPrefix),
                tailFrom: path[level]);
        }
        else
        {
            // Node split: the middle entry's key is promoted; its child becomes the left node's tail.
            int mid = splitAt < 0 ? entries.Count / 2 : splitAt;
            Entry middle = entries[mid];
            var left = entries.GetRange(0, mid);
            var right = entries.GetRange(mid + 1, entries.Count - mid - 1);
            promoted = middle.Key;
            int oldTail = _splitTail;

            byte[] leftBytes = KeepTail(path[level], Build(type, prev, rightPage, tail: middle.Trailer, nodeLevel, left)
                ?? throw new NotSupportedException("An index node still overflows after a split."));
            LeaveMiddleBehind(leftBytes, prev, rightPage, nodeLevel, left, middle);
            _channel.WritePage(leftPage, leftBytes);
            WriteOrThrow(rightPage, Build(type, leftPage, next, tail: oldTail, nodeLevel, right));
        }
        if (next != 0) SetSiblingLink(next, PrevPageOffset, rightPage, type); // the old next page's back-link

        if (level == 0)
        {
            // The root split: the root becomes the node [promoted -> left] with the right page as its tail. Its
            // bytes past the new live end stay as they were, as on any rewrite of a page in place.
            WriteOrThrow(path[0], Build(PageType.IntermediateIndexPage, 0, 0, tail: rightPage, nodeLevel + 1,
                [new Entry(promoted, leftPage)]));
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

        // The old child's pointer is repointed first, so the page ACE compresses in place when the separator
        // does not fit already carries it (see below).
        int slot = entries.FindIndex(e => e.Trailer == oldChild);
        if (slot >= 0) entries[slot] = entries[slot] with { Trailer = newRight };
        else tail = newRight; // oldChild was the tail
        var old = new List<Entry>(entries);
        entries.Insert(slot >= 0 ? slot : entries.Count, new Entry(promoted, oldChild));

        // A node fills, compresses and splits exactly as a leaf does (see InsertIntoLeaf).
        int parentLevel = path.Count - 1 - level;
        int share = CommonPrefixLength(entries[0].Key, entries[^1].Key);
        int keep = Math.Min(page.CompressedByteCount, share);
        if (Build(PageType.IntermediateIndexPage, page.Previous, page.Next, tail, parentLevel, entries, keep)
            is { } asIs)
        {
            _channel.WritePage(parentPage, KeepTail(parentPage, asIs));
            return;
        }

        if (share > keep
            && Build(PageType.IntermediateIndexPage, page.Previous, page.Next, tail, parentLevel, entries, share)
                is { } compressed)
        {
            _channel.WritePage(parentPage, KeepTail(parentPage, compressed));
            return;
        }

        // As a leaf does, the full node is compressed in place before it splits (see InsertIntoLeaf).
        int oldShare = old.Count <= 1 ? 0 : CommonPrefixLength(old[0].Key, old[^1].Key);
        if (oldShare > page.CompressedByteCount)
            WriteOrThrow(parentPage,
                Build(PageType.IntermediateIndexPage, page.Previous, page.Next, tail, parentLevel, old, oldShare));

        // A node has a right-edge split too: when the new separator is its last entry — the child that split was
        // the tail, as on every split of an ascending load — the left node keeps every old entry but the last,
        // that one is promoted, and the new separator starts the right node alone (verified vs ACE: a node of 16
        // separators taking a 17th is cut 15, promote 1, 1). Anywhere else it is cut in the middle.
        _splitTail = tail;
        SplitAndPropagate(index, path, level, entries, PageType.IntermediateIndexPage, page.Previous, page.Next,
            splitAt: slot < 0 ? entries.Count - 2 : -1);
    }

    private int _splitTail; // carries a node's tail into SplitAndPropagate

    /// <summary>
    /// The parse of a leaf <see cref="Build"/> has just made from <paramref name="entries"/> at
    /// <paramref name="prefix"/>, with <paramref name="read"/>'s links: what decoding the written page gives, so the
    /// write can hand it to the channel rather than have the next insert into the leaf decode every entry again.
    /// </summary>
    /// <remarks>Consecutive inserts mostly land on one leaf, and each write dropped the parse the next one needed:
    /// re-decoding the leaf — hundreds of entries, an array each — was two-fifths of what an insert allocated.
    /// Every field is what Build writes: the leaf's own owner, no child tail, and a prefix of 0 for a lone entry.
    /// The entry list becomes the shared parse, so the caller must not touch it again.
    /// <c>CachedParseMatchesPage</c> is the check that the two agree.</remarks>
    private ParsedIndexPage AsBuilt(ParsedIndexPage read, List<Entry> entries, int prefix) =>
        read with
        {
            Type = PageType.LeafIndexPage,
            Owner = _table.DefinitionPage,
            Entries = entries,
            Tail = 0,
            Compressed = entries.Count <= 1 ? 0 : prefix,
        };

    /// <summary>Whether the parse cached for <paramref name="pageNumber"/> is exactly what decoding the page's bytes
    /// gives now — the invariant <see cref="AsBuilt"/> relies on — or null when no parse of one of this table's
    /// index pages is cached there. For tests.</summary>
    internal bool? CachedParseMatchesPage(int pageNumber)
    {
        if (!_channel.TryGetParsedPage(pageNumber, out object? cached) || cached is not ParsedIndexPage hit
            || hit.Owner != _table.DefinitionPage)
            return null;
        CheckedIndexPage page = IndexPageReader.Read(_channel, pageNumber, _table.DefinitionPage);
        (List<Entry> entries, int tail) = Parse(page);
        return hit.Type == page.Type && hit.Owner == page.Owner && hit.Tail == tail
            && hit.Next == page.Next && hit.Previous == page.Previous && hit.Compressed == page.CompressedByteCount
            && hit.Entries.Count == entries.Count
            && hit.Entries.Zip(entries).All(p => p.First.Trailer == p.Second.Trailer
                && p.First.Key.AsSpan().SequenceEqual(p.Second.Key));
    }

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

    /// <summary>Builds a page from entries; null if they overflow the page. Leaf and node pages alike are
    /// prefix-compressed at the length the caller gives, and a node carries its height above the leaves at
    /// <see cref="LevelOffset"/>, matching what Access writes. (An isolation test showed a node's height is not
    /// required — Access reads a node with <c>0x1A=0</c> just fine; it is kept purely for byte-faithfulness. The
    /// one hard requirement is a <b>leaf's</b> <c>0x1A=0</c> and the leaf-chain offsets at <c>0x0C</c>/<c>0x10</c>.)</summary>
    /// <param name="type">Leaf or node.</param>
    /// <param name="prev">The page's left sibling at the same level.</param>
    /// <param name="next">Its right sibling.</param>
    /// <param name="tail">The page's trailing pointer — a node's rightmost child.</param>
    /// <param name="level">A node's height above the leaves; 0 on a leaf.</param>
    /// <param name="entries">The entries to write, in key order.</param>
    /// <param name="prefix">The shared-prefix length to store the entries at. Null computes the largest
    /// available, which is what a split writes. It must not exceed what the entries actually share.</param>
    private byte[]? Build(PageType type, int prev, int next, int tail, int level, List<Entry> entries,
        int? prefix = null)
    {
        int pageSize = _channel.PageSize;
        var page = new byte[pageSize];
        page[0] = (byte)type;
        page[1] = 0x01; // page flags (observed constant)
        page[LevelOffset] = (byte)level; // 0 on a leaf; the node's height above the leaves otherwise
        WriteInt32Le(page, OwnerOffset, _table.DefinitionPage);
        WriteInt32Le(page, PrevPageOffset, prev);
        WriteInt32Le(page, NextPageOffset, next);
        WriteInt32Le(page, ChildTailOffset, tail);

        // A single entry has no common-prefix compression — ACE writes 0 here (the whole key with itself would
        // otherwise "compress" to its full length, which ACE does not do for one entry). Otherwise the caller
        // chooses, for a node as for a leaf: see InsertIntoLeaf for when a page is compressed at all.
        int compress = entries.Count <= 1
            ? 0
            : prefix ?? CommonPrefixLength(entries[0].Key, entries[^1].Key);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(CompressedByteCountOffset, 2), (ushort)compress);

        int pos = EntryDataOffset;
        bool first = true;
        Span<byte> trailer = stackalloc byte[TrailerSize];
        foreach (Entry e in entries)
        {
            // The prefix covers the entry WHOLE — key ++ trailer — so where many rows share a key it reaches
            // past the key into the row pointer, and what is stored is the tail of that concatenation. ACE
            // writes leaves this way and IndexPageReader.DecodeEntries reads them back the same way; taking
            // the tail of the key alone throws on exactly those pages.
            int skip = first ? 0 : compress;
            first = false;
            int keySkip = Math.Min(skip, e.Key.Length);
            int trailerSkip = skip - keySkip;
            int len = e.Key.Length - keySkip + TrailerSize - trailerSkip;
            if (pos + len > pageSize) return null; // overflow

            BinaryPrimitives.WriteInt32BigEndian(trailer, e.Trailer);
            e.Key.AsSpan(keySkip).CopyTo(page.AsSpan(pos));
            trailer[trailerSkip..].CopyTo(page.AsSpan(pos + e.Key.Length - keySkip));
            pos += len;

            int end = pos - EntryDataOffset;
            page[EntryMaskOffset + (end >> 3)] |= (byte)(1 << (end & 7));
        }

        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(FreeSpaceOffset, 2), (ushort)(pageSize - pos));
        return page;
    }

    /// <summary>Repoints the index-data block's B-tree root (0x26) — when a new table's foreign-key index is given
    /// its root (a split never moves one). Walks stats → column descriptors → column names → data blocks to the
    /// index's block.</summary>
    /// <remarks>
    /// A wide table's definition spans continuation pages, and the data blocks sit past the column names —
    /// well beyond the first page for a 255-column table. The walk therefore runs over the <i>stitched</i>
    /// definition (the absolute coordinate space the descriptors use), and only the 4 root bytes are written
    /// back, mapped to whichever page actually holds them. Nothing changes length, so no re-split is needed.
    /// </remarks>
    internal void UpdateIndexRoot(IndexDef index, int newRoot)
    {
        (_, IReadOnlyList<int> continuations, int block) = LocateIndexBlock(index);
        WriteInt32IntoDefinition(continuations, block + IndexBlockFormat.RootPageOffset, newRoot);
        // The root pointer is part of the definition every other handle caches: until they reload it they
        // descend from the old root — by now only a page inside the tree — and an insert through one would
        // split that page as if it were the root and write it over this one.
        _channel.MarkSchemaChanged();
    }

    /// <summary>The (row, page) pointer to the index's own pages usage map, read from its data block.</summary>
    private (int MapRow, int MapPage) IndexUsageMapPointer(IndexDef index)
    {
        (byte[] tdef, _, int block) = LocateIndexBlock(index);
        int pointer = block + IndexBlockFormat.UsageMapRowOffset;   // 1-byte row + 3-byte page
        int row = tdef[pointer];
        int mapPage = tdef[pointer + 1] | tdef[pointer + 2] << 8 | tdef[pointer + 3] << 16;
        return (row, mapPage);
    }

    /// <summary>Walks the stitched definition (stats → column descriptors → column names → data blocks) to
    /// the index's 52-byte data block, returning the buffer, its continuation pages, and the block's absolute
    /// offset. A wide table's blocks sit past the column names, well beyond the first page.</summary>
    private (byte[] Definition, IReadOnlyList<int> Continuations, int BlockOffset) LocateIndexBlock(IndexDef index)
    {
        (byte[] tdef, IReadOnlyList<int> continuations) = ReadDefinition();
        TdefRegions regions = TdefRegions.Of(tdef, _channel.Format);

        // Bounded like every region before it, because this offset decides where UpdateIndexRoot writes: an
        // ordinal past the blocks would otherwise repoint whatever follows them.
        int block = TableDefinitionPage.CheckedRegionEnd(
            regions.DataBlocks, index.RealIndexOrdinal + 1, IndexBlockFormat.DataBlockSize, tdef.Length,
            "index-data blocks")
            - IndexBlockFormat.DataBlockSize;
        return (tdef, continuations, block);
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

    /// <summary>Patches one end of a neighbouring page's sibling link — <see cref="PrevPageOffset"/> or
    /// <see cref="NextPageOffset"/> — without disturbing its entries. A split repairs the back-link of the page
    /// it pushed right, leaf or node; unlinking an emptied leaf repairs both of its neighbours.</summary>
    private void SetSiblingLink(int pageNumber, int offset, int target, PageType type)
    {
        CheckedIndexPage checkedPage = IndexPageReader.Read(_channel, pageNumber, _table.DefinitionPage);
        if (checkedPage.Type != type)
            throw new InvalidDataException(
                $"Sibling pointer targets page {pageNumber}, a {checkedPage.Type} where a {type} was expected.");
        byte[] page = checkedPage.Buffer.Span.ToArray();
        WriteInt32Le(page, offset, target);
        _channel.WritePage(pageNumber, page);
    }

    /// <param name="pageNumber">The page to write.</param>
    /// <param name="page">The built page; null when its entries overflowed.</param>
    /// <param name="tailFrom">The page whose dead bytes the write carries over — this page itself, unless the
    /// content is moving here from another (a split root's left half).</param>
    private void WriteOrThrow(int pageNumber, byte[]? page, int? tailFrom = null) =>
        _channel.WritePage(pageNumber, KeepTail(tailFrom ?? pageNumber, page ?? throw new NotSupportedException(
            "An index page still overflows after a split (a key wider than half a page).")));

    /// <summary>
    /// Carries the destination's bytes past the new live end into a freshly built page, because that is what
    /// Access leaves behind.
    /// </summary>
    /// <remarks>
    /// <para><see cref="Build"/> works in a zeroed buffer and fills it only as far as the last entry, so
    /// everything beyond is written back as zeros. ACE instead edits a page in place: it moves the free-space
    /// pointer and lets the bytes the entries used to occupy stand. Measured on an insert that splits three
    /// levels of <c>[Order Details]</c>: on every index page ACE rewrote (both leaves and nodes) the region
    /// past the new live end still held the original bytes, and on the one page it took fresh from the
    /// allocator that region was zero. So the rule is <b>zero-fill on allocation, never clear again</b>.</para>
    /// <para>Only a rewrite of this index's own page qualifies — a page just recycled from somewhere else
    /// still carries the previous owner's type and owner id, and ACE zero-fills that one, which is what
    /// building in a clean buffer already does.</para>
    /// <para>The bytes kept are dead: they sit past the free-space boundary, so no reader reaches them.
    /// Keeping them is for byte-faithfulness with ACE, not for meaning.</para>
    /// </remarks>
    private byte[] KeepTail(int pageNumber, byte[] built) => Merge(built, ImageOf(pageNumber));

    /// <summary>What a rewrite of <paramref name="pageNumber"/> keeps past its live end: the page's bytes when it
    /// is already one of this table's index pages, zeros otherwise (see <see cref="KeepTail"/>).</summary>
    private byte[] ImageOf(int pageNumber)
    {
        if (pageNumber >= _channel.PageCount) return new byte[_channel.PageSize];

        // A leaf turning node — the root, when it splits — still counts as the same page rewritten.
        ReadOnlySpan<byte> existing = _channel.ReadPage(pageNumber).Span;
        if (existing[0] is not ((byte)PageType.LeafIndexPage or (byte)PageType.IntermediateIndexPage)
            || BinaryPrimitives.ReadInt32LittleEndian(existing[OwnerOffset..]) != _table.DefinitionPage)
            return new byte[_channel.PageSize];
        return existing.ToArray();
    }

    /// <summary>Carries <paramref name="image"/>'s bytes past <paramref name="built"/>'s live end into it.</summary>
    private byte[] Merge(byte[] built, byte[] image)
    {
        int liveEnd = LiveEnd(built);
        image.AsSpan(liveEnd).CopyTo(built.AsSpan(liveEnd));
        return built;
    }

    /// <summary>Stores a split node's promoted middle entry just past its left half's live end, at the page's
    /// prefix: ACE writes it into the page and then ends the page before it.</summary>
    private void LeaveMiddleBehind(byte[] leftBytes, int prev, int right, int level, List<Entry> left, Entry middle)
    {
        int prefix = BinaryPrimitives.ReadUInt16LittleEndian(leftBytes.AsSpan(CompressedByteCountOffset, 2));
        byte[] withMiddle = Build(PageType.IntermediateIndexPage, prev, right, middle.Trailer, level, [.. left, middle], prefix)!;
        int leftEnd = LiveEnd(leftBytes);
        withMiddle.AsSpan(leftEnd, LiveEnd(withMiddle) - leftEnd).CopyTo(leftBytes.AsSpan(leftEnd));
    }

    /// <summary>Where a built page's entries end: everything past it is free space.</summary>
    private int LiveEnd(byte[] page) =>
        _channel.PageSize - BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(FreeSpaceOffset, 2));

    /// <summary>Width of the row/child pointer an entry carries after its key.</summary>
    private const int TrailerSize = 4;

    private static byte[] WithTrailer(byte[] key, int trailer)
    {
        var result = new byte[key.Length + TrailerSize];
        key.CopyTo(result, 0);
        WriteInt32Be(result, key.Length, trailer);
        return result;
    }

    /// <summary>
    /// Fills an <b>empty</b> index from <paramref name="entries"/>, leaving exactly the pages that inserting them
    /// one at a time in key order would leave, but building a page only when its layout changes rather than
    /// rewriting it for every entry.
    /// </summary>
    /// <param name="index">The empty index to fill.</param>
    /// <param name="entries">(key, row pointer) pairs with a <c>NullKey</c> marker; any order. Sorted here.</param>
    /// <param name="rejectDuplicates">Enforce uniqueness — adjacent equal keys after the sort, null keys exempt
    /// (Jet's uniqueness is over the non-null keys only).</param>
    /// <remarks>
    /// <para>This is what ACE's <c>CREATE INDEX</c> writes: its tree is the incremental one for sorted input,
    /// byte for byte — the root keeps its page, leaves fill uncompressed, are compressed in place when full and
    /// split at the right edge, and the nodes above fill, compress and split the same way, all allocated in the
    /// order those splits happen (verified vs ACE). Sorted input only ever reaches the right edge of the tree,
    /// so the one page per level being filled there — the spine — is all that is held; every page to its left is
    /// finished, and written, when it is split off.</para>
    /// <para>Whether an entry fits is decided by <b>arithmetic</b>, not by calling <see cref="Build"/>: Build
    /// allocates a page-sized array, so probing with it would allocate two pages per entry and lose exactly
    /// what this exists to save. <see cref="LeafFits"/> mirrors Build's layout — entry data starts at
    /// <c>0x1E0</c>, the first entry stores its whole key, the rest drop the shared prefix, and each carries a
    /// 4-byte trailer (see page-01/page-03-04 §10.2–10.3). A page is built only when the prefix it is stored at
    /// changes, when it splits, and at the end: the incremental path's writes between those only extend the
    /// same layout, so they leave nothing past the live end that a later write keeps.</para>
    /// </remarks>
    internal void BulkBuild(IndexDef index, List<(byte[] Key, int Pointer, bool NullKey)> entries, bool rejectDuplicates)
    {
        if (entries.Count == 0)
            return; // the caller's freshly created empty root is already the correct tree

        entries.Sort((a, b) => CompareEntries(a.Key, a.Pointer, b.Key, b.Pointer));

        if (rejectDuplicates)
            for (int i = 1; i < entries.Count; i++)
                if (!entries[i].NullKey && !entries[i - 1].NullKey
                    && CompareBytes(entries[i].Key, entries[i - 1].Key) == 0)
                    throw new InvalidOperationException(
                        $"Cannot create unique index '{index.Name}' on '{_table.Name}': duplicate key values exist.");

        // The empty leaf the caller created is the root, and stays the root however tall the tree grows.
        var spine = new List<SpinePage> { new(index.RootPage, previous: 0, ImageOf(index.RootPage)) };
        foreach ((byte[] key, int pointer, _) in entries)
            Append(index, spine, level: 0, new Entry(key, pointer));

        for (int level = 0; level < spine.Count; level++)
        {
            SpinePage page = spine[level];
            _channel.WritePage(page.Number, Merge(BuildSpine(page, level, next: 0, page.Entries, page.Stored), page.Image));
        }
    }

    /// <summary>The page being filled at one level of a <see cref="BulkBuild"/>: its entries, the prefix they
    /// are stored at, and what the incremental path's writes would have left past the live end.</summary>
    private sealed class SpinePage(int number, int previous, byte[] image)
    {
        public int Number { get; } = number;
        public int Previous { get; } = previous;
        public byte[] Image { get; set; } = image;
        public List<Entry> Entries { get; } = [];
        public long KeyBytes { get; set; }
        public int Stored { get; set; }  // the prefix the page is stored at (0x18), as the last write left it
        public int Tail { get; set; }    // a node's rightmost child
    }

    /// <summary>Appends an entry to the page at <paramref name="level"/> of the spine as an insert of the page's
    /// new maximum would: at the prefix the page is stored at, compressed in place when that is what makes room,
    /// and split at the right edge when nothing does (see <see cref="InsertIntoLeaf"/>, <see cref="InsertSeparator"/>).</summary>
    private void Append(IndexDef index, List<SpinePage> spine, int level, Entry entry)
    {
        SpinePage page = spine[level];
        page.Entries.Add(entry);
        page.KeyBytes += entry.Key.Length;

        int share = Share(page.Entries);
        int keep = Math.Min(page.Stored, share);
        if (LeafFits(page.Entries.Count, page.KeyBytes, keep)) { StoreAt(page, level, keep); return; }
        if (share > keep && LeafFits(page.Entries.Count, page.KeyBytes, share)) { StoreAt(page, level, share); return; }

        // Full: the page as it stood, compressed in place when its entries share more than it is stored at, and
        // then split with the new entry starting the next page.
        page.Entries.RemoveAt(page.Entries.Count - 1);
        page.KeyBytes -= entry.Key.Length;
        Materialize(page, level, page.Entries, page.Stored);
        int oldShare = Share(page.Entries);
        if (oldShare > page.Stored)
        {
            page.Stored = oldShare;
            Materialize(page, level, page.Entries, oldShare);
        }
        SplitSpine(index, spine, level, entry);
    }

    /// <summary>Moves a spine page to <paramref name="prefix"/>. When that changes its layout, the write before
    /// the entry just appended is what stands past the new live end, so it is made concrete first.</summary>
    private void StoreAt(SpinePage page, int level, int prefix)
    {
        if (prefix == page.Stored) return;
        Materialize(page, level, page.Entries.GetRange(0, page.Entries.Count - 1), page.Stored);
        page.Stored = prefix;
    }

    /// <summary>What writing <paramref name="entries"/> at <paramref name="prefix"/> over the page leaves.</summary>
    private void Materialize(SpinePage page, int level, List<Entry> entries, int prefix) =>
        page.Image = Merge(BuildSpine(page, level, next: 0, entries, prefix), page.Image);

    private byte[] BuildSpine(SpinePage page, int level, int next, List<Entry> entries, int prefix) =>
        Build(level == 0 ? PageType.LeafIndexPage : PageType.IntermediateIndexPage, page.Previous, next, page.Tail,
            level, entries, prefix)
        ?? throw new NotSupportedException("An index page overflows (a key wider than half a page).");

    /// <summary>The right-edge split of the spine page at <paramref name="level"/>, as
    /// <see cref="SplitAndPropagate"/> makes it: the finished left half is written, the new entry starts the
    /// right half, which takes the page's place on the spine, and the separator goes up a level — or, when the
    /// page is the root, both halves move out and the root becomes the node over them.</summary>
    private void SplitSpine(IndexDef index, List<SpinePage> spine, int level, Entry entry)
    {
        SpinePage page = spine[level];
        bool root = level == spine.Count - 1;
        int left = root ? AllocateIndexPage(index) : page.Number;
        int right = AllocateIndexPage(index);

        byte[] leftBytes, promoted;
        int rightTail;
        if (level == 0)
        {
            // Every old entry stays, at the prefix the page is stored at; a copy of the last is promoted.
            Entry last = page.Entries[^1];
            promoted = WithTrailer(last.Key, last.Trailer);
            leftBytes = Merge(Build(PageType.LeafIndexPage, page.Previous, right, tail: 0, level: 0, page.Entries,
                page.Stored)!, page.Image);
            rightTail = 0;
        }
        else
        {
            // A node's right-edge split: its last entry is promoted, that entry's child becoming the left node's
            // tail, and the new separator starts the right node under the old tail.
            Entry middle = page.Entries[^1];
            List<Entry> kept = page.Entries.GetRange(0, page.Entries.Count - 1);
            promoted = middle.Key;
            leftBytes = Merge(Build(PageType.IntermediateIndexPage, page.Previous, right, middle.Trailer, level, kept)!,
                page.Image);
            LeaveMiddleBehind(leftBytes, page.Previous, right, level, kept, middle);
            rightTail = page.Tail;
        }
        _channel.WritePage(left, leftBytes);

        var next = new SpinePage(right, previous: left, ImageOf(right)) { Tail = rightTail };
        next.Entries.Add(entry);
        next.KeyBytes = entry.Key.Length;
        spine[level] = next;

        if (root)
        {
            // The root keeps its page and becomes the node over both halves; its image stays past the live end.
            var top = new SpinePage(page.Number, previous: 0, page.Image) { Tail = right };
            top.Entries.Add(new Entry(promoted, left));
            top.KeyBytes = promoted.Length;
            spine.Add(top);
            return;
        }

        spine[level + 1].Tail = right;
        Append(index, spine, level + 1, new Entry(promoted, left));
    }

    private static int Share(List<Entry> entries) =>
        entries.Count <= 1 ? 0 : CommonPrefixLength(entries[0].Key, entries[^1].Key);

    /// <summary>Whether <paramref name="count"/> entries totalling <paramref name="keyBytes"/> of key data fit
    /// one leaf at prefix <paramref name="prefix"/> — Build's layout, without building anything.</summary>
    private bool LeafFits(int count, long keyBytes, int prefix) =>
        EntryDataOffset + keyBytes + (long)TrailerSize * count - (long)prefix * (count - 1) <= _channel.PageSize;

    /// <summary>Orders two entries as their stored <c>key ++ trailer</c> bytes compare, without building either.</summary>
    internal static int CompareEntries(byte[] aKey, int aTrailer, byte[] bKey, int bTrailer)
    {
        int shared = Math.Min(aKey.Length, bKey.Length);
        for (int i = 0; i < shared; i++)
            if (aKey[i] != bKey[i]) return aKey[i] - bKey[i];

        // The keys agree as far as the shorter runs, so the shorter one's trailer meets the longer one's
        // remaining key bytes — the same crossing CompareWithTrailer handles, and why this cannot be
        // "compare keys, then compare trailers".
        return aKey.Length == bKey.Length
            ? CompareTrailers(aTrailer, bTrailer)
            : aKey.Length < bKey.Length
                ? -CompareWithTrailer(bKey, bTrailer, WithTrailer(aKey, aTrailer))
                : CompareWithTrailer(aKey, aTrailer, WithTrailer(bKey, bTrailer));
    }

    private static int CompareTrailers(int a, int b)
    {
        for (int shift = 24; shift >= 0; shift -= 8)
        {
            int x = (byte)(a >> shift), y = (byte)(b >> shift);
            if (x != y) return x - y;
        }

        return 0;
    }

    /// <summary>
    /// Compares <paramref name="key"/> ++ its 4-byte big-endian <paramref name="trailer"/> against
    /// <paramref name="other"/>, byte for byte, without building the concatenation.
    /// </summary>
    /// <remarks>
    /// Exactly <c>CompareBytes(WithTrailer(key, trailer), other)</c>, and it has to be: comparing the keys
    /// alone and then breaking the tie on the trailer is NOT the same relation. <see cref="CompareBytes"/>
    /// compares the shared prefix and only then falls back to length, so when one key is a prefix of another
    /// the real comparison runs on into the trailer bytes — precisely the case a naive rewrite gets wrong, and
    /// it would misplace an entry rather than fail.
    /// </remarks>
    internal static int CompareWithTrailer(byte[] key, int trailer, ReadOnlySpan<byte> other)
    {
        int total = key.Length + TrailerSize;
        int n = Math.Min(total, other.Length);
        for (int i = 0; i < n; i++)
        {
            // Past the key, read the trailer's bytes most-significant first, as WriteInt32Be lays them out.
            int mine = i < key.Length ? key[i] : (byte)(trailer >> (8 * (TrailerSize - 1 - (i - key.Length))));
            if (mine != other[i]) return mine - other[i];
        }

        return total - other.Length;
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