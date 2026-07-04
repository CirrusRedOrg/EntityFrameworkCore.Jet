using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// Access runs a LibRed-created DISTINCT view with a RIGHT JOIN and a BETWEEN/date WHERE — Northwind's
/// "Quarterly Orders". DISTINCT is the MSysQueries flag row; the WHERE is stored verbatim.
/// </summary>
public class DistinctViewAccessTests
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
    public void Access_runs_a_distinct_right_join_between_view()
    {
        string path = Path.Combine(Path.GetTempPath(), $"distinct-view-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                db.CreateView("QO2", new ViewSpec(
                    Distinct: true,
                    Columns: ["Customers.CustomerID", "Customers.CompanyName", "Customers.City", "Customers.Country"],
                    Tables: [new ViewTableSpec("Customers", null), new ViewTableSpec("Orders", null)],
                    Joins: [new ViewJoinSpec(ViewJoinType.Right, "Customers.CustomerID = Orders.CustomerID", "Customers", "Orders")],
                    Where: "Orders.OrderDate BETWEEN #1/1/1997# And #12/31/1997#"));

            using var conn = OpenOleDb(path);
            using var count = conn.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM QO2";
            Assert.Equal(86, Convert.ToInt32(count.ExecuteScalar()));
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
