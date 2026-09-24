using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// A view whose definition reaches itself, directly or round a longer loop. Nothing stops one being stored —
// the body's sources are resolved when the view is USED, and a file can carry a cycle that Access wrote or
// that a DROP + CREATE left behind — so the expansion is where it has to be caught. Expanding blind is not a
// failed statement but a StackOverflowException, which .NET cannot catch: it takes the process down.
public class RecursiveViewTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "recursive-view-");
        var e = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE T (K LONG)");
        return e;
    }

    [Fact]
    public void A_view_defined_in_terms_of_itself_is_reported()
    {
        var e = Fresh();
        e.ExecuteNonQuery("CREATE VIEW V AS SELECT * FROM T");
        e.ExecuteNonQuery("DROP VIEW V");
        e.ExecuteNonQuery("CREATE VIEW V AS SELECT * FROM V");

        var error = Assert.Throws<InvalidOperationException>(() => e.ExecuteQuery("SELECT * FROM V"));
        Assert.Contains("defined in terms of itself", error.Message);
    }

    // The same cycle one hop longer, which a naive "is this the view we started from" check would miss.
    [Fact]
    public void A_mutually_recursive_view_pair_is_reported_too()
    {
        var e = Fresh();
        e.ExecuteNonQuery("CREATE VIEW V1 AS SELECT * FROM T");
        e.ExecuteNonQuery("CREATE VIEW V2 AS SELECT * FROM V1");
        e.ExecuteNonQuery("DROP VIEW V1");
        e.ExecuteNonQuery("CREATE VIEW V1 AS SELECT * FROM V2");

        Assert.Throws<InvalidOperationException>(() => e.ExecuteQuery("SELECT * FROM V1"));
    }

    // And the shape that must NOT be mistaken for a cycle: one view used twice in a query still expands.
    [Fact]
    public void The_same_view_used_twice_in_one_query_still_expands()
    {
        var e = Fresh();
        e.ExecuteNonQuery("INSERT INTO T (K) VALUES (1)");
        e.ExecuteNonQuery("CREATE VIEW V AS SELECT * FROM T");

        var result = e.ExecuteQuery("SELECT A.K FROM V AS A INNER JOIN V AS B ON A.K = B.K");
        Assert.Equal(1, Convert.ToInt32(result.Rows.Single()[0]));
    }
}
