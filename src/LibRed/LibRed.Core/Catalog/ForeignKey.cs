namespace LibRed.Catalog;

/// <summary>
/// A relationship (foreign key) between two tables, as recorded in <c>MSysRelationships</c>.
/// </summary>
/// <param name="Name">The relationship name.</param>
/// <param name="Table">The referencing (child / foreign-key) table.</param>
/// <param name="ReferencedTable">The referenced (parent) table.</param>
/// <param name="Columns">The column pairs (child column → referenced column), in key order.</param>
/// <param name="IsEnforced">Whether referential integrity is enforced.</param>
/// <param name="CascadeUpdate">Whether updates to the parent key cascade.</param>
/// <param name="CascadeDelete">Whether deletes of the parent row cascade.</param>
/// <param name="DeleteSetNull">Whether deleting the parent sets the child's FK columns to NULL (Jet's
/// <c>ON DELETE SET NULL</c>, grbit <c>0x2000</c>; mutually exclusive with cascade delete, and Jet has no
/// <c>ON UPDATE SET NULL</c>).</param>
public sealed record ForeignKey(
    string Name,
    string Table,
    string ReferencedTable,
    IReadOnlyList<(string Column, string ReferencedColumn)> Columns,
    bool IsEnforced,
    bool CascadeUpdate,
    bool CascadeDelete,
    bool DeleteSetNull = false,
    /// <summary>ON UPDATE SET NULL — pathway only; not read back yet (its storage is unverified — the ACE
    /// OLE DB provider rejects the DDL, so the grbit/info-block bytes couldn't be probed).</summary>
    bool UpdateSetNull = false);
