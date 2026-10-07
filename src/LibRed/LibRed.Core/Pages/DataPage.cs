using LibRed.Formats;
using LibRed.IO;
using LibRed.Storage;
using System.Buffers.Binary;

namespace LibRed.Pages;

/// <summary>
/// A data page holding the rows of a single table. Rows are addressed by a slot
/// directory near the front of the page and packed from the page end backward.
/// Long-value (memo/OLE) pages share this page type but carry the "LVAL" owner marker.
/// </summary>
public sealed class DataPage : Page
{
    /// <summary>One entry in a data page's row slot directory.</summary>
    /// <param name="Offset">Byte offset of the row record within the page.</param>
    /// <param name="Length">Length of the row record in bytes.</param>
    /// <param name="IsDeleted">The row is marked deleted.</param>
    /// <param name="HasOverflow">The slot points at an overflow/lookup record rather than inline data.</param>
    public readonly record struct RowSlot(int Offset, int Length, bool IsDeleted, bool HasOverflow);


    private readonly List<DataPage.RowSlot> _rows = [];
    private PageBuffer _buffer;

    public override PageType Type => PageType.DataPage;

    /// <summary>Marks this data page released, preserving its row directory and data.</summary>
    internal static void MarkReleased(Span<byte> page) => PageHeader.WriteType(page, PageType.ReleasedDataPage);

    /// <summary>The TDEF page of the table that owns this data page (0 for long-value pages).</summary>
    public int OwningTablePage { get; private set; }

    /// <summary>True when this is a long-value (memo/OLE overflow) page.</summary>
    public bool IsLongValuePage { get; private set; }

    public int FreeSpace { get; private set; }
    public int RowCount { get; private set; }

    public IReadOnlyList<DataPage.RowSlot> Rows => _rows;

    internal override void Read(PageBuffer buffer, JetFormatBase format)
    {
        ValidateHeader(buffer, format, out int rowCount, out int directoryEnd);
        _buffer = buffer;
        PageNumber = buffer.PageNumber;

        uint owner = ReadOwner(buffer.Span, format);
        IsLongValuePage = owner == JetFormatBase.LongValuePageMarker;
        OwningTablePage = IsLongValuePage ? 0 : (int)owner;

        FreeSpace = ReadFreeSpace(buffer.Span, format);
        RowCount = rowCount;

        _rows.Clear();
        int prevEnd = buffer.Length;
        for (int i = 0; i < RowCount; i++)
        {
            (int offset, RowSlotFlags flags) = ReadSlot(buffer.Span, format, i);

            // Rows are packed from the page end backward, so a slot runs from its own
            // offset up to where the previous slot's row began.
            ValidateSlot(buffer, i, offset, prevEnd, directoryEnd);
            _rows.Add(Slot(offset, prevEnd - offset, flags));
            prevEnd = offset;
        }
    }

    private static DataPage.RowSlot Slot(int offset, int length, RowSlotFlags flags) =>
        new(offset, length, flags.HasFlag(RowSlotFlags.Deleted), flags.HasFlag(RowSlotFlags.Overflow));

    /// <summary>A slot's <see cref="RowSlotFlags"/>, the inverse of how <see cref="Read"/> reads them.</summary>
    internal static RowSlotFlags Flags(DataPage.RowSlot slot) =>
        (slot.IsDeleted ? RowSlotFlags.Deleted : RowSlotFlags.None)
        | (slot.HasOverflow ? RowSlotFlags.Overflow : RowSlotFlags.None);

    /// <summary>The page's owner field: the TDEF page of the table it belongs to, <see cref="JetFormatBase.LongValuePageMarker"/>
    /// on a long-value page, and 0 on a usage-map page, which belongs to no table.</summary>
    internal static uint ReadOwner(ReadOnlySpan<byte> page, JetFormatBase format) =>
        BinaryPrimitives.ReadUInt32LittleEndian(page.Slice(format.DataOwnerOffset, sizeof(uint)));

    /// <summary>A fresh data page belonging to <paramref name="owner"/> (as <see cref="ReadOwner"/> reads it), with no
    /// rows: a zero count, and the whole page free. Every data page this engine creates starts here.</summary>
    internal static byte[] NewPage(JetFormatBase format, uint owner)
    {
        var page = new byte[format.PageSize];
        PageHeader.WriteType(page, PageType.DataPage);
        BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(format.DataOwnerOffset, sizeof(uint)), owner);
        LayRows(page, format, [], []);
        return page;
    }

    /// <summary>The page's row count — the number of entries in its slot directory.</summary>
    internal static int ReadRowCount(ReadOnlySpan<byte> page, JetFormatBase format) =>
        BinaryPrimitives.ReadUInt16LittleEndian(page.Slice(format.DataRowCountOffset, sizeof(ushort)));

    /// <summary>The page's declared free space: the bytes between the end of the slot directory and the lowest row.</summary>
    internal static int ReadFreeSpace(ReadOnlySpan<byte> page, JetFormatBase format) =>
        BinaryPrimitives.ReadUInt16LittleEndian(page.Slice(format.DataFreeSpaceOffset, sizeof(ushort)));

    /// <summary>Where a slot directory of <paramref name="rowCount"/> entries ends — the lowest offset a row may take.</summary>
    internal static int DirectoryEnd(JetFormatBase format, int rowCount) =>
        format.DataRowDirectoryOffset + rowCount * format.DataRowDirectoryEntrySize;

    private static void WriteHeader(Span<byte> page, JetFormatBase format, int rowCount, int freeSpace)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(page.Slice(format.DataRowCountOffset, sizeof(ushort)), (ushort)rowCount);
        BinaryPrimitives.WriteUInt16LittleEndian(page.Slice(format.DataFreeSpaceOffset, sizeof(ushort)), (ushort)freeSpace);
    }

    /// <summary>The slot directory entry of <paramref name="row"/>: the row's offset, and the
    /// <see cref="RowSlotFlags"/> in the bits above it. Unvalidated — the caller bounds the row and the offset.</summary>
    internal static (int Offset, RowSlotFlags Flags) ReadSlot(ReadOnlySpan<byte> page, JetFormatBase format, int row)
    {
        int raw = BinaryPrimitives.ReadUInt16LittleEndian(
            page.Slice(format.DataRowDirectoryOffset + row * format.DataRowDirectoryEntrySize, format.DataRowDirectoryEntrySize));
        return (raw & format.DataRowOffsetMask, (RowSlotFlags)(raw & ~format.DataRowOffsetMask));
    }

    /// <summary>Writes the slot directory entry of <paramref name="row"/> — the inverse of <see cref="ReadSlot"/>.</summary>
    internal static void WriteSlot(Span<byte> page, JetFormatBase format, int row, int offset, RowSlotFlags flags) =>
        BinaryPrimitives.WriteUInt16LittleEndian(
            page.Slice(format.DataRowDirectoryOffset + row * format.DataRowDirectoryEntrySize, format.DataRowDirectoryEntrySize),
            (ushort)((int)flags | (offset & format.DataRowOffsetMask)));

    /// <summary>The offset of the lowest row on the page — where the next row packed from the end goes below —
    /// or the page size when there are none.</summary>
    internal static int LowestRowOffset(ReadOnlySpan<byte> page, JetFormatBase format)
    {
        int lowest = format.PageSize;
        for (int i = ReadRowCount(page, format) - 1; i >= 0; i--)
            lowest = Math.Min(lowest, ReadSlot(page, format, i).Offset);
        return lowest;
    }

    /// <summary>
    /// Lays <paramref name="records"/> on <paramref name="page"/> from its end backward in slot order — row 0
    /// nearest the end — each slot carrying its own <paramref name="flags"/>, and sets the row count and free space
    /// to match. A zero-length record is a tombstone on the previous row's start. Every write that rearranges a
    /// page's rows comes through here; what precedes the directory and what lies between it and the rows is left as
    /// it stands. Returns false, writing nothing, when the records and their directory do not fit the page.
    /// </summary>
    internal static bool LayRows(Span<byte> page, JetFormatBase format, IReadOnlyList<byte[]> records,
        IReadOnlyList<RowSlotFlags> flags)
    {
        int directoryEnd = DirectoryEnd(format, records.Count);
        if (format.PageSize - records.Sum(r => r.Length) < directoryEnd) return false;

        int offset = format.PageSize;
        for (int i = 0; i < records.Count; i++)
        {
            offset -= records[i].Length;
            records[i].CopyTo(page[offset..]);
            WriteSlot(page, format, i, offset, flags[i]);
        }
        WriteHeader(page, format, records.Count, offset - directoryEnd);
        return true;
    }

    /// <summary>
    /// Appends <paramref name="record"/> below the page's lowest row as a new slot carrying <paramref name="flags"/>,
    /// leaving every existing row where it is, and updates the row count and free space — the declared free space
    /// less the record and its slot, so bytes the page already counted are not recounted. <paramref name="row"/> is
    /// the new slot — the old row count. Returns false, writing nothing, when the record and its slot do not fit
    /// between the directory and the lowest row.
    /// </summary>
    internal static bool TryAppendRow(Span<byte> page, JetFormatBase format, ReadOnlySpan<byte> record,
        RowSlotFlags flags, out int row)
    {
        row = ReadRowCount(page, format);
        int offset = LowestRowOffset(page, format) - record.Length;
        if (offset < DirectoryEnd(format, row + 1)) return false;

        record.CopyTo(page[offset..]);
        WriteSlot(page, format, row, offset, flags);
        WriteHeader(page, format, row + 1,
            ReadFreeSpace(page, format) - record.Length - format.DataRowDirectoryEntrySize);
        return true;
    }

    /// <summary>
    /// Takes a row's bytes off its page the way ACE does: the rows stored below it slide up to close the
    /// gap, their slot offsets follow, and the emptied slot becomes a zero-length tombstone whose offset is
    /// the row's FORMER END, flagged deleted + overflow. The freed bytes go back to the page's free-space
    /// count, so a delete-heavy table stops growing where ACE's would not.
    /// <para>
    /// Slot <i>indices</i> never move, which is what keeps index entries and row ids valid — only offsets
    /// change. Pointing the tombstone at the former end rather than at the page end is what keeps the
    /// directory non-increasing, which <see cref="Read"/> relies on to derive each row's length from the
    /// previous slot. Verified against ACE for a first, middle and last row: deleting the first of three
    /// 19-byte rows gives <c>D000 0FED 0FDA</c>, the middle <c>0FED CFED 0FDA</c>, the last
    /// <c>0FED 0FDA CFDA</c>, with free space rising by 19 in each case.
    /// </para>
    /// </summary>
    internal static void ReclaimRow(Span<byte> page, JetFormatBase format, int row)
    {
        int rowCount = ReadRowCount(page, format);

        // Slot offsets are non-increasing with slot index, so row i occupies [offset(i), offset(i-1)) and
        // every row stored below this one is simply a LATER slot. Working by slot index rather than by
        // comparing offsets is what keeps zero-length tombstones correct: one sitting at exactly this row's
        // offset has to move up with the rows after it, and an offset comparison leaves it behind — where it
        // then absorbs this row's length and starves the next live row down to zero.
        int start = ReadSlot(page, format, row).Offset;
        int end = row == 0 ? format.PageSize : ReadSlot(page, format, row - 1).Offset;
        int length = end - start;

        int lowest = rowCount > 0 ? ReadSlot(page, format, rowCount - 1).Offset : format.PageSize;
        if (length > 0 && start > lowest)
            page[lowest..start].CopyTo(page[(lowest + length)..]);

        for (int i = row + 1; i < rowCount; i++)
        {
            (int offset, RowSlotFlags flags) = ReadSlot(page, format, i);
            WriteSlot(page, format, i, offset + length, flags);
        }

        WriteSlot(page, format, row, end, RowSlotFlags.Deleted | RowSlotFlags.Overflow);
        WriteHeader(page, format, rowCount, ReadFreeSpace(page, format) + length);
    }

    /// <summary>Returns the raw bytes of the row at <paramref name="index"/> in the slot directory.</summary>
    /// <exception cref="InvalidDataException">The index is outside the slot directory. Callers pass a row
    /// number read out of the file (a usage-map pointer, a long-value descriptor), so out of range means
    /// corruption, not a caller bug.</exception>
    public ReadOnlySpan<byte> GetRow(int index)
    {
        if (index < 0 || index >= _rows.Count)
            throw new InvalidDataException(
                $"Row {index} is outside this page's slot directory ({_rows.Count} rows).");

        DataPage.RowSlot slot = _rows[index];
        return _buffer.Slice(slot.Offset, slot.Length);
    }

    /// <summary>Reads a single row's slot and bytes straight from the raw page directory in O(1), without
    /// materialising the whole slot list the way <see cref="Read"/> does — for an index seek's per-row fetch,
    /// where parsing every slot on the page just to take one row was the dominant cost. Returns false when
    /// <paramref name="index"/> is past the page's row count.</summary>
    public static bool TryReadRow(PageBuffer buffer, JetFormatBase format, int index,
        out DataPage.RowSlot slot, out ReadOnlySpan<byte> bytes)
    {
        ValidateHeader(buffer, format, out int rowCount, out int directoryEnd);
        if (index < 0 || index >= rowCount)
        {
            slot = default;
            bytes = default;
            return false;
        }

        (int offset, RowSlotFlags flags) = ReadSlot(buffer.Span, format, index);
        // Rows pack from the page end backward, so this slot runs up to where the previous slot began
        // (or the page end for slot 0) — read just those two directory entries instead of walking all of them.
        int prevEnd = index == 0 ? buffer.Length : ReadSlot(buffer.Span, format, index - 1).Offset;
        ValidateSlot(buffer, index, offset, prevEnd, directoryEnd);
        slot = Slot(offset, prevEnd - offset, flags);
        bytes = buffer.Slice(offset, prevEnd - offset);
        return true;
    }

    private static void ValidateHeader(PageBuffer buffer, JetFormatBase format, out int rowCount, out int directoryEnd)
    {
        if (buffer.Length != format.PageSize)
            throw new InvalidDataException(
                $"Data page {buffer.PageNumber} has {buffer.Length} bytes; expected {format.PageSize}.");
        if (PageHeader.ReadType(buffer.Span) != PageType.DataPage)
            throw new InvalidDataException(
                $"Page {buffer.PageNumber} is type 0x{(ushort)PageHeader.ReadType(buffer.Span):X4}, not a data page (0x0101).");

        rowCount = ReadRowCount(buffer.Span, format);
        // An index addresses a row by a one-byte slot number, so a page can hold at most 255 and ACE stops
        // there. Reading further is not tolerance, it is reading rows no index can name — including any this
        // engine wrote past the cap, which is exactly the bug a strict reader is here to surface.
        if (rowCount > format.MaxRowsPerPage)
            throw new InvalidDataException(
                $"Data page {buffer.PageNumber} declares {rowCount} rows; a page holds at most "
                + $"{format.MaxRowsPerPage}, the most a one-byte slot number can address.");
        directoryEnd = DirectoryEnd(format, rowCount); // the count is capped above, so this cannot overflow
        if (directoryEnd > buffer.Length)
            throw new InvalidDataException(
                $"Data page {buffer.PageNumber} declares {rowCount} rows, placing its slot directory past the page.");
    }

    private static void ValidateSlot(PageBuffer buffer, int index, int offset, int previousEnd, int directoryEnd) =>
        ValidateSlot(buffer.PageNumber, buffer.Length, index, offset, previousEnd, directoryEnd);

    /// <summary>The slot-directory invariant, over raw values so the write paths can share it. Offsets are
    /// masked out of <see cref="JetFormatBase.DataRowOffsetMask"/> and so can exceed the page; rows are packed from the page end
    /// backward, so they must also never increase. <c>RowInserter</c>'s in-place repackers re-derived this
    /// arithmetic without the checks, which on a corrupt directory produced an out-of-range exception from a
    /// half-repacked page rather than a diagnosis.</summary>
    internal static void ValidateSlot(
        int pageNumber, int bufferLength, int index, int offset, int previousEnd, int directoryEnd)
    {
        if (offset < directoryEnd || offset > bufferLength)
            throw new InvalidDataException(
                $"Data page {pageNumber} row slot {index} has offset {offset}, outside the row heap " +
                $"[{directoryEnd}, {bufferLength}].");
        if (offset > previousEnd)
            throw new InvalidDataException(
                $"Data page {pageNumber} row slot {index} has offset {offset}, above the previous row boundary {previousEnd}.");
    }

    /// <summary>A validated, page-backed relocated-row target. The bytes remain zero-copy for index seeks.</summary>
    internal readonly record struct RelocatedRow(PageBuffer Buffer, DataPage.RowSlot Slot, int RowNumber)
    {
        public ReadOnlySpan<byte> Bytes => Buffer.Slice(Slot.Offset, Slot.Length);
    }



    /// <summary>
    /// Validates and follows the forward pointer at the START of a live overflow row slot.
    /// </summary>
    /// <remarks>
    /// The slot is normally exactly 4 bytes: ACE's DML and <see cref="RowInserter"/> both trim it down to the
    /// pointer when a row is relocated. Measured over 317 relocations with no exception, across ACE x64, the
    /// ACE 2010 x86 runtime, and LibRed's own writer, under growing and shrinking text, repeated re-relocation,
    /// page fragmentation by interleaved deletes, and an OLE column going from NULL to a value.
    ///
    /// Real files nevertheless contain longer ones. Northwind's <c>MSysAccessStorage</c> has live overflow slots
    /// of 45-63 bytes, and their contents are the row as it was BEFORE it moved, with only the leading 4 bytes
    /// replaced by the pointer: every field lands where the row format puts it once those 4 bytes are discounted,
    /// the keys match the row it forwards to, and the remnant's null bitmap differs from its target's in exactly
    /// the OLE column's bit — the value whose arrival grew the row and forced the move. The slot simply kept the
    /// old row's width.
    ///
    /// What wrote them is NOT known: no write path reproduces the shape, including the OLE-column transition the
    /// bytes themselves record. So this reads the leading pointer and ignores whatever follows, rather than
    /// asserting a width. The checks that matter are unchanged and do the real work — the target must be in the
    /// file, owned by the same table, and a nonempty hidden inline row.
    /// </remarks>
    internal static RelocatedRow ResolveRelocation(PageChannel channel, int owningTablePage,
        DataPage.RowSlot sourceSlot, ReadOnlySpan<byte> sourceBytes)
    {
        if (sourceSlot.IsDeleted || !sourceSlot.HasOverflow)
            throw new InvalidDataException("A relocation source must be a live overflow row slot.");
        if (sourceBytes.Length < PageBuffer.RecordPointerSize)
            throw new InvalidDataException(
                $"A relocation source must begin with a {PageBuffer.RecordPointerSize}-byte pointer; found {sourceBytes.Length} bytes.");

        (int rowNumber, int pageNumber) = PageBuffer.ReadRecordPointer(sourceBytes, 0);
        if (pageNumber <= 0 || pageNumber >= channel.PageCount)
            throw new InvalidDataException(
                $"Relocation pointer targets page {pageNumber}, outside the file's 1..{channel.PageCount - 1} range.");

        PageBuffer targetBuffer = channel.ReadPageShared(pageNumber);
        uint owner = DataPage.ReadOwner(targetBuffer.Span, channel.Format);
        if (owner != (uint)owningTablePage)
            throw new InvalidDataException(
                $"Relocation target page {pageNumber} belongs to TDEF {owner}, not TDEF {owningTablePage}.");
        if (!DataPage.TryReadRow(targetBuffer, channel.Format, rowNumber, out DataPage.RowSlot targetSlot, out _))
            throw new InvalidDataException(
                $"Relocation pointer targets missing row {rowNumber} on page {pageNumber}.");
        if (!targetSlot.IsDeleted || targetSlot.HasOverflow || targetSlot.Length == 0)
            throw new InvalidDataException(
                $"Relocation target {pageNumber}:{rowNumber} is not a nonempty hidden inline row.");

        return new RelocatedRow(targetBuffer, targetSlot, rowNumber);
    }

}