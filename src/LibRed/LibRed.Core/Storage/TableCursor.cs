using LibRed.IO;
using LibRed.Pages;

namespace LibRed.Storage;

/// <summary>
/// A forward-only cursor over the rows of a table. Walks the table's usage map,
/// reads each data page and yields decoded rows via <see cref="RowDecoder"/>.
/// </summary>
public sealed class TableCursor(Table table) : IEnumerable<object?[]>
{
    private readonly Table _table = table;

    public IEnumerator<object?[]> GetEnumerator()
    {
        var decoder = new RowDecoder(_table.Definition);
        foreach (int pageNumber in _table.UsageMap.DataPages())
        {
            PageBuffer buffer = _table.Channel.ReadPage(pageNumber);
            var dataPage = new DataPage();
            dataPage.Read(buffer, _table.Channel.Format);

            // TODO: walk the page's row slot directory and decode each row record.
            _ = decoder;
        }

        yield break;
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
