using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;

namespace LibRed.Storage;

/// <summary>
/// Inserts a row into an existing data page of a table. This first cut only fills free space on
/// the table's already-owned pages — it does not yet allocate a new page or update indexes — so
/// it is valid for tables whose last data page has room. The page's slot directory grows forward
/// while row data is packed from the page end backward (see <see cref="DataPage"/>).
/// </summary>
public sealed class RowInserter(PageChannel channel, TableDef table)
{
    private readonly PageChannel _channel = channel;
    private readonly UsageMapWriter _usageMaps = new(channel);
    private readonly TableDef _table = table;

    /// <summary>Encodes and writes <paramref name="values"/> (aligned to column Index) into the table.</summary>
    public void Insert(object?[] values) => Insert(values, updateIndexes: true);

    /// <summary>
    /// Inserts a row, optionally skipping index maintenance. Heap-only inserts are used for the
    /// MSysObjects catalog row (whose text indexes are not yet writable), which the catalog reader
    /// finds by table scan anyway.
    /// </summary>
    public void Insert(object?[] values, bool updateIndexes)
    {
        JetFormatBase format = _channel.Format;

        // Assign AutoNumber ids for any AutoNumber column the caller left unset (the usual case —
        // Jet SQL omits the AutoNumber column from the insert). An explicitly supplied value is kept
        // as-is (Jet, unlike SQL Server, permits explicit AutoNumber values); either way the row's
        // final id drives both the row encoding and the high-water update below.
        AssignAutoNumbers(format, values);
        if (updateIndexes) EnforceUniqueIndexes(values); // reject a duplicate before writing anything

        // Index keys are encoded from the *logical* values. MaterializeLongValues replaces a memo/OLE value
        // with its on-disk LongValueDescriptor, and a Memo column IS indexable (its key is the collation key
        // of the first 255 characters), so snapshot the values first and key the index off that snapshot.
        object?[] keyValues = updateIndexes ? (object?[])values.Clone() : values;
        MaterializeLongValues(values);

        // Encode first: the fixed-region length is pinned by any existing row (to match Access),
        // or derived from the columns for a just-created empty table.
        var encoder = new RowEncoder(_table.Columns, format, InferFixedDataLength(format));
        byte[] record = encoder.Encode(values);

        // Then find an owned page with room for the record plus its 2-byte slot entry.
        (int pageNumber, byte[] page) = FindPageWithRoom(format, record.Length + 2);

        int rowCount = BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(format.DataRowCountOffset, 2));
        int freeSpace = BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(format.DataFreeSpaceOffset, 2));

        // Rows are packed from the page end backward, with strictly decreasing slot offsets,
        // so the new row goes just below the current lowest row start.
        int lowestOffset = LowestRowOffset(page, format, rowCount);
        int newOffset = lowestOffset - record.Length;
        record.CopyTo(page.AsSpan(newOffset));

        // Append the slot, bump the row count, shrink free space by row + slot-entry bytes.
        // The new row's slot index is the old row count, giving its row id on this page.
        BinaryPrimitives.WriteUInt16LittleEndian(
            page.AsSpan(format.DataRowDirectoryOffset + rowCount * 2, 2), (ushort)(newOffset & RowPointer.OffsetMask));
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataRowCountOffset, 2), (ushort)(rowCount + 1));
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataFreeSpaceOffset, 2), (ushort)(freeSpace - record.Length - 2));

        _channel.WritePage(pageNumber, page);

        UpdateTdefCounters(format, values);
        if (updateIndexes)
            UpdateIndexes(keyValues, new RowId(pageNumber, rowCount));
    }

    /// <summary>
    /// Rewrites an existing row in place at its slot (page + row index preserved, matching Access, so
    /// index rowid pointers stay valid). Any changed memo/OLE value is re-materialized onto LVAL pages;
    /// the page is repacked to absorb a size change (slot order = physical order, as Access keeps it).
    /// Throws if the row no longer fits its page (relocation not implemented yet); index-key maintenance for
    /// a changed indexed column is the caller's responsibility. Does not touch the old LVAL pages (freeing
    /// them is a follow-up).
    /// </summary>
    public void Update(RowId id, object?[] values, IReadOnlySet<int> changedColumns)
    {
        JetFormatBase format = _channel.Format;

        // Long-value (memo/OLE) columns: keep an unchanged column's on-disk descriptor verbatim (so it is not
        // needlessly re-materialised onto fresh LVAL pages), and free a changed column's old chained pages.
        var oldDescriptors = new RowDecoder(_table.Columns, format).LongValueRaw(ReadRowBytes(id));
        foreach (ColumnDef column in _table.Columns)
        {
            if (column.Type is not (JetDataType.Memo or JetDataType.Ole)) continue;
            if (!oldDescriptors.TryGetValue(column.Index, out byte[]? oldDescriptor)) continue; // old value was null
            if (changedColumns.Contains(column.Index)) FreeLongValue(column, oldDescriptor);
            else values[column.Index] = new LongValueDescriptor(oldDescriptor);
        }

        MaterializeLongValues(values);

        byte[] srcPage = _channel.ReadPageShared(id.Page).Span.ToArray();
        // Use the same guarded inference as Insert (Math.Max with the column-derived length): the raw per-row
        // parse returns a negative length for an all-fixed-column table (no variable columns — e.g. Northwind
        // Order Details), which without the guard would overflow `new byte[len]`.
        var encoder = new RowEncoder(_table.Columns, format, InferFixedDataLength(format));
        byte[] record = encoder.Encode(values);

        int raw = BinaryPrimitives.ReadUInt16LittleEndian(srcPage.AsSpan(format.DataRowDirectoryOffset + id.Row * 2, 2));
        if ((raw & RowPointer.OverflowFlag) != 0)
        {
            // This slot is a 4-byte forward pointer to the real (relocated) row; rewrite it on its target page
            // (which keeps its hidden "deleted" flag). If it grows past that page too, we'd need to re-relocate.
            DataPage.TryReadRow(new PageBuffer(srcPage, id.Page), format, id.Row,
                out RowSlot sourceSlot, out ReadOnlySpan<byte> sourceBytes);
            RelocatedRow target = RowRelocationReader.Resolve(
                _channel, _table.DefinitionPage, sourceSlot, sourceBytes);
            if (!TryRewriteRowInPlace(target.Buffer.PageNumber, target.RowNumber, record))
                throw new NotSupportedException("Re-relocating an already-relocated row that grew again is not supported yet.");
            return;
        }

        // Normal row: rewrite in place if it still fits its page (row id preserved).
        if (TryRewriteRowInPlace(id.Page, id.Row, record)) return;

        // It no longer fits: relocate the row to another page as a hidden ("deleted") record, and turn this
        // slot into a 4-byte forward pointer (row id preserved, so index entries stay valid) — Access's own
        // overflow mechanism, verified against ACE.
        (int targetPage, int targetRow) = WriteHiddenRow(format, record);
        var pointerBytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(pointerBytes, (targetPage << 8) | targetRow);
        TryRewriteRowInPlace(id.Page, id.Row, pointerBytes, addFlags: RowPointer.OverflowFlag); // 4 bytes always fits
    }

    /// <summary>Rewrites the row at (page, slot) in place, repacking the page from the end in slot order so
    /// every row id is preserved and each slot keeps its flags (plus <paramref name="addFlags"/> on the
    /// target). Returns false without writing if the row no longer fits the page.</summary>
    private bool TryRewriteRowInPlace(int pageNumber, int slot, byte[] record, int addFlags = 0)
    {
        JetFormatBase format = _channel.Format;
        // Pool the transient working page: read into a rented buffer, repack in place, write back, return it —
        // instead of allocating (and immediately discarding) two 4 KB arrays (ReadPage's + ToArray's) per rewrite.
        byte[] page = ArrayPool<byte>.Shared.Rent(format.PageSize);
        try
        {
            _channel.ReadPage(pageNumber, page);
            int rowCount = BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(format.DataRowCountOffset, 2));

            var rows = new byte[rowCount][];
            var rawDir = new int[rowCount];
            int prevEnd = format.PageSize;
            for (int i = 0; i < rowCount; i++)
            {
                int raw = BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(format.DataRowDirectoryOffset + i * 2, 2));
                rawDir[i] = raw;
                int offset = raw & RowPointer.OffsetMask;
                rows[i] = page.AsSpan(offset, prevEnd - offset).ToArray(); // preserve every row's bytes (deleted/overflow included)
                prevEnd = offset;
            }
            rows[slot] = record;
            rawDir[slot] |= addFlags;

            int total = rows.Sum(r => r.Length);
            if (total > format.PageSize - format.DataRowDirectoryOffset - rowCount * 2) return false;

            int off = format.PageSize;
            for (int i = 0; i < rowCount; i++)
            {
                off -= rows[i].Length;
                rows[i].CopyTo(page.AsSpan(off));
                BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataRowDirectoryOffset + i * 2, 2),
                    (ushort)((rawDir[i] & ~RowPointer.OffsetMask) | (off & RowPointer.OffsetMask)));
            }
            BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataFreeSpaceOffset, 2),
                (ushort)(off - format.DataRowDirectoryOffset - rowCount * 2));
            _channel.WritePage(pageNumber, page.AsSpan(0, format.PageSize));
            return true;
        }
        finally { ArrayPool<byte>.Shared.Return(page); }
    }

    /// <summary>Writes a relocated row's bytes onto a page with room, as a hidden slot (Access marks it
    /// "deleted" so scans skip it there — it's only reached via the forward pointer). Returns its location.</summary>
    private (int Page, int Row) WriteHiddenRow(JetFormatBase format, byte[] record)
    {
        (int pageNumber, byte[] page) = FindPageWithRoom(format, record.Length + 2);
        int rowCount = BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(format.DataRowCountOffset, 2));
        int freeSpace = BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(format.DataFreeSpaceOffset, 2));
        int newOffset = LowestRowOffset(page, format, rowCount) - record.Length;
        record.CopyTo(page.AsSpan(newOffset));

        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataRowDirectoryOffset + rowCount * 2, 2),
            (ushort)((newOffset & RowPointer.OffsetMask) | RowPointer.DeletedFlag)); // hidden target
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataRowCountOffset, 2), (ushort)(rowCount + 1));
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataFreeSpaceOffset, 2), (ushort)(freeSpace - record.Length - 2));
        _channel.WritePage(pageNumber, page);
        return (pageNumber, rowCount);
    }

    /// <summary>
    /// Soft-deletes the row at <paramref name="id"/> — sets the deleted flag (0x8000) on its slot (the bytes
    /// stay; scans and Access skip it) and decrements the TDEF row count (0x10), matching Access. The caller
    /// removes the row's index entries first. (The row's LVAL pages, if any, are not reclaimed yet.)
    /// </summary>
    public void Delete(RowId id)
    {
        JetFormatBase format = _channel.Format;

        // Free the deleted row's chained long-value pages.
        var oldDescriptors = new RowDecoder(_table.Columns, format).LongValueRaw(ReadRowBytes(id));
        foreach (ColumnDef column in _table.Columns)
            if (column.Type is JetDataType.Memo or JetDataType.Ole && oldDescriptors.TryGetValue(column.Index, out byte[]? d))
                FreeLongValue(column, d);

        byte[] page = ArrayPool<byte>.Shared.Rent(format.PageSize);
        try
        {
            _channel.ReadPage(id.Page, page);
            int dir = format.DataRowDirectoryOffset + id.Row * 2;
            ushort entry = BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(dir, 2));
            BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(dir, 2), (ushort)(entry | RowPointer.DeletedFlag));
            _channel.WritePage(id.Page, page.AsSpan(0, format.PageSize));
        }
        finally { ArrayPool<byte>.Shared.Return(page); }

        byte[] tdef = ArrayPool<byte>.Shared.Rent(format.PageSize);
        try
        {
            _channel.ReadPage(_table.DefinitionPage, tdef);
            int rowCount = BinaryPrimitives.ReadInt32LittleEndian(tdef.AsSpan(format.TdefRowCountOffset, 4));
            BinaryPrimitives.WriteInt32LittleEndian(tdef.AsSpan(format.TdefRowCountOffset, 4), rowCount - 1);
            _channel.WritePage(_table.DefinitionPage, tdef.AsSpan(0, format.PageSize));
        }
        finally { ArrayPool<byte>.Shared.Return(tdef); }
    }

    /// <summary>The full inline bytes of the row at <paramref name="id"/> (following an overflow pointer).</summary>
    public byte[] ReadRow(RowId id) => ReadRowBytes(id);

    /// <summary>Rewrites the row at <paramref name="id"/> with the exact <paramref name="record"/> bytes given,
    /// handling growth the same way <see cref="Update"/> does — repack in place if it fits, else relocate as a
    /// hidden row with a forward pointer. The row id is preserved (so index entries stay valid).</summary>
    public void RewriteRowRaw(RowId id, byte[] record)
    {
        JetFormatBase format = _channel.Format;
        byte[] srcPage = _channel.ReadPageShared(id.Page).Span.ToArray();
        int raw = BinaryPrimitives.ReadUInt16LittleEndian(srcPage.AsSpan(format.DataRowDirectoryOffset + id.Row * 2, 2));
        if ((raw & RowPointer.OverflowFlag) != 0)
        {
            DataPage.TryReadRow(new PageBuffer(srcPage, id.Page), format, id.Row,
                out RowSlot sourceSlot, out ReadOnlySpan<byte> sourceBytes);
            RelocatedRow target = RowRelocationReader.Resolve(
                _channel, _table.DefinitionPage, sourceSlot, sourceBytes);
            if (!TryRewriteRowInPlace(target.Buffer.PageNumber, target.RowNumber, record))
                throw new NotSupportedException("Re-relocating an already-relocated row that grew again is not supported yet.");
            return;
        }
        if (TryRewriteRowInPlace(id.Page, id.Row, record)) return;
        (int targetPage, int targetRow) = WriteHiddenRow(format, record);
        var pointerBytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(pointerBytes, (targetPage << 8) | targetRow);
        TryRewriteRowInPlace(id.Page, id.Row, pointerBytes, addFlags: RowPointer.OverflowFlag);
    }

    /// <summary>The full inline bytes of the row at <paramref name="id"/>, following an overflow-forward
    /// pointer to the row's real location if the slot has been relocated.</summary>
    private byte[] ReadRowBytes(RowId id)
    {
        JetFormatBase format = _channel.Format;
        PageBuffer page = _channel.ReadPageShared(id.Page);
        if (!DataPage.TryReadRow(page, format, id.Row, out RowSlot slot, out ReadOnlySpan<byte> bytes))
            throw new InvalidDataException($"Row {id.Page}:{id.Row} does not exist.");
        if (!slot.HasOverflow) return bytes.ToArray();

        return RowRelocationReader.Resolve(_channel, _table.DefinitionPage, slot, bytes).Bytes.ToArray();
    }

    /// <summary>
    /// Reclaims the LVAL pages of a replaced/deleted long value. A <b>chained</b> value owns dedicated pages
    /// (one chunk per page): each is freed to the global map and cleared from the column's owned/free maps.
    /// Inline (0x80) values have no pages; single-page (0x40) values share a page with others, so reclaiming
    /// their row is deferred (they are left in place — a small, shared-page leak).
    /// </summary>
    private void FreeLongValue(ColumnDef column, byte[] descriptor)
    {
        byte flags = descriptor[3];
        if ((flags & 0x80) != 0 || (flags & 0x40) != 0) return; // inline or single (shared) page — not reclaimed here

        TableDefinitionPage definition = ReadDefinition();
        definition.LongValueOwnedMaps.TryGetValue(column.ColumnId, out (int Row, int Page) owned);
        definition.LongValueFreeMaps.TryGetValue(column.ColumnId, out (int Row, int Page) free);
        var allocator = new PageAllocator(_channel);
        var reader = new LongValueReader(_channel);
        _ = reader.ResolveWithPages(descriptor, out IReadOnlyList<int> pages);
        HashSet<int> ownedPages = MapPages(owned.Row, owned.Page).ToHashSet();
        _ = MapPages(free.Row, free.Page); // validate both mutation targets before the first free
        foreach (int page in pages)
            if (!ownedPages.Contains(page))
                throw new InvalidDataException(
                    $"Long-value page {page} is not present in column '{column.Name}'s owned-pages map.");

        // Validation above completes before the first free-map or page mutation. The writes are still not
        // atomic without the planned transaction/savepoint layer, but malformed chains cannot partially free.
        foreach (int page in pages)
        {
            allocator.Free(page);
            _usageMaps.SetBit(owned.Row, owned.Page, page, set: false);
            _usageMaps.SetBit(free.Row, free.Page, page, set: false);
        }
    }

    /// <summary>The raw bytes of slot <paramref name="slot"/> on a data page (walks the packed rows).</summary>
    private static byte[] SlotBytes(byte[] page, JetFormatBase format, int slot)
    {
        int prevEnd = format.PageSize;
        for (int i = 0; i <= slot; i++)
        {
            int offset = BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(format.DataRowDirectoryOffset + i * 2, 2)) & RowPointer.OffsetMask;
            if (i == slot) return page.AsSpan(offset, prevEnd - offset).ToArray();
            prevEnd = offset;
        }
        throw new ArgumentOutOfRangeException(nameof(slot));
    }

    /// <summary>Adds the new row to every index B-tree (deduped by root page, since relationship
    /// indexes share a real index's data) so indexed lookups — and Access — find it.</summary>
    /// <summary>Rejects the insert if a UNIQUE or PRIMARY index would gain a duplicate key. A row with a
    /// null in any of a unique index's columns is skipped — Jet treats nulls as distinct, so a unique index
    /// allows multiple nulls (verified vs ACE). Runs before the row is written so nothing is half-inserted.</summary>
    private void EnforceUniqueIndexes(object?[] values)
    {
        IndexWriter? writer = null;
        foreach (IndexDef index in _table.Indexes
            .Where(i => i.IsUnique && i.RootPage > 0)
            .GroupBy(i => i.RootPage).Select(g => g.First()))
        {
            if (HasNullKey(index, values)) continue; // nulls are distinct — multiple allowed
            writer ??= new IndexWriter(_channel, _table);
            if (writer.KeyExists(index, values))
                throw new ConstraintViolationException(
                    $"Cannot insert into '{_table.Name}': a row with the same {(index.IsPrimaryKey ? "primary key" : "unique key")} " +
                    $"already exists (index '{index.Name}').",
                    index.Name,
                    index.IsPrimaryKey);
        }
    }

    private void UpdateIndexes(object?[] values, RowId rowId)
    {
        var writer = new IndexWriter(_channel, _table);
        foreach (IndexDef index in _table.Indexes
            .Where(i => i.RootPage > 0)
            .GroupBy(i => i.RootPage)
            .Select(g => g.First()))
        {
            // WITH IGNORE NULL: a row with a null in any indexed column is not added to this index.
            if (index.IgnoreNulls && HasNullKey(index, values)) continue;
            writer.AddEntry(index, values, rowId);
        }
    }

    private static bool HasNullKey(IndexDef index, object?[] values) =>
        index.Columns.Any(c => values[c.Column.Index] is null or DBNull);

    /// <summary>Finds a data page with room for a row of <paramref name="needed"/> bytes, growing the table
    /// if none has.</summary>
    /// <remarks>
    /// Consults the <i>free</i>-pages map, not the owned-pages map. The two agree only while a table is
    /// small: once a page fills, Access clears its free bit (see <see cref="AllocateDataPage"/>), so the free
    /// map is normally just the page currently being appended to. Scanning the owned map instead would read
    /// every already-full page from disk on every insert — O(pages) per row, i.e. quadratic over a bulk load.
    /// </remarks>
    private (int PageNumber, byte[] Page) FindPageWithRoom(JetFormatBase format, int needed)
    {
        foreach (int pageNumber in new UsageMap(_channel, _table).FreeDataPages())
        {
            byte[] page = _channel.ReadPageShared(pageNumber).Span.ToArray();
            int freeSpace = BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(format.DataFreeSpaceOffset, 2));
            if (freeSpace >= needed)
                return (pageNumber, page);
        }

        return AllocateDataPage(format);
    }

    /// <summary>
    /// Grows the table by one data page — like Access does for the first insert into a fresh
    /// (data-page-less) table: takes a page from the global free-pages map, initialises it as an
    /// empty data page owned by this table, and records it in the table's owned- and free-pages
    /// usage maps so both Access and LibRed find it.
    /// </summary>
    private (int PageNumber, byte[] Page) AllocateDataPage(JetFormatBase format)
    {
        // We only allocate when no owned page had room, so the current tail (highest owned data page)
        // is full. Access clears such a page from the free-pages map when it moves past it to a new
        // page — leaving only the page currently being appended to marked free. Match that: clear the
        // old tail's free bit, then set the new page's. (Verified against an ACE sequential fill: only
        // the last of six equally-full pages stays in the free map.)
        int previousTail = new UsageMap(_channel, _table).MaxDataPage();

        int pageNumber = new PageAllocator(_channel).Allocate();

        var page = new byte[format.PageSize];
        page[0] = (byte)PageType.DataPage;
        page[1] = 0x01; // page flags (observed constant)
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(format.DataOwnerOffset, 4), _table.DefinitionPage);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataRowCountOffset, 2), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataFreeSpaceOffset, 2),
            (ushort)(format.PageSize - format.DataRowDirectoryOffset));
        _channel.WritePage(pageNumber, page);

        if (previousTail >= 0)
            UpdateUsageBit(format.TdefFreePagesOffset, previousTail, set: false); // old tail is now full
        UpdateUsageBit(format.TdefOwnedPagesOffset, pageNumber, set: true);
        UpdateUsageBit(format.TdefFreePagesOffset, pageNumber, set: true);        // new tail has room

        return (pageNumber, page);
    }

    /// <summary>Sets or clears the bit for <paramref name="targetPage"/> in the inline usage map
    /// referenced by the TDEF pointer at <paramref name="tdefPointerOffset"/> (row byte + 3-byte page).</summary>
    private void UpdateUsageBit(int tdefPointerOffset, int targetPage, bool set)
    {
        PageBuffer tdef = _channel.ReadPageShared(_table.DefinitionPage);
        _usageMaps.SetBit(tdef.ReadByte(tdefPointerOffset), tdef.ReadInt24(tdefPointerOffset + 1), targetPage, set,
            movableWindow: tdefPointerOffset == _channel.Format.TdefFreePagesOffset);
    }


    /// <summary>Pins the fixed-region length to an existing row anywhere in the table (so the layout
    /// matches Access), or returns null for an empty table (the encoder then derives it).</summary>
    private int? InferFixedDataLength(JetFormatBase format)
    {
        // The current fixed-region end from the column descriptors — this includes a just-added fixed column,
        // whereas an existing row pins only the length as of when it was written.
        int derived = _table.Columns.Where(c => c.IsFixedLength)
            .Select(c => c.FixedOffset + c.Length).DefaultIfEmpty(0).Max();

        // A table with no variable columns has no variable-offset table in its rows (ACE omits it), so a row
        // can't pin the fixed length — and the schema fully determines it. Let the encoder derive it.
        if (_table.Columns.All(c => c.IsFixedLength))
            return null;

        foreach (int pageNumber in new UsageMap(_channel, _table).DataPages())
        {
            byte[] page = _channel.ReadPageShared(pageNumber).Span.ToArray();
            if (InferFixedDataLength(page, format) is { } pinned)
                return Math.Max(pinned, derived); // ADD COLUMN of a fixed column grows the region past old rows
        }
        return null; // empty table → RowEncoder derives the same length from the columns
    }

    private static int LowestRowOffset(byte[] page, JetFormatBase format, int rowCount)
    {
        int lowest = format.PageSize;
        for (int i = 0; i < rowCount; i++)
        {
            int raw = BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(format.DataRowDirectoryOffset + i * 2, 2));
            lowest = Math.Min(lowest, raw & RowPointer.OffsetMask);
        }
        return lowest;
    }

    /// <summary>
    /// Reads the fixed-region length straight off an existing inline row: its variable-offset
    /// table's last entry is the variable-data start (= columnCount field + fixed region), so the
    /// fixed length is that minus the leading column-count field.
    /// </summary>
    private int? InferFixedDataLength(byte[] page, JetFormatBase format)
    {
        int rowCount = BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(format.DataRowCountOffset, 2));

        // Take the largest sane fixed length across the page's inline rows rather than trusting the first one:
        // some rows decode to a nonsensical (e.g. zero) variable-data offset, which would yield a negative
        // length — those are ignored, and the real rows agree on the true fixed length.
        int? best = null;
        int prevEnd = format.PageSize;
        for (int i = 0; i < rowCount; i++)
        {
            int raw = BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(format.DataRowDirectoryOffset + i * 2, 2));
            int offset = raw & RowPointer.OffsetMask;
            int length = prevEnd - offset;
            prevEnd = offset;

            if ((raw & (RowPointer.DeletedFlag | RowPointer.OverflowFlag)) != 0) continue;
            if (length < format.RowColumnCountSize + 2) continue;

            ReadOnlySpan<byte> row = page.AsSpan(offset, length);
            // An old row written before the table's first variable ADD has no variable trailer even though
            // the current schema does. Its stored count cannot cover that new variable column, which lets us
            // distinguish the verified all-fixed form without interpreting fixed bytes as trailer offsets.
            int storedCount = BinaryPrimitives.ReadUInt16LittleEndian(row[..format.RowColumnCountSize]);
            bool rowHasVar = _table.Columns.Any(c => !c.IsFixedLength && c.ColumnId < storedCount);
            RowLayout layout = RowLayout.Parse(row, format.RowColumnCountSize, rowHasVar);
            int varDataStart = layout.FixedRegionLength + format.RowColumnCountSize;

            // The variable-data start is the end of the fixed region; it must lie after the column-count field
            // and no later than the offset table. A row that fails this decoded garbage — skip it.
            int candidate = layout.FixedRegionLength;
            if (candidate > 0 && (!rowHasVar || varDataStart <= layout.VarTableStart))
                best = best is null ? candidate : Math.Max(best.Value, candidate);
        }
        return best;
    }

    /// <summary>
    /// Fills each AutoNumber column the caller left null with the next id — the TDEF high-water value
    /// (`0x14`) plus one — matching how Jet assigns AutoNumbers. A value the caller supplied
    /// explicitly is left untouched (Jet allows it, and <see cref="UpdateTdefCounters"/> then bumps
    /// the high-water to it). Access permits only one AutoNumber column per table, but any number are
    /// handled here for safety.
    /// </summary>
    /// <summary>
    /// Access stores a memo/OLE value <b>inline</b> only up to 64 bytes (Jackcess
    /// <c>MAX_INLINE_LONG_VALUE_SIZE</c>, the same for Jet3/Jet4); a larger value goes on its own LVAL
    /// page. Inlining a long value works for LibRed's own reader but Access rejects it (e.g. it opens the
    /// database yet fails to run a view whose subquery Expression is inlined). So for each memo/OLE column
    /// whose value exceeds the inline limit, write it to an LVAL page and substitute the 12-byte reference
    /// descriptor (short values, and pre-built descriptors from other callers, are left as-is to inline).
    /// </summary>
    private void MaterializeLongValues(object?[] values)
    {
        const int maxInline = 64; // Jackcess MAX_INLINE_LONG_VALUE_SIZE (Jet3 and Jet4)
        LongValueWriter? writer = null;
        TableDefinitionPage? definition = null;

        foreach (ColumnDef column in _table.Columns)
        {
            if (column.Type is not (JetDataType.Memo or JetDataType.Ole)) continue;
            byte[]? payload = values[column.Index] switch
            {
                string s => Encoding.Unicode.GetBytes(s), // memo: UTF-16LE
                byte[] b => b,                             // OLE: raw bytes
                _ => null,                                 // null, or an already-built LongValueDescriptor
            };
            if (payload is null || payload.Length <= maxInline) continue;

            writer ??= new LongValueWriter(_channel);
            definition ??= ReadDefinition();
            definition.LongValueOwnedMaps.TryGetValue(column.ColumnId, out (int Row, int Page) owned);
            definition.LongValueFreeMaps.TryGetValue(column.ColumnId, out (int Row, int Page) free);
            values[column.Index] = new LongValueDescriptor(StoreLongValue(writer, payload, owned, free));
        }
    }

    // A page is dropped from the free-pages map once it cannot hold the smallest long value (a 65-byte
    // payload — anything up to 64 inlines — plus its 2-byte row-directory entry).
    private const int MaxLvalRowSize = 4076; // one LVAL page row (Jackcess MAX_LONG_VALUE_ROW_SIZE, Jet4)
    private const int MinLvalRow = 65 + 2;

    /// <summary>Stores <paramref name="payload"/> on an LVAL page for long-value column
    /// <paramref name="columnId"/> — packing onto a free page as usual — and returns the in-row descriptor.
    /// For a caller that must use a page regardless of size (the MSysObjects <c>LvProp</c> property blob,
    /// which Access reads only from a page, never inline). Call before <see cref="Insert(object?[], bool)"/>
    /// so the row carries the returned descriptor as a <see cref="LongValueDescriptor"/>.</summary>
    public byte[] StorePackedLongValue(int columnId, byte[] payload)
    {
        TableDefinitionPage definition = ReadDefinition();
        definition.LongValueOwnedMaps.TryGetValue(columnId, out (int Row, int Page) owned);
        definition.LongValueFreeMaps.TryGetValue(columnId, out (int Row, int Page) free);
        return StoreLongValue(new LongValueWriter(_channel), payload, owned, free);
    }

    /// <summary>
    /// Writes one long value to LVAL page(s) and returns its in-row descriptor, maintaining the column's
    /// §3.3.2 usage maps. A value up to one page is <b>packed</b> onto an existing free page (a page in the
    /// free-pages map with room), the way Access shares a page across many small values; only when none has
    /// room is a fresh page allocated (owned + free). A value larger than one page is chained across
    /// dedicated pages.
    /// </summary>
    private byte[] StoreLongValue(LongValueWriter writer, byte[] payload, (int Row, int Page) owned, (int Row, int Page) free)
    {
        if (owned.Page == 0 || free.Page == 0)
            throw new InvalidDataException("Long-value column has no complete owned/free usage-map pointers.");
        _ = MapPages(owned.Row, owned.Page); // validate both map targets before allocating or writing LVAL pages
        IReadOnlyList<int> freePages = MapPages(free.Row, free.Page);

        if (payload.Length > MaxLvalRowSize)
        {
            LongValueResult chained = writer.Write(payload);
            foreach (int page in chained.OwnedPages) _usageMaps.SetBit(owned.Row, owned.Page, page, set: true);
            _usageMaps.SetBit(free.Row, free.Page, chained.FreePage, set: true, movableWindow: true);
            return chained.Descriptor;
        }

        // Pack onto the first free page that has room for the value plus its directory entry.
        if (free.Page != 0)
            foreach (int page in freePages)
                if (writer.TryAppend(page, payload) is (int row, int remaining))
                {
                    if (remaining < MinLvalRow) _usageMaps.SetBit(free.Row, free.Page, page, set: false); // now full
                    return LongValueWriter.SinglePageDescriptor(payload.Length, page, row);
                }

        // No free page had room: a fresh page (owned, and free — it still has spare room).
        int newPage = writer.WriteNewPage(payload);
        _usageMaps.SetBit(owned.Row, owned.Page, newPage, set: true);
        _usageMaps.SetBit(free.Row, free.Page, newPage, set: true, movableWindow: true);
        return LongValueWriter.SinglePageDescriptor(payload.Length, newPage, 0);
    }

    /// <summary>Reads every page marked in a validated inline or reference usage map.</summary>
    private IReadOnlyList<int> MapPages(int mapRow, int mapPage)
    {
        if (mapPage <= 1 || mapPage >= _channel.PageCount)
            throw new InvalidDataException($"Long-value usage-map page {mapPage} is outside the physical file.");
        var holder = new DataPage();
        holder.Read(_channel.ReadPageShared(mapPage), _channel.Format);
        if (holder.IsLongValuePage || holder.OwningTablePage != 0)
            throw new InvalidDataException(
                $"Long-value usage-map pointer {mapPage}:{mapRow} does not target an owner-zero usage-map data page.");
        if (mapRow < 0 || mapRow >= holder.RowCount)
            throw new InvalidDataException($"Long-value usage-map row {mapPage}:{mapRow} does not exist.");
        RowSlot slot = holder.Rows[mapRow];
        if (slot.IsDeleted || slot.HasOverflow || slot.Length == 0)
            throw new InvalidDataException($"Long-value usage-map row {mapPage}:{mapRow} is deleted, overflowed, or empty.");
        ReadOnlySpan<byte> map = holder.GetRow(mapRow);
        var result = new List<int>();

        if (map[0] == 0x00)
        {
            if (map.Length < 5)
                throw new InvalidDataException("Inline long-value usage map is shorter than its 5-byte header.");
            int startPage = BinaryPrimitives.ReadInt32LittleEndian(map.Slice(1, 4));
            AppendMapBits(result, map[5..], startPage);
            return result;
        }

        if (map[0] != 0x01 || map.Length != 69)
            throw new InvalidDataException(
                $"Long-value usage map has invalid type/length 0x{map[0]:X2}/{map.Length}.");
        int pagesPerBitmap = (_channel.PageSize - 4) * 8;
        var bitmapPages = new HashSet<int>();
        for (int i = 0; i < 17; i++)
        {
            int bitmapPage = BinaryPrimitives.ReadInt32LittleEndian(map.Slice(1 + i * 4, 4));
            if (bitmapPage == 0) continue;
            if (bitmapPage <= 1 || bitmapPage >= _channel.PageCount || !bitmapPages.Add(bitmapPage))
                throw new InvalidDataException($"Long-value usage map has invalid bitmap-page pointer {bitmapPage}.");
            ReadOnlySpan<byte> bitmap = _channel.ReadPageShared(bitmapPage).Span;
            if (bitmap[0] != (byte)PageType.PageUsageBitmap || bitmap[1] != 0x01 || bitmap[2] != 0 || bitmap[3] != 0)
                throw new InvalidDataException($"Long-value usage-map pointer {bitmapPage} is not a bitmap page.");
            AppendMapBits(result, bitmap[4..], i * pagesPerBitmap);
        }
        return result;
    }

    private void AppendMapBits(List<int> result, ReadOnlySpan<byte> bitmap, int startPage)
    {
        for (int i = 0; i < bitmap.Length; i++)
            for (int bit = 0; bit < 8; bit++)
                if ((bitmap[i] & (1 << bit)) != 0)
                {
                    int page = startPage + i * 8 + bit;
                    if (page <= 1 || page >= _channel.PageCount)
                        throw new InvalidDataException(
                            $"Long-value usage map names page {page}, outside the physical reusable range.");
                    result.Add(page);
                }
    }

    private TableDefinitionPage ReadDefinition()
    {
        var definition = new TableDefinitionPage();
        definition.Read(_channel, _table.DefinitionPage);
        return definition;
    }

    private void AssignAutoNumbers(JetFormatBase format, object?[] values)
    {
        bool needed = false;
        foreach (ColumnDef column in _table.Columns)
            if (column.IsAutoNumber && values[column.Index] is null or DBNull) { needed = true; break; }
        if (!needed) return;

        ReadOnlySpan<byte> tdef = _channel.ReadPageShared(_table.DefinitionPage).Span;
        int highWater = BinaryPrimitives.ReadInt32LittleEndian(tdef.Slice(format.TdefLastAutoNumberOffset, 4));

        foreach (ColumnDef column in _table.Columns)
            if (column.IsAutoNumber && values[column.Index] is null or DBNull)
            {
                if (column.IsRandomAutoNumber)
                    // "Random" AutoNumber (DefaultValue = GenUniqueID()): a random Int32, independent of the
                    // sequential counter. Access relies on the PK's uniqueness to reject the rare collision.
                    values[column.Index] = RandomAutoNumber();
                else
                    // Next id = last-assigned + increment. On a fresh table the last value (0x14) is
                    // Seed-Increment, so the first assigned id is the Seed.
                    values[column.Index] = highWater += column.Increment;
            }
    }

    /// <summary>A random non-zero signed Int32 for a "Random" AutoNumber, mirroring Access's <c>GenUniqueID()</c>.</summary>
    private static int RandomAutoNumber()
    {
        int value;
        do { value = Random.Shared.Next(int.MinValue, int.MaxValue); } while (value == 0);
        return value;
    }

    /// <summary>
    /// Updates the TDEF counters Access maintains on insert, in one read-modify-write of the TDEF
    /// page:
    /// <list type="bullet">
    /// <item>Row count (`0x10`) — incremented.</item>
    /// <item>AutoNumber high-water (`0x14`) — set to the max of its current value and the id just
    /// written (Access reads it to pick the *next* id = this + 1; leaving it stale makes Access
    /// reissue an existing id and reject the insert as a duplicate primary key).</item>
    /// <item>Per-index **unique-entry count** (`0x3F + ordinal×12`, `+4`) — incremented by one for
    /// each **unique** index (a unique index gets a distinct key per row). This is the cumulative
    /// count Access advances on every insert and never decrements. The sibling **total-entry count**
    /// (`+0`) is deliberately left untouched: Access does **not** maintain it live — it stays `0`
    /// through inserts and is only written (to the row count) on compact/repair (verified: a live
    /// ACE-inserted table reads total `0` while saved Northwind tables read total = row count).</item>
    /// </list>
    /// </summary>
    private void UpdateTdefCounters(JetFormatBase format, object?[] values)
    {
        byte[] tdef = _channel.ReadPageShared(_table.DefinitionPage).Span.ToArray();

        int count = BinaryPrimitives.ReadInt32LittleEndian(tdef.AsSpan(format.TdefRowCountOffset, 4));
        BinaryPrimitives.WriteInt32LittleEndian(tdef.AsSpan(format.TdefRowCountOffset, 4), count + 1);

        foreach (ColumnDef column in _table.Columns)
        {
            if (!column.IsAutoNumber || values[column.Index] is not { } value) continue;
            // A "Random" AutoNumber leaves the high-water at its default (Access ignores 0x14 for it — verified:
            // a UI-authored Random AutoNumber reads last-value 0). Advancing it would be meaningless (random ids
            // don't form a monotone sequence) and diverge from Access's on-disk state.
            if (column.IsRandomAutoNumber) continue;
            int assigned = Convert.ToInt32(value);
            int highWater = BinaryPrimitives.ReadInt32LittleEndian(tdef.AsSpan(format.TdefLastAutoNumberOffset, 4));
            // Advance 0x14 to the id just written when it moves further in the counter's direction — for a
            // positive increment that's the max seen, for a negative (descending) counter the min. Using max
            // unconditionally would let a descending counter reissue the previous id (duplicate key).
            // NB deliberate divergence from Access: ACE seeds 0x14 from the *last inserted* value regardless
            // of direction (KB 884185), so an explicit lower INSERT drops the counter and the next auto id
            // collides with an existing row ("duplicate values in the index/primary key"). LibRed's monotone
            // rule is immune — verified both sides in AutoNumberSeed[Immunity]Tests.
            bool advances = column.Increment >= 0 ? assigned > highWater : assigned < highWater;
            int newHighWater = advances ? assigned : highWater;
            if (advances)
                BinaryPrimitives.WriteInt32LittleEndian(tdef.AsSpan(format.TdefLastAutoNumberOffset, 4), assigned);
            // Keep the cached catalog seed in sync with the on-disk 0x14. The insert path itself reads the
            // high-water from disk (AssignAutoNumbers), so this isn't needed for assigning ids — but RewriteColumn
            // (an ALTER on a table that has an AutoNumber) reconstructs the counter from the cached
            // ColumnDef.Seed; if left stale, a rebuild after the high rows were deleted resets the counter to its
            // create-time value (verified: next id dropped to 1 instead of continuing past 6). Seed = next id.
            column.Seed = newHighWater + column.Increment;
        }

        // TODO(non-unique-index-stats): a non-unique index's unique-entry count must advance only
        // when the inserted key is genuinely new (Access's cumulative-distinct semantics), which
        // needs a probe of the existing keys. LibRed only creates unique (PK) indexes today, so we
        // handle just those; extend this when secondary/non-unique indexes are supported.
        foreach (IndexDef index in _table.Indexes)
        {
            if (!index.IsUnique) continue;
            if (index.IgnoreNulls && HasNullKey(index, values)) continue; // row was excluded from the index
            int statsUnique = format.TdefRealIndexBlockOffset + index.RealIndexOrdinal * format.RealIndexEntrySize + 4;
            int unique = BinaryPrimitives.ReadInt32LittleEndian(tdef.AsSpan(statsUnique, 4));
            BinaryPrimitives.WriteInt32LittleEndian(tdef.AsSpan(statsUnique, 4), unique + 1);
        }

        _channel.WritePage(_table.DefinitionPage, tdef);
    }
}
