namespace LibRed.Formats;

/// <summary>
/// Constants of the long-value (Memo / OLE / long binary) storage form: the flags in a 12-byte in-row
/// long-value descriptor, and the "LVAL" page owner marker. Shared by the read side
/// (<c>LongValueReader</c>, <c>DataPage</c>) and the write side (<c>LongValueWriter</c>) so they can't drift.
/// </summary>
internal static class LongValueFormat
{
    /// <summary>Descriptor flag: the payload follows the descriptor inline (no LVAL page).</summary>
    public const byte FlagInline = 0x80;

    /// <summary>Descriptor flag: the payload is a single LVAL page row.</summary>
    public const byte FlagSinglePage = 0x40;

    /// <summary>Descriptor flag value for a payload chained across multiple LVAL pages.</summary>
    public const byte FlagChained = 0x00;

    /// <summary>The 4-byte owner marker on a long-value page: ASCII "LVAL" (little-endian 4C 56 41 4C).</summary>
    public const uint LvalMarker = 0x4C41564C;
}
