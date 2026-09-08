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

    // An INSERT ... SELECT stored query (written by ACE) is read back but classified unsupported: LibRed
    // reconstructs no executable SQL for it, only a reason ("throw on the rest").
    [Fact]
    public void Insert_select_stored_query_is_read_back_as_unsupported()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "action-sel-");
        try
        {
            using (var conn = OpenOleDb(path))
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText =
                    "CREATE PROCEDURE CopyUkShippers AS " +
                    "INSERT INTO Shippers (CompanyName) SELECT ContactName FROM Customers WHERE Country = 'UK'";
                cmd.ExecuteNonQuery();
            }

            using var db = JetDatabase.Open(path);
            StoredActionQuery q = db.Catalog.ActionQueries["CopyUkShippers"];
            Assert.Null(q.Sql);
            Assert.Contains("INSERT", q.UnsupportedReason!, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("SELECT", q.UnsupportedReason!, StringComparison.OrdinalIgnoreCase);
            Assert.Equal((short)3, ActionFlag(db, "CopyUkShippers"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Ace_parameterized_update_retains_parameter_order_while_remaining_explicitly_unsupported()
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
            Assert.Equal(["pTitle", "pCountry"], db.Catalog.QueryParameters["UpdateByCountry"]);
            StoredActionQuery query = db.Catalog.ActionQueries["UpdateByCountry"];
            Assert.Null(query.Sql);
            Assert.Contains("UPDATE", query.UnsupportedReason!, StringComparison.OrdinalIgnoreCase);
            Assert.Equal((short)4, ActionFlag(db, "UpdateByCountry"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Ace_update_and_delete_procedures_are_retained_with_their_exact_action_kinds()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "action-kinds-");
        try
        {
            using (var conn = OpenOleDb(path))
            {
                CreateProcedure(conn, "UpdateUkTitles",
                    "UPDATE Customers SET ContactTitle = 'Changed' WHERE Country = 'UK'");
                CreateProcedure(conn, "DeleteNoShipper",
                    "DELETE FROM Shippers WHERE CompanyName = 'Does not exist'");
            }

            using var db = JetDatabase.Open(path);
            StoredActionQuery update = db.Catalog.ActionQueries["UpdateUkTitles"];
            StoredActionQuery delete = db.Catalog.ActionQueries["DeleteNoShipper"];
            Assert.Equal((short)4, ActionFlag(db, "UpdateUkTitles"));
            Assert.Equal((short)5, ActionFlag(db, "DeleteNoShipper"));
            Assert.Null(update.Sql);
            Assert.Contains("UPDATE", update.UnsupportedReason!, StringComparison.OrdinalIgnoreCase);
            Assert.Null(delete.Sql);
            Assert.Contains("DELETE", delete.UnsupportedReason!, StringComparison.OrdinalIgnoreCase);
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
            // Complex ACE-authored SELECT queries are deliberately omitted until their MSysQueries attributes
            // can be reconstructed losslessly; they must not appear as a different executable query shape.
            Assert.False(db.Catalog.Views.ContainsKey("CountriesWithManyCustomers"));
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
