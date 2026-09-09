using System.Buffers.Binary;
using LibRed.IO;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

public class GlobalMapGrowthTests : TempDatabaseTest
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Writes_beyond_measured_ACE_file_limit_leave_database_unchanged(bool transactional)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "global-limit-");
        byte[] before = File.ReadAllBytes(path);
        using (var channel = PageChannel.Open(path, readOnly: false))
        {
            int pages = channel.PageCount;
            if (transactional) channel.BeginTransaction();
            var exception = Assert.Throws<InvalidOperationException>(() =>
                channel.WritePage(524288, new byte[channel.PageSize]));
            Assert.Contains("2 GiB", exception.Message);
            Assert.Equal(pages, channel.PageCount);
            if (transactional) channel.CommitTransaction();
        }
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void Growth_matches_measured_ACE_geometry_and_rolls_back()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "global-growth-");
        using var channel = PageChannel.Open(path, readOnly: false);
        var allocator = new PageAllocator(channel);
        while (channel.PageCount < 512) allocator.Allocate();
        Assert.Equal(69, ReadMap(channel).Length);
        Assert.Equal(512, allocator.Allocate());
        byte[] grown = ReadMap(channel);
        Assert.Equal(73, grown.Length);
        Assert.Equal(new byte[] { 0xFE, 0xFF, 0xFF, 0xFF }, grown[^4..]);

        while (channel.PageCount < 32000) allocator.Allocate();
        Assert.Equal(4005, ReadMap(channel).Length);
        byte[] before = channel.ReadPage(1).Span.ToArray();
        channel.BeginTransaction();
        Assert.Equal(32001, allocator.Allocate());
        Assert.Equal(32002, channel.PageCount);
        Assert.Equal(69, ReadMap(channel).Length);
        channel.RollbackTransaction();
        Assert.Equal(32000, channel.PageCount);
        Assert.Equal(before, channel.ReadPage(1).Span.ToArray());

        Assert.Equal(32001, allocator.Allocate());
        byte[] reference = ReadMap(channel);
        Assert.Equal(1, reference[0]);
        Assert.Equal(32000, BinaryPrimitives.ReadInt32LittleEndian(reference.AsSpan(1)));
        AssertBitmap(channel, 32000, 32002);
        while (channel.PageCount < 32736) allocator.Allocate();
        Assert.Equal(32737, allocator.Allocate());
        reference = ReadMap(channel);
        Assert.Equal(32736, BinaryPrimitives.ReadInt32LittleEndian(reference.AsSpan(5)));
        AssertBitmap(channel, 32736, 2);
        allocator.Free(32737);
        Assert.Equal(32737, allocator.Allocate());
    }

    private static byte[] ReadMap(PageChannel channel)
    {
        var page = new DataPage();
        page.Read(channel.ReadPage(1), channel.Format);
        return page.GetRow(0).ToArray();
    }

    private static void AssertBitmap(PageChannel channel, int number, int firstFree)
    {
        byte[] bitmap = channel.ReadPage(number).Span.ToArray();
        Assert.Equal(new byte[] { 5, 1, 0, 0 }, bitmap[..4]);
        for (int bit = 0; bit < (bitmap.Length - 4) * 8; bit++)
            Assert.Equal(bit >= firstFree, (bitmap[4 + bit / 8] & (1 << (bit % 8))) != 0);
    }
}
