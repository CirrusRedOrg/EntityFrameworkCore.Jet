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

    public void Create(string name, IReadOnlyList<ColumnSpec> columns)
    {
        JetFormatBase format = _channel.Format;

        // Allocate the three pages the table needs.
        int tdefPage = _channel.AllocatePage();
        int dataPage = _channel.AllocatePage();
        int usageMapPage = _channel.AllocatePage();

        WriteEmptyDataPage(format, dataPage, owner: tdefPage);
        WriteOwnedPagesMap(format, usageMapPage, owner: tdefPage, ownedPage: dataPage);

        // Build the definition, point it at the owned-pages map, and write it.
        byte[] tdef = TdefBuilder.Build(format, TableType.User, columns).Page;
        tdef[format.TdefOwnedPagesOffset] = 0; // map record row
        WriteInt24(tdef, format.TdefOwnedPagesOffset + 1, usageMapPage);
        _channel.WritePage(tdefPage, tdef);

        AddCatalogRow(name, tdefPage);
    }

    private void WriteEmptyDataPage(JetFormatBase format, int pageNumber, int owner)
    {
        var page = new byte[format.PageSize];
        page[0] = (byte)PageType.DataPage;
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(format.DataOwnerOffset, 4), owner);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataRowCountOffset, 2), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataFreeSpaceOffset, 2),
            (ushort)(format.PageSize - format.DataRowDirectoryOffset));
        _channel.WritePage(pageNumber, page);
    }

    /// <summary>
    /// Writes a data page holding a single inline usage-map record (row 0) that owns exactly
    /// <paramref name="ownedPage"/>: <c>[0x00 type][startPage:4][bitmap: bit 0 set]</c>.
    /// </summary>
    private void WriteOwnedPagesMap(JetFormatBase format, int pageNumber, int owner, int ownedPage)
    {
        var page = new byte[format.PageSize];
        page[0] = (byte)PageType.DataPage;
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(format.DataOwnerOffset, 4), owner);

        byte[] map = new byte[6];
        map[0] = 0x00; // inline map
        BinaryPrimitives.WriteInt32LittleEndian(map.AsSpan(1, 4), ownedPage);
        map[5] = 0x01; // bit 0 → startPage (ownedPage) is owned

        int offset = format.PageSize - map.Length;
        map.CopyTo(page.AsSpan(offset));
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataRowCountOffset, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataRowDirectoryOffset, 2), (ushort)offset);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(format.DataFreeSpaceOffset, 2),
            (ushort)(offset - format.DataRowDirectoryOffset - 2));
        _channel.WritePage(pageNumber, page);
    }

    /// <summary>
    /// Adds a minimal MSysObjects row (Id = TDEF page, Type = table, Name, Flags = 0) so the
    /// catalog enumerates the table. Written heap-only since MSysObjects' indexes include text
    /// keys (not yet writable) and the catalog is read by table scan.
    /// </summary>
    private void AddCatalogRow(string name, int tdefPage)
    {
        TableDef msysObjects = _catalog.FindTable("MSysObjects")
            ?? throw new InvalidOperationException("MSysObjects catalog table was not found.");

        var values = new object?[msysObjects.Columns.Count];
        SetByName(msysObjects, values, "Id", tdefPage);
        SetByName(msysObjects, values, "Type", (short)1); // table object
        SetByName(msysObjects, values, "Name", name);
        SetByName(msysObjects, values, "Flags", 0);

        new RowInserter(_channel, msysObjects).Insert(values, updateIndexes: false);
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
