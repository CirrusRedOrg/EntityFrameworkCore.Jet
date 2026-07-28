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
    // Inherited from a linked table. LibRed neither reads nor writes this on disk (its grbit bit is unverified)
    // and never authors inherited relationships, so it stays false; the field exists for parity with ForeignKey.
    bool IsInherited = false,
    bool DeleteSetNull = false,
    // ON UPDATE SET NULL: the docs list it, but the ACE OLE DB provider rejects it via SQL DDL ("Invalid
    // argument"), so its on-disk storage (grbit flag + info-block +0x15 byte) couldn't be probed. The
    // pathway is threaded through; TableCreator throws NotImplemented rather than guess the bytes. See the
    // libred-foreign-key-status memory / spec §11.
    bool UpdateSetNull = false);

/// <summary>A UNIQUE constraint to create as a unique (non-primary) index over the named columns.</summary>
public sealed record UniqueIndexSpec(string Name, IReadOnlyList<string> Columns);
