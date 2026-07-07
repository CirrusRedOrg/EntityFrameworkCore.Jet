using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

// Byte-faithful: after LibRed widens a text column (ALTER COLUMN path, via AlterColumn), ACE reads the new max
// length and enforces it — a value that fits the new max is accepted, one past it is rejected.
public class AceAlterColumnTests
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

    [Fact]
    public void Access_reads_and_enforces_a_libred_widened_text_column()
    {
        string path = Path.Combine(Path.GetTempPath(), $"alc-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("T",
                    [new ColumnSpec("K", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("V", JetDataType.Text, 40, IsFixedLength: false)],   // TEXT(20)
                    primaryKey: ["K"]);
                db.AlterColumn("T", "V", new ColumnSpec("V", JetDataType.Text, 80, IsFixedLength: false)); // → TEXT(40)
            }

            using var conn = OpenOleDb(path);
            // 40 chars fits the new max
            using (var c = conn.CreateCommand()) { c.CommandText = $"INSERT INTO T (K, V) VALUES (1, '{new string('a', 40)}')"; c.ExecuteNonQuery(); }
            // 41 chars exceeds it → ACE rejects
            using var bad = conn.CreateCommand();
            bad.CommandText = $"INSERT INTO T (K, V) VALUES (2, '{new string('b', 41)}')";
            Assert.ThrowsAny<OleDbException>(() => bad.ExecuteNonQuery());

            string? v; using (var c = conn.CreateCommand()) { c.CommandText = "SELECT V FROM T WHERE K = 1"; v = (string?)c.ExecuteScalar(); }
            Assert.Equal(40, v!.Length);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
