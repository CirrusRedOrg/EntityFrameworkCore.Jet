using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// x IN (literal list) is kept as a flat node and evaluated iteratively. The regression that motivated this:
// EF Core inlines a "huge number of values" Contains as thousands of constants, and lowering that to a deep
// OR-tree recursed once per item and overflowed the stack (crashing the test host, not just failing).
public class InListTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "inlist-");
        var e = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE T ( K LONG PRIMARY KEY )");
        for (int i = 1; i <= 5; i++) e.ExecuteNonQuery($"INSERT INTO T (K) VALUES ({i})");
        return e;
    }

    [Theory]
    [InlineData("K IN (2, 4)", 2)]
    [InlineData("K NOT IN (2, 4)", 3)]
    [InlineData("K IN (99)", 0)]
    [InlineData("K IN (1, 2, 3, 4, 5)", 5)]
    public void In_list_membership(string predicate, int expectedCount)
    {
        var e = Fresh();
        Assert.Equal(expectedCount, e.ExecuteQuery($"SELECT K FROM T WHERE {predicate}").Rows.Count());
    }

    // The actual host-crash repro: a value list of many thousands of constants must evaluate without
    // overflowing the stack.
    [Fact]
    public void A_huge_value_list_does_not_overflow_the_stack()
    {
        var e = Fresh();
        string list = string.Join(", ", Enumerable.Range(1, 10_000));   // K=3 is somewhere in the middle
        var rows = e.ExecuteQuery($"SELECT K FROM T WHERE K IN ({list})").Rows;
        Assert.Equal(5, rows.Count());   // all five seeded rows are in 1..10000
    }
}
