using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class AlterTableAddPrimaryKeyTests
{
    private static string Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"alterpk-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        return path;
    }

    // ALTER TABLE … ADD CONSTRAINT … PRIMARY KEY (a, b) adds a named, primary, unique index; it persists
    // and reads back with the right name and columns (Northwind's CcdTest shape).
    [Fact]
    public void Add_multi_column_primary_key()
    {
        string path = Fresh();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var e = new QueryEngine(db);
                e.ExecuteNonQuery("CREATE TABLE CcdTest (CustomerID TEXT(10), CustomerTypeID TEXT(10))");
                e.ExecuteNonQuery(
                    "ALTER TABLE CcdTest ADD CONSTRAINT `PK_CcdTest` " +
                    "PRIMARY KEY (`CustomerID`, `CustomerTypeID`)");
            }

            using (var db = JetDatabase.Open(path)) // fresh open: read from the file
            {
                var t = db.Catalog.Tables.First(x => x.Name == "CcdTest");
                var pk = Assert.Single(t.Indexes, ix => ix.IsPrimaryKey);
                Assert.Equal("PK_CcdTest", pk.Name);
                Assert.True(pk.IsUnique);
                Assert.Equal(["CustomerID", "CustomerTypeID"], pk.Columns.Select(c => c.Column.Name));
            }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
