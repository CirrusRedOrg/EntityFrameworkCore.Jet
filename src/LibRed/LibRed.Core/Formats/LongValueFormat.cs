namespace LibRed.Formats;

/// <summary>
/// Constants of the long-value (Memo / OLE / long binary) storage form: the flags in a 12-byte in-row
/// long-value descriptor, and the "LVAL" page owner marker. Shared by the read side
/// (<c>LongValueReader</c>, <c>DataPage</c>) and the write side (<c>LongValueWriter</c>) so they can't drift.
/// </summary>
internal static class LongValueFormat
{
    // ACE accepted and fully read back 0x3FFFFFFF binary bytes, then rejected 0x40000000.
    // Length carries into byte 3; its 0x40/0x80 storage flags must remain separate.
    public const int LengthMask = 0x3FFFFFFF;
    public const byte FlagMask = 0xC0;

    internal static void ValidateLength(int length)
    {
        if ((uint)length > LengthMask)
            throw new ArgumentOutOfRangeException(nameof(length), "The long-value length would overwrite its storage flags.");
    }

    /// <summary>
    /// The largest value ACE keeps on a <b>single</b> LVAL page; anything longer is chained. Measured
    /// (<c>LongTextStorageAccessTests</c>): 3816 bytes stays single-page, 3818 chains, for a plain and a
    /// <c>WITH COMPRESSION</c> column alike. Distinct from the 4076-byte chunk row a chained value uses —
    /// conflating the two made LibRed keep 3818–4076 byte values on one page where ACE chains them. What
    /// fixes the boundary at 3816 rather than the 4076 a row can hold is not established.
    /// </summary>
    /// <remarks>The decision is made on the <b>uncompressed</b> length, as are the inline and chained ones;
    /// compression is applied to whatever form results, and a chained value is never compressed.</remarks>
    public const int MaxSinglePageValue = 3816;

    /// <summary>Descriptor flag: the payload follows the descriptor inline (no LVAL page).</summary>
    public const byte FlagInline = 0x80;

    /// <summary>Descriptor flag: the payload is a single LVAL page row.</summary>
    public const byte FlagSinglePage = 0x40;

    /// <summary>Descriptor flag value for a payload chained across multiple LVAL pages.</summary>
    public const byte FlagChained = 0x00;

    /// <summary>The 4-byte owner marker on a long-value page: ASCII "LVAL" (little-endian 4C 56 41 4C).</summary>
    public const uint LvalMarker = 0x4C41564C;
}
