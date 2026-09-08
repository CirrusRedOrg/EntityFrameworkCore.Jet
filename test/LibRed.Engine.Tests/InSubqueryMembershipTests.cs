using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// An uncorrelated <c>IN (subquery)</c> tests membership against a hash set built when the body is hoisted
/// (<see cref="LibRed.Engine.Execution.HoistedInSet"/>), instead of walking the hoisted values for every outer
/// row. A hash only agrees with the evaluator's <c>=</c> within one type kind, so the set declines whenever it
/// cannot be sure — and these pin that it declines in the right places, because a wrong hash does not fail, it
/// silently drops matching rows.
/// </summary>
public class InSubqueryMembershipTests : TempDatabaseTest
{
    private static QueryEngine Seeded()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "in-membership-");
        var e = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));

        e.ExecuteNonQuery("CREATE TABLE Src (Id LONG PRIMARY KEY, N LONG, T TEXT(20))");
        e.ExecuteNonQuery("CREATE TABLE Probe (Id LONG PRIMARY KEY, N LONG, T TEXT(20))");

        e.ExecuteNonQuery("INSERT INTO Src (Id, N, T) VALUES (1, 5, 'abc')");
        e.ExecuteNonQuery("INSERT INTO Src (Id, N, T) VALUES (2, 7, 'def')");
        e.ExecuteNonQuery("INSERT INTO Src (Id, N, T) VALUES (3, 5, 'abc')");   // duplicate: a set must not change counts

        e.ExecuteNonQuery("INSERT INTO Probe (Id, N, T) VALUES (1, 5, 'abc')");
        e.ExecuteNonQuery("INSERT INTO Probe (Id, N, T) VALUES (2, 7, 'ABC')"); // case differs
        e.ExecuteNonQuery("INSERT INTO Probe (Id, N, T) VALUES (3, 9, 'zzz')"); // no match either way
        e.ExecuteNonQuery("INSERT INTO Probe (Id, N, T) VALUES (4, 5, '5')");   // text probe against numeric body
        return e;
    }

    private static long[] Ids(QueryEngine e, string sql)
        => e.ExecuteQuery(sql).Rows.Select(r => Convert.ToInt64(r[0])).ToArray();

    [Fact]
    public void Numeric_membership_matches()
        => Assert.Equal([1, 2, 4], Ids(Seeded(), "SELECT p.Id FROM Probe AS p WHERE p.N IN (SELECT s.N FROM Src AS s)"));

    [Fact]
    public void Text_membership_is_case_insensitive_as_the_evaluator_is()
        // The set hashes text through Access's case-insensitive, trailing-space-trimmed collation, so 'ABC'
        // must match 'abc' exactly as the linear comparison did.
        => Assert.Equal([1, 2], Ids(Seeded(), "SELECT p.Id FROM Probe AS p WHERE p.T IN (SELECT s.T FROM Src AS s)"));

    [Fact]
    public void A_duplicate_in_the_body_does_not_change_the_answer()
        // Src has 5 twice. A set collapses them; the answer must not move.
        => Assert.Equal([1, 4], Ids(Seeded(), "SELECT p.Id FROM Probe AS p WHERE p.N IN (SELECT s.N FROM Src AS s WHERE s.N = 5)"));

    [Fact]
    public void A_text_probe_against_a_numeric_body_still_matches()
    {
        // `5 = '5'` under the evaluator's coercions, but no single hash agrees with that across kinds — so the
        // set must decline a probe of a different kind and let the comparison answer. Probe row 4 holds '5'.
        QueryEngine e = Seeded();
        Assert.Equal([4], Ids(e, "SELECT p.Id FROM Probe AS p WHERE p.T IN (SELECT s.N FROM Src AS s WHERE s.N = 5)"));
    }

    [Fact]
    public void A_numeric_probe_against_a_text_body_still_matches()
    {
        QueryEngine e = Seeded();
        e.ExecuteNonQuery("INSERT INTO Src (Id, N, T) VALUES (4, 99, '5')");
        Assert.Equal([1, 4], Ids(e, "SELECT p.Id FROM Probe AS p WHERE p.N IN (SELECT s.T FROM Src AS s WHERE s.T = '5')"));
    }

    [Fact]
    public void A_mixed_kind_body_falls_back_to_comparison()
    {
        // The body yields a number for one row and text for the others, so no consistent hash exists over it and
        // the set must decline the whole body rather than hash part of it.
        QueryEngine e = Seeded();
        Assert.Equal(
            [1, 2],
            Ids(e, "SELECT p.Id FROM Probe AS p WHERE p.T IN (SELECT IIF(s.Id = 2, 1, 'abc') FROM Src AS s)"));
    }

    [Fact]
    public void Cross_numeric_types_match_as_the_evaluator_does()
    {
        // 5 = 5.0: both numeric, so they share a kind and the set hashes them alike.
        QueryEngine e = Seeded();
        e.ExecuteNonQuery("CREATE TABLE D (Id LONG PRIMARY KEY, V DOUBLE)");
        e.ExecuteNonQuery("INSERT INTO D (Id, V) VALUES (1, 5.0)");
        Assert.Equal([1, 4], Ids(e, "SELECT p.Id FROM Probe AS p WHERE p.N IN (SELECT d.V FROM D AS d)"));
    }

    // --- three-valued semantics, which the set must reproduce, not just membership --------------------------

    [Fact]
    public void A_null_in_the_body_makes_a_non_match_unknown_not_false()
    {
        // With a null present, a row that matches nothing is UNKNOWN and so excluded — the same as a non-match,
        // but it must NOT come back as a match either. Row 3 (N = 9) matches nothing.
        QueryEngine e = Seeded();
        e.ExecuteNonQuery("INSERT INTO Src (Id, N, T) VALUES (4, NULL, NULL)");
        Assert.Equal([1, 2, 4], Ids(e, "SELECT p.Id FROM Probe AS p WHERE p.N IN (SELECT s.N FROM Src AS s)"));
    }

    [Fact]
    public void Not_in_over_a_body_holding_a_null_yields_no_rows()
    {
        // The classic NOT IN trap: every row is UNKNOWN, so none survive. If the set reported HasNull wrongly,
        // the non-matching rows would come back.
        QueryEngine e = Seeded();
        e.ExecuteNonQuery("INSERT INTO Src (Id, N, T) VALUES (4, NULL, NULL)");
        Assert.Empty(Ids(e, "SELECT p.Id FROM Probe AS p WHERE p.N NOT IN (SELECT s.N FROM Src AS s)"));
    }

    [Fact]
    public void Not_in_over_a_body_with_no_nulls_excludes_only_the_matches()
        => Assert.Equal([3], Ids(Seeded(), "SELECT p.Id FROM Probe AS p WHERE p.N NOT IN (SELECT s.N FROM Src AS s)"));

    [Fact]
    public void An_empty_body_matches_nothing_and_not_in_matches_everything()
    {
        QueryEngine e = Seeded();
        Assert.Empty(Ids(e, "SELECT p.Id FROM Probe AS p WHERE p.N IN (SELECT s.N FROM Src AS s WHERE s.Id < 0)"));
        Assert.Equal([1, 2, 3, 4], Ids(e, "SELECT p.Id FROM Probe AS p WHERE p.N NOT IN (SELECT s.N FROM Src AS s WHERE s.Id < 0)"));
    }

    [Fact]
    public void A_null_probe_value_is_unknown_whatever_the_body_holds()
    {
        QueryEngine e = Seeded();
        e.ExecuteNonQuery("INSERT INTO Probe (Id, N, T) VALUES (5, NULL, 'q')");
        Assert.DoesNotContain(5L, Ids(e, "SELECT p.Id FROM Probe AS p WHERE p.N IN (SELECT s.N FROM Src AS s)"));
        Assert.DoesNotContain(5L, Ids(e, "SELECT p.Id FROM Probe AS p WHERE p.N NOT IN (SELECT s.N FROM Src AS s)"));
    }
}
