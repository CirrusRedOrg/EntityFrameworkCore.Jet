using System.Data.OleDb;
using LibRed;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// Adding an index / primary key to a table that already has rows (Northwind seeds data, then adds the
/// key). LibRed back-fills the new index B-tree from the existing rows and appends its usage-map row
/// without disturbing the data/other-index maps; Access reads every row and enforces the key.
/// </summary>
public class AddIndexToPopulatedTableAccessTests
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
    public void Access_reads_and_enforces_a_primary_key_added_after_data()
    {
        string path = Path.Combine(Path.GetTempPath(), $"populated-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using (var conn = OpenOleDb(path))
            using (var c = conn.CreateCommand())
            { c.CommandText = "CREATE TABLE Region2 (RegionID LONG, RegionDescription TEXT(50))"; c.ExecuteNonQuery(); }

            // LibRed seeds rows, THEN adds the primary key (the order Northwind uses).
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var t = db.OpenTable("Region2");
                t.Insert([1, "Eastern"]);
                t.Insert([2, "Western"]);
                t.Insert([3, "Northern"]);
                t.Insert([4, "Southern"]);
                db.CreateIndex("Region2", "PK_Region2", [("RegionID", false)], isUnique: true, isPrimary: true);
            }

            // LibRed still reads all rows after the index was added (usage maps intact).
            using (var db = JetDatabase.Open(path))
                Assert.Equal(4, db.OpenTable("Region2").Rows().Count());

            using var conn2 = OpenOleDb(path);
            using (var c = conn2.CreateCommand())
            {
                c.CommandText = "SELECT COUNT(*) FROM Region2";
                Assert.Equal(4, Convert.ToInt32(c.ExecuteScalar()));
            }
            // Access seeks via the new PK.
            using (var c = conn2.CreateCommand())
            {
                c.CommandText = "SELECT RegionDescription FROM Region2 WHERE RegionID = 3";
                Assert.Equal("Northern", c.ExecuteScalar());
            }
            // The PK is enforced: a duplicate id is rejected, a new one is accepted.
            using (var dup = conn2.CreateCommand())
            {
                dup.CommandText = "INSERT INTO Region2 (RegionID, RegionDescription) VALUES (2, 'Dup')";
                Assert.ThrowsAny<Exception>(() => dup.ExecuteNonQuery());
            }
            using (var ok = conn2.CreateCommand())
            {
                ok.CommandText = "INSERT INTO Region2 (RegionID, RegionDescription) VALUES (5, 'Central')";
                ok.ExecuteNonQuery();
            }
            using (var c = conn2.CreateCommand())
            {
                c.CommandText = "SELECT COUNT(*) FROM Region2";
                Assert.Equal(5, Convert.ToInt32(c.ExecuteScalar()));
            }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    // A large back-fill forces the index B-tree to split into multiple levels; Access must read every row
    // and the whole index (COUNT + a deep seek).
    [Fact]
    public void Access_reads_a_large_backfilled_index()
    {
        const int n = 2000;
        string path = Path.Combine(Path.GetTempPath(), $"populated-big-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using (var conn = OpenOleDb(path))
            using (var c = conn.CreateCommand())
            { c.CommandText = "CREATE TABLE Big (Id LONG, Payload TEXT(20))"; c.ExecuteNonQuery(); }

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var t = db.OpenTable("Big");
                for (int i = 1; i <= n; i++) t.Insert([i, $"row{i}"]);
                db.CreateIndex("Big", "PK_Big", [("Id", false)], isUnique: true, isPrimary: true);
            }

            using var conn2 = OpenOleDb(path);
            using (var c = conn2.CreateCommand())
            {
                c.CommandText = "SELECT COUNT(*) FROM Big";
                Assert.Equal(n, Convert.ToInt32(c.ExecuteScalar()));
            }
            using (var c = conn2.CreateCommand())
            {
                c.CommandText = "SELECT Payload FROM Big WHERE Id = 1777";
                Assert.Equal("row1777", c.ExecuteScalar());
            }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
