using System.Data.OleDb;
using System.Globalization;
using Xunit;

namespace LibRed.Core.Tests;

// PROBE (not an assertion of LibRed behaviour): what exactly do ACE's VBA conversion functions return, for the
// cases where .NET's Convert.* stands in for OLE Automation's and the two may have drifted apart?
//
// Background: OA's double->decimal conversion (VarDecFromR8) rounded at 15 significant digits. dotnet/runtime#130566
// (.NET 11 preview 7) replaced Convert.ToDecimal(double) with a correctly-rounded full-precision conversion, which
// broke EFCore.Jet's reader (fixed by JetDecimalConverter). LibRed's ExpressionEvaluator calls the same Convert.*
// APIs, so the question is which of its VBA functions ACE would now disagree with. The 15-digit convention shows up
// in string form too: VB formats a Double at ~15 significant digits, while .NET Core 3.0+ returns the shortest
// round-trippable form.
//
// Probes, in order:
//   1. CDec(<double>)  - the direct analogue of the reader bug. Evaluator does a bare Convert.ToDecimal.
//   2. CCur(<double>)  - expected safe: the evaluator quantises to 4 dp straight after, killing any noise.
//   3. CStr(<double>)  - 15 significant digits (VB), or shortest-round-trippable (.NET)?
//   4. CInt/CLng(True) - VARIANT_TRUE is all-bits-set, so VBA gives -1; Convert.ToInt16(true) gives 1.
//   5. CBool('-1')     - VBA accepts numeric strings; Convert.ToBoolean parses only "True"/"False".
// TypeName/VarType are reported alongside each so the result type is pinned too, not just the value.
//
// Output is written to the test log; the assertions pin only what has actually been observed, so a future ACE
// change (or a wrong assumption on our side) is noticed rather than silently absorbed.
public class AceVbaConversionRegressionTests(ITestOutputHelper output)
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

    /// <summary>Renders a value without losing digits, so a 15-digit result is distinguishable from a full-precision one.</summary>
    private static string Describe(object? v) => v switch
    {
        null => "NULL",
        decimal d => $"{d.ToString("G30", CultureInfo.InvariantCulture)} ({v.GetType().Name})",
        double d => $"{d.ToString("R", CultureInfo.InvariantCulture)} ({v.GetType().Name})",
        float f => $"{f.ToString("R", CultureInfo.InvariantCulture)} ({v.GetType().Name})",
        _ => $"{Convert.ToString(v, CultureInfo.InvariantCulture)} ({v.GetType().Name})",
    };

    /// <summary>Runs one expression and logs its value, plus ACE's own TypeName/VarType for it. Never throws:
    /// a rejected expression is a result too (e.g. if CBool refuses a numeric string).</summary>
    private void Report(OleDbConnection c, string label, string expr)
    {
        string value, type;
        try { value = Describe(Scalar(c, $"SELECT {expr} FROM `P`")); }
        catch (OleDbException ex) { value = $"<ACE error: {ex.Message.Trim()}>"; }

        try { type = $"{Scalar(c, $"SELECT TypeName({expr}) FROM `P`")}/{Scalar(c, $"SELECT VarType({expr}) FROM `P`")}"; }
        catch (OleDbException) { type = "-"; }

        output.WriteLine($"{label,-22} {expr,-26} = {value,-46} TypeName/VarType: {type}");
    }

    [Fact]
    public void Ace_vba_conversion_values_and_provider_types_are_pinned()
    {
        // 58.6 as a double is really 58.600000000000001421085471520..., so CDec either rounds it back to 58.6
        // (OA's 15-digit VarDecFromR8) or expands the exact binary value. This is the value that broke
        // Sum_over_round_works_correctly_in_projection.
        string d586 = (58.6).ToString("R", CultureInfo.InvariantCulture);

        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "acevba-");
        try
        {
            using var conn = OpenOleDb(path);
            Exec(conn, "CREATE TABLE `P` (`Id` INT, `D` DOUBLE, `C` CURRENCY)");
            Exec(conn, $"INSERT INTO `P` (`Id`, `D`, `C`) VALUES (1, {d586}, 58.6)");

            output.WriteLine("--- 1. CDec: does ACE round a Double back to 15 significant digits? ---");
            Report(conn, "CDec(double col)", "CDec(`D`)");
            Report(conn, "CDec(literal)", $"CDec({d586})");
            Report(conn, "CDec(1/3)", "CDec(1/3)");
            Report(conn, "CDec(0.1+0.2)", "CDec(0.1+0.2)");
            Report(conn, "control: currency", "CDec(`C`)");

            output.WriteLine("--- 2. CCur: expected safe (quantised to 4 dp) ---");
            Report(conn, "CCur(double col)", "CCur(`D`)");
            Report(conn, "CCur(1/3)", "CCur(1/3)");

            output.WriteLine("--- 3. CStr: 15 significant digits, or shortest round-trippable? ---");
            Report(conn, "CStr(0.1+0.2)", "CStr(0.1+0.2)");
            Report(conn, "CStr(1/3)", "CStr(1/3)");
            Report(conn, "CStr(double col)", "CStr(`D`)");

            output.WriteLine("--- 4. Boolean -> integer: VARIANT_TRUE is -1, Convert.ToInt16(true) is 1 ---");
            Report(conn, "CInt(True)", "CInt(True)");
            Report(conn, "CLng(True)", "CLng(True)");
            Report(conn, "CDbl(True)", "CDbl(True)");
            Report(conn, "CStr(True)", "CStr(True)");
            Report(conn, "True + 0", "(True + 0)");

            output.WriteLine("--- 4b. Single formatting, and the types that cannot hold -1 ---");
            Report(conn, "CStr(CSng(1/3))", "CStr(CSng(1/3))");
            Report(conn, "CStr(CSng(0.1))", "CStr(CSng(0.1))");
            Report(conn, "CByte(True)", "CByte(True)");
            Report(conn, "CSng(True)", "CSng(True)");
            Report(conn, "CCur(True)", "CCur(True)");

            output.WriteLine("--- 5. CBool on strings/numbers ---");
            Report(conn, "CBool('-1')", "CBool('-1')");
            Report(conn, "CBool('True')", "CBool('True')");
            Report(conn, "CBool(0.5)", "CBool(0.5)");

            // ---------------------------------------------------------------------------------------------
            // Verdict (observed 2026-08-14, ACE OLE DB). Each assertion pins a fact, so a change is noticed.
            // ---------------------------------------------------------------------------------------------

            // 1. CDec DOES NOT EXIST in the Jet Expression Service. ACE rejects it with its generic unknown-
            //    function error ("Wrong number of arguments used with function in query expression"), for a
            //    column, a literal, anything. So there is no ACE behaviour for LibRed's CDEC to match: LibRed
            //    accepting CDec is a superset of ACE, not a parity obligation. CCur is the supported route to a
            //    decimal, and it quantises to 4 dp.
            Assert.Throws<OleDbException>(() => Scalar(conn, "SELECT CDec(1) FROM `P`"));

            // 2. CCur quantises to 4 dp and reports as Currency (vbCurrency = 6) - matches the evaluator's
            //    Math.Round(Convert.ToDecimal(v), 4).
            Assert.Equal(0.3333m, Assert.IsType<decimal>(Scalar(conn, "SELECT CCur(1/3) FROM `P`")));

            // 3. CStr formats a Double at 15 significant digits - the OA/VB convention, NOT .NET Core 3.0+'s
            //    shortest-round-trippable form. LibRed's Convert.ToString(double) returns "0.30000000000000004"
            //    and "0.333333333333333333" respectively, so this is a real parity gap.
            Assert.Equal("0.3", Scalar(conn, "SELECT CStr(0.1+0.2) FROM `P`"));
            Assert.Equal("0.333333333333333", Scalar(conn, "SELECT CStr(1/3) FROM `P`"));

            // 4. Booleans convert as VARIANT_BOOL: True is -1, not 1. Convert.ToInt16(true) gives 1, so
            //    CInt/CLng/CDbl on a Boolean are all wrong in LibRed today. Note CStr(True) is "-1" here, NOT
            //    "True" as VBA proper would render it - JES differs from the VBA runtime, which is exactly why
            //    this is probed rather than assumed.
            Assert.Equal((short)-1, Scalar(conn, "SELECT CInt(True) FROM `P`"));
            Assert.Equal(-1, Scalar(conn, "SELECT CLng(True) FROM `P`"));
            Assert.Equal(-1d, Scalar(conn, "SELECT CDbl(True) FROM `P`"));
            Assert.Equal("-1", Scalar(conn, "SELECT CStr(True) FROM `P`"));

            // 5. CBool accepts numeric strings and non-integral numbers; Convert.ToBoolean("-1") throws a
            //    FormatException, so LibRed rejects input ACE accepts.
            Assert.Equal((short)-1, Scalar(conn, "SELECT CBool('-1') FROM `P`"));
            Assert.Equal((short)-1, Scalar(conn, "SELECT CBool(0.5) FROM `P`"));

            // Pin the remaining values emitted by the diagnostic table as well; the output is explanatory,
            // not an unasserted exploratory branch.
            Assert.Equal(58.6000m, Scalar(conn, "SELECT CCur(`D`) FROM `P`"));
            Assert.Equal("58.6", Scalar(conn, "SELECT CStr(`D`) FROM `P`"));
            // Do not pin OLE DB's CLR box here: computed values are frequently widened or otherwise
            // misrepresented by the provider. The numeric result is the contract (JetDataReader normalizes it).
            Assert.Equal(-1f, Convert.ToSingle(Scalar(conn, "SELECT CSng(True) FROM `P`")));
            Assert.Equal(-1.0000m, Scalar(conn, "SELECT CCur(True) FROM `P`"));
            Assert.Throws<OleDbException>(() => Scalar(conn, "SELECT CByte(True) FROM `P`"));
            Assert.Equal((short)-1, Scalar(conn, "SELECT CBool('True') FROM `P`"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
