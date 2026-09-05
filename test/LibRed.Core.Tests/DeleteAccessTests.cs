using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// LibRed soft-deletes a row (slot flagged, index entries removed, TDEF row count decremented) and Access
/// reads the table without it — the deleted row is gone from scans, seeks, and COUNT.
/// </summary>
public class DeleteAccessTests
{
    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    [Fact]
    public void Access_reads_a_libred_deleted_row_as_gone()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "del-ace-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("T",
                    [new("Id", JetDataType.Int32, 4, IsFixedLength: true), new("N", JetDataType.Int32, 4, IsFixedLength: true)],
                    primaryKey: ["Id"]);
                var table = db.OpenTable("T");
                for (int i = 1; i <= 5; i++) table.Insert([i, i * 10]);

                int idIdx = table.Definition.FindColumn("Id")!.Index;
                var pk = table.Definition.Indexes.First(i => i.IsPrimaryKey);
                (RowId id, object?[] values) = table.Rows().WithIds().First(x => Convert.ToInt32(x.Values[idIdx]) == 3);

                table.RemoveIndexEntry(pk, values, id);
                table.Delete(id);
            }

            using var conn = OpenOleDb(path);
            using (var c = conn.CreateCommand())
            { c.CommandText = "SELECT COUNT(*) FROM T"; Assert.Equal(4, Convert.ToInt32(c.ExecuteScalar())); }
            using (var c = conn.CreateCommand())
            { c.CommandText = "SELECT COUNT(*) FROM T WHERE Id = 3"; Assert.Equal(0, Convert.ToInt32(c.ExecuteScalar())); }
            using (var c = conn.CreateCommand())
            { c.CommandText = "SELECT SUM(N) FROM T"; Assert.Equal(120, Convert.ToInt32(c.ExecuteScalar())); } // 10+20+40+50 (30 gone)
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
