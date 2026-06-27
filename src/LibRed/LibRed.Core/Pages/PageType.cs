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
}
