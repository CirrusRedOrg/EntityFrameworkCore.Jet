using System.Buffers.Binary;
using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.Pages;
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
public class WideTableUsageMapTests
{
    private const int Columns = 255;

    /// <summary>Enough full-page rows to push the owned map past what an inline record can hold. The
    /// owned bitmap reaches ~3,800 bytes at 30,000 pages and no longer fits soon after.</summary>
    private const int Rows = 32_000;

    private static OleDbConnection OpenOleDb(string path)
    {
        foreach (string provider in new[] { "Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0" })
        {
            try
            {
                var connection = new OleDbConnection($"Provider={provider};Data Source={path};OLE DB Services=-4;");
                connection.Open();
                return connection;
            }
            catch (Exception ex) when (ex is OleDbException or InvalidOperationException) { }
        }
        throw new InvalidOperationException("No Microsoft.ACE.OLEDB provider available.");
    }

    /// <summary>The type, record length and (inline only) start page of a table's usage map.</summary>
    private static (byte Type, int Length, int StartPage) ReadMap(Table table, JetFormatBase format, int tdefPointerOffset)
    {
        var tdef = table.Channel.ReadPage(table.Definition.DefinitionPage);
        var holder = new DataPage();
        holder.Read(table.Channel.ReadPage(tdef.ReadInt24(tdefPointerOffset + 1)), format);
        byte[] record = holder.GetRow(tdef.ReadByte(tdefPointerOffset)).ToArray();

        int startPage = record[0] == 0x00 ? BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(1, 4)) : -1;
        return (record[0], record.Length, startPage);
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
        string path = Path.Combine(Path.GetTempPath(), $"freewin-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            db.CreateTable("W", FullPageRowColumns(), primaryKey: ["Id"]);
            var table = db.OpenTable("W");

            Fill(table, 2_000); // one row per page, so the tail is ~2,300 pages in

            (byte type, int length, int startPage) = ReadMap(table, db.Format, db.Format.TdefFreePagesOffset);
            Assert.Equal(0x00, type);
            Assert.Equal(69, length);              // 5-byte header + a fixed 64-byte bitmap, never grown
            Assert.Equal(0, startPage % 512);      // window aligned to a 512-page boundary

            // The window covers the tail: exactly one page has room, and it is the highest owned page.
            var usage = new UsageMap(table.Channel, table.Definition);
            int tail = Assert.Single(usage.FreeDataPages());
            Assert.Equal(usage.MaxDataPage(), tail);
            Assert.InRange(tail, startPage, startPage + 511);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void A_255_column_table_spanning_a_reference_usage_map_round_trips_through_access()
    {
        string path = Path.Combine(Path.GetTempPath(), $"wide255-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("Wide255", FullPageRowColumns(), primaryKey: ["Id"]);
                var table = db.OpenTable("Wide255");

                Assert.Equal(0x00, ReadMap(table, db.Format, db.Format.TdefOwnedPagesOffset).Type); // starts inline
                Fill(table, Rows);
            }

            using (var db = JetDatabase.Open(path))
            {
                var table = db.OpenTable("Wide255");

                Assert.Equal(0x01, ReadMap(table, db.Format, db.Format.TdefOwnedPagesOffset).Type); // grew past inline

                // The free map never grows, which is precisely what leaves the owned map room to reach
                // ~3,800 bytes before converting.
                (byte freeType, int freeLength, _) = ReadMap(table, db.Format, db.Format.TdefFreePagesOffset);
                Assert.Equal(0x00, freeType);
                Assert.Equal(69, freeLength);

                Assert.Equal(Rows, table.Rows().Count());
            }

            using var connection = OpenOleDb(path);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM Wide255";
            Assert.Equal(Rows, Convert.ToInt32(command.ExecuteScalar()));
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
