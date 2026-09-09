using System.Reflection;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// Conformance: a calculated column's cached result must decode to the value ACE reads back.
//
// ACE stores the result in the variable-length section wrapped in an envelope (page-02b §3.4a), and the
// descriptor's type is a PROMOTED storage type — the payload's own length is what says how to decode it.
// Neither fact is guessable from the descriptor, so the only way to hold them is to have ACE author a
// column of every type and compare.
//
// LibRed cannot create a calculated column (Access SQL has no syntax for one), so DAO's object model is
// the author here — the same path Access's UI uses. Each column gets its own table because ACE validates
// the expression when the TableDef is appended, and one rejected expression would take the rest with it.
public class CalculatedColumnAccessTests(ITestOutputHelper output)
{
    private const int UseJet = 2;
    private const int DbBoolean = 1, DbByte = 2, DbInteger = 3, DbLong = 4, DbCurrency = 5,
                      DbSingle = 6, DbDouble = 7, DbDate = 8, DbText = 10, DbMemo = 12;

    /// <summary>(column, DAO type, text size, expression) — one per type DAO will accept.</summary>
    private static readonly (string Name, int Type, int Size, string Expression)[] Calculated =
    [
        ("CText",  DbText,     40, "[A] & \"-x\""),
        ("CMemo",  DbMemo,      0, "[A] & \"-memo\""),
        ("CLong",  DbLong,      0, "[Qty]*2"),
        ("CInt",   DbInteger,   0, "[Qty]+1"),
        ("CByte",  DbByte,      0, "[Qty]+2"),
        ("CDbl",   DbDouble,    0, "[Qty]/4"),
        ("CSng",   DbSingle,    0, "[Qty]/8"),
        ("CCur",   DbCurrency,  0, "[Price]*2"),
        ("CDate",  DbDate,      0, "[D1]+1"),
        ("CBool",  DbBoolean,   0, "[Qty]>1"),
    ];

    // Row 3 is the one that matters twice over: Qty=0 makes [Qty]>1 store False (whose null-bitmap bit is
    // set anyway), and the NULLs make [D1]+1 evaluate to Null (stored as a zero-length payload).
    private static readonly string[] Seed =
    [
        "INSERT INTO {0} (Id, Qty, Price, A, D1) VALUES (1, 7, 12.5, 'hello', #2003-09-29#)",
        "INSERT INTO {0} (Id, Qty, Price, A, D1) VALUES (2, 3, 0.25, 'zz', #1999-01-02#)",
        "INSERT INTO {0} (Id, Qty, Price, A, D1) VALUES (3, 0, 0, NULL, NULL)",
    ];

    [Fact]
    public void Decodes_the_same_calculated_values_as_ace()
    {
        object? engine = CreateDbEngine();
        Assert.SkipWhen(engine is null, "DAO is unavailable in this process; it authors the fixture.");

        string path = TemporaryDatabase.CreatePath("calculated-");
        try
        {
            string[] created = CreateFixture(engine!, path);
            Assert.NotEmpty(created);
            output.WriteLine($"DAO created: {string.Join(", ", created)}");

            Dictionary<string, List<object?>> ace = SeedAndReadWithAce(path, created);

            // Opening the catalog at all is half the regression: ACE gives a calculated Memo a long-value
            // map entry while declaring it Text, and the catalog reads EVERY table definition — so a guard
            // that rejected the entry made every table in the database unreadable, not just that one.
            using var db = JetDatabase.Open(path, readOnly: true);
            Assert.NotEmpty(db.Catalog.Tables);

            var mismatches = new List<string>();
            foreach (string name in created)
            {
                Table table = db.OpenTable("T_" + name);
                int index = table.Definition.FindColumn(name)!.Index;
                List<object?> libred = [.. table.Rows().Select(r => r[index])];
                List<object?> expected = ace[name];

                output.WriteLine($"  {name,-6} ACE [{Format(expected)}]  LibRed [{Format(libred)}]");
                if (libred.Count != expected.Count)
                {
                    mismatches.Add($"{name}: {expected.Count} rows from ACE, {libred.Count} from LibRed");
                    continue;
                }
                for (int i = 0; i < expected.Count; i++)
                    if (!Matches(expected[i], libred[i]))
                        mismatches.Add($"{name} row {i + 1}: ACE {Describe(expected[i])}, LibRed {Describe(libred[i])}");
            }

            Assert.Empty(mismatches);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Reading these tables is new, which makes writing one newly reachable — and a write would put a bare
    // value where ACE expects an envelope, corrupting the column silently. Refusing is the honest outcome
    // while only ACE can evaluate the expression.
    [Fact]
    public void Refuses_to_write_a_row_containing_a_calculated_column()
    {
        object? engine = CreateDbEngine();
        Assert.SkipWhen(engine is null, "DAO is unavailable in this process; it authors the fixture.");

        string path = TemporaryDatabase.CreatePath("calculated-write-");
        try
        {
            string[] created = CreateFixture(engine!, path);
            Assert.Contains("CLong", created);

            using var db = JetDatabase.Open(path, readOnly: false);
            Table table = db.OpenTable("T_CLong");
            var values = new object?[table.Definition.Columns.Count];
            values[table.Definition.FindColumn("Id")!.Index] = 1;

            var ex = Assert.Throws<NotSupportedException>(() => table.Insert(values));
            output.WriteLine(ex.Message);
            Assert.Contains("CLong", ex.Message);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // DELETE does not encode a row, so it is still allowed — but it frees the deleted row's long values,
    // and a calculated Memo reaches its pages through a descriptor while being declared Text. Keying that
    // off the declared type walked past it and orphaned the pages; ACE must still read the table after.
    [Fact]
    public void Deleting_a_row_frees_a_calculated_memo_and_leaves_the_table_readable()
    {
        object? engine = CreateDbEngine();
        Assert.SkipWhen(engine is null, "DAO is unavailable in this process; it authors the fixture.");

        string path = TemporaryDatabase.CreatePath("calculated-delete-");
        try
        {
            string[] created = CreateFixture(engine!, path);
            Assert.Contains("CMemo", created);
            SeedAndReadWithAce(path, ["CMemo"]);

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                Table table = db.OpenTable("T_CMemo");
                RowId first = table.Rows().WithIds().First().Id;
                table.Delete(first);
            }

            using (var db = JetDatabase.Open(path, readOnly: true))
            {
                Table table = db.OpenTable("T_CMemo");
                int index = table.Definition.FindColumn("CMemo")!.Index;
                List<object?> remaining = [.. table.Rows().Select(r => r[index])];
                Assert.Equal(["zz-memo", "-memo"], remaining);
            }

            // ACE is the arbiter of whether the delete left the file coherent.
            using var connection = AceTestDatabase.Open(path);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT CMemo FROM T_CMemo ORDER BY Id";
            using var reader = command.ExecuteReader();
            var ace = new List<object?>();
            while (reader.Read()) ace.Add(reader.IsDBNull(0) ? null : reader.GetValue(0));
            Assert.Equal(["zz-memo", "-memo"], ace);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>Builds the database and one table per calculated column, returning those DAO accepted.</summary>
    private string[] CreateFixture(object engine, string path)
    {
        object workspace = Invoke(engine, "CreateWorkspace", "", "admin", "", UseJet)!;
        object database = Invoke(workspace, "CreateDatabase", path, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128)!;

        var created = new List<string>();
        foreach ((string name, int type, int size, string expression) in Calculated)
        {
            try
            {
                object tdf = Invoke(database, "CreateTableDef", "T_" + name)!;
                object fields = GetProperty(tdf, "Fields")!;
                Invoke(fields, "Append", Invoke(tdf, "CreateField", "Id", DbLong)!);
                Invoke(fields, "Append", Invoke(tdf, "CreateField", "Qty", DbLong)!);
                Invoke(fields, "Append", Invoke(tdf, "CreateField", "Price", DbCurrency)!);
                Invoke(fields, "Append", Invoke(tdf, "CreateField", "A", DbText, 20)!);
                Invoke(fields, "Append", Invoke(tdf, "CreateField", "D1", DbDate)!);

                object field = size > 0
                    ? Invoke(tdf, "CreateField", name, type, size)!
                    : Invoke(tdf, "CreateField", name, type)!;
                SetProperty(field, "Expression", expression);
                Invoke(fields, "Append", field);
                Invoke(GetProperty(database, "TableDefs")!, "Append", tdf);
                created.Add(name);
            }
            catch (TargetInvocationException ex)
            {
                // A future ACE could narrow what an expression may contain; skip rather than fail on it.
                output.WriteLine($"  {name,-6} rejected by DAO: {ex.InnerException?.Message.Trim()}");
            }
        }

        Invoke(database, "Close");
        return [.. created];
    }

    /// <summary>Seeds the base columns through ACE — which computes and caches each result — then reads the
    /// calculated values back the same way, so the expectation is ACE's own answer.</summary>
    private static Dictionary<string, List<object?>> SeedAndReadWithAce(string path, string[] created)
    {
        var values = new Dictionary<string, List<object?>>();
        using var connection = AceTestDatabase.Open(path);
        foreach (string name in created)
        {
            foreach (string template in Seed)
            {
                using var insert = connection.CreateCommand();
                insert.CommandText = string.Format(template, "T_" + name);
                insert.ExecuteNonQuery();
            }

            using var select = connection.CreateCommand();
            select.CommandText = $"SELECT [{name}] FROM [T_{name}] ORDER BY Id";
            using var reader = select.ExecuteReader();
            var read = new List<object?>();
            while (reader.Read()) read.Add(reader.IsDBNull(0) ? null : reader.GetValue(0));
            values[name] = read;
        }
        return values;
    }

    /// <summary>Compares across two providers, which disagree about width and decimal scale but not value:
    /// ACE hands back a Currency zero as <c>0.0000</c> where LibRed says <c>0</c>.</summary>
    private static bool Matches(object? ace, object? libred)
    {
        if (ace is null) return libred is null;
        if (libred is null) return false;
        if (ace is string || libred is string) return string.Equals(ace.ToString(), libred.ToString(), StringComparison.Ordinal);
        if (ace is bool || libred is bool) return Convert.ToBoolean(ace) == Convert.ToBoolean(libred);
        if (ace is DateTime || libred is DateTime) return Convert.ToDateTime(ace) == Convert.ToDateTime(libred);
        return Convert.ToDecimal(ace) == Convert.ToDecimal(libred);
    }

    private static string Format(IEnumerable<object?> values) => string.Join(", ", values.Select(Describe));

    private static string Describe(object? value) => value switch
    {
        null => "<null>",
        string s => $"\"{s}\"",
        DateTime d => d.ToString("yyyy-MM-dd HH:mm:ss"),
        _ => $"{value} ({value.GetType().Name})",
    };

    private static object? CreateDbEngine()
    {
        foreach (int n in new[] { 170, 160, 150, 140, 130, 120 })
        {
            Type? type = Type.GetTypeFromProgID($"DAO.DBEngine.{n}");
            if (type is null) continue;
            try { return Activator.CreateInstance(type); }
            catch (Exception) { /* registered but not instantiable in this bitness */ }
        }
        return null;
    }

    private static object? Invoke(object target, string member, params object?[] args) =>
        target.GetType().InvokeMember(member, BindingFlags.InvokeMethod, null, target, args);

    private static object? GetProperty(object target, string member) =>
        target.GetType().InvokeMember(member, BindingFlags.GetProperty, null, target, null);

    private static void SetProperty(object target, string member, object? value) =>
        target.GetType().InvokeMember(member, BindingFlags.SetProperty, null, target, [value]);
}
