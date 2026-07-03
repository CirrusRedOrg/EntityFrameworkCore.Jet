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
        // The shape from NorthwindGroupByQueryLibRedTest that used to fail to parse. (Table renamed from
        // "Employees" to avoid colliding with Northwind's own Employees in this copied-fixture test.)
        const string sql = """
            CREATE TABLE `EmpCheck` (
                `EmployeeID` counter NOT NULL,
                `LastName` varchar(20) NOT NULL,
                `BirthDate` datetime NULL,
                `ReportsTo` int NULL,
                CONSTRAINT `PK_EmpCheck` PRIMARY KEY (`EmployeeID`),
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
                var t = db.Catalog.FindTable("EmpCheck")!;
                Assert.NotNull(t);
                Assert.Contains(t.Indexes, ix => ix.IsPrimaryKey
                    && ix.Columns.Select(c => c.Column.Name).SequenceEqual(["EmployeeID"]));
                // The CHECK persists and reads back with its name and verbatim expression.
                var (name, expr) = Assert.Single(t.CheckConstraints);
                Assert.Equal("CK_BirthDate", name);
                Assert.Equal("[BirthDate] < NOW()", expr);
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
