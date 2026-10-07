using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// LibRed soft-deletes a row (slot flagged, index entries removed, TDEF row count decremented) and Access
/// reads the table without it — the deleted row is gone from scans, seeks, and COUNT.
/// </summary>
[Collection(AceCollection.Name)]
public class DeleteAccessTests(ITestOutputHelper output) : TempDatabaseTest
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

    // What a bulk delete gives back. Deleting enough rows to empty whole pages exercises the three kinds of
    // page a delete retires — data pages emptied of every row, the index leaf their keys filled, and the
    // table's own usage maps — and page accounting is only measurable one way: the same delete through both
    // engines over two copies of one ACE-authored file, compared byte for byte. Difference skips the index
    // pages themselves (§10.4a), so the tree's shape is checked separately in IndexSplitAccessTests; what is
    // compared here is the data pages, the usage-map holders, the global map and the TDEF.
    [Fact]
    public void A_delete_that_empties_pages_gives_back_what_ace_gives_back()
    {
        const int N = 1200; // past one leaf, so the PK B-tree splits and the delete also empties a leaf

        string start = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "del-reclaim-start-");
        using (OleDbConnection connection = OpenOleDb(start))
        {
            Exec(connection, "CREATE TABLE Shape (Id LONG CONSTRAINT pk PRIMARY KEY, T TEXT(60))");
            for (int i = 1; i <= N; i++)
                Exec(connection, $"INSERT INTO Shape (Id, T) VALUES ({i}, '{new string('x', 50)}')");
        }

        string ace = TemporaryDatabase.CopyPath(start, "del-reclaim-ace-");
        using (OleDbConnection connection = OpenOleDb(ace))
            Exec(connection, "DELETE FROM Shape WHERE Id < 700");

        string libred = TemporaryDatabase.CopyPath(start, "del-reclaim-lib-");
        using (var database = JetDatabase.Open(libred, readOnly: false))
        {
            var table = database.OpenTable("Shape");
            foreach ((RowId id, object?[] values) in table.Rows().WithIds().ToList())
            {
                if (Convert.ToInt32(values[0]) >= 700) continue;
                table.Delete(id);
            }
        }

        string difference = DropTableParityAccessTests.Difference(ace, libred);
        output.WriteLine(difference);
        Assert.Equal("", difference);
    }

    // Every row gone, not just most: the table still keeps its first data page, and every other page it owned
    // goes back. This is the rule WriteReclaimedPage's first-page exception exists for, measured on its own so
    // a change to it fails here rather than only inside a whole-file diff.
    [Fact]
    public void Deleting_every_row_keeps_only_the_tables_first_page()
    {
        const int N = 1200;
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "del-all-");
        using (OleDbConnection connection = OpenOleDb(path))
        {
            Exec(connection, "CREATE TABLE Shape (Id LONG CONSTRAINT pk PRIMARY KEY, T TEXT(60))");
            for (int i = 1; i <= N; i++)
                Exec(connection, $"INSERT INTO Shape (Id, T) VALUES ({i}, '{new string('x', 50)}')");
        }

        int[] owned;
        using (var database = JetDatabase.Open(path, readOnly: false))
        {
            var table = database.OpenTable("Shape");
            owned = [.. new UsageMap(database.Channel, table.Definition).DataPages()];
            foreach ((RowId id, object?[] values) in table.Rows().WithIds().ToList())
            {
                table.Delete(id);
            }
        }

        Assert.True(owned.Length > 30, $"expected a multi-page table, got {owned.Length}");
        using var reopened = JetDatabase.Open(path);
        int[] left = [.. new UsageMap(reopened.Channel, reopened.OpenTable("Shape").Definition).DataPages()];
        Assert.Equal([owned[0]], left);
        Assert.Equal(PageType.DataPage, PageHeader.ReadType(reopened.Channel.ReadPage(owned[0]).Span));
        foreach (int page in owned[1..])
            Assert.Equal(PageType.ReleasedDataPage, PageHeader.ReadType(reopened.Channel.ReadPage(page).Span));
    }

    private static void Exec(OleDbConnection connection, string sql)
    {
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}