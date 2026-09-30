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

    // The relationship is refused when the rows already in the table break it, and nothing of it is left behind:
    // an orphan inserted afterwards is accepted. The same rows ACE refuses and accepts (probed over OLE DB): MATCH
    // FULL, so a composite key that is partly null is refused and one entirely null is not, and the ON DELETE
    // action makes no difference.
    [Theory]
    [InlineData("10, 1, NULL", "(ParentId) REFERENCES Parents (Id)", true)]
    [InlineData("10, NULL, NULL", "(ParentId) REFERENCES Parents (Id)", true)]
    [InlineData("10, 2, NULL", "(ParentId) REFERENCES Parents (Id)", false)]
    [InlineData("10, 2, NULL", "(ParentId) REFERENCES Parents", false)]
    [InlineData("10, 2, NULL", "(ParentId) REFERENCES Parents (Id) ON DELETE CASCADE", false)]
    [InlineData("10, 2, NULL", "(ParentId) REFERENCES Parents (Id) ON DELETE SET NULL", false)]
    [InlineData("10, 1, 1", "(ParentId, Code) REFERENCES Parents (Id, Code)", true)]
    [InlineData("10, NULL, NULL", "(ParentId, Code) REFERENCES Parents (Id, Code)", true)]
    [InlineData("10, 1, NULL", "(ParentId, Code) REFERENCES Parents (Id, Code)", false)]
    [InlineData("10, 1, 2", "(ParentId, Code) REFERENCES Parents (Id, Code)", false)]
    public void Add_foreign_key_checks_the_rows_already_in_the_table(string row, string constraint, bool accepted)
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery("CREATE TABLE Parents (Id LONG PRIMARY KEY, Code LONG, CONSTRAINT UQ_Code UNIQUE (Id, Code))");
            e.ExecuteNonQuery("CREATE TABLE Children (Id LONG PRIMARY KEY, ParentId LONG, Code LONG)");
            e.ExecuteNonQuery("INSERT INTO Parents (Id, Code) VALUES (1, 1)");
            e.ExecuteNonQuery($"INSERT INTO Children (Id, ParentId, Code) VALUES ({row})");

            const string add = "ALTER TABLE Children ADD CONSTRAINT FK_Child FOREIGN KEY ";
            if (accepted)
            {
                e.ExecuteNonQuery(add + constraint);
                Assert.Single(db.Catalog.ForeignKeysOf("Children"));
                Assert.Throws<InvalidOperationException>(
                    () => e.ExecuteNonQuery("INSERT INTO Children (Id, ParentId, Code) VALUES (20, 3, 3)"));
            }
            else
            {
                var refused = Assert.Throws<InvalidOperationException>(() => e.ExecuteNonQuery(add + constraint));
                Assert.Equal(
                    "Cannot create relationships to enforce referential integrity. Existing data in table "
                    + "'Children' violates referential integrity rules in table 'Parents'.", refused.Message);
                Assert.Empty(db.Catalog.ForeignKeysOf("Children"));
                e.ExecuteNonQuery("INSERT INTO Children (Id, ParentId, Code) VALUES (20, 3, 3)");
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // A self-reference is checked against the table's own rows: a row may point at itself or at another row that
    // is there, and not at one that isn't.
    [Theory]
    [InlineData("(1, 1), (2, 1)", true)]
    [InlineData("(1, NULL), (2, 3)", false)]
    public void Add_self_referencing_foreign_key_checks_the_rows_already_in_the_table(string rows, bool accepted)
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery("CREATE TABLE Staff (EmployeeID LONG PRIMARY KEY, ReportsTo LONG)");
            e.ExecuteNonQuery($"INSERT INTO Staff (EmployeeID, ReportsTo) VALUES {rows}");

            const string add = "ALTER TABLE Staff ADD CONSTRAINT FK_Staff_Staff FOREIGN KEY (ReportsTo) REFERENCES Staff (EmployeeID)";
            if (accepted)
                e.ExecuteNonQuery(add);
            else
                Assert.Throws<InvalidOperationException>(() => e.ExecuteNonQuery(add));
            Assert.Equal(accepted ? 1 : 0, db.Catalog.ForeignKeysOf("Staff").Count());
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
