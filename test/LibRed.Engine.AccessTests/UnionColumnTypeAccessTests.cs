using System.Data.OleDb;
using System.Globalization;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// A UNION's column type, and every value converted to it, run through ACE and through LibRed on copies of the same
/// table: a column of each kind, paired with every other. The types must match and so must the values.
/// </summary>
/// <remarks>
/// A date written as text follows ACE's regional settings (Windows') and LibRed's culture (the test's), so for a pair
/// that turns a date into text only the type is compared. The Large Number rows need an ACE that has the type, which
/// CI's 2016 redistributable does not; they are skipped there.
/// </remarks>
[Collection(AceCollection.Name)]
public class UnionColumnTypeAccessTests(UnionColumnTypeAccessTests.Databases databases)
    : TempDatabaseTest, IClassFixture<UnionColumnTypeAccessTests.Databases>
{
    public sealed class Databases : IDisposable
    {
        private const string Create =
            "CREATE TABLE T (Id INT, M CURRENCY, B BYTE, S SMALLINT, D DATETIME, F DOUBLE, R REAL, E DECIMAL(18,4), "
            + "L LONG, X TEXT(10), Y YESNO, G GUID, N BINARY(4))";
        private const string Insert =
            "INSERT INTO T (Id, M, B, S, D, F, R, E, L, X, Y) "
            + "VALUES (1, 10.5, 3, 7, #2020-01-02#, 2.5, 1.5, 4.25, 70000, 'abc', TRUE)";
        private const string Guid = "00112233-4455-6677-8899-AABBCCDDEEFF";

        private readonly TemporaryDatabase _ace;
        private readonly TemporaryDatabase _libred;

        public Databases()
        {
            string northwind = Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");
            BigInt = AceTestDatabase.SupportsColumnType(northwind, "BIGINT");
            string[] bigInt = BigInt ? ["ALTER TABLE T ADD COLUMN Z BIGINT", "UPDATE T SET Z = 5000000000"] : [];

            _ace = TemporaryDatabase.CopyOf(northwind, "union-types-ace-");
            using (OleDbConnection connection = AceTestDatabase.Open(_ace.Path))
            {
                foreach (string statement in (string[])[Create, Insert, $"UPDATE T SET G = {{guid {{{Guid}}}}}",
                    "UPDATE T SET N = 0x41004200", .. bigInt])
                {
                    using OleDbCommand command = connection.CreateCommand();
                    command.CommandText = statement;
                    command.ExecuteNonQuery();
                }
            }

            _libred = TemporaryDatabase.CopyOf(northwind, "union-types-libred-");
            using (var db = JetDatabase.Open(_libred.Path, readOnly: false))
            {
                var engine = new QueryEngine(db);
                foreach (string statement in (string[])[Create, Insert, $"UPDATE T SET G = {{{Guid}}}",
                    "UPDATE T SET N = 0x41004200", .. bigInt])
                    engine.ExecuteNonQuery(statement);
            }
        }

        /// <summary>Whether the installed ACE has the Large Number type, and so the table has column Z.</summary>
        public bool BigInt { get; }

        public (string Type, string[] Values) Ace(string sql)
        {
            using OleDbConnection connection = AceTestDatabase.Open(_ace.Path);
            using OleDbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            using OleDbDataReader reader = command.ExecuteReader();
            var values = new List<string>();
            while (reader.Read())
                values.Add(Describe(reader.GetValue(0)));
            return (reader.GetFieldType(0).Name, values.ToArray());
        }

        public (string Type, string[] Values) LibRed(string sql)
        {
            using var db = JetDatabase.Open(_libred.Path, readOnly: true);
            var result = new QueryEngine(db).ExecuteQuery(sql);
            return (result.ColumnTypes[0].Name, result.Rows.Select(row => Describe(row[0])).ToArray());
        }

        /// <summary>A value with its type; a Decimal without trailing zeros, which ACE adds for a column's scale.</summary>
        private static string Describe(object? value) => value switch
        {
            null or DBNull => "NULL",
            byte[] bytes => "Byte[] " + Convert.ToHexString(bytes),
            decimal m => "Decimal " + m.ToString("G29", CultureInfo.InvariantCulture),
            double d => "Double " + d.ToString("R", CultureInfo.InvariantCulture),
            float f => "Single " + f.ToString("R", CultureInfo.InvariantCulture),
            _ => $"{value.GetType().Name} {Convert.ToString(value, CultureInfo.InvariantCulture)}",
        };

        public void Dispose()
        {
            _ace.Dispose();
            _libred.Dispose();
        }
    }

    // Left, right, and whether the values are compared: not when a date becomes text, nor where LibRed departs.
    public static TheoryData<string, string, bool> Pairs => new()
    {
        { "B", "5", true }, { "5", "B", true }, { "S", "70000", true }, { "B", "S", true },
        { "NULL", "B", true }, { "B", "NULL", true },
        { "M", "F", true }, { "F", "M", true }, { "E", "F", true }, { "M", "E", true }, { "E", "M", true },
        { "M", "L", true }, { "E", "L", true },
        { "R", "S", true }, { "R", "L", true }, { "L", "F", true },
        { "Y", "B", true }, { "Y", "S", true }, { "Y", "F", true }, { "Y", "M", true }, { "Y", "Y", true },
        { "X", "B", true }, { "B", "X", true }, { "Y", "X", true },
        { "D", "D", true }, { "D", "X", false }, { "D", "F", false }, { "D", "M", false }, { "Y", "D", false },
        { "G", "G", true }, { "G", "X", true }, { "X", "G", true }, { "G", "B", true }, { "G", "N", true },
        { "N", "X", true }, { "N", "B", true }, { "N", "S", true }, { "N", "L", true }, { "N", "F", true },
        { "N", "R", true }, { "N", "D", true }, { "N", "Y", true }, { "N", "M", true }, { "N", "E", true },
        // The Large Number.
        { "Z", "B", true }, { "Z", "S", true }, { "L", "Z", true }, { "Z", "Y", true }, { "Z", "5", true },
        { "Z", "R", true }, { "Z", "F", true }, { "Z", "M", true }, { "M", "Z", true }, { "Z", "E", true },
        { "E", "Z", true }, { "Z", "X", true }, { "Z", "D", true }, { "NULL", "Z", true }, { "Z", "Z", true },
        // ACE cuts a Large Number's text to 8 bytes in a binary column; LibRed keeps all of it.
        { "N", "Z", false },
    };

    [Theory]
    [MemberData(nameof(Pairs))]
    public void A_union_column_matches_ace(string left, string right, bool compareValues)
    {
        if (left == "Z" || right == "Z")
            Assert.SkipUnless(databases.BigInt, AceTestDatabase.UnsupportedColumnTypeReason("BIGINT"));

        string sql = $"SELECT {left} AS c FROM T WHERE Id = 1 UNION ALL SELECT {right} FROM T WHERE Id = 1";
        (string aceType, string[] aceValues) = databases.Ace(sql);
        (string libredType, string[] libredValues) = databases.LibRed(sql);

        Assert.Equal(aceType, libredType);
        if (compareValues)
            Assert.Equal(aceValues, libredValues);
    }
}
