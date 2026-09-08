using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// A foreign key added to an existing table (ALTER TABLE ADD CONSTRAINT … FOREIGN KEY). LibRed writes the
/// child backing index, the parent's incoming relationship block and the MSysRelationships rows; Access
/// reads the relationship and enforces referential integrity.
/// </summary>
public class AlterForeignKeyAccessTests
{
    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    [Fact]
    public void Access_reads_and_enforces_a_libred_added_foreign_key()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "fk-");
        try
        {
            using (var conn = OpenOleDb(path))
            {
                using var c1 = conn.CreateCommand();
                c1.CommandText = "CREATE TABLE CustDemo (CustomerTypeID TEXT(10) CONSTRAINT PK PRIMARY KEY, Descr TEXT(50))";
                c1.ExecuteNonQuery();
                using var c2 = conn.CreateCommand();
                c2.CommandText = "CREATE TABLE CustCustDemo (CustomerID TEXT(10), CustomerTypeID TEXT(10))";
                c2.ExecuteNonQuery();
            }

            using (var db = JetDatabase.Open(path, readOnly: false))
                db.AddForeignKey("CustCustDemo", new RelationshipSpec(
                    "FK_CustCustDemo", "CustDemo",
                    [("CustomerTypeID", "CustomerTypeID")],
                    IsEnforced: true, CascadeUpdate: false, CascadeDelete: false));

            // LibRed's own read side sees the relationship.
            using (var db = JetDatabase.Open(path))
                Assert.Single(db.Catalog.ForeignKeysOf("CustCustDemo"));

            using var conn2 = OpenOleDb(path);
            void Exec(string sql) { using var c = conn2.CreateCommand(); c.CommandText = sql; c.ExecuteNonQuery(); }

            Exec("INSERT INTO CustDemo (CustomerTypeID, Descr) VALUES ('T1', 'Type one')");
            Exec("INSERT INTO CustCustDemo (CustomerID, CustomerTypeID) VALUES ('ALFKI', 'T1')"); // valid parent
            // Referencing a non-existent parent must be rejected by the enforced relationship.
            Assert.Throws<OleDbException>(() =>
                Exec("INSERT INTO CustCustDemo (CustomerID, CustomerTypeID) VALUES ('ANATR', 'ZZ')"));

            using var count = conn2.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM CustCustDemo";
            Assert.Equal(1, Convert.ToInt32(count.ExecuteScalar()));
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
