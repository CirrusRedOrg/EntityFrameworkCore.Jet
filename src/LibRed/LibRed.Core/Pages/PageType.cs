using System.Buffers.Binary;

namespace LibRed.Pages;

/// <summary>
/// The page type: the little-endian 16-bit word at offset 0 of every page. Every type Jet/ACE writes has high
/// byte <c>0x01</c>, page 0's included — the byte once read as a constant "flags" field is half of the type, and
/// ACE tests the whole word: it reads a table's owned page as rows only when the word is one of the row-bearing
/// types, and skips a page whose low byte is right but whose high byte is not (verified against ACE, every
/// combination of low byte <c>00</c>–<c>09</c> and high byte). See docs/format/README.md.
/// </summary>
public enum PageType : ushort
{
    DatabaseDefinition = 0x0100,
    DataPage = 0x0101,
    TableDefinition = 0x0102,
    IntermediateIndexPage = 0x0103,
    LeafIndexPage = 0x0104,
    PageUsageBitmap = 0x0105,

    /// <summary>A table-definition page that has been released by <c>DROP TABLE</c>. Access marks it by
    /// setting this type and changing nothing else — the old definition stays on the page — and it
    /// leaves the data, long-value and usage-map holder pages it frees at their original types. Measured:
    /// exactly one byte of the 4,096 differs across an ACE drop. Compact reclaims the page.
    /// See <c>docs/format/page-08-released-tdef.md</c>.</summary>
    ReleasedTableDefinition = 0x0108,

    /// <summary>A data page released because its last live row was deleted. It covers both an ordinary
    /// table's data page emptied by DELETE and a packed long-value page whose last tenant was deleted —
    /// structurally the same event, and the same one-byte stamp. Every row slot is left a 0-length
    /// deleted+overflow tombstone and the page is given back. A <b>chained</b> long value owns its pages
    /// outright and they go back at <see cref="DataPage"/> instead. Nothing needs to handle it on read:
    /// allocation selects on the free map, not on this type.
    /// See <c>docs/format/page-09-released-data.md</c>.</summary>
    ReleasedDataPage = 0x0109,
}

/// <summary>Reads and writes a page's <see cref="PageType"/> — the whole word at offset 0, never its low byte
/// alone.</summary>
public static class PageHeader
{
    /// <summary>The page's type, as stored.</summary>
    public static PageType ReadType(ReadOnlySpan<byte> page) => (PageType)BinaryPrimitives.ReadUInt16LittleEndian(page);

    /// <summary>Stamps <paramref name="type"/> at offset 0.</summary>
    public static void WriteType(Span<byte> page, PageType type) =>
        BinaryPrimitives.WriteUInt16LittleEndian(page, (ushort)type);
}
