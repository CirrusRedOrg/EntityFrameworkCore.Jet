using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// A parenthesized join group in FROM, <c>A INNER JOIN (B INNER JOIN C ON …) ON …</c>, parses (as a
/// nested join, not a subquery) and executes — the join shape Access's query designer emits (e.g. the
/// "Invoices" view). A bare <c>(Table)</c> is also accepted.
/// </summary>
public class ParenthesizedJoinTests
{
    private static string Fresh()
    {
        string p = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "parenjoin-");
        return p;
    }

    [Fact]
    public void Parenthesized_join_group_matches_the_flat_join()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path);
            var e = new QueryEngine(db);

            int flat = e.ExecuteQuery(
                "SELECT Customers.CompanyName, Orders.OrderID FROM Customers " +
                "INNER JOIN Orders ON Customers.CustomerID = Orders.CustomerID " +
                "INNER JOIN `Order Details` ON Orders.OrderID = `Order Details`.OrderID").Rows.Count();

            int nested = e.ExecuteQuery(
                "SELECT Customers.CompanyName, Orders.OrderID FROM Customers " +
                "INNER JOIN (Orders INNER JOIN `Order Details` ON Orders.OrderID = `Order Details`.OrderID) " +
                "ON Customers.CustomerID = Orders.CustomerID").Rows.Count();

            Assert.Equal(2155, flat);      // Customers ⋈ Orders ⋈ Order Details
            Assert.Equal(flat, nested);
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
