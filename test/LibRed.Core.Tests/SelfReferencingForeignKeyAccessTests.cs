using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// A self-referencing foreign key added via ALTER TABLE (Northwind's Employees.ReportsTo →
/// Employees.EmployeeID). Both relationship ends live in the one table's TDEF; the backing index over the
/// FK column is back-filled from the existing rows. Access reads and enforces the relationship.
/// </summary>
public class SelfReferencingForeignKeyAccessTests
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
    public void Access_reads_and_enforces_a_self_referencing_foreign_key()
    {
        string path = Path.Combine(Path.GetTempPath(), $"selffk-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using (var conn = OpenOleDb(path))
            using (var c = conn.CreateCommand())
            { c.CommandText = "CREATE TABLE Emp (EmployeeID LONG CONSTRAINT PK PRIMARY KEY, ReportsTo LONG)"; c.ExecuteNonQuery(); }

            // Seed a small hierarchy via LibRed, then add the self-reference (back-filling the ReportsTo index).
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var t = db.OpenTable("Emp");
                t.Insert([1, null]);   // top of the tree
                t.Insert([2, 1]);
                t.Insert([3, 1]);
                t.Insert([4, 2]);
                db.AddForeignKey("Emp", new RelationshipSpec(
                    "FK_Emp_Emp", "Emp", [("ReportsTo", "EmployeeID")],
                    IsEnforced: true, CascadeUpdate: false, CascadeDelete: false));
            }

            using (var db = JetDatabase.Open(path))
                Assert.Single(db.Catalog.ForeignKeysOf("Emp")); // LibRed sees the relationship

            using var conn2 = OpenOleDb(path);
            void Exec(string sql) { using var c = conn2.CreateCommand(); c.CommandText = sql; c.ExecuteNonQuery(); }

            using (var c = conn2.CreateCommand())
            {
                c.CommandText = "SELECT COUNT(*) FROM Emp";
                Assert.Equal(4, Convert.ToInt32(c.ExecuteScalar()));
            }

            Exec("INSERT INTO Emp (EmployeeID, ReportsTo) VALUES (5, 2)");    // valid manager
            Exec("INSERT INTO Emp (EmployeeID, ReportsTo) VALUES (6, NULL)"); // no manager — allowed
            Assert.ThrowsAny<Exception>(() =>                                 // manager 99 doesn't exist
                Exec("INSERT INTO Emp (EmployeeID, ReportsTo) VALUES (7, 99)"));

            using (var c = conn2.CreateCommand())
            {
                c.CommandText = "SELECT COUNT(*) FROM Emp";
                Assert.Equal(6, Convert.ToInt32(c.ExecuteScalar()));
            }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
