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
        e.ExecuteNonQuery("CREATE TABLE F (Id LONG PRIMARY KEY, N LONG)"); // all fixed-length columns
        for (int i = 0; i < 40; i++) e.ExecuteNonQuery($"INSERT INTO P (Id, Flag) VALUES ({i}, {i % 2})");
        for (int i = 0; i < 200; i++) e.ExecuteNonQuery($"INSERT INTO C (Id, Pid, Amt) VALUES ({i}, {i % 40}, {i})");
        for (int i = 0; i < 20; i++) e.ExecuteNonQuery($"INSERT INTO F (Id, N) VALUES ({i}, {i})");
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
    public void Update_of_an_all_fixed_column_table_does_not_overflow()
    {
        // Regression: re-encoding a row of a table with no variable-length columns used to throw
        // OverflowException (negative inferred fixed-data length) on the UPDATE path.
        var e = Seeded();
        int affected = e.ExecuteNonQuery("UPDATE F SET N = 99 WHERE Id = 5");
        Assert.Equal(1, affected);
        Assert.Equal(99L, Convert.ToInt64(e.ExecuteQuery("SELECT N FROM F WHERE Id = 5").Rows.Single()[0]!));
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
