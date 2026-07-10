using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Pages;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// A table at Jet's limits: the full 255 columns, and enough data pages that its owned-pages usage map
/// outgrows the inline (0x00) bitmap and must become a reference (0x01) map pointing at dedicated
/// bitmap pages. Each row here fills a whole 4 KB page, so ~16,000 rows cross the threshold.
/// </summary>
/// <remarks>
/// This pins two things a wide, large table needs and nothing else exercises:
/// <list type="bullet">
/// <item>a 255-column definition spans TDEF continuation pages, so repointing an index's B-tree root
/// after a root split has to address the stitched definition, not just its first page;</item>
/// <item>the reference-type usage map write path, including the inline→reference conversion.</item>
/// </list>
/// Access must be able to read the result — that is what makes the layout byte-faithful rather than
/// merely self-consistent.
/// </remarks>
public class WideTableUsageMapTests
{
    private const int Columns = 255;
    private const int Rows = 16_000;

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

    /// <summary>The type byte of the table's owned-pages usage-map record.</summary>
    private static byte OwnedMapType(JetDatabase db, TableDef table)
    {
        var tdef = db.OpenTable(table.Name).Channel.ReadPage(table.DefinitionPage);
        var holder = new DataPage();
        holder.Read(db.OpenTable(table.Name).Channel.ReadPage(tdef.ReadInt24(db.Format.TdefOwnedPagesOffset + 1)), db.Format);
        return holder.GetRow(tdef.ReadByte(db.Format.TdefOwnedPagesOffset))[0];
    }

    [Fact]
    public void A_255_column_table_spanning_a_reference_usage_map_round_trips_through_access()
    {
        string path = Path.Combine(Path.GetTempPath(), $"wide255-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            // 1 LONG primary key + 254 CURRENCY = a ~2,070-byte fixed row, so one row per 4 KB page.
            var columns = new List<ColumnSpec> { new("Id", JetDataType.Int32, 4, IsFixedLength: true) };
            for (int i = 1; i < Columns; i++)
                columns.Add(new ColumnSpec($"c{i}", JetDataType.Currency, 8, IsFixedLength: true));

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("Wide255", columns, primaryKey: ["Id"]);
                var table = db.OpenTable("Wide255");

                Assert.Equal(0x00, OwnedMapType(db, table.Definition)); // starts inline

                var row = new object?[Columns];
                for (int r = 0; r < Rows; r++)
                {
                    row[0] = r;
                    for (int i = 1; i < Columns; i++) row[i] = (decimal)i;
                    table.Insert(row);
                }
            }

            using (var db = JetDatabase.Open(path))
            {
                var table = db.OpenTable("Wide255");
                Assert.Equal(0x01, OwnedMapType(db, table.Definition)); // grew past inline
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
