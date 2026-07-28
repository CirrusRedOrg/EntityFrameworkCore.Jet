using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

// Byte-faithful: a LibRed-written Switch() default is read and applied by ACE (which has the VBA Switch
// function), confirming LibRed's Switch matches ACE.
public class AceSwitchDefaultTests
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

    [Theory]
    [InlineData("Switch(1=1, 10, 1=2, 20)", 10)]
    [InlineData("Switch(1=2, 10, 1=1, 20)", 20)]
    public void Access_reads_and_applies_a_libred_written_switch_default(string def, int expected)
    {
        string path = Path.Combine(Path.GetTempPath(), $"sw-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                db.CreateTable("T",
                    [new ColumnSpec("K", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("V", JetDataType.Int32, 4, IsFixedLength: true)],
                    primaryKey: ["K"], columnDefaults: [("V", def)]);

            using var conn = OpenOleDb(path);
            using (var c = conn.CreateCommand()) { c.CommandText = "INSERT INTO T (K) VALUES (1)"; c.ExecuteNonQuery(); }
            object? v; using (var c = conn.CreateCommand()) { c.CommandText = "SELECT V FROM T"; v = c.ExecuteScalar(); }

            Assert.Equal(expected, Convert.ToInt32(v));
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
