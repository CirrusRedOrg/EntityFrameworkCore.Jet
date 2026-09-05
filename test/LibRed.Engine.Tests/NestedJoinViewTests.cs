using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// A view over a deeply nested, parenthesized join with column aliases and computed columns — Northwind's
/// "Invoices" — is decomposed (joins flattened, each recorded by the two tables in its condition, aliases
/// captured) and created. (Its ON conditions and CCur() are stored verbatim; Access runs the result.)
/// </summary>
public class NestedJoinViewTests
{
    private const string InvoicesView = @"
CREATE VIEW Invoices2 AS
SELECT Orders.ShipName, Orders.CustomerID, Customers.CompanyName AS CustomerName,
    (FirstName + ' ' + LastName) AS Salesperson, Orders.OrderID, Shippers.CompanyName AS ShipperName,
    `Order Details`.ProductID, Products.ProductName, `Order Details`.Quantity,
    (CCur(`Order Details`.UnitPrice*Quantity*(1-Discount)/100)*100) AS ExtendedPrice, Orders.Freight
FROM Shippers INNER JOIN
    (Products INNER JOIN
        ((Employees INNER JOIN
            (Customers INNER JOIN Orders ON Customers.CustomerID = Orders.CustomerID)
          ON Employees.EmployeeID = Orders.EmployeeID)
         INNER JOIN `Order Details` ON Orders.OrderID = `Order Details`.OrderID)
     ON Products.ProductID = `Order Details`.ProductID)
    ON Shippers.ShipperID = Orders.ShipVia";

    private static string Fresh()
    {
        string p = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "nested-view-");
        return p;
    }

    [Fact]
    public void Nested_join_view_with_aliases_is_created()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            new QueryEngine(db).ExecuteNonQuery(InvoicesView); // parses, flattens the joins, stores it
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Access_multi_join_views_read_back_through_libred()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path);
            var e = new QueryEngine(db);

            // The reconstruction re-orders the flat joins into a valid chain (topological over the
            // conditions), so LibRed executes Access's own complex multi-join views.
            Assert.Equal(2155, Convert.ToInt32(e.ExecuteQuery("SELECT COUNT(*) FROM Invoices").Rows.First()[0]));
            Assert.Equal(2155, Convert.ToInt32(e.ExecuteQuery("SELECT COUNT(*) FROM `Order Details Extended`").Rows.First()[0]));

            // Its computed columns evaluate: '+'-concat of text, and CCur().
            var row = e.ExecuteQuery("SELECT Salesperson, ExtendedPrice FROM Invoices WHERE OrderID = 10248").Rows.First();
            Assert.Equal("Steven Buchanan", row[0]);                      // FirstName + ' ' + LastName
            Assert.Equal(168.00m, Convert.ToDecimal(row[1]));            // CCur(14*12*(1-0)/100)*100
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
