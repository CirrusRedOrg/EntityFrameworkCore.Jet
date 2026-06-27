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
        var decoder = new RowDecoder(_table.Definition.Columns, _table.Channel.Format);

        foreach (int pageNumber in _table.UsageMap.DataPages())
        {
            PageBuffer buffer = _table.Channel.ReadPage(pageNumber);
            var page = new DataPage();
            page.Read(buffer, _table.Channel.Format);

            for (int i = 0; i < page.RowCount; i++)
            {
                RowSlot slot = page.Rows[i];
                if (slot.IsDeleted || slot.HasOverflow) continue;

                object?[]? values = null;
                try
                {
                    values = decoder.Decode(page.GetRow(i));
                }
                catch (NotSupportedException)
                {
                    // Rows >= 256 bytes use the variable-offset jump table (not yet
                    // implemented). Skip rather than fail the whole scan. TODO.
                }

                if (values is not null) yield return values;
            }
        }
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
