using System.Data.OleDb;
using LibRed;
using Xunit;

namespace LibRed.Core.Tests;

// ACE's DROP TABLE removes the object's MSysObjects + MSysACEs rows and frees its pages (a later create
// reuses them). LibRed mirrors this: after a LibRed DROP TABLE, ACE opens the file, no longer sees the
// table, reads the other tables, and reuses the freed pages when creating a new table.
public class DropTableAccessTests
{
    private static OleDbConnection Open(string path) => AceTestDatabase.Open(path);
    private static void Ace(string path, params string[] sqls)
    { using var c = Open(path); foreach (var s in sqls) { using var m = c.CreateCommand(); m.CommandText = s; m.ExecuteNonQuery(); } }

    [Fact]
    public void Access_reads_a_libred_table_dropped_file_and_reuses_the_pages()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "droptab-lr-");
        try
        {
            // ACE creates the tables + rows so the catalog/pages are authentic.
            Ace(path,
                "CREATE TABLE Doomed (Id LONG PRIMARY KEY, Name TEXT(20))",
                "CREATE INDEX IX_Name ON Doomed (Name)",
                "CREATE TABLE Keep (Id LONG PRIMARY KEY)",
                "INSERT INTO Doomed (Id, Name) VALUES (1, 'x')",
                "INSERT INTO Keep (Id) VALUES (42)");

            long pagesBeforeDrop = new FileInfo(path).Length;

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                Assert.True(db.DropTable("Doomed"));
                Assert.False(db.DropTable("Nope"));               // missing → false
                Assert.Null(db.Catalog.FindTable("Doomed"));      // gone from the catalog
            }

            using (var conn = Open(path))
            {
                // ACE no longer sees the table, still reads the other one.
                using var c1 = conn.CreateCommand();
                c1.CommandText = "SELECT COUNT(*) FROM Keep";
                Assert.Equal(1, Convert.ToInt32(c1.ExecuteScalar()));
                Assert.ThrowsAny<OleDbException>(() =>
                { using var c = conn.CreateCommand(); c.CommandText = "SELECT * FROM Doomed"; c.ExecuteReader(); });

                // ACE reuses the freed pages: creating a similar table doesn't grow the file past its pre-drop size.
                using var c2 = conn.CreateCommand();
                c2.CommandText = "CREATE TABLE Fresh (Id LONG PRIMARY KEY, Name TEXT(20))";
                c2.ExecuteNonQuery();
            }
            Assert.True(new FileInfo(path).Length <= pagesBeforeDrop,
                $"file grew ({new FileInfo(path).Length} > {pagesBeforeDrop}) — freed pages were not reused");
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
