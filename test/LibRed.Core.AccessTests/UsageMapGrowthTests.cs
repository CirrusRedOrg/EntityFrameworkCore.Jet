using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// A table that grows past the inline usage-map window (page 512): Access enlarges the owned/free bitmap
/// record in place (256-bit chunks) rather than switching to a reference map. LibRed does the same, so
/// large tables keep working and Access still reads them.
/// </summary>
[Collection(AceCollection.Name)]
public class UsageMapGrowthTests
{
    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    [Fact]
    public void Table_growing_past_the_inline_window_round_trips_through_libred_and_access()
    {
        const int rows = 400; // ~1 row/page on top of Northwind → owned pages cross page 512
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "umgrow-");
        string big = new('x', 255);
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var cols = new List<ColumnSpec> { new("Id", JetDataType.Int32, 4, IsFixedLength: true, IsAutoNumber: true) };
                for (int c = 0; c < 7; c++) cols.Add(new ColumnSpec($"C{c}", JetDataType.Text, 255 * 2, IsFixedLength: false));
                db.CreateTable("Big", cols, primaryKey: ["Id"]);

                var table = db.OpenTable("Big");
                for (int i = 0; i < rows; i++)
                    table.Insert([null, big, big, big, big, big, big, big]);
            }

            // LibRed reads every owned page back, past the old 512 window, and the owned map really grew.
            using (var db = JetDatabase.Open(path))
            {
                var table = db.OpenTable("Big");
                var maps = new UsageMap(table.Channel, table.Definition);
                var pages = maps.DataPages().ToList();
                int window = db.Format.UsageMapInlineBitmapSize * 8;
                Assert.True(pages.Max() >= window, $"expected owned pages past {window - 1}, max={pages.Max()}");

                (int mapRow, int mapPage) = table.Channel.ReadPage(table.Definition.DefinitionPage)
                    .ReadRecordPointer(db.Format.TdefOwnedPagesOffset);
                int recLen = maps.ReadRecordAt(mapRow, mapPage).Length;
                Assert.True(recLen > db.Format.UsageMapInlineRecordSize,
                    $"owned map should have grown past the full-width record, recLen={recLen}");
                // The grown bitmap must cover the highest owned page.
                Assert.True((recLen - db.Format.UsageMapInlineHeaderSize) * 8 > pages.Max());
            }

            // Access opens the file, counts every row, and reads one that lives past page 512.
            using (var conn = OpenOleDb(path))
            {
                using (var c = conn.CreateCommand())
                { c.CommandText = "SELECT COUNT(*) FROM Big"; Assert.Equal(rows, Convert.ToInt32(c.ExecuteScalar())); }
                using (var c = conn.CreateCommand())
                { c.CommandText = $"SELECT C0 FROM Big WHERE Id = {rows}"; Assert.Equal(big, c.ExecuteScalar()); }
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}