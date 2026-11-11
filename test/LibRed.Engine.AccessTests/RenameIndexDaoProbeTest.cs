using System.Data.OleDb;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// Probe: whether DAO can rename an existing index by setting <c>Index.Name</c> — a plain index, a primary key, and
/// the index a relationship created — and what that changes on disk. Reports; asserts nothing. ACE's SQL has no
/// statement for it, so DAO is the only engine route there could be.
/// </summary>
/// <remarks>
/// Each case renames on its own copy of one database, then reopens the copy through a fresh DAO engine and through
/// LibRed to list the table's indexes, and lists the pages that differ from the untouched copy. Both files are kept
/// under <c>%TEMP%\libred-rename-index-probe</c> for a byte diff.
/// </remarks>
[Collection(AceCollection.Name)]
public class RenameIndexDaoProbeTest(ITestOutputHelper output)
{
    private static readonly string KeepDirectory = Path.Combine(Path.GetTempPath(), "libred-rename-index-probe");

    [Theory(Explicit = true)]
    [InlineData("Child", "IX_Note", "IX_Note_Renamed")]
    [InlineData("Child", "IX_Note", "AA_Note")]             // moves ahead of FK_Child_Parent in name order
    [InlineData("Child", "IX_Note", "ix_note")]             // case only
    [InlineData("Child", "IX_Note", "PK_Child")]            // onto another index's name
    [InlineData("Parent", "PK_Parent", "PK_Parent_Renamed")]
    [InlineData("Parent", "PK_Parent", "A")]                // the parent also holds the relationship's hidden block
    [InlineData("Child", "FK_Child_Parent", "FK_Child_Parent_Renamed")]
    [InlineData("Wide", "IX_Wide", "IX_Wide_Renamed_To_A_Much_Longer_Name")] // a multi-page definition
    public void Dao_renames_an_index(string table, string index, string newName)
    {
        Assert.SkipWhen(AceTestDatabase.CreateDaoEngine() is null, "DAO is not registered in this process.");
        Directory.CreateDirectory(KeepDirectory);

        string source = CreateProbeDatabase();
        string before = Path.Combine(KeepDirectory, $"{index}-{newName}-before.accdb");
        string after = Path.Combine(KeepDirectory, $"{index}-{newName}-after.accdb");
        try
        {
            File.Copy(source, before, overwrite: true);
            File.Copy(source, after, overwrite: true);

            output.WriteLine($"Before: {string.Join(", ", DaoIndexNames(before, table))}");

            string outcome = Outcome(() =>
            {
                dynamic engine = AceTestDatabase.CreateDaoEngine()!;
                dynamic database = engine.OpenDatabase(after);
                try
                {
                    database.TableDefs[table].Indexes[index].Name = newName;
                }
                finally { database.Close(); }
            });
            output.WriteLine($"DAO rename {table}.{index} -> {newName}: {outcome}");

            output.WriteLine($"After (DAO):    {string.Join(", ", DaoIndexNames(after, table))}");
            string libred = "";
            string libredOutcome = Outcome(() => libred = LibRedIndexNames(after, table));
            output.WriteLine($"After (LibRed): {(libredOutcome == "accepted" ? libred : libredOutcome)}");
            output.WriteLine($"Relationships (DAO): {string.Join(", ", DaoRelations(after))}");
            output.WriteLine($"Pages differing from before: {string.Join(", ", DifferingPages(before, after))}");
            output.WriteLine($"Kept: {before} | {after}");
        }
        finally
        {
            AceTestDatabase.ReleaseAbandonedComObjects();
            TemporaryDatabase.Delete(source);
        }
    }

    private static string CreateProbeDatabase()
    {
        string path = TemporaryDatabase.CreatePath("rename-index-");
        JetDatabase.Create(path);
        using (OleDbConnection connection = AceTestDatabase.Open(path))
            foreach (string sql in (string[])
                     [
                         "CREATE TABLE Parent (Id LONG, CONSTRAINT PK_Parent PRIMARY KEY (Id))",
                         "CREATE TABLE Child (Id LONG, ParentId LONG, Label TEXT(10), CONSTRAINT PK_Child PRIMARY KEY (Id), " +
                         "CONSTRAINT FK_Child_Parent FOREIGN KEY (ParentId) REFERENCES Parent (Id))",
                         "CREATE INDEX IX_Note ON Child (Label)",
                         "INSERT INTO Parent VALUES (1)",
                         "INSERT INTO Child VALUES (1, 1, 'a')",
                         $"CREATE TABLE Wide (Id LONG{string.Concat(Enumerable.Range(1, 254).Select(i => $", c{i:D3} CURRENCY"))})",
                         "CREATE INDEX IX_Wide ON Wide (c001)",
                     ])
            {
                using OleDbCommand command = connection.CreateCommand();
                command.CommandText = sql;
                try { command.ExecuteNonQuery(); }
                catch (OleDbException e) { throw new InvalidOperationException($"ACE refused setup: {sql}", e); }
            }
        OleDbConnection.ReleaseObjectPool();
        return path;
    }

    private static List<string> DaoIndexNames(string path, string table)
    {
        var names = new List<string>();
        dynamic engine = AceTestDatabase.CreateDaoEngine()!;
        dynamic database = engine.OpenDatabase(path, false, true);
        try
        {
            dynamic indexes = database.TableDefs[table].Indexes;
            for (int i = 0; i < indexes.Count; i++)
            {
                dynamic ix = indexes[i];
                names.Add($"{ix.Name}{(ix.Primary ? " [PK]" : "")}{(ix.Foreign ? " [FK]" : "")}{(ix.Unique ? " [U]" : "")}");
            }
        }
        finally { database.Close(); }
        return names;
    }

    private static List<string> DaoRelations(string path)
    {
        var names = new List<string>();
        dynamic engine = AceTestDatabase.CreateDaoEngine()!;
        dynamic database = engine.OpenDatabase(path, false, true);
        try
        {
            dynamic relations = database.Relations;
            for (int i = 0; i < relations.Count; i++)
            {
                dynamic relation = relations[i];
                names.Add($"{relation.Name} ({relation.ForeignTable} -> {relation.Table})");
            }
        }
        finally { database.Close(); }
        return names;
    }

    private static string LibRedIndexNames(string path, string table)
    {
        using var db = JetDatabase.Open(path, readOnly: true);
        TableDefinition definition = db.Catalog.FindTable(table)
            ?? throw new InvalidOperationException($"LibRed cannot find {table}.");
        TableDefinition msysObjects = db.Catalog.FindTable("MSysObjects")
            ?? throw new InvalidOperationException("LibRed cannot find MSysObjects.");
        return $"{string.Join(", ", definition.Indexes.Select(i => i.Name))} " +
               $"(TDEF page {definition.DefinitionPage}; MSysObjects TDEF page {msysObjects.DefinitionPage})";
    }

    private static List<int> DifferingPages(string before, string after)
    {
        byte[] a = File.ReadAllBytes(before);
        byte[] b = File.ReadAllBytes(after);
        const int pageSize = 4096;
        var pages = new List<int>();
        int count = Math.Max(a.Length, b.Length) / pageSize;
        for (int page = 0; page < count; page++)
        {
            ReadOnlySpan<byte> pa = page * pageSize < a.Length ? a.AsSpan(page * pageSize, pageSize) : default;
            ReadOnlySpan<byte> pb = page * pageSize < b.Length ? b.AsSpan(page * pageSize, pageSize) : default;
            if (!pa.SequenceEqual(pb)) pages.Add(page);
        }
        return pages;
    }

    private static string Outcome(Action run)
    {
        try
        {
            run();
            return "accepted";
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException
                                      or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            return $"{e.GetType().Name}: {e.Message}";
        }
    }
}
