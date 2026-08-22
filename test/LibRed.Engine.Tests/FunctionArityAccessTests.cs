using System.Data.OleDb;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>Function arities taken from docs/functions.md and verified directly against ACE's JES.</summary>
// This class deliberately feeds ACE expressions it must reject. It runs in the ACE collection so no other
// ACE-driving class is inside the provider at the same time — concurrent ACE use faults natively and kills
// the test process (see AceCollection).
[Collection(AceCollection.Name)]
public class FunctionArityAccessTests
{
    private sealed record Arity(string Name, int Min, int? Max, params string[] Arguments);

    private static readonly Arity[] ConversionMathString =
    [
        .. Unary("CBool", "CByte", "CInt", "CLng", "CSng", "CDbl", "CCur", "CStr", "CDate", "CVar",
            "Abs", "Sgn", "Int", "Fix", "Sqr", "Exp", "Log", "Sin", "Cos", "Tan", "Atn",
            "Len", "LCase", "UCase", "Trim", "LTrim", "RTrim", "Space", "StrReverse", "Str", "Val",
            "Chr", "Asc", "Hex", "Oct"),
        new("Round", 1, 2, "1", "0", "0"), new("Rnd", 0, 1, "1", "1"), new("Timer", 0, 0, "1"),
        new("Left", 2, 2, "'abc'", "1", "0"), new("Right", 2, 2, "'abc'", "1", "0"),
        new("Mid", 2, 3, "'abc'", "1", "1", "0"),
        new("InStr", 2, 4, "1", "'abc'", "'b'", "0", "0"),
        new("InStrRev", 2, 4, "'abc'", "'b'", "-1", "0", "0"),
        new("Replace", 3, 6, "'abc'", "'a'", "'x'", "1", "-1", "0", "0"),
        new("String", 2, 2, "2", "'x'", "0"), new("StrComp", 2, 3, "'a'", "'b'", "0", "0"),
        new("StrConv", 2, 3, "'abc'", "1", "1033", "0"),
        new("Left$", 2, 2, "'abc'", "1", "0"), new("UCase$", 1, 1, "'abc'", "0"),
    ];

    private static readonly Arity[] DateLogicalInspection =
    [
        new("Now", 0, 0, "1"), new("Date", 0, 0, "1"), new("Time", 0, 0, "1"),
        new("DateAdd", 3, 3, "'d'", "1", "#1/1/2020#", "1"),
        new("DateDiff", 3, 5, "'d'", "#1/1/2020#", "#1/2/2020#", "1", "1", "1"),
        new("DatePart", 2, 4, "'d'", "#1/1/2020#", "1", "1", "1"),
        new("DateSerial", 3, 3, "2020", "1", "1", "1"), new("TimeSerial", 3, 3, "1", "2", "3", "1"),
        .. Unary("DateValue", "TimeValue", "Year", "Month", "Day", "Hour", "Minute", "Second", "IsDate",
            "IsNull", "IsNumeric", "IsError", "TypeName", "VarType"),
        new("Weekday", 1, 2, "#1/1/2020#", "1", "1"), new("MonthName", 1, 2, "1", "True", "1"),
        new("WeekdayName", 1, 3, "1", "True", "1", "1"),
        new("IIf", 2, 3, "True", "1", "2", "3"), new("Choose", 2, null, "1", "'a'"),
        new("Switch", 2, null, "True", "1"),
    ];

    private static readonly Arity[] FormattingFinancialVariants =
    [
        new("Format", 1, 4, "1", "'0.00'", "1", "1", "1"),
        new("FormatCurrency", 1, 5, "1", "2", "-1", "-1", "-1", "1"),
        new("FormatNumber", 1, 5, "1", "2", "-1", "-1", "-1", "1"),
        new("FormatPercent", 1, 5, "1", "2", "-1", "-1", "-1", "1"),
        new("FormatDateTime", 1, 2, "#1/1/2020#", "1", "1"),
        new("Partition", 4, 4, "1", "0", "10", "1", "1"),
        new("Pmt", 3, 5, "0.01", "12", "100", "0", "0", "0"),
        new("FV", 3, 5, "0.01", "12", "-10", "100", "0", "0"),
        new("PV", 3, 5, "0.01", "12", "-10", "0", "0", "0"),
        new("NPer", 3, 5, "0.01", "-10", "100", "0", "0", "0"),
        new("IPmt", 4, 6, "0.01", "1", "12", "100", "0", "0", "0"),
        new("PPmt", 4, 6, "0.01", "1", "12", "100", "0", "0", "0"),
        new("Rate", 3, 6, "12", "-10", "100", "0", "0", "0.1", "0"),
        new("SLN", 3, 3, "100", "10", "5", "1"), new("SYD", 4, 4, "100", "10", "5", "1", "1"),
        new("DDB", 4, 5, "100", "10", "5", "1", "2", "1"),
        new("RGB", 3, 3, "1", "2", "3", "4"), new("QBColor", 1, 1, "1", "1"),
        .. Unary("AscB", "LenB", "AscW", "ChrW"),
        new("LeftB", 2, 2, "'abc'", "2", "1"), new("RightB", 2, 2, "'abc'", "2", "1"),
        new("MidB", 2, 3, "'abc'", "1", "2", "1"),
        new("InStrB", 2, 4, "1", "'abc'", "'b'", "0", "0"),
    ];

    private static readonly Arity[] Aggregates =
    [
        .. Unary("Count", "Sum", "Avg", "Min", "Max", "First", "Last", "StDev", "Var", "StDevP", "VarP",
            "StdDev", "StdDevP"),
    ];

    [Fact]
    public void All_documented_function_arities_are_enforced_by_libred() => AssertLibRedArities(
        ConversionMathString.Concat(DateLogicalInspection).Concat(FormattingFinancialVariants).Concat(Aggregates),
        "Switch(True, 1, False)");

    [Fact]
    public void Representative_arity_boundaries_match_ACE() => AssertAceExpressions(
        [
            "Len('abc', 1)", "Abs(1, 2)", "Left('abc', 1, 2)", "IIf(True, 1, 2, 3)",
            "Date(1)", "RGB(1, 2, 3, 4)", "Round(1, 2, 3)",
            "Replace('abc', 'a', 'x', 1, -1, 0, 99)",
            "Len()", "Left('abc')", "RGB(1, 2)", "Replace('abc', 'a')",
        ]);

    [Fact]
    public void Libred_only_functions_have_documented_arities() =>
        AssertLibRedRejects("CDec()", "CDec(1, 2)", "GenUniqueID(1)", "GenGUID(1)");

    private static IEnumerable<Arity> Unary(params string[] names) =>
        names.Select(n => new Arity(n, 1, 1, "1", "1"));

    private static void AssertLibRedArities(IEnumerable<Arity> cases, params string[] extraInvalid)
    {
        string path = CopyNorthwind("function-arity-");
        try
        {
            using var db = JetDatabase.Open(path);
            var engine = new QueryEngine(db);
            foreach (Arity item in cases)
            {
                if (item.Min > 0) AssertLibRedRejected(engine, Call(item, item.Min - 1));
                if (item.Max is int max) AssertLibRedRejected(engine, Call(item, max + 1));
            }
            foreach (string expression in extraInvalid) AssertLibRedRejected(engine, expression);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static void AssertAceExpressions(IEnumerable<string> expressions)
    {
        string path = CopyNorthwind("function-arity-ace-");
        try
        {
            using OleDbConnection ace = AceTestDatabase.Open(path);
            using var db = JetDatabase.Open(path);
            var engine = new QueryEngine(db);
            foreach (string expression in expressions) AssertRejected(ace, engine, expression);
            AssertIifQuirk(ace, engine, "IIf(True, 7)", 7);
            AssertIifQuirk(ace, engine, "IIf(False, 7)", null);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static void AssertIifQuirk(OleDbConnection ace, QueryEngine engine, string expression, int? expected)
    {
        using OleDbCommand command = ace.CreateCommand();
        command.CommandText = $"SELECT {expression} AS V FROM Customers";
        object? aceValue = command.ExecuteScalar();
        Assert.Equal(expected, aceValue is DBNull ? null : Convert.ToInt32(aceValue));
        object? libRedValue = engine.ExecuteQuery($"SELECT {expression} AS V FROM Customers").Rows.First()[0];
        Assert.Equal(expected, libRedValue is null ? null : Convert.ToInt32(libRedValue));
    }

    private static string Call(Arity item, int count) =>
        $"{item.Name}({string.Join(", ", item.Arguments.Take(count))})";

    private static void AssertRejected(OleDbConnection ace, QueryEngine engine, string expression)
    {
        using OleDbCommand command = ace.CreateCommand();
        command.CommandText = $"SELECT {expression} AS V FROM Customers";
        Exception? aceError = Record.Exception(() => command.ExecuteScalar());
        Assert.True(aceError is OleDbException,
            $"ACE unexpectedly accepted {expression}; result/error was {aceError?.GetType().Name ?? "no error"}.");
        AssertLibRedRejected(engine, expression);
    }

    private static void AssertLibRedRejected(QueryEngine engine, string expression)
    {
        Exception error = Assert.Throws<InvalidOperationException>(() =>
            engine.ExecuteQuery($"SELECT {expression} AS V FROM Customers").Rows.ToList());
        Assert.Contains("Wrong number of arguments", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertLibRedRejects(params string[] expressions)
    {
        string path = CopyNorthwind("function-arity-libred-");
        try
        {
            using var db = JetDatabase.Open(path);
            var engine = new QueryEngine(db);
            foreach (string expression in expressions)
            {
                Exception error = Assert.Throws<InvalidOperationException>(() =>
                    engine.ExecuteQuery($"SELECT {expression} AS V FROM Customers").Rows.ToList());
                Assert.Contains("Wrong number of arguments", error.Message, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static string CopyNorthwind(string prefix) => TemporaryDatabase.CopyPath(
        Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), prefix);
}
