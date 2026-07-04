using LibRed.IO;
using LibRed.Pages;

namespace LibRed.Storage;

/// <summary>
/// A forward-only cursor over the rows of a table. Walks the table's data pages,
/// decodes each inline row, and yields one value array per row.
/// </summary>
public sealed class TableCursor(Table table) : IEnumerable<object?[]>
{
    private readonly Table _table = table;

    public IEnumerator<object?[]> GetEnumerator()
    {
        foreach ((RowId _, object?[] values) in WithIds())
            yield return values;
    }

    /// <summary>Yields each live row together with its <see cref="RowId"/> — used when the caller needs to
    /// reference the row (e.g. back-filling an index over existing data).</summary>
    public IEnumerable<(RowId Id, object?[] Values)> WithIds()
    {
        var decoder = new RowDecoder(
            _table.Definition.Columns,
            _table.Channel.Format,
            new LongValueReader(_table.Channel));

        foreach (int pageNumber in _table.UsageMap.DataPages())
        {
            PageBuffer buffer = _table.Channel.ReadPage(pageNumber);
            var page = new DataPage();
            page.Read(buffer, _table.Channel.Format);

            for (int i = 0; i < page.RowCount; i++)
            {
                RowSlot slot = page.Rows[i];
                if (slot.IsDeleted || slot.HasOverflow) continue;

                yield return (new RowId(pageNumber, i), decoder.Decode(page.GetRow(i)));
            }
        }
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
