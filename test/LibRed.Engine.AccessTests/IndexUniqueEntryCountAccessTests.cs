using System.Data.OleDb;
using LibRed;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// An index's unique-entry count (+4 of its statistics block) advances on INSERT only, by one when the row brings a
/// key the index does not hold yet — collation-equal keys are one key, a Null is a key except in an IGNORE NULL
/// index, a key whose last row was deleted counts again, and an UPDATE never advances it
/// (docs/format/page-02d-constraints.md §3.3.1). The same statements through ACE and LibRed leave the same counts.
/// </summary>
[Collection(AceCollection.Name)]
public class IndexUniqueEntryCountAccessTests(ITestOutputHelper output)
{
    private static readonly string[] Setup =
    [
        "CREATE TABLE S (Id LONG CONSTRAINT pkS PRIMARY KEY, A LONG, T TEXT(10), N LONG)",
        "CREATE INDEX ixA ON S (A)",
        "CREATE INDEX ixT ON S (T)",
        "CREATE INDEX ixN ON S (N)",
        "CREATE INDEX ixAN ON S (A, N)",
        "CREATE INDEX ixNi ON S (N) WITH IGNORE NULL",
    ];

    private static readonly string[] Steps =
    [
        "INSERT INTO S VALUES (1, 5, 'a', NULL)",
        "INSERT INTO S VALUES (2, 5, 'A', NULL)",
        "INSERT INTO S VALUES (3, 6, 'b', 1)",
        "INSERT INTO S VALUES (4, 6, 'b', 1)",
        "DELETE FROM S WHERE Id = 3",
        "DELETE FROM S WHERE Id = 4",
        "INSERT INTO S VALUES (5, 6, 'b', 1)",
        "UPDATE S SET A = 7 WHERE Id = 1",
        "UPDATE S SET T = 'z' WHERE Id = 2",
    ];

    [Fact]
    public void Libred_counts_new_keys_as_ace_does()
    {
        string northwind = Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");
        string ace = TemporaryDatabase.CopyPath(northwind, "uniqcount-ace-");
        string libred = TemporaryDatabase.CopyPath(northwind, "uniqcount-lib-");
        try
        {
            foreach (string sql in Setup) { Ace(ace, sql); LibRed(libred, sql); }
            foreach (string sql in Steps)
            {
                Ace(ace, sql);
                LibRed(libred, sql);
                string aceCounts = Counts(ace), libredCounts = Counts(libred);
                output.WriteLine($"{sql,-40} ACE {aceCounts}   LibRed {libredCounts}");
                Assert.Equal(aceCounts, libredCounts);
            }
            Assert.Equal("ixA=3 ixAN=3 ixN=3 ixNi=2 ixT=3 pkS=5", Counts(libred));
        }
        finally
        {
            TemporaryDatabase.Delete(ace);
            TemporaryDatabase.Delete(libred);
        }
    }

    // A parent's primary key also carries the relationship's incoming logical index: one real index, so one count
    // per insert. The child's foreign-key index is non-unique, so a repeated parent id adds nothing.
    [Fact]
    public void A_real_index_shared_by_a_relationship_is_counted_once()
    {
        string northwind = Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");
        string ace = TemporaryDatabase.CopyPath(northwind, "uniqcount-rel-ace-");
        string libred = TemporaryDatabase.CopyPath(northwind, "uniqcount-rel-lib-");
        string[] statements =
        [
            "CREATE TABLE P (Id LONG CONSTRAINT pkP PRIMARY KEY)",
            "CREATE TABLE C (Id LONG, PId LONG CONSTRAINT fkCP REFERENCES P (Id))",
            "INSERT INTO P VALUES (1)", "INSERT INTO P VALUES (2)", "INSERT INTO P VALUES (3)",
            "INSERT INTO C VALUES (10, 1)", "INSERT INTO C VALUES (11, 1)", "INSERT INTO C VALUES (12, 2)",
        ];
        try
        {
            foreach (string sql in statements) { Ace(ace, sql); LibRed(libred, sql); }
            string aceCounts = Counts(ace, "P") + " / " + Counts(ace, "C");
            string libredCounts = Counts(libred, "P") + " / " + Counts(libred, "C");
            output.WriteLine($"ACE {aceCounts}   LibRed {libredCounts}");
            Assert.Equal(aceCounts, libredCounts);
        }
        finally
        {
            TemporaryDatabase.Delete(ace);
            TemporaryDatabase.Delete(libred);
        }
    }

    // Building an index over existing rows sets its total to the entries it holds and its unique count to its
    // distinct keys, from the rows present — here after deletions, so they differ from the cumulative counts the
    // table's other indexes keep; no other DDL touches those.
    [Theory]
    [InlineData("CREATE INDEX ixN ON S (N)")]
    [InlineData("CREATE INDEX ixNi ON S (N) WITH IGNORE NULL")]
    [InlineData("CREATE INDEX ixAN ON S (A, N)")]
    [InlineData("CREATE UNIQUE INDEX uxU ON S (U)")]
    [InlineData("ALTER TABLE S ADD CONSTRAINT fkSP FOREIGN KEY (W) REFERENCES P (Id)")]
    [InlineData("ALTER TABLE S ALTER COLUMN A DOUBLE")]
    [InlineData("ALTER TABLE S ALTER COLUMN U DOUBLE")]
    [InlineData("ALTER TABLE S ADD COLUMN Z LONG")]
    [InlineData("ALTER TABLE S DROP COLUMN U")]
    [InlineData("DROP INDEX ixA ON S")]
    public void An_index_built_over_existing_rows_is_counted_from_them(string sql)
    {
        string northwind = Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");
        string start = TemporaryDatabase.CopyPath(northwind, "uniqcount-ddl-start-");
        string ace = "", libred = "";
        try
        {
            foreach (string statement in (string[])
            [
                "CREATE TABLE P (Id LONG CONSTRAINT pkP PRIMARY KEY)",
                "INSERT INTO P VALUES (1)", "INSERT INTO P VALUES (2)", "INSERT INTO P VALUES (3)",
                "CREATE TABLE S (Id LONG CONSTRAINT pkS PRIMARY KEY, A LONG, T TEXT(10), N LONG, U LONG, W LONG)",
                "CREATE INDEX ixA ON S (A)", "CREATE INDEX ixT ON S (T)",
                "INSERT INTO S VALUES (1, 5, 'a', NULL, 10, 1)", "INSERT INTO S VALUES (2, 5, 'A', NULL, 20, 1)",
                "INSERT INTO S VALUES (3, 6, 'b', 1, 30, 2)", "INSERT INTO S VALUES (4, 6, 'b', 1, 40, 3)",
                "INSERT INTO S VALUES (5, 7, 'c', 2, 50, 3)", "INSERT INTO S VALUES (6, 8, 'd', NULL, 60, 2)",
                "DELETE FROM S WHERE Id = 5", "DELETE FROM S WHERE Id = 2",
            ])
                Ace(start, statement);

            ace = TemporaryDatabase.CopyPath(start, "uniqcount-ddl-ace-");
            libred = TemporaryDatabase.CopyPath(start, "uniqcount-ddl-lib-");
            Ace(ace, sql);
            LibRed(libred, sql);
            string aceStats = Statistics(ace), libredStats = Statistics(libred);
            output.WriteLine($"{sql}\n  ACE    {aceStats}\n  LibRed {libredStats}");
            Assert.Equal(aceStats, libredStats);
        }
        finally
        {
            TemporaryDatabase.Delete(start);
            if (ace.Length > 0) TemporaryDatabase.Delete(ace);
            if (libred.Length > 0) TemporaryDatabase.Delete(libred);
        }
    }

    // A retype to or from Memo/OLE, which LibRed does by rebuilding the whole table, leaves the counts as ACE's ALTER
    // does: only an index over the changed column is rebuilt (a primary key included), every other index here keeps
    // its cumulative counts, and so does the foreign-key index of a table referencing this one.
    [Theory]
    [InlineData("ALTER TABLE S ALTER COLUMN U MEMO")]
    [InlineData("ALTER TABLE S ALTER COLUMN T MEMO")]
    [InlineData("ALTER TABLE S ALTER COLUMN M TEXT(20)")]
    [InlineData("ALTER TABLE Q ALTER COLUMN Code MEMO")]
    public void A_memo_or_ole_retype_recounts_only_the_index_over_the_column(string sql)
    {
        string northwind = Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");
        string start = TemporaryDatabase.CopyPath(northwind, "uniqcount-memo-start-");
        string ace = "", libred = "";
        try
        {
            foreach (string statement in (string[])
            [
                "CREATE TABLE S (Id LONG CONSTRAINT pkS PRIMARY KEY, A LONG, T TEXT(10), U LONG, M MEMO)",
                "CREATE INDEX ixA ON S (A)", "CREATE INDEX ixT ON S (T)", "CREATE INDEX ixM ON S (M)",
                "INSERT INTO S VALUES (1, 5, 'a', 10, 'm1')", "INSERT INTO S VALUES (2, 5, 'A', 20, 'm1')",
                "INSERT INTO S VALUES (3, 6, 'b', 30, 'm2')", "INSERT INTO S VALUES (4, 6, 'b', 40, 'm3')",
                "INSERT INTO S VALUES (5, 7, 'c', 50, 'm3')", "INSERT INTO S VALUES (6, 8, 'd', 60, 'm4')",
                "CREATE TABLE K (Id LONG CONSTRAINT pkK PRIMARY KEY, SId LONG CONSTRAINT fkKS REFERENCES S (Id))",
                "INSERT INTO K VALUES (1, 1)", "INSERT INTO K VALUES (2, 1)", "INSERT INTO K VALUES (3, 3)",
                "INSERT INTO K VALUES (4, 4)", "INSERT INTO K VALUES (5, 6)",
                "CREATE TABLE Q (Code TEXT(10) CONSTRAINT pkQ PRIMARY KEY, V LONG)",
                "INSERT INTO Q VALUES ('x', 1)", "INSERT INTO Q VALUES ('y', 2)",
                "DELETE FROM S WHERE Id = 5", "DELETE FROM K WHERE Id = 2", "DELETE FROM Q WHERE Code = 'x'",
            ])
                Ace(start, statement);

            ace = TemporaryDatabase.CopyPath(start, "uniqcount-memo-ace-");
            libred = TemporaryDatabase.CopyPath(start, "uniqcount-memo-lib-");
            Ace(ace, sql);
            LibRed(libred, sql);
            string aceStats = Statistics(ace, "S", "K", "Q"), libredStats = Statistics(libred, "S", "K", "Q");
            output.WriteLine($"{sql}\n  ACE    {aceStats}\n  LibRed {libredStats}");
            Assert.Equal(aceStats, libredStats);
        }
        finally
        {
            TemporaryDatabase.Delete(start);
            if (ace.Length > 0) TemporaryDatabase.Delete(ace);
            if (libred.Length > 0) TemporaryDatabase.Delete(libred);
        }
    }

    /// <summary>Each of the tables' real indexes as table.name=total/unique, read from its statistics block.</summary>
    private static string Statistics(string path, params string[] tables)
    {
        if (tables.Length == 0) tables = ["S"];
        using var db = JetDatabase.Open(path);
        byte[] file = File.ReadAllBytes(path);
        return string.Join(" ", tables.SelectMany(name =>
        {
            var table = db.Catalog.FindTable(name)!;
            int block = table.DefinitionPage * 4096 + 0x3F;
            return table.Indexes.GroupBy(i => i.RealIndexOrdinal).Select(g => g.First())
                .OrderBy(i => i.Name, StringComparer.Ordinal)
                .Select(i => $"{name}.{i.Name}={BitConverter.ToInt32(file, block + i.RealIndexOrdinal * 12)}"
                    + $"/{BitConverter.ToInt32(file, block + i.RealIndexOrdinal * 12 + 4)}");
        }));
    }

    private static void Ace(string path, string sql)
    {
        using OleDbConnection connection = AceTestDatabase.Open(path);
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void LibRed(string path, string sql)
    {
        using var db = JetDatabase.Open(path, readOnly: false);
        new QueryEngine(db).ExecuteNonQuery(sql);
    }

    private static string Counts(string path, string table = "S")
    {
        using var db = JetDatabase.Open(path);
        return string.Join(" ", db.Catalog.FindTable(table)!.Indexes.OrderBy(i => i.Name, StringComparer.Ordinal)
            .Select(i => $"{i.Name}={i.UniqueEntryCount}"));
    }
}
