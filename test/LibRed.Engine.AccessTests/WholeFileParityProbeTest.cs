using System.Buffers.Binary;
using System.Data.OleDb;
using System.Globalization;
using System.Reflection;
using System.Text;
using LibRed;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// A statement corpus run through ACE over OLE DB and through LibRed on copies of one ACE-created empty database,
/// and the files compared byte for byte.
/// </summary>
/// <remarks>
/// Two measurements. The whole run applies every statement in one session per engine and compares the two files at
/// the end — how close LibRed comes over a realistic workload; it reports, since one early placement difference
/// leaves every later page misaligned. Step by step applies one statement at a time, both engines starting from the
/// same file — ACE's result of the step before — so each statement is compared on its own, and it asserts that
/// every statement leaves identical files.
/// <para>Only what differs for reasons other than the format is masked: page 0's user commit-byte table, which
/// counts a lock-file user's writes and LibRed does not keep; the clock values stamped during the run, wherever they
/// lie, dead bytes included — the wall-clock DateCreate and DateUpdate of MSysObjects rows, a data page's write
/// stamp at 0x08, and the tick-count copy of it a chained long value's descriptor carries; and a stored query's
/// WHERE-row LvExtra, which ACE can leave uninitialised. A query's compiled plan
/// (MSysObjects.Lv) is copied from ACE's file into LibRed's before comparing. A copy opened and closed through ACE with nothing
/// done to it shows what ACE writes merely for holding a session; a byte that alone changes is counted apart rather
/// than as a difference. Calculated and complex columns are left out — ACE's OLE DB DDL cannot create either.</para>
/// </remarks>
[Collection(AceCollection.Name)]
public class WholeFileParityProbeTest(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0)] // The entire current corpus; explicit prefixes locate continuous-session divergence.
    [InlineData(8, Explicit = true)]
    [InlineData(64, Explicit = true)]
    [InlineData(96, Explicit = true)]
    [InlineData(117, Explicit = true)]
    [InlineData(118, Explicit = true)]
    [InlineData(118, 430, Explicit = true)]
    [InlineData(118, 480, Explicit = true)]
    [InlineData(118, 600, Explicit = true)]
    [InlineData(118, 550, Explicit = true)]
    [InlineData(118, 590, Explicit = true)]
    [InlineData(118, 591, Explicit = true)]
    [InlineData(118, 820, Explicit = true)]
    [InlineData(118, 840, Explicit = true)]
    [InlineData(118, 880, Explicit = true)]
    [InlineData(118, 900, Explicit = true)]
    [InlineData(118, 910, Explicit = true)]
    [InlineData(118, 918, Explicit = true)]
    [InlineData(118, -900, Explicit = true)]
    [InlineData(118, 1000, 80, Explicit = true)]
    [InlineData(118, 1000, 120, Explicit = true)]
    [InlineData(118, 550, 120, Explicit = true)]
    [InlineData(118, 580, 120, Explicit = true)]
    [InlineData(118, 590, 120, Explicit = true)]
    [InlineData(118, 591, 120, Explicit = true)]
    [InlineData(118, 610, 120, Explicit = true)]
    [InlineData(118, 1000, 200, Explicit = true)]
    [InlineData(118, 920, Explicit = true)]
    [InlineData(119, Explicit = true)]
    [InlineData(120, Explicit = true)]
    [InlineData(124, Explicit = true)]
    [InlineData(128, Explicit = true)]
    [InlineData(132, Explicit = true)]
    [InlineData(136, Explicit = true)]
    [InlineData(140, Explicit = true)]
    [InlineData(150, Explicit = true)]
    [InlineData(151, Explicit = true)]
    [InlineData(155, Explicit = true)]
    [InlineData(162, Explicit = true)]
    [InlineData(164, Explicit = true)]
    [InlineData(168, Explicit = true)]
    [InlineData(176, Explicit = true)]
    [InlineData(184, Explicit = true)]
    [InlineData(192, Explicit = true)]
    [InlineData(200, Explicit = true)]
    [InlineData(247, Explicit = true)]
    [InlineData(253, Explicit = true)]
    [InlineData(265, Explicit = true)]
    [InlineData(270, Explicit = true)]
    [InlineData(300, Explicit = true)]
    public void The_whole_run_leaves_libred_and_ace_with_the_same_file(int count, int bulkRows = 1000, int payloadLength = 150)
    {
        string[] statements = Statements();
        if (count > 0) statements = statements[..count];
        if (payloadLength != 150)
            statements[117] = statements[117].Replace("String(150, 'p')", $"String({payloadLength}, 'p')", StringComparison.Ordinal);
        if (bulkRows < 0)
        {
            string bulk = statements[117];
            statements[117] = bulk.Replace("ORDER BY 1", $"WHERE a.D * 100 + b.D * 10 + c.D < {-bulkRows} ORDER BY 1", StringComparison.Ordinal);
            statements = [.. statements, bulk.Replace("ORDER BY 1", $"WHERE a.D * 100 + b.D * 10 + c.D >= {-bulkRows} ORDER BY 1", StringComparison.Ordinal)];
        }
        else if (bulkRows != 1000)
            statements[117] = statements[117].Replace("ORDER BY 1", $"WHERE a.D * 100 + b.D * 10 + c.D < {bulkRows} ORDER BY 1", StringComparison.Ordinal);
        string? origin = CreateThroughDao();
        Assert.SkipWhen(origin is null, "DAO is not registered in this bitness.");
        string ace = TemporaryDatabase.CopyPath(origin, "wholefile-ace-");
        string noise = TemporaryDatabase.CopyPath(origin, "wholefile-noise-");
        string libred = TemporaryDatabase.CopyPath(origin, "wholefile-libred-");
        try
        {
            void Snapshot(string path, string label)
            {
                if (bulkRows == 1000 || payloadLength != 150) return;
                Thread.Sleep(3000);
                string snapshot = TemporaryDatabase.CopyPath(path, "continuous-live-");
                try
                {
                    using var database = JetDatabase.Open(snapshot, readOnly: true);
                    var free = PageGroupProbeTest.GlobalFree(database.OpenTable("Bulk"), 256);
                    output.WriteLine($"{label} live free: {string.Join(",", free.Where(p => p >= 140 && p < 240).Order())}");
                }
                finally { TemporaryDatabase.Delete(snapshot); }
            }
            string?[] aceErrors = RunThroughAce(ace, statements, () => Snapshot(ace, "ACE"));
            using (AceTestDatabase.Open(noise)) { }
            string?[] libredErrors = RunThroughLibRed(libred, statements, () => Snapshot(libred, "LibRed"));

            var report = new StringBuilder();
            report.AppendLine(CultureInfo.InvariantCulture, $"{statements.Length} statements");
            for (int i = 0; i < statements.Length; i++)
                if (aceErrors[i] is not null || libredErrors[i] is not null)
                    report.AppendLine(CultureInfo.InvariantCulture,
                        $"  #{i + 1} {statements[i]}\n      ACE: {aceErrors[i] ?? "ok"}\n      LibRed: {libredErrors[i] ?? "ok"}");
            report.Append(Compare(origin, ace, noise, libred).Report);
            if (statements.Length >= 117)
            {
                report.AppendLine($"ACE placement: {PageGroupProbeTest.Runs(ace)}");
                report.AppendLine($"LibRed placement: {PageGroupProbeTest.Runs(libred)}");
                foreach (string path in new[] { ace, libred })
                {
                    using var database = JetDatabase.Open(path, readOnly: true);
                    Table bulk = database.OpenTable("Bulk");
                    foreach (var index in bulk.Definition.Indexes)
                        report.AppendLine($"{(path == ace ? "ACE" : "LibRed")} {index.Name} pages: {string.Join(",", bulk.UsageMap.PagesInMap(index.UsageMap.Row, index.UsageMap.Page))}");
                }
            }
            output.WriteLine(report.ToString());
        }
        finally
        {
            foreach (string path in new[] { origin, ace, noise, libred }) TemporaryDatabase.Delete(path);
        }
    }

    /// <summary>The whole run without the statements that create, run or drop a stored query, which asserts the two
    /// files match: those leave ACE's compiled plan in MSysObjects.Lv, a memory image LibRed does not write, and the
    /// pages it takes shift every later allocation. On an ACE 12 file and on a Jet 4 one; and with the statements in
    /// transactions, every other group rolled back and then run again, the rest committed.</summary>
    [Theory]
    [InlineData(Ace12, false)]
    // Open: a Jet 4 file differs at three statements (see the Jet 4 step run), and a run in transactions past its
    // first 96 statements.
    [InlineData(Jet4, false, Explicit = true)]
    [InlineData(Ace12, true, Explicit = true)]
    [InlineData(Ace12, true, 12, Explicit = true)] // prefixes locate where a run in transactions diverges
    [InlineData(Ace12, true, 24, Explicit = true)]
    [InlineData(Ace12, true, 28, Explicit = true)]
    [InlineData(Ace12, true, 32, Explicit = true)]
    [InlineData(Ace12, true, 33, Explicit = true)]
    [InlineData(Ace12, true, 34, Explicit = true)]
    [InlineData(Ace12, true, 35, Explicit = true)]
    [InlineData(Ace12, true, 36, Explicit = true)]
    [InlineData(Ace12, true, 40, Explicit = true)]
    [InlineData(Ace12, true, 44, Explicit = true)]
    [InlineData(Ace12, true, 48, Explicit = true)]
    [InlineData(Ace12, true, 72, Explicit = true)]
    [InlineData(Ace12, true, 76, Explicit = true)]
    [InlineData(Ace12, true, 80, Explicit = true)]
    [InlineData(Ace12, true, 84, Explicit = true)]
    [InlineData(Ace12, true, 85, Explicit = true)]
    [InlineData(Ace12, true, 86, Explicit = true)]
    [InlineData(Ace12, true, 87, Explicit = true)]
    [InlineData(Ace12, true, 88, Explicit = true)]
    [InlineData(Ace12, true, 92, Explicit = true)]
    [InlineData(Ace12, true, 96, Explicit = true)]
    public void The_whole_run_without_stored_queries_leaves_libred_and_ace_with_the_same_file(int version, bool transactions, int count = 0)
    {
        string[] statements = [.. Statements().Where(s => !s.StartsWith("CREATE VIEW", StringComparison.Ordinal)
            && !s.StartsWith("CREATE PROCEDURE", StringComparison.Ordinal) && !s.StartsWith("EXECUTE", StringComparison.Ordinal)
            && !s.StartsWith("DROP VIEW", StringComparison.Ordinal) && !s.StartsWith("DROP PROCEDURE", StringComparison.Ordinal))];
        if (count > 0) statements = statements[..count];
        if (transactions)
        {
            // Groups of up to four, an ALTER or DROP always opening one: ACE cannot lock a table for either once
            // the transaction has used it, a limit of its locking LibRed does not copy.
            var groups = new List<List<string>>();
            foreach (string statement in statements)
            {
                if (groups.Count == 0 || groups[^1].Count == 4
                    || statement.StartsWith("ALTER", StringComparison.Ordinal) || statement.StartsWith("DROP", StringComparison.Ordinal))
                    groups.Add([]);
                groups[^1].Add(statement);
            }
            statements = [.. groups.SelectMany((group, i) => i % 2 == 0
                ? (string[])["BEGIN TRANSACTION", .. group, "ROLLBACK TRANSACTION", .. group]
                : ["BEGIN TRANSACTION", .. group, "COMMIT TRANSACTION"])];
        }
        AssertSameFile(CreateThroughDao(version), statements);
    }

    /// <summary>A key smaller than every key of a leaf full of one duplicated key: the new entry is the leaf's first,
    /// so it splits there, its entries compressed to a byte or two.</summary>
    [Fact]
    public void A_new_first_key_on_a_leaf_of_duplicates_leaves_libred_and_ace_with_the_same_file() =>
        AssertSameFile(CreateThroughDao(Ace12),
        [
            "CREATE TABLE Digit (D INTEGER)",
            .. Enumerable.Range(0, 10).Select(d => $"INSERT INTO Digit (D) VALUES ({d})"),
            "CREATE TABLE T (Id LONG, G LONG)",
            "CREATE INDEX ixG ON T (G)",
            "INSERT INTO T (Id, G) SELECT a.D * 100 + b.D * 10 + c.D, 2 FROM Digit a, Digit b, Digit c ORDER BY 1",
            "INSERT INTO T (Id, G) SELECT 1000 + a.D * 100 + b.D * 10 + c.D, 2 FROM Digit a, Digit b, Digit c ORDER BY 1",
            "INSERT INTO T (Id, G) VALUES (5000, 1)",
        ]);

    /// <summary>An index dropped and another created on the same table — or a row deleted and another inserted — on
    /// their own and in one transaction.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void An_index_swapped_leaves_libred_and_ace_with_the_same_file(bool inTransaction, bool rows = false)
    {
        string[] swap = rows
            ? ["DELETE FROM C WHERE Id = 3", "INSERT INTO C (Name, City) VALUES ('Customer 99', 'City 9')"]
            : ["DROP INDEX ixCity ON C", "CREATE INDEX ixName ON C (Name)"];
        AssertSameFile(CreateThroughDao(Ace12),
        [
            "CREATE TABLE C (Id COUNTER CONSTRAINT pkC PRIMARY KEY, Name TEXT(50), City TEXT(50))",
            "CREATE INDEX ixCity ON C (City)",
            .. Enumerable.Range(1, 12).Select(i => $"INSERT INTO C (Name, City) VALUES ('Customer {i:00}', 'City {i % 5}')"),
            .. inTransaction ? ["BEGIN TRANSACTION", .. swap, "COMMIT TRANSACTION"] : swap,
        ]);
    }

    /// <summary>Writes to calculated and complex columns, which ACE's DDL cannot create: DAO creates them, with a
    /// complex column's first values, in the file both engines start from — the calculated column raising it to the
    /// 2010 format — and the statements insert, update and delete through them, run through DAO.</summary>
    // Open: the owner table's rows land on other pages through DAO's INSERT than LibRed places them.
    [Fact(Explicit = true)]
    public void Writes_to_calculated_and_complex_columns_leave_libred_and_ace_with_the_same_file()
    {
        string attachment = TemporaryDatabase.CreatePath("wholefile-attachment-") + ".txt";
        File.WriteAllText(attachment, new string('a', 3000));
        try
        {
            string? origin = CreateThroughDao(Ace12, database =>
            {
                object calculated = Invoke(database, "CreateTableDef", "Calc")!;
                object fields = Get(calculated, "Fields");
                Invoke(fields, "Append", Invoke(calculated, "CreateField", "Id", DaoLong)!);
                Invoke(fields, "Append", Invoke(calculated, "CreateField", "Qty", DaoLong)!);
                Invoke(fields, "Append", Invoke(calculated, "CreateField", "Txt", DaoText, 255)!);
                foreach ((string name, int type, string expression) in new[]
                {
                    ("Twice", DaoLong, "[Qty]*2"), ("Label", DaoText, "\"n=\" & [Qty]"), ("Long", DaoMemo, "[Txt] & [Txt] & [Txt] & [Txt]"),
                })
                {
                    object field = Invoke(calculated, "CreateField", name, type)!;
                    Set(field, "Expression", expression);
                    Invoke(fields, "Append", field);
                }
                Invoke(Get(database, "TableDefs"), "Append", calculated);

                object owner = Invoke(database, "CreateTableDef", "Owner")!;
                fields = Get(owner, "Fields");
                Invoke(fields, "Append", Invoke(owner, "CreateField", "Id", DaoLong)!);
                Invoke(fields, "Append", Invoke(owner, "CreateField", "Txt", DaoText, 255)!);
                Invoke(fields, "Append", Invoke(owner, "CreateField", "Numbers", DaoMultiValueLong)!);
                Invoke(fields, "Append", Invoke(owner, "CreateField", "Words", DaoMultiValueText)!);
                Invoke(fields, "Append", Invoke(owner, "CreateField", "Files", DaoAttachment)!);
                Invoke(Get(database, "TableDefs"), "Append", owner);

                object rows = Invoke(database, "OpenRecordset", "Owner")!;
                for (int id = 1; id <= 40; id++)
                {
                    Invoke(rows, "AddNew");
                    Set(Item(Get(rows, "Fields"), "Id"), "Value", id);
                    Set(Item(Get(rows, "Fields"), "Txt"), "Value", new string('t', id * 5));
                    foreach ((string column, object[] values) in new[]
                    {
                        ("Numbers", Enumerable.Range(0, id % 4 + 1).Select(v => (object)(id * 10 + v)).ToArray()),
                        ("Words", Enumerable.Range(0, id % 3).Select(v => (object)new string((char)('a' + v), 40 + id)).ToArray()),
                    })
                    {
                        object child = Get(Item(Get(rows, "Fields"), column), "Value");
                        foreach (object value in values)
                        {
                            Invoke(child, "AddNew");
                            Set(Item(Get(child, "Fields"), "Value"), "Value", value);
                            Invoke(child, "Update");
                        }
                        Invoke(child, "Close");
                    }
                    if (id % 5 == 0)
                    {
                        object files = Get(Item(Get(rows, "Fields"), "Files"), "Value");
                        Invoke(files, "AddNew");
                        Invoke(Item(Get(files, "Fields"), "FileData"), "LoadFromFile", attachment);
                        Invoke(files, "Update");
                        Invoke(files, "Close");
                    }
                    Invoke(rows, "Update");
                }
                Invoke(rows, "Close");
            });
            AssertSameFile(origin,
            [
                "CREATE TABLE Digit (D INTEGER)",
                .. Enumerable.Range(0, 10).Select(d => $"INSERT INTO Digit (D) VALUES ({d})"),
                "INSERT INTO Calc (Id, Qty, Txt) SELECT a.D * 10 + b.D, a.D + b.D, String(a.D * 20 + b.D, 'x') FROM Digit a, Digit b ORDER BY 1",
                "UPDATE Calc SET Qty = Qty + 7 WHERE Id MOD 3 = 0",
                "UPDATE Calc SET Txt = String(250, 'y') WHERE Id MOD 4 = 0",
                "UPDATE Calc SET Txt = 'short' WHERE Id MOD 6 = 0",
                "DELETE FROM Calc WHERE Id MOD 5 = 0",
                "ALTER TABLE Calc ADD COLUMN Extra LONG",
                "CREATE INDEX ixCalcQty ON Calc (Qty)",
                "INSERT INTO Calc (Id, Qty, Txt) SELECT 100 + a.D * 10 + b.D, b.D, String(a.D * 25, 'z') FROM Digit a, Digit b ORDER BY 1",
                "DELETE FROM Owner WHERE Id MOD 4 = 0",
                "UPDATE Owner SET Txt = String(200, 'u') WHERE Id MOD 3 = 0",
                "INSERT INTO Owner (Id, Txt) SELECT 100 + D, 'new' FROM Digit ORDER BY 1",
                "DELETE FROM Owner WHERE Id > 30",
                "DELETE FROM Calc WHERE Id < 50",
                "DELETE FROM Owner",
                "DROP TABLE Calc",
            ], dao: true);
        }
        finally { TemporaryDatabase.Delete(attachment); }
    }

    /// <summary>A file grown past the 32,000 pages ACE's inline global free map covers, so the map turns to bitmap
    /// pages and a second bitmap's range is reached, then rows deleted, updated, indexed and added again.</summary>
    [Theory]
    // Open: from 16 statements on, stale bytes in map holders' free space — on page 1 one bit of the inline global map
    // left by its promotion, then on the table's holder what its free-pages map's conversion leaves — and from 19 on
    // the CREATE INDEX over a much-duplicated key builds three fewer index pages than ACE's.
    [InlineData(0, Explicit = true)]
    [InlineData(13)] // prefixes locate where the large file diverges
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16, Explicit = true)]
    [InlineData(17, Explicit = true)]
    [InlineData(18, Explicit = true)]
    [InlineData(19, Explicit = true)]
    [InlineData(20, Explicit = true)]
    public void A_file_past_the_inline_free_map_leaves_libred_and_ace_with_the_same_file(int count)
    {
        string[] statements = LargeFileStatements();
        AssertSameFile(CreateThroughDao(Ace12), count > 0 ? statements[..count] : statements);
    }

    /// <summary>The large-file run: <paramref name="secondRows"/> rows in the second insert and
    /// <paramref name="fourthRows"/> in the fourth, one row to a data page.</summary>
    private static string[] LargeFileStatements(int secondRows = 10000, int fourthRows = 3000)
    {
        // Seven uncompressed 255-character texts make a row of over 3,500 bytes: one row to a data page.
        static string Insert(int from, int rows) =>
            "INSERT INTO Wide (Id, Grp, A, B, C, E, F, G, H) SELECT " +
            $"{from} + a.D * 1000 + b.D * 100 + c.D * 10 + d.D, d.D, " +
            string.Join(", ", Enumerable.Repeat("String(255, 'w')", 7)) +
            $" FROM Digit a, Digit b, Digit c, Digit d WHERE a.D * 1000 + b.D * 100 + c.D * 10 + d.D < {rows} ORDER BY 1";
        return
        [
            "CREATE TABLE Digit (D INTEGER)",
            .. Enumerable.Range(0, 10).Select(d => $"INSERT INTO Digit (D) VALUES ({d})"),
            "CREATE TABLE Wide (Id LONG CONSTRAINT pkWide PRIMARY KEY, Grp LONG, A TEXT(255), B TEXT(255), C TEXT(255), "
                + "E TEXT(255), F TEXT(255), G TEXT(255), H TEXT(255))",
            Insert(0, 10000), Insert(10000, secondRows), Insert(20000, 10000), Insert(30000, fourthRows),
            "DELETE FROM Wide WHERE Id MOD 7 = 0",
            "UPDATE Wide SET A = 'short' WHERE Id MOD 11 = 0",
            "CREATE INDEX ixWideGrp ON Wide (Grp)",
            Insert(40000, 1500),
            "DELETE FROM Wide WHERE Id >= 31000 AND Id < 32000",
            Insert(50000, 500),
        ];
    }

    // PROBE: the global maps and Wide's usage maps after the first count statements of the large-file run, in each
    // engine — where each map turns reference form and where its bitmap pages go — with the last pages' layout.
    // With lowFree, a dropped table leaves free pages low in the file and the second insert runs in a session of its
    // own: whether the bitmap page takes one of those or the index group's held page. With splitAt, the statements from
    // there on run in a session of their own.
    [Theory(Explicit = true)]
    [InlineData(10000, false, 19)]
    public void Probe_large_file_usage_maps(int rows, bool lowFree, int count = 14, int fourthRows = 3000, int splitAt = 0)
    {
        string[] statements = LargeFileStatements(rows, fourthRows)[..count];
        string[][] sessions = splitAt > 0 ? [statements[..splitAt], statements[splitAt..]] : lowFree
            ?
            [
                [
                    .. statements[..11],
                    "CREATE TABLE Filler (Id LONG, A TEXT(255), B TEXT(255), C TEXT(255), E TEXT(255), F TEXT(255), G TEXT(255), H TEXT(255))",
                    "INSERT INTO Filler (Id, A, B, C, E, F, G, H) SELECT D, " + string.Join(", ", Enumerable.Repeat("String(255, 'f')", 7)) + " FROM Digit",
                    .. statements[11..13],
                    "DROP TABLE Filler",
                ],
                [statements[13]],
            ]
            : [statements];
        string? origin = CreateThroughDao(Ace12);
        Assert.SkipWhen(origin is null, "DAO is not registered in this bitness.");
        string ace = TemporaryDatabase.CopyPath(origin, "largemaps-ace-"), libred = TemporaryDatabase.CopyPath(origin, "largemaps-libred-");
        try
        {
            foreach (string[] session in sessions)
            {
                Assert.All(RunThroughAce(ace, session), e => Assert.Null(e));
                Assert.All(RunThroughLibRed(libred, session), e => Assert.Null(e));
                if (lowFree) output.WriteLine($"ACE free below the end after a session: {FreeBelowEnd(ace)}");
            }
            output.WriteLine($"ACE:    {Maps(ace)}");
            output.WriteLine($"LibRed: {Maps(libred)}");
            output.WriteLine($"ACE free below the end:    {FreeBelowEnd(ace)}");
            output.WriteLine($"LibRed free below the end: {FreeBelowEnd(libred)}");
            foreach ((string name, string path) in new[] { ("ACE", ace), ("LibRed", libred) })
                output.WriteLine($"{name}: {RollbackAllocationProbeTest.Describe(path, 33100)}");
        }
        finally
        {
            foreach (string path in new[] { origin, ace, libred }) TemporaryDatabase.Delete(path);
        }

        static string FreeBelowEnd(string path)
        {
            using var db = JetDatabase.Open(path, readOnly: true);
            int pages = db.Channel.PageCount;
            return string.Join(",", PageGroupProbeTest.GlobalFree(db.OpenTable("MSysObjects"), pages).Where(p => p < pages).Order());
        }

        static string Maps(string path)
        {
            using var db = JetDatabase.Open(path, readOnly: true);
            JetFormatBase format = db.Format;
            Catalog.TableDefinition wide = db.Catalog.FindTable("Wide")!;
            IO.PageBuffer tdef = db.Channel.ReadPage(wide.DefinitionPage);
            var maps = new Storage.UsageMap(db.Channel, wide);
            var text = new StringBuilder($"{db.Channel.PageCount} pages");
            var page0 = new DatabaseDefinitionPage();
            page0.Read(db.Channel.ReadPageShared(0), format);
            foreach ((string name, (int row, int page)) in new[]
                {
                    ("global free", page0.FreePagesMap),
                    ("global released", page0.ReleasedPagesMap),
                    ("owned", tdef.ReadRecordPointer(format.TdefOwnedPagesOffset)),
                    ("free", tdef.ReadRecordPointer(format.TdefFreePagesOffset)),
                }.Concat(wide.Indexes.GroupBy(i => i.RealIndexOrdinal).Select(g => (g.First().Name, g.First().UsageMap))))
            {
                (IO.PageBuffer holder, _, DataPage.RowSlot slot) = Storage.UsageMap.ReadRecord(db.Channel, row, page, name);
                byte[] record = holder.Span.Slice(slot.Offset, slot.Length).ToArray();
                text.Append($"; {name} @{page}/{row} free {DataPage.ReadFreeSpace(db.Channel.ReadPage(page).Span, format)}: ");
                if (Storage.UsageMap.RecordType(record) == UsageMapType.Inline)
                    text.Append($"inline start {Storage.UsageMap.StartPage(record, format)} length {record.Length}");
                else
                    text.Append("reference [" + string.Join(",", Enumerable.Range(0, format.UsageMapReferenceSlots)
                        .Select(s => Storage.UsageMap.ReferencePointer(record, s, format)).Where(p => p != 0)) + "]");
            }
            return text.ToString();
        }
    }

    /// <summary>Runs <paramref name="statements"/> through ACE — over OLE DB, or through DAO with
    /// <paramref name="dao"/> — and through LibRed, each in one session on a copy of <paramref name="origin"/>, and
    /// asserts each statement succeeds or fails in both and the files match.</summary>
    private static void AssertSameFile(string? origin, string[] statements, bool dao = false)
    {
        Assert.SkipWhen(origin is null, "DAO is not registered in this bitness.");
        string ace = TemporaryDatabase.CopyPath(origin, "wholefile-ace-");
        string noise = TemporaryDatabase.CopyPath(origin, "wholefile-noise-");
        string libred = TemporaryDatabase.CopyPath(origin, "wholefile-libred-");
        try
        {
            string?[] aceErrors = RunThroughAce(ace, statements, dao: dao);
            RunThroughAce(noise, [], dao: dao);
            string?[] libredErrors = RunThroughLibRed(libred, statements);

            string outcomes = string.Concat(Enumerable.Range(0, statements.Length)
                .Where(i => aceErrors[i] is null != libredErrors[i] is null)
                .Select(i => $"\n#{i + 1} {statements[i]}\n  ACE: {aceErrors[i] ?? "ok"}\n  LibRed: {libredErrors[i] ?? "ok"}"));
            Assert.True(outcomes.Length == 0, outcomes);
            (string report, int pages) = Compare(origin, ace, noise, libred);
            // Each engine's layout — a small file's whole, a large one's around where the pages first differ — shows
            // where an allocation went its own way.
            if (pages > 0)
            {
                int first = System.Text.RegularExpressions.Regex.Matches(report, @"page (\d+):")
                    .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).Where(p => p > 100).DefaultIfEmpty(38).Min();
                (int from, int to) = new FileInfo(ace).Length < 200 * 4096 ? (38, int.MaxValue) : (Math.Max(38, first - 40), first + 60);
                report += $"\nACE layout: {RollbackAllocationProbeTest.Describe(ace, from, to)}" +
                    $"\nLibRed layout: {RollbackAllocationProbeTest.Describe(libred, from, to)}";
            }
            Assert.True(pages == 0, report);
        }
        finally
        {
            foreach (string path in new[] { origin, ace, noise, libred }) TemporaryDatabase.Delete(path);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(138)]
    [InlineData(-1)] // The bulk-allocation statement, independent of corpus numbering.
    // Open on a Jet 4 file: #230 and #231 (CREATE TABLE with constraints) and #473 (the Late table's long values).
    [InlineData(0, Jet4, Explicit = true)]
    public void Each_statement_leaves_libred_and_ace_with_the_same_file(int statement, int version = Ace12)
    {
        string[] statements = Statements();
        string? origin = CreateThroughDao(version);
        Assert.SkipWhen(origin is null, "DAO is not registered in this bitness.");
        string current = origin;
        var summary = new StringBuilder();
        var details = new StringBuilder();
        var outcomes = new string[statements.Length];
        int same = 0;
        try
        {
            for (int i = 0; i < statements.Length; i++)
            {
                string ace = TemporaryDatabase.CopyPath(current, $"stepfile-ace{i}-");
                bool selected = statement == 0 || statement > 0 && i + 1 == statement
                    || statement == -1 && statements[i].StartsWith("INSERT INTO Bulk (Id, Grp, Label, Payload, Amount) SELECT", StringComparison.Ordinal);
                if (!selected)
                {
                    Assert.Null(RunThroughAce(ace, [statements[i]])[0]);
                    if (current != origin) TemporaryDatabase.Delete(current);
                    current = ace;
                    continue;
                }
                string noise = TemporaryDatabase.CopyPath(current, $"stepfile-noise{i}-");
                string libred = TemporaryDatabase.CopyPath(current, $"stepfile-libred{i}-");
                string? aceError = RunThroughAce(ace, [statements[i]])[0];
                using (AceTestDatabase.Open(noise)) { }
                string? libredError = RunThroughLibRed(libred, [statements[i]])[0];
                CopyQueryPlans(ace, libred);

                (string report, int pages) = Compare(current, ace, noise, libred);
                if (statement == -1)
                {
                    output.WriteLine($"ACE placement: {PageGroupProbeTest.Runs(ace)}");
                    output.WriteLine($"LibRed placement: {PageGroupProbeTest.Runs(libred)}");
                }
                string outcome = aceError is not null || libredError is not null
                    ? $"ACE: {aceError ?? "ok"} | LibRed: {libredError ?? "ok"}"
                    : pages == 0 ? "identical" : $"{pages} page(s) differ";
                if (outcome == "identical") same++;
                outcomes[i] = outcome;
                summary.AppendLine(CultureInfo.InvariantCulture, $"#{i + 1,-3} {outcome,-22} {Abbreviate(statements[i])}");
                if (pages > 0)
                    details.AppendLine(CultureInfo.InvariantCulture, $"==== #{i + 1} {statements[i]}").Append(report);

                // The next statement starts from ACE's result, so each is measured on its own.
                if (current != origin) TemporaryDatabase.Delete(current);
                current = ace;
                TemporaryDatabase.Delete(noise);
                TemporaryDatabase.Delete(libred);
                if (statement != 0) break;
            }

            output.WriteLine($"{same} of {statements.Length} statements left identical files\n{summary}\n{details}");

            var unexpected = new List<string>();
            for (int i = 0; i < statements.Length; i++)
            {
                if (outcomes[i] is null) continue;
                if (outcomes[i] != "identical")
                    unexpected.Add($"#{i + 1} {outcomes[i]}: {Abbreviate(statements[i])}");
            }
            Assert.True(unexpected.Count == 0, string.Join("\n", unexpected));
        }
        finally
        {
            if (current != origin) TemporaryDatabase.Delete(current);
            TemporaryDatabase.Delete(origin);
        }
    }

    /// <summary>One statement run through ACE three times from the same file: every byte where ACE does not agree
    /// with itself, which no engine could be expected to reproduce.</summary>
    [Theory(Explicit = true)]
    [InlineData(162, 234)]
    [InlineData(247, 69)]
    [InlineData(253, 0)]
    public void Ace_against_itself(int statement, int page)
    {
        string[] statements = Statements();
        string? origin = CreateThroughDao();
        Assert.SkipWhen(origin is null, "DAO is not registered in this bitness.");
        string current = origin;
        var runs = new List<string>();
        try
        {
            for (int i = 0; i < statement - 1; i++)
            {
                string next = TemporaryDatabase.CopyPath(current, $"selfcheck{i}-");
                Assert.Null(RunThroughAce(next, [statements[i]])[0]);
                if (current != origin) TemporaryDatabase.Delete(current);
                current = next;
            }
            for (int run = 0; run < 3; run++)
            {
                runs.Add(TemporaryDatabase.CopyPath(current, $"selfcheck-run{run}-"));
                Assert.Null(RunThroughAce(runs[run], [statements[statement - 1]])[0]);
            }
            byte[][] files = [.. runs.Select(File.ReadAllBytes)];
            output.WriteLine($"#{statement} {statements[statement - 1]}; lengths {string.Join(", ", files.Select(f => f.Length))}");
            for (int at = 0; at < files.Min(f => f.Length); at++)
                if (files[1][at] != files[0][at] || files[2][at] != files[0][at])
                    output.WriteLine($"  page {at / 4096} +0x{at % 4096:X3}: {string.Join(" ", files.Select(f => f[at].ToString("X2", CultureInfo.InvariantCulture)))}");
            if (page > 0)
            {
                byte[] before = File.ReadAllBytes(current);
                for (int line = 0; line < 4096; line += 32)
                {
                    string was = Convert.ToHexString(before, page * 4096 + line, 32), now = Convert.ToHexString(files[0], page * 4096 + line, 32);
                    if (was != now || was.Trim('0').Length > 0)
                        output.WriteLine($"  +0x{line:X3} WAS {was}\n         NOW {now}");
                }
            }
        }
        finally
        {
            foreach (string run in runs) TemporaryDatabase.Delete(run);
            if (current != origin) TemporaryDatabase.Delete(current);
            TemporaryDatabase.Delete(origin);
        }
    }

    /// <summary>Statements over ordinary column types and each kind of
    /// constraint, inserts, updates and deletes, more inserts into the space the deletes freed, a column added,
    /// retyped and dropped, indexes made and dropped, and a table dropped and another made after it. Then what
    /// spans pages: a thousand rows from one <c>INSERT … SELECT</c> over many data pages with a text index grown
    /// past one leaf, a 253-column table whose definition runs onto continuation pages, and long values chained
    /// across pages, grown, shrunk and deleted.</summary>
    private static string[] Statements()
    {
        var s = new List<string>
        {
            "CREATE TABLE Customer (Id COUNTER CONSTRAINT pkCustomer PRIMARY KEY, Name TEXT(50) NOT NULL, City TEXT(30), " +
                "Joined DATETIME, Credit CURRENCY, Active BIT)",
            "CREATE TABLE Product (Code TEXT(10) CONSTRAINT pkProduct PRIMARY KEY, Title TEXT(100), Price DOUBLE, " +
                "Weight REAL, Stock SHORT, Rating BYTE, Notes MEMO)",
            "CREATE TABLE Orders (OrderId LONG CONSTRAINT pkOrders PRIMARY KEY, CustomerId LONG, Placed DATETIME, " +
                "Total DECIMAL(18,4), Ref GUID, CONSTRAINT fkOrderCustomer FOREIGN KEY (CustomerId) REFERENCES Customer (Id))",
            "CREATE TABLE OrderLine (OrderId LONG, LineNo SHORT, ProductCode TEXT(10), Qty LONG, " +
                "CONSTRAINT pkOrderLine PRIMARY KEY (OrderId, LineNo))",
            "CREATE TABLE Scratch (Id LONG, Data VARBINARY(100), Flag BIT, Label TEXT(20))",
            "CREATE INDEX ixCustomerCity ON Customer (City)",
            "CREATE UNIQUE INDEX ixProductTitle ON Product (Title)",
            "CREATE INDEX ixOrderPlaced ON Orders (Placed DESC)",
        };

        string[] cities = ["London", "Paris", "Berlin", "Madrid", "Rome", "Oslo", "Vienna", "Prague", "Lisbon", "Dublin"];
        for (int i = 1; i <= 12; i++)
            s.Add($"INSERT INTO Customer (Name, City, Joined, Credit, Active) VALUES ('Customer {i:D2}', " +
                  $"'{cities[i % cities.Length]}', #2020-{(i % 12) + 1:D2}-{(i % 27) + 1:D2} {i % 24:D2}:15:00#, {i * 125.5m}, {(i % 3 == 0 ? "FALSE" : "TRUE")})");

        for (int i = 1; i <= 10; i++)
            s.Add($"INSERT INTO Product (Code, Title, Price, Weight, Stock, Rating, Notes) VALUES ('P{i:D3}', 'Product number {i}', " +
                  $"{i * 9.99}, {i * 0.25}, {i * 7}, {i * 20 % 256}, '{Notes(i)}')");

        for (int i = 1; i <= 12; i++)
            s.Add($"INSERT INTO Orders (OrderId, CustomerId, Placed, Total, Ref) VALUES ({1000 + i}, {(i % 9) + 1}, " +
                  $"#2021-{(i % 12) + 1:D2}-15#, {i * 101.1234m}, {{guid {{{new Guid(i, 0, 0, [1, 2, 3, 4, 5, 6, 7, (byte)i])}}}}})");

        for (int i = 1; i <= 16; i++)
            s.Add($"INSERT INTO OrderLine (OrderId, LineNo, ProductCode, Qty) VALUES ({1000 + (i % 8) + 1}, {i}, 'P{(i % 10) + 1:D3}', {i % 6})");

        for (int i = 1; i <= 6; i++)
            s.Add($"INSERT INTO Scratch (Id, Data, Flag, Label) VALUES ({i}, 0x{Convert.ToHexString([.. Enumerable.Range(i, 20).Select(b => (byte)b)])}, " +
                  $"{(i % 2 == 0 ? "TRUE" : "FALSE")}, 'scratch {i}')");

        s.AddRange(
        [
            "UPDATE Customer SET City = 'Paris' WHERE Id = 3",
            "UPDATE Customer SET Credit = Credit * 2 WHERE Active = TRUE",
            "UPDATE Product SET Price = Price + 1, Stock = Stock - 1",
            "UPDATE Product SET Notes = 'short note' WHERE Code = 'P002'",
            "UPDATE Orders SET Total = Total + 0.5 WHERE CustomerId = 2",
            "DELETE FROM OrderLine WHERE Qty < 2",
            "DELETE FROM Scratch WHERE Id > 4",
            "DELETE FROM Customer WHERE Id = 12",
            "DELETE FROM Product WHERE Code = 'P010'",
        ]);

        for (int i = 17; i <= 22; i++)
            s.Add($"INSERT INTO OrderLine (OrderId, LineNo, ProductCode, Qty) VALUES ({1000 + (i % 8) + 1}, {i}, 'P{(i % 9) + 1:D3}', {i})");

        s.AddRange(
        [
            "ALTER TABLE Customer ADD COLUMN Email TEXT(80)",
            "UPDATE Customer SET Email = 'c' & Id & '@example.com' WHERE Id < 6",
            "ALTER TABLE Product ALTER COLUMN Stock LONG",
            "ALTER TABLE Scratch DROP COLUMN Flag",
            "INSERT INTO Scratch (Id, Data, Label) VALUES (7, 0x0A0B0C, 'after drop')",
            "DROP INDEX ixCustomerCity ON Customer",
            "CREATE INDEX ixCustomerName ON Customer (Name)",
            "DROP TABLE Scratch",
            "CREATE TABLE Audit (Id COUNTER CONSTRAINT pkAudit PRIMARY KEY, Detail TEXT(255), LoggedOn DATETIME, Charge CURRENCY)",
        ]);

        for (int i = 1; i <= 8; i++)
            s.Add($"INSERT INTO Audit (Detail, LoggedOn, Charge) VALUES ('Audit entry {i} {new string('x', i * 20)}', #2022-03-{i:D2} 08:00:00#, {i * 3.25m})");

        s.AddRange(
        [
            "DELETE FROM Audit WHERE Id IN (2, 4, 6)",
            "INSERT INTO Audit (Detail, LoggedOn, Charge) VALUES ('refill one', #2022-04-01#, 1)",
            "INSERT INTO Audit (Detail, LoggedOn, Charge) VALUES ('refill two', #2022-04-02#, 2)",
            "ALTER TABLE Audit ADD COLUMN Severity BYTE",
            "UPDATE Audit SET Severity = Id MOD 4",
            "UPDATE Customer SET Name = 'Renamed ' & Name WHERE Id MOD 2 = 0",
            "DELETE FROM Orders WHERE OrderId = 1012",
            "INSERT INTO Customer (Name, City, Joined, Credit, Active, Email) VALUES ('Late comer', 'Oslo', #2023-01-01#, 10, TRUE, 'late@example.com')",
        ]);

        // A table over many data pages, filled by one INSERT … SELECT over a cross join, with an index that grows
        // past one leaf; deletes across its pages; and updates that grow rows until they have to move.
        s.Add("CREATE TABLE Digits (D BYTE)");
        for (int d = 0; d <= 9; d++) s.Add($"INSERT INTO Digits (D) VALUES ({d})");
        s.AddRange(
        [
            "CREATE TABLE Bulk (Id LONG CONSTRAINT pkBulk PRIMARY KEY, Grp LONG, Label TEXT(60), Payload TEXT(255), " +
                "Code BINARY(8), Amount DECIMAL(10,2))",
            "CREATE INDEX ixBulkLabel ON Bulk (Label)",
            "INSERT INTO Bulk (Id, Grp, Label, Payload, Amount) SELECT a.D * 100 + b.D * 10 + c.D, a.D, " +
                "'Bulk label number ' & (a.D * 100 + b.D * 10 + c.D) & ' ' & String(30, 'L'), String(150, 'p'), " +
                "a.D + b.D * 0.5 FROM Digits AS a, Digits AS b, Digits AS c ORDER BY 1",
            "DELETE FROM Bulk WHERE Id MOD 7 = 0",
            "UPDATE Bulk SET Payload = Payload & String(100, 'g') WHERE Id MOD 10 = 1",
            "UPDATE Bulk SET Label = 'R ' & Label WHERE Id < 50",
            "UPDATE Bulk SET Code = 0x0102030405060708 WHERE Id < 5",
            "INSERT INTO Bulk (Id, Grp, Label, Payload) VALUES (5000, 1, 'Late bulk', 'late')",
        ]);

        // A 253-column table, whose definition runs onto continuation pages, leaving room for one more column id
        // after a drop — ids are never reused before a compact, and 255 is the lifetime limit.
        string[] wideTypes = ["LONG", "TEXT(20)", "DOUBLE", "DATETIME", "CURRENCY", "BIT"];
        s.Add("CREATE TABLE Wide (Id LONG CONSTRAINT pkWide PRIMARY KEY, " +
              string.Join(", ", Enumerable.Range(1, 252).Select(i => $"Column{i:D3} {wideTypes[i % wideTypes.Length]}")) + ")");
        for (int i = 1; i <= 3; i++)
            s.Add($"INSERT INTO Wide (Id, Column001, Column002, Column252) VALUES ({i}, 'wide {i}', {i * 1.5}, {i})");
        s.AddRange(
        [
            "ALTER TABLE Wide DROP COLUMN Column005",
            "ALTER TABLE Wide ADD COLUMN Extra TEXT(10)",
            "UPDATE Wide SET Extra = 'extra' WHERE Id = 2",
        ]);

        // Long values across pages, the other binary types, and compressed text.
        s.AddRange(
        [
            "CREATE TABLE Doc (Id LONG CONSTRAINT pkDoc PRIMARY KEY, Body MEMO, Packed MEMO WITH COMPRESSION, " +
                "Brief TEXT(100) WITH COMPRESSION, Blob LONGBINARY, Big BIGBINARY(500))",
            $"INSERT INTO Doc VALUES (1, String(5000, 'b'), String(300, 'c'), 'short and compressed', 0x{Hex(6000, 1)}, 0x{Hex(400, 2)})",
            $"INSERT INTO Doc VALUES (2, String(2500, 'd'), 'packed two', 'two', 0x{Hex(100, 3)}, 0x{Hex(50, 4)})",
            "INSERT INTO Doc (Id, Body, Brief) VALUES (3, 'small', 'three')",
            "UPDATE Doc SET Body = String(8000, 'e') WHERE Id = 2",
            "UPDATE Doc SET Body = 'now small' WHERE Id = 1",
            $"UPDATE Doc SET Blob = 0x{Hex(9000, 5)} WHERE Id = 3",
            "DELETE FROM Doc WHERE Id = 2",
            "INSERT INTO Doc (Id, Body) VALUES (4, String(4000, 'f'))",
        ]);

        // Stored queries: views and procedures of each shape, run and dropped.
        s.AddRange(
        [
            "CREATE VIEW vActiveCustomer AS SELECT Name, City FROM Customer WHERE Active = TRUE",
            "CREATE VIEW vOrderTotal AS SELECT c.Name, o.Total FROM Customer AS c INNER JOIN Orders AS o ON c.Id = o.CustomerId",
            "CREATE VIEW vProductLine AS SELECT p.Code, l.Qty FROM Product AS p LEFT JOIN OrderLine AS l ON p.Code = l.ProductCode",
            "CREATE VIEW vCityCount AS SELECT City, COUNT(*) AS Customers FROM Customer GROUP BY City HAVING COUNT(*) > 0",
            "CREATE VIEW vCity AS SELECT DISTINCT City FROM Customer",
            "CREATE VIEW vCheapest AS SELECT TOP 3 Code, Price FROM Product",
            "CREATE VIEW vPriced AS SELECT Code, Price * 2 AS Doubled, Title & '!' AS Shout FROM Product WHERE Price BETWEEN 10 AND 90",
            "CREATE PROCEDURE pByCity (cityName TEXT(30)) AS SELECT Id, Name FROM Customer WHERE City = cityName",
            "CREATE PROCEDURE pSorted AS SELECT Name FROM Customer ORDER BY Name DESC",
            "CREATE PROCEDURE pBump (amount CURRENCY) AS UPDATE Customer SET Credit = Credit + amount WHERE Active = TRUE",
            "CREATE PROCEDURE pPurge AS DELETE FROM Audit WHERE Severity = 0",
            "CREATE PROCEDURE pLog AS INSERT INTO Audit (Detail, LoggedOn, Charge) VALUES ('from a procedure', #2022-05-01#, 1)",
            "CREATE PROCEDURE pCopy AS INSERT INTO Audit (Detail, LoggedOn, Charge) SELECT Name, Joined, Credit FROM Customer WHERE Id < 3",
            "EXECUTE pLog",
            "EXECUTE pPurge",
            "EXECUTE pCopy",
            "DROP VIEW vCheapest",
            "DROP PROCEDURE pPurge",
            "CREATE VIEW vDerived AS SELECT d.City, d.N FROM (SELECT City, COUNT(*) AS N FROM Customer GROUP BY City) AS d WHERE d.N > 0",
            "CREATE PROCEDURE pTyped (since DATETIME, lim LONG) AS SELECT Id FROM Customer WHERE Joined > since AND Id < lim",
            "CREATE PROCEDURE pSized (lim LONG, who TEXT(20)) AS SELECT Id FROM Customer WHERE Id < lim AND Name <> who ORDER BY Id",
            "CREATE PROCEDURE pSizedFirst (who TEXT(20), lim LONG) AS SELECT Id FROM Customer WHERE Id < lim AND Name <> who",
            "CREATE PROCEDURE pRename (who TEXT(20)) AS UPDATE Customer SET Name = who WHERE Id = 999",
            "CREATE PROCEDURE pCopyJoin AS INSERT INTO Audit (Detail, Charge) SELECT c.Name, o.Total FROM Customer AS c " +
                "INNER JOIN Orders AS o ON c.Id = o.CustomerId WHERE o.Total > 0",
            "CREATE PROCEDURE pMake AS SELECT Name, City INTO CustomerNames FROM Customer WHERE Id < 5",
            "CREATE VIEW vDistinctWhere AS SELECT DISTINCT City FROM Customer WHERE Id > 0",
            "CREATE PROCEDURE pMixed AS SELECT DISTINCT TOP 2 c.City FROM Customer AS c INNER JOIN Orders AS o ON c.Id = o.CustomerId " +
                "WHERE o.Total > 0 GROUP BY c.City HAVING COUNT(*) > 0 ORDER BY c.City",
        ]);

        // Reserved words as names, bracketed; defaults; CHECK constraints; a column-level REFERENCES dropped for a
        // cascading one, and the cascades it then makes.
        s.AddRange(
        [
            "CREATE TABLE Setting (Id LONG CONSTRAINT pkSetting PRIMARY KEY, Name TEXT(40) DEFAULT 'unnamed', " +
                "Enabled BIT DEFAULT TRUE, [Level] LONG DEFAULT 3, Created DATETIME DEFAULT Now(), [Note] TEXT(20), " +
                "CONSTRAINT uqSettingName UNIQUE (Name))",
            "INSERT INTO Setting (Id, Created) VALUES (1, #2024-01-01#)",
            "INSERT INTO Setting (Id, Name, Created) VALUES (2, 'second', #2024-01-02#)",
            "INSERT INTO Setting (Id, Name, [Level], Created, [Note]) VALUES (3, 'third', 9, #2024-01-03#, 'a note')",
            "INSERT INTO Setting (Id, Name, Enabled, Created) VALUES (4, 'a name longer than the default', FALSE, #2024-01-04#)",
            "ALTER TABLE Setting ADD CONSTRAINT ckSettingLevel CHECK ([Level] BETWEEN 0 AND 10)",
            "UPDATE Setting SET [Level] = 7 WHERE Id = 2",
            "UPDATE Setting SET Name = 'x' WHERE Id = 3",
            "UPDATE Setting SET [Note] = 'a longer note!' WHERE Id = 3",
            "ALTER TABLE Setting DROP CONSTRAINT ckSettingLevel",
            "ALTER TABLE Setting ADD CONSTRAINT ckSettingPair CHECK ([Level] < Len([Note]) + 20)",
            "ALTER TABLE Setting ADD CONSTRAINT ckSettingOther CHECK ([Level] <= (SELECT COUNT(*) FROM Customer) + 10)",
            "ALTER TABLE Setting ADD CONSTRAINT ckSettingNone CHECK (1 = 1)",
            "ALTER TABLE Setting DROP CONSTRAINT ckSettingPair",
            "ALTER TABLE Setting DROP CONSTRAINT ckSettingNone",
            "ALTER TABLE Setting DROP CONSTRAINT ckSettingOther",
            "ALTER TABLE Setting ADD CONSTRAINT ckSettingOrder CHECK (Len([Note]) < [Level] + 20 OR [Level] > 0)",
            "ALTER TABLE Setting ADD CONSTRAINT ckSettingCredit CHECK ([Level] < (SELECT MAX(Credit) FROM Customer WHERE City = 'Rome') + 10)",
            "ALTER TABLE Setting DROP CONSTRAINT ckSettingOrder",
            "ALTER TABLE Setting DROP CONSTRAINT ckSettingCredit",
            "ALTER TABLE Setting ADD CONSTRAINT ckSettingFirst CHECK ((SELECT COUNT(*) FROM Audit) >= [Level] - 100)",
            "ALTER TABLE Setting DROP CONSTRAINT ckSettingFirst",
            "ALTER TABLE Setting ADD CONSTRAINT ckShared CHECK ([Level] >= 0)",
            "ALTER TABLE Setting DROP CONSTRAINT ckShared",
            "ALTER TABLE Setting ADD CONSTRAINT Customer CHECK ([Level] >= 0)",
            "ALTER TABLE Setting DROP CONSTRAINT Customer",
            "ALTER TABLE Setting ADD CONSTRAINT ckSettingOwn CHECK (Setting.[Level] >= 0 and Setting.[Level]  <  100)",
            "ALTER TABLE Setting ADD CONSTRAINT ckSettingRow CHECK ((SELECT COUNT(*) FROM Customer WHERE Customer.Name = Setting.Name) >= 0)",
            "ALTER TABLE Setting DROP CONSTRAINT ckSettingOwn",
            "ALTER TABLE Setting DROP CONSTRAINT ckSettingRow",
            "CREATE TABLE Ranged (Id LONG, Lo LONG, Hi LONG, CONSTRAINT ckRange CHECK (Lo <= Hi))",
            "INSERT INTO Ranged VALUES (1, 2, 3)",
            "DROP TABLE Ranged",
            "CREATE TABLE Child (Id LONG CONSTRAINT pkChild PRIMARY KEY, SettingId LONG CONSTRAINT fkChildSetting REFERENCES Setting (Id), " +
                "Label TEXT(20))",
            "INSERT INTO Child VALUES (1, 1, 'one')",
            "INSERT INTO Child VALUES (2, 2, 'two')",
            "INSERT INTO Child VALUES (3, 3, 'three')",
            "ALTER TABLE Child DROP CONSTRAINT fkChildSetting",
            "ALTER TABLE Child ADD CONSTRAINT fkChildCascade FOREIGN KEY (SettingId) REFERENCES Setting (Id) ON DELETE CASCADE ON UPDATE CASCADE",
            "DELETE FROM Setting WHERE Id = 2",
            "UPDATE Setting SET Id = 10 WHERE Id = 1",
            "ALTER TABLE Child ADD COLUMN Weight DOUBLE",
            "UPDATE Child SET Weight = Id * 1.5",
            "ALTER TABLE Child ALTER COLUMN Label TEXT(50)",
            "ALTER TABLE Child ALTER COLUMN Weight LONG",
            "CREATE INDEX ixChildLabel ON Child (Label) WITH IGNORE NULL",
            "CREATE UNIQUE INDEX ixChildPair ON Child (SettingId, Label) WITH DISALLOW NULL",
            "DROP INDEX ixChildLabel ON Child",
            "CREATE TABLE Slack (K LONG, A TEXT(20) DEFAULT 'aaaa', B TEXT(20) DEFAULT 'bbbbbbbb', C TEXT(20))",
            "INSERT INTO Slack (K, A, B, C) VALUES (1, 'x', 'y', 'z')",
            "INSERT INTO Slack (K, C, B, A) VALUES (2, 'z', 'y', 'x')",
            "INSERT INTO Slack (K, C) VALUES (3, 'zzzzzzzzzzzzzzzz')",
            "INSERT INTO Slack (K, B) VALUES (4, 'z')",
            "INSERT INTO Slack (K, A, B) VALUES (5, NULL, '')",
            "UPDATE Slack SET A = 'xx', C = 'zzzzzz' WHERE K = 1",
            "UPDATE Slack SET C = 'zzzzzz', A = '' WHERE K = 2",
            "UPDATE Slack SET A = 'xxxxxxxxxxxxxxxxxxxx' WHERE K = 4",
            "UPDATE Slack SET K = 50 WHERE K = 5",
            "CREATE TABLE Prop1 (Id LONG, Name TEXT(40) DEFAULT 'unnamed', Enabled BIT DEFAULT TRUE, Tier LONG DEFAULT 3, " +
                "CONSTRAINT uqProp1 UNIQUE (Name))",
            "CREATE TABLE Prop2 (Id LONG, Name TEXT(40) DEFAULT 'unnamed', Enabled BIT DEFAULT TRUE, Tier LONG DEFAULT 3, " +
                "CONSTRAINT pkProp2 PRIMARY KEY (Id))",
            "CREATE TABLE Prop3 (Id LONG, Name TEXT(40) DEFAULT 'unnamed' CONSTRAINT uqProp3 UNIQUE, Enabled BIT DEFAULT TRUE, " +
                "Tier LONG DEFAULT 3)",
            "CREATE TABLE Prop4 (Id LONG, A TEXT(40) DEFAULT 'unnamed' NOT NULL, B LONG DEFAULT 3 NOT NULL, C TEXT(10) NOT NULL, " +
                "D LONG DEFAULT 4)",
            "CREATE TABLE Prop5 (Id LONG NOT NULL, A TEXT(40) NOT NULL, B LONG NOT NULL, C TEXT(10) NOT NULL, D LONG NOT NULL, " +
                "E LONG NOT NULL)",
            "CREATE TABLE Prop6 (Id LONG CONSTRAINT pkProp6 PRIMARY KEY, A TEXT(10) NOT NULL CONSTRAINT uqProp6 UNIQUE, " +
                "B LONG DEFAULT 1, C TEXT(20) DEFAULT 'a longer default value')",
            "CREATE TABLE [Order] ([Select] LONG CONSTRAINT pkOrder PRIMARY KEY, [From] TEXT(10), [Where] DATETIME, [Money] CURRENCY)",
            "INSERT INTO [Order] VALUES (1, 'here', #2025-02-03#, 12.5)",
            "UPDATE [Order] SET [From] = 'there', [Money] = [Money] * 3 WHERE [Select] = 1",
        ]);

        // Make-table queries, a table emptied and refilled, a primary key replaced, and more retypes.
        s.AddRange(
        [
            "SELECT * INTO CustomerCopy FROM Customer",
            "SELECT Code, Price INTO Cheap FROM Product WHERE Price < 50",
            "SELECT City, COUNT(*) AS N, SUM(Credit) AS Total INTO CityTotals FROM Customer GROUP BY City",
            "DROP TABLE Cheap",
            "DELETE FROM CustomerCopy WHERE Id > 0",
            "INSERT INTO CustomerCopy (Name, City, Joined, Credit, Active) SELECT Name, City, Joined, Credit, Active FROM Customer WHERE Id < 4",
            "ALTER TABLE OrderLine DROP CONSTRAINT pkOrderLine",
            "ALTER TABLE OrderLine ADD CONSTRAINT pkOrderLineAgain PRIMARY KEY (OrderId, LineNo)",
            "CREATE INDEX ixAuditSeverity ON Audit (Severity, LoggedOn DESC)",
            "CREATE UNIQUE INDEX ixOrdersRef ON Orders (Ref)",
            "ALTER TABLE Audit ALTER COLUMN Charge DOUBLE",
            "ALTER TABLE Audit ALTER COLUMN Severity TEXT(5)",
            "ALTER TABLE Audit ALTER COLUMN Detail MEMO",
            "ALTER TABLE Audit ALTER COLUMN LoggedOn DOUBLE",
            "UPDATE Customer SET Joined = DateAdd('m', 1, Joined) WHERE Joined IS NOT NULL",
            "UPDATE Product SET Title = Title & ' (' & Code & ')'",
            "UPDATE Customer SET City = UCase(City), Email = NULL WHERE Id IN (SELECT CustomerId FROM Orders WHERE Total > 500)",
            "DELETE FROM OrderLine WHERE ProductCode NOT IN (SELECT Code FROM Product)",
        ]);

        // Every ordinary type in one table, an AutoNumber with its own seed and step, and compressed text.
        s.AddRange(
        [
            "CREATE TABLE Kinds (Id LONG, Small SHORT, Tiny BYTE, Real4 REAL, Cash CURRENCY, [Exact] DECIMAL(28,10), Flag BIT, " +
                "Moment DATETIME, Ident GUID, Raw BINARY(16), Var VARBINARY(50), Fixed CHAR(5), Packed TEXT(40) WITH COMPRESSION)",
            "INSERT INTO Kinds VALUES (1, -32768, 255, 1.5, 123.4567, -123456789.0123456789, TRUE, #1899-12-30#, " +
                "{guid {00000001-0002-0003-0405-060708090A0B}}, 0x00FF00FF00FF00FF00FF00FF00FF00FF, 0x0102, 'ab', 'plain latin text')",
            "INSERT INTO Kinds (Id) VALUES (2)",
            "INSERT INTO Kinds (Id, Small, Real4, Cash, [Exact], Moment, Packed) VALUES (3, 32767, -3.4E38, -0.0001, 0.0000000001, " +
                "#9999-12-31 23:59:59#, 'ünïcödé — not latin')",
            "UPDATE Kinds SET [Exact] = [Exact] * -1, Cash = Cash * 2 WHERE Id <> 1",
            "UPDATE Kinds SET Fixed = 'abcde', Var = 0x0A0B0C0D, Packed = 'now packed' WHERE Id = 2",
            "CREATE TABLE Seq (Id AUTOINCREMENT(10, 5) CONSTRAINT pkSeq PRIMARY KEY, Label TEXT(10))",
            "INSERT INTO Seq (Label) VALUES ('a')",
            "INSERT INTO Seq (Label) VALUES ('b')",
            "INSERT INTO Seq (Label) VALUES ('c')",
            "DELETE FROM Seq WHERE Id = 15",
            "INSERT INTO Seq (Label) VALUES ('d')",
            "SELECT * INTO SeqCopy FROM Seq",
            "SELECT OrderId, CustomerId * 2 AS Doubled, Total * 1.5 AS Scaled, Placed + 1 AS NextDay, Total > 100 AS Big, " +
                "CustomerId & '' AS AsText, Ref AS SameRef INTO OrderCalc FROM Orders",
            "SELECT CByte(Rating) AS B1, CInt(Stock) AS I2, CLng(Stock) AS L4, CSng(Price) AS S4, CDbl(Price) AS D8, " +
                "CCur(Price) AS C8, CDate(Price) AS T8, CBool(Stock) AS Y1, Price > 10 AS Cmp, Price AS Raw, Code & '' AS Txt " +
                "INTO Casts FROM Product",
            "SELECT Total * 2 AS Dec4, Total / 3 AS DecDiv, CCur(Total) * 2 AS Cur2 INTO MoneyCalc FROM Orders",
            "CREATE TABLE Dz (A DECIMAL(10,2), M CURRENCY)",
            "INSERT INTO Dz VALUES (12.34, 5.5)",
            "SELECT A * 2 AS A2, A / 3 AS A3, A * 1.5 AS A4, M * 2 AS M2, SUM(M) AS M3 INTO DzCalc FROM Dz GROUP BY A, M",
        ]);

        // Every row of a table deleted: with no WHERE, through a WHERE that every row meets, over many pages and
        // two indexes, and over a keyed AutoNumber table; then a table dropped that other objects named.
        s.AddRange(
        [
            "DELETE FROM Digits WHERE D >= 0",
            "DELETE FROM Bulk",
            "DELETE FROM Seq",
            "INSERT INTO Seq (Label) VALUES ('after')",
            "DROP TABLE Child",
            "DROP TABLE Setting",
        ]);

        // A whole table deleted with no WHERE, over each shape: long values chained and packed, no index at all,
        // the DELETE * form, a definition on continuation pages, a table already empty, a composite key, the child
        // and then the parent of a relationship, a cascading parent, and a WHERE that is always true.
        s.AddRange(
        [
            "DELETE FROM Doc",
            "INSERT INTO Doc (Id, Body) VALUES (5, String(3000, 'g'))",
            "DELETE FROM Slack",
            "DELETE * FROM Kinds",
            "DELETE FROM Wide",
            "DELETE FROM Digits",
            "DELETE FROM OrderLine",
            "DELETE FROM Orders",
            "DELETE FROM Customer",
            "INSERT INTO Customer (Name, City) VALUES ('Fresh', 'Rome')",
            "CREATE TABLE Parent (Id LONG CONSTRAINT pkParent PRIMARY KEY, Name TEXT(20))",
            "CREATE TABLE Kid (Id LONG CONSTRAINT pkKid PRIMARY KEY, ParentId LONG, " +
                "CONSTRAINT fkKidParent FOREIGN KEY (ParentId) REFERENCES Parent (Id) ON DELETE CASCADE)",
            "INSERT INTO Parent VALUES (1, 'one')",
            "INSERT INTO Parent VALUES (2, 'two')",
            "INSERT INTO Kid VALUES (10, 1)",
            "INSERT INTO Kid VALUES (20, 2)",
            "INSERT INTO Kid VALUES (21, 2)",
            "DELETE FROM Parent",
            "CREATE TABLE Checked (Id LONG CONSTRAINT pkChecked PRIMARY KEY, Amount LONG, Body MEMO, " +
                "CONSTRAINT ckChecked CHECK (Amount >= 0))",
            "INSERT INTO Checked VALUES (1, 10, String(3000, 'a'))",
            "INSERT INTO Checked VALUES (2, 20, 'short')",
            "INSERT INTO Checked VALUES (3, 30, String(200, 'c'))",
            "DELETE FROM Checked",
            "DELETE FROM SeqCopy WHERE 1 = 1",
            "DELETE FROM CityTotals",
            "DELETE FROM Audit",
        ]);

        // Tables refilled through INSERT … SELECT, then multi-table UPDATE and DELETE through joins; constraints and
        // indexes added and dropped over rows; long values grown and nulled; columns retyped, dropped and added
        // with a default; make-tables with DISTINCT and TOP; reserved names; a fresh AutoNumber memo table; two UNION
    // procedures; and indexes sharing one data block with another index or a relationship, then dropped.
        s.AddRange(
        [
            "INSERT INTO Orders (OrderId, CustomerId, Placed, Total) SELECT 2000 + Id, Id, Joined, Credit FROM Customer",
            "INSERT INTO Customer (Name, City, Credit) VALUES ('Second', 'Oslo', 20)",
            "INSERT INTO Orders (OrderId, CustomerId, Total) SELECT 3000 + Id, Id, 5 FROM Customer",
            "INSERT INTO OrderLine (OrderId, LineNo, ProductCode, Qty) SELECT o.OrderId, p.Stock, p.Code, 3 FROM Orders AS o, Product AS p WHERE p.Price < 40",
            "UPDATE Orders AS o INNER JOIN Customer AS c ON o.CustomerId = c.Id SET o.Total = o.Total + 1, c.City = 'Turin'",
            "DELETE o.* FROM OrderLine AS o INNER JOIN Product AS p ON o.ProductCode = p.Code WHERE p.Price < 20",
            "ALTER TABLE Product ADD COLUMN Discontinued BIT",
            "UPDATE Product SET Discontinued = TRUE WHERE Stock < 30",
            "CREATE UNIQUE INDEX ixProductCodeTitle ON Product (Code, Title DESC)",
            "ALTER TABLE Product ADD CONSTRAINT uqProductWeight UNIQUE (Weight)",
            "ALTER TABLE Product DROP CONSTRAINT uqProductWeight",
            "UPDATE Product SET Notes = String(3000, 'n') WHERE Code = 'P003'",
            "UPDATE Product SET Notes = NULL WHERE Code = 'P004'",
            "INSERT INTO Kinds (Id, Packed) VALUES (10, 'ünïcödé — 中文')",
            "UPDATE Kinds SET Packed = Packed & ' more' WHERE Id = 10",
            "ALTER TABLE Kinds ALTER COLUMN Small LONG",
            "ALTER TABLE Kinds DROP COLUMN Raw",
            "CREATE INDEX ixKindsMoment ON Kinds (Moment DESC, Id)",
            "INSERT INTO Kinds (Id, Moment) VALUES (11, #2001-02-03 04:05:06#)",
            "DROP INDEX ixKindsMoment ON Kinds",
            "ALTER TABLE Seq ADD COLUMN Extra LONG DEFAULT 7",
            "INSERT INTO Seq (Label) VALUES ('def')",
            "SELECT DISTINCT City INTO CityList FROM Customer",
            "SELECT TOP 2 * INTO TopProducts FROM Product ORDER BY Price DESC",
            "INSERT INTO [Order] ([Select], [From]) VALUES (2, 'there')",
            "UPDATE [Order] SET [Where] = #2030-01-01# WHERE [Select] = 2",
            "DELETE FROM [Order] WHERE [Select] = 1",
            "DROP TABLE [Order]",
            "CREATE TABLE Notes2 (Id COUNTER CONSTRAINT pkNotes2 PRIMARY KEY, Body LONGTEXT, Tag TEXT(10) DEFAULT 'none' NOT NULL)",
            "INSERT INTO Notes2 (Body) VALUES ('first')",
            "INSERT INTO Notes2 (Body, Tag) VALUES (String(200, 'x'), 'long')",
            "UPDATE Notes2 SET Body = Body & Body WHERE Id = 2",
            "DELETE FROM Notes2 WHERE Id = 1",
            "INSERT INTO Notes2 (Body) SELECT Name FROM Customer",
            "CREATE PROCEDURE pUnion (who TEXT(20)) AS SELECT Name FROM Customer WHERE Name <> who " +
                "UNION SELECT Code FROM Product ORDER BY Name DESC",
            "CREATE PROCEDURE pUnionAll AS TABLE Seq UNION ALL SELECT Id, Label, Extra FROM Seq WITH OWNERACCESS OPTION",
            "CREATE INDEX ixSeqLabelA ON Seq (Label)",
            "CREATE INDEX ixSeqLabelB ON Seq (Label)",
            "DROP INDEX ixSeqLabelA ON Seq",
            "CREATE INDEX ixOrdersCustomer ON Orders (CustomerId)",
            "DROP INDEX ixOrdersCustomer ON Orders",
            "CREATE INDEX ixOrdersCustomer2 ON Orders (CustomerId)",
            "ALTER TABLE Orders DROP CONSTRAINT fkOrderCustomer",
            "CREATE TABLE ShP (Id LONG CONSTRAINT pkShP PRIMARY KEY)",
            "CREATE TABLE ShC (Id LONG, P LONG)",
            "CREATE INDEX ixShCP ON ShC (P)",
            "ALTER TABLE ShC ADD CONSTRAINT fkShC FOREIGN KEY (P) REFERENCES ShP (Id)",
            "DROP TABLE ShC",
            "CREATE TABLE ShU (K LONG, C TEXT(10), CONSTRAINT u1 UNIQUE (C), CONSTRAINT u2 UNIQUE (C))",
            "CREATE TABLE ShU2 (C TEXT(10) CONSTRAINT u3 UNIQUE, M MEMO, CONSTRAINT u4 UNIQUE (C))",
            "CREATE TABLE ShF (K LONG, P LONG, CONSTRAINT r1 FOREIGN KEY (P) REFERENCES ShP (Id), " +
                "CONSTRAINT r2 FOREIGN KEY (P) REFERENCES Seq (Id))",
            "INSERT INTO ShU (K, C) VALUES (1, 'a')",
            "DROP INDEX u1 ON ShU",
        ]);

        // A CHECK's place among the table-level elements written after the columns' properties: before and after a
        // table-level UNIQUE, and ahead of a table-level primary key. Then a definition written onto a page a dropped
        // table released — each step is its own session, so the drop's pages are back in the free map by the create.
        s.AddRange(
        [
            "CREATE TABLE CkBefore (Id LONG, Code TEXT(10) DEFAULT 'x', CONSTRAINT ckBefore CHECK (Id > 0), CONSTRAINT uqBefore UNIQUE (Code))",
            "CREATE TABLE CkAfter (Id LONG, Code TEXT(10) DEFAULT 'x', CONSTRAINT uqAfter UNIQUE (Code), CONSTRAINT ckAfter CHECK (Id > 0))",
            "CREATE TABLE CkKey (Id LONG, Code TEXT(10), CONSTRAINT ckKey CHECK (Id > 0), CONSTRAINT pkCkKey PRIMARY KEY (Id))",
            "CREATE TABLE Reuse1 (" + string.Join(", ", Enumerable.Range(1, 30).Select(i => $"Wide{i:D2} TEXT(50)")) + ")",
            "DROP TABLE Reuse1",
            "CREATE TABLE Reuse2 (Id LONG)",
        ]);

        // A backfill into leaves an ascending load packed: the odd keys go in after the even ones, in a scrambled
        // order (7919 is coprime with 1000, so every n is visited once). Then a no-WHERE DELETE of a table whose
        // one-leaf index root is compressed — 500 equal keys fill it, which is what makes ACE compress it.
        s.Add("CREATE TABLE Tens (D BYTE)");
        for (int d = 0; d <= 9; d++) s.Add($"INSERT INTO Tens (D) VALUES ({d})");
        s.AddRange(
        [
            "CREATE TABLE Backfill (K LONG)",
            "CREATE INDEX ixBackfill ON Backfill (K)",
            "INSERT INTO Backfill (K) SELECT 2 * (a.D * 100 + b.D * 10 + c.D) + 2 FROM Tens AS a, Tens AS b, Tens AS c ORDER BY 1",
            "INSERT INTO Backfill (K) SELECT 2 * (a.D * 100 + b.D * 10 + c.D) + 1 FROM Tens AS a, Tens AS b, Tens AS c " +
                "ORDER BY ((a.D * 100 + b.D * 10 + c.D) * 7919) MOD 1000",
            "CREATE TABLE Prefixed (K TEXT(100))",
            "CREATE INDEX ixPrefixed ON Prefixed (K)",
            "INSERT INTO Prefixed (K) SELECT 'same' FROM Tens AS a, Tens AS b, Tens AS c WHERE a.D < 5",
            "DELETE FROM Prefixed",
        ]);

        // A compressed single-page value goes on a page only if its UNCOMPRESSED bytes fit. A second value swept
        // across the boundary beside a first, each pair in a table of its own so every one starts from the same
        // page, settles whether the directory entry counts on top of those bytes.
        // Then the same pairs where the page's room after the second value crosses the free-pages map's 258-byte
        // cut-off when measured by the uncompressed bytes (n = 1009 | 1010), and single values on a fresh page
        // whose uncompressed image leaves more or less than that.
        foreach (int n in Enumerable.Range(1130, 16).Concat(Enumerable.Range(1006, 7)))
        {
            s.Add($"CREATE TABLE Cmp{n} (Id LONG, M MEMO WITH COMPRESSION)");
            s.Add($"INSERT INTO Cmp{n} (Id, M) VALUES (1, String(1800, 'e'))");
            s.Add($"INSERT INTO Cmp{n} (Id, M) VALUES (2, String({n}, 'f'))");
        }
        foreach (int n in new[] { 1900, 1911, 1912, 1950 })
        {
            s.Add($"CREATE TABLE CmpOne{n} (Id LONG, M MEMO WITH COMPRESSION)");
            s.Add($"INSERT INTO CmpOne{n} (Id, M) VALUES (1, String({n}, 'g'))");
        }

        // The page a table keeps through a row-by-row delete of every row, where its first page and its lowest
        // part company: Kept's first page sits above PadA's, PadA is dropped, and Kept grows into the holes below.
        // Then a free-pages map whose window has slid to 1024 with the append tail, asked to record a page below
        // 512 when a row is deleted there.
        s.AddRange(
        [
            "CREATE TABLE PadA (Id LONG, Body TEXT(255))",
            "INSERT INTO PadA (Id, Body) SELECT a.D * 10 + b.D, String(250, 'p') FROM Tens AS a, Tens AS b ORDER BY 1",
            "CREATE TABLE Kept (Id LONG, Body TEXT(255))",
            "INSERT INTO Kept (Id, Body) VALUES (0, String(250, 'k'))",
            "DROP TABLE PadA",
            "INSERT INTO Kept (Id, Body) SELECT 1 + a.D * 10 + b.D, String(250, 'k') FROM Tens AS a, Tens AS b ORDER BY 1",
            "DELETE FROM Kept WHERE Id >= 0",
            "CREATE TABLE WinMap (Id LONG, A TEXT(255), B TEXT(255), C TEXT(255))",
            "INSERT INTO WinMap (Id, A, B, C) SELECT a.D * 100 + b.D * 10 + c.D, String(255, 'a'), String(255, 'b'), " +
                "String(255, 'c') FROM Tens AS a, Tens AS b, Tens AS c ORDER BY 1",
            "INSERT INTO WinMap (Id, A, B, C) SELECT 1000 + a.D * 100 + b.D * 10 + c.D, String(255, 'a'), String(255, 'b'), " +
                "String(255, 'c') FROM Tens AS a, Tens AS b, Tens AS c WHERE a.D < 6 ORDER BY 1",
            "DELETE FROM WinMap WHERE Id = 5",
        ]);

        // A new table whose pages all lie past the first 512 — its data, index and long-value maps each start out
        // empty at page 0 and are re-based on their first page — grown until its free maps move on.
        s.AddRange(
        [
            "CREATE TABLE Late (Id LONG, W1 TEXT(255), W2 TEXT(255), M MEMO)",
            "CREATE INDEX ixLate ON Late (Id)",
            "INSERT INTO Late (Id, W1, W2, M) VALUES (0, String(255, 'a'), String(255, 'b'), String(100, 'm'))",
            "INSERT INTO Late (Id, W1, W2, M) SELECT 1 + a.D * 100 + b.D * 10 + c.D, String(255, 'a'), String(255, 'b'), " +
                "String(100, 'm') FROM Tens AS a, Tens AS b, Tens AS c ORDER BY 1",
            "INSERT INTO Late (Id, W1, W2, M) SELECT 1001 + a.D * 100 + b.D * 10 + c.D, String(255, 'a'), String(255, 'b'), " +
                "String(100, 'm') FROM Tens AS a, Tens AS b, Tens AS c WHERE a.D < 6 ORDER BY 1",
        ]);

        // A fresh data page joins the free map only if its first row leaves room for another as large: rows of about
        // 2,015 and 2,055 bytes, either side of that edge.
        foreach (int chars in new[] { 1000, 1020 })
        {
            s.Add($"CREATE TABLE Half{chars} (W1 TEXT(255), W2 TEXT(255), W3 TEXT(255), W4 TEXT(255))");
            string row = $"INSERT INTO Half{chars} VALUES (String(255, 'a'), String(255, 'b'), String(255, 'c'), String({chars - 765}, 'd'))";
            s.Add(row);
            s.Add(row);
        }

        return [.. s];
    }

    /// <summary><paramref name="bytes"/> bytes as hex, a repeating pattern seeded by <paramref name="seed"/>.</summary>
    private static string Hex(int bytes, int seed) =>
        Convert.ToHexString([.. Enumerable.Range(0, bytes).Select(i => (byte)((i * 7 + seed) % 251))]);

    private static string Notes(int i) => string.Concat(Enumerable.Repeat($"Note {i}. ", i * 12));

    /// <summary>Statement 118's bulk insert cut to every row count from 1 to 1,000, each on a fresh copy of the corpus
    /// file after statement 117, through ACE and through LibRed: the pages each owner gains from one row count to the
    /// next are that row's allocations, in order, so every placement decision of the statement is logged with the row
    /// that made it.</summary>
    [Theory(Explicit = true)]
    [InlineData(120, -2)]
    [InlineData(150, -2)]
    [InlineData(200, -2)]
    [InlineData(150, -1)]
    [InlineData(150, 2)]
    [InlineData(150, 4)]
    public void Bulk_insert_page_timeline(int payloadLength, int filler)
    {
        string? origin = CreateThroughDao();
        Assert.SkipWhen(origin is null, "DAO is not registered in this bitness.");
        try
        {
            string[] statements = Statements();
            // -2: statements 1–117 in one session; otherwise each in its own, as the step-by-step run applies them, then
            // a filler table of that many one-page rows (none for -1), as Bulk_insert_alignment_map builds its starts.
            if (filler == -2) RunThroughAce(origin, statements[..117]);
            else
            {
                foreach (string statement in statements[..117]) RunThroughAce(origin, [statement]);
                if (filler >= 0)
                    RunThroughAce(origin, ["CREATE TABLE Filler (Id LONG, W1 TEXT(255), W2 TEXT(255), W3 TEXT(255), W4 TEXT(255), W5 TEXT(255), W6 TEXT(255), W7 TEXT(255))",
                        .. Enumerable.Range(0, filler).Select(i => $"INSERT INTO Filler VALUES ({i}, String(255,'a'), String(255,'b'), " +
                            "String(255,'c'), String(255,'d'), String(255,'e'), String(255,'f'), String(255,'g'))")]);
            }
            output.WriteLine($"before statement 118: {PageGroupProbeTest.Runs(origin)}");
            string bulk = statements[117].Replace("String(150, 'p')", $"String({payloadLength}, 'p')", StringComparison.Ordinal);
            var seen = new Dictionary<string, Dictionary<string, HashSet<int>>> { ["ACE"] = [], ["LibRed"] = [] };
            for (int rows = 1; rows <= 1000; rows++)
            {
                string sql = bulk.Replace("ORDER BY 1", $"WHERE a.D * 100 + b.D * 10 + c.D < {rows} ORDER BY 1", StringComparison.Ordinal);
                var events = new List<string>();
                foreach (string engine in new[] { "ACE", "LibRed" })
                {
                    string copy = TemporaryDatabase.CopyPath(origin, "timeline-");
                    try
                    {
                        if (engine == "ACE") Assert.Null(RunThroughAce(copy, [sql])[0]);
                        else Assert.Null(RunThroughLibRed(copy, [sql])[0]);
                        using var database = JetDatabase.Open(copy, readOnly: true);
                        Table table = database.OpenTable("Bulk");
                        var owners = new List<(string Name, IEnumerable<int> Pages)> { ("data", table.UsageMap.DataPages()) };
                        owners.AddRange(table.Definition.RealIndexes.Select(i =>
                            (i.Name, table.UsageMap.PagesInMap(i.UsageMap.Row, i.UsageMap.Page))));
                        var parts = new List<string>();
                        foreach ((string name, IEnumerable<int> pages) in owners)
                        {
                            HashSet<int> before = seen[engine].TryGetValue(name, out var known) ? known : [];
                            int[] added = [.. pages.Where(p => !before.Contains(p)).Order()];
                            seen[engine][name] = [.. pages];
                            // An index page's siblings name the page it was split from.
                            string Siblings(int page)
                            {
                                if (name == "data") return "";
                                ReadOnlySpan<byte> bytes = database.Channel.ReadPage(page).Span;
                                return $"({(PageHeader.ReadType(bytes) == PageType.LeafIndexPage ? "leaf" : "node")} " +
                                    $"{BinaryPrimitives.ReadInt32LittleEndian(bytes[database.Format.IndexPrevPageOffset..])}<>" +
                                    $"{BinaryPrimitives.ReadInt32LittleEndian(bytes[database.Format.IndexNextPageOffset..])})";
                            }
                            if (added.Length > 0) parts.Add($"{name} +{string.Join(",", added.Select(p => p + Siblings(p)))}");
                        }
                        if (parts.Count > 0) events.Add($"{engine} {string.Join("; ", parts)}");
                    }
                    finally { TemporaryDatabase.Delete(copy); }
                }
                if (events.Count > 0) output.WriteLine($"row {rows}: {string.Join(" | ", events)}");
            }
        }
        finally
        {
            OleDbConnection.ReleaseObjectPool();
            TemporaryDatabase.Delete(origin);
        }
    }

    /// <summary>A controlled bulk insert: on a fresh database, one statement appends rows in ascending key order to
    /// <c>Indexed (K TEXT(200), P TEXT(n))</c> with an index on K — so every index split is at the rightmost leaf and
    /// the index entries are the same whatever <paramref name="padding"/> is. The padding, not indexed, only changes
    /// how often the data needs a page. Each row's allocations through ACE and LibRed, the first disagreement marked.</summary>
    [Theory(Explicit = true)]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(200)]
    public void Indexed_append_timeline(int padding)
    {
        string? origin = CreateThroughDao();
        Assert.SkipWhen(origin is null, "DAO is not registered in this bitness.");
        try
        {
            RunThroughAce(origin, ["CREATE TABLE Digits (D BYTE)", .. Enumerable.Range(0, 10).Select(d => $"INSERT INTO Digits (D) VALUES ({d})"),
                "CREATE TABLE Source (Id LONG CONSTRAINT pkSource PRIMARY KEY)",
                "INSERT INTO Source (Id) SELECT a.D * 100 + b.D * 10 + c.D FROM Digits AS a, Digits AS b, Digits AS c ORDER BY 1",
                $"CREATE TABLE Indexed (K TEXT(200), P TEXT({Math.Max(padding, 1)}))", "CREATE INDEX ixK ON Indexed (K)"]);
            var seen = new Dictionary<string, Dictionary<string, HashSet<int>>> { ["ACE"] = [], ["LibRed"] = [] };
            bool parted = false;
            for (int rows = 1; rows <= 1000; rows++)
            {
                string sql = $"INSERT INTO Indexed (K, P) SELECT Right('0000' & Id, 4) & String(180, 'x'), String({padding}, 'q') " +
                    $"FROM Source WHERE Id < {rows} ORDER BY Id";
                var events = new Dictionary<string, string>();
                foreach (string engine in new[] { "ACE", "LibRed" })
                {
                    string copy = TemporaryDatabase.CopyPath(origin, "append-");
                    try
                    {
                        Assert.Null((engine == "ACE" ? RunThroughAce(copy, [sql]) : RunThroughLibRed(copy, [sql]))[0]);
                        using var database = JetDatabase.Open(copy, readOnly: true);
                        Table table = database.OpenTable("Indexed");
                        var owners = new List<(string Name, IEnumerable<int> Pages)> { ("data", table.UsageMap.DataPages()) };
                        owners.AddRange(table.Definition.RealIndexes.Select(i => ("ix", table.UsageMap.PagesInMap(i.UsageMap.Row, i.UsageMap.Page))));
                        var parts = new List<string>();
                        foreach ((string name, IEnumerable<int> pages) in owners)
                        {
                            HashSet<int> before = seen[engine].TryGetValue(name, out var known) ? known : [];
                            int[] added = [.. pages.Where(p => !before.Contains(p)).Order()];
                            seen[engine][name] = [.. pages];
                            // An index page's type and siblings: a new leaf's previous sibling is the leaf it split from.
                            string Shape(int page)
                            {
                                if (name == "data") return "";
                                ReadOnlySpan<byte> bytes = database.Channel.ReadPage(page).Span;
                                return $"({(PageHeader.ReadType(bytes) == PageType.LeafIndexPage ? "leaf" : "node")} " +
                                    $"{BinaryPrimitives.ReadInt32LittleEndian(bytes[database.Format.IndexPrevPageOffset..])}<>" +
                                    $"{BinaryPrimitives.ReadInt32LittleEndian(bytes[database.Format.IndexNextPageOffset..])})";
                            }
                            if (added.Length > 0) parts.Add($"{name} +{string.Join(",", added.Select(p => p + Shape(p)))}");
                        }
                        events[engine] = string.Join("; ", parts);
                    }
                    finally { TemporaryDatabase.Delete(copy); }
                }
                bool differ = events["ACE"] != events["LibRed"];
                if (events["ACE"] != "" || events["LibRed"] != "")
                    output.WriteLine($"{(differ && !parted ? "* " : "  ")}row {rows}: ACE {events["ACE"]} | LibRed {events["LibRed"]}");
                parted |= differ;
            }
        }
        finally
        {
            OleDbConnection.ReleaseObjectPool();
            TemporaryDatabase.Delete(origin);
        }
    }

    /// <summary>The scaffolding test's two indexes over one column, created by ACE and by LibRed: each file's
    /// real (data-block) and logical index counts, and the names each engine's TDEF gives them.</summary>
    [Fact(Explicit = true)]
    public void Two_indexes_on_one_column()
    {
        string? origin = CreateThroughDao();
        Assert.SkipWhen(origin is null, "DAO is not registered in this bitness.");
        string[] ddl =
        [
            "CREATE TABLE IndexTable (Id int, IndexProperty int)",
            "CREATE INDEX IX_One on IndexTable ( IndexProperty )",
            "CREATE INDEX IX_Two on IndexTable ( IndexProperty )",
        ];
        string ace = TemporaryDatabase.CopyPath(origin!, "twoix-ace-");
        string libred = TemporaryDatabase.CopyPath(origin!, "twoix-libred-");
        try
        {
            foreach (string sql in ddl) Assert.Null(RunThroughAce(ace, [sql])[0]);
            foreach (string sql in ddl) Assert.Null(RunThroughLibRed(libred, [sql])[0]);
            foreach ((string engine, string path) in new[] { ("ACE", ace), ("LibRed", libred) })
            {
                using var database = JetDatabase.Open(path, readOnly: true);
                TableDefinition t = database.OpenTable("IndexTable").Definition;
                output.WriteLine($"{engine}: real {t.IndexCount}, logical {t.LogicalIndexCount}; " +
                    $"data blocks [{string.Join("; ", t.Indexes.Select(i => $"#{i.RealIndexOrdinal} {i.Name} root {i.RootPage}"))}]; " +
                    $"logical [{string.Join("; ", t.LogicalIndexes.Select(l => $"{l.Name} -> #{l.RealIndexOrdinal}"))}]");
            }
        }
        finally
        {
            OleDbConnection.ReleaseObjectPool();
            foreach (string path in new[] { origin!, ace, libred }) TemporaryDatabase.Delete(path);
        }
    }

    /// <summary>The indexes ACE lists for Northwind's tables whose named indexes share data blocks.</summary>
    [Theory(Explicit = true)]
    [InlineData("Order Details")]
    [InlineData("Orders")]
    [InlineData("Products")]
    public void Shared_block_indexes_as_ACE_lists_them(string table)
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "od-indexes-");
        try
        {
            using var connection = AceTestDatabase.Open(path);
            System.Data.DataTable indexes = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Indexes, [null, null, null, null, table])!;
            foreach (string name in indexes.Rows.Cast<System.Data.DataRow>().Select(r => $"{r["INDEX_NAME"]} [{r["COLUMN_NAME"]}] pk={r["PRIMARY_KEY"]}").Distinct())
                output.WriteLine(name);
        }
        finally
        {
            OleDbConnection.ReleaseObjectPool();
            TemporaryDatabase.Delete(path);
        }
    }

    /// <summary>A table whose owned map starts above a page ACE then gives it: Filler fills pages past the first 512,
    /// T is created after it (so its owned map is re-based above 512), Filler is dropped, and T's next rows take the
    /// pages it freed. T's owned-map record — type, start, length — and data pages, before and after.</summary>
    [Fact(Explicit = true)]
    public void Owned_map_given_a_page_below_its_start()
    {
        string? origin = CreateThroughDao();
        Assert.SkipWhen(origin is null, "DAO is not registered in this bitness.");
        const string Wide = "W1 TEXT(255), W2 TEXT(255), W3 TEXT(255), W4 TEXT(255), W5 TEXT(255), W6 TEXT(255), W7 TEXT(255)";
        const string WideValues = "String(255,'a'), String(255,'b'), String(255,'c'), String(255,'d'), String(255,'e'), String(255,'f'), String(255,'g')";
        try
        {
            RunThroughAce(origin, [$"CREATE TABLE Filler (Id LONG, {Wide})",
                .. Enumerable.Range(0, 560).Select(i => $"INSERT INTO Filler VALUES ({i}, {WideValues})"),
                $"CREATE TABLE T (Id LONG, {Wide})", $"INSERT INTO T VALUES (0, {WideValues})"]);
            Report("before the drop");
            RunThroughAce(origin, ["DROP TABLE Filler"]);
            RunThroughAce(origin, [.. Enumerable.Range(1, 3).Select(i => $"INSERT INTO T VALUES ({i}, {WideValues})")]);
            Report("after three more rows");
        }
        finally
        {
            OleDbConnection.ReleaseObjectPool();
            TemporaryDatabase.Delete(origin);
        }

        void Report(string when)
        {
            using var database = JetDatabase.Open(origin!, readOnly: true);
            Table table = database.OpenTable("T");
            (int row, int page) = database.Channel.ReadPageShared(table.Definition.DefinitionPage)
                .ReadRecordPointer(database.Format.TdefOwnedPagesOffset);
            var holder = new DataPage();
            holder.Read(database.Channel.ReadPageShared(page), database.Format);
            DataPage.RowSlot slot = holder.Rows[row];
            byte[] record = database.Channel.ReadPageShared(page).Span.Slice(slot.Offset, slot.Length).ToArray();
            UsageMapType type = UsageMap.RecordType(record);
            int start = type == UsageMapType.Inline ? UsageMap.StartPage(record, database.Format) : -1;
            output.WriteLine($"{when}: owned map {type}, start {start}, length {slot.Length}; " +
                $"data [{string.Join(",", table.UsageMap.DataPages())}]; file {database.Channel.PageCount}");
        }
    }

    /// <summary>Statement 118 on the chained-sessions starting file shifted by a filler table of 0–10 one-page rows (its
    /// own session): for each shift, whether ACE and LibRed end alike, and where they part the first row that differs
    /// and each engine's pages for that row.</summary>
    [Theory(Explicit = true)]
    [InlineData(150)]
    public void Bulk_insert_alignment_map(int payloadLength)
    {
        string? origin = CreateThroughDao();
        Assert.SkipWhen(origin is null, "DAO is not registered in this bitness.");
        try
        {
            string[] statements = Statements();
            foreach (string statement in statements[..117]) RunThroughAce(origin, [statement]);
            string bulk = statements[117].Replace("String(150, 'p')", $"String({payloadLength}, 'p')", StringComparison.Ordinal);
            string Cut(int rows) => bulk.Replace("ORDER BY 1", $"WHERE a.D * 100 + b.D * 10 + c.D < {rows} ORDER BY 1", StringComparison.Ordinal);
            const string Wide = "W1 TEXT(255), W2 TEXT(255), W3 TEXT(255), W4 TEXT(255), W5 TEXT(255), W6 TEXT(255), W7 TEXT(255)";
            const string WideValues = "String(255,'a'), String(255,'b'), String(255,'c'), String(255,'d'), String(255,'e'), String(255,'f'), String(255,'g')";

            for (int filler = -1; filler <= 10; filler++)
            {
                string start = TemporaryDatabase.CopyPath(origin, "align-");
                try
                {
                    if (filler >= 0)
                        RunThroughAce(start, [$"CREATE TABLE Filler (Id LONG, {Wide})",
                            .. Enumerable.Range(0, filler).Select(i => $"INSERT INTO Filler VALUES ({i}, {WideValues})")]);
                    int startPages;
                    using (var database = JetDatabase.Open(start, readOnly: true)) startPages = database.Channel.PageCount;

                    // Every page Bulk owns, per owner, and the file's length, after the statement cut to the given rows.
                    string Placement(string engine, int rows, out Dictionary<string, int[]> owners)
                    {
                        string copy = TemporaryDatabase.CopyPath(start, "align-run-");
                        try
                        {
                            Assert.Null((engine == "ACE" ? RunThroughAce(copy, [Cut(rows)]) : RunThroughLibRed(copy, [Cut(rows)]))[0]);
                            using var database = JetDatabase.Open(copy, readOnly: true);
                            Table table = database.OpenTable("Bulk");
                            owners = new() { ["data"] = [.. table.UsageMap.DataPages()] };
                            foreach (IndexDef index in table.Definition.RealIndexes)
                                owners[index.Name] = [.. table.UsageMap.PagesInMap(index.UsageMap.Row, index.UsageMap.Page)];
                            return $"{database.Channel.PageCount}|" + string.Join("|", owners.Select(o => $"{o.Key}:{string.Join(",", o.Value)}"));
                        }
                        finally { TemporaryDatabase.Delete(copy); }
                    }

                    bool Same(int rows) => Placement("ACE", rows, out _) == Placement("LibRed", rows, out _);
                    if (Same(1000)) { output.WriteLine($"filler {filler} (start {startPages} pages): identical"); continue; }
                    int low = 0, high = 1000; // Same(low) holds, Same(high) does not
                    while (high - low > 1)
                    {
                        int mid = (low + high) / 2;
                        if (Same(mid)) low = mid; else high = mid;
                    }
                    Placement("ACE", low, out var aceBefore);
                    Placement("ACE", high, out var aceAfter);
                    Placement("LibRed", high, out var libredAfter);
                    string Added(Dictionary<string, int[]> after) => string.Join("; ", after
                        .Select(o => (o.Key, New: o.Value.Except(aceBefore.TryGetValue(o.Key, out var b) ? b : []).ToArray()))
                        .Where(o => o.New.Length > 0).Select(o => $"{o.Key} +{string.Join(",", o.New)}"));
                    output.WriteLine($"filler {filler} (start {startPages} pages): first differs at row {high} — ACE {Added(aceAfter)} | LibRed {Added(libredAfter)}");
                    foreach ((string owner, int[] pages) in aceBefore)
                        output.WriteLine($"    before, {owner}: {string.Join(",", pages.Where(p => p >= startPages - 8))}");
                }
                finally { TemporaryDatabase.Delete(start); }
            }
        }
        finally
        {
            OleDbConnection.ReleaseObjectPool();
            TemporaryDatabase.Delete(origin);
        }
    }

    /// <summary>The file statement 118 starts from, built two ways — statements 1–117 in one ACE session, or each in a
    /// session of its own — with each one's free pages below its end and its page layout.</summary>
    [Fact(Explicit = true)]
    public void Bulk_insert_starting_files()
    {
        foreach (bool chained in new[] { false, true })
        {
            string? origin = CreateThroughDao();
            Assert.SkipWhen(origin is null, "DAO is not registered in this bitness.");
            try
            {
                string[] statements = Statements();
                if (chained)
                    foreach (string statement in statements[..117]) RunThroughAce(origin, [statement]);
                else RunThroughAce(origin, statements[..117]);
                using var database = JetDatabase.Open(origin, readOnly: true);
                Table bulk = database.OpenTable("Bulk");
                int end = database.Channel.PageCount;
                HashSet<int> free = PageGroupProbeTest.GlobalFree(bulk, int.MaxValue);
                output.WriteLine($"{(chained ? "chained" : "one session")}: {end} pages; free below the end " +
                    $"[{string.Join(",", free.Where(p => p < end).Order())}]; free from the end [{string.Join(",", free.Where(p => p >= end).Order().Take(12))}…]");
                output.WriteLine($"  {PageGroupProbeTest.Runs(origin)}");
            }
            finally { TemporaryDatabase.Delete(origin); }
        }
    }

    /// <summary>Statement 118 on the one-session starting file, which has free holes below its end, and on a twin with
    /// only those holes' free bits cleared: where each engine's index pages end up, and the pages each engine holds
    /// — taken while the session is open, free once it closes — after the first rows, around the data's first pages.</summary>
    [Theory(Explicit = true)]
    [InlineData(150)]
    public void Bulk_insert_holes_pair(int payloadLength)
    {
        string? origin = CreateThroughDao();
        Assert.SkipWhen(origin is null, "DAO is not registered in this bitness.");
        string filled = "";
        try
        {
            string[] statements = Statements();
            RunThroughAce(origin, statements[..117]);
            int[] holes;
            using (var database = JetDatabase.Open(origin, readOnly: true))
            {
                int end = database.Channel.PageCount;
                holes = [.. PageGroupProbeTest.GlobalFree(database.OpenTable("Bulk"), end).Order()];
            }
            filled = TemporaryDatabase.CopyPath(origin, "holes-filled-");
            using (var database = JetDatabase.Open(filled, readOnly: false))
            {
                (int row, int holderPage) = database.DefinitionPage.FreePagesMap;
                byte[] bytes = database.Channel.ReadPage(holderPage).Span.ToArray();
                var holder = new DataPage();
                holder.Read(database.Channel.ReadPageShared(holderPage), database.Format);
                DataPage.RowSlot slot = holder.Rows[row];
                Span<byte> record = bytes.AsSpan(slot.Offset, slot.Length);
                int start = UsageMap.StartPage(record, database.Format);
                foreach (int hole in holes) BitmapBits.Set(UsageMap.InlineBits(record, database.Format), hole - start, false);
                database.Channel.WritePage(holderPage, bytes);
            }
            output.WriteLine($"holes [{string.Join(",", holes)}]; the twin has them marked used");

            string bulk = statements[117].Replace("String(150, 'p')", $"String({payloadLength}, 'p')", StringComparison.Ordinal);
            string Cut(int rows) => bulk.Replace("ORDER BY 1", $"WHERE a.D * 100 + b.D * 10 + c.D < {rows} ORDER BY 1", StringComparison.Ordinal);
            foreach ((string name, string file) in new[] { ("holes", origin), ("filled", filled) })
            {
                foreach (string engine in new[] { "ACE", "LibRed" })
                {
                    string copy = TemporaryDatabase.CopyPath(file, "holes-run-");
                    try
                    {
                        Assert.Null((engine == "ACE" ? RunThroughAce(copy, [bulk]) : RunThroughLibRed(copy, [bulk]))[0]);
                        using var database = JetDatabase.Open(copy, readOnly: true);
                        Table table = database.OpenTable("Bulk");
                        output.WriteLine($"{name} {engine} 1,000 rows: {database.Channel.PageCount} pages; " + string.Join("; ",
                            table.Definition.RealIndexes.Select(i => $"{i.Name} [{string.Join(",", table.UsageMap.PagesInMap(i.UsageMap.Row, i.UsageMap.Page))}]")));
                    }
                    finally { TemporaryDatabase.Delete(copy); }
                }
                foreach (int rows in new[] { 1, 2, 10, 11, 19, 20, 28, 29, 37, 38 })
                {
                    var line = new List<string>();
                    foreach (string engine in new[] { "ACE", "LibRed" })
                    {
                        string copy = TemporaryDatabase.CopyPath(file, "holes-run-");
                        string open = TemporaryDatabase.CreatePath("holes-open-");
                        try
                        {
                            void Snapshot()
                            {
                                if (engine == "ACE") Thread.Sleep(3000);
                                using var source = new FileStream(copy, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                                using var target = File.Create(open);
                                source.CopyTo(target);
                            }
                            Assert.Null((engine == "ACE" ? RunThroughAce(copy, [Cut(rows)], Snapshot) : RunThroughLibRed(copy, [Cut(rows)], Snapshot))[0]);
                            OleDbConnection.ReleaseObjectPool();
                            HashSet<int> NotFree(string path)
                            {
                                using var database = JetDatabase.Open(path, readOnly: true);
                                HashSet<int> free = PageGroupProbeTest.GlobalFree(database.OpenTable("Bulk"), int.MaxValue);
                                return [.. Enumerable.Range(40, 160).Where(p => !free.Contains(p))];
                            }
                            int[] held = [.. NotFree(open).Except(NotFree(copy)).Order()];
                            using var closed = JetDatabase.Open(copy, readOnly: true);
                            line.Add($"{engine} held [{string.Join(",", held)}] data [{string.Join(",", closed.OpenTable("Bulk").UsageMap.DataPages())}]");
                        }
                        finally
                        {
                            TemporaryDatabase.Delete(copy);
                            TemporaryDatabase.Delete(open);
                        }
                    }
                    output.WriteLine($"{name} row {rows}: {string.Join(" | ", line)}");
                }
            }
        }
        finally
        {
            OleDbConnection.ReleaseObjectPool();
            TemporaryDatabase.Delete(origin);
            if (filled != "") TemporaryDatabase.Delete(filled);
        }
    }

    /// <summary>What ACE still holds after statement 118's bulk insert cut to <paramref name="rows"/> row counts — read
    /// from a copy taken with the session still open, after ACE's flush timeout, since a shared reservation goes back
    /// only at close. Pages taken in the global map but owned by none of Bulk's maps are held; listed with each
    /// owner's pages, around each label-index split of the timeline.</summary>
    [Theory(Explicit = true)]
    [InlineData(120, new[] { 369, 370, 414, 415, 459, 460, 545, 546, 590, 591, 602, 603, 634, 635, 679, 680, 724, 725, 769, 770, 814, 815 }, -2)]
    [InlineData(150, new[] { 500, 510, 520, 530, 540, 545, 546, 547, 590, 591, 602, 603 }, 2)]
    public void Bulk_insert_held_pages(int payloadLength, int[] rows, int filler)
    {
        string? origin = CreateThroughDao();
        Assert.SkipWhen(origin is null, "DAO is not registered in this bitness.");
        try
        {
            string[] statements = Statements();
            // -2: statements 1–117 in one session; otherwise each in its own, then a filler table of that many one-page
            // rows (none for -1), as Bulk_insert_alignment_map builds its starts.
            if (filler == -2) RunThroughAce(origin, statements[..117]);
            else
            {
                foreach (string statement in statements[..117]) RunThroughAce(origin, [statement]);
                if (filler >= 0)
                    RunThroughAce(origin, ["CREATE TABLE Filler (Id LONG, W1 TEXT(255), W2 TEXT(255), W3 TEXT(255), W4 TEXT(255), W5 TEXT(255), W6 TEXT(255), W7 TEXT(255))",
                        .. Enumerable.Range(0, filler).Select(i => $"INSERT INTO Filler VALUES ({i}, String(255,'a'), String(255,'b'), " +
                            "String(255,'c'), String(255,'d'), String(255,'e'), String(255,'f'), String(255,'g'))")]);
            }
            string bulk = statements[117].Replace("String(150, 'p')", $"String({payloadLength}, 'p')", StringComparison.Ordinal);
            foreach (int count in rows)
            {
                string sql = bulk.Replace("ORDER BY 1", $"WHERE a.D * 100 + b.D * 10 + c.D < {count} ORDER BY 1", StringComparison.Ordinal);
                string copy = TemporaryDatabase.CopyPath(origin, "held-");
                string open = TemporaryDatabase.CreatePath("held-open-");
                try
                {
                    Assert.Null(RunThroughAce(copy, [sql], () =>
                    {
                        Thread.Sleep(3000);
                        using var source = new FileStream(copy, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var target = File.Create(open);
                        source.CopyTo(target);
                    })[0]);
                    using var database = JetDatabase.Open(open, readOnly: true);
                    Table table = database.OpenTable("Bulk");
                    var owned = new Dictionary<string, int[]> { ["data"] = [.. table.UsageMap.DataPages().Where(p => p >= 112)] };
                    foreach (IndexDef index in table.Definition.RealIndexes)
                        owned[index.Name] = [.. table.UsageMap.PagesInMap(index.UsageMap.Row, index.UsageMap.Page).Where(p => p >= 112)];
                    HashSet<int> free = PageGroupProbeTest.GlobalFree(table, int.MaxValue);
                    int end = database.Channel.PageCount;
                    int[] held = [.. Enumerable.Range(112, end + 40 - 112).Where(p => !free.Contains(p) && !owned.Values.Any(o => o.Contains(p)))];
                    output.WriteLine($"row {count}: file {end}; held [{string.Join(",", held)}]; " +
                        string.Join("; ", owned.Select(o => $"{o.Key} [{string.Join(",", o.Value)}]")));
                }
                finally
                {
                    OleDbConnection.ReleaseObjectPool();
                    TemporaryDatabase.Delete(copy);
                    TemporaryDatabase.Delete(open);
                }
            }
        }
        finally { TemporaryDatabase.Delete(origin); }
    }

    /// <summary>The bulk insert of statement 118 run inside Access, its label computed by a VBA callback that copies the
    /// file before each sampled row, so the label index's and the data's pages can be read mid-statement; LibRed runs
    /// the same rows on a twin. The rows come from a keyed source table in Id order rather than through ORDER BY,
    /// which would evaluate every callback before the first insert.</summary>
    [Theory(Explicit = true)]
    [InlineData(120)]
    public void Bulk_insert_observed_inside_Access(int payloadLength)
    {
        Type? accessType = Type.GetTypeFromProgID("Access.Application");
        Assert.SkipWhen(accessType is null, "Access automation is unavailable.");
        string origin = TemporaryDatabase.CreatePath("bulk-inside-");
        string prefix = origin + "-inside-";
        string libred = "";
        int[] samples = [.. Enumerable.Range(585, 9)];
        dynamic access = Activator.CreateInstance(accessType)!;
        try
        {
            access.Visible = false;
            // Only this disposable automation instance opens our own generated callback module.
            access.AutomationSecurity = 1;
            access.NewCurrentDatabase(origin);
            dynamic module = access.VBE.ActiveVBProject.VBComponents.Add(1);
            module.Name = "BulkCallback";
            string path = prefix.Replace("\"", "\"\"", StringComparison.Ordinal);
            module.CodeModule.AddFromString($$"""
                Public Function BulkLabel(ByVal id As Long) As String
                    Dim f As Integer, g As Integer, bytes() As Byte
                    BulkLabel = "Bulk label number " & CStr(id) & " " & String$(30, "L")
                    Select Case id
                    Case {{string.Join(", ", samples)}}
                        DBEngine.Idle dbRefreshCache
                        f = FreeFile
                        Open CurrentProject.FullName For Binary Access Read Shared As #f
                        ReDim bytes(0 To LOF(f) - 1)
                        Get #f, 1, bytes
                        Close #f
                        g = FreeFile
                        Open "{{path}}" & CStr(id) & ".accdb" For Binary Access Write As #g
                        Put #g, 1, bytes
                        Close #g
                    End Select
                End Function
                Public Sub BulkCache()
                    DBEngine.SetOption dbMaxBufferSize, 64
                End Sub
                """);
            access.DoCmd.Save(5, "BulkCallback");
            access.Run("BulkLabel", -1);
            access.CloseCurrentDatabase();

            RunThroughAce(origin, [.. Statements()[..117],
                "CREATE TABLE Src (Id LONG CONSTRAINT pkSrc PRIMARY KEY)",
                "INSERT INTO Src (Id) SELECT a.D * 100 + b.D * 10 + c.D FROM Digits AS a, Digits AS b, Digits AS c ORDER BY 1"]);
            libred = TemporaryDatabase.CopyPath(origin, "bulk-inside-lib-");

            string Insert(string label) =>
                $"INSERT INTO Bulk (Id, Grp, Label, Payload, Amount) SELECT Id, Id \\ 100, {label}, String({payloadLength}, 'p'), " +
                "Id \\ 100 + ((Id \\ 10) MOD 10) * 0.5 FROM Src";
            access.OpenCurrentDatabase(origin, false);
            access.Run("BulkLabel", -1);
            // A small buffer, so the statement's pages reach the file while it runs (the final placement is the same).
            access.Run("BulkCache");
            access.CurrentDb().Execute(Insert("BulkLabel(Id)"), 128);
            access.CloseCurrentDatabase();
            using (var database = JetDatabase.Open(libred, readOnly: false))
                new QueryEngine(database).ExecuteNonQuery(Insert("'Bulk label number ' & Id & ' ' & String(30, 'L')"));

            foreach ((string name, string file) in samples.Select(s => ($"ACE before row {s}", prefix + s.ToString(CultureInfo.InvariantCulture) + ".accdb"))
                .Append(("ACE after", origin)).Append(("LibRed after", libred)))
            {
                if (!File.Exists(file)) { output.WriteLine($"{name}: no snapshot"); continue; }
                // The table's own maps are still cached mid-statement; the pages themselves say what they hold. (Not
                // for the end state, which the maps give, and whose file Access may still hold.)
                if (file != origin && file != libred) output.WriteLine($"{name}: pages {PageGroupProbeTest.Runs(file)}");
                using var snapshot = JetDatabase.Open(file, readOnly: true);
                Table bulk = snapshot.OpenTable("Bulk");
                IndexDef label = bulk.Definition.Indexes.Single(i => i.Name == "ixBulkLabel");
                HashSet<int> free = PageGroupProbeTest.GlobalFree(bulk, int.MaxValue);
                int low = 136, high = Math.Max(176, snapshot.Channel.PageCount);
                output.WriteLine($"{name}: {snapshot.Channel.PageCount} pages; data [{string.Join(",", bulk.UsageMap.DataPages().Where(p => p >= low))}]; " +
                    $"label index [{string.Join(",", bulk.UsageMap.PagesInMap(label.UsageMap.Row, label.UsageMap.Page).Where(p => p >= low))}]; " +
                    $"not free [{string.Join(",", Enumerable.Range(low, high - low).Where(p => !free.Contains(p)))}]");
            }
        }
        finally
        {
            access.Quit(2);
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject((object)access);
            TemporaryDatabase.Delete(origin);
            if (libred != "") TemporaryDatabase.Delete(libred);
            foreach (int sample in samples) TemporaryDatabase.Delete(prefix + sample.ToString(CultureInfo.InvariantCulture) + ".accdb");
        }
    }

    private static string Abbreviate(string statement) => statement.Length <= 90 ? statement : statement[..87] + "...";

    internal static string?[] RunThroughAce(string path, string[] statements, Action? beforeClose = null, bool dao = false)
    {
        var errors = new string?[statements.Length];
        if (dao)
        {
            // Through DAO, as Access runs SQL: OLE DB sees a complex column only as Jet 4 did, and leaves its values
            // behind when it deletes the row that owns them.
            object database = Invoke(AceTestDatabase.CreateDaoEngine()!, "OpenDatabase", path)!;
            try
            {
                for (int i = 0; i < statements.Length; i++)
                {
                    try { Invoke(database, "Execute", statements[i], 128); }   // dbFailOnError
                    catch (TargetInvocationException e) { errors[i] = e.InnerException?.Message ?? e.Message; }
                }
                beforeClose?.Invoke();
            }
            finally
            {
                Invoke(database, "Close");
                AceTestDatabase.ReleaseAbandonedComObjects();
            }
            return errors;
        }
        using OleDbConnection connection = AceTestDatabase.Open(path);
        for (int i = 0; i < statements.Length; i++)
        {
            try
            {
                using OleDbCommand command = connection.CreateCommand();
                command.CommandText = statements[i];
                command.ExecuteNonQuery();
            }
            catch (OleDbException e) { errors[i] = e.Message; }
        }
        beforeClose?.Invoke();
        return errors;
    }

    private static string?[] RunThroughLibRed(string path, string[] statements, Action? beforeClose = null)
    {
        var errors = new string?[statements.Length];
        using var database = JetDatabase.Open(path, readOnly: false);
        var engine = new QueryEngine(database);
        for (int i = 0; i < statements.Length; i++)
        {
            try { engine.ExecuteNonQuery(statements[i]); }
#pragma warning disable CA1031 // a probe records every refusal, whatever its type, and carries on
            catch (Exception e) { errors[i] = $"{e.GetType().Name}: {e.Message}"; }
#pragma warning restore CA1031
        }
        beforeClose?.Invoke();
        return errors;
    }

    /// <summary>Compares the two results of the same statements applied to <paramref name="originPath"/>: page
    /// counts, then every differing page — page 0 on its own, the rest grouped by page type and owner — with its
    /// differing byte count and the first few differences. Returns the report and how many pages differ.</summary>
    internal static (string Report, int Pages) Compare(string originPath, string acePath, string noisePath, string libredPath)
    {
        byte[] origin = File.ReadAllBytes(originPath), ace = File.ReadAllBytes(acePath),
               noise = File.ReadAllBytes(noisePath), libred = File.ReadAllBytes(libredPath);
        JetFormatBase format = JetFormatBase.Detect(new MemoryStream(origin));
        int pageSize = format.PageSize;
        Dictionary<int, string> owners = Owners(acePath, libredPath);
        HashSet<int>[] masks = [.. Enumerable.Range(0, Math.Max(ace.Length, libred.Length) / pageSize).Select(_ => new HashSet<int>())];
        Mask(acePath, ace, masks);
        Mask(libredPath, libred, masks);

        int acePages = ace.Length / pageSize, libredPages = libred.Length / pageSize;
        int identical = 0, housekeeping = 0, differingPages = 0;
        var groups = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        for (int page = 0; page < Math.Max(acePages, libredPages); page++)
        {
            int at = page * pageSize;
            if (page >= acePages || page >= libredPages)
            {
                byte[] only = page >= acePages ? libred : ace;
                Add(groups, "pages in one file only", $"page {page}: {(page >= acePages ? "LibRed" : "ACE")} only, " +
                    $"type 0x{only[at]:X2} {Owner(owners, format, only, at)}");
                differingPages++;
                continue;
            }

            var differing = new List<int>();
            for (int i = 0; i < pageSize; i++)
            {
                if (ace[at + i] == libred[at + i] || masks[page].Contains(i)) continue;
                // A byte ACE writes merely for holding a session, which LibRed need not reproduce.
                if (at + i < origin.Length && at + i < noise.Length && origin[at + i] != noise[at + i]) { housekeeping++; continue; }
                differing.Add(i);
            }
            if (differing.Count == 0) { identical++; continue; }

            differingPages++;
            string detail = string.Join(" ", differing.Take(8).Select(i => $"+0x{i:X3}:{ace[at + i]:X2}/{libred[at + i]:X2}"));
            string kind = page == 0 ? "page 0"
                : $"type 0x{ace[at]:X2}{(ace[at] != libred[at] ? $"/0x{libred[at]:X2}" : "")} {Owner(owners, format, ace, at)}";
            string slots = ace[at] == 0x01
                ? "\n      slots " + string.Join(" ", Enumerable.Range(0, DataPage.ReadRowCount(ace.AsSpan(at, pageSize), format))
                    .Select(r => DataPage.ReadSlot(ace.AsSpan(at, pageSize), format, r))
                    .Select(s => $"{s.Offset:X3}/{(int)s.Flags:X4}"))
                : "";
            Add(groups, kind, $"page {page}: {differing.Count} bytes  {detail}{slots}{Runs(origin, ace, libred, at, differing)}");
        }

        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture,
            $"pages: before {origin.Length / pageSize}, ACE {acePages}, LibRed {libredPages}; identical {identical}; " +
            $"{housekeeping} byte(s) differ only where ACE's session alone writes (ace/libred shown)");
        foreach (var (kind, lines) in groups)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"-- {kind}: {lines.Count} page(s)");
            foreach (string line in lines) report.AppendLine(CultureInfo.InvariantCulture, $"   {line}");
        }
        return (report.ToString(), differingPages);
    }

    /// <summary>The page's differing bytes as runs — those within four bytes of each other joined, with sixteen bytes
    /// of context either side — the file before the statement, then ACE's line over LibRed's, for the first forty
    /// runs.</summary>
    private static string Runs(byte[] origin, byte[] ace, byte[] libred, int at, List<int> differing)
    {
        var text = new StringBuilder();
        int k = 0, runs = 0;
        while (k < differing.Count && runs++ < 40)
        {
            int start = differing[k], end = start;
            while (k + 1 < differing.Count && differing[k + 1] <= end + 4) end = differing[++k];
            k++;
            int from = Math.Max(0, start - 16), length = Math.Min(Math.Min(end + 17, 4096), ace.Length - at) - from;
            if (at + from + length <= origin.Length)
                text.Append(CultureInfo.InvariantCulture, $"\n      +0x{from:X3} WAS {Convert.ToHexString(origin, at + from, length)}");
            text.Append(CultureInfo.InvariantCulture, $"\n      +0x{from:X3} ACE {Convert.ToHexString(ace, at + from, length)}")
                .Append(CultureInfo.InvariantCulture, $"\n      +0x{from:X3} LIB {Convert.ToHexString(libred, at + from, length)}");
        }
        return text.ToString();
    }

    private static void Add(SortedDictionary<string, List<string>> groups, string kind, string line)
    {
        if (!groups.TryGetValue(kind, out var lines)) groups[kind] = lines = [];
        lines.Add(line);
    }

    /// <summary>What owns a page: for a data or index page the table whose TDEF page its header names, for a TDEF
    /// page its own table, where either file's catalog knows the name.</summary>
    private static string Owner(Dictionary<int, string> owners, JetFormatBase format, byte[] file, int at)
    {
        if (PageHeader.ReadType(file.AsSpan(at)) is PageType.TableDefinition or PageType.ReleasedTableDefinition)
            return owners.TryGetValue(at / format.PageSize, out string? self) ? $"({self})" : "";
        if (PageHeader.ReadType(file.AsSpan(at)) is not (PageType.DataPage or PageType.IntermediateIndexPage or PageType.LeafIndexPage))
            return "";
        ReadOnlySpan<byte> page = file.AsSpan(at, format.PageSize);
        uint ownerField = PageHeader.ReadType(page) == PageType.DataPage
            ? DataPage.ReadOwner(page, format)
            : (uint)IndexTree.ReadOwner(page, format);
        if (ownerField == JetFormatBase.LongValuePageMarker) return "(long values)";
        int owner = (int)ownerField;
        return owners.TryGetValue(owner, out string? name) ? $"({name})" : $"(tdef {owner})";
    }

    private static Dictionary<int, string> Owners(params string[] paths)
    {
        var owners = new Dictionary<int, string>();
        foreach (string path in paths)
        {
            using var database = JetDatabase.Open(path, readOnly: true);
            foreach (TableDefinition table in database.Catalog.Tables) owners.TryAdd(table.DefinitionPage, table.Name);
        }
        return owners;
    }

    /// <summary>Masks the bytes known to differ for reasons other than the format: page 0's user commit-byte table,
    /// a count of each lock-file user's committed writes that LibRed, keeping no lock file, leaves alone
    /// (page-00-database.md §2.2); the clock values either engine stamps during the run — the wall-clock DateCreate
    /// and DateUpdate on MSysObjects pages, every data page's write stamp, and a chained long value's tick-count
    /// chain stamp — wherever they lie, a record's dead bytes and the free space below the rows included; and a
    /// stored query's WHERE-row LvExtra.</summary>
    private static void Mask(string path, byte[] file, HashSet<int>[] masks)
    {
        using var database = JetDatabase.Open(path, readOnly: true);
        JetFormatBase format = database.Format;
        int pageSize = format.PageSize;

        for (int i = format.CommitByteTableOffset; i < pageSize; i++) masks[0].Add(i);

        // The run's clocks: everything either engine stamps happens while this process lives — ACE runs in it.
        DateTime started = System.Diagnostics.Process.GetCurrentProcess().StartTime, now = DateTime.Now;
        double earliest = started.ToOADate(), latest = now.ToOADate();
        uint firstTick = (uint)Environment.TickCount - (uint)(now - started).TotalMilliseconds;
        uint ticks = (uint)Environment.TickCount - firstTick;

        int objects = database.Catalog.FindTable("MSysObjects")!.DefinitionPage;
        int stampAt = format.LongValueDescriptorChainStampOffset;
        for (int page = 1; page < file.Length / pageSize; page++)
        {
            int at = page * pageSize;
            if (PageHeader.ReadType(file.AsSpan(at)) != PageType.DataPage) continue;
            for (int i = format.DataChainStampOffset; i < format.DataChainStampOffset + sizeof(int); i++) masks[page].Add(i);
            ReadOnlySpan<byte> bytes = file.AsSpan(at, pageSize);
            int from = DataPage.DirectoryEnd(format, DataPage.ReadRowCount(bytes, format));

            // A chained long value's descriptor — bytes 8–11 of its twelve the tick count the chain's first page
            // carries at 0x08 (long-values.md) — in a live record or in the bytes an old one left behind.
            for (int d = from; d + format.LongValueDescriptorSize <= pageSize; d++)
            {
                uint head = BinaryPrimitives.ReadUInt32LittleEndian(bytes[d..]);
                uint stamp = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(d + stampAt)..]);
                if ((head & ~(uint)format.LongValueLengthMask) != (uint)LongValueStore.StorageKind.Chained
                    || stamp - firstTick > ticks) continue;
                for (int i = 0; i < sizeof(uint); i++) masks[page].Add(d + stampAt + i);
            }

            if ((int)DataPage.ReadOwner(bytes, format) != objects) continue;

            // An MSysObjects row's DateCreate and DateUpdate, live or in an old record's image.
            var dates = new List<byte[]>();
            for (int p = from; p + sizeof(double) <= pageSize; p++)
            {
                double date = BinaryPrimitives.ReadDoubleLittleEndian(bytes[p..]);
                if (date < earliest || date > latest) continue;
                dates.Add(bytes.Slice(p, sizeof(double)).ToArray());
                for (int i = 0; i < sizeof(double); i++) masks[page].Add(p + i);
            }

            // And an old record's image the next record cut short keeps only the front of its last date — the low
            // bytes, where the clocks differ.
            for (int p = from; p < pageSize; p++)
                foreach (byte[] date in dates)
                {
                    int kept = bytes[p..].CommonPrefixLength(date);
                    if (kept >= sizeof(uint))
                        for (int i = 0; i < kept; i++) masks[page].Add(p + i);
                }
        }

        // A WHERE row's LvExtra, which ACE can fill with an uninitialised value that changes from one ACE process to
        // the next (system-catalog.md). Only the WHERE row's: a parameter row's LvExtra holds its facets, the other
        // rows' are ACE's 0, and whether a row carries one at all is its null-bitmap bit — all still compared.
        TableDefinition queries = database.Catalog.FindTable("MSysQueries")!;
        ColumnDef attribute = queries.FindColumn("Attribute")!, lvExtra = queries.FindColumn("LvExtra")!;
        if (!lvExtra.IsFixedLength) return;
        Table queryRows = database.OpenTable("MSysQueries");
        foreach ((RowId id, object?[] values) in queryRows.Rows().WithIds())
        {
            if (values[attribute.Index] is not (byte and 8) || values[lvExtra.Index] is null) continue;
            var page = new DataPage();
            page.Read(queryRows.Channel.ReadPageShared(id.Page), queryRows.Channel.Format);
            if (page.Rows[id.Row] is not { IsDeleted: false, HasOverflow: false } slot) continue;
            int at = slot.Offset + format.RowColumnCountSize + lvExtra.FixedOffset;
            for (int i = 0; i < sizeof(int); i++) masks[id.Page].Add(at + i);
        }
    }

    /// <summary>Gives each query in LibRed's file the <c>MSysObjects.Lv</c> ACE's has, through LibRed's own row update.
    /// It is ACE's compiled plan, a memory image with heap and code addresses in it that ACE rewrites on every run
    /// (system-catalog.md), so its bytes can never match; only they are taken from ACE — the row's descriptor, the
    /// LVAL page and the maps the value brings are still LibRed's to write, and compared.</summary>
    private static void CopyQueryPlans(string acePath, string libredPath)
    {
        Dictionary<string, byte[]> plans = new(StringComparer.OrdinalIgnoreCase);
        using (var ace = JetDatabase.Open(acePath, readOnly: true))
            foreach ((string name, byte[]? plan, _, _) in QueryRows(ace))
                if (plan is not null) plans[name] = plan;

        using var libred = JetDatabase.Open(libredPath, readOnly: false);
        foreach ((string name, byte[]? plan, RowId id, object?[] values) in QueryRows(libred))
        {
            if (!plans.TryGetValue(name, out byte[]? acePlan) || plan is not null && plan.AsSpan().SequenceEqual(acePlan)) continue;
            int lv = libred.Catalog.FindTable("MSysObjects")!.FindColumn("Lv")!.Index;
            values[lv] = acePlan;
            libred.OpenTable("MSysObjects").Update(id, values, new HashSet<int> { lv });
        }
    }

    /// <summary>Every stored query's MSysObjects row: its name, its <c>Lv</c>, its id and its values.</summary>
    private static List<(string Name, byte[]? Plan, RowId Id, object?[] Values)> QueryRows(JetDatabase database)
    {
        TableDefinition objects = database.Catalog.FindTable("MSysObjects")!;
        int name = objects.FindColumn("Name")!.Index, type = objects.FindColumn("Type")!.Index, lv = objects.FindColumn("Lv")!.Index;
        return [.. database.OpenTable("MSysObjects").Rows().WithIds()
            .Where(r => r.Values[type] is short t && t == 5)
            .Select(r => ((string)r.Values[name]!, r.Values[lv] as byte[], r.Id, r.Values))];
    }

    // DAO's CreateDatabase format options and field types.
    private const int Jet4 = 64, Ace12 = 128;
    private const int DaoLong = 4, DaoText = 10, DaoMemo = 12, DaoAttachment = 101, DaoMultiValueLong = 104, DaoMultiValueText = 109;

    /// <summary>A database made by DAO, the path Access itself takes, in <paramref name="version"/>'s format — empty,
    /// or holding what <paramref name="prepare"/> adds through the open DAO database; null where DAO is missing.</summary>
    private static string? CreateThroughDao(int version = Ace12, Action<object>? prepare = null)
    {
        object? engine = AceTestDatabase.CreateDaoEngine();
        if (engine is null) return null;
        string path = TemporaryDatabase.CreatePath("wholefile-origin-");
        object workspace = Invoke(engine, "CreateWorkspace", "", "admin", "", 2)!;
        object database = Invoke(workspace, "CreateDatabase", path, ";LANGID=0x0409;CP=1252;COUNTRY=0", version)!;
        try { prepare?.Invoke(database); }
        finally
        {
            Invoke(database, "Close");
            AceTestDatabase.ReleaseAbandonedComObjects();
        }
        return path;
    }

    private static object? Invoke(object target, string member, params object?[] args) =>
        target.GetType().InvokeMember(member, BindingFlags.InvokeMethod, null, target, args);

    private static object Get(object target, string member) =>
        target.GetType().InvokeMember(member, BindingFlags.GetProperty, null, target, null)!;

    private static void Set(object target, string member, object value) =>
        target.GetType().InvokeMember(member, BindingFlags.SetProperty, null, target, [value]);

    private static object Item(object collection, object key) =>
        collection.GetType().InvokeMember("Item", BindingFlags.GetProperty, null, collection, [key])!;
}
