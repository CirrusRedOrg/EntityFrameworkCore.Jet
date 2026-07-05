using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class UpdateTests
{
    private static string Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"update-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        return path;
    }

    [Fact]
    public void Update_sets_values_with_where_and_current_value_expressions()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery("CREATE TABLE T (Id counter PRIMARY KEY, N long, S text(200), M memo)");
            for (int i = 0; i < 3; i++) e.ExecuteNonQuery($"INSERT INTO T (N, S, M) VALUES ({i}, 'short{i}', 'm{i}')");

            // SET value referencing the current value; WHERE targets one row. @@ROWCOUNT = 1.
            Assert.Equal(1, e.ExecuteNonQuery("UPDATE T SET N = N + 100 WHERE Id = 2"));
            Assert.Equal(101, Convert.ToInt32(e.ExecuteQuery("SELECT N FROM T WHERE Id = 2").Rows.Single()[0]));
            Assert.Equal(0, Convert.ToInt32(e.ExecuteQuery("SELECT N FROM T WHERE Id = 1").Rows.Single()[0])); // untouched

            // Multi-column SET, and a variable-length grow (row repacks in place).
            e.ExecuteNonQuery("UPDATE T SET S = 'a considerably longer string value that grows the row', N = 5 WHERE Id = 2");
            var r = e.ExecuteQuery("SELECT N, S FROM T WHERE Id = 2").Rows.Single();
            Assert.Equal(5, Convert.ToInt32(r[0]));
            Assert.Equal("a considerably longer string value that grows the row", r[1]);

            // Memo grows to an LVAL page.
            string big = new('x', 5000);
            e.ExecuteNonQuery($"UPDATE T SET M = '{big}' WHERE Id = 2");
            Assert.Equal(big, e.ExecuteQuery("SELECT M FROM T WHERE Id = 2").Rows.Single()[0]);

            // WHERE-less UPDATE hits every row; @@ROWCOUNT reflects it.
            Assert.Equal(3, e.ExecuteNonQuery("UPDATE T SET N = 0"));
            Assert.Equal(3, Convert.ToInt32(e.ExecuteQuery("SELECT @@ROWCOUNT").Rows.Single()[0]));
            Assert.All(e.ExecuteQuery("SELECT N FROM T").Rows, row => Assert.Equal(0, Convert.ToInt32(row[0])));
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void Update_of_an_indexed_column_throws_for_now()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery("CREATE TABLE T (Id long PRIMARY KEY, N long)");
            e.ExecuteNonQuery("INSERT INTO T (Id, N) VALUES (1, 10)");

            var ex = Assert.Throws<NotSupportedException>(() => e.ExecuteNonQuery("UPDATE T SET Id = 2 WHERE Id = 1"));
            Assert.Contains("indexed column", ex.Message);

            // Updating a non-indexed column on the same table is fine.
            Assert.Equal(1, e.ExecuteNonQuery("UPDATE T SET N = 20 WHERE Id = 1"));
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
