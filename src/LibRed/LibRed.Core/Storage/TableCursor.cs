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
            // Zero-copy read: the scan only reads this page and consumes each row immediately, so serve it from
            // the shared cache instead of allocating a fresh 4 KB buffer per page. Copy-on-write Store keeps the
            // returned array stable even if decoding a row triggers a long-value read that evicts this page.
            PageBuffer buffer = _table.Channel.ReadPageShared(pageNumber);
            var page = new DataPage();
            page.Read(buffer, _table.Channel.Format);

            for (int i = 0; i < page.RowCount; i++)
            {
                RowSlot slot = page.Rows[i];
                if (slot.IsDeleted) continue; // deleted, or a relocated row's hidden target (reached via its pointer)

                // A relocated row: the slot holds a 4-byte pointer (page<<8 | row) to the real row, which lives
                // on another page flagged "deleted" so scans skip it there. The row id stays this slot's, so
                // index entries keep pointing here. Follow the pointer and decode the target's bytes.
                if (slot.HasOverflow)
                {
                    RelocatedRow target = RowRelocationReader.Resolve(
                        _table.Channel, _table.Definition.DefinitionPage, slot, page.GetRow(i));
                    yield return (new RowId(pageNumber, i), decoder.Decode(target.Bytes));
                    continue;
                }

                yield return (new RowId(pageNumber, i), decoder.Decode(page.GetRow(i)));
            }
        }
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
