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
    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    [Fact]
    public void Access_runs_a_distinct_right_join_between_view()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "distinct-view-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                db.CreateView("QO2", new ViewSpec(
                    Distinct: true,
                    Columns: [new ViewColumnSpec("Customers.CustomerID", null), new ViewColumnSpec("Customers.CompanyName", null), new ViewColumnSpec("Customers.City", null), new ViewColumnSpec("Customers.Country", null)],
                    Tables: [new ViewTableSpec("Customers", null), new ViewTableSpec("Orders", null)],
                    Joins: [new ViewJoinSpec(ViewJoinType.Right, "Customers.CustomerID = Orders.CustomerID", "Customers", "Orders")],
                    Where: "Orders.OrderDate BETWEEN #1/1/1997# And #12/31/1997#"));

            using var conn = OpenOleDb(path);
            using var count = conn.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM QO2";
            Assert.Equal(86, Convert.ToInt32(count.ExecuteScalar()));
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
