using System.Data.OleDb;
using System.Globalization;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// Variants and Mixed values, as ACE's expression service types them: <c>CVar(x)</c> and what keeps one, and a
/// choice whose values disagree in kind. Each query runs through ACE over OLE DB and through LibRed, and the column
/// types and every value must agree — which covers where each becomes text (a result, a derived table, a scalar
/// subquery, a union), where it counts as a Double, and where it sorts, groups and takes Max as its text.
/// </summary>
/// <remarks>ACE writes a date out in the Windows user locale, so LibRed runs under that culture here; the rest of the
/// suite pins en-US.</remarks>
[Collection(AceCollection.Name)]
public class VariantAccessTests(ITestOutputHelper output) : TempDatabaseTest
{
    private static readonly string[] Setup =
    [
        "CREATE TABLE V (Id LONG, B BYTE, F DOUBLE, M CURRENCY, D DATETIME, Y BIT, T TEXT(20), G GUID, N VARBINARY(4))",
        "INSERT INTO V VALUES (1, 3, 2.25, 10.5, #2020-01-02 03:04:05#, TRUE, '8', {00112233-4455-6677-8899-AABBCCDDEEFF}, 0x41004200)",
        "INSERT INTO V VALUES (2, 10, 7.5, 1.25, #2021-05-06#, FALSE, '9', NULL, NULL)",
        "INSERT INTO V VALUES (3, 25, 0.5, 3, #2019-12-31#, TRUE, '10', NULL, NULL)",
    ];

    public static TheoryData<string> Expressions =>
    [
        // A Variant is written out as text, as CStr writes it.
        "CVar(B)", "CVar(F)", "CVar(M)", "CVar(D)", "CVar(Y)", "CVar(T)", "CVar(G)", "CVar(N)", "CVar(CVar(B))",
        "-CVar(B)",
        // + keeps a Variant beside a Variant or text; it adds or concatenates as the values are.
        "CVar(B) + CVar(B)", "CVar(D) + CVar(B)", "CVar(T) + CVar(T)", "CVar(T) + CVar(B)", "CVar(B) + '1'",
        "CVar(B) + T",
        // Anything else counts a Variant as a Double.
        "CVar(B) + 1", "1 + CVar(B)", "CVar(B) * 2", "CVar(B) - B", "CVar(B) / 2", "CVar(B) ^ 2", "CVar(B) \\ 2",
        "CVar(B) MOD 2", "CVar(B) - CVar(B)", "CVar(B) * CVar(B)", "CVar(B) / CVar(B)", "CVar(D) + 1", "CVar(D) - D",
        "CVar(M) + 1", "CVar(M) * 2", "CVar(B) + M", "CVar(M) + M", "CVar(F) + 1", "CVar(T) + 1", "CVar(B) & 'x'",
        "CInt(CVar(B))", "CStr(CVar(B))", "Abs(CVar(B))", "Round(CVar(F), 1)", "Len(CVar(B))", "Left(CVar(T), 1)",
        "DateAdd('d', 1, CVar(D))", "Int(CVar(F))",
        // A choice: a Variant when all its values are, Mixed when only some are or text meets another kind.
        "IIF(Id = 1, CVar(B), 5)", "IIF(Id = 1, 5, CVar(B))", "IIF(Id = 1, CVar(B), CVar(B))", "IIF(Id = 1, CVar(B), NULL)",
        "IIF(Id = 1, IIF(Id = 2, CVar(B), 1), 2)", "IIF(Id = 1, CVar(B), 5) + 1",
        "IIF(Id = 1, CVar(B), 5) + IIF(Id = 1, CVar(B), 5)",
        "IIF(Id = 1, CVar(B), CVar(B)) + IIF(Id = 1, CVar(B), CVar(B))", "SWITCH(Id = 1, CVar(B), TRUE, 7)",
        "CHOOSE(Id, CVar(B), 5, 6)", "IIF(Id = 1, T, 2)", "IIF(Id = 1, T, 2) + 1", "IIF(Id = 1, T, 2) + IIF(Id = 1, T, 2)",
        "IIF(Id = 1, T, T) + IIF(Id = 1, T, T)", "IIF(Id = 1, D, T)", "IIF(Id = 1, Y, T)", "IIF(Id = 1, G, T)",
        "IIF(Id = 1, N, T)", "IIF(Id = 1, T, NULL)", "SWITCH(Id = 1, T, TRUE, 2)", "CHOOSE(Id, T, 2, 3)",
        "-IIF(Id = 1, T, 2)", "IIF(Id = 1, T, 2) + CVar(B)", "IIF(Id = 1, T, 2) + T", "IIF(Id = 1, T, 2) * 2",
        "Abs(IIF(Id = 1, T, 2))", "CStr(IIF(Id = 1, CVar(B), 5))", "IIF(Id = 1, CVar(B), 5) & ''",
        "SWITCH(Id = 1, CVar(B), TRUE, CVar(B))", "CHOOSE(Id, CVar(B), CVar(B), CVar(B))",
        "IIF(Id = 1, IIF(Id = 1, CVar(B), CVar(B)), 2)", "IIF(Id = 1, CVar(B) + CVar(B), 2)",
        "IIF(Id = 1, IIF(Id = 1, CVar(B), CVar(B)), 2) + 1",
        // A date beside a number is a date; a Boolean beside one a Long.
        "IIF(Id = 1, D, 2)", "IIF(Id = 1, D, 2) + 1", "IIF(Id = 1, Y, 2)",
        // Aggregates: Min, Max, First and Last over text; the others count a Variant as a Double.
        "SUM(CVar(B))", "MAX(CVar(B))", "MIN(CVar(B))", "MIN(CVar(T))", "AVG(CVar(F))", "COUNT(CVar(B))",
        "FIRST(CVar(B))", "LAST(CVar(B))", "MAX(CVar(D))", "STDEV(CVar(F))", "MAX(CVar(B) + 1)", "MAX(-CVar(B))",
        "MAX(IIF(Id = 0, 1, T))", "MAX(IIF(Id = 0, 1, B))", "FIRST(IIF(Id = 1, T, 2))", "SUM(IIF(Id = 0, 1, T))",
    ];

    public static TheoryData<string> Queries =>
    [
        // Grouping and ordering compare the text; a filter compares the value itself.
        "SELECT CVar(B), COUNT(*) FROM V GROUP BY CVar(B)",
        "SELECT IIF(Id = 0, 1, T), COUNT(*) FROM V GROUP BY IIF(Id = 0, 1, T)",
        "SELECT Id FROM V ORDER BY CVar(B)", "SELECT Id FROM V ORDER BY -CVar(B)", "SELECT Id FROM V ORDER BY CVar(B) + 1",
        "SELECT Id FROM V ORDER BY IIF(Id = 0, 1, T)", "SELECT Id FROM V ORDER BY CVar(T)",
        "SELECT Id FROM V WHERE CVar(B) > 5", "SELECT Id FROM V WHERE CVar(B) = '3'",
        "SELECT Id FROM V WHERE CVar(T) > 9", "SELECT Id FROM V WHERE CVar(T) > '9'",
        // A derived table keeps a Variant and writes a Mixed value out as text.
        "SELECT X + X FROM (SELECT CVar(B) AS X FROM V)", "SELECT X + 1 FROM (SELECT CVar(B) AS X FROM V)",
        "SELECT X FROM (SELECT CVar(B) AS X FROM V) ORDER BY X",
        "SELECT X + X FROM (SELECT IIF(Id = 1, CVar(B), 5) AS X FROM V)",
        "SELECT X + X FROM (SELECT IIF(Id = 1, T, 2) AS X FROM V)",
        "SELECT X + X FROM (SELECT IIF(Id = 1, CVar(B), CVar(B)) AS X FROM V)",
        "SELECT X + X FROM (SELECT IIF(Id = 1, CVar(B), 5) + 0 AS X FROM V)",
        "SELECT X + X FROM (SELECT -CVar(B) AS X FROM V)", "SELECT X + X FROM (SELECT CVar(B) + CVar(B) AS X FROM V)",
        "SELECT X + X FROM (SELECT CVar(B) + '1' AS X FROM V)", "SELECT X + X FROM (SELECT CVar(B) + T AS X FROM V)",
        "SELECT X + X FROM (SELECT IIF(Id = 1, T, 2) + CVar(B) AS X FROM V)",
        "SELECT X + X FROM (SELECT SWITCH(Id = 1, CVar(B), TRUE, CVar(B)) AS X FROM V)",
        "SELECT X + X FROM (SELECT CHOOSE(Id, CVar(B), CVar(B), CVar(B)) AS X FROM V)",
        "SELECT X + X FROM (SELECT IIF(Id = 1, IIF(Id = 1, CVar(B), CVar(B)), 2) AS X FROM V)",
        "SELECT X + X FROM (SELECT IIF(Id = 1, CVar(B) + CVar(B), 2) AS X FROM V)",
        "SELECT X, COUNT(*) FROM (SELECT CVar(B) AS X FROM V) GROUP BY X", "SELECT MAX(X) FROM (SELECT CVar(B) AS X FROM V)",
        // A scalar subquery and a union write a Variant out as text.
        "SELECT (SELECT CVar(B) FROM V WHERE Id = 2) FROM V", "SELECT (SELECT CVar(B) FROM V WHERE Id = 2) + 1 FROM V",
        "SELECT (SELECT CVar(B) FROM V WHERE Id = 2) + (SELECT CVar(B) FROM V WHERE Id = 2) FROM V",
        "SELECT (SELECT MAX(CVar(B)) FROM V) FROM V",
        "SELECT CVar(B) FROM V UNION ALL SELECT B FROM V", "SELECT B FROM V UNION ALL SELECT CVar(B) FROM V",
        "SELECT X + X FROM (SELECT CVar(B) AS X FROM V UNION ALL SELECT B FROM V)",
    ];

    [Theory]
    [MemberData(nameof(Expressions))]
    public void An_expression_is_typed_and_valued_as_ace_does(string expression) =>
        Matches($"SELECT {expression} FROM V");

    [Theory]
    [MemberData(nameof(Queries))]
    public void A_query_is_typed_and_valued_as_ace_does(string query) => Matches(query);

    // A make-table query writes a Variant or a Mixed value out as text, into a Text(255) column; a value over one that
    // counts as a number keeps its number column.
    [Fact]
    public void Select_into_makes_a_variant_a_text_column_as_ace_does()
    {
        const string query = "SELECT CVar(B) AS XB, CVar(D) AS XD, IIF(Id = 1, T, 2) AS XM, CVar(B) + 1 AS XP INTO W FROM V";
        string ace = MadeTable(path =>
        {
            using OleDbConnection connection = AceTestDatabase.Open(path);
            using OleDbCommand command = connection.CreateCommand();
            command.CommandText = query;
            return command.ExecuteNonQuery();
        });
        string libred = MadeTable(path => UnderUserCulture(() =>
        {
            using var database = JetDatabase.Open(path, readOnly: false);
            return new QueryEngine(database).ExecuteNonQuery(query);
        }));

        output.WriteLine($"ACE    {ace}\nLibRed {libred}");
        Assert.Equal(ace, libred);
    }

    /// <summary>Table W as a make-table query left it: each column's type and length, and its rows.</summary>
    private static string MadeTable(Func<string, int> run)
    {
        string path = Seeded();
        try
        {
            run(path);
            using var database = JetDatabase.Open(path, readOnly: true);
            var table = database.Catalog.FindTable("W")!;
            return $"[{string.Join(", ", table.Columns.Select(c => $"{c.Name} {c.Type}({c.Length})"))}] "
                + Rows(new QueryEngine(database).ExecuteQuery("SELECT * FROM W").Rows);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private void Matches(string query)
    {
        string path = Seeded();
        try
        {
            string ace;
            using (OleDbConnection connection = AceTestDatabase.Open(path))
            using (OleDbCommand command = connection.CreateCommand())
            {
                command.CommandText = query;
                using OleDbDataReader reader = command.ExecuteReader();
                var types = Enumerable.Range(0, reader.FieldCount).Select(reader.GetFieldType).ToList();
                var rows = new List<object?[]>();
                while (reader.Read())
                    rows.Add([.. Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? null : reader.GetValue(i))]);
                ace = Describe(types, rows);
            }

            string libred = UnderUserCulture(() =>
            {
                using var database = JetDatabase.Open(path, readOnly: true);
                var result = new QueryEngine(database).ExecuteQuery(query);
                return Describe(result.ColumnTypes, result.Rows);
            });

            output.WriteLine($"ACE    {ace}\nLibRed {libred}");
            Assert.Equal(ace, libred);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>A copy of Northwind with table V made and filled through ACE.</summary>
    private static string Seeded()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "variant-");
        using OleDbConnection connection = AceTestDatabase.Open(path);
        foreach (string statement in Setup)
        {
            using OleDbCommand setup = connection.CreateCommand();
            setup.CommandText = statement;
            setup.ExecuteNonQuery();
        }
        return path;
    }

    /// <summary>Runs LibRed under the Windows user's own locale and formats, which ACE writes a date out in.</summary>
    private static T UnderUserCulture<T>(Func<T> run)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture =
            Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\International")?.GetValue("LocaleName") is string name
                ? new CultureInfo(name, useUserOverride: true)
                : CultureInfo.InstalledUICulture;
        try { return run(); }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    private static string Describe(IReadOnlyList<Type> types, IEnumerable<object?[]> rows) =>
        $"[{string.Join(", ", types.Select(t => t.Name))}] " + Rows(rows);

    private static string Rows(IEnumerable<object?[]> rows) =>
        string.Join(" | ", rows.Select(row => string.Join(", ", row.Select(Value))));

    private static string Value(object? value) => value switch
    {
        null => "NULL",
        byte[] bytes => $"{Convert.ToHexString(bytes)}:Byte[]",
        DateTime date => $"{date:o}:DateTime",
        double d => $"{d.ToString("R", CultureInfo.InvariantCulture)}:Double",
        // A Decimal's scale is not compared: LibRed's Currency sums keep .NET's (21.0) where ACE's do not (21).
        decimal m => $"{(m / 1.0000000000000000000000000000m).ToString(CultureInfo.InvariantCulture)}:Decimal",
        _ => $"{Convert.ToString(value, CultureInfo.InvariantCulture)}:{value.GetType().Name}",
    };
}
