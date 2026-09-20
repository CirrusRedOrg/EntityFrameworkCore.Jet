using System.Data.OleDb;
using LibRed;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// An OLE column cannot be in an index — a key, a unique constraint, a relationship's — and ACE refuses every route
/// that would put one there, up front, with "Invalid field definition '…' in definition of index or relationship.",
/// leaving nothing behind (docs/format/page-03-04-index-btree.md). LibRed refuses the same statements at the same
/// point with the same message; it used to accept them on an empty table, after which every insert failed.
/// </summary>
[Collection(AceCollection.Name)]
public class OleIndexRefusalAccessTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("CREATE TABLE X (Id LONG, O LONGBINARY)", "CREATE INDEX ixO ON X (O)")]
    [InlineData("CREATE TABLE X (Id LONG, O LONGBINARY CONSTRAINT pkX PRIMARY KEY)")]
    [InlineData("CREATE TABLE X (Id LONG, O LONGBINARY CONSTRAINT uxO UNIQUE)")]
    [InlineData("CREATE TABLE X (Id LONG, O LONGBINARY)", "ALTER TABLE X ADD CONSTRAINT pkX PRIMARY KEY (O)")]
    [InlineData("CREATE TABLE X (Id LONG, O LONGBINARY)", "ALTER TABLE X ADD CONSTRAINT uxO UNIQUE (O)")]
    [InlineData("CREATE TABLE X (Id LONG, A LONG)", "CREATE INDEX ixA ON X (A)", "ALTER TABLE X ALTER COLUMN A LONGBINARY")]
    [InlineData("CREATE TABLE X (Id LONG, A LONG)", "CREATE INDEX ixA ON X (A)", "INSERT INTO X VALUES (1, 5)", "ALTER TABLE X ALTER COLUMN A LONGBINARY")]
    // Refused as OLE before the relationship's type match would refuse it.
    [InlineData("CREATE TABLE P (Id LONG CONSTRAINT pkP PRIMARY KEY)", "CREATE TABLE X (Id LONG, O LONGBINARY)", "ALTER TABLE X ADD CONSTRAINT fkXP FOREIGN KEY (O) REFERENCES P (Id)")]
    [InlineData("CREATE TABLE P (Id LONG CONSTRAINT pkP PRIMARY KEY)", "CREATE TABLE X (Id LONG, O LONGBINARY CONSTRAINT fkXP REFERENCES P (Id))")]
    public void Libred_refuses_an_ole_column_in_an_index_as_ace_does(params string[] steps)
    {
        string northwind = Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");
        string ace = TemporaryDatabase.CopyPath(northwind, "oleix-ace-");
        string libred = TemporaryDatabase.CopyPath(northwind, "oleix-lib-");
        try
        {
            string aceOutcome = Run(steps, sql =>
            {
                using OleDbConnection connection = AceTestDatabase.Open(ace);
                using OleDbCommand command = connection.CreateCommand();
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }) + "; " + Left(ace);
            string libredOutcome = Run(steps, sql =>
            {
                using var db = JetDatabase.Open(libred, readOnly: false);
                new QueryEngine(db).ExecuteNonQuery(sql);
            }) + "; " + Left(libred);

            output.WriteLine($"ACE    {aceOutcome}\nLibRed {libredOutcome}");
            Assert.Contains("Invalid field definition", aceOutcome);
            Assert.Equal(aceOutcome, libredOutcome);
        }
        finally
        {
            TemporaryDatabase.Delete(ace);
            TemporaryDatabase.Delete(libred);
        }
    }

    /// <summary>Runs the steps until one is refused: which step, and the refusal's message.</summary>
    private static string Run(string[] steps, Action<string> execute)
    {
        for (int step = 0; step < steps.Length; step++)
        {
            try { execute(steps[step]); }
            catch (Exception e) { return $"refused at step {step + 1}: {e.GetBaseException().Message}"; }
        }
        return "accepted";
    }

    /// <summary>What the statements left: table X's columns and indexes, or its absence.</summary>
    private static string Left(string path)
    {
        using var db = JetDatabase.Open(path);
        var table = db.Catalog.FindTable("X");
        return table is null ? "no table X"
            : $"X [{string.Join(",", table.Columns.Select(c => $"{c.Name}:{c.Type}"))}] indexes [{string.Join(",", table.Indexes.Select(i => i.Name).Order(StringComparer.Ordinal))}]";
    }
}
