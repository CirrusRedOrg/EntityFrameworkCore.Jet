namespace LibRed.Catalog;

/// <summary>
/// One logical index: a name in the TDEF's logical-index list, pointing at the real index that stores its
/// entries. Several logical indexes share one real index — a foreign key's relationship name, the index the
/// designer named, and a primary key can all sit over the same B-tree — so <see cref="IndexDef"/> (one per
/// real index) cannot represent them all. Access's own schema views list every logical index, which is why
/// the names are kept rather than collapsed to the one that wins.
/// </summary>
/// <param name="Name">The logical index's name.</param>
/// <param name="RealIndexOrdinal">Which real index (<see cref="IndexDef.RealIndexOrdinal"/>) stores it.</param>
/// <param name="IsRelationship">True when the entry is a foreign-key relationship rather than a named index.</param>
/// <param name="IsPrimaryKey">True when this entry is the table's primary key.</param>
/// <param name="ForeignKeyType">The relationship's direction as the info block records it: 0 none, 1 incoming
/// (this table is the parent — the half Access names <c>.r…</c> and keeps out of its schema views), 2 outgoing
/// (this table holds the foreign key).</param>
public sealed record LogicalIndexDef(
    string Name, int RealIndexOrdinal, bool IsRelationship, bool IsPrimaryKey, byte ForeignKeyType)
{
    /// <summary>True for the parent side of a relationship, which Access hides.</summary>
    public bool IsIncomingRelationship => ForeignKeyType == IncomingRelationship;

    /// <summary><see cref="ForeignKeyType"/> of the parent side.</summary>
    public const byte IncomingRelationship = 1;

    /// <summary><see cref="ForeignKeyType"/> of the child side, the table holding the foreign key.</summary>
    public const byte OutgoingRelationship = 2;
}
