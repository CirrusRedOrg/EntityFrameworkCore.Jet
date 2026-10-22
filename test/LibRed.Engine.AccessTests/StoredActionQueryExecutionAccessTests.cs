using System.Data;
using System.Data.OleDb;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// Running a stored action query BY NAME, cross-checked against ACE running the same one. That LibRed rebuilds
// the statement is one thing; what decides whether the reconstruction is right is whether executing the query
// leaves the database in the state Access leaves it in — a SET assignment read from the wrong row column, or
// a join dropped from the FROM, changes which rows are touched rather than failing outright.
[Collection(AceCollection.Name)]
public class StoredActionQueryExecutionAccessTests : TempDatabaseTest
{
    private static string Copy() => TemporaryDatabase.CopyPath(
        Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "action-exec-");

    /// <summary>Has ACE store <paramref name="body"/> as a procedure, then runs it in ACE on one copy and in
    /// LibRed on another, comparing what <paramref name="verify"/> reports afterwards.</summary>
    private static void AssertSameAsAce(string body, string verify)
    {
        string acePath = Copy(), libRedPath = Copy();
        try
        {
            object? aceResult;
            foreach (string path in new[] { acePath, libRedPath })
                using (var connection = AceTestDatabase.Open(path))
                {
                    using var create = connection.CreateCommand();
                    create.CommandText = $"CREATE PROCEDURE [P] AS {body}";
                    create.ExecuteNonQuery();
                }

            using (var connection = AceTestDatabase.Open(acePath))
            {
                using (var run = connection.CreateCommand())
                {
                    run.CommandText = "P";
                    run.CommandType = CommandType.StoredProcedure;
                    run.ExecuteNonQuery();
                }
                using var check = connection.CreateCommand();
                check.CommandText = verify;
                aceResult = check.ExecuteScalar();
            }

            using var db = TemporaryDatabase.OpenTracked(libRedPath, readOnly: false);
            var engine = new QueryEngine(db);
            engine.ExecuteNonQuery("EXECUTE [P]");
            object? ourResult = engine.ExecuteQuery(verify).Rows.Single()[0];

            Assert.Equal(Convert.ToString(aceResult), Convert.ToString(ourResult));
        }
        finally
        {
            TemporaryDatabase.Delete(acePath);
            TemporaryDatabase.Delete(libRedPath);
        }
    }

    [Fact]
    public void Update_query_touches_the_rows_ace_touches() =>
        AssertSameAsAce(
            "UPDATE Customers SET ContactTitle = 'Changed' WHERE Country = 'UK'",
            "SELECT COUNT(*) FROM Customers WHERE ContactTitle = 'Changed'");

    [Fact]
    public void Update_query_over_a_join_reads_the_other_table()
    {
        // The join lives in rows of its own, so losing it would silently update every order instead of the
        // seven UK ones.
        AssertSameAsAce(
            "UPDATE Orders INNER JOIN Customers ON Orders.CustomerID = Customers.CustomerID " +
            "SET Orders.ShipCountry = 'Checked' WHERE Customers.Country = 'UK'",
            "SELECT COUNT(*) FROM Orders WHERE ShipCountry = 'Checked'");
    }

    // Deleted from the child table: a customer or an order has dependent rows, and both engines refuse to
    // orphan them — which would be a test of referential integrity rather than of the stored query.
    [Fact]
    public void Delete_query_removes_the_rows_ace_removes() =>
        AssertSameAsAce(
            "DELETE FROM [Order Details] WHERE OrderID = 10248",
            "SELECT COUNT(*) FROM [Order Details]");

    [Fact]
    public void Delete_query_written_with_a_table_star_target_removes_the_same_rows() =>
        AssertSameAsAce(
            "DELETE [Order Details].* FROM [Order Details] WHERE OrderID = 10249",
            "SELECT COUNT(*) FROM [Order Details]");

    [Fact]
    public void Append_query_from_a_select_inserts_the_rows_ace_inserts() =>
        AssertSameAsAce(
            "INSERT INTO Shippers (CompanyName) SELECT ContactName FROM Customers WHERE Country = 'UK'",
            "SELECT COUNT(*) FROM Shippers");

    /// <summary>Without a column list ACE fills the target columns by the SELECT's output names, not their places,
    /// and stores those names rather than target columns — so aliases that swap two columns swap them here too.</summary>
    [Theory]
    [InlineData("INSERT INTO Shippers SELECT CompanyName, Phone FROM Suppliers WHERE SupplierID < 4")]
    [InlineData("INSERT INTO Shippers SELECT ContactTitle AS Phone, Phone AS CompanyName FROM Suppliers WHERE SupplierID < 4")]
    [InlineData("INSERT INTO Shippers SELECT Suppliers.CompanyName FROM Suppliers WHERE SupplierID < 4")]
    [InlineData("INSERT INTO Shippers SELECT * FROM (SELECT CompanyName, Phone FROM Suppliers WHERE SupplierID < 4) AS s")]
    public void Append_query_without_a_column_list_fills_the_columns_ace_fills(string body) =>
        AssertSameAsAce(body, "SELECT Max(CompanyName & '|' & Phone) FROM Shippers WHERE ShipperID > 3");

    /// <summary>A stored append or make-table query's SELECT runs as ACE stored it — its VALUES without a column list,
    /// a SELECT with no FROM, and its ORDER BY, GROUP BY and HAVING, which decide which rows a TOP or a group keeps.</summary>
    [Theory]
    [InlineData("INSERT INTO Shippers VALUES (50, 'Fifty', '555')",
        "SELECT Max(ShipperID & '|' & CompanyName & '|' & Phone) FROM Shippers WHERE ShipperID > 3")]
    [InlineData("INSERT INTO Shippers SELECT 'NoFrom' AS CompanyName", "SELECT Max(CompanyName) FROM Shippers WHERE ShipperID > 3")]
    [InlineData("INSERT INTO Shippers (CompanyName) SELECT TOP 1 CompanyName FROM Suppliers ORDER BY SupplierID DESC",
        "SELECT Max(CompanyName) FROM Shippers WHERE ShipperID > 3")]
    [InlineData("INSERT INTO Shippers (CompanyName) SELECT Country FROM Customers GROUP BY Country HAVING Count(*) > 10",
        "SELECT Count(*) & '|' & Max(CompanyName) FROM Shippers WHERE ShipperID > 3")]
    [InlineData("SELECT TOP 1 CompanyName INTO TopCopy FROM Customers ORDER BY CustomerID DESC", "SELECT Max(CompanyName) FROM TopCopy")]
    [InlineData("SELECT Country, Count(*) AS N INTO CountryCount FROM Customers GROUP BY Country HAVING Count(*) > 5",
        "SELECT Count(*) & '|' & Sum(N) FROM CountryCount")]
    public void An_ace_written_append_or_make_table_runs_its_whole_select(string body, string verify) =>
        AssertSameAsAce(body, verify);

    /// <summary>A stored DELETE that names columns, as the Access designer writes one, deletes whole rows of its one
    /// table; and an UPDATE whose target ACE stores bracketed sets that column.</summary>
    [Theory]
    [InlineData("DELETE Customers.Phone FROM Customers WHERE CustomerID NOT IN (SELECT CustomerID FROM Orders)",
        "SELECT Count(*) FROM Customers")]
    [InlineData("DELETE Customers.Phone, Customers.Fax FROM Customers WHERE CustomerID NOT IN (SELECT CustomerID FROM Orders)",
        "SELECT Count(*) FROM Customers")]
    [InlineData("DELETE Phone FROM Customers WHERE CustomerID NOT IN (SELECT CustomerID FROM Orders)", "SELECT Count(*) FROM Customers")]
    [InlineData("UPDATE [Order Details] SET [Order Details].Discount = 0.5 WHERE OrderID = 10248",
        "SELECT Sum(Discount) FROM [Order Details] WHERE OrderID = 10248")]
    public void An_ace_written_delete_naming_columns_or_bracketed_update_runs_as_ace_runs_it(string body, string verify) =>
        AssertSameAsAce(body, verify);

    [Fact]
    public void Make_table_query_writes_the_table_ace_writes() =>
        AssertSameAsAce(
            "SELECT ShipperID, CompanyName INTO [ShipperCopy] FROM Shippers WHERE ShipperID > 1",
            "SELECT COUNT(*) FROM ShipperCopy");

    [Fact]
    public void A_parameterized_action_query_runs_with_the_values_supplied_to_it()
    {
        string path = Copy();
        try
        {
            using (var connection = AceTestDatabase.Open(path))
            {
                using var create = connection.CreateCommand();
                create.CommandText =
                    "CREATE PROCEDURE [UpdateByCountry] (pTitle Text(50), pCountry Text(20)) AS " +
                    "UPDATE Customers SET ContactTitle = pTitle WHERE Country = pCountry";
                create.ExecuteNonQuery();
            }

            using var db = TemporaryDatabase.OpenTracked(path, readOnly: false);
            var engine = new QueryEngine(db);

            // Positional arguments bind to the parameters in declaration order, as EXECUTE does everywhere.
            // The title is one no Northwind customer already holds, so every row counted below was set here.
            Assert.Equal(7, engine.ExecuteNonQuery("EXECUTE [UpdateByCountry] 'Set by parameter', 'UK'"));
            Assert.Equal(
                7,
                Convert.ToInt32(engine.ExecuteQuery(
                    "SELECT COUNT(*) FROM Customers WHERE ContactTitle = 'Set by parameter' AND Country = 'UK'")
                    .Rows.Single()[0]));

            // Nothing else was touched: the second parameter is the predicate, not a constant folded into it.
            Assert.Equal(
                0,
                Convert.ToInt32(engine.ExecuteQuery(
                    "SELECT COUNT(*) FROM Customers WHERE ContactTitle = 'Set by parameter' AND Country <> 'UK'")
                    .Rows.Single()[0]));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void An_ace_written_crosstab_runs_by_name_as_ace_runs_it()
    {
        string path = Copy();
        try
        {
            var aceRows = new List<string>();
            using (var connection = AceTestDatabase.Open(path))
            {
                using var create = connection.CreateCommand();
                create.CommandText =
                    "CREATE PROCEDURE [ByCountry] AS " +
                    "TRANSFORM Count(*) SELECT Country FROM Customers GROUP BY Country PIVOT City";
                create.ExecuteNonQuery();

                using var run = connection.CreateCommand();
                run.CommandText = "ByCountry";
                run.CommandType = System.Data.CommandType.StoredProcedure;
                using var reader = run.ExecuteReader();
                aceRows.Add(string.Join(", ", Enumerable.Range(0, reader.FieldCount).Select(reader.GetName)));
                var values = new object[reader.FieldCount];
                while (reader.Read())
                {
                    reader.GetValues(values);
                    aceRows.Add(string.Join(" | ", values.Select(v => v is DBNull ? "NULL" : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture))));
                }
            }

            using var db = TemporaryDatabase.OpenTracked(path, readOnly: false);
            var result = new QueryEngine(db).ExecuteQuery("EXECUTE [ByCountry]");
            Assert.Equal(aceRows, [string.Join(", ", result.ColumnNames),
                .. result.Rows.Select(r => string.Join(" | ", r.Select(v => v is null ? "NULL" : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture))))]);
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
