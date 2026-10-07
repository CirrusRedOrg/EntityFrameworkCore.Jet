namespace LibRed.Catalog;

/// <summary>
/// The trailing §3.3.2 column-usage-map entry for a long-value (memo/OLE) column: its column id and
/// pointers to the owned- and free-pages usage maps that will track the column's LVAL pages.
/// </summary>
public sealed record LongValueColumnSpec(int ColumnId, int UsedRow, int FreeRow, int MapPage);