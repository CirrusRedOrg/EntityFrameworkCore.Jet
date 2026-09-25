using LibRed.Catalog;
using LibRed.IO;

namespace LibRed.Storage;

/// <summary>
/// Writes the catalog rows every new object gets whatever it is: its <c>MSysObjects</c> entry, and the
/// <c>MSysACEs</c> pair granting the Users and Admin accounts access to it (system-catalog §11). A table, a
/// stored query and a relationship differ only in the type, container, flags and masks they pass.
/// </summary>
/// <remarks>
/// <see cref="DatabaseCreator"/> writes the same two tables and does not use this: it is populating them
/// before either has an index or a catalog to resolve names through, and it grants to accounts (Creator,
/// Engine) that no later object's rows name.
/// </remarks>
internal sealed class CatalogWriter(PageChannel channel, JetCatalog catalog)
{
    /// <summary>Inserts the object's <c>MSysObjects</c> row — owner Users, both dates now — maintaining the
    /// table's indexes, including the (ParentId, Name) one Access resolves names through.</summary>
    /// <param name="name">The object's name, unique within its container.</param>
    /// <param name="objectId">A table's is its TDEF page; every other object takes a synthetic negative id.</param>
    /// <param name="type">1 table, 5 query, 8 relationship.</param>
    /// <param name="parentId">The container the object belongs to.</param>
    /// <param name="flags">Object-class specific; a query's encodes its kind.</param>
    /// <param name="properties">The object's extended properties, if it has any: a table's per-column
    /// DefaultValue/Required and its CHECK constraints all live in this one blob.</param>
    public void AddObjectRow(
        string name, int objectId, short type, int parentId, int flags,
        IReadOnlyList<PropertyBlob.Property>? properties = null)
    {
        TableDef msysObjects = catalog.RequireTable("MSysObjects");
        DateTime now = DateTime.Now;
        var values = new object?[msysObjects.Columns.Count];
        Set(msysObjects, values, "Id", objectId);
        Set(msysObjects, values, "ParentId", parentId);
        Set(msysObjects, values, "Type", type);
        Set(msysObjects, values, "Name", name);
        Set(msysObjects, values, "Flags", flags);
        Set(msysObjects, values, "Owner", catalog.SecuritySids.Users);
        Set(msysObjects, values, "DateCreate", now);
        Set(msysObjects, values, "DateUpdate", now);

        var inserter = new RowInserter(channel, msysObjects);
        if (properties is { Count: > 0 })
        {
            // Access reads object properties only from an LVAL-page long value, not an inline one, so store
            // the blob on a page (packed onto a shared LvProp page as Access does) and keep the descriptor.
            byte[] reference = inserter.StorePackedLongValue(
                msysObjects.RequireColumn("LvProp").ColumnId, PropertyBlob.Write(properties));
            Set(msysObjects, values, "LvProp", new LongValueDescriptor(reference));
        }

        inserter.Insert(values, updateIndexes: true);
    }

    /// <summary>Inserts the object's two <c>MSysACEs</c> rows, Users then Admin, maintaining the ObjectId
    /// index so Access's security check finds them — without them it warns about permissions on opening the
    /// object. The two masks are per object class, so they come from the caller.</summary>
    public void AddPermissionRows(int objectId, int usersAcm, int adminAcm)
    {
        TableDef msysAces = catalog.RequireTable("MSysACEs");
        (byte[] users, byte[] admin) = catalog.SecuritySids;
        foreach ((byte[] sid, int acm) in new[] { (users, usersAcm), (admin, adminAcm) })
        {
            var values = new object?[msysAces.Columns.Count];
            Set(msysAces, values, "ACM", acm);
            Set(msysAces, values, "FInheritable", false);
            Set(msysAces, values, "ObjectId", objectId);
            Set(msysAces, values, "SID", sid);
            new RowInserter(channel, msysAces).Insert(values, updateIndexes: true);
        }
    }

    /// <summary>Puts <paramref name="value"/> in the row slot <paramref name="column"/> occupies.</summary>
    public static void Set(TableDef table, object?[] values, string column, object? value) =>
        values[table.RequireColumn(column).Index] = value;
}
