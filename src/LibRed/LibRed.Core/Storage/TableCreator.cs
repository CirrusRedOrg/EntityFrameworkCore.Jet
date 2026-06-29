using System.Buffers.Binary;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;

namespace LibRed.Storage;

/// <summary>
/// Creates a new (heap) table in an existing database: allocates and writes its TDEF page, an
/// empty data page, and an owned-pages usage map, then records it in MSysObjects so the catalog
/// finds it. This first cut creates a no-index table and writes the catalog row heap-only (the
/// catalog is read by table scan), enough for LibRed to round-trip create → insert → query.
/// </summary>
public sealed class TableCreator(PageChannel channel, JetCatalog catalog)
{
    private readonly PageChannel _channel = channel;
    private readonly JetCatalog _catalog = catalog;
    private readonly PageAllocator _allocator = new(channel);

    public void Create(string name, IReadOnlyList<ColumnSpec> columns, IReadOnlyList<string>? primaryKey = null)
    {
        JetFormatBase format = _channel.Format;

        // Allocate the pages the table needs through the global free-pages map (so Access accounts
        // for them). Like Access, a fresh table has NO data page — the first is allocated lazily on
        // the first insert — so its usage maps start empty.
        int tdefPage = _allocator.Allocate();
        int usageMapPage = _allocator.Allocate();

        bool hasPk = primaryKey is { Count: > 0 };
        int indexRootPage = hasPk ? _allocator.Allocate() : 0;

        // Usage-map records on one page: row 0 = table owned, row 1 = table free, and (with an
        // index) row 2 = the index's pages. All start empty (the index root is referenced by the
        // index-data block, not the usage map).
        WriteUsageMaps(format, usageMapPage, mapCount: hasPk ? 3 : 2);

        // A primary key is one unique index over an empty leaf root, populated as rows are inserted.
        IndexSpec[] indexes = [];
        if (hasPk)
        {
            WriteEmptyLeafIndexPage(format, indexRootPage, owner: tdefPage);
            indexes = [new IndexSpec("PrimaryKey", primaryKey!, IsPrimaryKey: true, IsUnique: true,
                indexRootPage, UsageMapRow: 2, UsageMapPage: usageMapPage)];
        }

        // Build the definition and point it at the usage maps: owned-pages = row 0, free-pages =
        // row 1, both on the usage-map page.
        byte[] tdef = TdefBuilder.Build(format, TableType.User, columns, indexes).Page;
        const int FreePagesOffset = 0x3B;
        tdef[format.TdefOwnedPagesOffset] = 0; // owned map record row
        WriteInt24(tdef, format.TdefOwnedPagesOffset + 1, usageMapPage);
        tdef[FreePagesOffset] = 1; // free map record row
        WriteInt24(tdef, FreePagesOffset + 1, usageMapPage);
        _channel.WritePage(tdefPage, tdef);

        AddCatalogRow(name, tdefPage);
        AddPermissionRows(tdefPage);
    }

    /// <summary>Writes an empty B-tree leaf (no entries) to serve as a fresh index root.</summary>
    private void WriteEmptyLeafIndexPage(JetFormatBase format, int pageNumber, int owner)
    {
        const int EntryDataOffset = 0x1E0;
        const int OwnerOffset = 0x04;

        var page = new byte[format.PageSize];
        page[0] = (byte)PageType.LeafIndexPage;
        page[1] = 0x01; // page flags (observed constant)
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(OwnerOffset, 4), owner);
        // No entries: empty mask, no prefix compression, free space is the whole entry region.
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataFreeSpaceOffset, 2),
            (ushort)(format.PageSize - EntryDataOffset));
        _channel.WritePage(pageNumber, page);
    }

    /// <summary>
    /// Writes a data page of <paramref name="mapCount"/> empty inline usage-map records — like
    /// Access does for a fresh table that has no data page yet. Each record is
    /// <c>[0x00][startPage = 0][all-zero bitmap]</c>: row 0 = table owned-pages, row 1 = table
    /// free-pages, and (with an index) row 2 = the index's owned-pages. The first insert allocates a
    /// data page and sets the corresponding bit.
    /// </summary>
    private void WriteUsageMaps(JetFormatBase format, int pageNumber, int mapCount)
    {
        // An empty inline usage map: type byte + start page (0) + a bitmap of all-zero bytes. Access
        // writes a full-width bitmap; match its record length so the page layout matches byte-for-byte.
        const int BitmapBytes = 64;
        const int MapLength = 1 + 4 + BitmapBytes;

        var page = new byte[format.PageSize];
        page[0] = (byte)PageType.DataPage;
        page[1] = 0x01; // page flags (observed constant)
        // Owner of a usage-map page is 0 (it belongs to no table).

        int offset = format.PageSize;
        for (int row = 0; row < mapCount; row++)
        {
            offset -= MapLength;
            // page[offset] already 0x00 (inline type), start page already 0, bitmap already zero.
            BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataRowDirectoryOffset + row * 2, 2), (ushort)offset);
        }

        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataRowCountOffset, 2), (ushort)mapCount);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataFreeSpaceOffset, 2),
            (ushort)(offset - format.DataRowDirectoryOffset - mapCount * 2));
        _channel.WritePage(pageNumber, page);
    }

    // A user table's parent object is the database's "Tables" container; observed constant.
    private const int TablesContainerParentId = 0x0F000001;

    // The creating user's owner SID; for a workgroup-less database this 2-byte value is constant
    // across all user tables (verified on Northwind).
    private static readonly byte[] DefaultOwner = [0x69, 0x0C];

    /// <summary>
    /// Adds the MSysObjects row describing the new table so Access (and the catalog) see it: Id =
    /// TDEF page, ParentId = Tables container, Type = table, Name, Flags, Owner, and create/update
    /// dates. The extended-properties blob (LvProp, a long value) is left null — not writable yet.
    /// MSysObjects' own indexes (Id, and the composite ParentId+Name used for name resolution) are
    /// maintained so Access can open the table by name, not just enumerate it.
    /// </summary>
    private void AddCatalogRow(string name, int tdefPage)
    {
        TableDef msysObjects = _catalog.FindTable("MSysObjects")
            ?? throw new InvalidOperationException("MSysObjects catalog table was not found.");

        DateTime now = DateTime.Now;
        var values = new object?[msysObjects.Columns.Count];
        SetByName(msysObjects, values, "Id", tdefPage);
        SetByName(msysObjects, values, "ParentId", TablesContainerParentId);
        SetByName(msysObjects, values, "Type", (short)1); // table object
        SetByName(msysObjects, values, "Name", name);
        SetByName(msysObjects, values, "Flags", 0);
        SetByName(msysObjects, values, "Owner", DefaultOwner);
        SetByName(msysObjects, values, "DateCreate", now);
        SetByName(msysObjects, values, "DateUpdate", now);

        new RowInserter(_channel, msysObjects).Insert(values, updateIndexes: true);
    }

    // Permissions for a newly created table object: the owner (SID 0x690C) and the Admin/Users
    // SID (0x680C), each with full access (verified against an ACE-created table).
    private const int FullAccessMask = 1048319; // 0xFFEFF
    private static readonly byte[] AdminSid = [0x68, 0x0C];

    /// <summary>
    /// Adds the two MSysACEs permission rows Access writes for a new table object (owner + admin,
    /// full access), maintaining the table's ObjectId index so Access's security check sees them.
    /// </summary>
    private void AddPermissionRows(int objectId)
    {
        TableDef msysAces = _catalog.FindTable("MSysACEs")
            ?? throw new InvalidOperationException("MSysACEs catalog table was not found.");

        foreach (byte[] sid in new[] { DefaultOwner, AdminSid })
        {
            var values = new object?[msysAces.Columns.Count];
            SetByName(msysAces, values, "ACM", FullAccessMask);
            SetByName(msysAces, values, "FInheritable", false);
            SetByName(msysAces, values, "ObjectId", objectId);
            SetByName(msysAces, values, "SID", sid);
            new RowInserter(_channel, msysAces).Insert(values, updateIndexes: true);
        }
    }

    private static void SetByName(TableDef table, object?[] values, string column, object value)
    {
        ColumnDef def = table.FindColumn(column)
            ?? throw new InvalidOperationException($"MSysObjects is missing the '{column}' column.");
        values[def.Index] = value;
    }

    private static void WriteInt24(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
    }
}
