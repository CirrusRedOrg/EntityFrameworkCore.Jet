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

    public void Create(string name, IReadOnlyList<ColumnSpec> columns, IReadOnlyList<string>? primaryKey = null)
    {
        JetFormatBase format = _channel.Format;

        // Allocate the pages the table needs (index root too, so the usage map can cover it).
        int tdefPage = _channel.AllocatePage();
        int dataPage = _channel.AllocatePage();
        int usageMapPage = _channel.AllocatePage();

        bool hasPk = primaryKey is { Count: > 0 };
        int indexRootPage = hasPk ? _channel.AllocatePage() : 0;

        WriteEmptyDataPage(format, dataPage, owner: tdefPage);
        // Usage-map records on one page: row 0 = table owned, row 1 = table free, row 2 = index owned.
        WriteUsageMaps(format, usageMapPage, owner: tdefPage, tablePage: dataPage, indexPage: hasPk ? indexRootPage : null);

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

    private void WriteEmptyDataPage(JetFormatBase format, int pageNumber, int owner)
    {
        var page = new byte[format.PageSize];
        page[0] = (byte)PageType.DataPage;
        page[1] = 0x01; // page flags (observed constant)
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(format.DataOwnerOffset, 4), owner);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataRowCountOffset, 2), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataFreeSpaceOffset, 2),
            (ushort)(format.PageSize - format.DataRowDirectoryOffset));
        _channel.WritePage(pageNumber, page);
    }

    /// <summary>
    /// Writes a data page of inline usage-map records, each <c>[0x00][startPage:4][bit 0 set]</c>:
    /// row 0 = table owned-pages, row 1 = table free-pages (Access expects both, both marking the
    /// table's single empty data page), and — when the table has an index — row 2 = the index's
    /// owned-pages map, marking its root page.
    /// </summary>
    private void WriteUsageMaps(JetFormatBase format, int pageNumber, int owner, int tablePage, int? indexPage)
    {
        var page = new byte[format.PageSize];
        page[0] = (byte)PageType.DataPage;
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(format.DataOwnerOffset, 4), owner);

        static byte[] InlineMap(int startPage) { var m = new byte[] { 0x00, 0, 0, 0, 0, 0x01 }; BinaryPrimitives.WriteInt32LittleEndian(m.AsSpan(1, 4), startPage); return m; }

        // Rows are packed from the page end backward; slot directory in the same order.
        int[] startPages = indexPage is { } ip ? [tablePage, tablePage, ip] : [tablePage, tablePage];
        int offset = format.PageSize;
        for (int row = 0; row < startPages.Length; row++)
        {
            byte[] map = InlineMap(startPages[row]);
            offset -= map.Length;
            map.CopyTo(page.AsSpan(offset));
            BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataRowDirectoryOffset + row * 2, 2), (ushort)offset);
        }

        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataRowCountOffset, 2), (ushort)startPages.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataFreeSpaceOffset, 2),
            (ushort)(offset - format.DataRowDirectoryOffset - startPages.Length * 2));
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
