using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class CheckConstraintTests
{
    private static string Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"chk-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        return path;
    }

    // CHECK constraints (table- and column-level) parse and are ignored (not enforced yet), so a
    // CREATE TABLE that has them still creates the table and its other constraints.
    [Fact]
    public void Table_level_check_constraint_is_accepted_and_table_is_created()
    {
        // The exact script from NorthwindGroupByQueryLibRedTest that used to fail to parse.
        const string sql = """
            CREATE TABLE `Employees` (
                `EmployeeID` counter NOT NULL,
                `LastName` varchar(20) NOT NULL,
                `BirthDate` datetime NULL,
                `ReportsTo` int NULL,
                CONSTRAINT `PK_Employees` PRIMARY KEY (`EmployeeID`),
                CONSTRAINT `CK_BirthDate` CHECK ([BirthDate] < NOW())
            )
            """;
        string path = Fresh();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                new QueryEngine(db).ExecuteNonQuery(sql);
            using (var db = JetDatabase.Open(path))
            {
                var t = db.Catalog.FindTable("Employees")!;
                Assert.NotNull(t);
                Assert.Contains(t.Indexes, ix => ix.IsPrimaryKey
                    && ix.Columns.Select(c => c.Column.Name).SequenceEqual(["EmployeeID"]));
            }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void Column_level_check_constraint_is_accepted()
    {
        string path = Fresh();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                new QueryEngine(db).ExecuteNonQuery(
                    "CREATE TABLE `T` (`Id` INTEGER PRIMARY KEY, `Age` INTEGER CONSTRAINT `CK_Age` CHECK (`Age` > 0 AND `Age` < 200))");
            using (var db = JetDatabase.Open(path))
                Assert.NotNull(db.Catalog.FindTable("T"));
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
