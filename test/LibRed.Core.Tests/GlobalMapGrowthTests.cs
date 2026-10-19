using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

public class GlobalMapGrowthTests : TempDatabaseTest
{
    // ACE keeps the session's reservation through a rollback — to a savepoint (a nested transaction, a failed
    // statement) as of the transaction — and puts the pages the undone writes took from it in the free map rather
    // than back into it, so the next page is the reservation's next (measured: RollbackAllocationProbeTest).
    [Fact]
    public void Rollback_keeps_the_reservation_and_frees_the_pages_it_took()
    {
        // A fresh file, so the reservation the first page opens holds the pages after it, as in every case measured.
        string path = TemporaryDatabase.CreatePath("allocation-rollback-");
        JetDatabase.Create(path);
        using var channel = PageChannel.Open(path, readOnly: false);
        channel.BeginTransaction();
        int first = channel.Allocator.Allocate();
        var savepoint = channel.CreateSavepoint();
        int second = channel.Allocator.Allocate();
        channel.RollbackToSavepoint(savepoint);
        Assert.Contains(second, GlobalFree(channel));
        Assert.Equal(second + 1, channel.Allocator.Allocate());
        channel.RollbackTransaction();
        HashSet<int> free = GlobalFree(channel);
        Assert.True(free.Contains(first) && free.Contains(second) && free.Contains(second + 1), $"free [{string.Join(",", free.Order())}]");
        channel.BeginTransaction();
        Assert.Equal(second + 2, channel.Allocator.Allocate());
        channel.CommitTransaction();
    }

    private static HashSet<int> GlobalFree(PageChannel channel)
    {
        (_, _, byte[] holder, DataPage.RowSlot slot) = TestDatabases.GlobalMap(channel, channel.Format.FreePagesMapPointerOffset);
        return [.. UsageMap.PagesInRecord(channel, holder.AsSpan(slot.Offset, slot.Length), null, "The global free map")];
    }

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
        var allocator = channel.Allocator;
        int pagesPerBitmapPage = channel.Format.UsageMapPagesPerBitmapPage;
        int window = channel.Format.UsageMapInlineBitmapSize * 8;
        while (channel.PageCount < window) allocator.Allocate();
        Assert.Equal(channel.Format.UsageMapInlineRecordSize, ReadMap(channel).Length);
        Assert.Equal(window, allocator.Allocate());
        byte[] grown = ReadMap(channel);
        Assert.Equal(channel.Format.UsageMapInlineRecordSize + channel.Format.UsageMapInlineGrowthSize, grown.Length);
        // ACE's live CREATE INDEX on this same Northwind fixture reserves 512–543; close returns 513–543.
        // Measured by PageGroupProbeTest.Global_growth_reservations_on_the_unit_test_fixture.
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, grown[^4..]);

        while (channel.PageCount < 32000) allocator.Allocate();
        Assert.Equal(4005, ReadMap(channel).Length);   // measured from ACE at 32,000 pages, not derived
        int holder = TestDatabases.GlobalMap(channel, channel.Format.FreePagesMapPointerOffset).Page;
        byte[] before = channel.ReadPage(holder).Span.ToArray();
        channel.BeginTransaction();
        Assert.Equal(32001, allocator.Allocate());
        Assert.Equal(32002, channel.PageCount);
        Assert.Equal(channel.Format.UsageMapReferenceRecordSize, ReadMap(channel).Length);
        channel.RollbackTransaction();
        Assert.Equal(32000, channel.PageCount);
        Assert.Equal(before, channel.ReadPage(holder).Span.ToArray());

        Assert.Equal(32001, allocator.Allocate());
        byte[] reference = ReadMap(channel);
        Assert.Equal(UsageMapType.Reference, UsageMap.RecordType(reference));
        Assert.Equal(32000, UsageMap.ReferencePointer(reference, 0, channel.Format));
        AssertBitmap(channel, 32000, 32032); // bitmap + used page + unused live reservation
        while (channel.PageCount < pagesPerBitmapPage) allocator.Allocate();
        Assert.Equal(pagesPerBitmapPage + 1, allocator.Allocate());
        reference = ReadMap(channel);
        Assert.Equal(pagesPerBitmapPage, UsageMap.ReferencePointer(reference, 1, channel.Format));
        AssertBitmap(channel, pagesPerBitmapPage, 32);
        // Finish the reservation before checking lowest-free reuse in the next allocation scope.
        allocator.ReturnReleasedPages();
        allocator.Free(pagesPerBitmapPage + 1);
        Assert.Equal(pagesPerBitmapPage + 1, allocator.Allocate());
    }

    /// <summary>The global free map's record, wherever page 0 says it is.</summary>
    private static byte[] ReadMap(PageChannel channel)
    {
        (_, _, byte[] holder, DataPage.RowSlot slot) = TestDatabases.GlobalMap(channel, channel.Format.FreePagesMapPointerOffset);
        return holder[slot.Offset..(slot.Offset + slot.Length)];
    }

    private static void AssertBitmap(PageChannel channel, int number, int firstFree)
    {
        byte[] bitmap = channel.ReadPage(number).Span.ToArray();
        Assert.Equal(PageType.PageUsageBitmap, PageHeader.ReadType(bitmap));
        Assert.Equal(new byte[] { 0, 0 }, bitmap[sizeof(ushort)..channel.Format.UsageMapBitmapPageHeaderSize]);
        Span<byte> bits = UsageMap.BitmapPageBits(bitmap, channel.Format);
        for (int bit = 0; bit < bits.Length * 8; bit++)
            Assert.Equal(bit >= firstFree, BitmapBits.Get(bits, bit));
    }
}
