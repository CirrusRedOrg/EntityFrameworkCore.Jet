using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class StringComparisonTests
{
    private static string Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "strcmp-");
        return path;
    }

    private static object? Scalar(string expr)
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery("CREATE TABLE One (Id LONG)");
            e.ExecuteNonQuery("INSERT INTO One (Id) VALUES (1)");
            return e.ExecuteQuery($"SELECT IIF({expr}, 1, 0) FROM One").Rows.First()[0];
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Access text comparison is case-insensitive and ignores trailing spaces (verified vs ACE).
    [Fact]
    public void Equality_is_case_insensitive_and_trailing_space_insensitive()
    {
        Assert.Equal(1, Scalar("'London' = 'LONDON'"));
        Assert.Equal(1, Scalar("'abc' = 'abc  '"));
        Assert.Equal(1, Scalar("'abc  ' = 'abc'"));
        Assert.Equal(1, Scalar("'' = ' '"));
        Assert.Equal(0, Scalar("'London' = 'Paris'"));
    }

    [Fact]
    public void Ordering_is_case_insensitive()
    {
        Assert.Equal(0, Scalar("'London' < 'london'")); // equal → not less
        Assert.Equal(1, Scalar("'A' < 'b'"));
        Assert.Equal(1, Scalar("'a' < 'B'"));
    }

    // Accented letters sort next to their base letter (invariant-culture collation), matching ACE — an
    // accented letter is not shoved past 'z' the way an ordinal (code-point) compare would.
    [Fact]
    public void Ordering_is_accent_aware()
    {
        Assert.Equal(1, Scalar("'é' < 'f'"));       // é sorts near e, before f
        Assert.Equal(1, Scalar("'é' < 'z'"));
        Assert.Equal(1, Scalar("'café' < 'cafz'"));
        Assert.Equal(1, Scalar("'e' < 'é'"));       // but the accent still orders after the bare letter
        Assert.Equal(0, Scalar("'café' = 'cafe'")); // and stays significant for equality
    }

    // DISTINCT and GROUP BY treat strings case-insensitively (and ignore trailing spaces), like Access.
    [Fact]
    public void Distinct_and_group_by_are_case_insensitive()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery("CREATE TABLE C (Id LONG, City VARCHAR(20))");
            e.ExecuteNonQuery("INSERT INTO C (Id, City) VALUES (1, 'London')");
            e.ExecuteNonQuery("INSERT INTO C (Id, City) VALUES (2, 'LONDON')");
            e.ExecuteNonQuery("INSERT INTO C (Id, City) VALUES (3, 'london ')");
            e.ExecuteNonQuery("INSERT INTO C (Id, City) VALUES (4, 'Paris')");

            Assert.Equal(2, e.ExecuteQuery("SELECT DISTINCT City FROM C").Rows.Count());       // London*, Paris
            Assert.Equal(2, e.ExecuteQuery("SELECT City, COUNT(*) FROM C GROUP BY City").Rows.Count());
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // A real column filter is case-insensitive: matching 'london' finds the 'London' customers.
    [Fact]
    public void Column_filter_matches_regardless_of_case()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path);
            var e = new QueryEngine(db);
            int lower = e.ExecuteQuery("SELECT CustomerID FROM Customers WHERE City = 'london'").Rows.Count();
            int exact = e.ExecuteQuery("SELECT CustomerID FROM Customers WHERE City = 'London'").Rows.Count();
            Assert.True(exact > 0);
            Assert.Equal(exact, lower);
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
