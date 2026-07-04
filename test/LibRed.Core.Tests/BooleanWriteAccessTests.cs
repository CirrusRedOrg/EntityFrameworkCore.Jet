using System.Data.OleDb;
using LibRed;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// A native BIT (YesNo) column written by LibRed. The boolean value is stored in the row's null-bitmap bit
/// (set = true); LibRed coerces the inserted value (1/-1/0/TRUE/FALSE) with Access truthiness. Access reads
/// the bits back correctly and a bare-boolean predicate returns the right rows.
/// </summary>
public class BooleanWriteAccessTests
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
    public void Access_reads_libred_written_bit_values()
    {
        string path = Path.Combine(Path.GetTempPath(), $"bit-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using (var conn = OpenOleDb(path))
            using (var c = conn.CreateCommand())
            { c.CommandText = "CREATE TABLE Bits (Id LONG, Flag BIT NOT NULL)"; c.ExecuteNonQuery(); }

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var t = db.OpenTable("Bits");
                t.Insert([1, 1]);      // integer 1 → true
                t.Insert([2, 0]);      // integer 0 → false
                t.Insert([3, true]);   // bool true
                t.Insert([4, false]);  // bool false
                t.Insert([5, -1]);     // -1 → true
            }

            using var conn2 = OpenOleDb(path);
            using (var c = conn2.CreateCommand())
            {
                c.CommandText = "SELECT COUNT(*) FROM Bits WHERE Flag = TRUE";
                Assert.Equal(3, Convert.ToInt32(c.ExecuteScalar())); // ids 1, 3, 5
            }
            using (var c = conn2.CreateCommand())
            {
                c.CommandText = "SELECT Flag FROM Bits WHERE Id = 1";
                Assert.Equal(true, c.ExecuteScalar());
            }
            using (var c = conn2.CreateCommand())
            {
                c.CommandText = "SELECT Flag FROM Bits WHERE Id = 2";
                Assert.Equal(false, c.ExecuteScalar());
            }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
