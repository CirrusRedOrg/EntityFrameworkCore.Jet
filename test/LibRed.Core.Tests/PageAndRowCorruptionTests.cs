using System.Buffers.Binary;
using LibRed;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

public class PageAndRowCorruptionTests
{
    private static readonly JetFormatBase Format = OpenFormat();

    [Theory]
    [InlineData("wrong-page-type")]
    [InlineData("wrong-page-type-high-byte")]
    [InlineData("short-page")]
    [InlineData("directory-past-page")]
    [InlineData("row-overlaps-directory")]
    [InlineData("row-offset-past-page")]
    [InlineData("ascending-row-offsets")]
    public void Malformed_data_page_slots_are_rejected_as_corruption(string corruption)
    {
        byte[] page = NewDataPage(rowCount: 2, firstOffset: 4000, secondOffset: 3900);
        switch (corruption)
        {
            case "wrong-page-type":
                PageHeader.WriteType(page, PageType.TableDefinition);
                break;
            case "wrong-page-type-high-byte":
                // The type is the whole word: 01 02 is not a data page, though its low byte is. ACE skips such a
                // page rather than reading it as rows.
                page[1] = 0x02;
                break;
            case "short-page":
                page = page[..100];
                break;
            case "directory-past-page":
                BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(Format.DataRowCountOffset, 2), ushort.MaxValue);
                break;
            case "row-overlaps-directory":
                DataPage.WriteSlot(page, Format, 0,
                    Format.DataRowDirectoryOffset + Format.DataRowDirectoryEntrySize, RowSlotFlags.None);
                break;
            case "row-offset-past-page":
                DataPage.WriteSlot(page, Format, 0, 5000, RowSlotFlags.None);
                break;
            case "ascending-row-offsets":
                DataPage.WriteSlot(page, Format, 1, 4050, RowSlotFlags.None);
                break;
        }

        Assert.Throws<InvalidDataException>(() => new DataPage().Read(new PageBuffer(page, 7), Format));
    }

    [Fact]
    public void Direct_row_seek_applies_the_same_slot_validation()
    {
        byte[] page = NewDataPage(rowCount: 2, firstOffset: 4000, secondOffset: 4050);

        Assert.Throws<InvalidDataException>(() =>
            DataPage.TryReadRow(new PageBuffer(page, 7), Format, 1, out _, out _));
    }

    [Fact]
    public void Zero_length_deleted_overflow_tombstone_remains_a_valid_slot_shape()
    {
        byte[] page = NewDataPage(rowCount: 2, firstOffset: 4000, secondOffset: 4000);
        DataPage.WriteSlot(page, Format, 1, 4000, RowSlotFlags.Deleted | RowSlotFlags.Overflow);

        var dataPage = new DataPage();
        dataPage.Read(new PageBuffer(page, 7), Format);

        Assert.Equal(new DataPage.RowSlot(4000, 0, IsDeleted: true, HasOverflow: true), dataPage.Rows[1]);
    }

    [Fact]
    public void All_fixed_row_does_not_parse_fixed_bytes_as_a_variable_trailer()
    {
        ColumnDef column = FixedColumn(JetDataType.Int32, length: 4);
        byte[] row = [1, 0, 0xFF, 0xFF, 0xFF, 0x7F, 1];

        object?[] values = new RowCodec([column], Format).Decode(row);

        Assert.Equal(int.MaxValue, values[0]);
    }

    [Theory]
    [InlineData("truncated-trailer")]
    [InlineData("offset-table-before-row")]
    [InlineData("nonmonotonic-offsets")]
    [InlineData("short-fixed-scalar")]
    [InlineData("missing-variable-slot")]
    public void Malformed_rows_are_rejected_as_corruption(string corruption)
    {
        ColumnDef column;
        byte[] row;
        if (corruption == "short-fixed-scalar")
        {
            column = FixedColumn(JetDataType.Int32, length: 2);
            row = [1, 0, 1, 2, 1];
        }
        else
        {
            column = new ColumnDef
            {
                Name = "V", Type = JetDataType.Text, Index = 0, ColumnId = 0,
                Length = 100, VariableIndex = 0, IsFixedLength = false,
            };
            row = corruption switch
            {
                "truncated-trailer" => [1, 0, 1],
                "offset-table-before-row" => [1, 0, 0, 0, 0, 0, 1, 0, 1],
                "missing-variable-slot" => [1, 0, 2, 0, 0, 0, 1],
                _ => [1, 0, 0x41, 0, 2, 0, 4, 0, 1, 0, 1],
            };
        }

        Assert.Throws<InvalidDataException>(() => new RowCodec([column], Format).Decode(row));
    }

    [Fact]
    public void Column_added_after_an_old_row_decodes_as_null_when_its_bitmap_bit_does_not_exist()
    {
        ColumnDef column = FixedColumn(JetDataType.Int32, length: 4, columnId: 8);
        byte[] oldRow = [1, 0, 0];

        object?[] values = new RowCodec([column], Format).Decode(oldRow);

        Assert.Null(values[0]);
    }

    [Fact]
    public void First_variable_column_added_after_an_old_all_fixed_row_decodes_as_null()
    {
        var column = new ColumnDef
        {
            Name = "V", Type = JetDataType.Text, Index = 0, ColumnId = 8,
            Length = 100, VariableIndex = 0, IsFixedLength = false,
        };
        byte[] oldRow = [1, 0, 0];

        object?[] values = new RowCodec([column], Format).Decode(oldRow);

        Assert.Null(values[0]);
    }

    private static byte[] NewDataPage(int rowCount, int firstOffset, int secondOffset)
    {
        var page = new byte[Format.PageSize];
        PageHeader.WriteType(page, PageType.DataPage);
        BinaryPrimitives.WriteUInt16LittleEndian(page.AsSpan(Format.DataRowCountOffset, 2), (ushort)rowCount);
        DataPage.WriteSlot(page, Format, 0, firstOffset, RowSlotFlags.None);
        DataPage.WriteSlot(page, Format, 1, secondOffset, RowSlotFlags.None);
        return page;
    }

    private static ColumnDef FixedColumn(JetDataType type, int length, int columnId = 0) => new()
    {
        Name = "F", Type = type, Index = 0, ColumnId = columnId,
        Length = length, FixedOffset = 0, IsFixedLength = true,
    };

    private static JetFormatBase OpenFormat()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);
        return db.Format;
    }
}