using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// Byte-faithful check: a LibRed-written COUNTER(seed, increment) is read by Access — it opens the file without
// repair, and continues the AutoNumber sequence from the seed with the custom increment.
public class CounterSeedIncrementAccessTests
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
    [InlineData(1000, 7, new[] { 1000, 1007 })]     // ascending custom counter
    [InlineData(100, -5, new[] { 95, 100 })]        // descending counter (negative int32 increment), sorted asc
    public void Access_continues_a_libred_written_custom_counter(int seed, int increment, int[] expectedSortedIds)
    {
        string path = Path.Combine(Path.GetTempPath(), $"cnt-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                db.CreateTable("C1",
                [
                    new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true, IsAutoNumber: true, Seed: seed, Increment: increment),
                    new ColumnSpec("Name", JetDataType.Text, 20, IsFixedLength: false),
                ]);

            using var conn = OpenOleDb(path);
            void Insert(string n) { using var c = conn.CreateCommand(); c.CommandText = $"INSERT INTO C1 (Name) VALUES ('{n}')"; c.ExecuteNonQuery(); }
            Insert("a");
            Insert("b");

            using var q = conn.CreateCommand();
            q.CommandText = "SELECT Id FROM C1 ORDER BY Id";
            using var r = q.ExecuteReader();
            var ids = new List<int>();
            while (r.Read()) ids.Add(Convert.ToInt32(r[0]));

            // ACE picks up the LibRed-written seed/increment and continues the sequence in its direction.
            Assert.Equal(expectedSortedIds, ids);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
