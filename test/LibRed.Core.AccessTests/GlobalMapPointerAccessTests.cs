using System.Data.OleDb;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// LibRed allocates through page 0's global map pointers (0x18 free, 0x1C released), as ACE does. Two files ACE
// itself reads correctly — maps moved to another page, and pages sitting in the released map — must stay files ACE
// reads correctly after LibRed has allocated into them. Every test finds the maps through those pointers.
[Collection(AceCollection.Name)]
public class GlobalMapPointerAccessTests : TempDatabaseTest
{
    private static readonly JetFormatBase Format = TestDatabases.FormatOf(TestDatabases.NorthwindAccdb);
    private static readonly int NorthwindPages = (int)(new FileInfo(TestDatabases.NorthwindAccdb).Length / Format.PageSize);

    [Fact]
    public void Ace_reads_a_table_libred_filled_through_maps_moved_to_another_page()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "globalptr-ace-moved-");
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

        FillWithLibRed(path, rows: 1500);

        Assert.Equal(original, TestDatabases.ReadPage(path, free.Page));   // LibRed never touched the stale maps
        AssertAceReads(path, rows: 1500);
    }

    [Fact]
    public void Ace_reads_a_table_libred_filled_around_released_pages()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "globalptr-ace-released-");
        (int Row, int Page) free, released;
        using (var db = JetDatabase.Open(path))
            (free, released) = (db.DefinitionPage.FreePagesMap, db.DefinitionPage.ReleasedPagesMap);
        byte[] holder = TestDatabases.ReadPage(path, released.Page);
        foreach (int p in new[] { 310, 329, NorthwindPages, NorthwindPages + 1 })
            TestDatabases.SetMapBit(holder, Format, released.Row, page: p, set: true);
        TestDatabases.WritePage(path, released.Page, holder);

        FillWithLibRed(path, rows: 1500);

        // Never allocated while open; the close merged them into the free map and cleared the released map.
        byte[] freeAfter = TestDatabases.ReadPage(path, free.Page), releasedAfter = TestDatabases.ReadPage(path, released.Page);
        foreach (int p in new[] { 310, 329, NorthwindPages, NorthwindPages + 1 })
        {
            Assert.False(TestDatabases.MapBit(releasedAfter, Format, released.Row, p), $"page {p} should no longer be released");
            Assert.True(TestDatabases.MapBit(freeAfter, Format, free.Row, p), $"page {p} should be free");
        }
        AssertAceReads(path, rows: 1500);
    }

    // Whether the global free-pages map's coverage keeps up with the file, which decides whether a page can
    // ever be freed that the map has no bit for. A 69-byte inline record covers pages 0–511; the file here is
    // pushed well past that with ACE, and then an UPDATE replaces long values — the one free ACE performs
    // immediately rather than holding to close — so their pages have to be recorded somewhere above 511.
    [Fact]
    public void Ace_grows_the_free_map_to_cover_the_file_it_frees_pages_in()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "globalptr-ace-grow-");
        using (OleDbConnection connection = AceTestDatabase.Open(path))
        {
            using (OleDbCommand ddl = connection.CreateCommand())
            {
                ddl.CommandText = "CREATE TABLE Grown (K LONG CONSTRAINT pk PRIMARY KEY, M MEMO)";
                ddl.ExecuteNonQuery();
            }
            using OleDbCommand insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO Grown (K, M) VALUES (?, ?)";
            OleDbParameter k = insert.Parameters.Add("k", OleDbType.Integer);
            OleDbParameter m = insert.Parameters.Add("m", OleDbType.LongVarWChar);
            for (int i = 0; i < 700; i++)
            {
                k.Value = i;
                m.Value = new string((char)('a' + i % 26), 4000);
                insert.ExecuteNonQuery();
            }
        }

        int pages = (int)(new FileInfo(path).Length / Format.PageSize);
        (int Row, int Page) free;
        using (var db = JetDatabase.Open(path))
            free = db.DefinitionPage.FreePagesMap;
        byte[] holder = TestDatabases.ReadPage(path, free.Page);
        var holderPage = new DataPage();
        holderPage.Read(new PageBuffer(holder, free.Page), Format);
        int offset = holderPage.Rows[free.Row].Offset, length = holderPage.Rows[free.Row].Length;
        int start = UsageMap.StartPage(holder.AsSpan(offset), Format);

        // Then free pages above the window: shortening a memo releases its long-value pages at once.
        using (OleDbConnection connection = AceTestDatabase.Open(path))
        using (OleDbCommand update = connection.CreateCommand())
        {
            update.CommandText = "UPDATE Grown SET M = 'short' WHERE K < 100";
            update.ExecuteNonQuery();
        }

        byte[] after = TestDatabases.ReadPage(path, free.Page);
        int window = Format.UsageMapInlineBitmapSize * 8;
        int freeAbove512 = 0;
        for (int p = window; p < pages; p++) if (TestDatabases.MapBit(after, Format, free.Row, p)) freeAbove512++;

        // Measured: 700 memo rows made a 1,761-page file, whose free map is a 229-byte inline record starting at
        // page 0 — coverage 1,792 pages — and the UPDATE marked 43 pages free above 511. So ACE's map covers
        // every page it might have to free, which is why a page outside the map's coverage is a malformed file
        // rather than an ordinary state (PageAllocator.Free).
        Assert.True(pages > window, $"expected the file to pass the 512-page window; it is {pages} pages");
        Assert.Equal(0, start);
        Assert.True(length >= Format.UsageMapInlineHeaderSize + BitmapBits.ByteCount(pages),
            $"free map record is {length} bytes, too short for {pages} pages");
        Assert.True(freeAbove512 > 0, "expected the pages the UPDATE freed to be marked free above page 512");
    }

    // Where a freed page lives between the free and the close. LibRed keeps this handle's released pages in
    // memory and writes them to the global maps at close; the audit asks whether that loses them if the process
    // dies, which is only a fault if ACE puts them on disk sooner.
    [Fact]
    public void Ace_keeps_released_pages_off_disk_until_the_session_closes()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "globalptr-ace-release-");
        using (OleDbConnection connection = AceTestDatabase.Open(path))
        {
            using (OleDbCommand ddl = connection.CreateCommand())
            {
                ddl.CommandText = "CREATE TABLE Doomed (K LONG CONSTRAINT pk PRIMARY KEY, M MEMO)";
                ddl.ExecuteNonQuery();
            }
            using OleDbCommand insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO Doomed (K, M) VALUES (?, ?)";
            OleDbParameter k = insert.Parameters.Add("k", OleDbType.Integer);
            OleDbParameter m = insert.Parameters.Add("m", OleDbType.LongVarWChar);
            for (int i = 0; i < 200; i++)
            {
                k.Value = i;
                m.Value = new string((char)('a' + i % 26), 4000);
                insert.ExecuteNonQuery();
            }
        }

        int holder;
        using (var db = JetDatabase.Open(path))
            holder = db.DefinitionPage.FreePagesMap.Page;
        byte[] before = TestDatabases.ReadPage(path, holder);
        byte[] duringDrop;
        using (OleDbConnection connection = AceTestDatabase.Open(path))
        {
            using (OleDbCommand drop = connection.CreateCommand())
            {
                drop.CommandText = "DROP TABLE Doomed";
                drop.ExecuteNonQuery();
            }
            duringDrop = TestDatabases.ReadPage(path, holder); // the session is still open
        }
        byte[] after = TestDatabases.ReadPage(path, holder);

        Assert.Equal(before, duringDrop); // nothing on disk yet — the pages are the session's own business
        Assert.NotEqual(before, after);   // and the close puts them in the free map
    }

    private static void FillWithLibRed(string path, int rows)
    {
        using var db = JetDatabase.Open(path, readOnly: false);
        db.CreateTable("Filled", [
            new ColumnSpec("K", JetDataType.Int32, 4, IsFixedLength: true),
            new ColumnSpec("V", JetDataType.Text, 510, IsFixedLength: false),   // TEXT(255): the length is in bytes
        ]);
        Table table = db.OpenTable("Filled");
        string value = new('v', 255);
        for (int i = 0; i < rows; i++) table.Insert([i, value]);
    }

    private static void AssertAceReads(string path, int rows)
    {
        using OleDbConnection connection = AceTestDatabase.Open(path);
        using OleDbCommand count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*), SUM(K) FROM Filled";
        using OleDbDataReader reader = count.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal(rows, Convert.ToInt32(reader.GetValue(0)));
        Assert.Equal((long)rows * (rows - 1) / 2, Convert.ToInt64(reader.GetValue(1)));
    }
}