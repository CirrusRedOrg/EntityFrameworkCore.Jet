using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;
using System.Buffers.Binary;

namespace LibRed.Storage;

/// <summary>
/// Enumerates the data pages that belong to a table by reading its owned-pages usage map.
/// </summary>
/// <remarks>
/// The TDEF holds a pointer (row + page) to the usage-map record. An inline map
/// (<see cref="UsageMapType.Inline"/>) stores a start page and a bitmap where bit i marks page (startPage + i)
/// as owned. A reference map (<see cref="UsageMapType.Reference"/>, for very large tables) instead stores a list
/// of pointers to dedicated bitmap pages (page type 0x0105); pointer k's bitmap covers the page range starting
/// at k * <see cref="JetFormatBase.UsageMapPagesPerBitmapPage"/>.
/// </remarks>
internal sealed class UsageMap(PageChannel channel, TableDefinition? table = null)
{
    /// <summary>Builds the global map page: two full-width inline maps. Row 0 is the free map — pages &lt;
    /// <paramref name="usedPages"/> are used (bit 0); pages from there to the map's reach are marked <b>free</b>
    /// (bit 1), pre-declaring space beyond the file end so an allocator (LibRed's or Access's) grabs a "free" page
    /// and grows the file. Row 1 is the released map, empty, as real files carry.</summary>
    internal static byte[] BuildGlobalMapPage(JetFormatBase format, int usedPages)
    {
        // The holder's owner field reads 1 on the global map page (observed), where a table's usage-map page has 0.
        byte[] page = UsageMap.NewMapPage(format, mapCount: 2, owner: 1);

        Span<byte> freeMap = UsageMap.InlineBits(
            page.AsSpan(DataPage.ReadSlot(page, format, 0).Offset, format.UsageMapInlineRecordSize), format); // row 0's
        for (int p = usedPages; p < format.UsageMapInlineBitmapSize * 8; p++)
            BitmapBits.Set(freeMap, p, true);
        return page;
    }

    private readonly PageChannel _channel = channel;
    private readonly TableDefinition? _table = table;

    private int DefinitionPage => _table?.DefinitionPage
        ?? throw new InvalidOperationException("A table definition is required to enumerate its data pages.");

    /// <summary>Yields the page numbers of every data page owned by the table, in ascending order.</summary>
    public IEnumerable<int> DataPages() => PagesAt(_channel.Format.TdefOwnedPagesOffset);

    /// <summary>Yields the table's data pages that still have room for a row, in ascending order — the
    /// free-pages map. A strict subset of <see cref="DataPages"/>, and normally just the page currently
    /// being appended to, so it is the map to consult when looking for somewhere to put a new row.</summary>
    public IEnumerable<int> FreeDataPages() => PagesAt(_channel.Format.TdefFreePagesOffset);

    /// <summary>The highest-numbered data page the table owns, or -1 when it owns none.</summary>
    public int MaxDataPage() => EdgeDataPage(fromEnd: true);

    /// <summary>The lowest-numbered data page the table owns, or -1 when it owns none.</summary>
    public int MinDataPage() => EdgeDataPage(fromEnd: false);

    /// <summary>The owned-pages map's last (<paramref name="fromEnd"/>) or first page.</summary>
    /// <remarks>
    /// Scans the bitmap from one end rather than enumerating <see cref="DataPages"/> and taking the extreme:
    /// callers ask this on every page allocation and on every delete that empties a page, and materializing
    /// every owned page each time would make a bulk load quadratic. Cost here is bounded by the bitmap size,
    /// not the table's page count. Both ends share this walk so only the scan direction differs.
    /// </remarks>
    private int EdgeDataPage(bool fromEnd)
    {
        JetFormatBase format = _channel.Format;
        PageBuffer tdef = _channel.ReadPage(DefinitionPage);
        (int row, int page) = tdef.ReadRecordPointer(format.TdefOwnedPagesOffset);
        ReadOnlySpan<byte> record = ReadRecordAt(row, page);

        if (RecordType(record) == UsageMapType.Inline)
        {
            int bit = EdgeSetBit(record[format.UsageMapInlineHeaderSize..], fromEnd);
            return bit < 0 ? -1 : StartPage(record, format) + bit;
        }

        for (int k = 0; k < format.UsageMapReferenceSlots; k++)
        {
            int e = fromEnd ? format.UsageMapReferenceSlots - 1 - k : k;
            int bitmapPage = ReferencePointer(record, e, format);
            if (bitmapPage == 0) continue;

            int found = EdgeSetBit(BitmapPageBits(ReadBitmapPage(_channel, bitmapPage).Span, format), fromEnd);
            if (found >= 0) return e * format.UsageMapPagesPerBitmapPage + found;
        }
        return -1;
    }

    private static int EdgeSetBit(ReadOnlySpan<byte> bitmap, bool fromEnd) =>
        fromEnd ? BitmapBits.LastSetBit(bitmap) : BitmapBits.NextSetBit(bitmap, 0);

    /// <summary>Reads the usage map whose (row, page) pointer sits at <paramref name="pointerOffset"/> in
    /// the TDEF. Both maps share the same pointer shape and record format.</summary>
    private IEnumerable<int> PagesAt(int pointerOffset)
    {
        (int row, int page) = _channel.ReadPage(DefinitionPage).ReadRecordPointer(pointerOffset);
        return PagesInMap(row, page);
    }

    /// <summary>The pages recorded by the usage map at a <paramref name="mapPage"/>:<paramref name="mapRow"/>
    /// pointer. The TDEF's own two maps come through here (<see cref="DataPages"/>, <see cref="FreeDataPages"/>),
    /// and so does every other map reached by an explicit pointer — a long-value column's owned and free maps,
    /// whose pointers sit in the TDEF keyed by column id, and an index's own map.</summary>
    public IEnumerable<int> PagesInMap(int mapRow, int mapPage) =>
        PagesInRecord(_channel, ReadRecordAt(mapRow, mapPage), _channel.PageCount, "A usage map");

    /// <summary>The pages a map <paramref name="record"/> marks, inline or reference form. Pointer k's bitmap covers
    /// the page range starting at k * UsageMapPagesPerBitmapPage; a zero pointer means the range has no pages in the
    /// map, and a bitmap page belongs to one range only. A page at or past <paramref name="rejectBeyond"/> is
    /// corruption; null keeps every bit, for a map that may name pages past the file's end (see
    /// <see cref="BitmapBits.AppendPages"/>). <paramref name="what"/> names the map in the messages.</summary>
    internal static List<int> PagesInRecord(PageChannel channel, ReadOnlySpan<byte> record, int? rejectBeyond, string what)
    {
        JetFormatBase format = channel.Format;
        var pages = new List<int>();
        if (RecordType(record) == UsageMapType.Inline)
        {
            BitmapBits.AppendPages(pages, record[format.UsageMapInlineHeaderSize..], StartPage(record, format),
                rejectBeyond, what);
            return pages;
        }

        var bitmapPages = new HashSet<int>();
        for (int e = 0; e < format.UsageMapReferenceSlots; e++)
        {
            int bitmapPage = ReferencePointer(record, e, format);
            if (bitmapPage == 0) continue;
            if (!bitmapPages.Add(bitmapPage))
                throw new InvalidDataException($"{what} repeats bitmap page {bitmapPage}.");
            BitmapBits.AppendPages(pages, BitmapPageBits(ReadBitmapPage(channel, bitmapPage).Span, format),
                e * format.UsageMapPagesPerBitmapPage, rejectBeyond, what);
        }
        return pages;
    }

    /// <summary>The usage-map record a (row, page) pointer out of a TDEF names: <see cref="ReadRecord"/>, and on top
    /// of it an owner-zero holder — a table's, an index's and a long-value column's maps all live on usage-map
    /// pages that belong to no table.</summary>
    /// <remarks>Both halves of the pointer come out of the TDEF, so both are corruption when wrong. Unchecked,
    /// the page number reached the channel as an out-of-range read and the row number reached GetRow as an
    /// index.</remarks>
    internal ReadOnlySpan<byte> ReadRecordAt(int mapRow, int mapPage)
    {
        (PageBuffer page, _, DataPage.RowSlot slot) = ReadRecord(_channel, mapRow, mapPage, "Usage-map pointer");
        if (DataPage.ReadOwner(page.Span, _channel.Format) != 0)
            throw new InvalidDataException(
                $"Usage-map pointer {mapPage}:{mapRow} does not target an owner-zero usage-map data page.");
        return page.Slice(slot.Offset, slot.Length);
    }

    /// <summary>
    /// The usage-map record a (row, page) pointer names, after proving the pointer names one: a data page inside the
    /// file, a live, non-empty record on it, and a record of a known type and a valid length. The one place a map
    /// record is located — the TDEF's maps here, page 0's global maps in <see cref="PageAllocator"/>, and every map
    /// <see cref="UsageMap"/> writes; <paramref name="what"/> names the pointer in the messages. The holder
    /// comes back parsed as well, for a writer that repacks it.
    /// </summary>
    internal static (PageBuffer Page, DataPage Holder, DataPage.RowSlot Slot) ReadRecord(
        PageChannel channel, int mapRow, int mapPage, string what)
    {
        JetFormatBase format = channel.Format;
        if (mapPage <= 0 || mapPage >= channel.PageCount)
            throw new InvalidDataException(
                $"{what} names page {mapPage}, outside the file's 1..{channel.PageCount - 1} range.");

        PageBuffer page = channel.ReadPageShared(mapPage);
        if (PageHeader.ReadType(page.Span) != PageType.DataPage)
            throw new InvalidDataException($"{what} names page {mapPage}, which is not a data page.");
        var holder = new DataPage();
        holder.Read(page, format);
        if (mapRow < 0 || mapRow >= holder.RowCount)
            throw new InvalidDataException($"{what} names row {mapPage}:{mapRow}, which does not exist.");
        DataPage.RowSlot slot = holder.Rows[mapRow];
        if (slot.IsDeleted || slot.HasOverflow || slot.Length == 0)
            throw new InvalidDataException($"{what} names row {mapPage}:{mapRow}, which is deleted, overflowed, or empty.");

        ReadOnlySpan<byte> record = page.Slice(slot.Offset, slot.Length);
        switch (RecordType(record))
        {
            case UsageMapType.Inline:
                if (record.Length < format.UsageMapInlineHeaderSize)
                    throw new InvalidDataException(
                        $"An inline usage-map record must contain its {format.UsageMapInlineHeaderSize}-byte header.");
                break;
            case UsageMapType.Reference:
                if (record.Length != format.UsageMapReferenceRecordSize)
                    throw new InvalidDataException(
                        $"A reference usage-map record must be exactly {format.UsageMapReferenceRecordSize} bytes; got {record.Length}.");
                break;
            default:
                throw new InvalidDataException($"{what} names a usage map of unknown type 0x{record[0]:X2}.");
        }
        return (page, holder, slot);
    }

    /// <summary>A record's <see cref="UsageMapType"/>, its first byte.</summary>
    internal static UsageMapType RecordType(ReadOnlySpan<byte> record) => (UsageMapType)record[0];

    /// <summary>A new inline record starting at <paramref name="startPage"/> with <paramref name="bitmapBytes"/> bytes
    /// of bitmap, every bit clear.</summary>
    internal static byte[] NewInlineRecord(JetFormatBase format, int startPage, int bitmapBytes)
    {
        var record = new byte[format.UsageMapInlineHeaderSize + bitmapBytes];
        record[0] = (byte)UsageMapType.Inline;
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(format.UsageMapStartPageOffset, sizeof(int)), startPage);
        return record;
    }

    /// <summary>A new reference record with no bitmap pages.</summary>
    internal static byte[] NewReferenceRecord(JetFormatBase format)
    {
        var record = new byte[format.UsageMapReferenceRecordSize];
        record[0] = (byte)UsageMapType.Reference;
        return record;
    }

    /// <summary>An inline record's bitmap — what follows its header.</summary>
    internal static Span<byte> InlineBits(Span<byte> record, JetFormatBase format) =>
        record[format.UsageMapInlineHeaderSize..];

    /// <summary>An inline record's start page.</summary>
    internal static int StartPage(ReadOnlySpan<byte> record, JetFormatBase format) =>
        BinaryPrimitives.ReadInt32LittleEndian(record.Slice(format.UsageMapStartPageOffset, sizeof(int)));

    /// <summary>A reference record's pointer for range <paramref name="slot"/>; 0 when the range has no bitmap page.</summary>
    internal static int ReferencePointer(ReadOnlySpan<byte> record, int slot, JetFormatBase format) =>
        BinaryPrimitives.ReadInt32LittleEndian(record.Slice(ReferencePointerOffset(slot, format), sizeof(int)));

    /// <summary>Writes a reference record's pointer for range <paramref name="slot"/>.</summary>
    internal static void WriteReferencePointer(Span<byte> record, int slot, JetFormatBase format, int bitmapPage) =>
        BinaryPrimitives.WriteInt32LittleEndian(record.Slice(ReferencePointerOffset(slot, format), sizeof(int)), bitmapPage);

    private static int ReferencePointerOffset(int slot, JetFormatBase format) =>
        format.UsageMapReferencePointersOffset + slot * sizeof(int);

    /// <summary>Whether <paramref name="page"/> carries a dedicated bitmap page's complete
    /// <c>[05 01 00 00]</c> header — the check every reader and writer makes before trusting a pointer to one.</summary>
    private static bool IsBitmapPage(ReadOnlySpan<byte> page, JetFormatBase format) =>
        PageHeader.ReadType(page) == PageType.PageUsageBitmap
        && !page[sizeof(ushort)..format.UsageMapBitmapPageHeaderSize].ContainsAnyExcept((byte)0); // past the type: zero

    /// <summary>
    /// The dedicated bitmap page (type 0x0105) a reference record's pointer names, after proving it is one: inside the
    /// file and carrying the complete bitmap-page header. The pointer comes out of the file, so this is the one check
    /// every reader and writer makes before trusting it — the spec states it as mandatory "before any bitmap is
    /// expanded into page numbers", and a writer that skipped it OR'd a bit into an ordinary data, TDEF or index page.
    /// The bytes are the channel's shared copy; a writer copies them before changing any.
    /// </summary>
    internal static PageBuffer ReadBitmapPage(PageChannel channel, int pageNumber)
    {
        if (pageNumber <= 0 || pageNumber >= channel.PageCount)
            throw new InvalidDataException(
                $"Usage map names bitmap page {pageNumber}, outside the file's 1..{channel.PageCount - 1} range.");
        PageBuffer page = channel.ReadPageShared(pageNumber);
        if (!IsBitmapPage(page.Span, channel.Format))
            throw new InvalidDataException(
                $"Page {pageNumber} is named as a usage bitmap but does not carry the [05 01 00 00] header.");
        return page;
    }

    /// <summary>A bitmap page's bits — what follows its header.</summary>
    internal static ReadOnlySpan<byte> BitmapPageBits(ReadOnlySpan<byte> page, JetFormatBase format) =>
        page[format.UsageMapBitmapPageHeaderSize..];

    /// <summary>A bitmap page's bits, writable — over a page copy the caller will write back.</summary>
    internal static Span<byte> BitmapPageBits(byte[] page, JetFormatBase format) =>
        page.AsSpan(format.UsageMapBitmapPageHeaderSize);

    /// <summary>A fresh, empty dedicated bitmap page (type 0x0105).</summary>
    internal static byte[] NewBitmapPage(JetFormatBase format)
    {
        var bitmap = new byte[format.PageSize];
        PageHeader.WriteType(bitmap, PageType.PageUsageBitmap);
        return bitmap;
    }

    /// <summary>A usage-map data page holding <paramref name="mapCount"/> empty full-width inline records —
    /// what Access writes for a fresh table that has no data page yet. Each record is <c>[0x00][startPage = 0]
    /// [all-zero bitmap]</c>, row 0 nearest the page end. A table's map page belongs to no table (owner 0); the
    /// global maps' holder carries <paramref name="owner"/> 1.</summary>
    internal static byte[] NewMapPage(JetFormatBase format, int mapCount, uint owner = 0)
    {
        byte[] page = DataPage.NewPage(format, owner);
        // An all-zero record is an empty inline map: inline type, start page 0, zero bitmap.
        var records = Enumerable.Range(0, mapCount).Select(_ => new byte[format.UsageMapInlineRecordSize]).ToArray();
        DataPage.LayRows(page, format, records, new RowSlotFlags[mapCount]);
        return page;
    }

    /// <summary>How many empty full-width inline records, each with its directory slot, fit in
    /// <paramref name="freeBytes"/> of a usage-map page.</summary>
    internal static int RecordsFitting(JetFormatBase format, int freeBytes) =>
        freeBytes / (format.UsageMapInlineRecordSize + format.DataRowDirectoryEntrySize);

    /// <summary>Sets or clears the bit for <paramref name="targetPage"/> in the usage map at record
    /// <paramref name="mapRow"/> on <paramref name="mapPage"/>.</summary>
    /// <remarks>
    /// Handles both map types. An inline map is grown in place while its record still fits the page; once it
    /// cannot, the map is converted to a reference map — exactly Access's own threshold.
    /// <para>
    /// <paramref name="movableWindow"/> marks a map whose set bits stay clustered near the append tail — a
    /// free-pages map. Rather than growing a bitmap from <c>startPage = 0</c> all the way out to the tail,
    /// such a map slides a fixed full-width window, as Access does, so its record stays full-width forever.
    /// An owned-pages map cannot do this: it must retain every page it has ever taken.
    /// </para>
    /// </remarks>
    public void SetBit(int mapRow, int mapPage, int targetPage, bool set, bool movableWindow = false)
    {
        JetFormatBase format = _channel.Format;
        // The pointer comes out of the file, so the record is located as every reader locates it: a live, non-empty
        // record of a known type and a valid length, or corruption said as such. This writer used to index the slot
        // list bare and fall through on an unknown type byte, mangling the record in place.
        (PageBuffer shared, DataPage holder, DataPage.RowSlot slot) = ReadRecord(_channel, mapRow, mapPage, "Usage-map pointer");
        byte[] page = shared.Span.ToArray();
        int mapOffset = slot.Offset;

        if (RecordType(page.AsSpan(mapOffset)) == UsageMapType.Reference)
        {
            SetReferenceBit(page, mapPage, mapOffset, targetPage, set);
            return;
        }

        int headerSize = format.UsageMapInlineHeaderSize;
        int startPage = StartPage(page.AsSpan(mapOffset), format);
        int bitmapBits = (slot.Length - headerSize) * 8;
        int bitIndex = targetPage - startPage;

        // The inline bitmap covers pages [startPage, startPage + bitmapBits). Clearing a bit outside that
        // window is a no-op — it is already 0.
        if (bitIndex < 0 || bitIndex >= bitmapBits)
        {
            if (!set) return;

            // A free-pages map slides its window onto the target instead of growing (and can therefore also
            // move *backwards*, which a grown map could never do).
            if (movableWindow && TryRepositionWindow(page, holder, format, mapPage, mapRow, targetPage))
                return;

            if (bitIndex < 0)
                throw new NotSupportedException(
                    $"Page {targetPage} is below the usage map's start page {startPage}; this map's window cannot move.");
        }

        // When Access needs to mark a page beyond the window it grows the bitmap record in place (still
        // inline, same startPage), extending it in UsageMapInlineGrowthSize steps.
        if (bitIndex >= bitmapBits)
        {
            int neededBitmapBytes = InlineBitmapBytes(format, bitIndex + 1);
            byte[] grownRecord = new byte[headerSize + neededBitmapBytes]; // extra bitmap bytes stay zero
            page.AsSpan(mapOffset, slot.Length).CopyTo(grownRecord);

            byte[]? grown = ReplaceMapRecord(page, holder, format, mapRow, grownRecord, out mapOffset);
            if (grown is null)
            {
                // The record can no longer grow within its page: switch to a reference map and retry there.
                ConvertInlineToReference(mapPage, mapRow);
                (PageBuffer converted, _, DataPage.RowSlot reference) = ReadRecord(_channel, mapRow, mapPage, "Usage-map pointer");
                SetReferenceBit(converted.Span.ToArray(), mapPage, reference.Offset, targetPage, set);
                return;
            }

            page = grown;
        }

        BitmapBits.Set(InlineBits(page.AsSpan(mapOffset), format), bitIndex, set);
        _channel.WritePage(mapPage, page);
    }

    /// <summary>
    /// Slides an inline map's window onto <paramref name="targetPage"/>: a full-width bitmap starting at the
    /// window boundary below the target, carrying over every bit already set and adding the target's.
    /// Returns <see langword="false"/> — leaving the map untouched — when some page already marked would
    /// fall outside the new window, since moving would silently forget it; the caller then grows instead.
    /// </summary>
    private bool TryRepositionWindow(byte[] page, DataPage holder, JetFormatBase format, int mapPage, int mapRow, int targetPage)
    {
        DataPage.RowSlot slot = holder.Rows[mapRow];
        int headerSize = format.UsageMapInlineHeaderSize;
        int startPage = StartPage(page.AsSpan(slot.Offset), format);
        ReadOnlySpan<byte> bitmap = page.AsSpan(slot.Offset + headerSize, slot.Length - headerSize);

        int windowPages = format.UsageMapInlineBitmapSize * 8;
        int newStart = targetPage / windowPages * windowPages;
        int newEnd = newStart + windowPages;

        var marked = new List<int>();
        BitmapBits.AppendPages(marked, bitmap, startPage, rejectBeyond: null, "A free-pages map");
        bool fitsWindow = marked.TrueForAll(p => p >= newStart && p < newEnd);

        // A window that has already moved above the target cannot slide back down without dropping the pages
        // it still advertises, so it widens instead: the start drops to the lowest page it must cover (rounded
        // down to a byte, as the released map's move does) and the record is sized to reach the highest. That
        // keeps it inline and keeps every bit. Marking a page a table's free map cannot represent is not a
        // corruption — the bit only advertises room — but silently dropping it is what leaves reusable space
        // invisible, and throwing outright failed an ordinary DROP TABLE on a real file (complex1.accdb, whose
        // MSysObjects free map sits at page 2288 while its catalog rows live at page 17).
        int bitmapBytes = format.UsageMapInlineBitmapSize;
        if (!fitsWindow)
        {
            int lowest = Math.Min(targetPage, marked.Count == 0 ? targetPage : marked.Min());
            int highest = Math.Max(targetPage, marked.Count == 0 ? targetPage : marked.Max());
            newStart = lowest / 8 * 8;
            bitmapBytes = InlineBitmapBytes(format, highest - newStart + 1);
        }

        byte[] record = NewInlineRecord(format, newStart, bitmapBytes);
        marked.Add(targetPage);
        foreach (int markedPage in marked)
            BitmapBits.Set(InlineBits(record, format), markedPage - newStart, true);

        byte[]? rewritten = ReplaceMapRecord(page, holder, format, mapRow, record, out _);
        if (rewritten is null) return false; // shrinking or same size, so effectively unreachable

        _channel.WritePage(mapPage, rewritten);
        return true;
    }

    /// <summary>Sets or clears <paramref name="targetPage"/>'s bit in a reference map: pointer slot
    /// <c>targetPage / UsageMapPagesPerBitmapPage</c> names the bitmap page holding it. A slot's bitmap page is
    /// allocated lazily, only when a bit in its range is first set.</summary>
    private void SetReferenceBit(byte[] page, int mapPage, int mapOffset, int targetPage, bool set)
    {
        JetFormatBase format = _channel.Format;
        int pagesPerBitmap = format.UsageMapPagesPerBitmapPage;
        int slot = targetPage / pagesPerBitmap;
        if (slot >= format.UsageMapReferenceSlots)
            throw new NotSupportedException(
                $"Page {targetPage} lies past the {format.UsageMapReferenceSlots} bitmap slots a usage map can address (Jet's 2 GB file limit).");

        Span<byte> record = page.AsSpan(mapOffset);
        int bitmapPage = ReferencePointer(record, slot, format);
        if (bitmapPage == 0)
        {
            if (!set) return; // the bit is already clear — no need to materialize the bitmap page
            bitmapPage = AllocateBitmapPage();
            WriteReferencePointer(record, slot, format, bitmapPage);
            _channel.WritePage(mapPage, page);
        }

        // The pointer comes out of the file, so the page is proven to be a bitmap page before a bit goes into it.
        byte[] bitmap = ReadBitmapPage(_channel, bitmapPage).Span.ToArray();
        BitmapBits.Set(BitmapPageBits(bitmap, format), targetPage % pagesPerBitmap, set);
        _channel.WritePage(bitmapPage, bitmap);
    }

    /// <summary>Zeroes the bitmap of every dedicated bitmap page a reference-form <paramref name="record"/> names,
    /// leaving each page's header, and returns them; nothing for an inline record. ACE does this both when it
    /// clears the global released-pages map at close and when it retires a dropped object's map — even the table's
    /// own owned map, whose bits an inline record keeps.</summary>
    internal List<int> ClearBitmapPages(ReadOnlySpan<byte> record)
    {
        JetFormatBase format = _channel.Format;
        var pages = new List<int>();
        if (RecordType(record) != UsageMapType.Reference) return pages;
        for (int slot = 0; slot < format.UsageMapReferenceSlots; slot++)
        {
            int bitmapPage = ReferencePointer(record, slot, format);
            if (bitmapPage == 0) continue;
            byte[] bitmap = ReadBitmapPage(_channel, bitmapPage).Span.ToArray();
            BitmapPageBits(bitmap, format).Clear();
            _channel.WritePage(bitmapPage, bitmap);
            pages.Add(bitmapPage);
        }
        return pages;
    }

    /// <summary>Allocates and initialises an empty dedicated usage-bitmap page (type 0x0105).</summary>
    private int AllocateBitmapPage()
    {
        int pageNumber = _channel.Allocator.Allocate();
        _channel.WritePage(pageNumber, NewBitmapPage(_channel.Format));
        return pageNumber;
    }

    /// <summary>
    /// Rewrites an inline usage map as a reference map: every page the inline bitmap marked is re-marked in a
    /// dedicated bitmap page, and the record shrinks to the fixed-size pointer table. Bits are grouped by slot
    /// so each bitmap page is written once rather than once per page.
    /// </summary>
    private void ConvertInlineToReference(int mapPage, int mapRow)
    {
        JetFormatBase format = _channel.Format;
        (PageBuffer shared, _, DataPage.RowSlot slot) = ReadRecord(_channel, mapRow, mapPage, "Usage-map pointer");
        byte[] page = shared.Span.ToArray();

        int headerSize = format.UsageMapInlineHeaderSize;
        int startPage = StartPage(page.AsSpan(slot.Offset), format);
        ReadOnlySpan<byte> bitmap = page.AsSpan(slot.Offset + headerSize, slot.Length - headerSize);

        // Every bit kept, as above: this is re-expressing a map the file already holds, not reading one.
        var marked = new List<int>();
        BitmapBits.AppendPages(marked, bitmap, startPage, rejectBeyond: null, "An inline usage map");

        byte[] record = NewReferenceRecord(format);

        int pagesPerBitmap = format.UsageMapPagesPerBitmapPage;
        foreach (IGrouping<int, int> group in marked.GroupBy(p => p / pagesPerBitmap))
        {
            if (group.Key >= format.UsageMapReferenceSlots)
                throw new NotSupportedException(
                    $"Page {group.First()} lies past the {format.UsageMapReferenceSlots} bitmap slots a usage map can address (Jet's 2 GB file limit).");

            int bitmapPage = AllocateBitmapPage();
            byte[] bits = _channel.ReadPage(bitmapPage).Span.ToArray();
            foreach (int ownedPage in group)
                BitmapBits.Set(BitmapPageBits(bits, format), ownedPage % pagesPerBitmap, true);
            _channel.WritePage(bitmapPage, bits);
            WriteReferencePointer(record, group.Key, format, bitmapPage);
        }

        // Re-read: allocating bitmap pages above may have grown the file, though not this page.
        (PageBuffer fresh, DataPage current, _) = ReadRecord(_channel, mapRow, mapPage, "Usage-map pointer");
        byte[] rewritten = ReplaceMapRecord(fresh.Span.ToArray(), current, format, mapRow, record, out _)
            ?? throw new InvalidOperationException("The reference-map record does not fit its usage-map page.");
        _channel.WritePage(mapPage, rewritten);
    }

    /// <summary>The bitmap bytes an inline record needs to cover <paramref name="pages"/> pages from its start page:
    /// whole bytes, rounded up to <see cref="JetFormatBase.UsageMapInlineGrowthSize"/> steps.</summary>
    internal static int InlineBitmapBytes(JetFormatBase format, int pages)
    {
        int unit = format.UsageMapInlineGrowthSize;
        return (BitmapBits.ByteCount(pages) + unit - 1) / unit * unit;
    }

    /// <summary>Replaces the usage-map record at <paramref name="mapRow"/> with <paramref name="newRecord"/>,
    /// repacking every record on the page from the end backward — the way Access enlarges a table's owned/free
    /// bitmap once it spans past the current window. Records keep their directory order (row 0 nearest the
    /// page end), and are laid over the page as it stands: bytes a moved record vacates are not cleared, as ACE
    /// leaves them. Returns the rewritten page and the record's new offset, or <see langword="null"/> if the
    /// record no longer fits the page.</summary>
    internal static byte[]? ReplaceMapRecord(byte[] page, DataPage holder, JetFormatBase format, int mapRow, byte[] newRecord, out int newOffset)
    {
        int rowCount = holder.Rows.Count;
        var records = new byte[rowCount][];
        for (int i = 0; i < rowCount; i++)
            records[i] = page.AsSpan(holder.Rows[i].Offset, holder.Rows[i].Length).ToArray();
        records[mapRow] = newRecord;

        // Carry each slot's flags across, not just its offset. A usage-map page can hold the deleted + overflow
        // tombstone that the index-rebuild recycle deliberately leaves behind (RecycleOwnedMapRow, reproduced
        // byte-for-byte from ACE); rebuilding the entry from the offset alone cleared those two bits and turned
        // that tombstone back into a live zero-length record.
        newOffset = -1;
        byte[] result = [.. page];
        if (!DataPage.LayRows(result, format, records, [.. holder.Rows.Select(DataPage.Flags)])) return null;
        newOffset = DataPage.ReadSlot(result, format, mapRow).Offset;
        return result;
    }

}