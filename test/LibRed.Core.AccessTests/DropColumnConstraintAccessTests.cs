using System.Data.OleDb;
using Xunit;

namespace LibRed.Core.Tests;

// Ground truth for our DROP COLUMN guard: ACE REJECTS dropping a column that is part of an index/key or a
// relationship — it never cascades, you must drop the dependent first. (Our TableCreator.DropColumn mirrors
// this: it throws for an indexed/keyed column and for a column participating in a relationship.)
public class DropColumnConstraintAccessTests
{
    private static OleDbConnection Open(string path) => AceTestDatabase.Open(path);

    private static void Ok(OleDbConnection c, string sql)
    { using var m = c.CreateCommand(); m.CommandText = sql; m.ExecuteNonQuery(); }

    private static string Error(OleDbConnection c, string sql)
    {
        var ex = Assert.ThrowsAny<OleDbException>(() => { using var m = c.CreateCommand(); m.CommandText = sql; m.ExecuteNonQuery(); });
        return ex.Message;
    }

    [Fact]
    public void Access_rejects_dropping_an_indexed_or_related_column()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "dcc-");
        try
        {
            using var c = Open(path);
            Ok(c, "CREATE TABLE P (Id LONG PRIMARY KEY, Name TEXT(20))");
            Ok(c, "CREATE INDEX IX_P_Name ON P (Name)");
            Assert.Contains("index", Error(c, "ALTER TABLE P DROP COLUMN Name"), StringComparison.OrdinalIgnoreCase); // secondary index
            Assert.Contains("index", Error(c, "ALTER TABLE P DROP COLUMN Id"), StringComparison.OrdinalIgnoreCase);   // primary key

            Ok(c, "CREATE TABLE C (Id LONG PRIMARY KEY, Pid LONG, CONSTRAINT FK_C FOREIGN KEY (Pid) REFERENCES P (Id))");
            Assert.Contains("relationship", Error(c, "ALTER TABLE C DROP COLUMN Pid"), StringComparison.OrdinalIgnoreCase); // FK child
            Assert.Contains("relationship", Error(c, "ALTER TABLE P DROP COLUMN Id"), StringComparison.OrdinalIgnoreCase);  // FK parent
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
