using System.Buffers.Binary;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// The row's leading count and null-bitmap width are (max column id + 1), NOT the live column count — they
// diverge once ids have a gap (a burned type-change id, or a DROP COLUMN gap). Verified vs ACE (spec §5,
// AceModifyByteDiffProbe). These exercise that gapped-id case directly, which no contiguous-id table can.
public class RowCodecGappedIdTests
{
    private static List<ColumnDef> Int32Cols(params (string Name, int Id, int FixedOffset)[] cols) =>
        cols.Select((c, i) => new ColumnDef
        {
            Name = c.Name, Type = JetDataType.Int32, Index = i, ColumnId = c.Id,
            Length = 4, FixedOffset = c.FixedOffset, IsFixedLength = true,
        }).ToList();

    [Fact]
    public void Count_and_null_bitmap_use_max_id_plus_one_with_dead_id_bits_set()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb, readOnly: true);

        // Three live columns, id 1 is a DEAD gap (as a burned type-change would leave: ids 0, 2, 3).
        var cols = Int32Cols(("A", 0, 0), ("B", 2, 4), ("C", 3, 8));
        byte[] row = new RowEncoder(cols, db.Format).Encode([10, 20, 30]);

        // Leading count = max id + 1 = 4 (NOT the live count 3).
        Assert.Equal(4, BinaryPrimitives.ReadUInt16LittleEndian(row));
        // 1-byte null bitmap: live ids 0,2,3 present AND the dead id 1 present too → 0x0F.
        Assert.Equal(0x0F, row[^1]);
        // Round-trips (the decoder sizes the bitmap from the stored count, not the live count).
        Assert.Equal(new object?[] { 10, 20, 30 }, new RowDecoder(cols, db.Format).Decode(row));
    }

    [Fact]
    public void A_null_column_past_the_live_count_still_reads_null()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb, readOnly: true);

        // Two live columns with ids 0 and 3 (ids 1,2 dead); B (id 3) is null — its bit is beyond the live
        // count of 2, which is exactly the case that read back null before the fix.
        var cols = Int32Cols(("A", 0, 0), ("B", 3, 4));
        byte[] row = new RowEncoder(cols, db.Format).Encode([7, null]);

        Assert.Equal(4, BinaryPrimitives.ReadUInt16LittleEndian(row));       // count = max id + 1
        Assert.Equal(0x07, row[^1]);                                         // A(0)+dead1+dead2 present, B(3) null
        Assert.Equal(new object?[] { 7, null }, new RowDecoder(cols, db.Format).Decode(row));
    }
}
