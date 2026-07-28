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
}
