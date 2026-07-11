using LibRed.Catalog;
using LibRed.IO;

namespace LibRed.Storage;

/// <summary>
/// An opened table: pairs a <see cref="TableDef"/> with the means to read its rows.
/// The primary entry point for scanning data out of the storage layer.
/// </summary>
public sealed class Table
{
    public Table(PageChannel channel, TableDef definition)
    {
        Channel = channel;
        Definition = definition;
        UsageMap = new UsageMap(channel, definition);
    }

    public PageChannel Channel { get; }
    public TableDef Definition { get; }
    public UsageMap UsageMap { get; }

    public string Name => Definition.Name;

    /// <summary>Returns a forward-only cursor over all rows in the table.</summary>
    public TableCursor Rows() => new(this);

    /// <summary>Decodes the row at <paramref name="id"/> (following an overflow forward-pointer to a
    /// relocated row), or <see langword="null"/> if the slot is empty/deleted. Used by an index seek, which
    /// yields row ids.</summary>
    public object?[]? GetRow(RowId id)
    {
        var page = new Pages.DataPage();
        page.Read(Channel.ReadPage(id.Page), Channel.Format);
        if (id.Row >= page.RowCount) return null;
        Pages.RowSlot slot = page.Rows[id.Row];

        var decoder = new RowDecoder(Definition.Columns, Channel.Format, new LongValueReader(Channel));
        if (slot.HasOverflow)
        {
            int pointer = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(page.GetRow(id.Row));
            var target = new Pages.DataPage();
            target.Read(Channel.ReadPage(pointer >> 8), Channel.Format);
            return decoder.Decode(target.GetRow(pointer & 0xFF));
        }
        return slot.IsDeleted ? null : decoder.Decode(page.GetRow(id.Row));
    }

    /// <summary>Yields the rows whose <paramref name="index"/> key equals <paramref name="values"/> — an index
    /// seek (equality) instead of a full scan. May over-return (lossy text/binary keys); the caller re-checks
    /// the predicate.</summary>
    public IEnumerable<object?[]> SeekRows(IndexDef index, object?[] values)
    {
        foreach (RowId id in new IndexWriter(Channel, Definition).Seek(index, values))
            if (GetRow(id) is { } row)
                yield return row;
    }

    /// <summary>Yields the rows whose <paramref name="index"/> key lies in [<paramref name="low"/>,
    /// <paramref name="high"/>] (either bound null = open) — an index range scan. May over-return at the
    /// boundaries; the caller re-checks the predicate.</summary>
    public IEnumerable<object?[]> SeekRangeRows(IndexDef index, object?[]? low, object?[]? high)
    {
        foreach (RowId id in new IndexWriter(Channel, Definition).SeekRange(index, low, high))
            if (GetRow(id) is { } row)
                yield return row;
    }

    /// <summary>Inserts a row (values aligned to column <see cref="ColumnDef.Index"/>) into the table.</summary>
    public void Insert(object?[] values) => new RowInserter(Channel, Definition).Insert(values);

    /// <summary>Rewrites the row at <paramref name="id"/> in place with new values (row id preserved).
    /// <paramref name="changedColumns"/> are the columns that actually changed — an unchanged memo/OLE column
    /// keeps its stored descriptor (no re-materialise), a changed one has its old LVAL pages reclaimed.</summary>
    public void Update(RowId id, object?[] values, IReadOnlySet<int> changedColumns) =>
        new RowInserter(Channel, Definition).Update(id, values, changedColumns);

    /// <summary>Rewrites the row treating every column as changed (materialises all long values).</summary>
    public void Update(RowId id, object?[] values) =>
        Update(id, values, new HashSet<int>(System.Linq.Enumerable.Range(0, values.Length)));

    /// <summary>Moves a row's entry in one index when its key changes (remove old key, add new; row id
    /// unchanged). Used by UPDATE of an indexed column.</summary>
    public void MoveIndexEntry(IndexDef index, object?[] oldValues, object?[] newValues, RowId id) =>
        new IndexWriter(Channel, Definition).MoveEntry(index, oldValues, newValues, id);

    /// <summary>Whether <paramref name="values"/>' key already exists in <paramref name="index"/> for a row
    /// other than <paramref name="excludeRow"/> — used to enforce a UNIQUE/PRIMARY index on UPDATE.</summary>
    public bool HasDuplicateKey(IndexDef index, object?[] values, RowId excludeRow) =>
        new IndexWriter(Channel, Definition).KeyExists(index, values, (excludeRow.Page << 8) | excludeRow.Row);

    /// <summary>Soft-deletes the row at <paramref name="id"/> (row bytes kept, slot flagged; TDEF row count
    /// decremented). The caller removes its index entries first via <see cref="RemoveIndexEntry"/>.</summary>
    public void Delete(RowId id) => new RowInserter(Channel, Definition).Delete(id);

    /// <summary>Removes a deleted row's entry from one index.</summary>
    public void RemoveIndexEntry(IndexDef index, object?[] values, RowId id) =>
        new IndexWriter(Channel, Definition).DeleteEntry(index, values, id);
}
