using System.Data.OleDb;
using System.Reflection;
using System.Text;
using LibRed.Formats;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Engine.Tests;

// PROBE: what becomes of the pages a rolled-back transaction allocated — after the rollback, when the same work is
// then done committed, and at close — for ACE, with LibRed's end state beside it.
[Collection(AceCollection.Name)]
public class RollbackAllocationProbeTest(ITestOutputHelper output)
{
    // A nested transaction rolled back inside a committed one, and an INSERT that writes 24 rows and then fails on a
    // duplicate key — inside a transaction and on its own — each followed by the same inserts succeeding.
    [Theory]
    [InlineData("nested")]
    [InlineData("failed in a transaction")]
    [InlineData("failed on its own")]
    [InlineData("first page")]
    [InlineData("refused first")]
    [InlineData("refused first rolled back")]
    [InlineData("good first rolled back")]
    [InlineData("check first")]
    [InlineData("required first")]
    public void Probe_pages_of_a_rolled_back_statement(string scenario)
    {
        string[] setup =
        [
            "CREATE TABLE Digit (D LONG)",
            .. Enumerable.Range(0, 10).Select(d => $"INSERT INTO Digit (D) VALUES ({d})"),
            "CREATE TABLE B (Id LONG CONSTRAINT pkB PRIMARY KEY, T TEXT(255))",
            "INSERT INTO B (Id, T) VALUES (76, 'first')",
        ];
        const string Rows = "INSERT INTO B (Id, T) SELECT {0} - (a.D * 10 + b.D), String(250, 'r') FROM Digit a, Digit b " +
            "WHERE a.D * 10 + b.D <= 24 ORDER BY 1 DESC";
        string failing = string.Format(System.Globalization.CultureInfo.InvariantCulture, Rows, 100);
        string good = string.Format(System.Globalization.CultureInfo.InvariantCulture, Rows, 200);
        string[] steps = scenario switch
        {
            "nested" => ["BEGIN TRANSACTION", "BEGIN TRANSACTION", good, "ROLLBACK TRANSACTION",
                good.Replace("200 -", "300 -", StringComparison.Ordinal), "COMMIT TRANSACTION"],
            "failed in a transaction" => ["BEGIN TRANSACTION", failing, good, "COMMIT TRANSACTION"],
            // An empty table's first rows rolled back, then inserted again; then the same for a table with rows.
            "first page" => ["CREATE TABLE E (Id LONG, X TEXT(50))", "BEGIN TRANSACTION", "INSERT INTO E (Id, X) VALUES (1, 'a')",
                "INSERT INTO E (Id, X) VALUES (2, 'b')", "ROLLBACK TRANSACTION", "INSERT INTO E (Id, X) VALUES (3, 'c')",
                "BEGIN TRANSACTION", good, "ROLLBACK TRANSACTION", good],
            // An empty child table's first insert refused for a missing parent, then one that succeeds — on their
            // own, and rolled back in a transaction then run again.
            "refused first" => [.. Orphans, "INSERT INTO O (Id, Cid) VALUES (1003, 4)", "INSERT INTO O (Id, Cid) VALUES (1004, 5)"],
            "refused first rolled back" => [.. Orphans, "BEGIN TRANSACTION", "INSERT INTO O (Id, Cid) VALUES (1003, 4)",
                "INSERT INTO O (Id, Cid) VALUES (1004, 5)", "ROLLBACK TRANSACTION", "INSERT INTO O (Id, Cid) VALUES (1003, 4)",
                "INSERT INTO O (Id, Cid) VALUES (1004, 5)"],
            "good first rolled back" => [.. Orphans, "BEGIN TRANSACTION", "INSERT INTO O (Id, Cid) VALUES (1004, 5)",
                "ROLLBACK TRANSACTION", "INSERT INTO O (Id, Cid) VALUES (1004, 5)"],
            // An empty table's first insert refused by a CHECK, then by a required column, each followed by one that
            // succeeds: whether either refusal takes a page first.
            "check first" => ["CREATE TABLE K (Id LONG CONSTRAINT pkK PRIMARY KEY, V LONG, CONSTRAINT ckK CHECK (V >= 0))",
                "INSERT INTO K (Id, V) VALUES (1, -1)", "INSERT INTO K (Id, V) VALUES (2, 1)"],
            "required first" => ["CREATE TABLE Q (Id LONG CONSTRAINT pkQ PRIMARY KEY, V LONG NOT NULL)",
                "INSERT INTO Q (Id, V) VALUES (1, NULL)", "INSERT INTO Q (Id, V) VALUES (2, 1)"],
            _ => [failing, good],
        };

        object? engine = AceTestDatabase.CreateDaoEngine();
        Assert.SkipWhen(engine is null, "DAO is not registered in this bitness.");
        string origin = TemporaryDatabase.CreatePath("rollback-statement-");
        string ace = "", libred = "";
        try
        {
            object workspace = Invoke(engine!, "CreateWorkspace", "", "admin", "", 2)!;
            Invoke(Invoke(workspace, "CreateDatabase", origin, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128)!, "Close");
            AceTestDatabase.ReleaseAbandonedComObjects();
            ace = TemporaryDatabase.CopyPath(origin, "rollback-statement-ace-");
            libred = TemporaryDatabase.CopyPath(origin, "rollback-statement-libred-");
            using (OleDbConnection c = AceTestDatabase.Open(ace))
            {
                foreach (string sql in setup) Exec(c, sql);
                Live(ace, "ACE before");
                foreach (string sql in steps)
                {
                    try { Exec(c, sql); }
                    catch (OleDbException e) { output.WriteLine($"ACE refused {sql[..20]}…: {e.Message}"); }
                }
                Live(ace, "ACE after");
            }
            OleDbConnection.ReleaseObjectPool();
            output.WriteLine($"ACE closed: {Describe(ace)}");

            using (var database = JetDatabase.Open(libred, readOnly: false))
            {
                var engineL = new QueryEngine(database);
                foreach (string sql in setup) engineL.ExecuteNonQuery(sql);
                foreach (string sql in steps)
                {
                    try { engineL.ExecuteNonQuery(sql); }
#pragma warning disable CA1031 // the probe records the refusal and carries on, as ACE's side does
                    catch (Exception e) { output.WriteLine($"LibRed refused {sql[..20]}…: {e.Message}"); }
#pragma warning restore CA1031
                }
            }
            output.WriteLine($"LibRed closed: {Describe(libred)}");
        }
        finally
        {
            TemporaryDatabase.Delete(origin);
            if (ace != "") TemporaryDatabase.Delete(ace);
            if (libred != "") TemporaryDatabase.Delete(libred);
        }
    }

    // A leaf full of one duplicated key, compressed to a byte or two an entry, given a smaller key: how ACE splits it
    // when the new entry is its first.
    [Fact]
    public void Probe_a_new_first_key_on_a_leaf_of_duplicates()
    {
        object? engine = AceTestDatabase.CreateDaoEngine();
        Assert.SkipWhen(engine is null, "DAO is not registered in this bitness.");
        string path = TemporaryDatabase.CreatePath("dup-first-");
        try
        {
            object workspace = Invoke(engine!, "CreateWorkspace", "", "admin", "", 2)!;
            Invoke(Invoke(workspace, "CreateDatabase", path, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128)!, "Close");
            AceTestDatabase.ReleaseAbandonedComObjects();
            using (OleDbConnection c = AceTestDatabase.Open(path))
            {
                foreach (string sql in new[] { "CREATE TABLE Digit (D INTEGER)" }
                    .Concat(Enumerable.Range(0, 10).Select(d => $"INSERT INTO Digit (D) VALUES ({d})"))
                    .Concat([
                        "CREATE TABLE T (Id LONG, G LONG)", "CREATE INDEX ixG ON T (G)",
                        "INSERT INTO T (Id, G) SELECT a.D * 100 + b.D * 10 + c.D, 2 FROM Digit a, Digit b, Digit c",
                        "INSERT INTO T (Id, G) SELECT 1000 + a.D * 100 + b.D * 10 + c.D, 2 FROM Digit a, Digit b, Digit c",
                    ]))
                    Exec(c, sql);
            }
            OleDbConnection.ReleaseObjectPool();
            output.WriteLine($"before: {Leaves(path)}");
            using (OleDbConnection c = AceTestDatabase.Open(path)) Exec(c, "INSERT INTO T (Id, G) VALUES (5000, 1)");
            OleDbConnection.ReleaseObjectPool();
            output.WriteLine($"after: {Leaves(path)}");
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>Each page of table T's index ixG: its number, type, entry count, stored prefix, the first entry's
    /// stored length, free space and siblings.</summary>
    private static string Leaves(string path)
    {
        using var database = JetDatabase.Open(path, readOnly: true);
        Table table = database.OpenTable("T");
        Catalog.IndexDef index = table.Definition.Indexes.Single(i => i.Name == "ixG");
        var text = new StringBuilder();
        foreach (int page in table.UsageMap.PagesInMap(index.UsageMap.Row, index.UsageMap.Page))
        {
            IndexTree.CheckedPage checkedPage = IndexTree.Read(table.Channel, page, table.Definition.DefinitionPage);
            (int start, int end) first = checkedPage.EntryRanges.Count > 0 ? checkedPage.EntryRanges[0] : (0, 0);
            text.Append($"\n  page {page} {checkedPage.Type}: {checkedPage.EntryRanges.Count} entries, prefix {checkedPage.CompressedByteCount}, " +
                $"first stored {first.end - first.start} bytes, free {IndexTree.ReadFreeSpace(checkedPage.Buffer.Span, database.Format)}, " +
                $"prev {checkedPage.Previous} next {checkedPage.Next}; first key {Convert.ToHexString(IndexTree.DecodeEntries(checkedPage).First().Key)}");
        }
        return text.ToString();
    }

    // A parent with one row, and an empty table referencing it.
    private static readonly string[] Orphans =
    [
        "CREATE TABLE C (Id LONG CONSTRAINT pkC PRIMARY KEY)",
        "INSERT INTO C (Id) VALUES (5)",
        "CREATE TABLE O (Id LONG CONSTRAINT pkO PRIMARY KEY, Cid LONG CONSTRAINT fkO REFERENCES C (Id))",
    ];

    [Theory]
    [InlineData("create")]
    [InlineData("insert")]
    public void Probe_pages_of_a_rolled_back_transaction(string scenario)
    {
        string[] setup = scenario == "create" ? [] :
        [
            "CREATE TABLE B (Id LONG, T TEXT(255))",
            "INSERT INTO B (Id, T) VALUES (0, 'first')",
        ];
        string[] work = scenario == "create"
            ? ["CREATE TABLE A (Id LONG CONSTRAINT pkA PRIMARY KEY, T TEXT(50))", "CREATE INDEX ixAT ON A (T)"]
            : [.. Enumerable.Range(1, 24).Select(i => $"INSERT INTO B (Id, T) VALUES ({i}, String(250, 'r'))")];

        object? engine = AceTestDatabase.CreateDaoEngine();
        Assert.SkipWhen(engine is null, "DAO is not registered in this bitness.");
        string origin = TemporaryDatabase.CreatePath("rollback-pages-");
        string libred = "";
        try
        {
            object workspace = Invoke(engine!, "CreateWorkspace", "", "admin", "", 2)!;
            Invoke(Invoke(workspace, "CreateDatabase", origin, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128)!, "Close");
            AceTestDatabase.ReleaseAbandonedComObjects();
            libred = TemporaryDatabase.CopyPath(origin, "rollback-pages-libred-");
            string ace = TemporaryDatabase.CopyPath(origin, "rollback-pages-ace-");
            try
            {
                using (OleDbConnection c = AceTestDatabase.Open(ace))
                {
                    foreach (string sql in setup) Exec(c, sql);
                    Live(ace, "ACE before");
                    Exec(c, "BEGIN TRANSACTION");
                    foreach (string sql in work) Exec(c, sql);
                    Live(ace, "ACE inside the transaction");
                    Exec(c, "ROLLBACK TRANSACTION");
                    Live(ace, "ACE after the rollback");
                    foreach (string sql in work) Exec(c, sql.Replace("TABLE A ", "TABLE A2 ").Replace("ON A ", "ON A2 ").Replace("pkA ", "pkA2 ").Replace("ixAT ", "ixA2T "));
                    Live(ace, "ACE after the work committed");
                }
                OleDbConnection.ReleaseObjectPool();
                output.WriteLine($"ACE closed: {Describe(ace)}");

                using (var database = JetDatabase.Open(libred, readOnly: false))
                {
                    var engineL = new QueryEngine(database);
                    foreach (string sql in setup) engineL.ExecuteNonQuery(sql);
                    engineL.ExecuteNonQuery("BEGIN TRANSACTION");
                    foreach (string sql in work) engineL.ExecuteNonQuery(sql);
                    engineL.ExecuteNonQuery("ROLLBACK TRANSACTION");
                    foreach (string sql in work) engineL.ExecuteNonQuery(sql.Replace("TABLE A ", "TABLE A2 ").Replace("ON A ", "ON A2 ").Replace("pkA ", "pkA2 ").Replace("ixAT ", "ixA2T "));
                }
                output.WriteLine($"LibRed closed: {Describe(libred)}");
            }
            finally { TemporaryDatabase.Delete(ace); }
        }
        finally
        {
            TemporaryDatabase.Delete(origin);
            if (libred != "") TemporaryDatabase.Delete(libred);
        }
    }

    // ACE writes lazily; a pause lets it reach the file before the copy is read.
    private void Live(string path, string label)
    {
        Thread.Sleep(3000);
        string snapshot = TemporaryDatabase.CopyPath(path, "rollback-pages-live-");
        try { output.WriteLine($"{label}: {Describe(snapshot)}"); }
        finally { TemporaryDatabase.Delete(snapshot); }
    }

    /// <summary>The file's length, then every page from <paramref name="from"/> (38 by default) up to
    /// <paramref name="to"/>: its type and owner, '*' where the global free map marks it free, and the free pages past
    /// the end of the file.</summary>
    internal static string Describe(string path, int from = 38, int to = int.MaxValue)
    {
        byte[] file = File.ReadAllBytes(path);
        using var database = JetDatabase.Open(path, readOnly: true);
        JetFormatBase format = database.Format;
        int pageSize = format.PageSize, pages = file.Length / pageSize;
        var owners = database.Catalog.Tables.ToDictionary(t => t.DefinitionPage, t => t.Name);
        HashSet<int> free = PageGroupProbeTest.GlobalFree(database.OpenTable("MSysObjects"), pages + 64);
        var text = new StringBuilder($"{pages} pages;");
        for (int page = from; page < Math.Min(pages, to); page++)
        {
            ReadOnlySpan<byte> p = file.AsSpan(page * pageSize, pageSize);
            string owner = PageHeader.ReadType(p) switch
            {
                PageType.DataPage => (int)DataPage.ReadOwner(p, format) is var o && owners.TryGetValue(o, out string? n) ? n : $"t{DataPage.ReadOwner(p, format)}",
                PageType.LeafIndexPage or PageType.IntermediateIndexPage => owners.TryGetValue(IndexTree.ReadOwner(p, format), out string? n) ? n : "?",
                PageType.TableDefinition => owners.TryGetValue(page, out string? n) ? n : "?",
                _ => "",
            };
            text.Append($" {page}:{p[0]:X2}{owner}{(free.Contains(page) ? "*" : "")}");
        }
        text.Append($"; free past the end [{string.Join(",", free.Where(p => p >= pages).Order())}]");
        return text.ToString();
    }

    private static void Exec(OleDbConnection connection, string sql)
    {
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static object? Invoke(object target, string member, params object?[] args) =>
        target.GetType().InvokeMember(member, BindingFlags.InvokeMethod, null, target, args);
}
