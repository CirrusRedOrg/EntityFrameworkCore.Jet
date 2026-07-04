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
    public void Access_runs_a_top_order_by_query()
    {
        string path = Path.Combine(Path.GetTempPath(), $"orderby-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
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
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
