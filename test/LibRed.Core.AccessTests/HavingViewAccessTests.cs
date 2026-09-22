using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// A "totals" view whose groups are filtered — the <c>Attribute=0x0A</c> HAVING row that rides alongside the
/// <c>Attribute=9</c> GROUP BY ones. Access runs it, and reads it as the same query it would have written.
/// </summary>
[Collection(AceCollection.Name)]
public class HavingViewAccessTests
{
    private const string Subtotal = "Sum(CCur([Order Details].UnitPrice*Quantity*(1-Discount)/100)*100)";

    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    private static List<(int OrderId, decimal Total)> Read(OleDbConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        using var reader = cmd.ExecuteReader();
        var rows = new List<(int, decimal)>();
        while (reader.Read())
            rows.Add((Convert.ToInt32(reader.GetValue(0)), Convert.ToDecimal(reader.GetValue(1))));
        return rows;
    }

    [Fact]
    public void Access_runs_a_totals_view_with_having()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "having-view-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                db.CreateView("BigSubtotals", new ViewSpec(
                    Distinct: false,
                    Columns:
                    [
                        new ViewColumnSpec("[Order Details].OrderID", null),
                        new ViewColumnSpec(Subtotal, "Subtotal"),
                    ],
                    Tables: [new ViewTableSpec("Order Details", null)],
                    Joins: [],
                    Where: null,
                    GroupBy: ["[Order Details].OrderID"],
                    Having: $"{Subtotal} > 5000"));

            using var conn = OpenOleDb(path);

            // The stored view has to give exactly what Access gives for the query written out in full: the
            // HAVING row is only correct if it lands in the same place the inline clause does.
            var stored = Read(conn, "SELECT OrderID, Subtotal FROM BigSubtotals ORDER BY OrderID");
            var inline = Read(conn,
                $"SELECT [Order Details].OrderID, {Subtotal} AS Subtotal FROM [Order Details] " +
                $"GROUP BY [Order Details].OrderID HAVING {Subtotal} > 5000 ORDER BY [Order Details].OrderID");
            Assert.Equal(inline, stored);

            // …and it has to have filtered something, or the comparison above proves nothing. Northwind has
            // 830 orders; the ones over 5000 are a strict, non-empty subset.
            Assert.NotEmpty(stored);
            Assert.True(stored.Count < 830, $"HAVING filtered nothing: {stored.Count} groups.");
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Having_on_a_grouping_column_keeps_that_group()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "having-key-view-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                db.CreateView("OneSubtotal", new ViewSpec(
                    Distinct: false,
                    Columns:
                    [
                        new ViewColumnSpec("[Order Details].OrderID", null),
                        new ViewColumnSpec(Subtotal, "Subtotal"),
                    ],
                    Tables: [new ViewTableSpec("Order Details", null)],
                    Joins: [],
                    Where: null,
                    GroupBy: ["[Order Details].OrderID"],
                    Having: "[Order Details].OrderID = 10248"));

            using var conn = OpenOleDb(path);
            var rows = Read(conn, "SELECT OrderID, Subtotal FROM OneSubtotal");
            Assert.Equal([(10248, 440m)], rows);
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
