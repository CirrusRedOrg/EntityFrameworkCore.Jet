using System.Data.OleDb;
using LibRed;
using Xunit;

namespace LibRed.Core.Tests;

// DROP VIEW / DROP PROCEDURE removes a type-5 query object's MSysObjects + MSysQueries + MSysACEs rows
// (verified vs ACE, which also treats the two statements interchangeably). After a LibRed drop, ACE opens
// the file, no longer sees the object, and still runs the surviving views.
public class DropViewAccessTests
{
    private static OleDbConnection Open(string path) => AceTestDatabase.Open(path);
    private static void Ace(string path, params string[] sqls)
    { using var c = Open(path); foreach (var s in sqls) { using var m = c.CreateCommand(); m.CommandText = s; m.ExecuteNonQuery(); } }

    [Fact]
    public void Access_reads_a_libred_view_dropped_file()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "dropview-lr-");
        try
        {
            // ACE creates the views so the MSysObjects/MSysQueries/MSysACEs rows are authentic.
            Ace(path,
                "CREATE VIEW Doomed AS SELECT ProductID FROM Products",
                "CREATE VIEW Keep AS SELECT CategoryID FROM Categories");

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                Assert.True(db.DropQueryObject("Doomed"));
                Assert.False(db.DropQueryObject("Nope"));                 // missing → false
                Assert.False(db.Catalog.Views.ContainsKey("Doomed"));     // gone
                Assert.True(db.Catalog.Views.ContainsKey("Keep"));        // survivor intact
            }

            using var conn = Open(path);
            // ACE no longer sees the dropped view but still runs the surviving one.
            Assert.ThrowsAny<OleDbException>(() =>
            { using var c = conn.CreateCommand(); c.CommandText = "SELECT * FROM Doomed"; c.ExecuteReader(); });
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Keep";
            Assert.True(Convert.ToInt32(cmd.ExecuteScalar()) > 0);
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
