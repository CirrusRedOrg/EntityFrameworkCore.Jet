using System.Data.OleDb;
using System.Globalization;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// How ACE reads the names in a query, through ACE over OLE DB and through LibRed: what <c>Customers!CustomerID</c>
/// resolves to and what the column is called, a declared form control as a parameter and a stored query that declares
/// one, a reserved word naming a column after a period or a bang, a name in any script written unbracketed, and Yes,
/// No, On and Off as True and False.
/// </summary>
[Collection(AceCollection.Name)]
public class QueryNamesAccessTests(ITestOutputHelper output) : TempDatabaseTest
{
    // Every reserved word LibRed knows, each a column of table K. ACE refuses Union and When even qualified.
    private static readonly string[] Keywords =
    [
        "Select", "From", "Where", "Top", "As", "And", "Or", "Not", "Xor", "Eqv", "Imp", "Band", "Bor", "Bxor", "Bnot",
        "Like", "Mod", "Inner", "Left", "Right", "Full", "Outer", "Join", "In", "On", "Order", "Group", "Is", "By",
        "Having", "Exists", "If", "Then", "Distinctrow", "Distinct", "Percent", "Cross", "Apply", "Over", "Partition",
        "Case", "Else", "End", "Offset", "Fetch", "Next", "First", "Rows", "Row", "Only", "Between", "All", "Intersect",
        "Except", "Create", "Table", "Begin", "Commit", "Rollback", "Transaction", "Work", "Alter", "Rename", "To", "Add",
        "Drop", "Column", "Insert", "Into", "Values", "Primary", "Key", "Constraint", "Foreign", "References", "Delete",
        "Update", "Cascade", "Restrict", "Action", "Set", "Default", "No", "Unique", "Clustered", "Identity",
        "Nonclustered", "Index", "Temporary", "With", "Compression", "Comp", "Disallow", "Ignore", "Check", "View",
        "Procedure", "Parameters", "Execute", "Exec", "Asc", "Desc", "True", "False", "Null", "Yes", "Off", "Language",
    ];

    // ANSI-92 reserved words Access's own SQL does not use. OLE DB runs ACE in ANSI-92 mode, and ACE before Access
    // version 2311 (build 16.0.17029) refuses one as a name even qualified, with reserved error -1001, which has no
    // message; 2311 fixed that, so the current engine takes each as any other name. The ACE 2016 redistributable and
    // ACE 2010 are both older, and LibRed follows the current engine.
    private static readonly HashSet<string> RefusedBefore2311 = new(StringComparer.OrdinalIgnoreCase)
    {
        "Full", "Then", "Cross", "End", "Fetch", "Next", "Rows", "Only", "Intersect", "Except", "Restrict", "Temporary",
        "Language",
    };

    // The operator words, which ACE refuses after a bang (as are Select and All).
    private static readonly HashSet<string> RefusedAfterBang = new(StringComparer.OrdinalIgnoreCase)
    {
        "Select", "All", "And", "Or", "Not", "Xor", "Eqv", "Imp", "Band", "Bor", "Bxor", "Bnot", "Like", "Mod", "In",
        "Is", "Between", "Exists",
    };

    private static readonly string[] Setup =
    [
        $"CREATE TABLE K ({string.Join(", ", Keywords.Select(k => $"[{k}] LONG"))})",
        $"INSERT INTO K ({string.Join(", ", Keywords.Select(k => $"[{k}]"))}) VALUES ({string.Join(", ", Keywords.Select((_, i) => (i + 100).ToString(CultureInfo.InvariantCulture)))})",
        "CREATE TABLE [Árú] ([Név] TEXT(10), [Номер] LONG, [名前] TEXT(10), [ΑΒΓ] LONG, [straße] LONG, [Ñandú] LONG, [n1] LONG)",
        "INSERT INTO [Árú] VALUES ('x', 2, 'y', 3, 4, 5, 6)",
    ];

    public static TheoryData<string> Queries
    {
        get
        {
            var queries = new TheoryData<string>
            {
                "SELECT Customers!CustomerID, [Customers]![City], `Customers`!`Country` FROM Customers WHERE CustomerID = 'ALFKI'",
                "SELECT Customers!CustomerID AS X FROM Customers WHERE CustomerID = 'ALFKI'",
                "SELECT c!CustomerID FROM Customers AS c WHERE c!City = 'Berlin'",
                "SELECT [c]!CustomerID FROM Customers AS [c] WHERE [c]![City] = 'Berlin'",
                "SELECT City, COUNT(*) AS N FROM Customers GROUP BY Customers!City HAVING Customers!City > 'S' ORDER BY Customers!City DESC",
                "SELECT TOP 3 CustomerID FROM Customers ORDER BY Customers!CustomerID DESC",
                "SELECT CustomerID, (SELECT COUNT(*) FROM Orders WHERE Orders!CustomerID = Customers!CustomerID) AS N FROM Customers WHERE Country = 'Germany'",
                "SELECT d!X FROM (SELECT CustomerID AS X FROM Customers WHERE Country = 'Germany') AS d ORDER BY d!X",
                "SELECT * FROM (SELECT Customers!City, [Customers]![Country] FROM Customers WHERE CustomerID = 'ALFKI')",
                "SELECT [Customers!City] FROM (SELECT Customers!City FROM Customers WHERE CustomerID = 'ALFKI')",
                "SELECT Customers!City FROM Customers WHERE CustomerID = 'ALFKI' UNION SELECT Customers!City FROM Customers WHERE CustomerID = 'ANATR'",
                "SELECT MAX(Customers!CustomerID) AS M FROM Customers",
                "SELECT Név, Номер, 名前, ΑΒΓ, straße, Ñandú, n1 FROM Árú",
                "SELECT Árú.Név, Árú!Номер FROM Árú",
                "SELECT a.Név FROM Árú AS a WHERE a.ΑΒΓ = 3",
                // Through CInt and IIF: ACE types True and False, and so these, as Int16 where LibRed has a Boolean.
                "SELECT CInt(Yes) AS A, CInt(No) AS B, CInt(On) AS C, CInt(Off) AS D, CInt(True) AS E, CInt(False) AS F FROM K",
                "SELECT Yes + 1 AS A, IIF(Not No, 1, 2) AS B, IIF(Yes AND No, 1, 2) AS C, IIF(Yes = True, 1, 2) AS D, IIF(Off = False, 1, 2) AS E FROM K",
                "SELECT CustomerID FROM Customers WHERE (CustomerID = 'ALFKI') = Yes",
                "SELECT CustomerID FROM Customers WHERE On AND CustomerID < 'B'",
                // Unqualified, a name that is also a constant is the constant; qualified, it is the column.
                "SELECT CInt(Yes) AS A, K.Yes AS B, CInt(No) AS C, K.No AS D, CInt(Off) AS E, K.Off AS F, K.On AS G FROM K",
            };
            foreach (string k in Keywords)
            {
                queries.Add($"SELECT K.{k} FROM K");
                if (!RefusedAfterBang.Contains(k)) queries.Add($"SELECT K!{k} FROM K");
            }
            return queries;
        }
    }

    // A declared parameter, bound by position in ACE and by name in LibRed.
    public static TheoryData<string, string> Declared => new()
    {
        { "PARAMETERS [Forms]![f]![c] Long; SELECT CustomerID, Forms!f!c FROM Customers WHERE CustomerID = 'ALFKI'", "Forms!f!c" },
        { "PARAMETERS Forms!f!c Long; SELECT [Forms]![f]![c] FROM Customers WHERE CustomerID = 'ALFKI'", "Forms!f!c" },
        { "PARAMETERS Forms!x Long; SELECT Forms!x, Forms.x FROM Customers WHERE CustomerID = 'ALFKI'", "Forms!x" },
        { "PARAMETERS Forms!f!c Long; SELECT Forms!f.c, Forms.f!c FROM Customers WHERE CustomerID = 'ALFKI'", "Forms!f!c" },
        { "PARAMETERS Customers!City Long; SELECT Customers!City FROM Customers WHERE CustomerID = 'ALFKI'", "Customers!City" },
        { "PARAMETERS p Long; SELECT p FROM Customers WHERE CustomerID = 'ALFKI'", "p" },
        { "PARAMETERS [Forms]![f]![c] Long; SELECT CustomerID FROM Orders WHERE OrderID = Forms!f!c + 10247", "Forms!f!c" },
    };

    [Theory]
    [MemberData(nameof(Queries))]
    public void A_query_reads_and_names_as_ace_does(string query) =>
        Matches(query, command => { }, null,
            refusedBefore2311: RefusedBefore2311.Any(k => query == $"SELECT K.{k} FROM K" || query == $"SELECT K!{k} FROM K"));

    [Theory]
    [MemberData(nameof(Declared))]
    public void A_declared_form_control_is_a_parameter_as_in_ace(string query, string name) =>
        Matches(query, command => command.Parameters.AddWithValue("?", 1), new Dictionary<string, object?> { [name] = 1 });

    // A stored query declaring a form control, saved by Access, reads back and runs through LibRed; made by LibRed, it
    // stores the same declaration and runs in ACE. ACE's own CREATE PROCEDURE will not take a chain as a parameter
    // name, so Access's copy is saved through DAO, as the Access UI saves one.
    [Fact]
    public void A_stored_query_declaring_a_form_control_round_trips_with_ace()
    {
        const string body = "SELECT CustomerID FROM Customers WHERE City = Forms!frmMenu!txtCity";

        string byAce = Copy(), byLibRed = Copy();
        try
        {
            object? engine = AceTestDatabase.CreateDaoEngine();
            Assert.SkipWhen(engine is null, "DAO is not registered in this bitness.");
            object database = Invoke(engine!, "OpenDatabase", byAce, false, false, "")!;
            Invoke(database, "CreateQueryDef", "ByCity", $"PARAMETERS [Forms]![frmMenu]![txtCity] Text ( 20 ); {body}");
            Invoke(database, "Close");

            using (var db = JetDatabase.Open(byLibRed, readOnly: false))
                new QueryEngine(db).ExecuteNonQuery($"CREATE PROCEDURE ByCity [Forms]![frmMenu]![txtCity] Text(20) AS {body}");

            string aceSql, libredSql;
            using (var db = JetDatabase.Open(byAce, readOnly: true))
            {
                aceSql = db.Catalog.Views["ByCity"];
                Assert.Equal("Forms!frmMenu!txtCity", db.Catalog.QueryParameters["ByCity"].Single().Name);
                Assert.Equal(["ALFKI"], new QueryEngine(db).ExecuteQuery("EXECUTE ByCity 'Berlin'").Rows.Select(r => r[0]));
            }
            using (var db = JetDatabase.Open(byLibRed, readOnly: true))
                libredSql = db.Catalog.Views["ByCity"];
            output.WriteLine($"ACE    {aceSql}\nLibRed {libredSql}");
            Assert.Equal(aceSql, libredSql);

            using OleDbConnection ace = AceTestDatabase.Open(byLibRed);
            using OleDbCommand run = ace.CreateCommand();
            run.CommandText = "EXECUTE ByCity 'Berlin'";
            Assert.Equal("ALFKI", run.ExecuteScalar());
        }
        finally
        {
            TemporaryDatabase.Delete(byAce);
            TemporaryDatabase.Delete(byLibRed);
        }
    }

    /// <summary>Runs the query through ACE and through LibRed, over a copy of Northwind with tables K and Árú made
    /// by ACE, and compares each column's name and type and every value. <paramref name="refusedBefore2311"/> marks a
    /// query an ACE older than version 2311 refuses (<see cref="RefusedBefore2311"/>), which is skipped there.</summary>
    private void Matches(string query, Action<OleDbCommand> bind, IReadOnlyDictionary<string, object?>? parameters,
        bool refusedBefore2311 = false)
    {
        string path = Copy();
        try
        {
            string ace;
            using (OleDbConnection connection = AceTestDatabase.Open(path))
            {
                foreach (string statement in Setup)
                {
                    using OleDbCommand setup = connection.CreateCommand();
                    setup.CommandText = statement;
                    setup.ExecuteNonQuery();
                }
                using OleDbCommand command = connection.CreateCommand();
                command.CommandText = query;
                bind(command);
                OleDbDataReader executed;
                try { executed = command.ExecuteReader(); }
                catch (OleDbException) when (refusedBefore2311)
                {
                    Assert.Skip("This ACE predates Access version 2311 and refuses an unused ANSI-92 reserved word as a name "
                        + "(reserved error -1001).");
                    throw;
                }
                using OleDbDataReader reader = executed;
                var columns = Enumerable.Range(0, reader.FieldCount).Select(i => $"{reader.GetName(i)}:{reader.GetFieldType(i).Name}").ToList();
                var rows = new List<object?[]>();
                while (reader.Read())
                    rows.Add([.. Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? null : reader.GetValue(i))]);
                ace = Describe(columns, rows);
            }

            string libred;
            using (var database = JetDatabase.Open(path, readOnly: true))
            {
                var result = new QueryEngine(database).ExecuteQuery(query, parameters);
                libred = Describe([.. result.ColumnNames.Zip(result.ColumnTypes, (n, t) => $"{n}:{t.Name}")], result.Rows);
            }

            output.WriteLine($"ACE    {ace}\nLibRed {libred}");
            Assert.Equal(ace, libred);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static object? Invoke(object target, string member, params object?[] args) =>
        target.GetType().InvokeMember(member, System.Reflection.BindingFlags.InvokeMethod, null, target, args);

    private static string Copy() =>
        TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "names-");

    private static string Describe(IReadOnlyList<string> columns, IEnumerable<object?[]> rows) =>
        $"[{string.Join(", ", columns)}] " + string.Join(" | ", rows.Select(row =>
            string.Join(", ", row.Select(v => v is null ? "NULL" : Convert.ToString(v, CultureInfo.InvariantCulture)))));
}
