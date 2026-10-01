using LibRed.Catalog;
using LibRed.IO;

namespace LibRed.Storage;

/// <summary>
/// Writes the catalog rows every new object gets whatever it is: its <c>MSysObjects</c> entry, and the
/// <c>MSysACEs</c> rows its container's inheritable grants give it (system-catalog §11). A table, a stored query
/// and a relationship differ only in the type, container and flags they pass.
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
        Set(msysObjects, values, "Owner", catalog.SecuritySids.Admin);
        Set(msysObjects, values, "DateCreate", now);
        Set(msysObjects, values, "DateUpdate", now);

        var inserter = new RowInserter(channel, msysObjects);
        if (properties is { Count: > 0 })
        {
            // Stored as any long value is: inline up to 64 bytes, else packed onto a shared LvProp page as
            // Access does.
            byte[] reference = inserter.StorePackedLongValue(
                msysObjects.RequireColumn("LvProp").ColumnId, PropertyBlob.Write(properties));
            Set(msysObjects, values, "LvProp", new LongValueDescriptor(reference));
        }

        inserter.Insert(values, updateIndexes: true);
    }

    /// <summary>Inserts the object's <c>MSysACEs</c> rows, maintaining the ObjectId index so Access's security
    /// check finds them — without them it warns about permissions on opening the object.</summary>
    /// <remarks>The grants are the ones the object's container passes down, derived as ACE derives them
    /// (verified): the Creator account's inheritable grant becomes the owner's — the admin user, as whom LibRed
    /// creates every object — and every other inheritable grant is copied for its own account, OR'd into the
    /// owner's row when it names the owner's account. The owner's row comes first, the rest in the container's
    /// order. So the masks follow the database: a table's owner gets 0xF00FE where the Tables container grants the
    /// Creator that alone, and 0xFFEFF where it also grants admin 0xFFEFF, as Northwind's does.</remarks>
    public void AddPermissionRows(int objectId, int containerId)
    {
        TableDef msysAces = catalog.RequireTable("MSysACEs");
        int idIndex = msysAces.RequireColumn("ObjectId").Index, sidIndex = msysAces.RequireColumn("SID").Index;
        int acmIndex = msysAces.RequireColumn("ACM").Index, inheritIndex = msysAces.RequireColumn("FInheritable").Index;
        (byte[] owner, _, byte[] creator) = catalog.SecuritySids;

        var grants = new List<(byte[] Sid, int Acm)>();
        var aces = new Table(channel, msysAces);
        foreach (object?[] row in aces.Rows(aces.DecodeOnly([idIndex, sidIndex, acmIndex, inheritIndex])))
        {
            if (row[idIndex] is not int id || id != containerId || row[inheritIndex] is not true) continue;
            byte[] sid = (byte[])row[sidIndex]!;
            if (sid.AsSpan().SequenceEqual(creator)) sid = owner;
            int at = grants.FindIndex(g => g.Sid.AsSpan().SequenceEqual(sid));
            if (at < 0) grants.Add((sid, (int)row[acmIndex]!));
            else grants[at] = (grants[at].Sid, grants[at].Acm | (int)row[acmIndex]!);
        }

        foreach ((byte[] sid, int acm) in grants.OrderBy(g => g.Sid.AsSpan().SequenceEqual(owner) ? 0 : 1))
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
