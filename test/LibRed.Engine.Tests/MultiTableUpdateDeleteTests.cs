using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// Jet/ACE multi-table UPDATE/DELETE over a join: SET may touch columns in more than one joined table, and
// DELETE target.* names which table to remove rows from. Semantics verified against ACE (see the probe):
// a "one"-side row is updated once per matched join row, so a self-referencing SET accumulates.
public class MultiTableUpdateDeleteTests
{
    private static string Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "mtud-");
        return path;
    }

    private static QueryEngine Seed(JetDatabase db)
    {
        var e = new QueryEngine(db);
        e.ExecuteNonQuery("CREATE TABLE P (Id long PRIMARY KEY, PName text(20), Hits long)");
        e.ExecuteNonQuery("CREATE TABLE C (Id long PRIMARY KEY, ParentId long, CName text(20))");
        e.ExecuteNonQuery("INSERT INTO P (Id, PName, Hits) VALUES (1, 'p1', 0)");
        e.ExecuteNonQuery("INSERT INTO P (Id, PName, Hits) VALUES (2, 'p2', 0)");
        e.ExecuteNonQuery("INSERT INTO C (Id, ParentId, CName) VALUES (10, 1, 'c10')");
        e.ExecuteNonQuery("INSERT INTO C (Id, ParentId, CName) VALUES (11, 1, 'c11')"); // P#1 has TWO children
        e.ExecuteNonQuery("INSERT INTO C (Id, ParentId, CName) VALUES (12, 2, 'c12')");
        return e;
    }

    [Fact]
    public void Multi_table_update_touches_both_tables_and_accumulates_on_the_one_side()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = Seed(db);

            int affected = e.ExecuteNonQuery(
                "UPDATE P INNER JOIN C ON P.Id = C.ParentId " +
                "SET P.PName = 'hit', P.Hits = P.Hits + 1, C.CName = 'child' WHERE P.Id = 1");
            Assert.Equal(2, affected); // one join row per child of P#1

            var p1 = e.ExecuteQuery("SELECT PName, Hits FROM P WHERE Id = 1").Rows.Single();
            Assert.Equal("hit", p1[0]);
            Assert.Equal(2, Convert.ToInt32(p1[1]));          // P#1 incremented once per matched child → 2
            Assert.Equal(0, Convert.ToInt32(e.ExecuteQuery("SELECT Hits FROM P WHERE Id = 2").Rows.Single()[0])); // untouched
            Assert.All(e.ExecuteQuery("SELECT CName FROM C WHERE ParentId = 1").Rows, r => Assert.Equal("child", r[0]));
            Assert.Equal("c12", e.ExecuteQuery("SELECT CName FROM C WHERE Id = 12").Rows.Single()[0]);            // untouched
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Every SET reads the row as it was before any of them (verified vs ACE), in one table and across joined ones.
    [Theory]
    [InlineData("UPDATE S SET X = X + 5, Y = X", "6|1")]
    [InlineData("UPDATE S SET X = Y, Y = X", "2|1")]
    [InlineData("UPDATE S SET Y = X, X = Y + 10", "12|1")]
    public void Every_set_reads_the_original_row(string update, string expected)
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery("CREATE TABLE S (X long, Y long)");
            e.ExecuteNonQuery("INSERT INTO S (X, Y) VALUES (1, 2)");

            e.ExecuteNonQuery(update);

            object?[] row = e.ExecuteQuery("SELECT X, Y FROM S").Rows.Single();
            Assert.Equal(expected, $"{row[0]}|{row[1]}");
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void A_set_on_one_joined_table_reads_the_other_before_its_set()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = Seed(db);

            e.ExecuteNonQuery("UPDATE P INNER JOIN C ON P.Id = C.ParentId SET C.CName = P.PName, P.PName = C.CName WHERE C.Id = 12");

            Assert.Equal("c12", e.ExecuteQuery("SELECT PName FROM P WHERE Id = 2").Rows.Single()[0]);
            Assert.Equal("p2", e.ExecuteQuery("SELECT CName FROM C WHERE Id = 12").Rows.Single()[0]);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // UPDATE takes a comma list of tables, as FROM does, joined or not (verified vs ACE); the row count is the
    // number of joined rows.
    [Theory]
    [InlineData("UPDATE P, C SET P.Hits = C.Id WHERE P.Id = C.ParentId AND C.Id <> 11", 2, "10|12")]
    [InlineData("UPDATE P, C SET P.Hits = 7", 6, "7|7")]
    [InlineData("UPDATE P, C INNER JOIN P AS Q ON C.ParentId = Q.Id SET P.Hits = C.Id WHERE P.Id = Q.Id AND C.Id <> 10", 2, "11|12")]
    [InlineData("UPDATE P INNER JOIN (C INNER JOIN P AS Q ON C.ParentId = Q.Id) ON P.Id = Q.Id SET P.Hits = P.Hits + 1", 3, "2|1")]
    public void Update_takes_a_comma_list_of_tables(string update, int affected, string hits)
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = Seed(db);

            Assert.Equal(affected, e.ExecuteNonQuery(update));
            Assert.Equal(hits, string.Join("|", e.ExecuteQuery("SELECT Hits FROM P ORDER BY Id").Rows.Select(r => r[0])));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Theory]
    [InlineData("UPDATE P SET Hits = 1, Hits = 2")]
    [InlineData("UPDATE P SET P.Hits = 1, hits = 2")]
    [InlineData("UPDATE P INNER JOIN C ON P.Id = C.ParentId SET P.Hits = 1, P.Hits = 2")]
    public void A_column_set_twice_is_an_error(string update)
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = Seed(db);

            var error = Assert.Throws<InvalidOperationException>(() => e.ExecuteNonQuery(update));
            Assert.Contains("Duplicate output destination", error.Message);
            Assert.Equal(0, Convert.ToInt32(e.ExecuteQuery("SELECT Hits FROM P WHERE Id = 1").Rows.Single()[0]));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Multi_table_delete_removes_only_the_targeted_table()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = Seed(db);

            // Delete the children of parents named 'p1' — only C rows go, P stays.
            int affected = e.ExecuteNonQuery("DELETE C.* FROM C INNER JOIN P ON C.ParentId = P.Id WHERE P.PName = 'p1'");
            Assert.Equal(2, affected);

            Assert.Equal(new[] { 12 }, e.ExecuteQuery("SELECT Id FROM C").Rows.Select(r => Convert.ToInt32(r[0])).OrderBy(x => x));
            Assert.Equal(2, e.ExecuteQuery("SELECT Id FROM P").Rows.Count()); // both parents remain
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Three tables where A and B share Ids 1 and 2, B and C share 4 and C has 1: A3, B4 and C4's partners are missing.
    private static QueryEngine SeedThree(JetDatabase db)
    {
        var e = new QueryEngine(db);
        e.ExecuteNonQuery("CREATE TABLE A (Id long, X long)");
        e.ExecuteNonQuery("CREATE TABLE B (K counter, Id long, V long, D long DEFAULT 9)");
        e.ExecuteNonQuery("CREATE TABLE C (Id long, W long)");
        foreach (int id in (int[])[1, 2, 3])
            e.ExecuteNonQuery($"INSERT INTO A (Id, X) VALUES ({id}, {id * 10})");
        foreach (int id in (int[])[1, 2, 4])
            e.ExecuteNonQuery($"INSERT INTO B (Id, V) VALUES ({id}, 0)");
        foreach (int id in (int[])[1, 4])
            e.ExecuteNonQuery($"INSERT INTO C (Id, W) VALUES ({id}, 0)");
        return e;
    }

    private static string Rows(QueryEngine e, string sql) =>
        string.Join("|", e.ExecuteQuery(sql).Rows.Select(r => string.Join("/", r)));

    // A SET on the side of an outer join with no matching row writes a new row there, one per joined row, with the
    // SET values and the table's defaults and AutoNumber (verified vs ACE).
    [Fact]
    public void A_set_on_the_unmatched_side_of_an_outer_join_adds_a_row()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = SeedThree(db);

            Assert.Equal(3, e.ExecuteNonQuery("UPDATE A LEFT JOIN B ON A.Id = B.Id SET A.X = 1, B.V = A.X + 100"));
            Assert.Equal("1/1/110/9|2/2/120/9|3/4/0/9|4//130/9", Rows(e, "SELECT K, Id, V, D FROM B ORDER BY K"));
            Assert.Equal("1/1|2/1|3/1", Rows(e, "SELECT Id, X FROM A ORDER BY Id"));

            // Null values still make a row: B's rows with Id 4 and Null have no A.
            Assert.Equal(4, e.ExecuteNonQuery("UPDATE A RIGHT JOIN B ON A.Id = B.Id SET A.X = NULL"));
            Assert.Equal("/|/|1/|2/|3/1", Rows(e, "SELECT Id, X FROM A ORDER BY Id, X"));
            Assert.Equal(5, e.ExecuteNonQuery("UPDATE A LEFT JOIN C ON A.Id = C.Id SET C.W = NULL"));
            Assert.Equal("/|/|/|/|1/", Rows(e, "SELECT Id, W FROM C WHERE W IS NULL ORDER BY Id"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void A_new_row_that_breaks_a_rule_fails_the_whole_update()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = SeedThree(db);
            e.ExecuteNonQuery("CREATE TABLE R (Id long, V long, Req long NOT NULL)");

            Assert.Throws<InvalidOperationException>(() =>
                e.ExecuteNonQuery("UPDATE A LEFT JOIN R ON A.Id = R.Id SET A.X = 99, R.V = 3"));
            Assert.Equal("1/10|2/20|3/30", Rows(e, "SELECT Id, X FROM A ORDER BY Id"));
            Assert.Equal("0", Rows(e, "SELECT COUNT(*) FROM R"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Joins onto a bracketed group, and DELETE over a comma list, in the shapes ACE accepts (verified vs ACE). A
    // DELETE counts the joined rows where an outer join left the target without a row, as ACE does.
    [Theory]
    [InlineData("UPDATE A INNER JOIN (B LEFT JOIN C ON B.Id = C.Id) ON A.Id = B.Id SET A.X = 0", 2, "1/0|2/0|3/30")]
    [InlineData("UPDATE A LEFT JOIN (B LEFT JOIN C ON B.Id = C.Id) ON A.Id = B.Id SET A.X = 0", 3, "1/0|2/0|3/0")]
    [InlineData("UPDATE A INNER JOIN (B INNER JOIN C ON B.Id = C.Id) ON A.Id = B.Id SET A.X = 0", 1, "1/0|2/20|3/30")]
    [InlineData("UPDATE A RIGHT JOIN (B INNER JOIN C ON B.Id = C.Id) ON A.Id = B.Id SET A.X = 0", 2, "/0|1/0|2/20|3/30")]
    [InlineData("UPDATE A, B LEFT JOIN C ON B.Id = C.Id SET A.X = 0 WHERE A.Id = B.Id", 2, "1/0|2/0|3/30")]
    [InlineData("DELETE A.* FROM A INNER JOIN (B LEFT JOIN C ON B.Id = C.Id) ON A.Id = B.Id", 2, "3/30")]
    [InlineData("DELETE A.* FROM A, B WHERE A.Id = B.Id", 2, "3/30")]
    [InlineData("DELETE A.* FROM A, B INNER JOIN C ON B.Id = C.Id WHERE A.Id = B.Id", 1, "2/20|3/30")]
    [InlineData("DELETE A.* FROM B LEFT JOIN A ON A.Id = B.Id", 3, "3/30")]
    public void Joins_onto_a_group_and_comma_lists(string statement, int affected, string rows)
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = SeedThree(db);

            Assert.Equal(affected, e.ExecuteNonQuery(statement));
            Assert.Equal(rows, Rows(e, "SELECT Id, X FROM A ORDER BY Id"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // A derived table is written through to its tables' rows: the ones its join, WHERE, ORDER BY and TOP or OFFSET
    // choose, under the names its projection gives the columns (verified vs ACE). An unqualified SET names the one
    // table with that column, in a plain join too.
    [Theory]
    [InlineData("UPDATE (SELECT * FROM A INNER JOIN B ON A.Id = B.Id) SET X = 0", 2, "1/0|2/0|3/30")]
    [InlineData("UPDATE (SELECT TOP 1 * FROM A INNER JOIN B ON A.Id = B.Id ORDER BY A.Id DESC) AS Q SET Q.X = 0", 1, "1/10|2/0|3/30")]
    [InlineData("UPDATE (SELECT A.X, B.V AS W FROM A INNER JOIN B ON A.Id = B.Id) AS Q SET Q.X = Q.W", 2, "1/0|2/0|3/30")]
    [InlineData("UPDATE (SELECT * FROM A INNER JOIN B ON A.Id = B.Id WHERE B.Id > 1) SET X = 0", 1, "1/10|2/0|3/30")]
    [InlineData("UPDATE (SELECT A.Id, X FROM A INNER JOIN B ON A.Id = B.Id) AS Q INNER JOIN C ON Q.Id = C.Id SET Q.X = 0", 1, "1/0|2/20|3/30")]
    [InlineData("UPDATE A INNER JOIN B ON A.Id = B.Id SET X = 0", 2, "1/0|2/0|3/30")]
    [InlineData("UPDATE (SELECT TOP 2 * FROM A ORDER BY Id DESC) SET X = 0", 2, "1/10|2/0|3/0")]
    [InlineData("UPDATE (SELECT TOP 2 * FROM A ORDER BY Id DESC) AS Q SET Q.X = Q.Id", 2, "1/10|2/2|3/3")]
    [InlineData("UPDATE (SELECT * FROM A AS T WHERE T.X > 10) SET X = 0", 2, "1/10|2/0|3/0")]
    [InlineData("UPDATE (SELECT TOP 50 PERCENT * FROM A ORDER BY Id) SET X = 0", 2, "1/0|2/0|3/30")]
    [InlineData("UPDATE (SELECT * FROM A ORDER BY Id DESC OFFSET 1 ROWS) SET X = 0", 2, "1/0|2/0|3/30")]
    [InlineData("UPDATE (SELECT X AS Y, Id FROM A) SET Y = Id", 3, "1/1|2/2|3/3")]
    [InlineData("UPDATE (SELECT Id, X + 1 AS Z, X FROM A) SET X = 0", 3, "1/0|2/0|3/0")]
    [InlineData("UPDATE (SELECT Id, X FROM A WHERE Id > 1) AS Q INNER JOIN B ON Q.Id = B.Id SET Q.X = 5", 1, "1/10|2/5|3/30")]
    [InlineData("DELETE * FROM (SELECT TOP 2 * FROM A ORDER BY Id DESC)", 2, "1/10")]
    [InlineData("DELETE Q.* FROM (SELECT TOP 1 * FROM A ORDER BY Id) AS Q", 1, "2/20|3/30")]
    public void A_derived_table_is_written_through(string statement, int affected, string rows)
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = SeedThree(db);

            Assert.Equal(affected, e.ExecuteNonQuery(statement));
            Assert.Equal(rows, Rows(e, "SELECT Id, X FROM A ORDER BY Id"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Theory]
    [InlineData("UPDATE (SELECT TOP 2 Id FROM A ORDER BY Id DESC) SET X = 0", typeof(InvalidOperationException))]
    [InlineData("UPDATE (SELECT X AS Y, Id FROM A) SET X = 0", typeof(InvalidOperationException))]
    [InlineData("UPDATE (SELECT Id FROM A) SET Id = X", typeof(InvalidOperationException))]
    [InlineData("UPDATE (SELECT DISTINCT * FROM A) SET X = 0", typeof(NotSupportedException))]
    [InlineData("UPDATE (SELECT Id, COUNT(*) AS N FROM A GROUP BY Id) SET Id = 0", typeof(NotSupportedException))]
    // A name more than one table has, and a DELETE through a join (verified vs ACE).
    [InlineData("UPDATE (SELECT * FROM A INNER JOIN B ON A.Id = B.Id) SET Id = 0", typeof(InvalidOperationException))]
    [InlineData("UPDATE A INNER JOIN B ON A.Id = B.Id SET Id = 0", typeof(InvalidOperationException))]
    [InlineData("DELETE * FROM (SELECT * FROM A INNER JOIN B ON A.Id = B.Id)", typeof(InvalidOperationException))]
    [InlineData("DELETE Q.* FROM (SELECT A.* FROM A INNER JOIN B ON A.Id = B.Id) AS Q", typeof(InvalidOperationException))]
    [InlineData("DELETE A.* FROM (SELECT * FROM A INNER JOIN B ON A.Id = B.Id)", typeof(InvalidOperationException))]
    public void A_derived_table_that_cannot_be_written_is_refused(string statement, Type error)
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = SeedThree(db);

            Assert.Throws(error, () => e.ExecuteNonQuery(statement));
            Assert.Equal("1/10|2/20|3/30", Rows(e, "SELECT Id, X FROM A ORDER BY Id"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void A_derived_join_writes_both_tables_and_adds_rows_for_its_unmatched_side()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = SeedThree(db);

            Assert.Equal(2, e.ExecuteNonQuery("UPDATE (SELECT * FROM A INNER JOIN B ON A.Id = B.Id) SET V = X, X = V"));
            Assert.Equal("1/0|2/0|3/30", Rows(e, "SELECT Id, X FROM A ORDER BY Id"));
            Assert.Equal("1/10|2/20|4/0", Rows(e, "SELECT Id, V FROM B ORDER BY Id"));

            Assert.Equal(3, e.ExecuteNonQuery("UPDATE (SELECT * FROM A LEFT JOIN B ON A.Id = B.Id) SET V = 7"));
            Assert.Equal("1/1/7|2/2/7|3/4/0|4//7", Rows(e, "SELECT K, Id, V FROM B ORDER BY K"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // A derived join can sit anywhere in a bracketed group a table can, joined and null-extended as a whole (verified
    // vs ACE). Q is A joined to B, so Ids 1 and 2; C has 1 and 4, and D has 1 and 3.
    [Theory]
    [InlineData("UPDATE C INNER JOIN ((SELECT A.Id, X, V FROM A INNER JOIN B ON A.Id = B.Id) AS Q INNER JOIN D ON Q.Id = D.Id) ON C.Id = Q.Id SET Q.X = 0", 1, "1/0|2/20|3/30")]
    [InlineData("UPDATE C INNER JOIN ((SELECT A.Id, X, V FROM A INNER JOIN B ON A.Id = B.Id) AS Q LEFT JOIN D ON Q.Id = D.Id) ON C.Id = Q.Id SET Q.X = 0", 1, "1/0|2/20|3/30")]
    [InlineData("UPDATE C LEFT JOIN ((SELECT A.Id, X, V FROM A INNER JOIN B ON A.Id = B.Id) AS Q LEFT JOIN D ON Q.Id = D.Id) ON C.Id = Q.Id SET Q.X = 0", 2, "/0|1/0|2/20|3/30")]
    [InlineData("UPDATE C, (SELECT A.Id, X, V FROM A INNER JOIN B ON A.Id = B.Id) AS Q INNER JOIN D ON Q.Id = D.Id SET Q.X = 0 WHERE C.Id = Q.Id", 1, "1/0|2/20|3/30")]
    [InlineData("UPDATE C INNER JOIN (D INNER JOIN (SELECT A.Id, X, V FROM A INNER JOIN B ON A.Id = B.Id) AS Q ON D.Id = Q.Id) ON C.Id = D.Id SET Q.X = 0", 1, "1/0|2/20|3/30")]
    public void A_derived_join_can_sit_in_a_group(string statement, int affected, string rows)
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = SeedThree(db);
            e.ExecuteNonQuery("CREATE TABLE D (Id long, Z long)");
            e.ExecuteNonQuery("INSERT INTO D (Id, Z) VALUES (1, 0)");
            e.ExecuteNonQuery("INSERT INTO D (Id, Z) VALUES (3, 0)");

            Assert.Equal(affected, e.ExecuteNonQuery(statement));
            Assert.Equal(rows, Rows(e, "SELECT Id, X FROM A ORDER BY Id"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Inside a LEFT-joined group, a table has no row where the group's first table has none, whatever its own ON
    // would match (verified vs ACE: 5 joined rows, and A3's row writes a new C row).
    [Fact]
    public void A_left_joined_group_is_null_extended_as_a_whole()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = SeedThree(db);

            Assert.Equal(5, e.ExecuteNonQuery(
                "UPDATE A LEFT JOIN (B LEFT JOIN C ON B.V = C.W) ON A.Id = B.Id SET C.W = 5"));
            Assert.Equal("/5|1/5|4/5", Rows(e, "SELECT Id, W FROM C ORDER BY Id"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Theory]
    [InlineData("UPDATE A LEFT JOIN (B INNER JOIN C ON B.Id = C.Id) ON A.Id = B.Id SET A.X = 0")]
    [InlineData("UPDATE (A INNER JOIN B ON A.Id = B.Id) RIGHT JOIN C ON B.Id = C.Id SET A.X = 0")]
    [InlineData("DELETE A.* FROM A LEFT JOIN (B INNER JOIN C ON B.Id = C.Id) ON A.Id = B.Id")]
    public void Shapes_ace_refuses_are_not_supported(string statement)
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = SeedThree(db);

            Assert.Throws<NotSupportedException>(() => e.ExecuteNonQuery(statement));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // `DELETE *` (bare star) is fine for a single table, but a join DELETE without a `table.*` target is
    // ambiguous — Access rejects it ("specify the table"), and so does LibRed.
    [Fact]
    public void Delete_star_needs_a_table_target_on_a_join()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = Seed(db);

            // Single table: bare `*` deletes matching rows.
            Assert.Equal(2, e.ExecuteNonQuery("DELETE * FROM C WHERE ParentId = 1"));
            Assert.Equal(new[] { 12 }, e.ExecuteQuery("SELECT Id FROM C").Rows.Select(r => Convert.ToInt32(r[0])));

            // Join with a bare `*` (or no target) is ambiguous → rejected.
            Assert.Throws<InvalidOperationException>(() =>
                e.ExecuteNonQuery("DELETE * FROM C INNER JOIN P ON C.ParentId = P.Id WHERE P.PName = 'p2'"));
            Assert.Throws<InvalidOperationException>(() =>
                e.ExecuteNonQuery("DELETE FROM C INNER JOIN P ON C.ParentId = P.Id WHERE P.PName = 'p2'"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
