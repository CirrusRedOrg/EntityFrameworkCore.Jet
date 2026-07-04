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
        string p = Path.Combine(Path.GetTempPath(), $"nested-view-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), p);
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
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
