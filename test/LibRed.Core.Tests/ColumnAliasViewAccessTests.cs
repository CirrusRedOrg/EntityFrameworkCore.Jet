using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// A view column can carry an alias, stored in the MSysQueries column row's Name1 (as Access does, e.g.
/// Invoices' <c>Customers.CompanyName AS CustomerName</c>). Access resolves the aliased output column.
/// </summary>
public class ColumnAliasViewAccessTests
{
    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    [Fact]
    public void Access_runs_a_multi_join_view_with_a_column_alias()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "colalias-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                db.CreateView("CustLines", new ViewSpec(
                    Distinct: false,
                    Columns:
                    [
                        new ViewColumnSpec("Customers.CompanyName", "CustomerName"), // aliased
                        new ViewColumnSpec("Orders.OrderID", null),
                        new ViewColumnSpec("[Order Details].Quantity", null),
                    ],
                    Tables:
                    [
                        new ViewTableSpec("Customers", null),
                        new ViewTableSpec("Orders", null),
                        new ViewTableSpec("Order Details", null),
                    ],
                    Joins:
                    [
                        new ViewJoinSpec(ViewJoinType.Inner, "Customers.CustomerID = Orders.CustomerID", "Customers", "Orders"),
                        new ViewJoinSpec(ViewJoinType.Inner, "Orders.OrderID = [Order Details].OrderID", "Orders", "Order Details"),
                    ],
                    Where: null));

            using var conn = OpenOleDb(path);
            using var count = conn.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM CustLines";
            Assert.Equal(2155, Convert.ToInt32(count.ExecuteScalar()));

            // The output column is exposed under its alias.
            using var aliased = conn.CreateCommand();
            aliased.CommandText = "SELECT COUNT(CustomerName) FROM CustLines";
            Assert.Equal(2155, Convert.ToInt32(aliased.ExecuteScalar()));
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
