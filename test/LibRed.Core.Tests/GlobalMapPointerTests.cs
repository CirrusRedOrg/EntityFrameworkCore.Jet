using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// Page 0's 0x18 and 0x1C are [row][page] pointers to the global free-pages and released-pages usage maps. ACE
// follows both wherever they point, never allocates a released page, and a file whose pointers are wrong is
// corrupt to it (docs/format/page-05-usage-maps.md §9.1). Every test here finds the maps through those pointers.
// Northwind's free map has pages 310 and 329 free inside the file and every page past its end free; its released
// map is empty.
public class GlobalMapPointerTests
{
    private static readonly JetFormatBase Format = TestDatabases.FormatOf(TestDatabases.NorthwindAccdb);
    private static readonly int NorthwindPages = (int)(new FileInfo(TestDatabases.NorthwindAccdb).Length / Format.PageSize);

    [Fact]
    public void Page_zero_decodes_the_global_map_pointers()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "globalptr-decode-");
        try
        {
            // The values Northwind stores — both maps on the page ACE created them on.
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
            // Copy the maps' holder to a new page at the end of the file, mark that page itself used in the copy,
            // and aim both pointers at it.
            (int Row, int Page) free, released;
            using (var db = JetDatabase.Open(path))
                (free, released) = (db.DefinitionPage.FreePagesMap, db.DefinitionPage.ReleasedPagesMap);
            Assert.Equal(free.Page, released.Page);   // the copy moves both maps together
            byte[] original = TestDatabases.ReadPage(path, free.Page);
            byte[] moved = (byte[])original.Clone();
            TestDatabases.SetMapBit(moved, Format, free.Row, page: NorthwindPages, set: false);
            TestDatabases.AppendPage(path, moved);
            TestDatabases.WriteMapPointer(path, Format.FreePagesMapPointerOffset, free.Row, NorthwindPages);
            TestDatabases.WriteMapPointer(path, Format.ReleasedPagesMapPointerOffset, released.Row, NorthwindPages);
            // Poison the old holder's stale free map: were it still read, the next allocation would be page 400.
            byte[] stale = (byte[])original.Clone();
            for (int p = 0; p < Format.UsageMapInlineBitmapSize * 8; p++)
                TestDatabases.SetMapBit(stale, Format, free.Row, page: p, set: p == 400);
            TestDatabases.WritePage(path, free.Page, stale);

            using (var channel = PageChannel.Open(path, readOnly: false))
            {
                var allocator = channel.Allocator;
                Assert.Equal(310, allocator.Allocate());
                Assert.Equal(329, allocator.Allocate());
                Assert.Equal(NorthwindPages + 1, allocator.Allocate());   // the copy's own page is used
            }

            Assert.Equal(stale, TestDatabases.ReadPage(path, free.Page));   // never read or written
            byte[] after = TestDatabases.ReadPage(path, NorthwindPages);
            Assert.False(TestDatabases.MapBit(after, Format, free.Row, 310));
            Assert.False(TestDatabases.MapBit(after, Format, free.Row, 329));
            Assert.False(TestDatabases.MapBit(after, Format, free.Row, NorthwindPages + 1));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Allocation_skips_pages_set_in_the_released_map()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "globalptr-released-");
        try
        {
            (int Row, int Page) free, released;
            using (var db = JetDatabase.Open(path))
                (free, released) = (db.DefinitionPage.FreePagesMap, db.DefinitionPage.ReleasedPagesMap);
            byte[] holder = TestDatabases.ReadPage(path, released.Page);
            TestDatabases.SetMapBit(holder, Format, released.Row, page: 310, set: true);
            TestDatabases.SetMapBit(holder, Format, released.Row, page: 329, set: true);
            TestDatabases.WritePage(path, released.Page, holder);

            using (var channel = PageChannel.Open(path, readOnly: false))
            {
                var allocator = channel.Allocator;
                Assert.Equal(NorthwindPages, allocator.Allocate());       // past the two released pages
                Assert.Equal(NorthwindPages + 1, allocator.Allocate());
            }

            byte[] freeAfter = TestDatabases.ReadPage(path, free.Page), releasedAfter = TestDatabases.ReadPage(path, released.Page);
            Assert.True(TestDatabases.MapBit(freeAfter, Format, free.Row, 310));   // still free: released, not taken
            Assert.True(TestDatabases.MapBit(freeAfter, Format, free.Row, 329));
            Assert.True(TestDatabases.MapBit(releasedAfter, Format, released.Row, 310));   // and still released
            Assert.True(TestDatabases.MapBit(releasedAfter, Format, released.Row, 329));
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
            (int Row, int Page) released;
            using (var db = JetDatabase.Open(path))
                released = db.DefinitionPage.ReleasedPagesMap;
            byte[] holder = TestDatabases.ReadPage(path, released.Page);
            foreach (int p in new[] { 310, 329, NorthwindPages, NorthwindPages + 1 })
                TestDatabases.SetMapBit(holder, Format, released.Row, page: p, set: true);
            TestDatabases.WritePage(path, released.Page, holder);

            using (var channel = PageChannel.Open(path, readOnly: false))
            {
                Assert.Equal(NorthwindPages + 2, channel.Allocator.Allocate());
                Assert.Equal(NorthwindPages + 3, channel.PageCount);
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>Each case breaks one pointer and leaves the other as Northwind stores it.</summary>
    public static TheoryData<string, int, int, int, int> InvalidPointers
    {
        get
        {
            int freeRow, freePage, releasedRow, releasedPage, catalogRoot;
            using (var db = JetDatabase.Open(TestDatabases.NorthwindAccdb))
            {
                (freeRow, freePage) = db.DefinitionPage.FreePagesMap;
                (releasedRow, releasedPage) = db.DefinitionPage.ReleasedPagesMap;
                catalogRoot = db.DefinitionPage.CatalogRootPage;
            }
            int missingRow = DataPage.ReadRowCount(TestDatabases.ReadPage(TestDatabases.NorthwindAccdb, releasedPage), Format);

            return new()
            {
                // name, free row, free page, released row, released page
                { "free map past the end of the file", freeRow, NorthwindPages, releasedRow, releasedPage },
                { "released map past the end of the file", freeRow, freePage, releasedRow, NorthwindPages },
                { "both maps on the same record", freeRow, freePage, freeRow, freePage },
                { "free map on a TDEF page", freeRow, catalogRoot, releasedRow, releasedPage },
                { "released map on a row the page does not have", freeRow, freePage, missingRow, releasedPage },
                { "free map on page 0", freeRow, 0, releasedRow, releasedPage },
            };
        }
    }

    [Theory]
    [MemberData(nameof(InvalidPointers))]
    public void A_writable_open_refuses_invalid_global_map_pointers(
        string name, int freeRow, int freePage, int releasedRow, int releasedPage)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "globalptr-invalid-");
        try
        {
            TestDatabases.WriteMapPointer(path, Format.FreePagesMapPointerOffset, freeRow, freePage);
            TestDatabases.WriteMapPointer(path, Format.ReleasedPagesMapPointerOffset, releasedRow, releasedPage);

            var error = Assert.Throws<InvalidDataException>(() => JetDatabase.Open(path, readOnly: false));
            Assert.Contains("global", error.Message, StringComparison.OrdinalIgnoreCase);

            // A read-only open never allocates, and reads the file as ACE does.
            using var readOnly = JetDatabase.Open(path, readOnly: true);
            Assert.NotNull(readOnly.Catalog.FindTable("Customers"));
            _ = name;
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}