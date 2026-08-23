using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// The make-table query, cross-checked against ACE. The shape it creates was measured first
// (SelectIntoShapeProbeTest); this asserts LibRed lands in the same place, running the same SQL through both.
//
// The comparison covers the created table's COLUMNS and INDEXES as well as its rows, because the surprising
// part of a make-table is what it does not copy: the source's primary key and indexes are dropped, so
// comparing rows alone would pass while the schema silently differed.
[Collection(AceCollection.Name)]
public class SelectIntoAccessTests : TempDatabaseTest
{
    private static string Copy() => TemporaryDatabase.CopyPath(
        Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "selinto-ace-");

    private static readonly string[] Setup =
    [
        "CREATE TABLE SiSrc (Id LONG PRIMARY KEY, Label TEXT(30), Qty LONG)",
        "CREATE INDEX IX_SiSrc_Label ON SiSrc (Label)",
        "INSERT INTO SiSrc (Id, Label, Qty) VALUES (1, 'one', 10)",
        "INSERT INTO SiSrc (Id, Label, Qty) VALUES (2, 'two', 20)",
    ];

    /// <summary>Runs the make-table through both engines and compares the table each produced — its column
    /// names and types, its indexes, and the rows.</summary>
    private static void AssertSameAsAce(string makeTable, string verify)
    {
        string acePath = Copy(), ourPath = Copy();
        try
        {
            using (var connection = AceTestDatabase.Open(acePath))
            {
                foreach (string sql in Setup) Exec(connection, sql);
                Exec(connection, makeTable);
            }

            using (var ourDb = TemporaryDatabase.OpenTracked(ourPath, readOnly: false))
            {
                var engine = new QueryEngine(ourDb);
                foreach (string sql in Setup) engine.ExecuteNonQuery(sql);
                engine.ExecuteNonQuery(makeTable);
            }

            // Read BOTH files with LibRed, so the schemas are described the same way and any difference is a
            // real difference rather than two metadata vocabularies.
            using var aceDb = JetDatabase.Open(acePath);
            using var libRedDb = JetDatabase.Open(ourPath);
            Assert.Equal(Describe(aceDb, "SiNew"), Describe(libRedDb, "SiNew"));

            using var check = JetDatabase.Open(ourPath);
            var ours = new QueryEngine(check).ExecuteQuery(verify).Rows
                .Select(r => string.Join("|", r.Select(v => Convert.ToString(v)))).ToList();
            using (var connection = AceTestDatabase.Open(acePath))
            {
                using var command = connection.CreateCommand();
                command.CommandText = verify;
                using var reader = command.ExecuteReader();
                var theirs = new List<string>();
                while (reader.Read())
                    theirs.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount)
                        .Select(i => Convert.ToString(reader.GetValue(i)))));
                Assert.Equal(theirs, ours);
            }
        }
        finally
        {
            TemporaryDatabase.Delete(acePath);
            TemporaryDatabase.Delete(ourPath);
        }
    }

    private static string Describe(JetDatabase db, string table)
    {
        TableDef? def = db.Catalog.Tables.FirstOrDefault(t => t.Name == table);
        if (def is null) return "(not created)";
        return string.Join(", ", def.Columns.Select(c => $"{c.Name} {c.Type}({c.Length})")) +
               " | indexes: " + (def.Indexes.Count == 0 ? "(none)" : string.Join(", ", def.Indexes.Select(i => i.Name)));
    }

    [Fact]
    public void Named_columns_match_ACE() =>
        AssertSameAsAce("SELECT Id, Label INTO SiNew FROM SiSrc", "SELECT Label FROM SiNew ORDER BY Id");

    // SELECT * carries every column across — and still drops the key and the index.
    [Fact]
    public void Star_matches_ACE() =>
        AssertSameAsAce("SELECT * INTO SiNew FROM SiSrc", "SELECT Label FROM SiNew ORDER BY Id");

    [Fact]
    public void A_filtered_source_matches_ACE() =>
        AssertSameAsAce("SELECT Id, Label INTO SiNew FROM SiSrc WHERE Id = 2", "SELECT Label FROM SiNew");

    // An empty result still creates the table in both.
    [Fact]
    public void An_empty_result_matches_ACE() =>
        AssertSameAsAce("SELECT Id, Label INTO SiNew FROM SiSrc WHERE Id > 99", "SELECT COUNT(*) FROM SiNew");

    private static void Exec(OleDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
