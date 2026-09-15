using System.Buffers.Binary;
using LibRed.Catalog;
using LibRed.IO;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// ACE holds the pages a session frees — a deleted row's long values, a dropped index, a dropped table — until the
// session closes, then returns them and anything in the global released-pages map to the global free map and
// clears the released map, lengthening it to cover the highest page released (docs/format/page-05-usage-maps.md
// §9.1). The long value an UPDATE replaces is the exception: its pages are free at once. Northwind's maps are page 1
// rows 0 (free: 310 and 329 inside the file) and 1 (released, empty).
public class ReleaseAtCloseTests
{
    [Fact]
    public void Released_pages_stay_unreusable_until_close_then_become_free()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "release-close-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                PageChannel channel = db.OpenTable("MSysObjects").Channel;
                var allocator = new PageAllocator(channel);
                allocator.Release(300);
                allocator.Release(301);

                Assert.False(MapBit(channel, 0, 300));
                Assert.False(MapBit(channel, 0, 301));
                Assert.Equal(310, allocator.Allocate());   // the free pages, never the released ones
                Assert.Equal(329, allocator.Allocate());
                Assert.Equal(353, allocator.Allocate());
            }

            using (var db = JetDatabase.Open(path))
            {
                PageChannel channel = db.OpenTable("MSysObjects").Channel;
                Assert.True(MapBit(channel, 0, 300));
                Assert.True(MapBit(channel, 0, 301));
                Assert.False(MapBit(channel, 0, 310));
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
                var allocator = new PageAllocator(channel);

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
                Assert.False(MapBit(channel, 0, 300));
                Assert.True(MapBit(channel, 0, 301));
                Assert.False(MapBit(channel, 0, 302));
                Assert.False(MapBit(channel, 0, 303));
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
                var allocator = new PageAllocator(channel);
                before = ReleasedBits(channel).Length;
                for (int i = 0; i < 400; i++) allocator.Allocate();
                highest = channel.PageCount - 1;
                allocator.Release(highest);
            }

            using (var db = JetDatabase.Open(path))
            {
                PageChannel channel = db.OpenTable("MSysObjects").Channel;
                // 5-byte header, then the bitmap to the highest page in whole 4-byte words.
                int expected = ((highest / 8 + 1) + 3) / 4 * 4;
                Assert.True(expected > before);
                Assert.Equal(expected, ReleasedBits(channel).Length);
                Assert.All(ReleasedBits(channel), b => Assert.Equal(0, b));
                Assert.True(MapBit(channel, 0, highest));
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
            byte[] page1 = ReadPage(path, 1);
            SetReleasedBit(page1, 300);
            WritePage(path, 1, page1);

            using (JetDatabase.Open(path, readOnly: false)) { }
            Assert.Equal(page1, ReadPage(path, 1));   // an idle writable close changes nothing

            using (var db = JetDatabase.Open(path, readOnly: false))
                db.CreateTable("Wrote", [new("Id", JetDataType.Int32, 4, IsFixedLength: true)]);

            using (var db = JetDatabase.Open(path))
            {
                PageChannel channel = db.OpenTable("MSysObjects").Channel;
                Assert.True(MapBit(channel, 0, 300));
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
            Assert.False(MapBit(channel, 0, root));

            table = db.OpenTable("Paths");
            (RowId id, object?[] values) = table.Rows().WithIds().Single();
            // The old chain is free at once, so the new value lands on it and the file does not grow.
            int pagesBefore = channel.PageCount;
            values[1] = new string('b', 20000);
            table.Update(id, values, new HashSet<int> { 1 });
            Assert.Equal(pagesBefore, channel.PageCount);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // ---------------------------------------------------------------- helpers

    private static (int Offset, int Length) Record(ReadOnlySpan<byte> page1, int row)
    {
        int offset = BinaryPrimitives.ReadUInt16LittleEndian(page1[(14 + row * 2)..]) & 0x1FFF;
        int end = row == 0 ? page1.Length : BinaryPrimitives.ReadUInt16LittleEndian(page1[(14 + (row - 1) * 2)..]) & 0x1FFF;
        Assert.Equal(0x00, page1[offset]);   // inline
        return (offset, end - offset);
    }

    private static bool MapBit(PageChannel channel, int row, int page)
    {
        byte[] page1 = channel.ReadPage(1).Span.ToArray();
        (int offset, int length) = Record(page1, row);
        int bit = page - BinaryPrimitives.ReadInt32LittleEndian(page1.AsSpan(offset + 1));
        if (bit < 0 || bit / 8 >= length - 5) return false;
        return (page1[offset + 5 + bit / 8] & (1 << (bit % 8))) != 0;
    }

    private static byte[] ReleasedBits(PageChannel channel)
    {
        byte[] page1 = channel.ReadPage(1).Span.ToArray();
        (int offset, int length) = Record(page1, 1);
        return page1.AsSpan(offset + 5, length - 5).ToArray();
    }

    private static void SetReleasedBit(byte[] page1, int page)
    {
        (int offset, int length) = Record(page1, 1);
        int bit = page - BinaryPrimitives.ReadInt32LittleEndian(page1.AsSpan(offset + 1));
        Assert.InRange(bit, 0, (length - 5) * 8 - 1);
        page1[offset + 5 + bit / 8] |= (byte)(1 << (bit % 8));
    }

    private static byte[] ReadPage(string path, int page)
    {
        using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var bytes = new byte[4096];
        s.Position = page * 4096L;
        s.ReadExactly(bytes);
        return bytes;
    }

    private static void WritePage(string path, int page, byte[] bytes)
    {
        using var s = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        s.Position = page * 4096L;
        s.Write(bytes);
    }
}
