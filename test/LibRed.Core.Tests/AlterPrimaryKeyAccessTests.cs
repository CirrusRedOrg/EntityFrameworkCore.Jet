using System.Data.OleDb;
using LibRed;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// A multi-column primary key added to an (empty) table — the write path ALTER TABLE ADD CONSTRAINT
/// PRIMARY KEY reuses (CreateIndex with isPrimary/isUnique). Access accepts it and enforces uniqueness.
/// </summary>
public class AlterPrimaryKeyAccessTests
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
    public void Access_enforces_a_libred_written_multi_column_primary_key()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pk-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using (var conn = OpenOleDb(path))
            using (var c = conn.CreateCommand())
            { c.CommandText = "CREATE TABLE CCDemo (CustomerID TEXT(10), CustomerTypeID TEXT(10))"; c.ExecuteNonQuery(); }

            using (var db = JetDatabase.Open(path, readOnly: false))
                db.CreateIndex("CCDemo", "PK_CCDemo",
                    [("CustomerID", false), ("CustomerTypeID", false)], isUnique: true, isPrimary: true);

            using var conn2 = OpenOleDb(path);
            void Insert(string a, string b)
            {
                using var c = conn2.CreateCommand();
                c.CommandText = $"INSERT INTO CCDemo (CustomerID, CustomerTypeID) VALUES ('{a}', '{b}')";
                c.ExecuteNonQuery();
            }

            Insert("ALFKI", "T1");
            Insert("ALFKI", "T2");          // same CustomerID, different type — allowed
            Insert("ANATR", "T1");          // different CustomerID — allowed
            // Duplicate composite key must be rejected by the primary key.
            Assert.ThrowsAny<Exception>(() => Insert("ALFKI", "T1"));

            using var count = conn2.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM CCDemo";
            Assert.Equal(3, Convert.ToInt32(count.ExecuteScalar()));
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
