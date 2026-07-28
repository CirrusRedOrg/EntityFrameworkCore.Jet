using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

public class CreateViewAccessTests
{
    private static string CopyToTemp()
    {
        string path = Path.Combine(Path.GetTempPath(), $"libred-view-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        return path;
    }

    private static OleDbConnection OpenOleDb(string path)
    {
        foreach (string provider in new[] { "Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0" })
            try { var c = new OleDbConnection($"Provider={provider};Data Source={path};OLE DB Services=-4;"); c.Open(); return c; }
            catch (Exception ex) when (ex is OleDbException or InvalidOperationException) { }
        throw new InvalidOperationException("No Microsoft.ACE.OLEDB provider is available.");
    }

    [Fact]
    public void Access_executes_a_libred_created_view()
    {
        // LibRed writes a view the way Access does (MSysObjects type-5 row + MSysQueries rows, with the
        // MSysQueries composite index maintained). Access must open the file and run the stored query.
        string path = CopyToTemp();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateView("LondonCustomers", new ViewSpec(
                    Distinct: false, [new ViewColumnSpec("CustomerID", null), new ViewColumnSpec("CompanyName", null)],
                    [new ViewTableSpec("Customers", null)], [], "City = 'London'"));
                db.CreateView("CustOrders", new ViewSpec(
                    Distinct: false, [new ViewColumnSpec("c.CustomerID", null), new ViewColumnSpec("o.OrderID", null)],
                    [new ViewTableSpec("Customers", "c"), new ViewTableSpec("Orders", "o")],
                    [new ViewJoinSpec(ViewJoinType.Inner, "c.CustomerID = o.CustomerID", "c", "o")], null));
            }

            using var conn = OpenOleDb(path);
            using (var count = conn.CreateCommand())
            {
                count.CommandText = "SELECT COUNT(*) FROM Shippers";
                Assert.Equal(3, Convert.ToInt32(count.ExecuteScalar())); // opened without corruption
            }
            // The single-table view returns the same rows as the equivalent query on the base table.
            int viaView, viaTable;
            using (var v = conn.CreateCommand()) { v.CommandText = "SELECT COUNT(*) FROM LondonCustomers"; viaView = Convert.ToInt32(v.ExecuteScalar()); }
            using (var t = conn.CreateCommand()) { t.CommandText = "SELECT COUNT(*) FROM Customers WHERE City = 'London'"; viaTable = Convert.ToInt32(t.ExecuteScalar()); }
            Assert.Equal(viaTable, viaView);
            Assert.True(viaView > 0);

            // The join view runs and returns rows.
            using var j = conn.CreateCommand();
            j.CommandText = "SELECT COUNT(*) FROM CustOrders";
            Assert.True(Convert.ToInt32(j.ExecuteScalar()) > 0);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
