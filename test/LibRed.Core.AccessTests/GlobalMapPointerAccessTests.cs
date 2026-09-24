using System.Buffers.Binary;
using System.Data.OleDb;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// LibRed allocates through page 0's global map pointers (0x18 free, 0x1C released), as ACE does. Two files ACE
// itself reads correctly — maps moved off page 1, and pages sitting in the released map — must stay files ACE
// reads correctly after LibRed has allocated into them.
[Collection(AceCollection.Name)]
public class GlobalMapPointerAccessTests : TempDatabaseTest
{
    private const int NorthwindPages = 353;

    [Fact]
    public void Ace_reads_a_table_libred_filled_through_maps_moved_off_page_one()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "globalptr-ace-moved-");
        byte[] page1 = ReadPage(path, 1);
        byte[] moved = (byte[])page1.Clone();
        SetMapBit(moved, row: 0, page: NorthwindPages, set: false);
        AppendPage(path, moved);
        WritePointer(path, JetFormatBase.FreePagesMapPointerOffset, row: 0, page: NorthwindPages);
        WritePointer(path, JetFormatBase.ReleasedPagesMapPointerOffset, row: 1, page: NorthwindPages);

        FillWithLibRed(path, rows: 1500);

        Assert.Equal(page1, ReadPage(path, 1));   // LibRed never touched the stale maps
        AssertAceReads(path, rows: 1500);
    }

    [Fact]
    public void Ace_reads_a_table_libred_filled_around_released_pages()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "globalptr-ace-released-");
        byte[] page1 = ReadPage(path, 1);
        foreach (int p in new[] { 310, 329, NorthwindPages, NorthwindPages + 1 })
            SetMapBit(page1, row: 1, page: p, set: true);
        WritePage(path, 1, page1);

        FillWithLibRed(path, rows: 1500);

        // Never allocated while open; the close merged them into the free map and cleared the released map.
        byte[] after = ReadPage(path, 1);
        foreach (int p in new[] { 310, 329, NorthwindPages, NorthwindPages + 1 })
        {
            Assert.False(MapBit(after, 1, p), $"page {p} should no longer be released");
            Assert.True(MapBit(after, 0, p), $"page {p} should be free");
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

        int pages = (int)(new FileInfo(path).Length / 4096);
        byte[] holder = ReadPage(path, 1);
        int offset = BinaryPrimitives.ReadUInt16LittleEndian(holder.AsSpan(14)) & 0x1FFF;
        int length = 4096 - offset; // row 0 is the last record on the page, so its record runs to the page end
        int start = BinaryPrimitives.ReadInt32LittleEndian(holder.AsSpan(offset + 1));

        // Then free pages above the window: shortening a memo releases its long-value pages at once.
        using (OleDbConnection connection = AceTestDatabase.Open(path))
        using (OleDbCommand update = connection.CreateCommand())
        {
            update.CommandText = "UPDATE Grown SET M = 'short' WHERE K < 100";
            update.ExecuteNonQuery();
        }

        byte[] after = ReadPage(path, 1);
        int freeAbove512 = 0;
        for (int p = 512; p < pages; p++) if (MapBit(after, 0, p)) freeAbove512++;

        // Measured: 700 memo rows made a 1,761-page file, whose free map is a 229-byte inline record starting at
        // page 0 — coverage 1,792 pages — and the UPDATE marked 43 pages free above 511. So ACE's map covers
        // every page it might have to free, which is why a page outside the map's coverage is a malformed file
        // rather than an ordinary state (PageAllocator.Free).
        Assert.True(pages > 512, $"expected the file to pass the 512-page window; it is {pages} pages");
        Assert.Equal(0, start);
        Assert.True(length >= 5 + (pages + 7) / 8,
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

        byte[] before = ReadPage(path, 1);
        byte[] duringDrop;
        using (OleDbConnection connection = AceTestDatabase.Open(path))
        {
            using (OleDbCommand drop = connection.CreateCommand())
            {
                drop.CommandText = "DROP TABLE Doomed";
                drop.ExecuteNonQuery();
            }
            duringDrop = ReadPage(path, 1); // the session is still open
        }
        byte[] after = ReadPage(path, 1);

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

    private static (int Byte, int Bit) Locate(byte[] holder, int row, int page)
    {
        int offset = BinaryPrimitives.ReadUInt16LittleEndian(holder.AsSpan(14 + row * 2)) & 0x1FFF;
        int bit = page - BinaryPrimitives.ReadInt32LittleEndian(holder.AsSpan(offset + 1));
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
