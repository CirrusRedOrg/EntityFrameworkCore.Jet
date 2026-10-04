using System.Buffers.Binary;
using LibRed;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;
using Xunit;

namespace LibRed.Core.Tests;

public class TdefVariableRegionTests
{
    private static readonly JetFormatBase Format = OpenFormat();

    private static JetFormatBase OpenFormat()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);
        return db.Format;
    }

    [Theory]
    [InlineData("column-name-too-long")]
    [InlineData("column-name-out-of-bounds")]
    [InlineData("duplicate-column-id")]
    [InlineData("column-id-255")]
    [InlineData("unknown-column-type")]
    [InlineData("missing-lval-terminator")]
    [InlineData("lval-for-non-lval-column")]
    [InlineData("duplicate-lval-column")]
    [InlineData("odd-name-length")]
    [InlineData("invalid-name-utf16")]
    [InlineData("trailing-after-lval")]
    [InlineData("variable-index-out-of-range")]
    public void Malformed_variable_regions_are_rejected_with_a_corruption_error(string corruption)
    {
        ColumnSpec[] specs = corruption is "duplicate-column-id"
            ?
            [
                new("A", JetDataType.Int32, 4, IsFixedLength: true),
                new("B", JetDataType.Int32, 4, IsFixedLength: true),
            ]
            : corruption is "duplicate-lval-column"
                ? [new("M", JetDataType.Memo, 0, IsFixedLength: false)]
                : [new("C", JetDataType.Int32, 4, IsFixedLength: true)];

        byte[] page = TableDefinition.Build(Format, TableType.User, specs, Collation.GeneralLegacy).Page;
        // Where the regions are in the sound definition, before any corruption: with no index, the long-value
        // list follows the column names directly.
        TableDefinition.Regions regions = TableDefinition.Regions.Of(page, Format);
        int columnBlock = regions.ColumnDescriptors;
        int namePos = columnBlock + specs.Length * Format.ColumnDescriptorSize;
        int lvalPos = regions.IndexNames;
        int declaredLength = BinaryPrimitives.ReadInt32LittleEndian(page.AsSpan(Format.TdefLengthOffset, 4));
        int overlongName = Format.MaxNameBytes + sizeof(char);

        switch (corruption)
        {
            case "column-name-too-long":
                BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(namePos, Format.TdefNameLengthSize), (ushort)overlongName);
                page.AsSpan(namePos + Format.TdefNameLengthSize, overlongName).Fill((byte)'A');
                BinaryPrimitives.WriteUInt16LittleEndian(
                    page.AsSpan(namePos + Format.TdefNameLengthSize + overlongName, 2), JetFormatBase.TdefLongValueMapTerminator);
                declaredLength = namePos + Format.TdefNameLengthSize + overlongName + sizeof(ushort);
                break;
            case "column-name-out-of-bounds":
                BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(namePos, Format.TdefNameLengthSize), (ushort)Format.MaxNameBytes);
                break;
            case "duplicate-column-id":
                BinaryPrimitives.WriteUInt16LittleEndian(
                    page.AsSpan(columnBlock + Format.ColumnDescriptorSize + Format.ColumnNumberOffset, 2), 0);
                break;
            case "column-id-255":
                BinaryPrimitives.WriteUInt16LittleEndian(
                    page.AsSpan(columnBlock + Format.ColumnNumberOffset, 2), (ushort)Format.MaxColumnsPerTable);
                break;
            case "unknown-column-type":
                page[columnBlock + Format.ColumnTypeOffset] = 0xFF;
                break;
            case "missing-lval-terminator":
                declaredLength -= sizeof(ushort);
                break;
            case "lval-for-non-lval-column":
                WriteLvalEntry(page, lvalPos, 0);
                BinaryPrimitives.WriteUInt16LittleEndian(
                    page.AsSpan(lvalPos + Format.TdefLongValueMapEntrySize, 2), JetFormatBase.TdefLongValueMapTerminator);
                declaredLength += Format.TdefLongValueMapEntrySize;
                break;
            case "duplicate-lval-column":
                WriteLvalEntry(page, lvalPos, 0);
                WriteLvalEntry(page, lvalPos + Format.TdefLongValueMapEntrySize, 0);
                BinaryPrimitives.WriteUInt16LittleEndian(
                    page.AsSpan(lvalPos + 2 * Format.TdefLongValueMapEntrySize, 2), JetFormatBase.TdefLongValueMapTerminator);
                declaredLength += 2 * Format.TdefLongValueMapEntrySize;
                break;
            case "odd-name-length":
                BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(namePos, Format.TdefNameLengthSize), 1);
                break;
            case "invalid-name-utf16":
                page[namePos + Format.TdefNameLengthSize] = 0x00;
                page[namePos + Format.TdefNameLengthSize + 1] = 0xD8; // unpaired UTF-16 high surrogate
                break;
            case "trailing-after-lval":
                page[declaredLength] = 0;
                declaredLength++;
                break;
            case "variable-index-out-of-range":
                BinaryPrimitives.WriteUInt16LittleEndian(
                    page.AsSpan(columnBlock + Format.ColumnVariableIndexOffset, 2), 1);
                break;
        }

        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(Format.TdefLengthOffset, 4), declaredLength);
        var definition = new TableDefinition();
        Assert.Throws<InvalidDataException>(() =>
            definition.Read(new PageBuffer(page.AsMemory(0, declaredLength), 99), Format));
    }

    [Fact]
    public void Overlong_index_name_is_rejected_before_decoding()
    {
        ColumnSpec[] specs = [new("C", JetDataType.Int32, 4, IsFixedLength: true)];
        IndexSpec[] indexes = [new("I", ["C"], IsPrimaryKey: false, IsUnique: false, RootPage: 42)];
        byte[] page = TableDefinition.Build(Format, TableType.User, specs, Collation.GeneralLegacy, indexes).Page;

        int indexNamePos = TableDefinition.Regions.Of(page, Format).IndexNames;
        int overlongName = Format.MaxNameBytes + sizeof(char);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(indexNamePos, Format.TdefNameLengthSize), (ushort)overlongName);
        page.AsSpan(indexNamePos + Format.TdefNameLengthSize, overlongName).Fill((byte)'I');
        BinaryPrimitives.WriteUInt16LittleEndian(
            page.AsSpan(indexNamePos + Format.TdefNameLengthSize + overlongName, 2), JetFormatBase.TdefLongValueMapTerminator);
        int declaredLength = indexNamePos + Format.TdefNameLengthSize + overlongName + sizeof(ushort);
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(Format.TdefLengthOffset, 4), declaredLength);

        var definition = new TableDefinition();
        Assert.Throws<InvalidDataException>(() =>
            definition.Read(new PageBuffer(page.AsMemory(0, declaredLength), 99), Format));
    }

    [Theory]
    [InlineData("negative-length")]
    [InlineData("shorter-than-header")]
    [InlineData("longer-than-buffer")]
    [InlineData("too-many-columns")]
    [InlineData("variable-column-high-water-overflow")]
    [InlineData("negative-index-count")]
    [InlineData("too-many-indexes")]
    [InlineData("negative-logical-index-count")]
    [InlineData("logical-index-region-overflow")]
    public void Malformed_header_counts_and_lengths_are_rejected_before_region_allocation(string corruption)
    {
        byte[] page = TableDefinition.Build(Format, TableType.User,
            [new("C", JetDataType.Int32, 4, IsFixedLength: true)], Collation.GeneralLegacy).Page;
        int declaredLength = BinaryPrimitives.ReadInt32LittleEndian(page.AsSpan(Format.TdefLengthOffset, 4));

        switch (corruption)
        {
            case "negative-length":
                BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(Format.TdefLengthOffset, 4), -1);
                break;
            case "shorter-than-header":
                BinaryPrimitives.WriteInt32LittleEndian(
                    page.AsSpan(Format.TdefLengthOffset, 4), Format.TdefRealIndexBlockOffset - 1);
                break;
            case "longer-than-buffer":
                BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(Format.TdefLengthOffset, 4), declaredLength + 1);
                break;
            case "too-many-columns":
                BinaryPrimitives.WriteUInt16LittleEndian(
                    page.AsSpan(Format.TdefColumnCountOffset, 2), (ushort)(Format.MaxColumnsPerTable + 1));
                break;
            case "variable-column-high-water-overflow":
                BinaryPrimitives.WriteUInt16LittleEndian(
                    page.AsSpan(Format.TdefVariableColumnsOffset, 2), (ushort)(Format.MaxColumnsPerTable + 1));
                break;
            case "negative-index-count":
                BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(Format.TdefIndexCountOffset, 4), -1);
                break;
            case "too-many-indexes":
                BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(Format.TdefIndexCountOffset, 4), Format.MaxIndexesPerTable + 1);
                break;
            case "negative-logical-index-count":
                BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(Format.TdefLogicalIndexCountOffset, 4), -1);
                break;
            case "logical-index-region-overflow":
                BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(Format.TdefLogicalIndexCountOffset, 4), int.MaxValue);
                break;
        }

        var definition = new TableDefinition();
        Assert.Throws<InvalidDataException>(() =>
            definition.Read(new PageBuffer(page.AsMemory(0, declaredLength), 99), Format));
    }

    [Fact]
    public void Valid_memo_usage_map_entry_remains_available()
    {
        ColumnSpec[] specs = [new("M", JetDataType.Memo, 0, IsFixedLength: false)];
        LongValueColumnSpec[] maps = [new(ColumnId: 0, UsedRow: 2, FreeRow: 3, MapPage: 17)];
        byte[] page = TableDefinition.Build(Format, TableType.User, specs, Collation.GeneralLegacy, longValueColumns: maps).Page;
        int declaredLength = BinaryPrimitives.ReadInt32LittleEndian(page.AsSpan(Format.TdefLengthOffset, 4));

        var definition = new TableDefinition();
        definition.Read(new PageBuffer(page.AsMemory(0, declaredLength), 99), Format);

        Assert.Equal((2, 17), definition.LongValueOwnedMaps[0]);
        Assert.Equal((3, 17), definition.LongValueFreeMaps[0]);
    }

    private static void WriteLvalEntry(byte[] page, int pos, ushort columnId) =>
        TableDefinition.LongValueMapEntry(Format, columnId, usedRow: 2, freeRow: 3, mapPage: 17).CopyTo(page, pos);
}