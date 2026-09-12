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
    /// exactly one byte of the 4,096 differs across an ACE drop. Compact reclaims the page.</summary>
    ReleasedTableDefinition = 0x08,

    // Real files also carry a type 0x09: a released (free in the global map) and emptied page, usually a
    // long-value one. It is deliberately NOT named here — no operation reachable through the SQL surface
    // produces one, so what it means precisely is unestablished, and a name would assert otherwise. Nothing
    // needs to handle it: allocation selects on the free map, not on this byte. See the usage-maps spec (§9.1).
}
