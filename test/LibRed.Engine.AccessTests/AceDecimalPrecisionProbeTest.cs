using System.Data.OleDb;
using LibRed;
using LibRed.Data;
using Xunit;

namespace LibRed.Engine.Tests;

// A DECIMAL column's declared precision is a contract ACE enforces and LibRed did not. Found by the sweep: a
// workload stored a value wider than the declaration, and ACE then could not read the row. The payload cannot
// enforce it — sign byte plus 128-bit magnitude whatever the column says — and LibRed reading its own file back
// agreed with itself, so only a differential test could find it.
//
// The consequence was never an invalid file, and saying so was an error worth not repeating: ACE's engine reads
// such a row fine via CStr/CDbl/`& ''`. Only the TYPED path fails, because the OLE DB consumer buffer is sized
// from the declared precision — the same shape as the DATETIME2 defect, and the trap rung-3 triage now prevents.
//
// JetTypeCodec.EncodeNumeric now refuses the value, as ACE does; these hold both engines to the same answer.
[Collection(AceCollection.Name)]
public class AceDecimalPrecisionProbeTest(ITestOutputHelper output)
{
    // 20 significant digits, declared into an 18-digit column.
    private const decimal TooWide = 12345678901234567890m;

    /// <summary>DECIMAL(18,4) permits 14 digits before the point. Both engines must refuse a wider value AND
    /// accept the widest that fits — a guard that only says no would pass half of this.</summary>
    [Theory]
    [InlineData("12345678901234567890", false)]
    [InlineData("100000000000000", false)]          // 10^14: the first magnitude that does not fit
    [InlineData("99999999999999.9999", true)]       // the declared maximum
    [InlineData("1.2345", true)]
    public void Libred_accepts_exactly_what_ace_accepts(string literal, bool expectedAccepted)
    {
        var value = decimal.Parse(literal, System.Globalization.CultureInfo.InvariantCulture);

        bool ace = AceAccepts(value, out string aceDetail);
        bool libred = LibRedAccepts(value, out string libredDetail);
        output.WriteLine($"{literal,-22} ACE {(ace ? "accepts" : "refuses"),-8} LibRed {(libred ? "accepts" : "refuses")}");
        output.WriteLine($"   ACE:    {aceDetail}");
        output.WriteLine($"   LibRed: {libredDetail}");

        Assert.Equal(expectedAccepted, ace);        // the standard, re-measured rather than assumed
        Assert.Equal(ace, libred);                  // and LibRed agreeing with it
    }

    /// <summary>Excess scale is coerced, not refused, and both engines must coerce it the SAME way. ACE
    /// truncates toward zero; LibRed rounded half-to-even until 2026-09-13, storing 1.2346 for ACE's 1.2345.
    /// </summary>
    /// <remarks>Undetectable from one side — no error, both files valid, each engine reading its own answer
    /// back — and invisible to the sweep, since the file is fine and only the number differs. This test is the
    /// only thing between the truncation and a silent regression. The cases separate truncation from the
    /// roundings that coincide with it: both midpoints, either side of one, a propagating carry, and the
    /// negatives, where truncation parts company with "round down".</remarks>
    [Theory]
    [InlineData("1.23455")]
    [InlineData("1.23465")]
    [InlineData("1.23454")]
    [InlineData("1.23456")]
    [InlineData("1.99999")]
    [InlineData("-1.23456")]
    [InlineData("-1.23455")]
    public void Libred_coerces_excess_scale_the_way_ace_does(string literal)
    {
        var value = decimal.Parse(literal, System.Globalization.CultureInfo.InvariantCulture);

        string ace = StoredByAce(value);
        string libred = StoredByLibRed(value);
        string truncated = (decimal.Truncate(Math.Abs(value) * 10000m) / 10000m * Math.Sign(value))
            .ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);

        output.WriteLine($"{literal,-10} ACE {ace,-10} LibRed {libred,-10} (truncation gives {truncated})");

        Assert.Equal(truncated, ace);       // the rule, re-measured rather than assumed
        Assert.Equal(ace, libred);          // and LibRed agreeing with it
    }

    private static string StoredByAce(decimal value)
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "ace-scale-");
        try
        {
            using OleDbConnection connection = AceTestDatabase.Open(path);
            Execute(connection, "CREATE TABLE DecScale (Id LONG, V DECIMAL(18,4))");
            Execute(connection, $"INSERT INTO DecScale (Id, V) VALUES (1, {value})");
            using OleDbCommand back = connection.CreateCommand();
            back.CommandText = "SELECT CStr(V) FROM DecScale WHERE Id = 1";
            return Normalise(back.ExecuteScalar());
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static string StoredByLibRed(decimal value)
    {
        string path = TemporaryDatabase.CreatePath("libred-scale-");
        File.Delete(path);
        try
        {
            LibRedConnection.CreateDatabase($"Data Source={path}");
            using var db = JetDatabase.Open(path, readOnly: false);
            var engine = new QueryEngine(db);
            engine.ExecuteNonQuery("CREATE TABLE `D` (`Id` LONG, `V` DECIMAL(18,4))");
            engine.ExecuteNonQuery("INSERT INTO `D` (`Id`, `V`) VALUES (1, @v)",
                new Dictionary<string, object?> { ["v"] = value });
            return Normalise(engine.ExecuteQuery("SELECT `V` FROM `D`").Rows.First()[0]);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>Rendered at the declared scale, so the comparison is of the NUMBER each engine stored rather
    /// than of how each chose to print it.</summary>
    private static string Normalise(object? value) =>
        Convert.ToDecimal(value, System.Globalization.CultureInfo.InvariantCulture)
            .ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The defect itself, as a regression test: the value must not reach the file. If it ever stores
    /// again the row becomes one ACE's typed reader cannot materialise — invisible to LibRed, which reads its
    /// own file back without complaint.</summary>
    [Fact]
    public void An_over_precise_decimal_never_reaches_the_file()
    {
        string path = TemporaryDatabase.CreatePath("libred-decimal-");
        File.Delete(path);
        try
        {
            LibRedConnection.CreateDatabase($"Data Source={path}");
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var engine = new QueryEngine(db);
                engine.ExecuteNonQuery("CREATE TABLE `D` (`Id` LONG, `V` DECIMAL(18,4))");

                InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
                    engine.ExecuteNonQuery("INSERT INTO `D` (`Id`, `V`) VALUES (1, @v)",
                        new Dictionary<string, object?> { ["v"] = TooWide }));
                output.WriteLine(refused.Message);

                Assert.Empty(engine.ExecuteQuery("SELECT `V` FROM `D`").Rows);
            }

            // And the file ACE is left with is one it reads through the typed path it would have choked on.
            using OleDbConnection connection = AceTestDatabase.Open(path);
            using OleDbCommand command = connection.CreateCommand();
            command.CommandText = "SELECT Id, V FROM D";
            using OleDbDataReader reader = command.ExecuteReader();
            while (reader.Read()) { }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static bool AceAccepts(decimal value, out string detail)
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "ace-decimal-");
        try
        {
            using OleDbConnection connection = AceTestDatabase.Open(path);
            Execute(connection, "CREATE TABLE DecProbe (Id LONG, V DECIMAL(18,4))");
            try
            {
                Execute(connection, $"INSERT INTO DecProbe (Id, V) VALUES (1, {value})");
                using OleDbCommand back = connection.CreateCommand();
                back.CommandText = "SELECT CStr(V) FROM DecProbe WHERE Id = 1";
                detail = $"stored {back.ExecuteScalar()}";
                return true;
            }
            catch (Exception ex) { detail = ex.Message.Trim(); return false; }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static bool LibRedAccepts(decimal value, out string detail)
    {
        string path = TemporaryDatabase.CreatePath("libred-decimal-");
        File.Delete(path);
        try
        {
            LibRedConnection.CreateDatabase($"Data Source={path}");
            using var db = JetDatabase.Open(path, readOnly: false);
            var engine = new QueryEngine(db);
            engine.ExecuteNonQuery("CREATE TABLE `D` (`Id` LONG, `V` DECIMAL(18,4))");
            try
            {
                engine.ExecuteNonQuery("INSERT INTO `D` (`Id`, `V`) VALUES (1, @v)",
                    new Dictionary<string, object?> { ["v"] = value });
                detail = $"stored {engine.ExecuteQuery("SELECT `V` FROM `D`").Rows.First()[0]}";
                return true;
            }
            catch (Exception ex) { detail = $"{ex.GetType().Name}: {ex.Message.Trim()}"; return false; }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // -- every write path, not just INSERT -------------------------------------------------------------

    // 15 integer digits: one more than DECIMAL(18,4)'s 14, and comfortably inside DECIMAL(28,0).
    private const string TooWideForUpdate = "123456789012345";

    /// <summary>
    /// Whether INSERT is the only gate, or ACE checks every path that puts a value in the column. ALTER is the
    /// interesting one: no value is being written at all, the existing rows are what violates the contract, so
    /// refusing it makes the declaration an invariant over the whole column rather than a filter.
    /// </summary>
    [Theory]
    [InlineData("UPDATE")]
    [InlineData("INSERT_SELECT")]
    [InlineData("ALTER_NARROW")]
    public void Every_write_path_enforces_the_declared_precision(string path)
    {
        output.WriteLine($"{path}: putting {TooWideForUpdate} (15 integer digits) into a DECIMAL(18,4) — max 14");

        string aceResult = Describe(() => RunOnAce(path), out bool ace);
        string libredResult = Describe(() => RunOnLibRed(path), out bool libred);
        output.WriteLine($"   ACE:    {aceResult}");
        output.WriteLine($"   LibRed: {libredResult}");

        Assert.False(ace, $"ACE was expected to refuse the {path} path but accepted it — {aceResult}");
        Assert.False(libred, $"LibRed accepted the {path} path where ACE refuses it — {libredResult}");
    }

    private static string Describe(Func<string> run, out bool accepted)
    {
        try
        {
            string result = run();
            accepted = true;
            return result;
        }
        catch (Exception ex)
        {
            accepted = false;
            return $"REFUSES: {ex.GetType().Name}: {ex.Message.Trim()}";
        }
    }

    private static string RunOnAce(string path)
    {
        string file = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "ace-writepath-");
        try
        {
            using OleDbConnection connection = AceTestDatabase.Open(file);
            foreach (string sql in Script(path)) Execute(connection, sql);

            using OleDbCommand back = connection.CreateCommand();
            back.CommandText = "SELECT CStr(V) FROM W WHERE Id = 1";
            return $"accepts, stores {back.ExecuteScalar()}";
        }
        finally { TemporaryDatabase.Delete(file); }
    }

    private static string RunOnLibRed(string path)
    {
        string file = TemporaryDatabase.CreatePath("libred-writepath-");
        File.Delete(file);
        try
        {
            LibRedConnection.CreateDatabase($"Data Source={file}");
            using var db = JetDatabase.Open(file, readOnly: false);
            var engine = new QueryEngine(db);
            foreach (string sql in Script(path)) engine.ExecuteNonQuery(sql);
            return $"accepts, stores {engine.ExecuteQuery("SELECT `V` FROM `W` WHERE `Id` = 1").Rows.First()[0]}";
        }
        finally { TemporaryDatabase.Delete(file); }
    }

    /// <summary>The same statements for both engines, so the only variable is the engine. Each script ends with
    /// the too-wide value in a DECIMAL(18,4) column.</summary>
    private static string[] Script(string path) => path switch
    {
        // A row that fits, then an UPDATE that does not.
        "UPDATE" =>
        [
            "CREATE TABLE W (Id LONG, V DECIMAL(18,4))",
            "INSERT INTO W (Id, V) VALUES (1, 1)",
            $"UPDATE W SET V = {TooWideForUpdate} WHERE Id = 1",
        ],
        // The value arrives from a query over a column wide enough to hold it.
        "INSERT_SELECT" =>
        [
            "CREATE TABLE W (Id LONG, V DECIMAL(18,4))",
            "CREATE TABLE Src (Id LONG, V DECIMAL(28,0))",
            $"INSERT INTO Src (Id, V) VALUES (1, {TooWideForUpdate})",
            "INSERT INTO W (Id, V) SELECT Id, V FROM Src",
        ],
        // The column narrows underneath a value that was legal when written.
        "ALTER_NARROW" =>
        [
            "CREATE TABLE W (Id LONG, V DECIMAL(28,0))",
            $"INSERT INTO W (Id, V) VALUES (1, {TooWideForUpdate})",
            "ALTER TABLE W ALTER COLUMN V DECIMAL(18,4)",
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(path)),
    };

    private static void Execute(OleDbConnection connection, string sql)
    {
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
