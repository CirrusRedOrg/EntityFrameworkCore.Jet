using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// ACE holds the pages a session frees — a deleted row's long values, a dropped index, a dropped table — until the
// session closes, then returns them and anything in the global released-pages map to the global free map and
// clears the released map, lengthening it to cover the highest page released (docs/format/page-05-usage-maps.md
// §9.1). The long value an UPDATE replaces is the exception: its pages are free at once. Both maps are found through
// page 0's pointers; Northwind's free map has 310 and 329 inside the file, and its released map is empty.
public class ReleaseAtCloseTests
{
    private static readonly JetFormatBase Format = TestDatabases.FormatOf(TestDatabases.NorthwindAccdb);

    [Fact]
    public void Released_pages_stay_unreusable_until_close_then_become_free()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "release-close-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                PageChannel channel = db.OpenTable("MSysObjects").Channel;
                var allocator = channel.Allocator;
                allocator.Release(300);
                allocator.Release(301);

                Assert.False(FreeMapBit(channel,300));
                Assert.False(FreeMapBit(channel,301));
                Assert.Equal(310, allocator.Allocate());   // the free pages, never the released ones
                Assert.Equal(329, allocator.Allocate());
                Assert.Equal(353, allocator.Allocate());
            }

            using (var db = JetDatabase.Open(path))
            {
                PageChannel channel = db.OpenTable("MSysObjects").Channel;
                Assert.True(FreeMapBit(channel,300));
                Assert.True(FreeMapBit(channel,301));
                Assert.False(FreeMapBit(channel,310));
                Assert.All(ReleasedBits(channel), b => Assert.Equal(0, b));
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void A_rolled_back_release_frees_nothing_and_a_savepoint_rollback_keeps_the_earlier_ones()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "release-rollback-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                PageChannel channel = db.OpenTable("MSysObjects").Channel;
                var allocator = channel.Allocator;

                channel.BeginTransaction();
                allocator.Release(300);
                channel.RollbackTransaction();

                channel.BeginTransaction();
                allocator.Release(301);
                Savepoint savepoint = channel.CreateSavepoint();
                allocator.Release(302);
                channel.RollbackToSavepoint(savepoint);
                channel.CommitTransaction();

                channel.BeginTransaction();
                allocator.Release(303);
                // left open: the close discards it
            }

            using (var db = JetDatabase.Open(path))
            {
                PageChannel channel = db.OpenTable("MSysObjects").Channel;
                Assert.False(FreeMapBit(channel,300));
                Assert.True(FreeMapBit(channel,301));
                Assert.False(FreeMapBit(channel,302));
                Assert.False(FreeMapBit(channel,303));
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void The_close_lengthens_the_released_map_to_cover_the_highest_page_released()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "release-length-");
        try
        {
            int before, highest;
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                PageChannel channel = db.OpenTable("MSysObjects").Channel;
                var allocator = channel.Allocator;
                before = ReleasedBits(channel).Length;
                for (int i = 0; i < 400; i++) allocator.Allocate();
                highest = channel.PageCount - 1;
                allocator.Release(highest);
            }

            using (var db = JetDatabase.Open(path))
            {
                PageChannel channel = db.OpenTable("MSysObjects").Channel;
                // 5-byte header, then the bitmap to the highest page in whole 4-byte words.
                int growth = Format.UsageMapInlineGrowthSize;
                int expected = (BitmapBits.ByteCount(highest + 1) + growth - 1) / growth * growth;
                Assert.True(expected > before);
                Assert.Equal(expected, ReleasedBits(channel).Length);
                Assert.All(ReleasedBits(channel), b => Assert.Equal(0, b));
                Assert.True(FreeMapBit(channel,highest));
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Pages_already_in_the_released_map_are_merged_by_a_close_that_wrote_but_not_by_an_idle_one()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "release-merge-");
        try
        {
            int holderPage;
            byte[] holder;
            using (var channel = PageChannel.Open(path, readOnly: true))
            {
                (_, holderPage, holder, DataPage.RowSlot slot) = TestDatabases.GlobalMap(channel, Format.ReleasedPagesMapPointerOffset);
                Span<byte> record = InlineMap(holder, slot);
                int bit = 300 - UsageMap.StartPage(record, Format);
                Span<byte> bits = UsageMap.InlineBits(record, Format);
                Assert.InRange(bit, 0, bits.Length * 8 - 1);
                BitmapBits.Set(bits, bit, true);
            }
            TestDatabases.WritePage(path, holderPage, holder);

            using (JetDatabase.Open(path, readOnly: false)) { }
            Assert.Equal(holder, TestDatabases.ReadPage(path, holderPage));   // an idle writable close changes nothing

            using (var db = JetDatabase.Open(path, readOnly: false))
                db.CreateTable("Wrote", [new("Id", JetDataType.Int32, 4, IsFixedLength: true)]);

            using (var db = JetDatabase.Open(path))
            {
                PageChannel channel = db.OpenTable("MSysObjects").Channel;
                Assert.True(FreeMapBit(channel,300));
                Assert.All(ReleasedBits(channel), b => Assert.Equal(0, b));
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Dropping_an_index_holds_its_root_but_a_memo_update_frees_the_old_chain_at_once()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "release-paths-");
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            db.CreateTable("Paths",
                [new("Id", JetDataType.Int32, 4, IsFixedLength: true),
                 new("M", JetDataType.Memo, 0, IsFixedLength: false)],
                primaryKey: ["Id"]);
            Table table = db.OpenTable("Paths");
            table.Insert([1, new string('a', 20000)]);
            PageChannel channel = table.Channel;

            int root = db.Catalog.FindTable("Paths")!.Indexes.Single().RootPage;
            db.DropIndex("Paths", db.Catalog.FindTable("Paths")!.Indexes.Single().Name);
            Assert.False(FreeMapBit(channel,root));

            table = db.OpenTable("Paths");
            (RowId id, object?[] values) = table.Rows().WithIds().Single();
            // The old chain is freed after the new value is written, as ACE does, so this update grows the file...
            int pagesBefore = channel.PageCount;
            values[1] = new string('b', 20000);
            table.Update(id, values, new HashSet<int> { 1 });
            Assert.True(channel.PageCount > pagesBefore);

            // ...but it is free at once, not held to close: the next update lands on it and the file does not grow.
            int pagesAfterFirst = channel.PageCount;
            values[1] = new string('c', 20000);
            table.Update(id, values, new HashSet<int> { 1 });
            Assert.Equal(pagesAfterFirst, channel.PageCount);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>A global map's record on its holder page, which these tests expect in inline form.</summary>
    private static Span<byte> InlineMap(byte[] holder, DataPage.RowSlot slot)
    {
        Span<byte> record = holder.AsSpan(slot.Offset, slot.Length);
        Assert.Equal(UsageMapType.Inline, UsageMap.RecordType(record));
        return record;
    }

    private static bool FreeMapBit(PageChannel channel, int page)
    {
        (_, _, byte[] holder, DataPage.RowSlot slot) = TestDatabases.GlobalMap(channel, Format.FreePagesMapPointerOffset);
        Span<byte> record = InlineMap(holder, slot);
        int bit = page - UsageMap.StartPage(record, Format);
        Span<byte> bits = UsageMap.InlineBits(record, Format);
        if (bit < 0 || bit / 8 >= bits.Length) return false;
        return BitmapBits.Get(bits, bit);
    }

    private static byte[] ReleasedBits(PageChannel channel)
    {
        (_, _, byte[] holder, DataPage.RowSlot slot) = TestDatabases.GlobalMap(channel, Format.ReleasedPagesMapPointerOffset);
        return UsageMap.InlineBits(InlineMap(holder, slot), Format).ToArray();
    }
}