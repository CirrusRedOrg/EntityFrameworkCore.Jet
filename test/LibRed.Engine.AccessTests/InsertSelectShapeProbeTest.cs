using System.Data.OleDb;
using Xunit;

namespace LibRed.Engine.Tests;

// PROBE: how ACE resolves an append's source columns to the target's when no column list is given.
//
// LibRed was written to map POSITIONALLY, reasoning from "the names need not match, only the count". ACE
// rejected that with "unknown field name: 'A'" — quoting a SOURCE column name as though it had looked for it
// in the TARGET — which says it resolves by NAME. That is worth pinning down exactly rather than inferring
// from one error message, because the difference is silent: positional mapping into a table whose columns
// happen to be type-compatible puts values in the wrong columns and reports success.
[Collection(AceCollection.Name)]
public class InsertSelectShapeProbeTest(ITestOutputHelper output) : TempDatabaseTest
{
    [Fact]
    public void Probe_source_to_target_column_resolution()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("LIBRED_INSERT_SELECT") == "1",
            "set LIBRED_INSERT_SELECT=1 — this probe needs ACE");

        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "insel-probe-");
        try
        {
            using var connection = AceTestDatabase.Open(path);
            Exec(connection, "CREATE TABLE PSrc (A LONG, B TEXT(50))");
            Exec(connection, "CREATE TABLE PDst (Id LONG, Name TEXT(50))");
            Exec(connection, "CREATE TABLE PSame (A LONG, B TEXT(50))");
            Exec(connection, "INSERT INTO PSrc (A, B) VALUES (7, 'seven')");

            // Each of these answers a different question about the no-column-list form.
            foreach ((string label, string sql) in ((string, string)[])
            [
                ("names differ",            "INSERT INTO PDst SELECT A, B FROM PSrc"),
                ("aliased to target names", "INSERT INTO PDst SELECT A AS Id, B AS Name FROM PSrc"),
                ("names match",             "INSERT INTO PSame SELECT A, B FROM PSrc"),
                ("star, names match",       "INSERT INTO PSame SELECT * FROM PSrc"),
                ("star, names differ",      "INSERT INTO PDst SELECT * FROM PSrc"),
                ("explicit list, differing","INSERT INTO PDst (Id, Name) SELECT A, B FROM PSrc"),
                // Reversed aliases: if resolution is by NAME this lands B in Id and A in Name, which is the
                // case that shows positional and name-based mapping actually disagree rather than coincide.
                ("aliases reversed",        "INSERT INTO PDst SELECT B AS Name, A AS Id FROM PSrc"),
            ])
            {
                try
                {
                    Exec(connection, sql);
                    output.WriteLine($"  {label,-26} ACCEPTED   {sql}");
                }
                catch (OleDbException e)
                {
                    output.WriteLine($"  {label,-26} rejected — {e.Message.Split('.')[0]}");
                }
            }

            output.WriteLine("");
            foreach (string table in (string[])["PDst", "PSame"])
            {
                using var command = connection.CreateCommand();
                command.CommandText = $"SELECT * FROM {table}";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    output.WriteLine($"  {table}: " + string.Join(" | ",
                        Enumerable.Range(0, reader.FieldCount)
                            .Select(i => $"{reader.GetName(i)}={reader.GetValue(i)}")));
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
