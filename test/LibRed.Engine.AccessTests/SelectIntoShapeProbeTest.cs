using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Engine.Tests;

// PROBE: what a make-table query actually creates.
//
//   SELECT field1[, field2[, …]] INTO newtable [IN externaldatabase] FROM source
//
// Before implementing it, measure what ACE puts in the new table, because almost every part is a choice that
// could reasonably go either way and would be silently wrong if guessed:
//   - do the columns keep the source's types AND sizes, or widen to a default?
//   - does the new table inherit the primary key, the indexes, Required, defaults?
//   - what type does an EXPRESSION column get, where there is no source column to copy?
//   - what does it do when the target name is already taken? The docs say "a trappable error".
//
// Opt-in via LIBRED_SELECT_INTO=1.
[Collection(AceCollection.Name)]
public class SelectIntoShapeProbeTest(ITestOutputHelper output) : TempDatabaseTest
{
    [Fact]
    public void Probe_make_table_shape()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LIBRED_SELECT_INTO") == "1",
            "set LIBRED_SELECT_INTO=1 — this probe needs ACE");

        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "selinto-probe-");
        try
        {
            using (var connection = AceTestDatabase.Open(path))
            {
                // Which column forms ACE's DDL actually accepts, one at a time — a setup that throws reports
                // only "Syntax error in field definition" with no clue which field, so ask per form.
                foreach ((string label, string sql) in ((string, string)[])
                [
                    ("long pk",      "CREATE TABLE SiT1 (Id LONG PRIMARY KEY)"),
                    ("text sized",   "CREATE TABLE SiT2 (Id LONG, Label TEXT(30))"),
                    ("text notnull", "CREATE TABLE SiT3 (Id LONG, Label TEXT(30) NOT NULL)"),
                    ("currency",     "CREATE TABLE SiT4 (Id LONG, Price CURRENCY)"),
                    ("datetime",     "CREATE TABLE SiT5 (Id LONG, Stamp DATETIME)"),
                    ("bit",          "CREATE TABLE SiT6 (Id LONG, Flag BIT)"),
                    ("memo",         "CREATE TABLE SiT7 (Id LONG, Note MEMO)"),
                ])
                {
                    try { Exec(connection, sql); output.WriteLine($"  DDL {label,-14} ACCEPTED"); }
                    catch (OleDbException e) { output.WriteLine($"  DDL {label,-14} rejected — {e.Message.Split('.')[0]}"); }
                }

                // A source with a primary key, an index, a sized text column and Required — so the report
                // below says which of those survive into the new table and which do not. Column names avoid
                // Access's reserved words: Name and When are both reserved.
                Exec(connection, "CREATE TABLE SiSrc (Id LONG PRIMARY KEY, Label TEXT(30), Qty LONG)");
                Exec(connection, "CREATE INDEX IX_SiSrc_Label ON SiSrc (Label)");
                Exec(connection, "INSERT INTO SiSrc (Id, Label, Qty) VALUES (1, 'one', 10)");
                Exec(connection, "INSERT INTO SiSrc (Id, Label, Qty) VALUES (2, 'two', 20)");

                foreach ((string label, string sql) in ((string, string)[])
                [
                    ("named columns",   "SELECT Id, Label, Qty INTO SiA FROM SiSrc"),
                    ("star",            "SELECT * INTO SiB FROM SiSrc"),
                    ("expression",      "SELECT Id, Qty * 2 AS Doubled, Label & '!' AS Shout INTO SiC FROM SiSrc"),
                    ("aggregate",       "SELECT COUNT(*) AS N, SUM(Qty) AS Total INTO SiD FROM SiSrc"),
                    ("filtered",        "SELECT Id, Label INTO SiE FROM SiSrc WHERE Id = 2"),
                    ("empty result",    "SELECT Id, Label INTO SiF FROM SiSrc WHERE Id > 99"),
                ])
                {
                    try { Exec(connection, sql); output.WriteLine($"  {label,-16} ACCEPTED  {sql}"); }
                    catch (OleDbException e) { output.WriteLine($"  {label,-16} rejected — {e.Message.Split('.')[0]}"); }
                }

                // The docs say a name collision is "a trappable error" — confirm, and see the message.
                try
                {
                    Exec(connection, "SELECT Id INTO SiA FROM SiSrc");
                    output.WriteLine("  existing target   ACCEPTED (no error)");
                }
                catch (OleDbException e) { output.WriteLine($"  existing target   rejected — {e.Message.Split('.')[0]}"); }
            }

            // Read the created tables back through LibRed, which shows the real column definitions.
            using var db = JetDatabase.Open(path);
            foreach (string table in (string[])["SiSrc", "SiA", "SiB", "SiC", "SiD", "SiE", "SiF"])
            {
                TableDef? def = db.Catalog.Tables.FirstOrDefault(t => t.Name == table);
                if (def is null) { output.WriteLine($"  {table}: not created"); continue; }

                output.WriteLine($"  {table}: " + string.Join(", ", def.Columns.Select(c =>
                    $"{c.Name} {c.Type}" +
                    (c.Length > 0 ? $"({c.Length})" : "") +
                    (c.IsNullable ? "" : " NOT NULL") +
                    (c.IsAutoNumber ? " COUNTER" : ""))));
                output.WriteLine($"      indexes: " +
                    (def.Indexes.Count == 0 ? "(none)" : string.Join(", ", def.Indexes.Select(i =>
                        $"{i.Name}{(i.IsPrimaryKey ? " PK" : "")}{(i.IsUnique ? " UNIQUE" : "")}"))));
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static void Exec(OleDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
