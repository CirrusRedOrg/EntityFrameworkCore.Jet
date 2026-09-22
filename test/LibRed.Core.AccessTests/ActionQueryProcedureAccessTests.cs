using System.Data;
using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// Stored action queries (a CREATE PROCEDURE body that is not a SELECT) written by LibRed and then executed
/// by Access. A data-definition query (CREATE TABLE) stores the whole SQL in an <c>Attribute=1</c>/Flag 7
/// row; an append (INSERT … VALUES) query stores the target table (Attribute=1/Flag 3) plus one
/// <c>Attribute=6</c>/Flag 0x8000 row per column. Access recognises and runs both.
/// </summary>
[Collection(AceCollection.Name)]
public class ActionQueryProcedureAccessTests
{
    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    private static void Exec(OleDbConnection conn, string procName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = procName;
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void Access_runs_a_libred_written_make_table_and_append_procedure()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "action-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateActionQuery("MakeZ", new ActionQuerySpec(
                    ActionQueryKind.DataDefinition, DdlSql: "CREATE TABLE ZZLib (Id LONG, Nm TEXT(50))"));
                db.CreateActionQuery("AddShipper", new ActionQuerySpec(
                    ActionQueryKind.Append, TargetTable: "Shippers",
                    Values:
                    [
                        new AppendColumnSpec("CompanyName", "'LibRed Co'"),
                        new AppendColumnSpec("Phone", "'555-0100'"),
                    ]));
            }

            using var conn = OpenOleDb(path);

            // Data-definition query: running it creates the table.
            Exec(conn, "MakeZ");
            using (var c = conn.CreateCommand())
            {
                c.CommandText = "SELECT COUNT(*) FROM ZZLib";
                Assert.Equal(0, Convert.ToInt32(c.ExecuteScalar())); // table exists, empty
            }

            // Append query: running it inserts the row.
            int before;
            using (var c = conn.CreateCommand())
            {
                c.CommandText = "SELECT COUNT(*) FROM Shippers";
                before = Convert.ToInt32(c.ExecuteScalar());
            }
            Exec(conn, "AddShipper");
            using (var c = conn.CreateCommand())
            {
                c.CommandText = "SELECT Phone FROM Shippers WHERE CompanyName = 'LibRed Co'";
                Assert.Equal("555-0100", c.ExecuteScalar());
            }
            using (var c = conn.CreateCommand())
            {
                c.CommandText = "SELECT COUNT(*) FROM Shippers";
                Assert.Equal(before + 1, Convert.ToInt32(c.ExecuteScalar()));
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>
    /// An ACE-written query of each action kind, read back as the statement LibRed runs. Every kind keeps its
    /// sources, predicate and declared parameters where a SELECT keeps them — one <c>0x05</c> row per table,
    /// <c>0x07</c> per join, <c>0x08</c> for the WHERE — and differs only in what the <c>0x06</c> column rows
    /// mean: a SET assignment names its target in <c>Name2</c> (qualified, over a join), an append names the
    /// target column there and holds the value in <c>Expression</c>, and a DELETE's single row holds the
    /// verbatim <c>table.*</c> that Access writes when the query names one.
    /// </summary>
    [Theory]
    // UPDATE (kind 4): one column row per assignment.
    [InlineData(4, "UPDATE Customers SET ContactTitle = 'Changed' WHERE Country = 'UK'",
        "UPDATE [Customers] SET [ContactTitle] = 'Changed' WHERE Country = 'UK'")]
    [InlineData(4, "UPDATE Products SET UnitPrice = UnitPrice * 1.1, Discontinued = True WHERE CategoryID = 1",
        "UPDATE [Products] SET [UnitPrice] = UnitPrice * 1.1, [Discontinued] = True WHERE CategoryID = 1")]
    [InlineData(4, "UPDATE Orders INNER JOIN Customers ON Orders.CustomerID = Customers.CustomerID " +
                   "SET Orders.ShipCountry = Customers.Country WHERE Customers.Country = 'UK'",
        "UPDATE [Orders] INNER JOIN [Customers] ON Orders.CustomerID = Customers.CustomerID " +
        "SET [Orders].[ShipCountry] = Customers.Country WHERE Customers.Country = 'UK'")]
    // DELETE (kind 5): Access writes `DELETE * FROM` when the query names no columns, `DELETE t.* FROM` when
    // it does, and stores that `t.*` verbatim.
    [InlineData(5, "DELETE FROM Shippers WHERE CompanyName = 'Does not exist'",
        "DELETE * FROM [Shippers] WHERE CompanyName = 'Does not exist'")]
    [InlineData(5, "DELETE Shippers.* FROM Shippers WHERE Phone IS NULL",
        "DELETE Shippers.* FROM [Shippers] WHERE Phone IS NULL")]
    // Append (kind 3), from a SELECT rather than from VALUES.
    [InlineData(3, "INSERT INTO Shippers (CompanyName) SELECT ContactName FROM Customers WHERE Country = 'UK'",
        "INSERT INTO [Shippers] ([CompanyName]) SELECT ContactName FROM [Customers] WHERE Country = 'UK'")]
    // Make-table (kind 2): the target is on the action row rather than in the SQL.
    [InlineData(2, "SELECT ShipperID, CompanyName INTO [ShipperCopy] FROM Shippers WHERE ShipperID > 1",
        "SELECT ShipperID, CompanyName INTO [ShipperCopy] FROM [Shippers] WHERE ShipperID > 1")]
    public void Ace_written_action_query_is_read_back_as_runnable_sql(short kind, string body, string expected)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "action-kinds-");
        try
        {
            using (var conn = OpenOleDb(path)) CreateProcedure(conn, "P", body);

            using var db = JetDatabase.Open(path);
            StoredActionQuery query = db.Catalog.ActionQueries["P"];
            Assert.Equal(kind, ActionFlag(db, "P"));
            Assert.Null(query.UnsupportedReason);
            Assert.Equal(expected, query.Sql);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Ace_parameterized_update_is_read_back_with_its_parameters_clause()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "action-params-");
        try
        {
            using (var conn = OpenOleDb(path))
            using (var command = conn.CreateCommand())
            {
                command.CommandText =
                    "CREATE PROCEDURE [UpdateByCountry] (pTitle Text(50), pCountry Text(20)) AS " +
                    "UPDATE Customers SET ContactTitle = pTitle WHERE Country = pCountry";
                command.ExecuteNonQuery();
            }

            using var db = JetDatabase.Open(path);
            Assert.Equal(["pTitle", "pCountry"], db.Catalog.QueryParameters["UpdateByCountry"].Select(p => p.Name));

            StoredActionQuery query = db.Catalog.ActionQueries["UpdateByCountry"];
            Assert.Equal((short)4, ActionFlag(db, "UpdateByCountry"));
            // An action query declares its parameters exactly as a SELECT does, and is rebuilt with the same
            // leading clause — which is what makes the body's references to them parameters and not columns.
            // The declared lengths come back too: they are stored in the parameter row's LvExtra.
            Assert.Equal(
                "PARAMETERS [pTitle] TEXT(50), [pCountry] TEXT(20); " +
                "UPDATE [Customers] SET [ContactTitle] = pTitle WHERE Country = pCountry",
                query.Sql);
            Assert.Equal([50, 20], db.Catalog.QueryParameters["UpdateByCountry"].Select(p => p.Size));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void A_kind_libred_cannot_run_still_reports_which_kind_it_is()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "action-crosstab-");
        try
        {
            using (var conn = OpenOleDb(path))
                CreateProcedure(conn, "ByCountry",
                    "TRANSFORM Count(*) SELECT Country FROM Customers GROUP BY Country PIVOT City");

            using var db = JetDatabase.Open(path);
            StoredActionQuery query = db.Catalog.ActionQueries["ByCountry"];
            Assert.Null(query.Sql);
            // "Not supported" that doesn't say what it is leaves a caller no way to tell an unimplemented
            // feature from an unreadable file.
            Assert.Contains("Crosstab", query.UnsupportedReason!, StringComparison.OrdinalIgnoreCase);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Ace_having_view_is_not_misclassified_as_an_action_query()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "having-view-");
        try
        {
            using (var conn = OpenOleDb(path))
            using (var command = conn.CreateCommand())
            {
                command.CommandText =
                    "CREATE VIEW CountriesWithManyCustomers AS " +
                    "SELECT Country, COUNT(*) AS CustomerCount FROM Customers " +
                    "GROUP BY Country HAVING COUNT(*) > 3";
                command.ExecuteNonQuery();
            }

            using var db = JetDatabase.Open(path);
            Assert.False(db.Catalog.ActionQueries.ContainsKey("CountriesWithManyCustomers"));

            // A grouped SELECT is a view, and its HAVING rides along: the Attribute=0x0A row beside the
            // Attribute=9 GROUP BY ones. Reading the GROUP BY while silently dropping the HAVING would
            // reconstruct a query that returns every country — a wrong answer, not a missing feature — so the
            // clause has to survive into the rebuilt SQL.
            Assert.True(db.Catalog.Views.TryGetValue("CountriesWithManyCustomers", out string? sql));
            Assert.Contains("GROUP BY", sql, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("HAVING", sql, StringComparison.OrdinalIgnoreCase);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static void CreateProcedure(OleDbConnection connection, string name, string body)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"CREATE PROCEDURE [{name}] AS {body}";
        command.ExecuteNonQuery();
    }

    private static short ActionFlag(JetDatabase database, string queryName)
    {
        TableDef objectsDef = database.Catalog.FindTable("MSysObjects")!;
        int objectIdIndex = ColumnIndex(objectsDef, "Id");
        int objectNameIndex = ColumnIndex(objectsDef, "Name");
        int objectId = (int)database.OpenTable("MSysObjects")
            .Rows().Single(row => Equals(row[objectNameIndex], queryName))[objectIdIndex]!;

        TableDef queriesDef = database.Catalog.FindTable("MSysQueries")!;
        int queryObjectIdIndex = ColumnIndex(queriesDef, "ObjectId");
        int attributeIndex = ColumnIndex(queriesDef, "Attribute");
        int flagIndex = ColumnIndex(queriesDef, "Flag");
        object?[] action = database.OpenTable("MSysQueries")
            .Rows().Single(row => Equals(row[queryObjectIdIndex], objectId) && Equals(row[attributeIndex], (byte)1));
        return (short)action[flagIndex]!;
    }

    private static int ColumnIndex(TableDef definition, string name) =>
        definition.Columns.ToList().FindIndex(column => column.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}
