using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

public class TextPrimaryKeyInsertTests
{
    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    [Fact]
    public void Insert_into_text_primary_key_table_is_seekable_by_access()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "libred-textpk-");
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
        finally { TemporaryDatabase.Delete(path); }
    }
}
