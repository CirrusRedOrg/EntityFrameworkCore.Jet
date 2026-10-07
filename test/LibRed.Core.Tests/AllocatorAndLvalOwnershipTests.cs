using System.Buffers.Binary;
using LibRed;
using LibRed.Catalog;
using LibRed.Formats;
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
        JetFormatBase format = fixture.Table.Channel.Format;
        (_, int holder, byte[] page, DataPage.RowSlot slot) = TestDatabases.GlobalMap(fixture.Table.Channel, format.FreePagesMapPointerOffset);
        Span<byte> map = page.AsSpan(slot.Offset, slot.Length);
        Assert.Equal(UsageMapType.Inline, UsageMap.RecordType(map));
        Span<byte> bits = UsageMap.InlineBits(map, format);
        bits.Clear();

        // "reserved": the map's own holder page, which can never be handed out.
        int start = UsageMap.StartPage(map, format);
        int target = corruption == "reserved" ? holder : fixture.Table.Channel.PageCount + 1;
        int bit = target - start;
        Assert.InRange(bit, 0, bits.Length * 8 - 1);
        BitmapBits.Set(bits, bit, true);
        fixture.Table.Channel.WritePage(holder, page);

        Assert.Throws<InvalidDataException>(() => fixture.Table.Channel.Allocator.Allocate());
    }

    [Fact]
    public void Allocator_rejects_an_out_of_file_reference_bitmap_pointer()
    {
        using var fixture = new Fixture();
        JetFormatBase format = fixture.Table.Channel.Format;
        (_, int holder, byte[] page, DataPage.RowSlot slot) = TestDatabases.GlobalMap(fixture.Table.Channel, format.FreePagesMapPointerOffset);
        Span<byte> map = page.AsSpan(slot.Offset, slot.Length);
        Assert.True(map.Length >= format.UsageMapReferenceRecordSize);
        map.Clear();
        UsageMap.NewReferenceRecord(format).CopyTo(map);
        UsageMap.WriteReferencePointer(map, 0, format, fixture.Table.Channel.PageCount + 1);
        fixture.Table.Channel.WritePage(holder, page);

        Assert.Throws<InvalidDataException>(() => fixture.Table.Channel.Allocator.Allocate());
    }

    [Fact]
    public void Lval_append_rejects_a_non_lval_data_page()
    {
        using var fixture = new Fixture();
        int tablePage = fixture.Table.UsageMap.DataPages().First();

        Assert.Throws<InvalidDataException>(() =>
            new LongValueStore(fixture.Table.Channel).TryAppend(tablePage, [1]));
    }

    [Fact]
    public void Lval_append_rejects_inconsistent_free_space_before_mutation()
    {
        using var fixture = new Fixture();
        var writer = new LongValueStore(fixture.Table.Channel);
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
        var definition = new TableDefinition();
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
        byte[] descriptor = RowCodec
            .LongValueDescriptors(table.Definition.Columns, table.Channel.Format, row)[memo.Index];
        int firstPage = LongValueStore.Read(descriptor, table.Channel.Format).Page;

        var definition = new TableDefinition();
        definition.Read(table.Channel, table.Definition.DefinitionPage);
        (int mapRow, int mapPage) = definition.LongValueOwnedMaps[memo.ColumnId];
        new UsageMap(table.Channel).SetBit(mapRow, mapPage, firstPage, set: false);

        object?[] updated = (object?[])values.Clone();
        updated[memo.Index] = new string('b', 5000);
        Assert.Throws<InvalidDataException>(() =>
            table.Update(id, updated, new HashSet<int> { memo.Index }));
        Assert.Equal(original, table.Rows().Single()[memo.Index]);
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