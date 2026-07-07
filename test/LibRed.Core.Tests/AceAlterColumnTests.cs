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

    [Fact]
    public void Access_reads_a_libred_full_rewrite_with_converted_values()
    {
        string path = Path.Combine(Path.GetTempPath(), $"rw-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            // ACE creates + populates a LONG column.
            using (var conn = OpenOleDb(path))
            {
                void Exec(string s) { using var c = conn.CreateCommand(); c.CommandText = s; c.ExecuteNonQuery(); }
                Exec("CREATE TABLE T ( K LONG PRIMARY KEY, N LONG )");
                Exec("INSERT INTO T (K, N) VALUES (1, 42)");
                Exec("INSERT INTO T (K, N) VALUES (2, 7)");
            }

            // LibRed rewrites N: LONG -> DOUBLE (full column rewrite, converting values).
            using (var db = JetDatabase.Open(path, readOnly: false))
                db.AlterColumn("T", "N", new ColumnSpec("N", JetDataType.Double, 8, IsFixedLength: true));

            // ACE reads the converted data and treats N as a real Double (a fractional insert round-trips).
            using (var conn = OpenOleDb(path))
            {
                using (var c = conn.CreateCommand()) { c.CommandText = "INSERT INTO T (K, N) VALUES (3, 3.5)"; c.ExecuteNonQuery(); }
                var vals = new List<double>();
                using var q = conn.CreateCommand();
                q.CommandText = "SELECT N FROM T ORDER BY K";
                using var r = q.ExecuteReader();
                while (r.Read()) vals.Add(Convert.ToDouble(r[0]));
                Assert.Equal(new[] { 42.0, 7.0, 3.5 }, vals);
            }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
