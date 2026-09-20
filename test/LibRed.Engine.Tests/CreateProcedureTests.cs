using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class CreateProcedureTests
{
    private static string Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "proc-");
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
        finally { TemporaryDatabase.Delete(path); }
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
        finally { TemporaryDatabase.Delete(path); }
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
        finally { TemporaryDatabase.Delete(path); }
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
        finally { TemporaryDatabase.Delete(path); }
    }

    // Parenthesised @-parameter list + a nested paren-join onto a view ("Employee Sales by Country" shape).
    // The @-params are stored bare (no @), the body keeps @refs, and it reads back and executes with values.
    [Fact]
    public void Parenthesised_at_parameter_procedure_reads_back_and_executes()
    {
        string path = Fresh();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                new QueryEngine(db).ExecuteNonQuery(
                    "CREATE PROCEDURE `Emp Sales Test` " +
                    "(@Beginning_Date DateTime, @Ending_Date DateTime) AS " +
                    "SELECT Employees.Country, Orders.OrderID, `Order Subtotals`.Subtotal AS SaleAmount " +
                    "FROM Employees INNER JOIN " +
                    "(Orders INNER JOIN `Order Subtotals` ON Orders.OrderID = `Order Subtotals`.OrderID) " +
                    "ON Employees.EmployeeID = Orders.EmployeeID " +
                    "WHERE Orders.ShippedDate BETWEEN @Beginning_Date AND @Ending_Date");

            using (var db = JetDatabase.Open(path)) // fresh open: read from the file
            {
                var e = new QueryEngine(db);
                var args = new Dictionary<string, object?>
                {
                    ["Beginning_Date"] = new DateTime(1997, 1, 1),
                    ["Ending_Date"] = new DateTime(1997, 12, 31),
                };
                int viaProc = e.ExecuteQuery("SELECT * FROM `Emp Sales Test`", args).Rows.Count();
                int direct = e.ExecuteQuery(
                    "SELECT Orders.OrderID FROM Employees INNER JOIN " +
                    "(Orders INNER JOIN `Order Subtotals` ON Orders.OrderID = `Order Subtotals`.OrderID) " +
                    "ON Employees.EmployeeID = Orders.EmployeeID " +
                    "WHERE Orders.ShippedDate BETWEEN #1/1/1997# AND #12/31/1997#").Rows.Count();
                Assert.True(direct > 0);
                Assert.Equal(direct, viaProc);
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Action-query procedure bodies we support (CREATE TABLE, INSERT ... VALUES) parse and store.
    [Theory]
    [InlineData("CREATE PROCEDURE `MakeT` AS CREATE TABLE ZZT (Id LONG, Nm TEXT(50))")]
    [InlineData("CREATE PROCEDURE `AddShip` AS INSERT INTO Shippers (CompanyName) VALUES ('ZZ Co')")]
    public void Action_query_procedure_body_is_stored(string sql)
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            new QueryEngine(db).ExecuteNonQuery(sql); // no throw
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Stored action queries are read back from the file and executed through LibRed's own engine by name:
    // the make-table creates the table, the append inserts the row.
    [Fact]
    public void Action_queries_read_back_and_execute_through_libred()
    {
        string path = Fresh();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var e = new QueryEngine(db);
                e.ExecuteNonQuery("CREATE PROCEDURE MakeZ AS CREATE TABLE ZZLib (Id LONG, Nm TEXT(50))");
                e.ExecuteNonQuery(
                    "CREATE PROCEDURE AddShip AS " +
                    "INSERT INTO Shippers (CompanyName, Phone) VALUES ('LibRed Co', '555-0100')");
            }

            using (var db = JetDatabase.Open(path, readOnly: false)) // fresh open: read from the file
            {
                var e = new QueryEngine(db);

                e.ExecuteStoredActionQuery("MakeZ"); // reconstructed CREATE TABLE
                Assert.Equal(0, e.ExecuteQuery("SELECT * FROM ZZLib").Rows.Count()); // table now exists, empty

                int before = e.ExecuteQuery("SELECT * FROM Shippers").Rows.Count();
                e.ExecuteStoredActionQuery("AddShip"); // reconstructed INSERT ... VALUES
                Assert.Equal(before + 1, e.ExecuteQuery("SELECT * FROM Shippers").Rows.Count());
                Assert.Equal("555-0100", e.ExecuteQuery(
                    "SELECT Phone FROM Shippers WHERE CompanyName = 'LibRed Co'").Rows.First()[0]);

                Assert.Throws<InvalidOperationException>(() => e.ExecuteStoredActionQuery("NoSuchQuery"));
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // An INSERT with no column list can't be stored as an append query: the rows pair each value with the
    // column it goes in, so there is nowhere to put a positional value list.
    [Fact]
    public void Unsupported_procedure_body_is_rejected()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            Assert.Throws<NotSupportedException>(() => new QueryEngine(db).ExecuteNonQuery(
                "CREATE PROCEDURE `AddNoCols` AS INSERT INTO Shippers VALUES ('ZZ Co')"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Each remaining action kind, written and then read back from the file as the statement it was written
    // from, and run by name. (That the stored rows are the ones ACE writes, and that ACE runs them, is
    // measured in StoredActionQueryWriteAccessTests — this is the half that needs no engine but LibRed's.)
    [Theory]
    [InlineData("UPDATE Customers SET City = 'X' WHERE Country = 'UK'",
        "UPDATE [Customers] SET [City] = 'X' WHERE Country = 'UK'", 7)]
    [InlineData("UPDATE Orders INNER JOIN Customers ON Orders.CustomerID = Customers.CustomerID " +
                "SET Orders.ShipCity = 'X' WHERE Customers.Country = 'UK'",
        "UPDATE [Orders] INNER JOIN [Customers] ON Orders.CustomerID = Customers.CustomerID " +
        "SET [Orders].[ShipCity] = 'X' WHERE Customers.Country = 'UK'", 56)]
    [InlineData("DELETE FROM Shippers WHERE ShipperID > 900",
        "DELETE * FROM [Shippers] WHERE ShipperID > 900", 0)]
    [InlineData("DELETE Shippers.* FROM Shippers WHERE ShipperID > 900",
        "DELETE Shippers.* FROM [Shippers] WHERE ShipperID > 900", 0)]
    [InlineData("SELECT ShipperID, CompanyName INTO ShipperCopy FROM Shippers WHERE ShipperID > 1",
        "SELECT ShipperID, CompanyName INTO [ShipperCopy] FROM [Shippers] WHERE ShipperID > 1", 2)]
    [InlineData("INSERT INTO Shippers (CompanyName) SELECT ContactName FROM Customers WHERE Country = 'UK'",
        "INSERT INTO [Shippers] ([CompanyName]) SELECT ContactName FROM [Customers] WHERE Country = 'UK'", 7)]
    public void Action_query_body_round_trips_through_the_file(string body, string expected, int affected)
    {
        string path = Fresh();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                new QueryEngine(db).ExecuteNonQuery($"CREATE PROCEDURE [P] AS {body}");

            using (var db = JetDatabase.Open(path, readOnly: false)) // fresh open: read from the file
            {
                Assert.Equal(expected, db.Catalog.ActionQueries["P"].Sql);
                Assert.Equal(affected, new QueryEngine(db).ExecuteNonQuery("EXECUTE [P]"));
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void A_written_action_querys_parameters_bind_when_it_is_executed()
    {
        string path = Fresh();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                new QueryEngine(db).ExecuteNonQuery(
                    "CREATE PROCEDURE [ByCountry] (pCity Text(50), pCountry Text(20)) AS " +
                    "UPDATE Customers SET City = pCity WHERE Country = pCountry");

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var engine = new QueryEngine(db);
                // Read back with the PARAMETERS clause that makes the body's references parameters, declared
                // lengths and all — they ride in the parameter row's LvExtra.
                Assert.Equal(
                    "PARAMETERS [pCity] TEXT(50), [pCountry] TEXT(20); " +
                    "UPDATE [Customers] SET [City] = pCity WHERE Country = pCountry",
                    db.Catalog.ActionQueries["ByCountry"].Sql);

                Assert.Equal(7, engine.ExecuteNonQuery("EXECUTE [ByCountry] 'Ankh-Morpork', 'UK'"));
                Assert.Equal(
                    7,
                    engine.ExecuteQuery("SELECT COUNT(*) FROM Customers WHERE City = 'Ankh-Morpork'")
                        .Rows.Single()[0]);
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // A procedure name, like a view, cannot collide with an existing table.
    [Fact]
    public void Procedure_name_colliding_with_an_object_throws()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            Assert.Throws<SchemaObjectExistsException>(() =>
                new QueryEngine(db).ExecuteNonQuery("CREATE PROCEDURE `Customers` AS SELECT `CustomerID` FROM `Customers`"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
