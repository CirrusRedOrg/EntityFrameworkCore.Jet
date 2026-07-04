using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// A parameterized stored query (CREATE PROCEDURE) — Northwind's "Sales by Year". Access stores each
/// declared parameter as an <c>Attribute=2</c> MSysQueries row (Name1 = name, Flag = Jet type code, e.g.
/// 8 = DateTime). Access runs it and honours the supplied parameter values.
/// </summary>
public class ProcedureParameterAccessTests
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
    public void Access_runs_a_parameterized_procedure()
    {
        string path = Path.Combine(Path.GetTempPath(), $"proc-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                db.CreateView("Orders in range", new ViewSpec(
                    Distinct: false,
                    Columns: [new ViewColumnSpec("Orders.OrderID", null)],
                    Tables: [new ViewTableSpec("Orders", null)],
                    Joins: [],
                    Where: "Orders.OrderDate Between [Beginning Date] And [Ending Date]",
                    GroupBy: null,
                    Parameters:
                    [
                        new ViewParameterSpec("Beginning Date", (byte)JetDataType.DateTime),
                        new ViewParameterSpec("Ending Date", (byte)JetDataType.DateTime),
                    ]));

            using var conn = OpenOleDb(path);

            // Ground truth: the same date filter run directly against Orders.
            using var direct = conn.CreateCommand();
            direct.CommandText = "SELECT COUNT(*) FROM Orders WHERE OrderDate Between #1/1/1997# And #12/31/1997#";
            int expected = Convert.ToInt32(direct.ExecuteScalar());
            Assert.True(expected > 0);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM [Orders in range]";
            cmd.Parameters.Add(new OleDbParameter("Beginning Date", new DateTime(1997, 1, 1)));
            cmd.Parameters.Add(new OleDbParameter("Ending Date", new DateTime(1997, 12, 31)));
            int count = Convert.ToInt32(cmd.ExecuteScalar());
            Assert.Equal(expected, count); // the procedure honours the supplied parameter values
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    // "Employee Sales by Country" shape: @-parameters + a nested join onto the "Order Subtotals" view.
    // The params are stored bare (no @); the WHERE keeps @refs. Access runs it honouring supplied values.
    [Fact]
    public void Access_runs_a_nested_join_at_parameter_procedure()
    {
        string path = Path.Combine(Path.GetTempPath(), $"empsales-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                db.CreateView("Emp Sales By Country Test", new ViewSpec(
                    Distinct: false,
                    Columns:
                    [
                        new ViewColumnSpec("Employees.Country", null),
                        new ViewColumnSpec("Orders.OrderID", null),
                        new ViewColumnSpec("[Order Subtotals].Subtotal", "SaleAmount"),
                    ],
                    Tables:
                    [
                        new ViewTableSpec("Employees", null),
                        new ViewTableSpec("Orders", null),
                        new ViewTableSpec("Order Subtotals", null),
                    ],
                    Joins:
                    [
                        new ViewJoinSpec(ViewJoinType.Inner, "Orders.OrderID = [Order Subtotals].OrderID", "Orders", "Order Subtotals"),
                        new ViewJoinSpec(ViewJoinType.Inner, "Employees.EmployeeID = Orders.EmployeeID", "Employees", "Orders"),
                    ],
                    Where: "Orders.ShippedDate Between @Beginning_Date And @Ending_Date",
                    GroupBy: null,
                    Parameters:
                    [
                        new ViewParameterSpec("Beginning_Date", (byte)JetDataType.DateTime),
                        new ViewParameterSpec("Ending_Date", (byte)JetDataType.DateTime),
                    ]));

            using var conn = OpenOleDb(path);

            using var direct = conn.CreateCommand();
            direct.CommandText =
                "SELECT COUNT(*) FROM Employees INNER JOIN " +
                "(Orders INNER JOIN [Order Subtotals] ON Orders.OrderID = [Order Subtotals].OrderID) " +
                "ON Employees.EmployeeID = Orders.EmployeeID " +
                "WHERE Orders.ShippedDate Between #1/1/1997# And #12/31/1997#";
            int expected = Convert.ToInt32(direct.ExecuteScalar());
            Assert.True(expected > 0);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM [Emp Sales By Country Test]";
            cmd.Parameters.Add(new OleDbParameter("Beginning_Date", new DateTime(1997, 1, 1)));
            cmd.Parameters.Add(new OleDbParameter("Ending_Date", new DateTime(1997, 12, 31)));
            Assert.Equal(expected, Convert.ToInt32(cmd.ExecuteScalar()));
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
