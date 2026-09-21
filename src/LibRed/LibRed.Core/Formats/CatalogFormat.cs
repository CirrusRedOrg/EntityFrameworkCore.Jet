namespace LibRed.Formats;

/// <summary>Shared <c>MSysObjects</c> catalog constants used when writing new object rows.</summary>
internal static class CatalogFormat
{
    /// <summary>The <c>MSysObjects.ParentId</c> of a top-level user object — the database's object container,
    /// a constant <c>0x0F000001</c>. Used for both table and query/view objects (verified vs Northwind).</summary>
    public const int ObjectContainerParentId = 0x0F000001;

    /// <summary>The <c>MSysObjects.ParentId</c> of a relationship object — the Relationships container,
    /// <c>0x0F000003</c> (verified vs ACE). A relationship may share its name with a table or a query but not with
    /// another relationship (verified vs ACE).</summary>
    public const int RelationshipContainerParentId = 0x0F000003;

    /// <summary><c>MSysObjects.Type</c> value for a relationship object.</summary>
    public const short ObjectTypeRelationship = 8;
}