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

    // Growing a row so it no longer fits its page relocates it (Access's overflow-forwarding: the slot
    // becomes a pointer to the row on another page; the row id is preserved). All rows stay readable.
    [Fact]
    public void Update_that_grows_a_row_past_its_page_relocates_it()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery("CREATE TABLE T (Id counter PRIMARY KEY, A text(255), B text(255), C text(255))");
            string mid = new('m', 80), big = new('X', 255);
            for (int i = 0; i < 7; i++) e.ExecuteNonQuery($"INSERT INTO T (A,B,C) VALUES ('{mid}','{mid}','{mid}')"); // ~fill a page

            Assert.Equal(1, e.ExecuteNonQuery($"UPDATE T SET A='{big}', B='{big}', C='{big}' WHERE Id = 3"));

            // Every row is still readable (the relocated one is followed through its forward pointer).
            var rows = e.ExecuteQuery("SELECT Id, A FROM T").Rows.ToList();
            Assert.Equal(7, rows.Count);
            Assert.Equal(Enumerable.Range(1, 7), rows.Select(r => Convert.ToInt32(r[0])).OrderBy(x => x));
            Assert.Equal(big, e.ExecuteQuery("SELECT A FROM T WHERE Id = 3").Rows.Single()[0]);           // the grown row
            Assert.Equal(mid, e.ExecuteQuery("SELECT A FROM T WHERE Id = 4").Rows.Single()[0]);           // a neighbour, untouched
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    // Updating an indexed column moves its index entry (old key removed, new key added), so a seek by the
    // new key finds the row and a seek by the old key doesn't.
    [Fact]
    public void Update_of_an_indexed_column_moves_the_index_entry()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery("CREATE TABLE T (Id long PRIMARY KEY, N long)");
            for (int i = 1; i <= 5; i++) e.ExecuteNonQuery($"INSERT INTO T (Id, N) VALUES ({i}, {i * 10})");

            Assert.Equal(1, e.ExecuteNonQuery("UPDATE T SET Id = 20 WHERE Id = 2"));

            // The row is now found under the new key, not the old one; N came along unchanged.
            Assert.Empty(e.ExecuteQuery("SELECT N FROM T WHERE Id = 2").Rows);
            Assert.Equal(20, Convert.ToInt32(e.ExecuteQuery("SELECT N FROM T WHERE Id = 20").Rows.Single()[0]));
            Assert.Equal(5, e.ExecuteQuery("SELECT Id FROM T").Rows.Count()); // still five rows

            // A composite move: change both the key and a non-key column together.
            Assert.Equal(1, e.ExecuteNonQuery("UPDATE T SET Id = 99, N = 990 WHERE Id = 20"));
            Assert.Equal(990, Convert.ToInt32(e.ExecuteQuery("SELECT N FROM T WHERE Id = 99").Rows.Single()[0]));
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
