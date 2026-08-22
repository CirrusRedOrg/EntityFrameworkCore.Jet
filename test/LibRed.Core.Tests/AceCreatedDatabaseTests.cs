using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>A database LibRed creates from scratch (no DAO/ADOX) is opened, queried, and written by real Access.</summary>
public class AceCreatedDatabaseTests
{
    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    [Fact]
    public void Real_Access_opens_and_reads_a_libred_created_database()
    {
        string path = TemporaryDatabase.CreatePath("libred-created-");
        try
        {
            // Create the database, a user table, and a row entirely through LibRed — no Access, no DAO/ADOX.
            DatabaseCreator.CreateEmpty(path);
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("People", new[]
                {
                    new ColumnSpec("Id", JetDataType.Int32, 4, true),
                    new ColumnSpec("Name", JetDataType.Text, 100, false),
                }, primaryKey: new[] { "Id" });
                db.OpenTable("People").Insert([1, "Ada"]);
            }

            // Real Access (ACE OLE DB) opens the from-scratch file and reads LibRed's structure and data.
            using var conn = OpenOleDb(path);
            using (var count = conn.CreateCommand())
            { count.CommandText = "SELECT COUNT(*) FROM [People]"; Assert.Equal(1, Convert.ToInt32(count.ExecuteScalar())); }
            using (var sel = conn.CreateCommand())
            { sel.CommandText = "SELECT [Name] FROM [People] WHERE [Id]=1"; Assert.Equal("Ada", sel.ExecuteScalar()); }

            // And Access can WRITE into the file (needs a valid page-1 free map to allocate pages).
            using (var ins = conn.CreateCommand())
            { ins.CommandText = "INSERT INTO [People] ([Id],[Name]) VALUES (2,'Alan')"; Assert.Equal(1, ins.ExecuteNonQuery()); }
            using (var sel = conn.CreateCommand())
            { sel.CommandText = "SELECT [Name] FROM [People] WHERE [Id]=2"; Assert.Equal("Alan", sel.ExecuteScalar()); }
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
