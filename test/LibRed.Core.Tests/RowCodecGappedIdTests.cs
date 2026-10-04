using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// The row's leading count and null-bitmap width span every column id the table has handed out, NOT the live
// column count — they diverge once ids have a gap (a burned type-change id, or a DROP COLUMN gap). A dead
// id's own bit is CLEAR in a row written by an insert; only the ALTER COLUMN re-lay carries the old bit
// forward. Both measured against ACE (spec §5, RowByteParityAccessTests). These exercise the gapped-id case
// directly, which no contiguous-id table can. A standalone encoder has no TDEF, so the count here is the
// highest live id + 1; an insert passes the table's 0x29 high-water, which also outlives a dropped column.
public class RowCodecGappedIdTests
{
    private static List<ColumnDef> Int32Cols(params (string Name, int Id, int FixedOffset)[] cols) =>
        cols.Select((c, i) => new ColumnDef
        {
            Name = c.Name, Type = JetDataType.Int32, Index = i, ColumnId = c.Id,
            Length = 4, FixedOffset = c.FixedOffset, IsFixedLength = true,
        }).ToList();

    [Fact]
    public void Count_and_null_bitmap_span_the_dead_ids_whose_bits_stay_clear()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb, readOnly: true);

        // Three live columns, id 1 is a DEAD gap (as a burned type-change would leave: ids 0, 2, 3).
        var cols = Int32Cols(("A", 0, 0), ("B", 2, 4), ("C", 3, 8));
        byte[] row = new RowCodec(cols, db.Format).Encode([10, 20, 30]);

        // Leading count = max id + 1 = 4 (NOT the live count 3).
        Assert.Equal(4, RowCodec.Layout.ReadColumnCount(row, db.Format));
        // 1-byte null bitmap: live ids 0,2,3 present, the dead id 1 clear → 0x0D.
        Assert.Equal(0x0D, row[^1]);
        // Round-trips (the decoder sizes the bitmap from the stored count, not the live count).
        Assert.Equal(new object?[] { 10, 20, 30 }, new RowCodec(cols, db.Format).Decode(row));
    }

    [Fact]
    public void A_null_column_past_the_live_count_still_reads_null()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb, readOnly: true);

        // Two live columns with ids 0 and 3 (ids 1,2 dead); B (id 3) is null — its bit is beyond the live
        // count of 2, which is exactly the case that read back null before the fix.
        var cols = Int32Cols(("A", 0, 0), ("B", 3, 4));
        byte[] row = new RowCodec(cols, db.Format).Encode([7, null]);

        Assert.Equal(4, RowCodec.Layout.ReadColumnCount(row, db.Format));          // count spans id 3
        Assert.Equal(0x01, row[^1]);                                         // A(0) present; dead 1,2 and null B(3) clear
        Assert.Equal(new object?[] { 7, null }, new RowCodec(cols, db.Format).Decode(row));
    }
}