using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

// Byte-faithful: ACE reads and ENFORCES a LibRed-written CHECK (via AddCheckConstraint, the ALTER path) and a
// LibRed-written UNIQUE index (via CreateIndex, the ADD CONSTRAINT UNIQUE path). Confirms both write byte-faithful
// structures — answering "does AddUnique write it the way ACE does": ACE accepts and enforces it.
public class AceAlterConstraintTests
{
    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    [Fact]
    public void Access_enforces_a_libred_added_check()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "chk-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("tblInvoices",
                    [new ColumnSpec("ID", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("Amount", JetDataType.Double, 8, IsFixedLength: true)],
                    primaryKey: ["ID"]);
                db.AddCheckConstraint("tblInvoices", "CheckAmount", "Amount > 0");
            }

            using var conn = OpenOleDb(path);
            using (var c = conn.CreateCommand()) { c.CommandText = "INSERT INTO tblInvoices (ID, Amount) VALUES (1, 50)"; c.ExecuteNonQuery(); }
            using var bad = conn.CreateCommand();
            bad.CommandText = "INSERT INTO tblInvoices (ID, Amount) VALUES (2, -5)";
            Assert.ThrowsAny<OleDbException>(() => bad.ExecuteNonQuery());   // ACE rejects the check violation
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Access_stops_enforcing_a_check_libred_dropped()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "dchk-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("tblInvoices",
                    [new ColumnSpec("ID", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("Amount", JetDataType.Double, 8, IsFixedLength: true)],
                    primaryKey: ["ID"]);
                db.AddCheckConstraint("tblInvoices", "CheckAmount", "Amount > 0");
                Assert.True(db.DropCheckConstraint("tblInvoices", "CheckAmount"));   // ALTER TABLE … DROP CONSTRAINT
                Assert.False(db.DropCheckConstraint("tblInvoices", "CheckAmount"));  // already gone → false
            }

            // The check is gone, so ACE now accepts the value it previously rejected.
            using var conn = OpenOleDb(path);
            using var c = conn.CreateCommand();
            c.CommandText = "INSERT INTO tblInvoices (ID, Amount) VALUES (1, -5)";
            c.ExecuteNonQuery();
            using var read = conn.CreateCommand();
            read.CommandText = "SELECT Amount FROM tblInvoices WHERE ID = 1";
            Assert.Equal(-5.0, Convert.ToDouble(read.ExecuteScalar()));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Access_enforces_a_libred_added_unique_constraint()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "uq-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("tblCustomers",
                    [new ColumnSpec("CustomerID", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("LastName", JetDataType.Text, 60, IsFixedLength: false),
                     new ColumnSpec("FirstName", JetDataType.Text, 60, IsFixedLength: false)],
                    primaryKey: ["CustomerID"]);
                db.CreateIndex("tblCustomers", "UQ_Name",
                    [("LastName", false), ("FirstName", false)], isUnique: true, isPrimary: false);
            }

            using var conn = OpenOleDb(path);
            void Insert(int id, string ln, string fn) { using var c = conn.CreateCommand(); c.CommandText = $"INSERT INTO tblCustomers (CustomerID, LastName, FirstName) VALUES ({id}, '{ln}', '{fn}')"; c.ExecuteNonQuery(); }
            Insert(1, "Smith", "John");
            Insert(2, "Smith", "Jane");                                     // different first name — ok
            using var dup = conn.CreateCommand();
            dup.CommandText = "INSERT INTO tblCustomers (CustomerID, LastName, FirstName) VALUES (3, 'Smith', 'John')";
            Assert.ThrowsAny<OleDbException>(() => dup.ExecuteNonQuery());   // ACE rejects the duplicate composite
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
