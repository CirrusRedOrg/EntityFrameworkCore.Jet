using System.Data.OleDb;
using Xunit;

namespace LibRed.Core.Tests;

// PROBE (not an assertion of provider behaviour): which VBA function arguments raise on NULL rather than
// propagating it? Relational semantics say NULL in, NULL out; Access's VBA runtime disagrees for some functions
// and errors instead, which surfaces as "Invalid use of Null" or "Type mismatch" mid-query.
//
// This matters because the provider's protection currently rides on Convert nodes: EF 10 wrapped the length of
// a Substring in a conversion, JetQuerySqlGenerator wrapped that conversion in IIF(x IS NULL, NULL, ...), and the
// guard came along for free. EF 11 elides the redundant conversion, so MID now receives a NULL length directly
// and ACE errors (the GearsOfWar Null_semantics_..._optional_navigation_complex failures). Guarding has to move
// to the functions themselves, so this establishes which arguments actually need it.
public class AceNullArgumentRegressionTests(ITestOutputHelper output)
{
    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    private static void Exec(OleDbConnection c, string sql) { using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }

    [Fact]
    public void Vba_numeric_arguments_reject_null_while_value_arguments_propagate_it()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "acenull-");
        try
        {
            using var conn = OpenOleDb(path);
            // One row whose every column is NULL, so a NULL reaches the function from a column rather than a
            // literal — a literal NULL can be folded away before it ever reaches ACE.
            Exec(conn, "CREATE TABLE `N` (`Id` INT, `S` TEXT(50), `I` INT)");
            Exec(conn, "INSERT INTO `N` (`Id`, `S`, `I`) VALUES (1, NULL, NULL)");

            void Try(string label, string expr)
            {
                string result;
                try
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = $"SELECT {expr} FROM `N`";
                    var v = cmd.ExecuteScalar();
                    result = v is null or DBNull ? "NULL (propagates)" : $"'{v}'";
                }
                catch (OleDbException ex)
                {
                    result = "RAISES: " + ex.Message.Trim();
                }

                output.WriteLine($"{label,-34} {result}");
            }

            Try("MID(null string, 1, 2)", "MID(`S`, 1, 2)");
            Try("MID('abc', 1, null length)", "MID('abc', 1, `I`)");
            Try("MID('abc', null start, 2)", "MID('abc', `I`, 2)");
            Try("MID('abc', 1, LEN(null))", "MID('abc', 1, LEN(`S`))");
            Try("LEFT(null, 2)", "LEFT(`S`, 2)");
            Try("LEFT('abc', null)", "LEFT('abc', `I`)");
            Try("RIGHT('abc', null)", "RIGHT('abc', `I`)");
            Try("LEN(null)", "LEN(`S`)");
            Try("INSTR(null, 'a')", "INSTR(`S`, 'a')");
            Try("INSTR('abc', null)", "INSTR('abc', `S`)");
            Try("INSTR(null start, 'abc', 'a')", "INSTR(`I`, 'abc', 'a')");
            Try("UCASE(null)", "UCASE(`S`)");
            Try("TRIM(null)", "TRIM(`S`)");
            Try("STRING(null, 'a')", "STRING(`I`, 'a')");
            Try("SPACE(null)", "SPACE(`I`)");
            Try("CHR(null)", "CHR(`I`)");
            Try("ABS(null)", "ABS(`I`)");
            Try("ROUND(null, 2)", "ROUND(`I`, 2)");
            Try("DATEADD('d', null, #1/1/2000#)", "DATEADD('d', `I`, #01/01/2000#)");
            Try("DATEDIFF('d', null, #1/1/2000#)", "DATEDIFF('d', `S`, #01/01/2000#)");
            Try("IIF(null, 1, 2)", "IIF(`S`, 1, 2)");
            Try("MID(null, 1, LEN(null))", "MID(`S`, 1, LEN(`S`))");
            Try("MID(null, 1+1, LEN(null)-1)", "MID(`S`, 1 + 1, LEN(`S`) - 1)");
            Try("outer guard only", "IIF(`I` IS NULL, NULL, MID('abc', 1, `I`))");
            Try("outer + inner placeholder", "IIF(`I` IS NULL, NULL, MID('abc', 1, IIF(`I` IS NULL, 0, `I`)))");
            Try("EF 10 shape (inner yields NULL)", "IIF(`I` IS NULL, NULL, MID('abc', 1, IIF(`I` IS NULL, NULL, CLNG(`I`))))");

            // Verdict (observed 2026-08-16, ACE OLE DB): the split is by ARGUMENT POSITION, not by function.
            // A NULL in a string or value position propagates as relational semantics expect; a NULL in a
            // numeric position - a length, a start index, a count, a character code, a date increment - raises,
            // because VBA cannot coerce Null into a numeric parameter (error 94, "Invalid use of Null"; OLE DB
            // reports it as "Multiple-step OLE DB operation generated errors").
            //
            // So only the numeric arguments need guarding, and IIF is enough on its own: it SHORT-CIRCUITS, so
            // IIF(x IS NULL, NULL, MID(..., x)) never evaluates the MID when x is NULL. No inner placeholder is
            // needed. (A NULL condition also takes the else branch rather than erroring.)
            string? Raises(string expr)
            {
                try
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = $"SELECT {expr} FROM `N`";
                    cmd.ExecuteScalar();
                    return null;
                }
                catch (OleDbException ex) { return ex.Message; }
            }

            Assert.Null(Raises("MID(`S`, 1, 2)"));        // NULL string  -> propagates
            Assert.Null(Raises("LEN(`S`)"));              // NULL string  -> propagates
            Assert.NotNull(Raises("MID('abc', 1, `I`)")); // NULL length  -> raises
            Assert.NotNull(Raises("MID('abc', `I`, 2)")); // NULL start   -> raises
            Assert.NotNull(Raises("LEFT('abc', `I`)"));   // NULL length  -> raises
            Assert.NotNull(Raises("CHR(`I`)"));           // NULL code    -> raises

            // The guard, and the proof that IIF short-circuits: without it the same MID raises.
            Assert.Null(Raises("IIF(`I` IS NULL, NULL, MID('abc', 1, `I`))"));

            string[] propagates =
            [
                "MID(`S`, 1, 2)", "LEN(`S`)", "INSTR(`S`, 'a')", "INSTR('abc', `S`)",
                "UCASE(`S`)", "TRIM(`S`)", "ABS(`I`)", "ROUND(`I`, 2)",
                "DATEDIFF('d', `S`, #01/01/2000#)", "IIF(`S`, 1, 2)",
                "IIF(`I` IS NULL, NULL, MID('abc', 1, `I`))",
                "IIF(`I` IS NULL, NULL, MID('abc', 1, IIF(`I` IS NULL, 0, `I`)))",
            ];
            string[] rejectsNullNumericArgument =
            [
                "MID('abc', 1, `I`)", "MID('abc', `I`, 2)", "MID('abc', 1, LEN(`S`))",
                "LEFT('abc', `I`)", "RIGHT('abc', `I`)", "INSTR(`I`, 'abc', 'a')",
                "STRING(`I`, 'a')", "SPACE(`I`)", "CHR(`I`)",
                "DATEADD('d', `I`, #01/01/2000#)",
            ];

            foreach (string expression in propagates)
                Assert.Null(Raises(expression));
            foreach (string expression in rejectsNullNumericArgument)
                Assert.NotNull(Raises(expression));
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
