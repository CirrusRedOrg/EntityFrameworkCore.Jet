using System.Data;
using System.Data.OleDb;
using LibRed;
using LibRed.Storage;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// Ground truth: when LibRed creates a table whose primary key carries a name, Access names the primary-key
/// index after it and reports it through the Primary_Keys schema — so the name round-trips (this is what the
/// EF Core scaffolder reads). When unnamed, LibRed writes its stable "PrimaryKey" fallback, which Access reads
/// back unchanged (ACE creating an unnamed PK via SQL would instead generate a random "Index_<hex>").
/// </summary>
public class PrimaryKeyNameAccessTests
{
    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    private static string AcePrimaryKeyName(OleDbConnection conn, string table)
    {
        DataTable pk = conn.GetOleDbSchemaTable(OleDbSchemaGuid.Primary_Keys, [null, null, table])!;
        return (string)pk.Rows[0]["PK_NAME"];
    }

    [Theory]
    [InlineData("PK__T", "PK__T")]
    [InlineData(null, "PrimaryKey")]
    public void Access_reports_the_libred_primary_key_name(string? pkName, string expectedName)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "pkname-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                db.CreateTable("T",
                    [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true)],
                    primaryKey: ["Id"],
                    primaryKeyName: pkName);

            using var conn = OpenOleDb(path);
            Assert.Equal(expectedName, AcePrimaryKeyName(conn, "T"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
