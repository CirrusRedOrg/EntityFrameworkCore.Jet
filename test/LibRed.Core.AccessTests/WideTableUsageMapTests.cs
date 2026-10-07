using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// A table at Jet's limits: the full 255 columns, and enough data pages that its owned-pages usage map
/// outgrows the inline (0x00) bitmap and must become a reference (0x01) map pointing at dedicated
/// bitmap pages. Each row here fills a whole 4 KB page, so one row is one page.
/// </summary>
/// <remarks>
/// This pins three things a wide, large table needs and nothing else exercises:
/// <list type="bullet">
/// <item>a 255-column definition spans TDEF continuation pages, so repointing an index's B-tree root
/// after a root split has to address the stitched definition, not just its first page;</item>
/// <item>the free-pages map slides a fixed 512-page window instead of growing, so it stays 69 bytes
/// and leaves the usage-map page's room to the owned map (as Access does);</item>
/// <item>the reference-type usage map write path, including the inline→reference conversion.</item>
/// </list>
/// Access must be able to read the result — that is what makes the layout byte-faithful rather than
/// merely self-consistent.
/// </remarks>
[Collection(AceCollection.Name)]
public class WideTableUsageMapTests
{
    private const int Columns = 255;

    /// <summary>Enough full-page rows to push the owned map past what an inline record can hold. The
    /// owned bitmap reaches ~3,800 bytes at 30,000 pages and no longer fits soon after.</summary>
    private const int Rows = 32_000;

    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    /// <summary>The type, record length and (inline only) start page of a table's usage map.</summary>
    private static (UsageMapType Type, int Length, int StartPage) ReadMap(Table table, JetFormatBase format, int tdefPointerOffset)
    {
        var tdef = table.Channel.ReadPage(table.Definition.DefinitionPage);
        (int mapRow, int mapPage) = tdef.ReadRecordPointer(tdefPointerOffset);
        ReadOnlySpan<byte> record = new UsageMap(table.Channel, table.Definition).ReadRecordAt(mapRow, mapPage);

        UsageMapType type = UsageMap.RecordType(record);
        int startPage = type == UsageMapType.Inline ? UsageMap.StartPage(record, format) : -1;
        return (type, record.Length, startPage);
    }

    private static List<ColumnSpec> FullPageRowColumns()
    {
        // 1 LONG primary key + 254 CURRENCY = a ~2,070-byte fixed row, so one row per 4 KB page.
        var columns = new List<ColumnSpec> { new("Id", JetDataType.Int32, 4, IsFixedLength: true) };
        for (int i = 1; i < Columns; i++)
            columns.Add(new ColumnSpec($"c{i}", JetDataType.Currency, 8, IsFixedLength: true));
        return columns;
    }

    private static void Fill(Table table, int rows)
    {
        var row = new object?[Columns];
        for (int r = 0; r < rows; r++)
        {
            row[0] = r;
            for (int i = 1; i < Columns; i++) row[i] = (decimal)i;
            table.Insert(row);
        }
    }

    /// <summary>
    /// The free-pages map tracks only the current append tail, so rather than growing a bitmap out from
    /// page 0 it slides a 64-byte window aligned to a 512-page boundary — verified against ACE, whose free
    /// map for a table whose tail sat on page 852 / 1227 / 1852 started at 512 / 1024 / 1536.
    /// </summary>
    [Fact]
    public void The_free_pages_map_slides_a_512_page_window_instead_of_growing()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "freewin-");
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            db.CreateTable("W", FullPageRowColumns(), primaryKey: ["Id"]);
            var table = db.OpenTable("W");

            Fill(table, 2_000); // one row per page, so the tail is ~2,300 pages in

            (UsageMapType type, int length, int startPage) = ReadMap(table, db.Format, db.Format.TdefFreePagesOffset);
            int window = db.Format.UsageMapInlineBitmapSize * 8;
            Assert.Equal(UsageMapType.Inline, type);
            Assert.Equal(db.Format.UsageMapInlineRecordSize, length); // 5-byte header + a fixed 64-byte bitmap, never grown
            Assert.Equal(0, startPage % window);   // window aligned to a 512-page boundary

            // The window covers the tail: exactly one page has room, and it is the highest owned page.
            var usage = new UsageMap(table.Channel, table.Definition);
            int tail = Assert.Single(usage.FreeDataPages());
            Assert.Equal(usage.MaxDataPage(), tail);
            Assert.InRange(tail, startPage, startPage + window - 1);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>
    /// An owned-pages map keeps <c>startPage = 0</c> and grows its bitmap in 4-byte (32-bit) steps, so its
    /// record length is exactly <c>5 + roundUp(ceil((maxPage + 1) / 8), 4)</c>. Verified against ACE record
    /// lengths for the same table shape: 8,000 rows → 1053, 12,000 → 1553, 30,000 → 3801, 31,000 → 3925.
    /// Overshooting (we once rounded to 32 bytes) wastes the usage-map page's room and converts the map to
    /// reference type earlier than Access would.
    /// </summary>
    [Fact]
    public void The_owned_pages_bitmap_grows_in_4_byte_steps()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "grow4-");
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            db.CreateTable("W", FullPageRowColumns(), primaryKey: ["Id"]);
            var table = db.OpenTable("W");

            Fill(table, 2_000);

            (UsageMapType type, int length, int startPage) = ReadMap(table, db.Format, db.Format.TdefOwnedPagesOffset);
            Assert.Equal(UsageMapType.Inline, type);
            Assert.Equal(0, startPage); // an owned map never moves: it must retain every page ever taken

            int maxPage = new UsageMap(table.Channel, table.Definition).MaxDataPage();
            int bitmapBytes = BitmapBits.ByteCount(maxPage + 1);
            int growth = db.Format.UsageMapInlineGrowthSize;
            int expected = db.Format.UsageMapInlineHeaderSize + (bitmapBytes + growth - 1) / growth * growth;
            Assert.Equal(expected, length);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void A_255_column_table_spanning_a_reference_usage_map_round_trips_through_access()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "wide255-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("Wide255", FullPageRowColumns(), primaryKey: ["Id"]);
                var table = db.OpenTable("Wide255");

                Assert.Equal(UsageMapType.Inline, ReadMap(table, db.Format, db.Format.TdefOwnedPagesOffset).Type); // starts inline
                Fill(table, Rows);
            }

            using (var db = JetDatabase.Open(path))
            {
                var table = db.OpenTable("Wide255");

                Assert.Equal(UsageMapType.Reference, ReadMap(table, db.Format, db.Format.TdefOwnedPagesOffset).Type); // grew past inline

                // The free map never grows, which is precisely what leaves the owned map room to reach
                // ~3,800 bytes before converting.
                (UsageMapType freeType, int freeLength, _) = ReadMap(table, db.Format, db.Format.TdefFreePagesOffset);
                Assert.Equal(UsageMapType.Inline, freeType);
                Assert.Equal(db.Format.UsageMapInlineRecordSize, freeLength);

                Assert.Equal(Rows, table.Rows().Count());
            }

            using var connection = OpenOleDb(path);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM Wide255";
            Assert.Equal(Rows, Convert.ToInt32(command.ExecuteScalar()));
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}