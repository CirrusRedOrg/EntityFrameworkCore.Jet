using System.Buffers.Binary;
using LibRed;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

public class LongValueCorruptionTests
{
    [Fact]
    public void Rejects_a_short_descriptor()
    {
        using var fixture = new Fixture();
        Assert.Throws<InvalidDataException>(() =>
            fixture.Reader.Resolve(new byte[fixture.Table.Channel.Format.LongValueDescriptorSize - 1]));
    }

    [Fact]
    public void Rejects_a_truncated_inline_value()
    {
        using var fixture = new Fixture();
        byte[] descriptor = LongValueStore.Descriptor(fixture.Table.Channel.Format, 1, LongValueStore.StorageKind.Inline);
        Assert.Throws<InvalidDataException>(() => fixture.Reader.Resolve(descriptor));
    }

    [Theory]
    [InlineData("outside-file")]
    [InlineData("wrong-owner")]
    [InlineData("short-chunk")]
    [InlineData("early-end")]
    [InlineData("cycle")]
    public void Rejects_a_malformed_chained_value(string corruption)
    {
        using var fixture = new Fixture();
        (byte[] Descriptor, IReadOnlyList<int> OwnedPages, int FreePage) value = fixture.Writer.Write(new byte[5000]);
        byte[] descriptor = value.Descriptor.ToArray();
        int first = value.OwnedPages[0];

        switch (corruption)
        {
            case "outside-file":
                PageBuffer.WriteRecordPointer(descriptor, fixture.Table.Channel.Format.LongValueDescriptorPointerOffset,
                    0, fixture.Table.Channel.PageCount + 1);
                break;
            case "wrong-owner":
                byte[] wrongOwner = fixture.Table.Channel.ReadPage(first).Span.ToArray();
                BinaryPrimitives.WriteInt32LittleEndian(
                    wrongOwner.AsSpan(fixture.Table.Channel.Format.DataOwnerOffset, 4),
                    fixture.Table.Definition.DefinitionPage);
                fixture.Table.Channel.WritePage(first, wrongOwner);
                break;
            case "short-chunk":
                byte[] shortChunk = fixture.Table.Channel.ReadPage(first).Span.ToArray();
                DataPage.WriteSlot(shortChunk, fixture.Table.Channel.Format, 0,
                    fixture.Table.Channel.PageSize - (PageBuffer.RecordPointerSize - 1), RowSlotFlags.None);
                fixture.Table.Channel.WritePage(first, shortChunk);
                break;
            case "early-end":
                RewriteNextPointer(fixture, first, 0);
                break;
            case "cycle":
                RewriteNextPointer(fixture, first, first);
                break;
        }

        Assert.Throws<InvalidDataException>(() => fixture.Reader.Resolve(descriptor));
    }

    [Fact]
    public void Valid_inline_single_and_chained_values_round_trip()
    {
        using var fixture = new Fixture();
        byte[] inline = [.. LongValueStore.Descriptor(fixture.Table.Channel.Format, 3, LongValueStore.StorageKind.Inline), 1, 2, 3];
        Assert.Equal(new byte[] { 1, 2, 3 }, fixture.Reader.Resolve(inline));

        foreach (int size in new[] { 100, 5000 })
        {
            byte[] payload = Enumerable.Range(0, size).Select(i => (byte)i).ToArray();
            (byte[] Descriptor, IReadOnlyList<int> OwnedPages, int FreePage) stored = fixture.Writer.Write(payload);
            Assert.Equal(payload, fixture.Reader.Resolve(stored.Descriptor));
        }
    }

    private static void RewriteNextPointer(Fixture fixture, int pageNumber, int nextPage)
    {
        byte[] page = fixture.Table.Channel.ReadPage(pageNumber).Span.ToArray();
        var parsed = new DataPage();
        parsed.Read(fixture.Table.Channel.ReadPage(pageNumber), fixture.Table.Channel.Format);
        DataPage.RowSlot slot = parsed.Rows[0];
        PageBuffer.WriteRecordPointer(page, slot.Offset, 0, nextPage);
        fixture.Table.Channel.WritePage(pageNumber, page);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "lval-corrupt-");
        private readonly JetDatabase _database;

        public Fixture()
        {
            _database = JetDatabase.Open(_path, readOnly: false);
            Table = _database.OpenTable("Categories");
            Writer = new LongValueStore(Table.Channel);
            Reader = new LongValueStore(Table.Channel);
        }

        public Table Table { get; }
        public LongValueStore Writer { get; }
        public LongValueStore Reader { get; }

        public void Dispose()
        {
            _database.Dispose();
            TemporaryDatabase.Delete(_path);
        }
    }
}