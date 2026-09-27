namespace LibRed.Formats;

/// <summary>
/// The <c>MSysRelationships.grbit</c> flag values, shared by the read side (<c>JetCatalog</c>) and the
/// write side (<c>TableCreator</c>) so the two can't drift. Values verified against Access.
/// </summary>
internal static class RelationshipFlags
{
    /// <summary>A one-to-one relationship (DAO's <c>dbRelationUnique</c>). Set by whoever creates the relationship
    /// — Access's dialog when both sides are unique — and never by ACE's SQL; on an enforced relationship it makes
    /// the child's backing index unique.</summary>
    public const int OneToOne = 0x00000001;

    /// <summary>Referential integrity is NOT enforced.</summary>
    public const int DontEnforce = 0x00000002;

    /// <summary>Updates to the parent key cascade to the child (<c>ON UPDATE CASCADE</c>).</summary>
    public const int UpdateCascade = 0x00000100;

    /// <summary>Deletes of the parent row cascade to the child (<c>ON DELETE CASCADE</c>).</summary>
    public const int DeleteCascade = 0x00001000;

    /// <summary>Deleting the parent sets the child's FK columns to NULL (Jet <c>ON DELETE SET NULL</c>).</summary>
    public const int DeleteSetNull = 0x00002000;
}