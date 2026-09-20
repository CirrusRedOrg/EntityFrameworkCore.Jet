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
