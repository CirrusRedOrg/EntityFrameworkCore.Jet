using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// Byte-faithful check that a LibRed index which has undergone B-tree leaf/node splitting is readable
/// by the real Access engine: after inserting enough rows to force splits (root grows from a leaf to a
/// node), Access opens the file and resolves indexed point seeks, an indexed range, a full table scan,
/// and a leaf-chain <c>COUNT(*)</c>/<c>SUM</c> — all reaching every row.
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

            // Indexed point seeks at spread-out keys exercise different leaves/subtrees of the split B-tree.
            foreach (int id in new[] { 1, 400, 512, 1000, N })
            {
                using var seek = conn.CreateCommand();
                seek.CommandText = $"SELECT T FROM Big WHERE Id = {id}";
                Assert.Equal($"r{id}", seek.ExecuteScalar());
            }

            // A full table scan and index leaf-chain walk must both reach every row: COUNT(*) (leaf-chain),
            // a non-indexed scan, an indexed range, and SUM all account for all N rows. This catches a wrong
            // leaf next/prev pointer, where Access stops after the first leaf and silently loses the rest.
            long triangular = (long)N * (N + 1) / 2;
            foreach ((string sql, object expected) in new (string, object)[]
            {
                ("SELECT COUNT(*) FROM Big", N),
                ("SELECT COUNT(*) FROM Big WHERE T LIKE 'r%'", N),
                ("SELECT COUNT(*) FROM Big WHERE Id BETWEEN 300 AND 309", 10),
                ("SELECT SUM(Id) FROM Big", triangular),
            })
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = sql;
                Assert.Equal(Convert.ToInt64(expected), Convert.ToInt64(cmd.ExecuteScalar()));
            }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
