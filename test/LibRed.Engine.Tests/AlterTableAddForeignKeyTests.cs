using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class AlterTableAddForeignKeyTests
{
    private static string Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"alterfk-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
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
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
