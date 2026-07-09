using System.Data.OleDb;
using LibRed;
using Xunit;

namespace LibRed.Core.Tests;

// KB 884185 ground truth: after an explicit INSERT of a LOWER value into an AutoNumber column, Access sets the
// 0x14 high-water to the *last inserted value* (not the max), so the next auto id re-derives from it and
// collides with an existing row — the "duplicate values in the index/primary key" error. LibRed diverges (it
// advances 0x14 monotonically and is immune — see AutoNumberSeedImmunityTests in LibRed.Engine.Tests).
public class AutoNumberSeedTests
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

    private static int HighWater(JetDatabase db, string table)
    {
        var col = db.Catalog.FindTable(table)!.Columns.First(c => c.IsAutoNumber);
        return col.Seed - col.Increment;   // Seed = lastAuto (0x14) + increment
    }

    [Fact]
    public void Ace_seeds_the_high_water_from_the_last_inserted_value()
    {
        string path = Path.Combine(Path.GetTempPath(), $"anb-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using (var conn = OpenOleDb(path))
            {
                void Exec(string sql) { using var c = conn.CreateCommand(); c.CommandText = sql; c.ExecuteNonQuery(); }
                Exec("CREATE TABLE Table1 (Field1 COUNTER CONSTRAINT PK_T1 PRIMARY KEY, Field2 TEXT(10))");
                for (char ch = 'A'; ch <= 'F'; ch++) Exec($"INSERT INTO Table1 (Field2) VALUES ('{ch}')");   // Field1 → 1..6
                Exec("DELETE FROM Table1 WHERE Field1 = 3");
                Exec("INSERT INTO Table1 (Field1, Field2) VALUES (3, 'C')");   // explicit lower value
            }

            using (var db = JetDatabase.Open(path, readOnly: true))
                Assert.Equal(3, HighWater(db, "Table1"));   // ACE took the last-inserted value, not the max (6)

            // The next auto insert re-derives 3+1=4 → collides with the existing row 4.
            using var conn2 = OpenOleDb(path);
            using var bad = conn2.CreateCommand();
            bad.CommandText = "INSERT INTO Table1 (Field2) VALUES ('G')";
            Assert.ThrowsAny<OleDbException>(() => bad.ExecuteNonQuery());
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
