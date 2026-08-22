using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class AlterTableAddForeignKeyTests
{
    private static string Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "alterfk-");
        return path;
    }

    // ALTER TABLE … ADD CONSTRAINT … FOREIGN KEY (col) REFERENCES parent (col) — the Northwind
    // CustomerCustomerDemo → CustomerDemographics shape. It executes and reads back as a relationship.
    [Fact]
    public void Add_foreign_key_to_existing_table()
    {
        string path = Fresh();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var e = new QueryEngine(db);
                e.ExecuteNonQuery("CREATE TABLE Demographics (CustomerTypeID TEXT(10) CONSTRAINT PK_Demo PRIMARY KEY)");
                e.ExecuteNonQuery("CREATE TABLE CustDemoLink (CustomerID TEXT(10), CustomerTypeID TEXT(10))");
                e.ExecuteNonQuery(
                    "ALTER TABLE CustDemoLink ADD CONSTRAINT `FK_CustDemoLink` FOREIGN KEY (`CustomerTypeID`) " +
                    "REFERENCES `Demographics` (`CustomerTypeID`)");
            }

            using (var db = JetDatabase.Open(path)) // fresh open: read from the file
            {
                var fk = Assert.Single(db.Catalog.ForeignKeysOf("CustDemoLink"));
                Assert.Equal("Demographics", fk.ReferencedTable, ignoreCase: true);
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // A self-referencing foreign key (Northwind's Employees.ReportsTo → Employees.EmployeeID).
    [Fact]
    public void Add_self_referencing_foreign_key()
    {
        string path = Fresh();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var e = new QueryEngine(db);
                e.ExecuteNonQuery("CREATE TABLE Staff (EmployeeID LONG CONSTRAINT PK_Staff PRIMARY KEY, ReportsTo LONG)");
                e.ExecuteNonQuery(
                    "ALTER TABLE Staff ADD CONSTRAINT `FK_Staff_Staff` FOREIGN KEY (`ReportsTo`) " +
                    "REFERENCES `Staff` (`EmployeeID`)");
            }

            using (var db = JetDatabase.Open(path))
            {
                var fk = Assert.Single(db.Catalog.ForeignKeysOf("Staff"));
                Assert.Equal("Staff", fk.ReferencedTable, ignoreCase: true);
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
