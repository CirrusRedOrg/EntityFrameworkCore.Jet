using System.Linq;
using LibRed;
using LibRed.Engine;
using LibRed.Engine.Plan;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// FULL [OUTER] JOIN — a LibRed extension, since ACE has no full outer join and no syntax for one. It preserves
/// both sides at once, which is what separates it from every other join the engine runs: the right side's rows
/// have to be tracked across the whole left pass, and on the hash path the build side stops being disposable.
/// </summary>
public class FullJoinTests : TempDatabaseTest
{
    // P.Id is a PK (indexed); C.Pid is deliberately NOT indexed, so the equi-join cannot become an
    // index-nested-loop and takes the hash path. Exactly one parent has no child and one child has no parent, so
    // every count below distinguishes INNER (20) / LEFT (21) / RIGHT (21) / FULL (22).
    private static QueryEngine TwoTables()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "fulljoin-");
        var e = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE P (Id LONG PRIMARY KEY, Nm TEXT(20))");
        e.ExecuteNonQuery("CREATE TABLE C (Id LONG PRIMARY KEY, Pid LONG, Amt LONG)");
        for (int i = 0; i < 10; i++) e.ExecuteNonQuery($"INSERT INTO P (Id, Nm) VALUES ({i}, 'p{i}')");
        for (int i = 0; i < 20; i++) e.ExecuteNonQuery($"INSERT INTO C (Id, Pid, Amt) VALUES ({i}, {i % 10}, {i})");
        e.ExecuteNonQuery("INSERT INTO P (Id, Nm) VALUES (99, 'childless')"); // left row with no match
        e.ExecuteNonQuery("INSERT INTO C (Id, Pid, Amt) VALUES (99, 777, 999)"); // right row with no match
        return e;
    }

    private static int Count(QueryEngine e, string sql) => e.ExecuteQuery(sql).Rows.Count();

    [Fact]
    public void Full_join_keeps_the_unmatched_rows_from_both_sides()
    {
        var e = TwoTables();
        // 20 matched pairs, + the childless P, + the parentless C.
        Assert.Equal(20, Count(e, "SELECT P.Nm, C.Amt FROM P INNER JOIN C ON P.Id = C.Pid"));
        Assert.Equal(21, Count(e, "SELECT P.Nm, C.Amt FROM P LEFT JOIN C ON P.Id = C.Pid"));
        Assert.Equal(21, Count(e, "SELECT P.Nm, C.Amt FROM P RIGHT JOIN C ON P.Id = C.Pid"));
        Assert.Equal(22, Count(e, "SELECT P.Nm, C.Amt FROM P FULL JOIN C ON P.Id = C.Pid"));
    }

    [Fact]
    public void Full_outer_join_is_the_same_as_full_join()
        => Assert.Equal(
            Count(TwoTables(), "SELECT P.Nm FROM P FULL JOIN C ON P.Id = C.Pid"),
            Count(TwoTables(), "SELECT P.Nm FROM P FULL OUTER JOIN C ON P.Id = C.Pid"));

    [Fact]
    public void Each_unmatched_side_is_padded_with_nulls_on_the_other()
    {
        var e = TwoTables();
        // the childless P, with its C side all null
        Assert.Equal(1, Count(e, "SELECT P.Nm FROM P FULL JOIN C ON P.Id = C.Pid WHERE C.Id IS NULL"));
        // the parentless C, with its P side all null
        Assert.Equal(1, Count(e, "SELECT C.Amt FROM P FULL JOIN C ON P.Id = C.Pid WHERE P.Id IS NULL"));
    }

    [Fact]
    public void Full_join_equals_left_plus_right_minus_inner()
    {
        var e = TwoTables();
        int inner = Count(e, "SELECT P.Nm FROM P INNER JOIN C ON P.Id = C.Pid");
        int left = Count(e, "SELECT P.Nm FROM P LEFT JOIN C ON P.Id = C.Pid");
        int right = Count(e, "SELECT P.Nm FROM P RIGHT JOIN C ON P.Id = C.Pid");
        int full = Count(e, "SELECT P.Nm FROM P FULL JOIN C ON P.Id = C.Pid");
        Assert.Equal(left + right - inner, full);
    }

    [Fact]
    public void Full_join_is_symmetric()
    {
        var e = TwoTables();
        Assert.Equal(
            Count(e, "SELECT P.Nm FROM P FULL JOIN C ON P.Id = C.Pid"),
            Count(e, "SELECT P.Nm FROM C FULL JOIN P ON P.Id = C.Pid"));
    }

    [Fact]
    public void An_unindexed_full_equi_join_is_planned_as_a_hash_join()
    {
        var plan = TwoTables().PlanFor("SELECT P.Nm, C.Amt FROM P FULL JOIN C ON P.Id = C.Pid");
        Assert.True(ContainsHashJoin(plan), "expected a HashJoinNode in the plan");
    }

    [Fact]
    public void A_null_key_row_on_the_build_side_is_still_preserved()
    {
        // The hash build phase drops null-key rows, because a null key can never satisfy an equi-join. Under
        // FULL that is precisely a row to emit, not to discard — the regression this guards.
        var e = TwoTables();
        e.ExecuteNonQuery("UPDATE C SET Pid = NULL WHERE Id = 3");

        // C 3 no longer matches (P 3 still does, via C 13), so: 19 matched + 1 childless P + 2 unmatched C.
        Assert.Equal(22, Count(e, "SELECT P.Nm, C.Amt FROM P FULL JOIN C ON P.Id = C.Pid"));
        // Both unmatched C rows appear with a null P side — the null-key one and the 777 one.
        Assert.Equal(2, Count(e, "SELECT C.Id FROM P FULL JOIN C ON P.Id = C.Pid WHERE P.Id IS NULL"));
    }

    [Fact]
    public void Full_join_preserves_both_sides_on_the_nested_loop_path_too()
    {
        // No left-column = right-column equality, so there are no hash keys and the join stays a nested loop.
        // Nothing matches either, so every row of both tables must come through unmatched: 11 P + 21 C.
        var e = TwoTables();
        const string sql = "SELECT P.Nm, C.Amt FROM P FULL JOIN C ON P.Nm = 'nope' AND C.Amt = -1";
        Assert.False(ContainsHashJoin(e.PlanFor(sql)), "expected the nested-loop path, not a hash join");
        Assert.Equal(32, Count(e, sql));
    }

    [Fact]
    public void Full_is_a_keyword_so_a_column_of_that_name_needs_quoting()
    {
        // The cost of the extension: FULL is not reserved in Access, so a real column called "Full" has to be
        // bracketed or backticked here — the same tax LEFT, RIGHT and ORDER already charge.
        var e = TwoTables();
        e.ExecuteNonQuery("CREATE TABLE K (`Full` TEXT(10))");
        e.ExecuteNonQuery("INSERT INTO K (`Full`) VALUES ('x')");
        Assert.Equal(1, Count(e, "SELECT `Full` FROM K"));
        Assert.Equal(1, Count(e, "SELECT [Full] FROM K"));
    }

    private static bool ContainsHashJoin(PlanNode node) =>
        node is HashJoinNode || node.Children.Any(ContainsHashJoin);
}
