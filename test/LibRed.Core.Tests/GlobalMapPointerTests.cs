using System.Buffers.Binary;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// Page 0's 0x18 and 0x1C are [row][page] pointers to the global free-pages and released-pages usage maps. ACE
// follows both wherever they point, never allocates a released page, and a file whose pointers are wrong is
// corrupt to it (docs/format/page-05-usage-maps.md §9.1). Northwind's maps are the ACE layout: page 1 row 0 free
// (pages 310 and 329 inside the file, 353..511 past its end), page 1 row 1 released and empty.
public class GlobalMapPointerTests
{
    private const int NorthwindPages = 353;

    [Fact]
    public void Page_zero_decodes_the_global_map_pointers()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "globalptr-decode-");
        try
        {
            using var db = JetDatabase.Open(path);
            Assert.Equal((0, 1), db.DefinitionPage.FreePagesMap);
            Assert.Equal((1, 1), db.DefinitionPage.ReleasedPagesMap);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Allocation_follows_page_zero_to_maps_on_another_page()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "globalptr-moved-");
        try
        {
            // Copy page 1 to a new page 353, mark 353 itself used in the copy, and aim both pointers at it.
            byte[] page1 = ReadPage(path, 1);
            byte[] moved = (byte[])page1.Clone();
            SetMapBit(moved, row: 0, page: NorthwindPages, set: false);
            AppendPage(path, moved);
            WritePointer(path, JetFormatBase.FreePagesMapPointerOffset, row: 0, page: NorthwindPages);
            WritePointer(path, JetFormatBase.ReleasedPagesMapPointerOffset, row: 1, page: NorthwindPages);
            // Poison page 1's stale free map: were it still read, the next allocation would be page 400.
            byte[] stale = (byte[])page1.Clone();
            for (int p = 0; p < 512; p++) SetMapBit(stale, row: 0, page: p, set: p == 400);
            WritePage(path, 1, stale);

            using (var channel = PageChannel.Open(path, readOnly: false))
            {
                var allocator = new PageAllocator(channel);
                Assert.Equal(310, allocator.Allocate());
                Assert.Equal(329, allocator.Allocate());
                Assert.Equal(354, allocator.Allocate());   // 353 is the map's own page, and used
            }

            Assert.Equal(stale, ReadPage(path, 1));        // never read or written
            byte[] after = ReadPage(path, NorthwindPages);
            Assert.False(MapBit(after, 0, 310));
            Assert.False(MapBit(after, 0, 329));
            Assert.False(MapBit(after, 0, 354));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Allocation_skips_pages_set_in_the_released_map()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "globalptr-released-");
        try
        {
            byte[] page1 = ReadPage(path, 1);
            SetMapBit(page1, row: 1, page: 310, set: true);
            SetMapBit(page1, row: 1, page: 329, set: true);
            WritePage(path, 1, page1);

            using (var channel = PageChannel.Open(path, readOnly: false))
            {
                var allocator = new PageAllocator(channel);
                Assert.Equal(NorthwindPages, allocator.Allocate());       // past the two released pages
                Assert.Equal(NorthwindPages + 1, allocator.Allocate());
            }

            byte[] after = ReadPage(path, 1);
            Assert.True(MapBit(after, 0, 310));   // still free: released, not taken
            Assert.True(MapBit(after, 0, 329));
            Assert.True(MapBit(after, 1, 310));   // and still released
            Assert.True(MapBit(after, 1, 329));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Released pages can sit past the end of the file (a session that claimed them never wrote them). The file
    // stays contiguous, so they are materialized — but the allocation is the first page after them.
    [Fact]
    public void Released_pages_at_the_end_of_the_file_are_materialized_but_not_allocated()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "globalptr-frontier-");
        try
        {
            byte[] page1 = ReadPage(path, 1);
            foreach (int p in new[] { 310, 329, NorthwindPages, NorthwindPages + 1 })
                SetMapBit(page1, row: 1, page: p, set: true);
            WritePage(path, 1, page1);

            using (var channel = PageChannel.Open(path, readOnly: false))
            {
                Assert.Equal(NorthwindPages + 2, new PageAllocator(channel).Allocate());
                Assert.Equal(NorthwindPages + 3, channel.PageCount);
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    public static TheoryData<string, int, int, int, int> InvalidPointers => new()
    {
        // name, free row, free page, released row, released page
        { "free map past the end of the file", 0, NorthwindPages, 1, 1 },
        { "released map past the end of the file", 0, 1, 1, NorthwindPages },
        { "both maps on the same record", 0, 1, 0, 1 },
        { "free map on a TDEF page", 0, 2, 1, 1 },
        { "released map on a row the page does not have", 0, 1, 5, 1 },
        { "free map on page 0", 0, 0, 1, 1 },
    };

    [Theory]
    [MemberData(nameof(InvalidPointers))]
    public void A_writable_open_refuses_invalid_global_map_pointers(
        string name, int freeRow, int freePage, int releasedRow, int releasedPage)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "globalptr-invalid-");
        try
        {
            WritePointer(path, JetFormatBase.FreePagesMapPointerOffset, freeRow, freePage);
            WritePointer(path, JetFormatBase.ReleasedPagesMapPointerOffset, releasedRow, releasedPage);

            var error = Assert.Throws<InvalidDataException>(() => JetDatabase.Open(path, readOnly: false));
            Assert.Contains("global", error.Message, StringComparison.OrdinalIgnoreCase);

            // A read-only open never allocates, and reads the file as ACE does.
            using var readOnly = JetDatabase.Open(path, readOnly: true);
            Assert.NotNull(readOnly.Catalog.FindTable("Customers"));
            _ = name;
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // ---------------------------------------------------------------- helpers

    private static void WritePointer(string path, int offset, int row, int page)
    {
        ReadOnlySpan<byte> mask = JetFormatBase.PageZeroHeaderMask;
        byte[] value = BitConverter.GetBytes((uint)(page << 8 | row));
        for (int i = 0; i < 4; i++) value[i] ^= mask[offset - JetFormatBase.PageZeroHeaderMaskStart + i];
        using var s = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        s.Position = offset;
        s.Write(value);
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

    private static void AppendPage(string path, byte[] bytes)
    {
        using var s = new FileStream(path, FileMode.Append, FileAccess.Write);
        s.Write(bytes);
    }

    /// <summary>The bitmap byte and bit for <paramref name="page"/> in the inline map at <paramref name="row"/>
    /// of a usage-map holder page.</summary>
    private static (int Byte, int Bit) Locate(byte[] holder, int row, int page)
    {
        int offset = BinaryPrimitives.ReadUInt16LittleEndian(holder.AsSpan(14 + row * 2)) & 0x1FFF;
        int end = row == 0 ? 4096 : BinaryPrimitives.ReadUInt16LittleEndian(holder.AsSpan(14 + (row - 1) * 2)) & 0x1FFF;
        Assert.Equal(0x00, holder[offset]);   // inline
        int bit = page - BinaryPrimitives.ReadInt32LittleEndian(holder.AsSpan(offset + 1));
        Assert.InRange(bit, 0, (end - offset - 5) * 8 - 1);
        return (offset + 5 + bit / 8, bit % 8);
    }

    private static void SetMapBit(byte[] holder, int row, int page, bool set)
    {
        (int b, int bit) = Locate(holder, row, page);
        if (set) holder[b] |= (byte)(1 << bit);
        else holder[b] &= (byte)~(1 << bit);
    }

    private static bool MapBit(byte[] holder, int row, int page)
    {
        (int b, int bit) = Locate(holder, row, page);
        return (holder[b] & (1 << bit)) != 0;
    }
}
