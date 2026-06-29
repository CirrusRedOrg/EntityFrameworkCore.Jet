using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

public class CreateTableAccessTests
{
    private static string CopyToTemp()
    {
        string path = Path.Combine(Path.GetTempPath(), $"libred-createaccess-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        return path;
    }

    private static OleDbConnection OpenOleDb(string path)
    {
        foreach (string provider in new[] { "Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0" })
        {
            try
            {
                // "OLE DB Services=-4" disables connection pooling so the file is released on
                // Dispose and the temp copy can be deleted.
                var conn = new OleDbConnection($"Provider={provider};Data Source={path};OLE DB Services=-4;");
                conn.Open();
                return conn;
            }
            catch (Exception ex) when (ex is OleDbException or InvalidOperationException) { }
        }
        throw new InvalidOperationException("No Microsoft.ACE.OLEDB provider (12.0/16.0) is available.");
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { /* lock lingered; temp file, ignore */ }
    }

    [Fact]
    public void Access_lists_a_libred_created_table()
    {
        string path = CopyToTemp();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                db.CreateTable("Widgets", [
                    new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                    new ColumnSpec("Name", JetDataType.Text, 510, IsFixedLength: false),
                ]);

            using var conn = OpenOleDb(path);
            var tables = conn.GetSchema("Tables");
            var names = tables.Rows.Cast<System.Data.DataRow>()
                .Select(r => r["TABLE_NAME"]?.ToString())
                .ToList();

            Assert.Contains("Widgets", names);
        }
        finally { TryDelete(path); }
    }

    [Fact]
    public void Access_cannot_yet_open_the_table_by_name_pending_msysobjects_index()
    {
        // The MSysObjects row is complete enough that Access *enumerates* the table (test above),
        // but opening it by name resolves through MSysObjects' indexes — which we don't update
        // because the Name key is text and collation key-encoding isn't implemented. So a query
        // still fails with "cannot find the input table". This pins that boundary; flip the
        // assertion to a successful COUNT once MSysObjects index maintenance lands.
        string path = CopyToTemp();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                db.CreateTable("Widgets", [
                    new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                    new ColumnSpec("Name", JetDataType.Text, 510, IsFixedLength: false),
                ]);

            using var conn = OpenOleDb(path);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Widgets";
            var ex = Assert.Throws<OleDbException>(() => cmd.ExecuteScalar());
            Assert.Contains("Widgets", ex.Message);
        }
        finally { TryDelete(path); }
    }
}
