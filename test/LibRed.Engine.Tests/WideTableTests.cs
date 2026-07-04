using System.Text;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class WideTableTests
{
    private static string Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"wide-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        return path;
    }

    // A table with enough columns that its TDEF exceeds one page (the owned-types/proxy shape) must build
    // and read back — previously threw "Destination is too short".
    [Fact]
    public void Create_table_with_a_multi_page_definition()
    {
        const int n = 110;
        string path = Fresh();
        try
        {
            var ddl = new StringBuilder("CREATE TABLE Wide (Id counter NOT NULL");
            for (int i = 0; i < n; i++) ddl.Append($", Col{i} longchar NULL");
            ddl.Append(", CONSTRAINT PK_Wide PRIMARY KEY (Id))");

            using (var db = JetDatabase.Open(path, readOnly: false))
                new QueryEngine(db).ExecuteNonQuery(ddl.ToString());

            using (var db = JetDatabase.Open(path, readOnly: false)) // fresh open: read the (multi-page) definition back
            {
                var e = new QueryEngine(db);
                var t = db.Catalog.Tables.First(x => x.Name == "Wide");
                Assert.Equal(n + 1, t.Columns.Count);                  // Id + n columns
                Assert.Equal("Col50", t.Columns.Single(c => c.Name == "Col50").Name); // names intact
                Assert.Contains(t.Indexes, ix => ix.IsPrimaryKey);

                // Data round-trips, including the far-end column.
                e.ExecuteNonQuery($"INSERT INTO Wide (Col0, Col{n - 1}) VALUES ('a', 'z')");
                var row = e.ExecuteQuery($"SELECT Col0, Col{n - 1} FROM Wide").Rows.First();
                Assert.Equal("a", row[0]);
                Assert.Equal("z", row[1]);
            }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
