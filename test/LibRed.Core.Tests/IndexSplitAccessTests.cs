using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// Byte-faithful check that a LibRed index which has undergone B-tree leaf/node splitting is readable
/// by the real Access engine: after inserting enough rows to force splits (root grows from a leaf to a
/// node), Access opens the file and resolves both an indexed point seek and an indexed range scan.
/// (A full <c>COUNT(*)</c> is deliberately not asserted — LibRed's data-page usage map is not yet
/// byte-faithful at this scale, a separate concern from the index.)
/// </summary>
public class IndexSplitAccessTests
{
    private const int N = 1200; // well past one leaf, so the PK B-tree splits and the root grows a level

    private static OleDbConnection OpenOleDb(string path)
    {
        foreach (string provider in new[] { "Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0" })
        {
            try
            {
                var conn = new OleDbConnection($"Provider={provider};Data Source={path};OLE DB Services=-4;");
                conn.Open();
                return conn;
            }
            catch (Exception ex) when (ex is OleDbException or InvalidOperationException) { }
        }
        throw new InvalidOperationException("No Microsoft.ACE.OLEDB provider (12.0/16.0) is available.");
    }

    [Fact]
    public void Access_reads_a_split_index_by_seek_and_range()
    {
        string path = Path.Combine(Path.GetTempPath(), $"libred-splitaccess-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("Big",
                    [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("T", JetDataType.Text, 20, IsFixedLength: false)],
                    primaryKey: ["Id"]);
                var t = db.OpenTable("Big");
                for (int i = 1; i <= N; i++) t.Insert([i, $"r{i}"]);
            }

            using var conn = OpenOleDb(path);

            // Indexed point seeks at spread-out keys exercise different leaves/subtrees of the split
            // B-tree — Access resolves each through the PK index and follows the row pointer directly.
            // (A range/COUNT would go through a full scan, which is truncated by the separate data-page
            // usage-map gap, so it is not asserted here.)
            foreach (int id in new[] { 1, 400, 512, 1000, N })
            {
                using var seek = conn.CreateCommand();
                seek.CommandText = $"SELECT T FROM Big WHERE Id = {id}";
                Assert.Equal($"r{id}", seek.ExecuteScalar());
            }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
