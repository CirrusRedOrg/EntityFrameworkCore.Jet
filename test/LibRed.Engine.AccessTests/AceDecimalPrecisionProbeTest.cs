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

    [Theory(Explicit = true)]
    [InlineData("1.5")]
    [InlineData("0.1")]
    [InlineData("123.4567")]
    [InlineData("123456789.012345")]
    [InlineData("123456789.0123456")]
    [InlineData("-123456789.0123456789")]
    [InlineData("1.23456789012345678")]
    [InlineData("0.0000000001")]
    [InlineData("12345678901234567890.5")]
    public void Literal_type_probe(string literal)
    {
        string file = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "ace-literal-");
        try
        {
            using OleDbConnection connection = AceTestDatabase.Open(file);
            Execute(connection, "CREATE TABLE L (Id LONG, D DECIMAL(28,10), F DOUBLE)");
            using OleDbCommand type = connection.CreateCommand();
            type.CommandText = $"SELECT VarType({literal}), TypeName({literal}), CStr({literal})";
            using (OleDbDataReader r = type.ExecuteReader())
            {
                r.Read();
                output.WriteLine($"{literal}: VarType {r[0]} {r[1]} CStr {r[2]}");
            }
            try
            {
                Execute(connection, $"INSERT INTO L (Id, D, F) VALUES (1, {literal}, {literal})");
                using OleDbCommand back = connection.CreateCommand();
                back.CommandText = "SELECT CStr(D), CStr(F) FROM L";
                using OleDbDataReader b = back.ExecuteReader();
                b.Read();
                output.WriteLine($"   stored decimal {b[0]}, double {b[1]}");
            }
            catch (OleDbException e) { output.WriteLine($"   insert refused: {e.Message.Trim()}"); }
        }
        finally { TemporaryDatabase.Delete(file); }
    }

    /// <summary>Each expression's VarType, TypeName and CStr through ACE and through LibRed, over one row of every
    /// numeric column type: what a decimal-point literal is, and what it makes of the arithmetic, comparisons and
    /// functions it meets.</summary>
    [Fact(Explicit = true)]
    public void Expression_type_probe()
    {
        string[] expressions =
        [
            "1.5", "1.5E0", "1E5", "15E-1", ".5", "-1.5", "1.50", "3.0", "1.23456789012345678",
            "F * 1.5", "1.5 * F", "S * 1.5", "L * 1.5", "I * 1.5", "C * 1.5", "D * 1.5", "1.5 * 2", "1.5 * 1.25",
            "D * F", "D * S", "D * L", "C * F", "D * C", "F * L", "S * L",
            "F + 1.5", "L + 1.5", "C + 1.5", "D + 0.25", "1.5 + 0.25", "0.1 + 0.2", "S + 1.5", "1.5 - L",
            "1.5 / 3", "3 / 1.5", "F / 1.5", "L / 1.5", "D / F", "1.5 / 1.5", "1.5 \\ 1", "7.5 MOD 2", "1.5 ^ 2",
            "Abs(-1.5)", "Round(1.55, 1)", "Round(F, 1)", "Int(1.5)", "Fix(-1.5)", "Sgn(-1.5)", "Sqr(2.25)", "Sin(0.5)",
            "CDbl(1.5)", "CSng(1.5)", "CLng(2.5)", "CInt(1.5)", "CCur(1.5)", "Val('1.5')",
            "IIf(L = 3, 1.5, 2)", "IIf(L = 3, 1.5, F)", "Nz(Null, 1.5)", "1.5 & ''", "Format(1.5, '0.00')",
            "0.1 + 0.2 = 0.3", "F = 0.1", "S = 0.1", "D = 1.2345", "F < 0.10000000000000001",
            "1.23456789012345678 * 1", "1.23456789012345678 * F", "1.23456789012345678 * L",
            "1.5 * Null", "-(1.5)",
            // The failing engine tests' own literal-only expressions.
            "1 / 1.5", "1.5 / '2.5'", "1.5 * 1.5", "1.5 * 1.5 * 1.5", "459.35 / 3", "40000 / 0.3", "1.5 / LEFT('12', 2)",
            "CSTR(5) / 1.5", "459.35 * 334.90", "1.1 * 1.11", "1 / 3.0", "1 + 2.5", "5 / 2", "ROUND(2.5, 0)",
            "IIf(1 = 1, 1, 2.5E0)", "IIf(1 = 1, 1.5, 2.5)", "IIf(1 = 1, D, 2)", "IIf(1 = 1, 1.5, D)", "IIf(1 = 1, 1.5, C)",
            "IIf(1 = 1, 1.5, L)", "IIf(1 = 1, 0.0, D)",
            // Where a written number stops being a Decimal.
            "5E-324", "-1E-300", "1E-28", "1.5E-28", "1E-29", "1.23456E-20", "1E28", "1E29", "7.9E28", "1E300", "3E9", "3000000000.0",
            // Functions over Decimal and Currency columns.
            "Round(D, 1)", "Abs(D)", "Int(D)", "Fix(D)", "Round(C, 1)", "Abs(C)", "Int(C)",
        ];
        CompareTypes(
        [
            "CREATE TABLE T (F DOUBLE, S REAL, L LONG, I SHORT, C CURRENCY, D DECIMAL(18,4))",
            "INSERT INTO T (F, S, L, I, C, D) VALUES (0.1, 0.1, 3, 2, 1.5, 1.2345)",
        ], expressions);
    }

    /// <summary>The same comparison on <c>ArithmeticOperatorTests</c>' own table and row, for every expression its
    /// decimal theories pin.</summary>
    [Fact(Explicit = true)]
    public void Arithmetic_operator_tests_type_probe() => CompareTypes(
        [
            "CREATE TABLE T (Id LONG, N LONG, SI SHORT, SG REAL, CY CURRENCY, DC DECIMAL(18,4), D DATETIME, "
                + "S TEXT(60), NT TEXT(60), G GUID, B BINARY(4))",
            "INSERT INTO T (Id, N, SI, SG, CY, DC, D, S) VALUES (1, 3, 2, 1.5, 3.25, 4.5, #2020-01-02 12:00:00#, 'abc')",
            "UPDATE T SET G = {00112233-4455-6677-8899-AABBCCDDEEFF}",
            "UPDATE T SET B = 0x41004200",
        ],
        [
            "1 / 1.5", "1.5 / '2.5'", "1.5 * 1.5", "1.5 * 1.5 * 1.5", "459.35 / 3", "40000 / 0.3", "D * 0.25",
            "1.5 / LEFT('12', 2)", "CSTR(5) / 1.5", "1.5 * 1.25", "459.35 * 334.90", "1.1 * 1.11", "1 / 3.0",
            "1 / DC", "TRUE / DC", "DC / 40000", "DC / '7'", "DC * DC", "D / DC", "CY * CY", "CY / 3", "1.5 / DC",
            "DC * 334.90", "PMT(0.05 / 12, 60, 10000)",
            "SELECT d.X / 7 FROM (SELECT DC AS X FROM T) AS d",
            "SELECT d.X / 3 FROM (SELECT CY AS X FROM T) AS d",
            "SELECT SUM(DC) / 7 FROM T",
        ]);

    /// <summary>The same comparison on <c>MathFunctionTypeTests</c>' own table and row.</summary>
    [Fact(Explicit = true)]
    public void Math_function_tests_type_probe() => CompareTypes(
        [
            "CREATE TABLE T (D DOUBLE, C CURRENCY)",
            "INSERT INTO T (D, C) VALUES (3.7, 3.7)",
        ],
        [
            "ROUND(D, 1)", "ROUND(C, 1)", "ROUND(2.5, 0)", "FIX(D)", "FIX(C)", "INT(D)", "INT(C)", "ABS(D)", "ABS(C)",
            "ABS(-5)", "ROUND(C, 1) * 2", "ABS(-1.5) + 1", "INT(C) * 2",
        ]);

    /// <summary>The same comparison on <c>CurrencyArithmeticTests</c>' own table and row, and on Northwind for
    /// <c>ArithmeticTypeTests</c> (LibRed's side has no Northwind here; only ACE's is the measurement).</summary>
    [Fact(Explicit = true)]
    public void Currency_tests_type_probe() => CompareTypes(
        [
            "CREATE TABLE T (K LONG, A CURRENCY, B CURRENCY, Z CURRENCY, Y CURRENCY, I LONG, D DOUBLE, L BIGINT)",
            "INSERT INTO T (K, A, B, Z, Y, I, D, L) VALUES (1, 1.2345, 0.0003, 922337203685477, 1000000000.5, 3, 1.5, 1000000000000)",
        ],
        [
            "A + 0.00005", "Z + 0.5808", "Y * 864000000000", "Z + 1000000000000", "A * D", "A + D", "A / I",
            "SELECT TOP 1 UnitPrice + 1 FROM Products", "SELECT TOP 1 UnitPrice / 2 FROM Products",
        ]);

    /// <summary>The type of an IIf column with a Decimal arm, beside each other kind.</summary>
    [Fact(Explicit = true)]
    public void IIf_decimal_arm_type_probe() => CompareTypes(
        [
            "CREATE TABLE T (F DOUBLE, S REAL, L LONG, I SHORT, C CURRENCY, D DECIMAL(18,4), E DECIMAL(10,2), X TEXT(10), B BIT, T DATETIME)",
            "INSERT INTO T (F, S, L, I, C, D, E, X, B, T) VALUES (0.1, 0.1, 3, 2, 1.5, 1.2345, 2.5, 'a', TRUE, #2020-01-02#)",
        ],
        [
            "IIf(L = 3, D, D)", "IIf(L = 3, 1.5, 1.5)", "IIf(L = 3, 1.5, 2.25)", "IIf(L = 3, D, E)", "IIf(L = 3, D, Null)",
            "IIf(L = 3, 1.5, Null)", "IIf(L = 3, D, C)", "IIf(L = 3, D, F)", "IIf(L = 3, D, S)", "IIf(L = 3, D, I)",
            "IIf(L = 3, D, B)", "IIf(L = 3, D, T)", "IIf(L = 3, D, X)", "IIf(L = 3, 1.5, X)", "IIf(L = 3, C, C)",
            "IIf(L = 3, C, F)", "IIf(L = 3, C, L)", "IIf(L = 3, L, I)", "IIf(L = 3, F, S)", "IIf(L = 3, 1E20, 2)",
            "IIf(L = 3, Null, 1.5)", "IIf(L = 3, 3.0, 2)", "IIf(L = 3, C, S)", "IIf(L = 3, C, B)", "IIf(L = 3, C, T)",
            "Switch(L = 3, D, True, 2)", "Choose(1, 1.5, 2)", "Switch(L = 3, C, True, F)",
        ]);

    /// <summary><c>ResultColumnTypeTests</c>' own table and rows, and <c>CorrelatedSubqueryTests</c>' Northwind query.</summary>
    [Fact(Explicit = true)]
    public void Result_column_type_tests_probe() => CompareTypes(
        [
            "CREATE TABLE T (Id INT, M CURRENCY, B BYTE, S SMALLINT, D DATETIME, F DOUBLE, R REAL, E DECIMAL(18,4), "
                + "L LONG, X TEXT(10), Y YESNO, G GUID, N BINARY(4))",
            "INSERT INTO T (Id, M, B, S, D, F, R, E, L, X, Y) VALUES (1, 10.5, 3, 7, #2020-01-02#, 1.5, 1.5, 4.25, 70000, 'abc', TRUE)",
            "INSERT INTO T (Id, M, B, S, D, F, R, E, L, X, Y) VALUES (2, 2, 4, 8, #1899-12-30 06:00:00#, 2.5, 0.75, 5.5, 200000, 'xyz', FALSE)",
        ],
        [
            "IIF(M >= 5.1, M, 5.1)", "IIF(Id = 1, 0.5, Id)", "IIF(Id = 1, 0.5, F)", "IIF(Id = 1, M, L)",
            "IIF(Id = 1, 9, (SELECT SUM(M) FROM T))", "1000 - IIF(Id = 1, 0, (SELECT SUM(M) FROM T))",
            "IIF(Id = 1, ROUND(M), 0)", "IIF(Id = 2, ROUND(M), 0)", "IIF(Id = 1, ROUND(M, 1), F)",
            "SELECT IIF(SUM(M) IS NULL, 0.0, SUM(M)) AS c FROM T",
            "SELECT IIF(t3.x < 0, 9, t3.x + 8), t3.x + t3.x FROM (SELECT IIF(t2.x IS NULL, 0, t2.x) AS x "
                + "FROM (SELECT (SELECT SUM(M) FROM T) AS x FROM T q) t2) t3",
            "SELECT TOP 1 (SELECT IIF(SUM(ROUND(o0.UnitPrice * o0.UnitPrice, 2)) IS NULL, 0.0, SUM(ROUND(o0.UnitPrice * o0.UnitPrice, 2))) "
                + "FROM [Order Details] AS o0 WHERE o.OrderID = o0.OrderID) AS [Sum] FROM Orders AS o WHERE o.OrderID < 10300 ORDER BY o.OrderID",
            "SELECT TOP 1 SUM(ROUND(o0.UnitPrice * o0.UnitPrice, 2)) FROM [Order Details] AS o0 WHERE o0.OrderID = 10248",
        ]);

    /// <summary><c>KeptTypeFunctionDeclarationTests</c>' own table and row.</summary>
    [Fact(Explicit = true)]
    public void Kept_type_tests_type_probe() => CompareTypes(
        [
            "CREATE TABLE T (Id LONG, Y CURRENCY, E DECIMAL(18,4), D DOUBLE, R REAL, S SMALLINT, L BIGINT, T DATETIME, X TEXT(10))",
            "INSERT INTO T (Id, Y, E, D, R, S, L, T, X) VALUES (1, 2.5678, 2.5678, 2.5678, 2.5, -7, 5, #2020-01-02 12:00:00#, '2.5')",
        ],
        ["ROUND(E)", "ROUND(E, 2)", "ABS(E)", "INT(E)", "FIX(E)", "IIF(Id = 1, ROUND(Y), 0)", "IIF(Id = 2, ROUND(Y), 0)"]);

    /// <summary><c>NumericFunctionTests</c>' own table and row.</summary>
    [Fact(Explicit = true)]
    public void Numeric_function_tests_type_probe() => CompareTypes(
        [
            "CREATE TABLE T (Id LONG, NT TEXT(60), DT DATETIME, G GUID)",
            "INSERT INTO T (Id, DT) VALUES (1, #2020-01-02 12:00:00#)",
            "UPDATE T SET G = {00112233-4455-6677-8899-AABBCCDDEEFF}",
        ],
        ["ROUND(1E20 / 3, 2)", "1E20 / 3", "1E20", "ROUND(1E300, 2)"]);

    /// <summary>How ACE computes a result column: every operator over every pairing of a Double, Single, Long,
    /// Currency and Decimal column and a decimal-point literal, its value set beside the two candidate rules —
    /// both operands as Doubles (D), or both as Decimals with the result converted the OLE Automation way (M) —
    /// and then the same kind of expression in each context a value is computed in.</summary>
    [Fact(Explicit = true)]
    public void Evaluation_rule_probe()
    {
        string ace = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "ace-rule-");
        string libred = TemporaryDatabase.CreatePath("libred-rule-");
        File.Delete(libred);
        try
        {
            using OleDbConnection connection = AceTestDatabase.Open(ace);
            LibRedConnection.CreateDatabase($"Data Source={libred}");
            using var db = JetDatabase.Open(libred, readOnly: false);
            var engine = new QueryEngine(db);
            foreach (string sql in (string[])
            [
                "CREATE TABLE T (F DOUBLE, S REAL, L LONG, C CURRENCY, D DECIMAL(18,4))",
                "INSERT INTO T (F, S, L, C, D) VALUES (0.1, 0.1, 3, 1.1, 1.1)",
                "CREATE TABLE W (F DOUBLE, X DOUBLE, Y DECIMAL(28,10))",
                "INSERT INTO W (F) VALUES (0.1)",
                "UPDATE W SET X = F * 1.11, Y = F * 1.11",
                "INSERT INTO W (F, X, Y) SELECT F, F * 1.11, F * 1.11 FROM T",
                "INSERT INTO W (F, X, Y) VALUES (1, 0.1 * 1.11, 0.1 * 1.11)",
                "CREATE TABLE O (K LONG, F DOUBLE, G DOUBLE)",
                "INSERT INTO O (K, F, G) VALUES (1, 0, 0)",
                "INSERT INTO O (K, F, G) VALUES (2, 0.1, 0.111)",
            ])
            {
                Execute(connection, sql);
                engine.ExecuteNonQuery(sql);
            }
            static string Show(object? value) => value switch
            {
                null or DBNull => "Null",
                double d => $"Double {d.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}",
                bool b => $"Int16 {(b ? -1 : 0)}",
                _ => $"{value.GetType().Name} {Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)}",
            };
            string AceRows(string sql)
            {
                using OleDbCommand command = connection.CreateCommand();
                command.CommandText = sql;
                using OleDbDataReader r = command.ExecuteReader();
                var shown = new List<string>();
                while (r.Read())
                    for (int i = 0; i < r.FieldCount; i++) shown.Add(Show(r[i]));
                return string.Join(" | ", shown);
            }
            string LibRedRows(string sql)
            {
                try
                {
                    return string.Join(" | ", engine.ExecuteQuery(sql).Rows.SelectMany(row => row.Select(Show)));
                }
                catch (Exception e) { return $"error: {e.Message}"; }
            }
            var operands = new (string Sql, object Value)[]
            {
                ("F", 0.1d), ("S", 0.1f), ("L", 3), ("C", 1.1m), ("D", 1.1m), ("1.11", 1.11m), ("0.3", 0.3m), ("3", 3),
            };
            (string, string)[] pairs =
            [
                ("F", "1.11"), ("1.11", "F"), ("S", "1.11"), ("D", "1.11"), ("C", "1.11"), ("1.11", "0.3"), ("D", "F"),
                ("C", "F"), ("D", "C"), ("L", "1.11"), ("F", "L"), ("D", "L"), ("C", "L"), ("D", "D"), ("0.3", "3"),
                ("F", "F"), ("C", "C"), ("L", "0.3"),
            ];
            foreach ((string l, string r) in pairs)
                foreach (char op in "+-*/")
                {
                    object lv = operands.First(o => o.Sql == l).Value, rv = operands.First(o => o.Sql == r).Value;
                    double dl = EntityFrameworkCore.Jet.Data.JetDecimalConverter.ToDouble(lv, System.Globalization.CultureInfo.InvariantCulture);
                    double dr = EntityFrameworkCore.Jet.Data.JetDecimalConverter.ToDouble(rv, System.Globalization.CultureInfo.InvariantCulture);
                    decimal ml = EntityFrameworkCore.Jet.Data.JetDecimalConverter.ToDecimal(lv, System.Globalization.CultureInfo.InvariantCulture);
                    decimal mr = EntityFrameworkCore.Jet.Data.JetDecimalConverter.ToDecimal(rv, System.Globalization.CultureInfo.InvariantCulture);
                    double asDouble = op switch { '+' => dl + dr, '-' => dl - dr, '*' => dl * dr, _ => dl / dr };
                    decimal asDecimal = op switch { '+' => ml + mr, '-' => ml - mr, '*' => ml * mr, _ => ml / mr };
                    double decimalAsDouble = EntityFrameworkCore.Jet.Data.JetDecimalConverter.FromDecimal(asDecimal);
                    string expression = $"{l} {op} {r}";
                    using OleDbCommand command = connection.CreateCommand();
                    command.CommandText = $"SELECT {expression} FROM T";
                    object? value = command.ExecuteScalar();
                    string rule = value is double dv
                        ? (dv == asDouble ? "D" : "") + (dv == decimalAsDouble ? "M" : "") is { Length: > 0 } match ? match : "neither"
                        : value is decimal mv ? (mv == asDecimal ? "exact decimal" : $"decimal (exact {asDecimal})")
                        : "other";
                    string aceShown = Show(value), libredShown = LibRedRows($"SELECT {expression} FROM T");
                    output.WriteLine($"{(aceShown == libredShown ? "  " : "!!")} A {expression,-12} ACE {aceShown,-36} {rule,-14} LibRed {libredShown}");
                }

            string[] contexts =
            [
                "SELECT F * 1.11 FROM T",
                "SELECT (F * 1.11) * 1 FROM T",
                "SELECT F * 1.11 + 0 FROM T",
                "SELECT IIf(L = 3, F * 1.11, 0) FROM T",
                "SELECT Abs(F * 1.11) FROM T",
                "SELECT CStr(F * 1.11) FROM T",
                "SELECT x.V FROM (SELECT F * 1.11 AS V FROM T) AS x",
                "SELECT SUM(F * 1.11) FROM T",
                "SELECT COUNT(*) FROM T WHERE F * 1.11 = 0.111",
                "SELECT COUNT(*) FROM T WHERE F * 1.11 > 0.111",
                "SELECT COUNT(*) FROM T WHERE F < 0.10000000000000001",
                "SELECT COUNT(*) FROM T WHERE F = 0.1",
                "SELECT COUNT(*) FROM T WHERE 1.1 * 1.11 = 1.221",
                "SELECT COUNT(*) FROM T WHERE S = 0.1",
                "SELECT COUNT(*) FROM T WHERE F * 1.5 = 0.15",
                "SELECT IIf(F * 1.11 = 0.111, 'eq', 'ne') FROM T",
                "SELECT X, Y FROM W WHERE F = 0.1",
                "SELECT X, Y FROM W WHERE F = 1",
                // Nesting.
                "SELECT L * 1.11 + F FROM T",
                "SELECT (1.11 + 0.3) * L FROM T",
                "SELECT F * (L * 1.11) FROM T",
                "SELECT D * 1.11 + F FROM T",
                "SELECT 1.11 * 0.3 * L FROM T",
                "SELECT -1.11 * F FROM T",
                "SELECT F * 1.11 * L FROM T",
                // A comparison in the select list.
                "SELECT F * 1.11 = 0.111 FROM T",
                "SELECT 1.1 * 1.11 = 1.221 FROM T",
                // Currency or Decimal: the provider's type name follows each value.
                "SELECT C + F, C + 1.11, C * L, C + C, C * C, C - F FROM T",
                // A key that is 0 as Decimals and 1.4E-17 as Doubles on K = 2, and 0 either way on K = 1.
                "SELECT TOP 1 K FROM O ORDER BY F * 1.11 - G DESC, K",
                "SELECT COUNT(*) FROM (SELECT F * 1.11 - G AS V FROM O GROUP BY F * 1.11 - G) AS g",
                "SELECT COUNT(*) FROM O WHERE F * 1.11 - G = 0",
                "SELECT K FROM O GROUP BY K HAVING SUM(F) * 1.11 - SUM(G) = 0",
                "SELECT COUNT(*) FROM O AS a INNER JOIN O AS b ON a.F * 1.11 - a.G = b.F",
                "SELECT COUNT(*) FROM O AS a INNER JOIN O AS b ON a.F * 1 = b.F",
                "SELECT COUNT(*) FROM O AS a INNER JOIN O AS b ON a.F + 0 = b.F",
                "SELECT COUNT(*) FROM O AS a INNER JOIN O AS b ON a.F * 1.0 = b.F",
                "SELECT COUNT(*) FROM O AS a INNER JOIN O AS b ON a.F * 1.5 = b.F * 1.5",
                "SELECT COUNT(*) FROM O AS a INNER JOIN O AS b ON a.F = b.F * 1.11 - b.G",
                "SELECT COUNT(*) FROM O AS a INNER JOIN O AS b ON a.K = b.K AND a.F * 1.11 - a.G = 0",
                "SELECT COUNT(*) FROM O AS a, O AS b WHERE a.F * 1.11 - a.G = b.F",
                "SELECT COUNT(*) FROM O AS a INNER JOIN O AS b ON a.F * 1.11 - a.G = b.F * 1",
                "SELECT COUNT(*) FROM O AS a INNER JOIN O AS b ON a.F - a.G = b.F",
                "SELECT F * 1.11 - G FROM O ORDER BY K",
            ];
            foreach (string sql in contexts)
            {
                string aceShown = AceRows(sql), libredShown = LibRedRows(sql);
                output.WriteLine($"{(aceShown == libredShown ? "  " : "!!")} B {sql,-56} ACE {aceShown}\n{"",63}LibRed {libredShown}");
            }
        }
        finally
        {
            TemporaryDatabase.Delete(ace);
            TemporaryDatabase.Delete(libred);
        }
    }

    private static readonly string[] MakeTableSetup =
    [
        "CREATE TABLE T (C CURRENCY, D DECIMAL(10,2), F DOUBLE, S REAL, L LONG, I SHORT, B BYTE, Y BIT, "
            + "E DECIMAL(18,4), G DECIMAL(4,1), H DECIMAL(28,6), N BIGINT, X TEXT(10), W DATETIME, K DECIMAL(20,10))",
        "INSERT INTO T (C, D, F, S, L, I, B, Y, E, G, H, N, X, W, K) "
            + "VALUES (1.1, 12.34, 0.1, 0.1, 3, 2, 4, TRUE, 1.2345, 1.5, 1.123456, 7, '5', #2020-01-02#, 1.5)",
    ];

    private static readonly string[] MakeTableExpressions =
    [
        "G * 2.25", "D * 2.5", "H * K", "G * K", "(G * 2) * D", "(G * 1.5) * D", "E * 1.11", "D * 334.90", "H - G",
        "K + E", "H * E", "H * D", "K * H", "G + 0.25", "K * 1.5", "N * 1.7", "N * 2.5", "N + 0.5", "N * L",
        "E * D", "G * D", "G * B", "D + C", "H + C", "AVG(D)", "SUM(G)", "G * N", "1.5 * 1.5", "0.25 + 0.5", "D * 2",
        "ROUND(D, 1)", "ABS(D)", "INT(D)", "IIF(Y, D, 0)", "IIF(Y, D, G)", "AVG(H)", "AVG(K)", "AVG(G)", "SUM(D) / 2",
        "I * I", "D * (I * I)", "N", "N * 2", "L * 2", "-N", "IIF(Y, D, 1.5)", "IIF(Y, H, K)", "IIF(Y, K, H)",
    ];

    /// <summary>The make-table probe's expressions as a plain SELECT's result columns, to set against the columns a
    /// make-table query gives them.</summary>
    [Fact(Explicit = true)]
    public void Make_table_expressions_select_type_probe() => CompareTypes(MakeTableSetup,
        [
            .. MakeTableExpressions.Where(e => !e.Contains('(', StringComparison.Ordinal)),
            "SELECT AVG(V) FROM (SELECT D AS V FROM T UNION ALL SELECT D + 0.01 FROM T)",
            "SELECT AVG(V) FROM (SELECT G AS V FROM T UNION ALL SELECT G + 0.2 FROM T)",
        ]);

    /// <summary>The precision and scale ACE's OLE DB schema reports for a computed result column, against what
    /// LibRed's result describes.</summary>
    [Fact(Explicit = true)]
    public void Computed_decimal_schema_probe()
    {
        string ace = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "ace-schema-");
        string libred = TemporaryDatabase.CreatePath("libred-schema-");
        File.Delete(libred);
        try
        {
            using OleDbConnection connection = AceTestDatabase.Open(ace);
            foreach (string sql in MakeTableSetup) Execute(connection, sql);
            LibRedConnection.CreateDatabase($"Data Source={libred}");
            using var db = JetDatabase.Open(libred, readOnly: false);
            var engine = new QueryEngine(db);
            foreach (string sql in MakeTableSetup) engine.ExecuteNonQuery(sql);
            foreach (string expression in MakeTableExpressions)
            {
                string query = $"SELECT {expression} AS X FROM T";
                using OleDbCommand command = connection.CreateCommand();
                command.CommandText = query;
                using OleDbDataReader r = command.ExecuteReader();
                System.Data.DataRow row = r.GetSchemaTable()!.Rows[0];
                string aceSchema = $"{row["DataType"]} p{row["NumericPrecision"]} s{row["NumericScale"]} type{row["ProviderType"]}";
                LibRed.Engine.Execution.ResultColumn column = engine.ExecuteQuery(query).Columns[0];
                string libredSchema = $"{column.ClrType} p{column.Precision} s{column.Scale} type{column.ProviderType}";
                output.WriteLine($"{(aceSchema == libredSchema ? "  " : "!!")} {expression,-14} ACE {aceSchema,-36} LibRed {libredSchema}");
            }
        }
        finally
        {
            TemporaryDatabase.Delete(ace);
            TemporaryDatabase.Delete(libred);
        }
    }

    /// <summary>Descriptor bytes 0x0B–0x0E of each column a make-table query creates — a Decimal's precision and
    /// scale, anything else's collation — through ACE and through LibRed, for columns before and after a Decimal.</summary>
    [Fact(Explicit = true)]
    public void Make_table_descriptor_union_probe()
    {
        string[] queries =
        [
            "SELECT D * 2 AS A, F * 2 AS B, L * 2 AS C, X & '' AS S, F * 3 AS E, W + 1 AS H, Y AS I, C * 2 AS J, N * 2 AS K INTO M1 FROM T",
            "SELECT F * 2 AS B INTO M2 FROM T",
            "SELECT D AS A, F * 2 AS B INTO M3 FROM T",
            "SELECT F * 2 AS B, G * 2 AS A, F * 3 AS E INTO M4 FROM T",
            "SELECT G * 2 AS A, E * 2 AS B, F * 2 AS C INTO M5 FROM T",
            "SELECT D * 2 AS A, X AS S, F * 2 AS B INTO M6 FROM T",
            "SELECT D * 2 AS A, Y AS I, L AS C, F AS B INTO M7 FROM T",
        ];
        string ace = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "ace-union-");
        string libred = TemporaryDatabase.CreatePath("libred-union-");
        File.Delete(libred);
        try
        {
            LibRedConnection.CreateDatabase($"Data Source={libred}");
            using (OleDbConnection connection = AceTestDatabase.Open(ace))
                foreach (string sql in MakeTableSetup.Concat(queries)) Execute(connection, sql);
            using (var db = JetDatabase.Open(libred, readOnly: false))
            {
                var engine = new QueryEngine(db);
                foreach (string sql in MakeTableSetup.Concat(queries)) engine.ExecuteNonQuery(sql);
            }
            static string[] Unions(string path, int i)
            {
                using var db = JetDatabase.Open(path, readOnly: true);
                return [.. db.Catalog.FindTable($"M{i + 1}")!.Columns
                    .Select(c => $"{c.Name}:{c.Type} {Convert.ToHexString(c.RawDescriptor.AsSpan(0x0B, 4))}")];
            }
            for (int i = 0; i < queries.Length; i++)
            {
                string aceColumns = string.Join("  ", Unions(ace, i)), libredColumns = string.Join("  ", Unions(libred, i));
                output.WriteLine($"{(aceColumns == libredColumns ? "  " : "!!")} {queries[i]}\n   ACE    {aceColumns}\n   LibRed {libredColumns}");
            }
        }
        finally
        {
            TemporaryDatabase.Delete(ace);
            TemporaryDatabase.Delete(libred);
        }
    }

    /// <summary>The column a make-table query gives each expression, through ACE and through LibRed: its type,
    /// precision, scale and length, read from each file's TDEF.</summary>
    [Fact(Explicit = true)]
    public void Make_table_column_type_probe()
    {
        string[] setup = MakeTableSetup;
        string[] expressions = MakeTableExpressions;
        string ace = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "ace-make-");
        string libred = TemporaryDatabase.CreatePath("libred-make-");
        File.Delete(libred);
        try
        {
            LibRedConnection.CreateDatabase($"Data Source={libred}");
            using (OleDbConnection connection = AceTestDatabase.Open(ace))
            {
                foreach (string sql in setup) Execute(connection, sql);
                for (int i = 0; i < expressions.Length; i++)
                    Execute(connection, $"SELECT {expressions[i]} AS X INTO R{i} FROM T");
            }
            using (var db = JetDatabase.Open(libred, readOnly: false))
            {
                var engine = new QueryEngine(db);
                foreach (string sql in setup) engine.ExecuteNonQuery(sql);
                for (int i = 0; i < expressions.Length; i++)
                    engine.ExecuteNonQuery($"SELECT {expressions[i]} AS X INTO R{i} FROM T");
            }
            static string Describe(string path, int i)
            {
                using var db = JetDatabase.Open(path, readOnly: true);
                var column = db.Catalog.FindTable($"R{i}")!.FindColumn("X")!;
                object? value = new QueryEngine(db).ExecuteQuery($"SELECT X FROM R{i}").Rows.First()[0];
                return $"{column.Type} p{column.Precision} s{column.Scale} len{column.Length} "
                    + Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            }
            for (int i = 0; i < expressions.Length; i++)
            {
                string aceColumn = Describe(ace, i), libredColumn = Describe(libred, i);
                output.WriteLine($"{(aceColumn == libredColumn ? "  " : "!!")} {expressions[i],-14} ACE {aceColumn,-40} LibRed {libredColumn}");
            }
        }
        finally
        {
            TemporaryDatabase.Delete(ace);
            TemporaryDatabase.Delete(libred);
        }
    }

    private void CompareTypes(string[] setup, string[] expressions)
    {
        string ace = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "ace-types-");
        string libred = TemporaryDatabase.CreatePath("libred-types-");
        File.Delete(libred);
        try
        {
            using OleDbConnection connection = AceTestDatabase.Open(ace);
            foreach (string sql in setup) Execute(connection, sql);
            LibRedConnection.CreateDatabase($"Data Source={libred}");
            using var db = JetDatabase.Open(libred, readOnly: false);
            var engine = new QueryEngine(db);
            foreach (string sql in setup) engine.ExecuteNonQuery(sql);

            foreach (string expression in expressions)
            {
                // A whole query reports only its one column's value and type.
                bool whole = expression.StartsWith("SELECT", StringComparison.Ordinal);
                string query = whole ? expression
                    : $"SELECT VarType({expression}), TypeName({expression}), CStr({expression}), {expression} FROM T";
                int last = whole ? 0 : 3;
                string aceResult, libredResult;
                try
                {
                    using OleDbCommand command = connection.CreateCommand();
                    command.CommandText = query;
                    using OleDbDataReader r = command.ExecuteReader();
                    r.Read();
                    aceResult = (whole ? "" : $"{r[0]} {r[1]} {r[2]} ")
                        + $"[{r.GetFieldType(last).Name} {Convert.ToString(r[last], System.Globalization.CultureInfo.InvariantCulture)}]";
                }
                catch (Exception e) when (e is OleDbException or InvalidOperationException or OverflowException)
                {
                    aceResult = $"error: {e.Message.Trim()}";
                }
                try
                {
                    var rs = engine.ExecuteQuery(query);
                    object?[] row = rs.Rows.First();
                    libredResult = (whole ? "" : $"{row[0]} {row[1]} {row[2]} ")
                        + $"[{row[last]?.GetType().Name ?? "null"} {Convert.ToString(row[last], System.Globalization.CultureInfo.InvariantCulture)}]";
                }
                catch (Exception e) { libredResult = $"error: {e.GetType().Name}: {e.Message.Trim()}"; }
                output.WriteLine($"{(aceResult == libredResult ? "  " : "!!")} {expression,-28} ACE {aceResult,-48} LibRed {libredResult}");
            }
        }
        finally
        {
            TemporaryDatabase.Delete(ace);
            TemporaryDatabase.Delete(libred);
        }
    }

    private static void Execute(OleDbConnection connection, string sql)
    {
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
