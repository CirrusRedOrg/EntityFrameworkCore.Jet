using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// A table wide enough that its TDEF spans continuation pages (the owned-types / proxy shape). LibRed writes
/// the definition split across pages; Access opens it, reports every column, and round-trips a row.
/// </summary>
public class WideTableAccessTests
{
    private static OleDbConnection OpenOleDb(string path)
    {
        Exception? last = null;
        for (int attempt = 0; attempt < 12; attempt++)
            foreach (string p in new[] { "Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0" })
            {
                try { var c = new OleDbConnection($"Provider={p};Data Source={path};OLE DB Services=-4;"); c.Open(); return c; }
                catch (Exception ex) when (ex is OleDbException or InvalidOperationException) { last = ex; Thread.Sleep(40); }
            }
        throw new InvalidOperationException("no provider", last);
    }

    // A wide table of TEXT columns (inline — no long-value usage maps) whose TDEF spans continuation pages:
    // Access opens it, enumerates every column, and a row round-trips including the far-end column.
    [Fact]
    public void Access_opens_a_wide_multi_page_table()
    {
        const int n = 120;
        string path = Path.Combine(Path.GetTempPath(), $"wide-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            var cols = new List<ColumnSpec> { new("Id", JetDataType.Int32, 4, IsFixedLength: true, IsAutoNumber: true) };
            for (int i = 0; i < n; i++)
                cols.Add(new ColumnSpec($"Col{i}", JetDataType.Text, 50 * 2, IsFixedLength: false));

            using (var db = JetDatabase.Open(path, readOnly: false))
                db.CreateTable("Wide", cols, primaryKey: ["Id"]);

            using var conn = OpenOleDb(path);
            var schema = conn.GetSchema("Columns", [null, null, "Wide", null]);
            Assert.Equal(n + 1, schema.Rows.Count); // Access read the whole multi-page definition

            using (var c = conn.CreateCommand())
            { c.CommandText = $"INSERT INTO Wide (Col0, Col{n - 1}) VALUES ('a', 'z')"; c.ExecuteNonQuery(); }
            using (var c = conn.CreateCommand())
            {
                c.CommandText = $"SELECT Col{n - 1} FROM Wide";
                Assert.Equal("z", c.ExecuteScalar());
            }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
