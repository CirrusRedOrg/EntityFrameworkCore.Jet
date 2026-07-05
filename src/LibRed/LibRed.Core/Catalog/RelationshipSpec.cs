namespace LibRed.Catalog;

/// <summary>
/// A relationship (foreign key) to create on the table being defined: the child column pairs, the
/// referenced (parent) table, and the enforce/cascade options. The child table is implicit (the
/// table under construction). Persisted as rows in <c>MSysRelationships</c> plus a child-side index
/// on the foreign-key columns — the inverse of <see cref="ForeignKey"/>.
/// </summary>
public sealed record RelationshipSpec(
    string Name,
    string ReferencedTable,
    IReadOnlyList<(string Column, string ReferencedColumn)> Columns,
    bool IsEnforced,
    bool CascadeUpdate,
    bool CascadeDelete,
    bool NoIndex = false,
    bool DeleteSetNull = false);

/// <summary>A UNIQUE constraint to create as a unique (non-primary) index over the named columns.</summary>
public sealed record UniqueIndexSpec(string Name, IReadOnlyList<string> Columns);
