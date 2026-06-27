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
    /// <summary>The 4-byte owner marker used by long-value pages: ASCII "LVAL".</summary>
    private const uint LongValueMarker = 0x4C41564C;

    private const int RowOffsetMask = 0x1FFF;
    private const int DeletedFlag = 0x8000;
    private const int OverflowFlag = 0x4000;

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
        _buffer = buffer;
        PageNumber = buffer.PageNumber;

        uint owner = buffer.ReadUInt32(format.DataOwnerOffset);
        IsLongValuePage = owner == LongValueMarker;
        OwningTablePage = IsLongValuePage ? 0 : (int)owner;

        FreeSpace = buffer.ReadUInt16(format.DataFreeSpaceOffset);
        RowCount = buffer.ReadUInt16(format.DataRowCountOffset);

        _rows.Clear();
        int prevEnd = format.PageSize;
        for (int i = 0; i < RowCount; i++)
        {
            int raw = buffer.ReadUInt16(format.DataRowDirectoryOffset + i * 2);
            int offset = raw & RowOffsetMask;
            bool deleted = (raw & DeletedFlag) != 0;
            bool overflow = (raw & OverflowFlag) != 0;

            // Rows are packed from the page end backward, so a slot runs from its own
            // offset up to where the previous slot's row began.
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
}
