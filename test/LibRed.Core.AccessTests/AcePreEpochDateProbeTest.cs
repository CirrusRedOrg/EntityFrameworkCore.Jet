using System.Data.OleDb;
using System.Globalization;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

// PROBE (not an assertion of LibRed behaviour): how does ACE handle dates BEFORE the OLE Automation epoch?
//
// Background: an OA DATE is a double — integer part days since 1899-12-30, fraction the time of day. Below the
// epoch the representation is genuinely odd: the integer part goes negative but THE TIME FRACTION STAYS POSITIVE.
// So 1899-12-29 06:00 is -1.25 while 1899-12-29 18:00 is -1.75 — later in the day is a SMALLER double. Two
// consequences for LibRed, which manipulates these serials directly:
//   * ExpressionEvaluator.Arithmetic adds/subtracts raw OA doubles before calling FromOADate, which is not the
//     same as adding days to a date once the value is negative.
//   * IndexKeyEncoder encodes the OA double as the sort key, and raw doubles do not order correctly within a
//     pre-epoch day (see above).
// ACE may well have inherited the same weirdness, in which case LibRed is already bug-compatible and should stay
// that way. This probe establishes which it is. The existing DateAdd/DateDiff functional tests do not cover it:
// they all use modern (Northwind-era) dates, where the serial is positive and the anomaly cannot appear.
[Collection(AceCollection.Name)]
public class AcePreEpochDateRegressionTests(ITestOutputHelper output)
{
    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    private static void Exec(OleDbConnection c, string sql) { using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }

    private static object? Scalar(OleDbConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        var v = cmd.ExecuteScalar();
        return v == DBNull.Value ? null : v;
    }

    private static string Describe(object? v) => v switch
    {
        null => "NULL",
        double d => $"{d.ToString("R", CultureInfo.InvariantCulture)} (Double)",
        DateTime dt => $"{dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} (DateTime)",
        _ => $"{Convert.ToString(v, CultureInfo.InvariantCulture)} ({v?.GetType().Name})",
    };

    private void Report(OleDbConnection c, string label, string expr)
    {
        string value;
        try { value = Describe(Scalar(c, $"SELECT {expr} FROM `P`")); }
        catch (OleDbException ex) { value = $"<ACE error: {ex.Message.Trim()}>"; }
        output.WriteLine($"{label,-34} {expr,-42} = {value}");
    }

    [Fact]
    public void Ace_uses_oa_serial_comparison_and_date_space_functions_before_the_epoch()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "acepre-");
        try
        {
            using var conn = OpenOleDb(path);
            Exec(conn, "CREATE TABLE `P` (`Id` INT, `D` DATETIME)");
            Exec(conn, "INSERT INTO `P` (`Id`, `D`) VALUES (1, #12/29/1899 06:00:00#)");

            output.WriteLine("--- 1. The serial itself: is the time fraction positive below the epoch? ---");
            // If ACE follows OA, morning is -1.25 and evening is -1.75 (evening = the SMALLER number).
            Report(conn, "CDbl(1899-12-29 06:00)", "CDbl(#12/29/1899 06:00:00#)");
            Report(conn, "CDbl(1899-12-29 18:00)", "CDbl(#12/29/1899 18:00:00#)");
            Report(conn, "CDbl(1899-12-30 00:00) [epoch]", "CDbl(#12/30/1899 00:00:00#)");
            Report(conn, "CDbl(1899-12-31 06:00)", "CDbl(#12/31/1899 06:00:00#)");
            Report(conn, "CDbl(1850-06-15 10:30)", "CDbl(#06/15/1850 10:30:00#)");

            output.WriteLine("--- 2. Round-tripping a raw serial back to a date ---");
            Report(conn, "CDate(-1.25)", "CDate(-1.25)");
            Report(conn, "CDate(-1.75)", "CDate(-1.75)");

            output.WriteLine("--- 3. Arithmetic across and below the epoch ---");
            Report(conn, "date + 1 (pre-epoch)", "#12/29/1899 06:00:00# + 1");
            Report(conn, "date - 1 (pre-epoch)", "#12/29/1899 06:00:00# - 1");
            Report(conn, "date + 0.5 (pre-epoch)", "#12/29/1899 06:00:00# + 0.5");
            Report(conn, "DateAdd d +1", "DateAdd('d', 1, #12/29/1899 06:00:00#)");
            Report(conn, "DateAdd h +12", "DateAdd('h', 12, #12/29/1899 06:00:00#)");
            Report(conn, "DateDiff d across epoch", "DateDiff('d', #12/28/1899 00:00:00#, #01/02/1900 00:00:00#)");
            Report(conn, "DateDiff h same pre-epoch day", "DateDiff('h', #12/29/1899 06:00:00#, #12/29/1899 18:00:00#)");

            output.WriteLine("--- 4. Comparison within a pre-epoch day (raw-double ordering would invert this) ---");
            Report(conn, "06:00 < 18:00 ?", "(#12/29/1899 06:00:00# < #12/29/1899 18:00:00#)");

            output.WriteLine("--- 5. ORDER BY across the epoch ---");
            Exec(conn, "CREATE TABLE `S` (`Id` INT, `D` DATETIME)");
            Exec(conn, "INSERT INTO `S` (`Id`, `D`) VALUES (1, #06/15/1850 10:30:00#)");
            Exec(conn, "INSERT INTO `S` (`Id`, `D`) VALUES (2, #12/29/1899 06:00:00#)");
            Exec(conn, "INSERT INTO `S` (`Id`, `D`) VALUES (3, #12/29/1899 18:00:00#)");
            Exec(conn, "INSERT INTO `S` (`Id`, `D`) VALUES (4, #12/30/1899 00:00:00#)");
            Exec(conn, "INSERT INTO `S` (`Id`, `D`) VALUES (5, #01/02/1900 12:00:00#)");
            Exec(conn, "INSERT INTO `S` (`Id`, `D`) VALUES (6, #01/01/2000 00:00:00#)");

            string order;
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT `Id` FROM `S` ORDER BY `D`";
                using var r = cmd.ExecuteReader();
                var ids = new List<int>();
                while (r.Read()) ids.Add(r.GetInt32(0));
                order = string.Join(",", ids);
            }
            output.WriteLine($"ORDER BY `D` -> Id sequence: {order}   (chronological is 1,2,3,4,5,6)");

            // ---------------------------------------------------------------------------------------------
            // Verdict (observed 2026-08-14, ACE OLE DB) — pinned so a change is noticed.
            //
            // ACE inherits the OA representation exactly: below the epoch the day count is negative but the
            // time fraction is still added, so 06:00 is -1.25 and 18:00 is -1.75 on the same day.
            Assert.Equal(-1.25d, Scalar(conn, "SELECT CDbl(#12/29/1899 06:00:00#) FROM `P`"));
            Assert.Equal(-1.75d, Scalar(conn, "SELECT CDbl(#12/29/1899 18:00:00#) FROM `P`"));

            // The DATE FUNCTIONS are correct — they work in date space, not on the raw serial. This is why the
            // existing DateAdd/DateDiff functional tests never revealed any of this.
            Assert.Equal(new DateTime(1899, 12, 29, 18, 0, 0), Scalar(conn, "SELECT DateAdd('h', 12, #12/29/1899 06:00:00#) FROM `P`"));
            Assert.Equal(12, Scalar(conn, "SELECT DateDiff('h', #12/29/1899 06:00:00#, #12/29/1899 18:00:00#) FROM `P`"));

            // COMPARISON AND ORDERING are not: they use the raw double, so within a pre-epoch day ACE puts
            // later times FIRST. 06:00 < 18:00 evaluates to False (0), and ORDER BY returns 1,3,2,4,5,6 —
            // row 3 (18:00) ahead of row 2 (06:00). This is a genuine ACE defect, faithfully inherited from
            // the OA DATE representation. LibRed's IndexKeyEncoder encodes the same raw serial, so it is
            // expected to be bug-compatible here; the evaluator comparing CLR DateTime values is NOT, and
            // would order these correctly — a parity gap, in the direction of being right.
            Assert.Equal((short)0, Scalar(conn, "SELECT (#12/29/1899 06:00:00# < #12/29/1899 18:00:00#) FROM `P`"));
            Assert.Equal("1,3,2,4,5,6", order);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Below 100-01-01 an OA DATE still has a representation — the serial just keeps falling — but .NET's
    // ToOADate refuses it, except DateTime.MinValue, which it maps to 0.0: the epoch. What ACE does at that
    // edge decides whether LibRed should store such a date or refuse it. Asked through ACE's own SQL: the
    // managed OLE DB provider converts a DateTime parameter with that same ToOADate before ACE sees it.
    [Fact]
    public void Ace_dates_before_the_year_100()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "acey100-");
        try
        {
            using var conn = OpenOleDb(path);
            Exec(conn, "CREATE TABLE `Y` (`Id` INT, `D` DATETIME)");

            void Insert(string label, int id, string value)
            {
                string outcome;
                try
                {
                    Exec(conn, $"INSERT INTO `Y` (`Id`, `D`) VALUES ({id}, {value})");
                    outcome = $"serial {Describe(Scalar(conn, $"SELECT CDbl(`D`) FROM `Y` WHERE `Id` = {id}"))}, "
                              + $"text {Describe(Scalar(conn, $"SELECT CStr(`D`) FROM `Y` WHERE `Id` = {id}"))}";
                }
                catch (OleDbException ex) { outcome = $"<ACE error: {ex.Message.Trim()}>"; }
                output.WriteLine($"{label,-30} {outcome}");
            }

            Insert("#0100-01-01#", 1, "#0100-01-01#");
            Insert("CDate(-657434) [100-01-01]", 2, "CDate(-657434)");
            Insert("CDate(-657435) [one before]", 3, "CDate(-657435)");
            Insert("-700000 (a number)", 6, "-700000");

            // Verdict (observed 2026-09-23, ACE OLE DB). 100-01-01, serial -657434, is the floor: it stores
            // either way it is written, and one day below it is refused, as is any serial further down.
            Assert.Equal(-657434d, Scalar(conn, "SELECT CDbl(`D`) FROM `Y` WHERE `Id` = 1"));
            Assert.Equal(-657434d, Scalar(conn, "SELECT CDbl(`D`) FROM `Y` WHERE `Id` = 2"));
            Assert.Null(Scalar(conn, "SELECT `Id` FROM `Y` WHERE `Id` = 3"));
            Assert.Null(Scalar(conn, "SELECT `Id` FROM `Y` WHERE `Id` = 6"));
            // Not asked through a literal: a year under 100 there is read as a two-digit year and placed by the
            // machine's calendar window, so which date it names depends on the computer, not on ACE.
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // LibRed at the same floor: 0100-01-01 is stored at the serial ACE gives it, and anything earlier is refused
    // with the column named — DateTime.MinValue included, which would otherwise be stored as the epoch.
    [Fact]
    public void Libred_refuses_a_date_before_the_year_100_as_ace_does()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "libredy100-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("Y",
                    [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("D", JetDataType.DateTime, 8, IsFixedLength: true)],
                    primaryKey: ["Id"]);
                db.CreateIndex("Y", "IX_D", [("D", false)]);
                var table = db.OpenTable("Y");
                table.Insert([1, new DateTime(100, 1, 1)]);

                foreach (DateTime early in new[] { new DateTime(99, 12, 31), DateTime.MinValue })
                {
                    var error = Assert.Throws<InvalidOperationException>(() => table.Insert([2, early]));
                    output.WriteLine(error.Message);
                    Assert.Contains("'D'", error.Message);
                }
            }

            using var conn = OpenOleDb(path);
            Assert.Equal(1, Scalar(conn, "SELECT COUNT(*) FROM `Y`"));
            Assert.Equal(-657434d, Scalar(conn, "SELECT CDbl(`D`) FROM `Y` WHERE `Id` = 1"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
