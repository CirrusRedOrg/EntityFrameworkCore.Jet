namespace LibRed.Formats;

/// <summary>
/// An index-info block's update and delete action bytes (<see cref="JetFormatBase.IndexInfoUpdateActionOffset"/>,
/// <see cref="JetFormatBase.IndexInfoDeleteActionOffset"/>), verified against ACE-created relationships. These,
/// not <c>MSysRelationships.grbit</c>, are what ACE acts on: with the two made to disagree, it cascades by the
/// block. Written on both ends of a relationship.
/// </summary>
public enum RelationshipAction : byte
{
    NoCascade = 0x00,
    Cascade = 0x01,

    /// <summary>Delete only: <c>ON DELETE SET NULL</c>.</summary>
    SetNull = 0x02,

    /// <summary>Both actions of a plain index, which is not a relationship's.</summary>
    NotRelationship = 0x04,
}