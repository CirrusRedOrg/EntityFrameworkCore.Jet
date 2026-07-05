using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class DeleteTests
{
    private static string Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"delete-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        return path;
    }

    [Fact]
    public void Delete_removes_matching_rows_and_their_index_entries()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery("CREATE TABLE T (Id counter PRIMARY KEY, N long)");
            for (int i = 1; i <= 5; i++) e.ExecuteNonQuery($"INSERT INTO T (N) VALUES ({i * 10})");

            // Standard form.
            Assert.Equal(1, e.ExecuteNonQuery("DELETE FROM T WHERE Id = 3"));
            Assert.Empty(e.ExecuteQuery("SELECT N FROM T WHERE Id = 3").Rows); // gone (seek by the PK index)
            Assert.Equal(4, e.ExecuteQuery("SELECT Id FROM T").Rows.Count());

            // Access-specific `table.*` form, with a criteria over a non-key column.
            Assert.Equal(2, e.ExecuteNonQuery("DELETE T.* FROM T WHERE N > 30")); // deletes N=40, N=50
            Assert.Equal(new[] { 1, 2 }, e.ExecuteQuery("SELECT Id FROM T").Rows.Select(r => Convert.ToInt32(r[0])).OrderBy(x => x));

            // WHERE-less DELETE empties the table; @@ROWCOUNT reflects it.
            Assert.Equal(2, e.ExecuteNonQuery("DELETE FROM T"));
            Assert.Equal(2, Convert.ToInt32(e.ExecuteQuery("SELECT @@ROWCOUNT").Rows.Single()[0]));
            Assert.Empty(e.ExecuteQuery("SELECT Id FROM T").Rows);
            Assert.Equal(0, Convert.ToInt32(e.ExecuteQuery("SELECT COUNT(*) FROM T").Rows.Single()[0]));

            // The freed keys can be reused (index entries were removed): a new row inserts and reads back.
            e.ExecuteNonQuery("INSERT INTO T (N) VALUES (99)");
            Assert.Equal(99, Convert.ToInt32(e.ExecuteQuery("SELECT N FROM T").Rows.Single()[0]));
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
