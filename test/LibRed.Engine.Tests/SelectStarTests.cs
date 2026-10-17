using LibRed.Sql.Parsing;
using Xunit;

namespace LibRed.Engine.Tests;

// A bare * among other items, and the names a SELECT's output columns go by. Every expectation is what ACE returned for
// the same statement over Northwind.
public class SelectStarTests : TempDatabaseTest
{
    private static QueryEngine Fresh() => new(TemporaryDatabase.OpenTracked(
        TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "star-"), readOnly: false));

    private static IReadOnlyList<string> Names(QueryEngine engine, string sql) => engine.ExecuteQuery(sql).ColumnNames;

    [Theory]
    // The other items first, in their order, wherever the * stands; then every column none of them returns.
    [InlineData("SELECT *, ShipperID AS X FROM Shippers", "X, CompanyName, Phone")]
    [InlineData("SELECT ShipperID AS X, * FROM Shippers", "X, CompanyName, Phone")]
    [InlineData("SELECT Phone, *, CompanyName FROM Shippers", "Phone, CompanyName, ShipperID")]
    [InlineData("SELECT *, Shippers.Phone FROM Shippers", "Phone, ShipperID, CompanyName")]
    [InlineData("SELECT *, ShipperID AS X, ShipperID AS Y FROM Shippers", "X, Y, CompanyName, Phone")]
    // An expression returns no column, so leaves them all to the *.
    [InlineData("SELECT *, ShipperID * 2 AS Y FROM Shippers", "Y, ShipperID, CompanyName, Phone")]
    [InlineData("SELECT *, (SELECT Max(OrderID) FROM Orders) AS M FROM Shippers", "M, ShipperID, CompanyName, Phone")]
    [InlineData("SELECT *, CompanyName & '!' FROM Shippers", "Expr1000, ShipperID, CompanyName, Phone")]
    // However often written, and beside a qualified star that already returns them, the columns come once.
    [InlineData("SELECT *, * FROM Shippers", "ShipperID, CompanyName, Phone")]
    [InlineData("SELECT *, *, ShipperID FROM Shippers", "ShipperID, CompanyName, Phone")]
    [InlineData("SELECT Shippers.*, * FROM Shippers", "ShipperID, CompanyName, Phone")]
    [InlineData("SELECT *, Shippers.* FROM Shippers", "ShipperID, CompanyName, Phone")]
    // A column the * returns is told apart from an alias spelled the same by its table.
    [InlineData("SELECT *, CompanyName AS ShipperID FROM Shippers", "ShipperID, Shippers.ShipperID, Phone")]
    [InlineData("SELECT *, a.ShipperID FROM Shippers AS a, Shippers AS b WHERE a.ShipperID = 1 AND b.ShipperID = 2",
        "a.ShipperID, a.CompanyName, a.Phone, b.ShipperID, b.CompanyName, b.Phone")]
    [InlineData("SELECT *, b.CompanyName AS CompanyName FROM Shippers AS a, Shippers AS b WHERE a.ShipperID = 1 AND b.ShipperID = 2",
        "CompanyName, a.ShipperID, a.CompanyName, a.Phone, b.ShipperID, b.Phone")]
    [InlineData("SELECT *, Orders.ShipVia AS V FROM Orders INNER JOIN Shippers ON Orders.ShipVia = Shippers.ShipperID WHERE OrderID < 10252",
        "V, OrderID, CustomerID, EmployeeID, OrderDate, RequiredDate, ShippedDate, Freight, ShipName, ShipAddress, ShipCity, ShipRegion, " +
        "ShipPostalCode, ShipCountry, ShipperID, CompanyName, Phone")]
    public void A_star_among_other_items_returns_every_column_they_do_not_after_them(string sql, string names) =>
        Assert.Equal(names, string.Join(", ", Names(Fresh(), sql)));

    [Fact]
    public void A_star_among_other_items_returns_their_values_and_the_rest()
    {
        object?[] row = Fresh().ExecuteQuery("SELECT TOP 1 *, ShipperID AS X FROM Shippers ORDER BY ShipperID DESC").Rows.Single();
        Assert.Equal([3, "Federal Shipping", "(503) 555-9931"], row);
    }

    [Theory]
    // An expression is Expr and 1000 past its item's place, a qualified star one item and a bare * none.
    [InlineData("SELECT 1, CompanyName, 2 FROM Shippers", "Expr1000, CompanyName, Expr1002")]
    [InlineData("SELECT Count(*), Sum(ShipperID) FROM Shippers", "Expr1000, Expr1001")]
    [InlineData("SELECT CompanyName, Count(*) FROM Shippers GROUP BY CompanyName", "CompanyName, Expr1001")]
    [InlineData("SELECT Shippers.*, ShipperID * 2 FROM Shippers", "ShipperID, CompanyName, Phone, Expr1001")]
    [InlineData("SELECT CompanyName & '' AS Expr1001, Phone & '' FROM Shippers", "Expr1001, Expr1001")]
    // A reference written again — letter for letter but for case — is one item too many: all but its last are Expr.
    [InlineData("SELECT CompanyName, CompanyName, CompanyName FROM Shippers", "Expr1000, Expr1001, CompanyName")]
    [InlineData("SELECT CompanyName, companyname FROM Shippers", "Expr1000, companyname")]
    [InlineData("SELECT ShipperID, ShipperID * 1, CompanyName, CompanyName FROM Shippers", "ShipperID, Expr1001, Expr1002, CompanyName")]
    // Otherwise names two outputs share are told apart by their tables…
    [InlineData("SELECT CompanyName, [CompanyName] FROM Shippers", "Shippers.CompanyName, Shippers.CompanyName")]
    [InlineData("SELECT Shippers.CompanyName, CompanyName FROM Shippers", "Shippers.CompanyName, Shippers.CompanyName")]
    [InlineData("SELECT ShipperID, Shippers.ShipperID, ShipperID FROM Shippers", "Expr1000, Shippers.ShipperID, Shippers.ShipperID")]
    [InlineData("SELECT a.CompanyName, b.CompanyName, a.CompanyName FROM Shippers AS a, Shippers AS b WHERE a.ShipperID = 1 AND b.ShipperID = 2",
        "Expr1000, b.CompanyName, a.CompanyName")]
    [InlineData("SELECT [Order Details].OrderID, Orders.OrderID FROM [Order Details] INNER JOIN Orders ON [Order Details].OrderID = Orders.OrderID " +
                "WHERE Orders.OrderID = 10248", "Order Details.OrderID, Orders.OrderID")]
    [InlineData("SELECT Shippers.*, CompanyName FROM Shippers", "ShipperID, Shippers.CompanyName, Phone, Shippers.CompanyName")]
    [InlineData("SELECT Shippers.*, Shippers.* FROM Shippers",
        "Shippers.ShipperID, Shippers.CompanyName, Shippers.Phone, Shippers.ShipperID, Shippers.CompanyName, Shippers.Phone")]
    // …but an alias keeps its name, beside which an unqualified reference is an Expr name.
    [InlineData("SELECT CompanyName, Phone AS CompanyName FROM Shippers", "Expr1000, CompanyName")]
    [InlineData("SELECT ShipperID AS CompanyName, CompanyName, Shippers.CompanyName FROM Shippers", "CompanyName, Expr1001, Shippers.CompanyName")]
    [InlineData("SELECT Shippers.*, Phone AS CompanyName FROM Shippers", "ShipperID, Shippers.CompanyName, Phone, CompanyName")]
    [InlineData("SELECT Count(*) AS CompanyName, CompanyName FROM Shippers GROUP BY CompanyName", "CompanyName, Expr1001")]
    [InlineData("SELECT Orders.CustomerID, Customers.CustomerID AS CustomerID FROM Orders INNER JOIN Customers " +
                "ON Orders.CustomerID = Customers.CustomerID WHERE OrderID < 10252", "Orders.CustomerID, CustomerID")]
    public void Output_columns_are_named_as_ace_names_them(string sql, string names) =>
        Assert.Equal(names, string.Join(", ", Names(Fresh(), sql)));

    [Fact]
    public void A_lone_star_over_a_join_tells_shared_names_apart_and_a_derived_table_keeps_them()
    {
        QueryEngine engine = Fresh();
        const string Join = "SELECT * FROM Orders INNER JOIN Customers ON Orders.CustomerID = Customers.CustomerID WHERE OrderID < 10252";
        IReadOnlyList<string> names = Names(engine, Join);
        Assert.Equal("Orders.CustomerID", names[1]);
        Assert.Equal("Customers.CustomerID", names[14]);
        Assert.Equal("CompanyName", names[15]);

        Assert.Equal(["HANAR", "TOMSP", "VICTE", "VINET"],
            engine.ExecuteQuery($"SELECT q.[Orders.CustomerID] FROM ({Join}) AS q").Rows.Select(r => (string)r[0]!).Order(StringComparer.Ordinal));
        Assert.Equal("a.ShipperID, a.CompanyName, a.Phone, b.ShipperID, b.CompanyName, b.Phone",
            string.Join(", ", Names(engine, "SELECT * FROM Shippers AS a INNER JOIN Shippers AS b ON a.ShipperID = b.ShipperID")));
    }

    [Theory]
    [InlineData("SELECT CompanyName AS X, Phone AS X FROM Shippers", "Duplicate output alias 'X'.")]
    [InlineData("SELECT ShipperID AS X, CompanyName AS x FROM Shippers", "Duplicate output alias 'x'.")]
    [InlineData("CREATE PROCEDURE P AS SELECT CompanyName AS X, Phone AS X FROM Shippers", "Duplicate output alias 'X'.")]
    public void Two_outputs_aliased_alike_are_refused(string sql, string message) =>
        Assert.Equal(message, Assert.Throws<SqlParseException>(() => Fresh().ExecuteNonQuery(sql)).Message);

    [Theory]
    [InlineData("SELECT * FROM Shippers GROUP BY ShipperID, CompanyName, Phone")]
    [InlineData("SELECT *, Count(*) FROM Shippers")]
    public void A_star_with_grouping_is_refused(string sql) =>
        Assert.Equal("Cannot group on fields selected with '*'.",
            Assert.Throws<InvalidOperationException>(() => Fresh().ExecuteQuery(sql)).Message);

    [Fact]
    public void A_stored_star_query_keeps_its_star_and_its_distinctrow()
    {
        QueryEngine engine = Fresh();
        engine.ExecuteNonQuery("CREATE PROCEDURE StarX AS SELECT *, ShipperID AS X FROM Shippers");
        engine.ExecuteNonQuery("CREATE PROCEDURE Whole AS TABLE Shippers");
        engine.ExecuteNonQuery("CREATE PROCEDURE ByRow AS SELECT DISTINCTROW Customers.CompanyName FROM Customers " +
                               "INNER JOIN Orders ON Customers.CustomerID = Orders.CustomerID WHERE Customers.Country = 'UK'");
        engine.ExecuteNonQuery("CREATE PROCEDURE ByRowTop AS SELECT DISTINCTROW TOP 50 PERCENT Customers.CompanyName FROM Customers " +
                               "INNER JOIN Orders ON Customers.CustomerID = Orders.CustomerID WHERE Customers.Country = 'UK' ORDER BY Customers.CompanyName");

        Assert.Equal("X, CompanyName, Phone", string.Join(", ", Names(engine, "EXECUTE StarX")));
        Assert.Equal("ShipperID, CompanyName, Phone", string.Join(", ", Names(engine, "EXECUTE Whole")));
        Assert.Equal(7, engine.ExecuteQuery("EXECUTE ByRow").Rows.Count());
        Assert.Equal(4, engine.ExecuteQuery("EXECUTE ByRowTop").Rows.Count());
    }
}
