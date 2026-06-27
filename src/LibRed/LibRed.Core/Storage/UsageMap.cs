using LibRed.Catalog;
using LibRed.IO;
using LibRed.Pages;

namespace LibRed.Storage;

/// <summary>
/// Enumerates the data pages that belong to a table.
/// </summary>
/// <remarks>
/// TEMPORARY implementation: scans every page and matches the owning-table pointer.
/// This is O(total pages) per table and ignores page order. It will be replaced by
/// parsing the table's real usage map (the inline/reference bitmap referenced from the
/// TDEF's owned-pages pointer), which lists the owned pages directly.
/// </remarks>
public sealed class UsageMap(PageChannel channel, TableDef table)
{
    private readonly PageChannel _channel = channel;
    private readonly TableDef _table = table;

    /// <summary>Yields the page numbers of every data page owned by the table.</summary>
    public IEnumerable<int> DataPages()
    {
        int owner = _table.DefinitionPage;
        for (int p = 0; p < _channel.PageCount; p++)
        {
            PageBuffer buffer = _channel.ReadPage(p);
            if (buffer.ReadByte(0) != (byte)PageType.DataPage) continue;

            var page = new DataPage();
            page.Read(buffer, _channel.Format);
            if (page.OwningTablePage == owner) yield return p;
        }
    }
}
