namespace LibRed.Formats;

/// <summary>
/// Encoding of a 16-bit row-pointer directory entry on a data page: the low 13 bits are the row's byte
/// offset within the page, the top bits are status flags. Shared by the read side (<c>DataPage</c>) and the
/// write side (<c>RowInserter</c>) so the mask and flag bits can't drift. Fixed across the Jet 4 / ACE family.
/// </summary>
internal static class RowPointer
{
    /// <summary>Low 13 bits: the row's byte offset within the page.</summary>
    public const int OffsetMask = 0x1FFF;

    /// <summary>Top bit: the row is deleted (a tombstone).</summary>
    public const int DeletedFlag = 0x8000;

    /// <summary>Second-from-top bit: the pointer targets an overflow (long-value / relocated) row.</summary>
    public const int OverflowFlag = 0x4000;

    /// <summary>
    /// Rows a data page may hold. Not a space limit — an index entry addresses a row as
    /// <c>page &lt;&lt; 8 | row</c> (page-03-04-index-btree.md §10.2), so the slot number has to fit one byte
    /// and slots 0..255 are all that can ever be named.
    /// </summary>
    /// <remarks>
    /// Only narrow rows reach it: a 4 KB page fits 256 rows once they are under about 14 bytes each, which in
    /// practice means an all-fixed-column table of a few small columns. Exceed it and nothing fails — the rows
    /// are written and scan back fine, because a scan walks slots directly — but every row past slot 255 is
    /// <b>unaddressable by any index</b>, and its entry aliases a different row on another page. That is a
    /// silent wrong answer for any indexed read, which is why the placer refuses the page rather than trusting
    /// free space alone.
    /// </remarks>
    public const int MaxRowsPerPage = 256;
}
