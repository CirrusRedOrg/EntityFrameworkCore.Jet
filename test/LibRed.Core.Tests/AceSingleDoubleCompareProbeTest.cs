using System.Data.OleDb;
using System.Globalization;
using Xunit;

namespace LibRed.Core.Tests;

// PROBE (not an assertion of LibRed behaviour): what precision does ACE use to compare a SINGLE column against
// a DOUBLE column, versus a SINGLE column against a literal? This decides whether LibRed's value-level
// "compare in single precision when a float is involved" rule matches ACE for the column-vs-column case, or
// whether a column-type-aware fix is needed. Reports counts; the assertions just pin what we observed so a
// future ACE change is noticed.
public class AceSingleDoubleCompareRegressionTests(ITestOutputHelper output)
{
    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    private static void Exec(OleDbConnection c, string sql) { using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
    private static int Count(OleDbConnection c, string sql) { using var cmd = c.CreateCommand(); cmd.CommandText = sql; return Convert.ToInt32(cmd.ExecuteScalar()); }

    [Fact]
    public void Ace_compares_single_and_double_in_single_precision()
    {
        // 0.1 has different 4-byte (single) and 8-byte (double) approximations, so a SINGLE column and a DOUBLE
        // column both set to 0.1 hold genuinely different numbers — equal only if compared in single precision.
        string s = (0.1).ToString("R", CultureInfo.InvariantCulture);

        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "acesd-");
        try
        {
            using var conn = OpenOleDb(path);
            Exec(conn, "CREATE TABLE SD (Id INT, S SINGLE, D DOUBLE)");
            Exec(conn, $"INSERT INTO SD (Id, S, D) VALUES (1, {s}, {s})");

            int colVsCol = Count(conn, "SELECT COUNT(*) FROM SD WHERE S = D");          // single col vs double col
            int singleVsLit = Count(conn, $"SELECT COUNT(*) FROM SD WHERE S = {s}");     // control: single col vs literal
            int doubleVsLit = Count(conn, $"SELECT COUNT(*) FROM SD WHERE D = {s}");     // control: double col vs literal

            output.WriteLine($"ACE  S=D (col vs col): {colVsCol}   S={s} (col vs lit): {singleVsLit}   D={s} (col vs lit): {doubleVsLit}");

            // The controls confirm ACE matches a column against its own-precision literal.
            Assert.Equal(1, singleVsLit);
            Assert.Equal(1, doubleVsLit);
            // The verdict (confirmed 2026-07-25): ACE compares a SINGLE column against a DOUBLE column in
            // SINGLE precision too — not double as SQL type-precedence would suggest. So ACE's rule is simply
            // "if a Single is involved, compare in single precision", which is exactly what LibRed's evaluator
            // now does (ExpressionEvaluator.Compare). This pins that parity: a regression here (0) would mean
            // ACE changed, or that LibRed should stop narrowing. See [[libred-single-precision-compare]].
            Assert.Equal(1, colVsCol);
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
