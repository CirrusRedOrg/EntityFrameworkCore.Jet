using System.Linq;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// Multi-table UPDATE/DELETE (Access's joined-source form) must find its target rows via an index-nested-loop
/// over the ON equi-conditions, not a full cartesian product, and must correctly rewrite/remove exactly the
/// matched rows. Includes the all-fixed-column-table case that previously overflowed on re-encode.
/// </summary>
public class JoinedUpdateDeleteTests
{
    private static QueryEngine Seeded()
    {
        string path = Path.Combine(Path.GetTempPath(), $"jud-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));
        // Parent P and child C, plus an ALL-FIXED-COLUMN table F (no variable columns) to cover the re-encode path.
        e.ExecuteNonQuery("CREATE TABLE P (Id LONG PRIMARY KEY, Flag LONG)");
        e.ExecuteNonQuery("CREATE TABLE C (Id LONG PRIMARY KEY, Pid LONG, Amt LONG)");
        e.ExecuteNonQuery("CREATE INDEX IX_Pid ON C (Pid)");
        for (int i = 0; i < 40; i++) e.ExecuteNonQuery($"INSERT INTO P (Id, Flag) VALUES ({i}, {i % 2})");
        for (int i = 0; i < 200; i++) e.ExecuteNonQuery($"INSERT INTO C (Id, Pid, Amt) VALUES ({i}, {i % 40}, {i})");
        return e;
    }

    private static int Count(QueryEngine e, string sql) => (int)Convert.ToInt64(e.ExecuteQuery(sql).Rows.Single()[0]!);

    [Fact]
    public void Joined_update_sets_only_matched_rows()
    {
        var e = Seeded();
        // Update C.Amt to 0 for children whose parent has Flag = 1 (odd parents). Pid = i%40; odd parents are
        // the 20 odd ids, each with 5 children (200/40) → 100 rows.
        int expected = Count(e, "SELECT COUNT(*) FROM C INNER JOIN P ON C.Pid = P.Id WHERE P.Flag = 1");
        // Use a sentinel (-1) that no seeded row has, so the count is unambiguous.
        int affected = e.ExecuteNonQuery("UPDATE C INNER JOIN P ON C.Pid = P.Id SET C.Amt = -1 WHERE P.Flag = 1");
        Assert.Equal(expected, affected);
        Assert.Equal(expected, Count(e, "SELECT COUNT(*) FROM C WHERE Amt = -1"));
        // Exactly the matched rows changed; the rest are untouched.
        Assert.Equal(200 - expected, Count(e, "SELECT COUNT(*) FROM C WHERE Amt <> -1"));
    }

    [Fact]
    public void Joined_delete_removes_only_matched_target_rows()
    {
        var e = Seeded();
        int expected = Count(e, "SELECT COUNT(*) FROM C INNER JOIN P ON C.Pid = P.Id WHERE P.Flag = 1");
        int affected = e.ExecuteNonQuery("DELETE C.* FROM C INNER JOIN P ON C.Pid = P.Id WHERE P.Flag = 1");
        Assert.Equal(expected, affected);
        Assert.Equal(200 - expected, Count(e, "SELECT COUNT(*) FROM C"));
    }

    [Fact]
    public void Left_join_update_keeps_unmatched_outer_rows()
    {
        var e = Seeded();
        e.ExecuteNonQuery("INSERT INTO P (Id, Flag) VALUES (999, 0)"); // a parent with no children
        // LEFT JOIN so the childless parent survives as a null-padded row; C.Id IS NULL selects ONLY it — an
        // INNER join (dropping unmatched rows) could never match this, so it exercises true LEFT semantics.
        int affected = e.ExecuteNonQuery(
            "UPDATE P LEFT JOIN C ON P.Id = C.Pid SET P.Flag = -1 WHERE C.Id IS NULL");
        Assert.Equal(1, affected);
        Assert.Equal(-1L, Convert.ToInt64(e.ExecuteQuery("SELECT Flag FROM P WHERE Id = 999").Rows.Single()[0]!));
        Assert.Equal(0, Count(e, "SELECT COUNT(*) FROM P WHERE Flag = -1 AND Id <> 999")); // no matched parent touched
    }

    [Fact]
    public void Left_join_update_via_navigation_matches_inner_when_where_is_on_the_inner_side()
    {
        var e = Seeded();
        // The EF "set navigation null" shape: UPDATE outer LEFT JOIN inner … WHERE inner.col = v. Because the
        // WHERE rejects the null-padded rows, the result equals the INNER-join equivalent.
        int expected = Count(e, "SELECT COUNT(*) FROM P INNER JOIN C ON P.Id = C.Pid WHERE C.Amt = 0");
        int affected = e.ExecuteNonQuery("UPDATE P LEFT JOIN C ON P.Id = C.Pid SET P.Flag = 7 WHERE C.Amt = 0");
        Assert.Equal(expected, affected);
    }

    [Fact]
    public void Update_of_an_ace_authored_all_fixed_column_table_does_not_overflow()
    {
        // Regression: updating a row of an ACE-AUTHORED all-fixed-column table used to throw OverflowException.
        // The trigger is specific to rows written by Access/ACE (some decode to a 0 variable-data offset →
        // negative inferred fixed length); rows LibRed writes itself don't hit it, so this must update the real
        // Order Details table (OrderID, ProductID, UnitPrice, Quantity, Discount — all fixed) from the copied,
        // ACE-created Northwind, NOT a LibRed-created table.
        var e = Seeded();
        int affected = e.ExecuteNonQuery("UPDATE `Order Details` SET Quantity = 99 WHERE OrderID = 10248");
        Assert.True(affected > 0);
        Assert.Equal(affected, (int)Convert.ToInt64(
            e.ExecuteQuery("SELECT COUNT(*) FROM `Order Details` WHERE OrderID = 10248 AND Quantity = 99").Rows.Single()[0]!));
    }

    [Fact]
    public void Three_table_joined_update_matches_the_select()
    {
        var e = Seeded();
        // Third table via a second join; still an index-nested-loop, not a cartesian product.
        e.ExecuteNonQuery("CREATE TABLE G (Cid LONG PRIMARY KEY, Extra LONG)");
        for (int i = 0; i < 200; i++) e.ExecuteNonQuery($"INSERT INTO G (Cid, Extra) VALUES ({i}, {i})");
        int expected = Count(e,
            "SELECT COUNT(*) FROM C INNER JOIN P ON C.Pid = P.Id INNER JOIN G ON C.Id = G.Cid WHERE P.Flag = 1 AND G.Extra > 50");
        int affected = e.ExecuteNonQuery(
            "UPDATE C INNER JOIN P ON C.Pid = P.Id INNER JOIN G ON C.Id = G.Cid SET C.Amt = -1 WHERE P.Flag = 1 AND G.Extra > 50");
        Assert.Equal(expected, affected);
        Assert.Equal(expected, Count(e, "SELECT COUNT(*) FROM C WHERE Amt = -1"));
    }
}
