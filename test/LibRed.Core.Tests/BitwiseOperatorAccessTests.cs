using System.Data.OleDb;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// Access bitwise operators (BAND / BOR / BXOR infix, BNOT prefix) — the same operator syntax runs in both
/// LibRed's engine and ACE and yields the same results.
/// </summary>
public class BitwiseOperatorAccessTests
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

    // ACE evaluates the same bitwise operator syntax, giving the values the LibRed engine tests assert.
    [Fact]
    public void Access_evaluates_bitwise_operators()
    {
        string path = Path.Combine(Path.GetTempPath(), $"bitop-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using var conn = OpenOleDb(path);
            using (var c = conn.CreateCommand()) { c.CommandText = "CREATE TABLE B (Id LONG)"; c.ExecuteNonQuery(); }
            using (var c = conn.CreateCommand()) { c.CommandText = "INSERT INTO B (Id) VALUES (1)"; c.ExecuteNonQuery(); }

            int Ace(string expr)
            {
                using var c = conn.CreateCommand();
                c.CommandText = $"SELECT ({expr}) FROM B";
                return Convert.ToInt32(c.ExecuteScalar());
            }

            Assert.Equal(2, Ace("6 BAND 3"));
            Assert.Equal(7, Ace("6 BOR 3"));
            Assert.Equal(5, Ace("6 BXOR 3"));
            Assert.Equal(-6, Ace("BNOT 5"));
            Assert.Equal(10, Ace("6 BAND 3 BOR 8"));
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
