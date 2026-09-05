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
    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    [Fact]
    public void Access_enforces_a_libred_written_multi_column_primary_key()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "pk-");
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
            Assert.Throws<OleDbException>(() => Insert("ALFKI", "T1"));

            using var count = conn2.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM CCDemo";
            Assert.Equal(3, Convert.ToInt32(count.ExecuteScalar()));
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
