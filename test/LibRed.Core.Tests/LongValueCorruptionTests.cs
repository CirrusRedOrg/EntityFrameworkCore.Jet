using System.Buffers.Binary;
using LibRed;
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
        Assert.Throws<InvalidDataException>(() => fixture.Reader.Resolve(new byte[11]));
    }

    [Fact]
    public void Rejects_a_truncated_inline_value()
    {
        using var fixture = new Fixture();
        var descriptor = new byte[12];
        descriptor[0] = 1;
        descriptor[3] = 0x80;
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
        LongValueResult value = fixture.Writer.Write(new byte[5000]);
        byte[] descriptor = value.Descriptor.ToArray();
        int first = value.OwnedPages[0];

        switch (corruption)
        {
            case "outside-file":
                WritePagePointer(descriptor.AsSpan(4, 4), fixture.Table.Channel.PageCount + 1);
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
                BinaryPrimitives.WriteUInt16LittleEndian(
                    shortChunk.AsSpan(fixture.Table.Channel.Format.DataRowDirectoryOffset, 2),
                    (ushort)(fixture.Table.Channel.PageSize - 3));
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
        byte[] inline = [3, 0, 0, 0x80, 0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3];
        Assert.Equal(new byte[] { 1, 2, 3 }, fixture.Reader.Resolve(inline));

        foreach (int size in new[] { 100, 5000 })
        {
            byte[] payload = Enumerable.Range(0, size).Select(i => (byte)i).ToArray();
            LongValueResult stored = fixture.Writer.Write(payload);
            Assert.Equal(payload, fixture.Reader.Resolve(stored.Descriptor));
        }
    }

    private static void RewriteNextPointer(Fixture fixture, int pageNumber, int nextPage)
    {
        byte[] page = fixture.Table.Channel.ReadPage(pageNumber).Span.ToArray();
        var parsed = new DataPage();
        parsed.Read(fixture.Table.Channel.ReadPage(pageNumber), fixture.Table.Channel.Format);
        RowSlot slot = parsed.Rows[0];
        WritePagePointer(page.AsSpan(slot.Offset, 4), nextPage);
        fixture.Table.Channel.WritePage(pageNumber, page);
    }

    private static void WritePagePointer(Span<byte> pointer, int page)
    {
        pointer[0] = 0;
        pointer[1] = (byte)page;
        pointer[2] = (byte)(page >> 8);
        pointer[3] = (byte)(page >> 16);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"lval-corrupt-{Guid.NewGuid():N}.accdb");
        private readonly JetDatabase _database;

        public Fixture()
        {
            File.Copy(TestDatabases.NorthwindAccdb, _path);
            _database = JetDatabase.Open(_path, readOnly: false);
            Table = _database.OpenTable("Categories");
            Writer = new LongValueWriter(Table.Channel);
            Reader = new LongValueReader(Table.Channel);
        }

        public Table Table { get; }
        public LongValueWriter Writer { get; }
        public LongValueReader Reader { get; }

        public void Dispose()
        {
            _database.Dispose();
            File.Delete(_path);
        }
    }
}
