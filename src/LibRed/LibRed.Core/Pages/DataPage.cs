using LibRed.Formats;
using LibRed.IO;

namespace LibRed.Pages;

/// <summary>One entry in a data page's row slot directory.</summary>
/// <param name="Offset">Byte offset of the row record within the page.</param>
/// <param name="Length">Length of the row record in bytes.</param>
/// <param name="IsDeleted">The row is marked deleted.</param>
/// <param name="HasOverflow">The slot points at an overflow/lookup record rather than inline data.</param>
public readonly record struct RowSlot(int Offset, int Length, bool IsDeleted, bool HasOverflow);

/// <summary>
/// A data page holding the rows of a single table. Rows are addressed by a slot
/// directory near the front of the page and packed from the page end backward.
/// Long-value (memo/OLE) pages share this page type but carry the "LVAL" owner marker.
/// </summary>
public sealed class DataPage : Page
{

    private readonly List<RowSlot> _rows = [];
    private PageBuffer _buffer;

    public override PageType Type => PageType.DataPage;

    /// <summary>The TDEF page of the table that owns this data page (0 for long-value pages).</summary>
    public int OwningTablePage { get; private set; }

    /// <summary>True when this is a long-value (memo/OLE overflow) page.</summary>
    public bool IsLongValuePage { get; private set; }

    public int FreeSpace { get; private set; }
    public int RowCount { get; private set; }

    public IReadOnlyList<RowSlot> Rows => _rows;

    public override void Read(PageBuffer buffer, JetFormatBase format)
    {
        ValidateHeader(buffer, format, out int rowCount, out int directoryEnd);
        _buffer = buffer;
        PageNumber = buffer.PageNumber;

        uint owner = buffer.ReadUInt32(format.DataOwnerOffset);
        IsLongValuePage = owner == LongValueFormat.LvalMarker;
        OwningTablePage = IsLongValuePage ? 0 : (int)owner;

        FreeSpace = buffer.ReadUInt16(format.DataFreeSpaceOffset);
        RowCount = rowCount;

        _rows.Clear();
        int prevEnd = buffer.Length;
        for (int i = 0; i < RowCount; i++)
        {
            int raw = buffer.ReadUInt16(format.DataRowDirectoryOffset + i * 2);
            int offset = raw & RowPointer.OffsetMask;
            bool deleted = (raw & RowPointer.DeletedFlag) != 0;
            bool overflow = (raw & RowPointer.OverflowFlag) != 0;

            // Rows are packed from the page end backward, so a slot runs from its own
            // offset up to where the previous slot's row began.
            ValidateSlot(buffer, i, offset, prevEnd, directoryEnd);
            int length = prevEnd - offset;
            _rows.Add(new RowSlot(offset, length, deleted, overflow));
            prevEnd = offset;
        }
    }

    /// <summary>Returns the raw bytes of the row at <paramref name="index"/> in the slot directory.</summary>
    public ReadOnlySpan<byte> GetRow(int index)
    {
        RowSlot slot = _rows[index];
        return _buffer.Slice(slot.Offset, slot.Length);
    }

    /// <summary>Reads a single row's slot and bytes straight from the raw page directory in O(1), without
    /// materialising the whole slot list the way <see cref="Read"/> does — for an index seek's per-row fetch,
    /// where parsing every slot on the page just to take one row was the dominant cost. Returns false when
    /// <paramref name="index"/> is past the page's row count.</summary>
    public static bool TryReadRow(PageBuffer buffer, JetFormatBase format, int index,
        out RowSlot slot, out ReadOnlySpan<byte> bytes)
    {
        ValidateHeader(buffer, format, out int rowCount, out int directoryEnd);
        if (index < 0 || index >= rowCount)
        {
            slot = default;
            bytes = default;
            return false;
        }

        int dir = format.DataRowDirectoryOffset;
        int raw = buffer.ReadUInt16(dir + index * 2);
        int offset = raw & RowPointer.OffsetMask;
        // Rows pack from the page end backward, so this slot runs up to where the previous slot began
        // (or the page end for slot 0) — read just those two directory entries instead of walking all of them.
        int prevEnd = index == 0 ? buffer.Length : buffer.ReadUInt16(dir + (index - 1) * 2) & RowPointer.OffsetMask;
        ValidateSlot(buffer, index, offset, prevEnd, directoryEnd);
        slot = new RowSlot(offset, prevEnd - offset, (raw & RowPointer.DeletedFlag) != 0, (raw & RowPointer.OverflowFlag) != 0);
        bytes = buffer.Slice(offset, prevEnd - offset);
        return true;
    }

    private static void ValidateHeader(PageBuffer buffer, JetFormatBase format, out int rowCount, out int directoryEnd)
    {
        if (buffer.Length != format.PageSize)
            throw new InvalidDataException(
                $"Data page {buffer.PageNumber} has {buffer.Length} bytes; expected {format.PageSize}.");
        if (buffer.ReadByte(0) != (byte)PageType.DataPage)
            throw new InvalidDataException(
                $"Page {buffer.PageNumber} is type 0x{buffer.ReadByte(0):X2}, not a data page (0x01).");

        rowCount = buffer.ReadUInt16(format.DataRowCountOffset);
        long end = (long)format.DataRowDirectoryOffset + rowCount * 2L;
        if (end > buffer.Length)
            throw new InvalidDataException(
                $"Data page {buffer.PageNumber} declares {rowCount} rows, placing its slot directory past the page.");
        directoryEnd = (int)end;
    }

    private static void ValidateSlot(PageBuffer buffer, int index, int offset, int previousEnd, int directoryEnd)
    {
        if (offset < directoryEnd || offset > buffer.Length)
            throw new InvalidDataException(
                $"Data page {buffer.PageNumber} row slot {index} has offset {offset}, outside the row heap " +
                $"[{directoryEnd}, {buffer.Length}].");
        if (offset > previousEnd)
            throw new InvalidDataException(
                $"Data page {buffer.PageNumber} row slot {index} has offset {offset}, above the previous row boundary {previousEnd}.");
    }
}
