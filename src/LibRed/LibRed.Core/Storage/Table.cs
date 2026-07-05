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

    /// <summary>Inserts a row (values aligned to column <see cref="ColumnDef.Index"/>) into the table.</summary>
    public void Insert(object?[] values) => new RowInserter(Channel, Definition).Insert(values);

    /// <summary>Rewrites the row at <paramref name="id"/> in place with new values (row id preserved).</summary>
    public void Update(RowId id, object?[] values) => new RowInserter(Channel, Definition).Update(id, values);
}
