using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// A composite foreign key follows ACE's MATCH FULL rule (verified vs ACE): the FK is skipped only when EVERY
// column is null; a partial null (some null, some not) is rejected — unlike SQL Server's MATCH SIMPLE, which
// would skip the check when any column is null.
public class CompositeFkMatchFullTests : TempDatabaseTest
{
    private static QueryEngine Setup()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "cfk-");
        var e = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE P (A long, B long, CONSTRAINT PK_P PRIMARY KEY (A, B))");
        e.ExecuteNonQuery("CREATE TABLE C (Id long PRIMARY KEY, X long, Y long, " +
                          "CONSTRAINT FK_C FOREIGN KEY (X, Y) REFERENCES P (A, B))");
        e.ExecuteNonQuery("INSERT INTO P (A, B) VALUES (1, 2)");
        return e;
    }

    [Fact]
    public void All_null_is_allowed_full_match_required_partial_null_rejected()
    {
        var e = Setup();
        Assert.Equal(1, e.ExecuteNonQuery("INSERT INTO C (Id, X, Y) VALUES (1, 1, 2)"));       // full match → ok
        Assert.Equal(1, e.ExecuteNonQuery("INSERT INTO C (Id, X, Y) VALUES (2, NULL, NULL)")); // all null → ok

        // Partial nulls are rejected (MATCH FULL), as is a non-matching full key.
        Assert.Throws<InvalidOperationException>(() => e.ExecuteNonQuery("INSERT INTO C (Id, X, Y) VALUES (3, 1, NULL)"));
        Assert.Throws<InvalidOperationException>(() => e.ExecuteNonQuery("INSERT INTO C (Id, X, Y) VALUES (4, NULL, 2)"));
        Assert.Throws<InvalidOperationException>(() => e.ExecuteNonQuery("INSERT INTO C (Id, X, Y) VALUES (5, 1, 99)"));

        Assert.Equal(2, e.ExecuteQuery("SELECT Id FROM C").Rows.Count()); // only the two valid rows were written
    }

    [Fact]
    public void Update_to_a_partial_null_is_also_rejected()
    {
        var e = Setup();
        e.ExecuteNonQuery("INSERT INTO C (Id, X, Y) VALUES (1, 1, 2)");
        // Nulling only one of the two FK columns is a partial null → rejected.
        Assert.Throws<InvalidOperationException>(() => e.ExecuteNonQuery("UPDATE C SET Y = NULL WHERE Id = 1"));
        // Nulling both is fine.
        Assert.Equal(1, e.ExecuteNonQuery("UPDATE C SET X = NULL, Y = NULL WHERE Id = 1"));
    }
}
