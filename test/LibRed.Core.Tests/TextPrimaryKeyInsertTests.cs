using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

public class TextPrimaryKeyInsertTests
{
    private static OleDbConnection OpenOleDb(string path)
    {
        foreach (string p in new[] { "Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0" })
        {
            try { var c = new OleDbConnection($"Provider={p};Data Source={path};OLE DB Services=-4;"); c.Open(); return c; }
            catch (Exception ex) when (ex is OleDbException or InvalidOperationException) { }
        }
        throw new InvalidOperationException("No Microsoft.ACE.OLEDB provider available.");
    }

    [Fact]
    public void Insert_into_text_primary_key_table_is_seekable_by_access()
    {
        string path = Path.Combine(Path.GetTempPath(), $"libred-textpk-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            // Insert a new Customers row (text PK 'CustomerID') through LibRed.
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var table = db.OpenTable("Customers");
                var values = new object?[table.Definition.Columns.Count];
                values[table.Definition.FindColumn("CustomerID")!.Index] = "ZZZZZ";
                values[table.Definition.FindColumn("CompanyName")!.Index] = "ZZ Top Trading";
                table.Insert(values);
            }

            // LibRed reads the new row back via a table scan.
            using (var db = JetDatabase.Open(path))
            {
                var table = db.OpenTable("Customers");
                int idIdx = table.Definition.FindColumn("CustomerID")!.Index;
                int nameIdx = table.Definition.FindColumn("CompanyName")!.Index;
                var row = table.Rows().Single(r => (string?)r[idIdx] == "ZZZZZ");
                Assert.Equal("ZZ Top Trading", row[nameIdx]);
            }

            // Access finds the row via an indexed text primary-key seek — only possible because
            // the collation-encoded entry we wrote is in the PK B-tree and sorts correctly.
            using (var conn = OpenOleDb(path))
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT CompanyName FROM Customers WHERE CustomerID = 'ZZZZZ'";
                Assert.Equal("ZZ Top Trading", cmd.ExecuteScalar());
            }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
