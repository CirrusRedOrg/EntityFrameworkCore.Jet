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

        byte[] page = TdefBuilder.Build(Format, TableType.User, specs).Page;
        int columnBlock = Format.TdefRealIndexBlockOffset;
        int namePos = columnBlock + specs.Length * Format.ColumnDescriptorSize;
        int lvalPos = SkipNames(page, namePos, specs.Length);
        int declaredLength = BinaryPrimitives.ReadInt32LittleEndian(page.AsSpan(Format.TdefLengthOffset, 4));

        switch (corruption)
        {
            case "column-name-too-long":
                BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(namePos, 2), 130);
                page.AsSpan(namePos + 2, 130).Fill((byte)'A');
                BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(namePos + 132, 2), 0xFFFF);
                declaredLength = namePos + 134;
                break;
            case "column-name-out-of-bounds":
                BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(namePos, 2), 128);
                break;
            case "duplicate-column-id":
                BinaryPrimitives.WriteUInt16LittleEndian(
                    page.AsSpan(columnBlock + Format.ColumnDescriptorSize + Format.ColumnNumberOffset, 2), 0);
                break;
            case "column-id-255":
                BinaryPrimitives.WriteUInt16LittleEndian(
                    page.AsSpan(columnBlock + Format.ColumnNumberOffset, 2), 255);
                break;
            case "unknown-column-type":
                page[columnBlock + Format.ColumnTypeOffset] = 0xFF;
                break;
            case "missing-lval-terminator":
                declaredLength -= 2;
                break;
            case "lval-for-non-lval-column":
                WriteLvalEntry(page, lvalPos, 0);
                BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(lvalPos + 10, 2), 0xFFFF);
                declaredLength += 10;
                break;
            case "duplicate-lval-column":
                WriteLvalEntry(page, lvalPos, 0);
                WriteLvalEntry(page, lvalPos + 10, 0);
                BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(lvalPos + 20, 2), 0xFFFF);
                declaredLength += 20;
                break;
            case "odd-name-length":
                BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(namePos, 2), 1);
                break;
            case "invalid-name-utf16":
                page[namePos + 2] = 0x00;
                page[namePos + 3] = 0xD8; // unpaired UTF-16 high surrogate
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
        var definition = new TableDefinitionPage();
        Assert.Throws<InvalidDataException>(() =>
            definition.Read(new PageBuffer(page.AsMemory(0, declaredLength), 99), Format));
    }

    [Fact]
    public void Overlong_index_name_is_rejected_before_decoding()
    {
        ColumnSpec[] specs = [new("C", JetDataType.Int32, 4, IsFixedLength: true)];
        IndexSpec[] indexes = [new("I", ["C"], IsPrimaryKey: false, IsUnique: false, RootPage: 42)];
        byte[] page = TdefBuilder.Build(Format, TableType.User, specs, indexes).Page;

        int pos = Format.TdefRealIndexBlockOffset + Format.RealIndexEntrySize
            + Format.ColumnDescriptorSize;
        pos = SkipNames(page, pos, 1);
        int indexNamePos = pos + 52 + 28; // §3.5 data block + §3.6 logical-info block
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(indexNamePos, 2), 130);
        page.AsSpan(indexNamePos + 2, 130).Fill((byte)'I');
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(indexNamePos + 132, 2), 0xFFFF);
        int declaredLength = indexNamePos + 134;
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(Format.TdefLengthOffset, 4), declaredLength);

        var definition = new TableDefinitionPage();
        Assert.Throws<InvalidDataException>(() =>
            definition.Read(new PageBuffer(page.AsMemory(0, declaredLength), 99), Format));
    }

    [Fact]
    public void Valid_memo_usage_map_entry_remains_available()
    {
        ColumnSpec[] specs = [new("M", JetDataType.Memo, 0, IsFixedLength: false)];
        LongValueColumnSpec[] maps = [new(ColumnId: 0, UsedRow: 2, FreeRow: 3, MapPage: 17)];
        byte[] page = TdefBuilder.Build(Format, TableType.User, specs, longValueColumns: maps).Page;
        int declaredLength = BinaryPrimitives.ReadInt32LittleEndian(page.AsSpan(Format.TdefLengthOffset, 4));

        var definition = new TableDefinitionPage();
        definition.Read(new PageBuffer(page.AsMemory(0, declaredLength), 99), Format);

        Assert.Equal((2, 17), definition.LongValueOwnedMaps[0]);
        Assert.Equal((3, 17), definition.LongValueFreeMaps[0]);
    }

    private static int SkipNames(byte[] page, int pos, int count)
    {
        for (int i = 0; i < count; i++)
        {
            int length = BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(pos, 2));
            pos += 2 + length;
        }
        return pos;
    }

    private static void WriteLvalEntry(byte[] page, int pos, ushort columnId)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(pos, 2), columnId);
        page[pos + 2] = 2;
        page[pos + 3] = 17;
        page[pos + 6] = 3;
        page[pos + 7] = 17;
    }
}
