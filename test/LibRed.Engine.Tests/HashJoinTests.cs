using System.Linq;
using LibRed;
using LibRed.Engine;
using LibRed.Engine.Plan;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// An equi-join on columns with no usable index is executed as a hash join (O(n+m)) instead of the scan-based
/// nested loop (O(n·m)). These verify the planner picks it and — crucially — that it produces exactly the same
/// rows as a nested loop: same-value matching under Access's coercions (case-insensitive text, numeric width),
/// null keys never matching, LEFT-join null padding, composite keys, and residual non-equi conjuncts.
/// </summary>
public class HashJoinTests
{
    // P.Id is a PK (indexed); C.Pid is deliberately NOT indexed, so P ⋈ C ON P.Id = C.Pid cannot become an
    // index-nested-loop and falls to the hash join.
    private static QueryEngine TwoTables()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hashjoin-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE P (Id LONG PRIMARY KEY, Nm TEXT(20))");
        e.ExecuteNonQuery("CREATE TABLE C (Id LONG PRIMARY KEY, Pid LONG, Tag TEXT(10), Amt LONG)");
        for (int i = 0; i < 50; i++) e.ExecuteNonQuery($"INSERT INTO P (Id, Nm) VALUES ({i}, 'p{i}')");
        for (int i = 0; i < 200; i++) e.ExecuteNonQuery($"INSERT INTO C (Id, Pid, Tag, Amt) VALUES ({i}, {i % 50}, 'T{i % 50}', {i})");
        return e;
    }

    private static int Count(QueryEngine e, string sql) => e.ExecuteQuery(sql).Rows.Count();

    [Fact]
    public void An_unindexed_equi_join_is_planned_as_a_hash_join()
    {
        var plan = TwoTables().PlanFor("SELECT P.Nm, C.Amt FROM P INNER JOIN C ON P.Id = C.Pid");
        Assert.True(ContainsHashJoin(plan), "expected a HashJoinNode in the plan");
    }

    [Fact]
    public void Inner_hash_join_matches_the_nested_loop_result()
    {
        var e = TwoTables();
        // Every C has a P (Pid = i%50, all present), 200 children → 200 joined rows; each P has 4.
        Assert.Equal(200, Count(e, "SELECT P.Nm, C.Amt FROM P INNER JOIN C ON P.Id = C.Pid"));
        Assert.Equal(4, Count(e, "SELECT C.Amt FROM P INNER JOIN C ON P.Id = C.Pid WHERE P.Id = 7"));
    }

    [Fact]
    public void Hash_join_on_text_keys_is_case_insensitive_like_access()
    {
        var e = TwoTables();
        // Join P.Nm to C.Tag would not match; instead join two text columns that do. Build a case difference:
        e.ExecuteNonQuery("CREATE TABLE L (K TEXT(10))");
        e.ExecuteNonQuery("CREATE TABLE R (K TEXT(10))");
        e.ExecuteNonQuery("INSERT INTO L (K) VALUES ('Alpha')");
        e.ExecuteNonQuery("INSERT INTO R (K) VALUES ('ALPHA')");
        // Access text equality is case-insensitive, so the hash join must still match these.
        Assert.Equal(1, Count(e, "SELECT L.K FROM L INNER JOIN R ON L.K = R.K"));
    }

    [Fact]
    public void Null_keys_never_match()
    {
        var e = TwoTables();
        e.ExecuteNonQuery("UPDATE C SET Pid = NULL WHERE Id = 3"); // one child orphaned via null key
        // Inner join drops the null-key child: 199 matches.
        Assert.Equal(199, Count(e, "SELECT C.Id FROM P INNER JOIN C ON P.Id = C.Pid"));
    }

    [Fact]
    public void Left_join_null_pads_unmatched_rows()
    {
        var e = TwoTables();
        e.ExecuteNonQuery("INSERT INTO P (Id, Nm) VALUES (999, 'lonely')"); // a P with no children
        // 200 matched child rows + 1 null-padded row for the childless P = 201.
        Assert.Equal(201, Count(e, "SELECT P.Nm, C.Amt FROM P LEFT JOIN C ON P.Id = C.Pid"));
        Assert.Equal(1, Count(e, "SELECT P.Nm FROM P LEFT JOIN C ON P.Id = C.Pid WHERE C.Id IS NULL"));
    }

    [Fact]
    public void Residual_non_equi_conjunct_is_applied_after_the_hash_match()
    {
        var e = TwoTables();
        // The equality hashes; the extra C.Amt > 100 is the residual re-check on each bucket candidate.
        int viaJoin = Count(e, "SELECT C.Amt FROM P INNER JOIN C ON P.Id = C.Pid AND C.Amt > 100");
        int viaScan = Count(e, "SELECT Amt FROM C WHERE Amt > 100"); // every C has a P
        Assert.Equal(viaScan, viaJoin);
    }

    [Fact]
    public void Composite_key_hash_join_matches_on_all_columns()
    {
        var e = TwoTables();
        e.ExecuteNonQuery("CREATE TABLE A (X LONG, Y TEXT(5))");
        e.ExecuteNonQuery("CREATE TABLE B (X LONG, Y TEXT(5))");
        e.ExecuteNonQuery("INSERT INTO A (X, Y) VALUES (1, 'a')");
        e.ExecuteNonQuery("INSERT INTO A (X, Y) VALUES (1, 'b')");
        e.ExecuteNonQuery("INSERT INTO B (X, Y) VALUES (1, 'a')");
        // Only (1,'a') matches on both key columns, not (1,'b').
        Assert.Equal(1, Count(e, "SELECT A.X FROM A INNER JOIN B ON A.X = B.X AND A.Y = B.Y"));
    }

    [Fact]
    public void Right_join_is_hashed_and_preserves_unmatched_right_rows()
    {
        var e = TwoTables();
        e.ExecuteNonQuery("INSERT INTO P (Id, Nm) VALUES (999, 'childless')"); // a P (right side) with no children
        // C RIGHT JOIN P keeps every P; 200 children match + 50 childless P (ids 50..99 have no children, since
        // Pid = i%50 ∈ 0..49) ... actually every id 0..49 has children, only 999 is childless → 200 + 1 = 201.
        Assert.True(ContainsHashJoin(e.PlanFor("SELECT P.Nm, C.Amt FROM C RIGHT JOIN P ON C.Pid = P.Id")));
        Assert.Equal(201, Count(e, "SELECT P.Nm, C.Amt FROM C RIGHT JOIN P ON C.Pid = P.Id"));
        // The childless P appears with a null C side.
        Assert.Equal(1, Count(e, "SELECT P.Nm FROM C RIGHT JOIN P ON C.Pid = P.Id WHERE C.Id IS NULL"));
    }

    [Fact]
    public void Right_join_matches_the_equivalent_left_join()
    {
        var e = TwoTables();
        int right = Count(e, "SELECT C.Amt FROM C RIGHT JOIN P ON C.Pid = P.Id");
        int left = Count(e, "SELECT C.Amt FROM P LEFT JOIN C ON P.Id = C.Pid");
        Assert.Equal(left, right); // A RIGHT JOIN B ≡ B LEFT JOIN A
    }

    [Fact]
    public void Hash_join_resolves_a_derived_table_key_column()
    {
        // The right side is a derived table; its key column's kind is resolved through the projection, so the
        // join still hashes (this is what an unindexed join against a subquery relies on).
        var e = TwoTables();
        const string sql = "SELECT P.Nm FROM P INNER JOIN (SELECT Pid FROM C WHERE Amt > 100) AS d ON P.Id = d.Pid";
        Assert.True(ContainsHashJoin(e.PlanFor(sql)));
        // Each C row with Amt>100 (Amt=i∈101..199 → 99 rows) has a matching P, so 99 joined rows.
        Assert.Equal(99, Count(e, sql));
    }

    private static bool ContainsHashJoin(PlanNode node) =>
        node is HashJoinNode || node.Children.Any(ContainsHashJoin);
}
