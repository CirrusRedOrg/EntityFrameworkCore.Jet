namespace LibRed.Formats;

/// <summary>
/// The per-column prefix bytes of an order-preserving index key. Each non-boolean column is prefixed by a
/// start byte (present value) or null byte, with distinct values for ascending vs descending columns so that
/// a lexicographic byte compare matches the index's logical order. Shared by the write side
/// (<c>IndexKeyEncoder</c>) and the read side (<c>IndexKeyDecoder</c>) so the two can't drift.
/// </summary>
internal static class IndexKeyFlags
{
    /// <summary>Ascending column, present value.</summary>
    public const byte AscStart = 0x7F;

    /// <summary>Ascending column, null value.</summary>
    public const byte AscNull = 0x00;

    /// <summary>Descending column, present value.</summary>
    public const byte DescStart = 0x80;

    /// <summary>Descending column, null value.</summary>
    public const byte DescNull = 0xFF;
}
