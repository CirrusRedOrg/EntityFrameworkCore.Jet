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

    // A table with more memo columns than one usage-map page holds (each memo needs a used + a free map,
    // ~57 records per page). LibRed fills the primary usage-map page, then gives each overflowing column its
    // own page (matching ACE). Access opens it, and a long value in an overflow column round-trips — proving
    // the per-column usage map on its dedicated page is wired up. LibRed reads it back too.
    [Fact]
    public void Access_opens_a_wide_memo_table_and_round_trips_a_long_value()
    {
        const int n = 80; // 160 long-value maps: far more than fit on one usage-map page
        string path = Path.Combine(Path.GetTempPath(), $"widemem-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        string big = new('x', 8000); // forces an LVAL page — exercises the column's used-pages usage map
        try
        {
            var cols = new List<ColumnSpec> { new("Id", JetDataType.Int32, 4, IsFixedLength: true, IsAutoNumber: true) };
            for (int i = 0; i < n; i++)
                cols.Add(new ColumnSpec($"M{i}", JetDataType.Memo, 0, IsFixedLength: false));

            using (var db = JetDatabase.Open(path, readOnly: false))
                db.CreateTable("WideMemo", cols, primaryKey: ["Id"]);

            using (var conn = OpenOleDb(path))
            {
                var schema = conn.GetSchema("Columns", [null, null, "WideMemo", null]);
                Assert.Equal(n + 1, schema.Rows.Count);

                // Write a long value into the last (overflow) memo column, then read it back through Access.
                using (var c = conn.CreateCommand())
                { c.CommandText = $"INSERT INTO WideMemo (M0, M{n - 1}) VALUES ('a', @v)"; c.Parameters.AddWithValue("@v", big); c.ExecuteNonQuery(); }
                using (var c = conn.CreateCommand())
                {
                    c.CommandText = $"SELECT M{n - 1} FROM WideMemo";
                    Assert.Equal(big, c.ExecuteScalar());
                }
            }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
