using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class CreateViewTests
{
    private static string Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"view-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        return path;
    }

    [Fact]
    public void Create_view_executes()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery("CREATE VIEW `LondonCust` AS SELECT `CustomerID`, `CompanyName` FROM `Customers` WHERE `City` = 'London'");
            e.ExecuteNonQuery("CREATE VIEW `CustOrders` AS SELECT `c`.`CustomerID`, `o`.`OrderID` FROM `Customers` AS `c` INNER JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`");
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Theory]
    [InlineData("CREATE VIEW `V` AS SELECT `CustomerID` FROM `Customers` ORDER BY `CustomerID`", "ORDER BY")]
    [InlineData("CREATE VIEW `V` AS SELECT `Country`, COUNT(*) FROM `Customers` GROUP BY `Country`", "GROUP BY")]
    public void Non_simple_view_throws(string sql, string expected)
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var ex = Assert.Throws<NotSupportedException>(() => new QueryEngine(db).ExecuteNonQuery(sql));
            Assert.Contains(expected, ex.Message);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void View_name_colliding_with_an_object_throws()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            // Northwind already has a Customers table.
            Assert.Throws<InvalidOperationException>(() =>
                new QueryEngine(db).ExecuteNonQuery("CREATE VIEW `Customers` AS SELECT `CustomerID` FROM `Customers`"));
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
