using LibRed.IO;

namespace LibRed.Pages;

/// <summary>
/// A table definition (TDEF) page: row count, column definitions, index metadata
/// and the pointer to the first data/usage page for the table. May span multiple
/// pages for wide tables (continued via a "next page" pointer).
/// </summary>
public sealed class TableDefinitionPage : Page
{
    public override PageType Type => PageType.TableDefinition;

    public int RowCount { get; internal set; }
    public int ColumnCount { get; internal set; }
    public int IndexCount { get; internal set; }

    public override void Read(PageBuffer buffer)
    {
        PageNumber = buffer.PageNumber;
        // TODO: parse TDEF header, column descriptors, index descriptors and
        // real-index/all-index counts. See mdbtools read_table / Jackcess TableImpl.
    }
}
