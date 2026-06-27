using LibRed.IO;

namespace LibRed.Pages;

/// <summary>
/// A data page holding the actual rows of a single table. Rows are addressed by a
/// slot directory at the end of the page; each slot points at a variable-length
/// row record decoded by <see cref="Storage.RowDecoder"/>.
/// </summary>
public sealed class DataPage : Page
{
    public override PageType Type => PageType.DataPage;

    /// <summary>The table (TDEF page number) this data page belongs to.</summary>
    public int OwningTablePage { get; internal set; }

    /// <summary>Number of row slots present on the page.</summary>
    public int RowCount { get; internal set; }

    public override void Read(PageBuffer buffer)
    {
        PageNumber = buffer.PageNumber;
        // TODO: read free-space, owning-table pointer and the row-offset slot table.
    }
}
