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

    /// <summary>A long-value page released because the last value sharing it was deleted. Several small
    /// (single-page form) values pack onto one LVAL page; each delete retires its row to a 0-length
    /// deleted+overflow tombstone, and when none are left the page is stamped with this type and freed.
    /// A <b>chained</b> value owns its pages outright and they go back at <see cref="DataPage"/> instead,
    /// which is why only the packed form produces this. Nothing needs to handle it on read: allocation
    /// selects on the free map, not on this byte. See <c>docs/format/page-09-released-long-value.md</c>.</summary>
    ReleasedLongValuePage = 0x09,
}