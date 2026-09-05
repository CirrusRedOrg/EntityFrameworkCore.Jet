using System.Buffers.Binary;
using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.IO;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// A table that grows past the inline usage-map window (page 512): Access enlarges the owned/free bitmap
/// record in place (256-bit chunks) rather than switching to a reference map. LibRed does the same, so
/// large tables keep working and Access still reads them.
/// </summary>
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
                var pages = new UsageMap(table.Channel, table.Definition).DataPages().ToList();
                Assert.True(pages.Max() > 511, $"expected owned pages past 511, max={pages.Max()}");

                PageBuffer tdef = table.Channel.ReadPage(table.Definition.DefinitionPage);
                int mapRow = tdef.ReadByte(db.Format.TdefOwnedPagesOffset);
                int mapPage = tdef.ReadInt24(db.Format.TdefOwnedPagesOffset + 1);
                var holder = new DataPage();
                holder.Read(table.Channel.ReadPage(mapPage), db.Format);
                int recLen = holder.GetRow(mapRow).Length;
                Assert.True(recLen > 69, $"owned map should have grown past the 64-byte bitmap, recLen={recLen}");
                // The grown bitmap must cover the highest owned page.
                Assert.True((recLen - 5) * 8 > pages.Max());
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
