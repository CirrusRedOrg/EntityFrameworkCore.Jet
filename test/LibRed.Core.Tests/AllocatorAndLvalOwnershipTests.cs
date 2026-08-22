using System.Buffers.Binary;
using LibRed;
using LibRed.Catalog;
using LibRed.IO;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

public class AllocatorAndLvalOwnershipTests
{
    [Theory]
    [InlineData("reserved")]
    [InlineData("outside-file")]
    public void Allocator_rejects_invalid_pages_marked_free(string corruption)
    {
        using var fixture = new Fixture();
        (byte[] page, RowSlot slot) = GlobalMap(fixture.Table);
        Span<byte> map = page.AsSpan(slot.Offset, slot.Length);
        Assert.Equal(0, map[0]);
        map[5..].Clear();

        int start = BinaryPrimitives.ReadInt32LittleEndian(map.Slice(1, 4));
        int target = corruption == "reserved" ? 1 : fixture.Table.Channel.PageCount + 1;
        int bit = target - start;
        Assert.InRange(bit, 0, (map.Length - 5) * 8 - 1);
        map[5 + bit / 8] |= (byte)(1 << (bit % 8));
        fixture.Table.Channel.WritePage(1, page);

        Assert.Throws<InvalidDataException>(() => new PageAllocator(fixture.Table.Channel).Allocate());
    }

    [Fact]
    public void Allocator_rejects_an_out_of_file_reference_bitmap_pointer()
    {
        using var fixture = new Fixture();
        (byte[] page, RowSlot slot) = GlobalMap(fixture.Table);
        Span<byte> map = page.AsSpan(slot.Offset, slot.Length);
        Assert.True(map.Length >= 69);
        map.Clear();
        map[0] = 1;
        BinaryPrimitives.WriteInt32LittleEndian(map.Slice(1, 4), fixture.Table.Channel.PageCount + 1);
        fixture.Table.Channel.WritePage(1, page);

        Assert.Throws<InvalidDataException>(() => new PageAllocator(fixture.Table.Channel).Allocate());
    }

    [Fact]
    public void Lval_append_rejects_a_non_lval_data_page()
    {
        using var fixture = new Fixture();
        int tablePage = fixture.Table.UsageMap.DataPages().First();

        Assert.Throws<InvalidDataException>(() =>
            new LongValueWriter(fixture.Table.Channel).TryAppend(tablePage, [1]));
    }

    [Fact]
    public void Lval_append_rejects_inconsistent_free_space_before_mutation()
    {
        using var fixture = new Fixture();
        var writer = new LongValueWriter(fixture.Table.Channel);
        int pageNumber = writer.WriteNewPage([1]);
        byte[] page = fixture.Table.Channel.ReadPage(pageNumber).Span.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(
            page.AsSpan(fixture.Table.Channel.Format.DataFreeSpaceOffset, 2), ushort.MaxValue);
        fixture.Table.Channel.WritePage(pageNumber, page);

        Assert.Throws<InvalidDataException>(() => writer.TryAppend(pageNumber, [2]));
    }

    [Fact]
    public void Lval_write_rejects_a_usage_map_pointer_to_an_owned_data_page_shape()
    {
        using var fixture = new Fixture();
        ColumnDef column = fixture.Table.Definition.Columns.First(c => c.Type == JetDataType.Ole);
        var definition = new TableDefinitionPage();
        definition.Read(fixture.Table.Channel, fixture.Table.Definition.DefinitionPage);
        (_, int mapPage) = definition.LongValueOwnedMaps[column.ColumnId];

        byte[] page = fixture.Table.Channel.ReadPage(mapPage).Span.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(
            page.AsSpan(fixture.Table.Channel.Format.DataOwnerOffset, 4),
            fixture.Table.Definition.DefinitionPage);
        fixture.Table.Channel.WritePage(mapPage, page);

        Assert.Throws<InvalidDataException>(() =>
            new RowInserter(fixture.Table.Channel, fixture.Table.Definition)
                .StorePackedLongValue(column.ColumnId, new byte[100]));
    }

    [Fact]
    public void Lval_reclamation_validates_the_complete_chain_and_owned_map_before_freeing()
    {
        using var fixture = new Fixture();
        fixture.Database.CreateTable("LvalOwned",
            [new("Id", JetDataType.Int32, 4, IsFixedLength: true),
             new("M", JetDataType.Memo, 0, IsFixedLength: false)],
            primaryKey: ["Id"]);
        Table table = fixture.Database.OpenTable("LvalOwned");
        string original = new('a', 5000);
        table.Insert([1, original]);

        (RowId id, object?[] values) = table.Rows().WithIds().Single();
        PageBuffer page = table.Channel.ReadPage(id.Page);
        Assert.True(DataPage.TryReadRow(page, table.Channel.Format, id.Row, out _, out ReadOnlySpan<byte> row));
        ColumnDef memo = table.Definition.FindColumn("M")!;
        byte[] descriptor = new RowDecoder(table.Definition.Columns, table.Channel.Format)
            .LongValueRaw(row)[memo.Index];
        int firstPage = descriptor[5] | descriptor[6] << 8 | descriptor[7] << 16;

        var definition = new TableDefinitionPage();
        definition.Read(table.Channel, table.Definition.DefinitionPage);
        (int mapRow, int mapPage) = definition.LongValueOwnedMaps[memo.ColumnId];
        new UsageMapWriter(table.Channel).SetBit(mapRow, mapPage, firstPage, set: false);

        object?[] updated = (object?[])values.Clone();
        updated[memo.Index] = new string('b', 5000);
        Assert.Throws<InvalidDataException>(() =>
            table.Update(id, updated, new HashSet<int> { memo.Index }));
        Assert.Equal(original, table.Rows().Single()[memo.Index]);
    }

    private static (byte[] Page, RowSlot Slot) GlobalMap(Table table)
    {
        byte[] page = table.Channel.ReadPage(1).Span.ToArray();
        var parsed = new DataPage();
        parsed.Read(table.Channel.ReadPage(1), table.Channel.Format);
        return (page, parsed.Rows[0]);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "alloc-lval-");
        private readonly JetDatabase _database;

        public Fixture()
        {
            _database = JetDatabase.Open(_path, readOnly: false);
            Table = _database.OpenTable("Categories");
        }

        public Table Table { get; }
        public JetDatabase Database => _database;

        public void Dispose()
        {
            _database.Dispose();
            TemporaryDatabase.Delete(_path);
        }
    }
}
