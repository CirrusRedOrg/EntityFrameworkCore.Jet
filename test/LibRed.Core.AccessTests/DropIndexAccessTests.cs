using System.Data.OleDb;
using LibRed;
using Xunit;

namespace LibRed.Core.Tests;

// ACE's DROP INDEX removes the index's TDEF blocks and frees its B-tree root; it allows dropping plain,
// unique and even the primary-key index, but REJECTS an FK-backing index ("used in a relationship").
// LibRed mirrors this, and ACE opens+reads a LibRed-index-dropped file.
public class DropIndexAccessTests
{
    private static OleDbConnection Open(string path) => AceTestDatabase.Open(path);
    private static void Ace(string path, params string[] sqls)
    { using var c = Open(path); foreach (var s in sqls) { using var m = c.CreateCommand(); m.CommandText = s; m.ExecuteNonQuery(); } }

    [Fact]
    public void Access_reads_a_libred_index_dropped_table()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "dropix-lr-");
        try
        {
            // ACE creates the table + indexes + rows so the TDEF is authentic.
            Ace(path,
                "CREATE TABLE T (Id LONG PRIMARY KEY, Name TEXT(20), Code LONG)",
                "CREATE INDEX IX_Name ON T (Name)",
                "CREATE UNIQUE INDEX UX_Code ON T (Code)",
                "INSERT INTO T (Id, Name, Code) VALUES (1, 'a', 10)",
                "INSERT INTO T (Id, Name, Code) VALUES (2, 'b', 20)");

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                Assert.True(db.DropIndex("T", "IX_Name"));  // plain
                Assert.True(db.DropIndex("T", "UX_Code"));  // unique
                Assert.False(db.DropIndex("T", "Nope"));    // missing → false
                var pkName = db.Catalog.FindTable("T")!.Indexes.Single(i => i.IsPrimaryKey).Name;
                Assert.True(db.DropIndex("T", pkName));      // even the primary key is droppable
                Assert.Empty(db.Catalog.FindTable("T")!.Indexes);
            }

            // ACE opens the file without repair and reads the rows (indexes gone, data intact).
            using var conn = Open(path);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM T";
            Assert.Equal(2, Convert.ToInt32(cmd.ExecuteScalar()));
            using var cmd2 = conn.CreateCommand();
            cmd2.CommandText = "SELECT Name FROM T WHERE Id = 2";
            Assert.Equal("b", cmd2.ExecuteScalar());
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
