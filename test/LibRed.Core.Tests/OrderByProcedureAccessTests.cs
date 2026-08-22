using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// A TOP + ORDER BY stored query — Northwind's "Ten Most Expensive Products". Access stores TOP as an
/// <c>Attribute=3</c> flag row (Flag bit 0x10, Name1 = the count) and each ORDER BY key as an
/// <c>Attribute=0x0B</c> row (Expression = the column, Name1 = "d" for descending). Access runs it.
/// </summary>
public class OrderByProcedureAccessTests
{
    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    [Fact]
    public void Access_runs_a_top_order_by_query()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "orderby-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                db.CreateView("Priciest Ten", new ViewSpec(
                    Distinct: false,
                    Columns:
                    [
                        new ViewColumnSpec("Products.ProductName", "TenMostExpensiveProducts"),
                        new ViewColumnSpec("Products.UnitPrice", null),
                    ],
                    Tables: [new ViewTableSpec("Products", null)],
                    Joins: [],
                    Where: null,
                    GroupBy: null,
                    Parameters: null,
                    OrderBy: [new ViewOrderBySpec("Products.UnitPrice", Descending: true)],
                    Top: 10));

            using var conn = OpenOleDb(path);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT UnitPrice FROM [Priciest Ten]";
            var prices = new List<decimal>();
            using (var reader = cmd.ExecuteReader())
                while (reader.Read()) prices.Add(Convert.ToDecimal(reader[0]));

            Assert.Equal(10, prices.Count);                                  // TOP 10
            Assert.Equal(prices.OrderByDescending(p => p).ToList(), prices); // ORDER BY DESC
            Assert.Equal(263.50m, prices[0]);                               // Côte de Blaye
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
