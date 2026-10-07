namespace LibRed.Catalog;

/// <summary>
/// A complex (multi-value / attachment) column and the per-column table its values live in. The column's
/// in-row value is a 4-byte <b>complex id</b> naming one record; the values for that record are the rows of
/// <see cref="FlatTable"/> whose <see cref="OwnerLink"/> holds the id.
/// </summary>
/// <remarks>
/// <para>Everything here is resolved <b>structurally</b>, never by building names. The flat table and its two
/// bookkeeping columns keep whatever the table and column were called when the complex column was created, and
/// a later rename does not follow: a column now called <c>BK_category</c> can be backed by
/// <c>f_…_TempField*7</c>, and a table now called <c>Borrow</c> can have a value-id column still named
/// <c>Table1_BRW_book</c>. The flat table comes from <c>MSysComplexColumns.FlatTableID</c> and the two columns
/// from the index shape. See <c>docs/format/system-catalog.md</c>.</para>
/// <para>A record's id is allocated when the <b>row</b> is created, so a non-null id is no evidence that any
/// value exists — an empty <see cref="Read"/> is the ordinary answer, not an error.</para>
/// </remarks>
public sealed class ComplexColumn
{
    internal ComplexColumn(
        string columnName, int complexId, TableDefinition ownerTable, TableDefinition flatTable,
        ColumnDef ownerLink, ColumnDef valueId, string? elementTypeName)
    {
        ColumnName = columnName;
        ComplexId = complexId;
        OwnerTable = ownerTable;
        FlatTable = flatTable;
        OwnerLink = ownerLink;
        ValueId = valueId;
        ElementTypeName = elementTypeName;
        ValueColumns = flatTable.Columns.Where(c => c != ownerLink && c != valueId).ToList().AsReadOnly();
    }

    /// <summary>The complex column's name on <see cref="OwnerTable"/>.</summary>
    public string ColumnName { get; }

    /// <summary>Its <c>MSysComplexColumns.ComplexID</c> — the same value the column descriptor carries at
    /// <c>0x0B</c>.</summary>
    public int ComplexId { get; }

    /// <summary>The table holding the complex column.</summary>
    public TableDefinition OwnerTable { get; }

    /// <summary>The per-column table holding the values, one row each.</summary>
    public TableDefinition FlatTable { get; }

    /// <summary>The flat table's link back to the owning record: it holds that record's complex id and
    /// <b>repeats once per value</b>, which is what makes the column multi-valued. Indexed, not unique.</summary>
    public ColumnDef OwnerLink { get; }

    /// <summary>The flat table's per-value id: unique, primary, and an ordinary AutoNumber whose high-water
    /// is the flat table's own TDEF <c>0x14</c>.</summary>
    public ColumnDef ValueId { get; }

    /// <summary>The value columns proper — everything but the two bookkeeping ones. A scalar multi-value
    /// column has a single <c>Value</c>; an attachment has the six <c>File*</c> columns.</summary>
    public IReadOnlyList<ColumnDef> ValueColumns { get; }

    /// <summary>The <c>MSysComplexType_*</c> template this column was made from (e.g.
    /// <c>MSysComplexType_Attachment</c>), or null when the catalog row does not resolve to one.</summary>
    public string? ElementTypeName { get; }

    /// <summary>Whether this is an attachment column rather than a multi-value scalar — it carries the
    /// <c>FileData</c>/<c>FileName</c> shape, so <see cref="ComplexAttachment.Unwrap"/> applies.</summary>
    public bool IsAttachment =>
        FlatTable.FindColumn("FileData") is not null && FlatTable.FindColumn("FileName") is not null;
}