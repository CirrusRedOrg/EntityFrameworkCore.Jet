namespace LibRed.Formats;

/// <summary>Shared <c>MSysObjects</c> catalog constants used when writing new object rows.</summary>
internal static class CatalogFormat
{
    /// <summary>The <c>MSysObjects.ParentId</c> of a top-level user object — the database's object container,
    /// a constant <c>0x0F000001</c>. Used for both table and query/view objects (verified vs Northwind).</summary>
    public const int ObjectContainerParentId = 0x0F000001;
}
