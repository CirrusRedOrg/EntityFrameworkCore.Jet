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
    /// and slots 0..255 are all that can ever be named. <b>ACE itself writes at most 255</b>, one below that,
    /// and LibRed matches ACE rather than the pointer's maximum so its pages are shapes Access also produces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only narrow rows reach it: a 4 KB page fits this many once they are under about 14 bytes each, which in
    /// practice means an all-fixed-column table of a few small columns. ACE's own cap is not about space —
    /// a page it had filled to 255 still held 2,297 of its 4,096 bytes free — and on reaching it ACE drops the
    /// page from the table's free-pages map exactly as if it were full.
    /// </para>
    /// <para>
    /// Exceeding it loses rows silently, and to more than just indexed reads. Every row past slot 255 is
    /// <b>unaddressable by any index</b>, its entry aliasing a different row on another page. Worse, <b>ACE
    /// cannot see those rows at all</b>: it reads the full 16-bit row count but then caps at 256 slots per
    /// page and ignores the remainder. Measured — LibRed wrote 900 rows as 400/400/100 and read all 900 back,
    /// while ACE counted 612, exactly 256 + 256 + 100. A LibRed-only scan walks slots directly and sees them,
    /// which is what made this look harmless the first time. Hence the placer refuses the page rather than
    /// trusting free space alone.
    /// </para>
    /// </remarks>
    public const int MaxRowsPerPage = 255;
}