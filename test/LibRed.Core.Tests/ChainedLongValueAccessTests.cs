using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// A memo value larger than one LVAL page (&gt; 4076 bytes) is written as a <b>chain</b> of LVAL pages,
/// each chunk row beginning with a 4-byte pointer to the next. This checks a large value round-trips
/// through LibRed and that Access reads it back intact.
/// </summary>
public class ChainedLongValueAccessTests
{
    // 20 000 chars = 40 000 bytes → several chained LVAL pages (chunk data is 4072 bytes/page).
    private static readonly string Big =
        string.Concat(Enumerable.Range(0, 20_000).Select(i => (char)('A' + i % 26)));

    private static OleDbConnection OpenOleDb(string path)
    {
        Exception? last = null;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            foreach (string provider in new[] { "Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0" })
            {
                try
                {
                    var conn = new OleDbConnection($"Provider={provider};Data Source={path};OLE DB Services=-4;");
                    conn.Open();
                    return conn;
                }
                catch (Exception ex) when (ex is OleDbException or InvalidOperationException) { last = ex; }
            }
            Thread.Sleep(50);
        }
        throw new InvalidOperationException("No Microsoft.ACE.OLEDB provider opened the database.", last);
    }

    [Fact]
    public void A_large_memo_chains_across_lval_pages_and_round_trips()
    {
        string path = Path.Combine(Path.GetTempPath(), $"chained-lval-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("Big",
                    [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("M", JetDataType.Memo, 0, IsFixedLength: false)],
                    primaryKey: ["Id"]);
                db.OpenTable("Big").Insert([1, Big]);
            }

            // LibRed reads the chain back exactly.
            using (var db = JetDatabase.Open(path))
            {
                var table = db.OpenTable("Big");
                int m = table.Definition.Columns.First(c => c.Name == "M").Index;
                Assert.Equal(Big, (string)table.Rows().First()[m]!);
            }

            // Access reads the same value through its own long-value chain resolution.
            using var conn = OpenOleDb(path);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT M FROM Big WHERE Id = 1";
            Assert.Equal(Big, (string)cmd.ExecuteScalar()!);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
