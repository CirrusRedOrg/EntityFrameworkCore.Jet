using System.Linq;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// ANSI SQL:2008 paging — <c>OFFSET n ROWS</c>, <c>FETCH FIRST/NEXT m ROWS ONLY</c>, and the two together.
/// This is what EF Core's base <c>QuerySqlGenerator.GenerateLimitOffset</c> emits whenever the provider does
/// not rewrite Skip/Take, so LibRed's extended SQL mode depends on it; Jet/ACE has no OFFSET at all and the
/// compat mode still emulates paging with nested TOP instead.
/// </summary>
public class OffsetFetchTests
{
    // Employees has 9 rows with EmployeeID 1-9, which makes the returned window checkable rather than just
    // its size — a paging bug that returns the right count from the wrong place would pass a count assertion.
    private static QueryEngine Northwind()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "paging-");
        return new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
    }

    private static int[] Ids(QueryEngine e, string sql)
        => e.ExecuteQuery(sql).Rows.Select(r => Convert.ToInt32(r[0])).ToArray();

    [Fact]
    public void Fetch_first_alone_takes_from_the_start()
        => Assert.Equal([1, 2, 3], Ids(Northwind(),
            "SELECT EmployeeID FROM Employees ORDER BY EmployeeID FETCH FIRST 3 ROWS ONLY"));

    [Fact]
    public void Offset_alone_skips_and_returns_the_rest()
        => Assert.Equal([8, 9], Ids(Northwind(),
            "SELECT EmployeeID FROM Employees ORDER BY EmployeeID OFFSET 7 ROWS"));

    [Fact]
    public void Offset_with_fetch_returns_the_window()
        => Assert.Equal([4, 5], Ids(Northwind(),
            "SELECT EmployeeID FROM Employees ORDER BY EmployeeID OFFSET 3 ROWS FETCH NEXT 2 ROWS ONLY"));

    [Theory]
    // FIRST/NEXT and ROW/ROWS are interchangeable in the standard. EF only ever emits FETCH FIRST for a bare
    // Take and FETCH NEXT after an OFFSET, but accepting both costs nothing and a grammar that took only NEXT
    // would reject every plain Take.
    [InlineData("SELECT EmployeeID FROM Employees ORDER BY EmployeeID OFFSET 3 ROWS FETCH FIRST 2 ROWS ONLY")]
    [InlineData("SELECT EmployeeID FROM Employees ORDER BY EmployeeID OFFSET 3 ROW FETCH NEXT 2 ROW ONLY")]
    [InlineData("SELECT EmployeeID FROM Employees ORDER BY EmployeeID offset 3 rows fetch next 2 rows only")]
    public void Fetch_keyword_and_row_number_spellings_are_synonyms(string sql)
        => Assert.Equal([4, 5], Ids(Northwind(), sql));

    [Fact]
    public void Offset_past_the_end_returns_nothing()
        => Assert.Empty(Ids(Northwind(),
            "SELECT EmployeeID FROM Employees ORDER BY EmployeeID OFFSET 20 ROWS"));

    [Fact]
    public void Fetch_beyond_the_remainder_returns_what_is_left()
        => Assert.Equal([8, 9], Ids(Northwind(),
            "SELECT EmployeeID FROM Employees ORDER BY EmployeeID OFFSET 7 ROWS FETCH NEXT 50 ROWS ONLY"));

    [Fact]
    public void Zero_offset_is_a_no_op()
        => Assert.Equal([1, 2], Ids(Northwind(),
            "SELECT EmployeeID FROM Employees ORDER BY EmployeeID OFFSET 0 ROWS FETCH NEXT 2 ROWS ONLY"));

    [Fact]
    public void Paging_operands_may_be_parameters()
    {
        // The point of the whole exercise: EF passes the page bounds as parameters, and Access's TOP takes a
        // literal only - which is why EFCore.Jet has to rewrite `TOP @param` down in JetCommand. LibRed reads
        // them directly, reusing the operand rule TOP already uses.
        var engine = Northwind();
        var result = engine.ExecuteQuery(
            "SELECT EmployeeID FROM Employees ORDER BY EmployeeID OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY",
            new Dictionary<string, object?> { ["@skip"] = 5, ["@take"] = 3 });

        Assert.Equal([6, 7, 8], result.Rows.Select(r => Convert.ToInt32(r[0])).ToArray());
    }

    [Fact]
    public void Paging_applies_after_ordering_not_before()
    {
        // Descending order must page the descending sequence: 9..1 skipping 2 gives 7, 6. If the skip were
        // applied to the unordered scan the ids would come out of the storage order instead.
        Assert.Equal([7, 6], Ids(Northwind(),
            "SELECT EmployeeID FROM Employees ORDER BY EmployeeID DESC OFFSET 2 ROWS FETCH NEXT 2 ROWS ONLY"));
    }

    [Fact]
    public void Paging_applies_after_the_where_clause()
        => Assert.Equal([5, 6], Ids(Northwind(),
            "SELECT EmployeeID FROM Employees WHERE EmployeeID > 2 ORDER BY EmployeeID OFFSET 2 ROWS FETCH NEXT 2 ROWS ONLY"));
}
