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
    private static OleDbConnection OpenOleDb(string path)
    {
        Exception? last = null;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            foreach (string provider in new[] { "Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0" })
            {
                try { var c = new OleDbConnection($"Provider={provider};Data Source={path};OLE DB Services=-4;"); c.Open(); return c; }
                catch (Exception ex) when (ex is OleDbException or InvalidOperationException) { last = ex; }
            }
            Thread.Sleep(50);
        }
        throw new InvalidOperationException("No Microsoft.ACE.OLEDB provider opened the database.", last);
    }

    [Fact]
    public void Access_runs_a_multi_join_view_with_a_column_alias()
    {
        string path = Path.Combine(Path.GetTempPath(), $"colalias-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
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
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
