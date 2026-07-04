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
        string path = Path.Combine(Path.GetTempPath(), $"action-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
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
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
