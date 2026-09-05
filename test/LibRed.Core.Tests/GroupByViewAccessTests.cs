using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// A "totals" (GROUP BY) view — Northwind's "Order Subtotals". Access stores each GROUP BY column as an
/// <c>Attribute=9</c> row; aggregate output columns are ordinary <c>Attribute=6</c> rows. Access runs it.
/// </summary>
public class GroupByViewAccessTests
{
    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    [Fact]
    public void Access_runs_a_group_by_totals_view()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "groupby-view-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                db.CreateView("Subtotals", new ViewSpec(
                    Distinct: false,
                    Columns:
                    [
                        new ViewColumnSpec("[Order Details].OrderID", null),
                        new ViewColumnSpec("Sum(CCur([Order Details].UnitPrice*Quantity*(1-Discount)/100)*100)", "Subtotal"),
                    ],
                    Tables: [new ViewTableSpec("Order Details", null)],
                    Joins: [],
                    Where: null,
                    GroupBy: ["[Order Details].OrderID"]));

            using var conn = OpenOleDb(path);
            using var count = conn.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM Subtotals";
            Assert.Equal(830, Convert.ToInt32(count.ExecuteScalar())); // one grouped row per order

            using var sub = conn.CreateCommand();
            sub.CommandText = "SELECT Subtotal FROM Subtotals WHERE OrderID = 10248";
            Assert.Equal(440m, Convert.ToDecimal(sub.ExecuteScalar()));
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
