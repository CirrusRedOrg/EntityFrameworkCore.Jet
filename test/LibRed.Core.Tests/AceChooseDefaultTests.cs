using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

// Byte-faithful: a LibRed-written Choose()/CBool(Choose()) default is read and applied by ACE. Confirms LibRed's
// Choose support matches ACE (which has the VBA Choose function), incl. the nested CBool(Choose(...)) form that
// ACE's OLE DB DDL parser rejects at CREATE but its expression service applies at insert.
public class AceChooseDefaultTests
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
    [InlineData("Choose(1, 0, 1, 2)", false)]        // 1st choice = 0 = False
    [InlineData("Choose(2, 0, 1, 2)", true)]         // 2nd choice = 1 = True
    [InlineData("CBool(Choose(1, 0, 1, 2))", false)] // nested — DDL-parser-rejected, expression-service-applied
    public void Access_reads_and_applies_a_libred_written_choose_default(string def, bool expected)
    {
        string path = Path.Combine(Path.GetTempPath(), $"ch-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                db.CreateTable("T",
                    [new ColumnSpec("K", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("V", JetDataType.Boolean, 1, IsFixedLength: true)],
                    primaryKey: ["K"], columnDefaults: [("V", def)]);

            using var conn = OpenOleDb(path);
            using (var c = conn.CreateCommand()) { c.CommandText = "INSERT INTO T (K) VALUES (1)"; c.ExecuteNonQuery(); }
            object? v; using (var c = conn.CreateCommand()) { c.CommandText = "SELECT V FROM T"; v = c.ExecuteScalar(); }

            Assert.Equal(expected, Convert.ToBoolean(v));
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
