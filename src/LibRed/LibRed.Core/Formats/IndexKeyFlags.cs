using LibRed.Catalog;

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

    /// <summary>
    /// The key width of a fixed-width column type, or -1 where the key is not fixed-width — TEXT, Binary,
    /// GUID and DATETIME2 all encode to a variable, and for the first three lossy, form
    /// (page-03-04 §10.4). Here for the same reason as the flags above: the encoder and the decoder each had
    /// their own copy of this table and they drifted, the decoder never learning the widths the encoder
    /// writes for <see cref="JetDataType.Complex"/> and <see cref="JetDataType.FixedPoint"/>.
    /// </summary>
    public static int FixedKeySize(JetDataType type) => type switch
    {
        JetDataType.Byte => 1,
        JetDataType.Int16 => 2,
        JetDataType.Int32 => 4,
        // A complex (multi-value / attachment) column's key is its Int32 complex id, encoded exactly as an
        // Int32 — verified against ACE over 43 entries across 9 such indexes in two files, covering
        // attachment, Text and Long element types, with no difference in any byte.
        JetDataType.Complex => 4,
        JetDataType.Single => 4,
        JetDataType.Double or JetDataType.DateTime => 8,
        // Int64/BIGINT keys like Currency — both are an int64, sign bit flipped, big-endian. Its VARIABLE
        // storage does not change that: this dispatch is on the type, not on where the row keeps it.
        JetDataType.Currency or JetDataType.Int64 => 8,
        JetDataType.FixedPoint => 17, // sign byte + 16-byte big-endian magnitude
        _ => -1,
    };
}