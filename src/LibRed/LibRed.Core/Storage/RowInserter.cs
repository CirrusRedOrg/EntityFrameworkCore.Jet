using System.Buffers.Binary;
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
    private const int RowOffsetMask = 0x1FFF;
    private const int DeletedFlag = 0x8000;
    private const int OverflowFlag = 0x4000;

    private readonly PageChannel _channel = channel;
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
            page.AsSpan(format.DataRowDirectoryOffset + rowCount * 2, 2), (ushort)(newOffset & RowOffsetMask));
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataRowCountOffset, 2), (ushort)(rowCount + 1));
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataFreeSpaceOffset, 2), (ushort)(freeSpace - record.Length - 2));

        _channel.WritePage(pageNumber, page);

        UpdateTdefCounters(format, values);
        if (updateIndexes)
            UpdateIndexes(values, new RowId(pageNumber, rowCount));
    }

    /// <summary>Adds the new row to every index B-tree (deduped by root page, since relationship
    /// indexes share a real index's data) so indexed lookups — and Access — find it.</summary>
    private void UpdateIndexes(object?[] values, RowId rowId)
    {
        var writer = new IndexWriter(_channel);
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

    // The TDEF free-pages-map pointer (row + page); the owned-pages pointer lives at
    // format.TdefOwnedPagesOffset. Both maps mark the table's own data pages.
    private const int TdefFreePagesOffset = 0x3B;

    private (int PageNumber, byte[] Page) FindPageWithRoom(JetFormatBase format, int needed)
    {
        foreach (int pageNumber in new UsageMap(_channel, _table).DataPages())
        {
            byte[] page = _channel.ReadPage(pageNumber).Span.ToArray();
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
        int previousTail = new UsageMap(_channel, _table).DataPages().DefaultIfEmpty(-1).Max();

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
            UpdateUsageBit(TdefFreePagesOffset, previousTail, set: false); // old tail is now full
        UpdateUsageBit(format.TdefOwnedPagesOffset, pageNumber, set: true);
        UpdateUsageBit(TdefFreePagesOffset, pageNumber, set: true);        // new tail has room

        return (pageNumber, page);
    }

    /// <summary>Sets or clears the bit for <paramref name="targetPage"/> in the inline usage map
    /// referenced by the TDEF pointer at <paramref name="tdefPointerOffset"/> (row byte + 3-byte page).</summary>
    private void UpdateUsageBit(int tdefPointerOffset, int targetPage, bool set)
    {
        JetFormatBase format = _channel.Format;
        PageBuffer tdef = _channel.ReadPage(_table.DefinitionPage);
        int mapRow = tdef.ReadByte(tdefPointerOffset);
        int mapPage = tdef.ReadInt24(tdefPointerOffset + 1);

        byte[] page = _channel.ReadPage(mapPage).Span.ToArray();
        var holder = new DataPage();
        holder.Read(_channel.ReadPage(mapPage), format);
        int mapOffset = holder.Rows[mapRow].Offset;

        if (page[mapOffset] != 0x00)
            throw new NotSupportedException("Reference-type usage map growth is not implemented yet.");

        // The inline map covers pages [startPage, startPage + bitmapBits). A page outside that window
        // needs a wider start or a reference-type map — neither is implemented; fail loudly rather
        // than writing past the record and corrupting the neighbouring map/slot data.
        int startPage = BinaryPrimitives.ReadInt32LittleEndian(page.AsSpan(mapOffset + 1, 4));
        int bitmapBits = (holder.Rows[mapRow].Length - 5) * 8;
        int bitIndex = targetPage - startPage;
        if (bitIndex < 0 || bitIndex >= bitmapBits)
            throw new NotSupportedException(
                $"Data page {targetPage} falls outside the inline usage map's window " +
                $"[{startPage}, {startPage + bitmapBits}); a wider/reference-type usage map is not implemented yet.");

        int byteIndex = mapOffset + 5 + bitIndex / 8;
        byte mask = (byte)(1 << (bitIndex % 8));
        if (set) page[byteIndex] |= mask;
        else page[byteIndex] &= (byte)~mask;
        _channel.WritePage(mapPage, page);
    }

    /// <summary>Pins the fixed-region length to an existing row anywhere in the table (so the layout
    /// matches Access), or returns null for an empty table (the encoder then derives it).</summary>
    private int? InferFixedDataLength(JetFormatBase format)
    {
        foreach (int pageNumber in new UsageMap(_channel, _table).DataPages())
        {
            byte[] page = _channel.ReadPage(pageNumber).Span.ToArray();
            if (InferFixedDataLength(page, format) is { } length)
                return length;
        }
        return null;
    }

    private static int LowestRowOffset(byte[] page, JetFormatBase format, int rowCount)
    {
        int lowest = format.PageSize;
        for (int i = 0; i < rowCount; i++)
        {
            int raw = BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(format.DataRowDirectoryOffset + i * 2, 2));
            lowest = Math.Min(lowest, raw & RowOffsetMask);
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
        int columnCount = _table.Columns.Count;
        int nullBitmapSize = (columnCount + 7) / 8;

        int prevEnd = format.PageSize;
        for (int i = 0; i < rowCount; i++)
        {
            int raw = BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(format.DataRowDirectoryOffset + i * 2, 2));
            int offset = raw & RowOffsetMask;
            int length = prevEnd - offset;
            prevEnd = offset;

            if ((raw & (DeletedFlag | OverflowFlag)) != 0) continue;
            if (length < format.RowColumnCountSize + 2 + 2 + nullBitmapSize) continue; // not an inline record

            ReadOnlySpan<byte> row = page.AsSpan(offset, length);
            int numVar = BinaryPrimitives.ReadUInt16LittleEndian(row.Slice(row.Length - nullBitmapSize - 2, 2));
            int varTableStart = row.Length - nullBitmapSize - 2 - (numVar + 1) * 2;
            int varDataStart = BinaryPrimitives.ReadUInt16LittleEndian(row.Slice(varTableStart + numVar * 2, 2));
            return varDataStart - format.RowColumnCountSize;
        }

        return null;
    }

    /// <summary>
    /// Fills each AutoNumber column the caller left null with the next id — the TDEF high-water value
    /// (`0x14`) plus one — matching how Jet assigns AutoNumbers. A value the caller supplied
    /// explicitly is left untouched (Jet allows it, and <see cref="UpdateTdefCounters"/> then bumps
    /// the high-water to it). Access permits only one AutoNumber column per table, but any number are
    /// handled here for safety.
    /// </summary>
    private void AssignAutoNumbers(JetFormatBase format, object?[] values)
    {
        bool needed = false;
        foreach (ColumnDef column in _table.Columns)
            if (column.IsAutoNumber && values[column.Index] is null or DBNull) { needed = true; break; }
        if (!needed) return;

        ReadOnlySpan<byte> tdef = _channel.ReadPage(_table.DefinitionPage).Span;
        int highWater = BinaryPrimitives.ReadInt32LittleEndian(tdef.Slice(format.TdefLastAutoNumberOffset, 4));

        foreach (ColumnDef column in _table.Columns)
            if (column.IsAutoNumber && values[column.Index] is null or DBNull)
                values[column.Index] = ++highWater;
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
        byte[] tdef = _channel.ReadPage(_table.DefinitionPage).Span.ToArray();

        int count = BinaryPrimitives.ReadInt32LittleEndian(tdef.AsSpan(format.TdefRowCountOffset, 4));
        BinaryPrimitives.WriteInt32LittleEndian(tdef.AsSpan(format.TdefRowCountOffset, 4), count + 1);

        foreach (ColumnDef column in _table.Columns)
        {
            if (!column.IsAutoNumber || values[column.Index] is not { } value) continue;
            int assigned = Convert.ToInt32(value);
            int highWater = BinaryPrimitives.ReadInt32LittleEndian(tdef.AsSpan(format.TdefLastAutoNumberOffset, 4));
            if (assigned > highWater)
                BinaryPrimitives.WriteInt32LittleEndian(tdef.AsSpan(format.TdefLastAutoNumberOffset, 4), assigned);
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
