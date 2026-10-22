using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class DeleteTests
{
    private static string Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "delete-");
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
        finally { TemporaryDatabase.Delete(path); }
    }

    // The Access designer writes a DELETE naming columns. Each form deletes the whole row of its one table, and over a
    // join a column names no table to delete from (all verified vs ACE on these rows).
    [Fact]
    public void Delete_naming_columns_deletes_whole_rows_of_its_one_table()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            foreach (string name in new[] { "del1", "del2", "del3", "del4" })
                e.ExecuteNonQuery($"INSERT INTO Shippers (CompanyName, Phone) VALUES ('{name}', '555')");
            e.ExecuteNonQuery("CREATE TABLE P (Id LONG, PName TEXT(10))");
            e.ExecuteNonQuery("CREATE TABLE C (Id LONG, PId LONG, CNote TEXT(10))");
            e.ExecuteNonQuery("INSERT INTO P VALUES (1, 'x'), (2, 'y')");
            e.ExecuteNonQuery("INSERT INTO C VALUES (10, 1, 'n10'), (11, 1, 'n11'), (20, 2, 'n20')");

            Assert.Equal(1, e.ExecuteNonQuery("DELETE Shippers.Phone FROM Shippers WHERE CompanyName = 'del1'"));
            Assert.Equal(1, e.ExecuteNonQuery("DELETE Shippers.Phone, Shippers.CompanyName FROM Shippers WHERE CompanyName = 'del2'"));
            Assert.Equal(1, e.ExecuteNonQuery("DELETE Phone FROM Shippers WHERE CompanyName = 'del3'"));
            Assert.Equal(["del4|555"],
                e.ExecuteQuery("SELECT CompanyName & '|' & Phone FROM Shippers WHERE Left(CompanyName, 3) = 'del'").Rows.Select(r => (string)r[0]!));

            Assert.Throws<InvalidOperationException>(() => e.ExecuteNonQuery("DELETE C.CNote FROM C INNER JOIN P ON C.PId = P.Id WHERE P.PName = 'x'"));
            Assert.Throws<InvalidOperationException>(() => e.ExecuteNonQuery("DELETE CNote FROM C INNER JOIN P ON C.PId = P.Id WHERE P.PName = 'x'"));
            Assert.Equal(2, e.ExecuteNonQuery("DELETE C.* FROM C INNER JOIN P ON C.PId = P.Id WHERE P.PName = 'x'"));

            // Stored, every item is kept and the query runs as written.
            e.ExecuteNonQuery("CREATE PROCEDURE D4 AS DELETE Shippers.Phone, Shippers.* FROM Shippers WHERE CompanyName = 'del4'");
            Assert.Equal("DELETE Shippers.Phone, Shippers.* FROM [Shippers] WHERE CompanyName = 'del4'", db.Catalog.FindQuery("D4")!.Sql);
            Assert.Equal(1, e.ExecuteNonQuery("EXECUTE D4"));
            Assert.Empty(e.ExecuteQuery("SELECT CompanyName FROM Shippers WHERE Left(CompanyName, 3) = 'del'").Rows);
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
