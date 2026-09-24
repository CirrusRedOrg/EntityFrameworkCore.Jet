namespace LibRed.Pages;

/// <summary>
/// The page-type marker stored in byte 0 of every page. Values match the on-disk
/// Jet/ACE encoding.
/// </summary>
public enum PageType : byte
{
    DatabaseDefinition = 0x00,
    DataPage = 0x01,
    TableDefinition = 0x02,
    IntermediateIndexPage = 0x03,
    LeafIndexPage = 0x04,
    PageUsageBitmap = 0x05,

    /// <summary>A table-definition page that has been released by <c>DROP TABLE</c>. Access marks it by
    /// setting this type byte and changing nothing else — the old definition stays on the page — and it
    /// leaves the data, long-value and usage-map holder pages it frees at their original types. Measured:
    /// exactly one byte of the 4,096 differs across an ACE drop. Compact reclaims the page.
    /// See <c>docs/format/page-08-released-tdef.md</c>.</summary>
    ReleasedTableDefinition = 0x08,

    /// <summary>A data page released because its last live row was deleted. It covers both an ordinary
    /// table's data page emptied by DELETE and a packed long-value page whose last tenant was deleted —
    /// structurally the same event, and the same one-byte stamp. Every row slot is left a 0-length
    /// deleted+overflow tombstone and the page is given back. A <b>chained</b> long value owns its pages
    /// outright and they go back at <see cref="DataPage"/> instead. Nothing needs to handle it on read:
    /// allocation selects on the free map, not on this byte.
    /// See <c>docs/format/page-09-released-data.md</c>.</summary>
    ReleasedDataPage = 0x09,
}