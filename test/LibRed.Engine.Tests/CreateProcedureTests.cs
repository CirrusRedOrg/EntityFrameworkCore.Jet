using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class CreateProcedureTests
{
    private static string Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"proc-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        return path;
    }

    // A CREATE PROCEDURE with declared parameters (no parens; Access syntax) parses and executes, storing a
    // parameterized query the same way a view is stored plus a parameter row per declared parameter.
    [Fact]
    public void Create_procedure_with_parameters_executes()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            new QueryEngine(db).ExecuteNonQuery(
                "CREATE PROCEDURE `Orders in range` " +
                "`Beginning Date` DateTime, `Ending Date` DateTime AS " +
                "SELECT Orders.OrderID FROM Orders " +
                "WHERE Orders.OrderDate BETWEEN `Beginning Date` AND `Ending Date`");
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    // A stored parameterized query is read back (PARAMETERS clause + body) and executed through LibRed's
    // own engine when parameter values are supplied — matching the same date filter run directly on Orders.
    [Fact]
    public void Parameterized_procedure_reads_back_and_executes()
    {
        string path = Fresh();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                new QueryEngine(db).ExecuteNonQuery(
                    "CREATE PROCEDURE `Orders in range` " +
                    "`Beginning Date` DateTime, `Ending Date` DateTime AS " +
                    "SELECT Orders.OrderID FROM Orders " +
                    "WHERE Orders.OrderDate BETWEEN `Beginning Date` AND `Ending Date`");

            using (var db = JetDatabase.Open(path)) // fresh open: the procedure is read from the file
            {
                var e = new QueryEngine(db);
                var args = new Dictionary<string, object?>
                {
                    ["Beginning Date"] = new DateTime(1997, 1, 1),
                    ["Ending Date"] = new DateTime(1997, 12, 31),
                };
                int viaProc = e.ExecuteQuery("SELECT OrderID FROM `Orders in range`", args).Rows.Count();
                int direct = e.ExecuteQuery(
                    "SELECT OrderID FROM Orders WHERE OrderDate BETWEEN #1/1/1997# AND #12/31/1997#").Rows.Count();
                Assert.True(direct > 0);
                Assert.Equal(direct, viaProc);
            }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    // Read back ACE's OWN stored "Ten Most Expensive Products" (TOP 10 + ORDER BY DESC, shipped in Northwind)
    // and execute it through LibRed's engine — 10 rows, price-descending. Exercises TOP (an AttrFlag row) and
    // ORDER BY (AttrOrderBy rows) reconstruction against a real Access-written query.
    [Fact]
    public void Read_back_northwinds_top_order_by_procedure()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path);
            var rows = new QueryEngine(db)
                .ExecuteQuery("SELECT * FROM `Ten Most Expensive Products`").Rows.ToList();
            Assert.Equal(10, rows.Count);
            var prices = rows.Select(r => Convert.ToDecimal(r[1])).ToList();
            Assert.Equal(prices.OrderByDescending(p => p).ToList(), prices); // descending
            Assert.Equal(263.50m, prices[0]); // Côte de Blaye, Northwind's priciest
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    // Round-trip a LibRed-created TOP + ORDER BY procedure (under a non-colliding name).
    [Fact]
    public void Top_and_order_by_procedure_round_trips()
    {
        string path = Fresh();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                new QueryEngine(db).ExecuteNonQuery(
                    "CREATE PROCEDURE `Priciest Ten` AS " +
                    "SELECT TOP 10 Products.ProductName, Products.UnitPrice " +
                    "FROM Products ORDER BY Products.UnitPrice DESC");

            using (var db = JetDatabase.Open(path))
            {
                var prices = new QueryEngine(db)
                    .ExecuteQuery("SELECT * FROM `Priciest Ten`").Rows.Select(r => Convert.ToDecimal(r[1])).ToList();
                Assert.Equal(10, prices.Count);
                Assert.Equal(prices.OrderByDescending(p => p).ToList(), prices);
                Assert.Equal(263.50m, prices[0]);
            }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    // An action-query procedure body (INSERT/CREATE TABLE) parses but is not stored yet; other statement
    // types (UPDATE/DELETE/…) have no grammar and fail to parse. Either way, CREATE PROCEDURE rejects it.
    [Theory]
    [InlineData("CREATE PROCEDURE `AddCust` AS INSERT INTO Customers (CustomerID) VALUES ('ZZZZZ')")]
    [InlineData("CREATE PROCEDURE `MakeT` AS CREATE TABLE T (Id LONG)")]
    [InlineData("CREATE PROCEDURE `DelCust` AS DELETE FROM Customers")]
    public void Action_query_procedure_body_is_rejected(string sql)
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            Assert.ThrowsAny<Exception>(() => new QueryEngine(db).ExecuteNonQuery(sql));
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    // A procedure name, like a view, cannot collide with an existing table.
    [Fact]
    public void Procedure_name_colliding_with_an_object_throws()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            Assert.Throws<InvalidOperationException>(() =>
                new QueryEngine(db).ExecuteNonQuery("CREATE PROCEDURE `Customers` AS SELECT `CustomerID` FROM `Customers`"));
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
