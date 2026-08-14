using System.Linq;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// Dates before the OLE Automation epoch (1899-12-30) have a negative day count but a still-positive time
/// fraction, so 06:00 is -1.25 and 18:00 is -1.75 on the same day — later in the day is the SMALLER serial.
/// ACE compares and sorts on that raw serial, so it puts later pre-epoch times FIRST (verified in
/// LibRed.Core.Tests.AcePreEpochDateProbeTest: ORDER BY returned 1,3,2,4,5,6, and 06:00 &lt; 18:00 is False).
/// Its date FUNCTIONS are unaffected — DateAdd/DateDiff work in date space — which is why the DateAdd/DateDiff
/// functional tests never surfaced this.
///
/// This is not hypothetical: the GearsOfWar test model stores DateTimes in the year 102 (raised from the
/// original year 2, which is unusable because years below 100 do not round-trip), so pre-epoch serials are
/// live data in the suite.
///
/// What matters most here is INTERNAL CONSISTENCY. LibRed has two paths to an ordered or filtered result: the
/// index (IndexKeyEncoder writes the raw OA serial as the sort key, so it inherits ACE's ordering) and the
/// evaluator (which compares CLR DateTime values, and is therefore chronologically correct). If those two
/// disagree, the same query returns different answers depending on whether the planner picks a seek or a scan.
/// These tests pin that they agree, and record which convention the agreement follows.
/// </summary>
public class PreEpochDateOrderingTests
{
    /// <summary>Two tables with identical rows: <c>T</c> indexed on the date column, <c>U</c> not — so the same
    /// query exercises the index path and the scan path.</summary>
    private static QueryEngine Seeded()
    {
        string path = Path.Combine(Path.GetTempPath(), $"preepoch-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE T (Id LONG PRIMARY KEY, D DATETIME)");
        e.ExecuteNonQuery("CREATE INDEX IX_T_D ON T (D)");
        e.ExecuteNonQuery("CREATE TABLE U (Id LONG PRIMARY KEY, D DATETIME)");

        // Chronological order is 1..6. Rows 2 and 3 are the same pre-epoch DAY at different times, which is the
        // case that inverts under raw-serial ordering. Row 1 is GearsOfWar-style (year 102).
        string[] values =
        [
            "(1, #01/15/0102 06:00:00#)",   // year 102 — as used by the GearsOfWar model
            "(2, #12/29/1899 06:00:00#)",   // serial -1.25
            "(3, #12/29/1899 18:00:00#)",   // serial -1.75 — later in the day, smaller serial
            "(4, #12/30/1899 00:00:00#)",   // the epoch itself, serial 0
            "(5, #01/02/1900 12:00:00#)",
            "(6, #01/01/2000 00:00:00#)",
        ];
        foreach (string v in values)
        {
            e.ExecuteNonQuery($"INSERT INTO T (Id, D) VALUES {v}");
            e.ExecuteNonQuery($"INSERT INTO U (Id, D) VALUES {v}");
        }

        return e;
    }

    private static string Order(QueryEngine e, string sql)
        => string.Join(",", e.ExecuteQuery(sql).Rows.Select(r => r[0]!.ToString()));

    [Fact]
    public void Index_and_scan_agree_on_pre_epoch_ordering()
    {
        var e = Seeded();

        string indexed = Order(e, "SELECT Id FROM T ORDER BY D");
        string scanned = Order(e, "SELECT Id FROM U ORDER BY D");

        // The point of the test: whichever convention LibRed follows, both paths must follow the same one.
        Assert.Equal(scanned, indexed);

        // And the convention is ACE's — ordering by the raw OA serial, so the 18:00 row (3) precedes the 06:00
        // row (2) on the same pre-epoch day. This is the exact sequence ACE produced for the same six rows in
        // LibRed.Core.Tests.AcePreEpochDateProbeTest, so it doubles as a cross-engine parity check.
        Assert.Equal("1,3,2,4,5,6", indexed);
    }

    [Fact]
    public void Index_and_scan_agree_on_a_pre_epoch_range_predicate()
    {
        var e = Seeded();

        // A range that straddles the epoch, so the seek has to walk keys on both sides of zero.
        const string predicate = "WHERE D > #12/29/1899 00:00:00# AND D < #01/02/1900 00:00:00#";
        string indexed = Order(e, $"SELECT Id FROM T {predicate} ORDER BY Id");
        string scanned = Order(e, $"SELECT Id FROM U {predicate} ORDER BY Id");

        Assert.Equal(scanned, indexed);

        // Only row 4 qualifies: the lower bound 1899-12-29 00:00 is serial -1.0, while rows 2 and 3 sit at
        // -1.25 and -1.75, i.e. BELOW it in serial space even though they are chronologically later. ACE
        // excludes them for the same reason. Before the evaluator moved to serial comparison the scan returned
        // "2,3,4" here while the index seek returned "4" — the disagreement this class exists to prevent.
        Assert.Equal("4", indexed);
    }

    [Fact]
    public void Comparison_within_a_pre_epoch_day_is_consistent_with_ordering()
    {
        var e = Seeded();

        // Rows 2 (06:00) and 3 (18:00) are the same day. Whether the engine calls 06:00 "less than" 18:00 must
        // match the order ORDER BY puts them in, or a WHERE and an ORDER BY on the same column contradict.
        string order = Order(e, "SELECT Id FROM U WHERE Id IN (2, 3) ORDER BY D");
        bool morningFirst = order == "2,3";

        object? cmp = e.ExecuteQuery(
            "SELECT COUNT(*) FROM U WHERE Id = 2 AND D < #12/29/1899 18:00:00#").Rows.First()[0];
        bool morningIsLess = Convert.ToInt32(cmp) == 1;

        Assert.Equal(morningFirst, morningIsLess);
    }
}
