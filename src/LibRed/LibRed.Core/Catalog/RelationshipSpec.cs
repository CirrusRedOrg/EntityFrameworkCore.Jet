namespace LibRed.Catalog;

/// <summary>
/// A relationship (foreign key) to create on the table being defined: the child column pairs, the
/// referenced (parent) table, and the enforce/cascade options. The child table is implicit (the
/// table under construction). Persisted as rows in <c>MSysRelationships</c> plus a child-side index
/// on the foreign-key columns — the inverse of <see cref="ForeignKey"/>.
/// </summary>
/// <remarks>
/// <see cref="ReferencesPrimaryKey"/> is <c>REFERENCES table</c> with no column list: each pair names only its
/// child column, and the table creator pairs them with the parent's primary-key columns in order.
/// </remarks>
public sealed record RelationshipSpec(
    string Name,
    string ReferencedTable,
    IReadOnlyList<(string Column, string ReferencedColumn)> Columns,
    bool IsEnforced,
    bool CascadeUpdate,
    bool CascadeDelete,
    bool NoIndex = false,
    // Inherited from a linked table. LibRed neither reads nor writes this on disk (its grbit bit is unverified)
    // and never authors inherited relationships, so it stays false; the field exists for parity with ForeignKey.
    bool IsInherited = false,
    bool DeleteSetNull = false,
    // ON UPDATE SET NULL: the docs list it, but the ACE OLE DB provider rejects it via SQL DDL ("Invalid
    // argument"), so its on-disk storage (grbit flag + info-block +0x15 byte) couldn't be probed. The
    // pathway is threaded through; TableCreator throws NotImplemented rather than guess the bytes. See the
    // libred-foreign-key-status memory / spec §11.
    bool UpdateSetNull = false,
    bool ReferencesPrimaryKey = false)
{
    /// <summary>A relationship naming only its child columns, to reference the parent's primary key.</summary>
    public static IReadOnlyList<(string Column, string ReferencedColumn)> ChildColumnsOnly(IReadOnlyList<string> columns) =>
        columns.Select(c => (c, string.Empty)).ToList();

    /// <summary>Pairs child columns with the parent columns they reference, in order. ACE refuses a relationship
    /// whose two sides differ in number, with this message.</summary>
    public static IReadOnlyList<(string Column, string ReferencedColumn)> PairColumns(
        string referencedTable, IReadOnlyList<string> columns, IReadOnlyList<string> referencedColumns) =>
        columns.Count == referencedColumns.Count
            ? columns.Zip(referencedColumns).ToList()
            : throw new InvalidOperationException(
                "Relationship must be on the same number of fields with the same data types. " +
                $"The foreign key has {columns.Count} columns but references {referencedColumns.Count} in '{referencedTable}'.");
}

/// <summary>A UNIQUE constraint to create as a unique (non-primary) index over the named columns.</summary>
public sealed record UniqueIndexSpec(string Name, IReadOnlyList<string> Columns);
