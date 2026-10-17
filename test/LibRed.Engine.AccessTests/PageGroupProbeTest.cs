using System.Buffers.Binary;
using System.Data.OleDb;
using System.Globalization;
using System.Reflection;
using System.Text;
using LibRed;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;
using LibRed.Storage;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// Probe: how ACE chooses the page it allocates — the session extents it reserves in the global free map, and where
/// single pages go outside them — measured on a thousand-row INSERT … SELECT and variations of it. Reports; asserts
/// nothing. Every test is explicit: several take minutes, the reservation samples up to twenty.
/// </summary>
/// <remarks>
/// A reservation is visible only while ACE's session is open — at close the unused rest is set free again — so the
/// <c>*_reservations</c> tests copy the file with the session still open, after waiting out ACE's flush timeout.
/// </remarks>
[Collection(AceCollection.Name)]
public class PageGroupProbeTest(ITestOutputHelper output)
{
    [Theory(Explicit = true)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Index_groups_observed_inside_Access_statement(int callbackMode)
    {
        Type? accessType = Type.GetTypeFromProgID("Access.Application");
        Assert.SkipWhen(accessType is null, "Access automation is unavailable.");
        string origin = TemporaryDatabase.CreatePath("inside-allocation-");
        string prefix = origin + "-inside-";
        int[] samples = [115, 133, 134, 152, 153, 171, 172, 190, 191, 209, 210, 228, 229];
        dynamic access = Activator.CreateInstance(accessType)!;
        try
        {
            access.Visible = false;
            // Only this disposable automation instance opens our own generated callback module.
            access.AutomationSecurity = 1;
            access.NewCurrentDatabase(origin);
            dynamic module = access.VBE.ActiveVBProject.VBComponents.Add(1);
            module.Name = "AllocationCallback";
            string code = $$"""
                Public Function AllocationKey(ByVal id As Long) As String
                    Dim f As Integer, g As Integer, bytes() As Byte
                    AllocationKey = Format$(id, "0000") & String$(180, "x")
                    Select Case id
                    Case {{string.Join(", ", samples)}}
                        {{(callbackMode >= 2 ? "DBEngine.Idle dbRefreshCache" : "")}}
                        g = FreeFile
                        Open "{{prefix.Replace("\"", "\"\"", StringComparison.Ordinal)}}rows.txt" For Append As #g
                        Print #g, CStr(id) & ":" & CStr(CurrentDb.TableDefs("Indexed").RecordCount)
                        Close #g
                        f = FreeFile
                        Open CurrentProject.FullName For Binary Access Read Shared As #f
                        ReDim bytes(0 To LOF(f) - 1)
                        Get #f, 1, bytes
                        Close #f
                        g = FreeFile
                        Open "{{prefix.Replace("\"", "\"\"", StringComparison.Ordinal)}}" & CStr(id) & ".accdb" For Binary Access Write As #g
                        Put #g, 1, bytes
                        Close #g
                    End Select
                End Function
                Public Sub AllocationCache()
                    DBEngine.SetOption dbMaxBufferSize, 64
                End Sub
                """;
            module.CodeModule.AddFromString(code);
            access.DoCmd.Save(5, "AllocationCallback");
            access.Run("AllocationKey", -1);
            access.CloseCurrentDatabase();
            using (OleDbConnection connection = AceTestDatabase.Open(origin))
            using (OleDbCommand command = connection.CreateCommand())
            {
                command.CommandText = "CREATE TABLE Indexed (K TEXT(200))";
                command.ExecuteNonQuery();
                command.CommandText = "CREATE INDEX ProbeIndex ON Indexed (K)";
                command.ExecuteNonQuery();
                for (int id = 0; id < 115; id++)
                {
                    command.CommandText = $"INSERT INTO Indexed (K) VALUES ('{id:D4}{new string('x', 180)}')";
                    command.ExecuteNonQuery();
                }
                command.CommandText = "CREATE TABLE Source (Id LONG)";
                command.ExecuteNonQuery();
                for (int id = 0; id < 400; id++)
                {
                    command.CommandText = $"INSERT INTO Source VALUES ({id})";
                    command.ExecuteNonQuery();
                }
            }
            (int Row, int Page) global, indexMap, dataMap;
            using (var database = JetDatabase.Open(origin, readOnly: false))
            {
                Table table = database.OpenTable("Indexed");
                indexMap = table.Definition.Indexes.Single().UsageMap;
                Assert.Equal(8, table.UsageMap.PagesInMap(indexMap.Row, indexMap.Page).Count());
                dataMap = table.Channel.ReadPage(table.Definition.DefinitionPage).ReadRecordPointer(database.Format.TdefOwnedPagesOffset);
                while (table.Channel.PageCount < 1024) table.Channel.Allocator.Allocate();
                table.Channel.Allocator.ReturnReleasedPages();
                global = database.DefinitionPage.FreePagesMap;
                byte[] holderBytes = table.Channel.ReadPage(global.Page).Span.ToArray();
                var holder = new DataPage();
                holder.Read(table.Channel.ReadPage(global.Page), database.Format);
                var slot = holder.Rows[global.Row];
                Span<byte> record = holderBytes.AsSpan(slot.Offset, slot.Length);
                Assert.Equal(UsageMapType.Inline, UsageMap.RecordType(record));
                Span<byte> bits = UsageMap.InlineBits(record, database.Format);
                for (int bit = 0; bit < bits.Length * 8; bit++) BitmapBits.Set(bits, bit, bit >= 512);
                table.Channel.WritePage(global.Page, holderBytes);
            }
            access.OpenCurrentDatabase(origin, false);
            access.Run("AllocationKey", -1);
            if (callbackMode == 3) access.Run("AllocationCache");
            dynamic current = access.CurrentDb();
            string expression = callbackMode == 0 ? "Right('0000' & (Id + 115), 4) & String(180, 'x')" : "AllocationKey(Id + 115)";
            current.Execute($"INSERT INTO Indexed (K) SELECT {expression} FROM Source", 128);
            access.CloseCurrentDatabase();
            if (File.Exists(prefix + "rows.txt")) output.WriteLine(File.ReadAllText(prefix + "rows.txt"));
            foreach (int sample in samples.Append(-1))
            {
                string path = sample < 0 ? origin : prefix + sample.ToString(CultureInfo.InvariantCulture) + ".accdb";
                if (!File.Exists(path)) continue;
                using var snapshot = JetDatabase.Open(path, readOnly: true);
                Table observed = snapshot.OpenTable("Indexed");
                UsageMap maps = observed.UsageMap;
                HashSet<int> free = GlobalFree(observed, 640);
                output.WriteLine($"mode {callbackMode}, before id {sample}: data [{Ranges(maps.PagesInMap(dataMap.Row, dataMap.Page).Where(p => p >= 512))}]; index [{Ranges(maps.PagesInMap(indexMap.Row, indexMap.Page).Where(p => p >= 512))}]; unavailable [{Ranges(Enumerable.Range(512, 128).Where(p => !free.Contains(p)))}]");
            }
        }
        finally
        {
            access.Quit(2);
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject((object)access);
            TemporaryDatabase.Delete(origin);
            foreach (int sample in samples) TemporaryDatabase.Delete(prefix + sample.ToString(CultureInfo.InvariantCulture) + ".accdb");
            TemporaryDatabase.Delete(prefix + "rows.txt");
        }
    }

    [Theory(Explicit = true)]
    [InlineData(0)]
    [InlineData(40)]
    public void Bulk_insert_page_map(int paddingRows)
    {
        object? engine = AceTestDatabase.CreateDaoEngine();
        Assert.SkipWhen(engine is null, "DAO is not registered in this bitness.");
        string origin = TemporaryDatabase.CreatePath("pagegroup-origin-");
        object workspace = Invoke(engine, "CreateWorkspace", "", "admin", "", 2)!;
        object db = Invoke(workspace, "CreateDatabase", origin, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128)!;
        Invoke(db, "Close");
        int pageSize;
        using (FileStream stream = File.OpenRead(origin)) pageSize = JetFormatBase.Detect(stream).PageSize;

        var setup = new List<string>
        {
            "CREATE TABLE Padding (Id LONG, Filler TEXT(255))",
            "CREATE TABLE Digits (D BYTE)",
        };
        for (int i = 0; i < paddingRows; i++)
            setup.Add($"INSERT INTO Padding VALUES ({i}, String(255, 'x'))");
        for (int d = 0; d <= 9; d++) setup.Add($"INSERT INTO Digits (D) VALUES ({d})");
        setup.Add("CREATE TABLE Bulk (Id LONG CONSTRAINT pkBulk PRIMARY KEY, Grp LONG, Label TEXT(60), Payload TEXT(255), " +
                  "Code BINARY(8), Amount DECIMAL(10,2))");
        setup.Add("CREATE INDEX ixBulkLabel ON Bulk (Label)");
        const string bulk =
            "INSERT INTO Bulk (Id, Grp, Label, Payload, Amount) SELECT a.D * 100 + b.D * 10 + c.D, a.D, " +
            "'Bulk label number ' & (a.D * 100 + b.D * 10 + c.D) & ' ' & String(30, 'L'), String(150, 'p'), " +
            "a.D + b.D * 0.5 FROM Digits AS a, Digits AS b, Digits AS c ORDER BY 1";

        string ace = TemporaryDatabase.CopyPath(origin, "pagegroup-ace-");
        string libred = TemporaryDatabase.CopyPath(origin, "pagegroup-libred-");
        try
        {
            using (OleDbConnection connection = AceTestDatabase.Open(origin))
                foreach (string statement in setup)
                {
                    using OleDbCommand command = connection.CreateCommand();
                    command.CommandText = statement;
                    command.ExecuteNonQuery();
                }
            File.Copy(origin, ace, overwrite: true);
            File.Copy(origin, libred, overwrite: true);

            using (OleDbConnection connection = AceTestDatabase.Open(ace))
            {
                using OleDbCommand command = connection.CreateCommand();
                command.CommandText = bulk;
                command.ExecuteNonQuery();
            }
            using (var database = JetDatabase.Open(libred, readOnly: false))
                new QueryEngine(database).ExecuteNonQuery(bulk);

            var report = new StringBuilder();
            report.AppendLine(CultureInfo.InvariantCulture, $"before: {new FileInfo(origin).Length / pageSize} pages");
            Describe(report, "ACE", ace);
            Describe(report, "LibRed", libred);
            output.WriteLine(report.ToString());
        }
        finally
        {
            foreach (string path in new[] { origin, ace, libred }) TemporaryDatabase.Delete(path);
        }
    }

    /// <summary>The full table's insert, cut at a few row counts, with the file read while ACE's session is still
    /// open (after its flush) and again after close: which pages the free and released maps hold at each point.</summary>
    [Fact(Explicit = true)]
    public void Bulk_insert_maps_while_the_session_is_open()
    {
        object? engine = AceTestDatabase.CreateDaoEngine();
        Assert.SkipWhen(engine is null, "DAO is not registered in this bitness.");
        string origin = TemporaryDatabase.CreatePath("pagegroup-origin-");
        object workspace = Invoke(engine, "CreateWorkspace", "", "admin", "", 2)!;
        object db = Invoke(workspace, "CreateDatabase", origin, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128)!;
        Invoke(db, "Close");
        var setup = new List<string> { "CREATE TABLE Digits (D BYTE)" };
        for (int d = 0; d <= 9; d++) setup.Add($"INSERT INTO Digits (D) VALUES ({d})");
        setup.Add("CREATE TABLE Bulk (Id LONG CONSTRAINT pkBulk PRIMARY KEY, Grp LONG, Label TEXT(60), Payload TEXT(255), " +
                  "Code BINARY(8), Amount DECIMAL(10,2))");
        setup.Add("CREATE INDEX ixBulkLabel ON Bulk (Label)");
        var report = new StringBuilder();
        try
        {
            using (OleDbConnection connection = AceTestDatabase.Open(origin))
                foreach (string statement in setup)
                {
                    using OleDbCommand command = connection.CreateCommand();
                    command.CommandText = statement;
                    command.ExecuteNonQuery();
                }

            foreach (int rows in new[] { 240, 480, 560, 1000 })
            {
                string ace = TemporaryDatabase.CopyPath(origin, "pagegroup-open-");
                string open = TemporaryDatabase.CreatePath("pagegroup-opencopy-");
                try
                {
                    using (OleDbConnection connection = AceTestDatabase.Open(ace))
                    {
                        using OleDbCommand command = connection.CreateCommand();
                        command.CommandText =
                            "INSERT INTO Bulk (Id, Grp, Label, Payload, Amount) SELECT a.D * 100 + b.D * 10 + c.D, a.D, " +
                            "'Bulk label number ' & (a.D * 100 + b.D * 10 + c.D) & ' ' & String(30, 'L'), String(150, 'p'), " +
                            $"a.D + b.D * 0.5 FROM Digits AS a, Digits AS b, Digits AS c WHERE a.D * 100 + b.D * 10 + c.D < {rows} ORDER BY 1";
                        command.ExecuteNonQuery();
                        Thread.Sleep(3000); // past ACE's flush timeout, so the committed pages are on disk
                        using var source = new FileStream(ace, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var target = File.Create(open);
                        source.CopyTo(target);
                    }
                    report.AppendLine(CultureInfo.InvariantCulture, $"{rows} rows, session open: {Maps(open)}");
                    report.AppendLine(CultureInfo.InvariantCulture, $"{rows} rows, after close:  {Maps(ace)}");
                }
                finally
                {
                    TemporaryDatabase.Delete(ace);
                    TemporaryDatabase.Delete(open);
                }
            }
            output.WriteLine(report.ToString());
        }
        finally { TemporaryDatabase.Delete(origin); }
    }

    /// <summary>The full table's insert at sampled row counts — row by row across the known transitions — each read
    /// with ACE's session still open: the pages newly in use since the last sample, and the pages ACE holds reserved
    /// (neither in use nor free), with the free pages inside the file.</summary>
    [Fact(Explicit = true)]
    public void Bulk_insert_reservations() =>
        Reservations(FullColumns, LabelIndex, 0, 1000,
            [(88, 102), (225, 245), (455, 482), (535, 610), (630, 690), (720, 780), (810, 820), (855, 925), (990, 1000)]);

    /// <summary>The same, with the first 200 rows inserted by the earlier session that built the table.</summary>
    [Fact(Explicit = true)]
    public void Bulk_insert_reservations_after_an_earlier_session() =>
        Reservations(FullColumns, LabelIndex, 200, 1000, [(201, 250), (455, 485), (540, 610), (630, 690), (720, 760)]);

    /// <summary>A heap table with no index, 200 rows from the earlier session: whether a data page that must grow
    /// the file takes an extent when it is the second session's first growth.</summary>
    [Fact(Explicit = true)]
    public void Heap_insert_reservations_after_an_earlier_session() =>
        Reservations("Id LONG, Grp LONG, Label TEXT(60), Payload TEXT(255), Code BINARY(8), Amount DECIMAL(10,2)", "",
            200, 300, [(200, 240)]);

    /// <summary>An empty heap table, filled from a later session: whether its first data page takes an extent.</summary>
    [Fact(Explicit = true)]
    public void Heap_first_page_reservations() =>
        Reservations("Id LONG, Grp LONG, Label TEXT(60), Payload TEXT(255), Code BINARY(8), Amount DECIMAL(10,2)", "",
            0, 50, [(1, 20)]);

    /// <summary>A primary-keyed table, 600 rows from the earlier session, so the second session's first allocation
    /// is the key's root split (at the 603rd row) while the last data page still has room.</summary>
    [Fact(Explicit = true)]
    public void Root_split_first_reservations() =>
        Reservations("Id LONG CONSTRAINT pkBulk PRIMARY KEY, Grp LONG, Label TEXT(60), Payload TEXT(255), Code BINARY(8), Amount DECIMAL(10,2)", "",
            600, 625, [(600, 612)]);

    /// <summary>Whether growth lands at the end of the file or the next 8-page boundary depends on this session having
    /// used the file's last group. Setup leaves Bulk (primary key only) at 600 rows, ending mid-group, and a heap
    /// table Side with full pages; the second session first grows Side, then brings Bulk to its root split.</summary>
    [Fact(Explicit = true)]
    public void Growth_alignment_follows_the_session()
    {
        object? engine = AceTestDatabase.CreateDaoEngine();
        Assert.SkipWhen(engine is null, "DAO is not registered in this bitness.");
        string origin = TemporaryDatabase.CreatePath("pagegroup-origin-");
        object workspace = Invoke(engine, "CreateWorkspace", "", "admin", "", 2)!;
        object db = Invoke(workspace, "CreateDatabase", origin, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128)!;
        Invoke(db, "Close");

        const string select =
            "INSERT INTO Bulk (Id, Grp, Label, Payload, Amount) SELECT a.D * 100 + b.D * 10 + c.D, a.D, " +
            "'Bulk label number ' & (a.D * 100 + b.D * 10 + c.D) & ' ' & String(30, 'L'), String(150, 'p'), " +
            "a.D + b.D * 0.5 FROM Digits AS a, Digits AS b, Digits AS c WHERE a.D * 100 + b.D * 10 + c.D ";
        var setup = new List<string> { "CREATE TABLE Digits (D BYTE)" };
        for (int d = 0; d <= 9; d++) setup.Add($"INSERT INTO Digits (D) VALUES ({d})");
        setup.Add("CREATE TABLE Side (Id LONG, Filler TEXT(255))");
        for (int i = 0; i < 28; i++) setup.Add($"INSERT INTO Side VALUES ({i}, String(255, 's'))");
        setup.Add("CREATE TABLE Bulk (Id LONG CONSTRAINT pkBulk PRIMARY KEY, Grp LONG, Label TEXT(60), Payload TEXT(255), " +
                  "Code BINARY(8), Amount DECIMAL(10,2))");
        setup.Add($"{select}< 600 ORDER BY 1");

        string[] side = [.. Enumerable.Range(100, 20).Select(i => $"INSERT INTO Side VALUES ({i}, String(255, 't'))")];
        var runs = new List<(string Label, string[] Sql)>
        {
            ("nothing", []),
            ("Side grown", side),
            ("then Bulk split", [.. side, $"{select}>= 600 AND a.D * 100 + b.D * 10 + c.D < 603 ORDER BY 1"]),
        };

        var report = new StringBuilder();
        try
        {
            using (OleDbConnection connection = AceTestDatabase.Open(origin))
                foreach (string statement in setup)
                {
                    using OleDbCommand command = connection.CreateCommand();
                    command.CommandText = statement;
                    command.ExecuteNonQuery();
                }
            int pageSize;
            using (FileStream stream = File.OpenRead(origin)) pageSize = JetFormatBase.Detect(stream).PageSize;
            report.AppendLine(CultureInfo.InvariantCulture, $"after setup: {new FileInfo(origin).Length / pageSize} pages");

            Dictionary<int, string> previous = Kinds(origin);
            foreach ((string label, string[] sql) in runs)
            {
                string ace = TemporaryDatabase.CopyPath(origin, "pagegroup-grow-");
                string open = TemporaryDatabase.CreatePath("pagegroup-growcopy-");
                try
                {
                    using (OleDbConnection connection = AceTestDatabase.Open(ace))
                    {
                        foreach (string statement in sql)
                        {
                            using OleDbCommand command = connection.CreateCommand();
                            command.CommandText = statement;
                            command.ExecuteNonQuery();
                        }
                        Thread.Sleep(3000); // past ACE's flush timeout, so the committed pages are on disk
                        using var source = new FileStream(ace, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var target = File.Create(open);
                        source.CopyTo(target);
                    }

                    Dictionary<int, string> kinds = Kinds(open);
                    int pages = (int)(new FileInfo(open).Length / pageSize);
                    HashSet<int> free;
                    string owners;
                    using (var database = JetDatabase.Open(open, readOnly: true))
                    {
                        free = GlobalFree(database.OpenTable("MSysObjects"), 512);
                        owners = $"Side tdef {database.OpenTable("Side").Definition.DefinitionPage}, " +
                                 $"Bulk tdef {database.OpenTable("Bulk").Definition.DefinitionPage}";
                    }
                    string reserved = Ranges(Enumerable.Range(1, 511)
                        .Where(p => !free.Contains(p) && (p >= pages || !kinds.ContainsKey(p))));
                    var changes = kinds.OrderBy(p => p.Key)
                        .Where(p => !previous.TryGetValue(p.Key, out string? before) || before != p.Value)
                        .Select(p => $"+{p.Key}{p.Value}");
                    report.AppendLine(CultureInfo.InvariantCulture,
                        $"{label,-16} ({pages}; {owners}): {string.Join(" ", changes)}\n{"",18}reserved [{reserved}]  " +
                        $"free in file [{Ranges(free.Where(p => p < pages))}]");
                }
                finally
                {
                    TemporaryDatabase.Delete(ace);
                    TemporaryDatabase.Delete(open);
                }
            }
            output.WriteLine(report.ToString());
        }
        finally { TemporaryDatabase.Delete(origin); }
    }

    private const string FullColumns =
        "Id LONG CONSTRAINT pkBulk PRIMARY KEY, Grp LONG, Label TEXT(60), Payload TEXT(255), Code BINARY(8), Amount DECIMAL(10,2)";

    private const string LabelIndex = "CREATE INDEX ixBulkLabel ON Bulk (Label)";

    [Theory(Explicit = true)]
    [InlineData(512)]
    [InlineData(32000)]
    [InlineData(32736)]
    public void Global_growth_reservations_on_the_unit_test_fixture(int frontier)
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "allocation-growth-measure-");
        string open = TemporaryDatabase.CreatePath("allocation-growth-open-");
        try
        {
            using (var channel = PageChannel.Open(path, readOnly: false))
            {
                while (channel.PageCount < frontier) channel.Allocator.Allocate();
                Assert.Equal(frontier, channel.PageCount);
            }
            using (OleDbConnection connection = AceTestDatabase.Open(path))
            {
                using OleDbCommand command = connection.CreateCommand();
                command.CommandText = "CREATE INDEX AllocationGrowthProbe ON Categories (CategoryName, CategoryID)";
                command.ExecuteNonQuery();
                Thread.Sleep(3000);
                using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var target = File.Create(open);
                source.CopyTo(target);
            }
            var report = new StringBuilder();
            foreach ((string label, string file) in new[] { ("open", open), ("closed", path) })
            {
                using var database = JetDatabase.Open(file, readOnly: true);
                Table table = database.OpenTable("Categories");
                HashSet<int> free = GlobalFree(table, int.MaxValue, report);
                int root = table.Definition.Indexes.Single(i => i.Name == "AllocationGrowthProbe").RootPage;
                report.AppendLine($"frontier={frontier}; {label}; root={root}; free near frontier [{Ranges(free.Where(p => p >= frontier && p < frontier + 40))}]");
            }
            output.WriteLine(report.ToString());
        }
        finally
        {
            TemporaryDatabase.Delete(path);
            TemporaryDatabase.Delete(open);
        }
    }

    /// <summary>Controlled synthetic free space for heap and index allocation requests. Wide binary rows
    /// isolate one data page per row; smaller rows distinguish row count from page count. Only unused pages
    /// are changed; live pages retain their allocation bits. Reports actual free-map deltas while open and
    /// after close, rather than inferring reservations from page types.</summary>
    [Theory(Explicit = true)]
    [InlineData("contiguous", "heap")]
    [InlineData("unaligned", "heap")]
    [InlineData("singleton-before-run", "heap")]
    [InlineData("fragmented", "heap")]
    [InlineData("short-run", "heap")]
    [InlineData("contiguous", "index")]
    [InlineData("singleton-before-run", "index")]
    [InlineData("fragmented", "index")]
    [InlineData("growth", "heap")]
    [InlineData("growth", "index")]
    [InlineData("contiguous", "heap-then-index")]
    [InlineData("singleton-before-run", "heap-then-index")]
    [InlineData("contiguous", "index-then-heap")]
    [InlineData("contiguous", "second-table")]
    [InlineData("contiguous", "index-pages")]
    [InlineData("singleton-before-run", "index-pages")]
    [InlineData("fragmented", "index-pages")]
    [InlineData("shift72", "heap")]
    [InlineData("shift80", "heap")]
    [InlineData("shift93", "heap")]
    [InlineData("shift97", "heap")]
    [InlineData("after-gap", "heap")]
    [InlineData("after-singleton", "heap")]
    [InlineData("after-singleton", "heap-then-index")]
    [InlineData("preload1", "heap")]
    [InlineData("preload8", "heap")]
    [InlineData("preload16", "heap")]
    [InlineData("preload24", "heap")]
    [InlineData("preload31", "heap")]
    [InlineData("preload32", "heap")]
    [InlineData("preload33", "heap")]
    [InlineData("preload64", "heap")]
    [InlineData("preload2", "heap")]
    [InlineData("preload4", "heap")]
    [InlineData("preload7", "heap")]
    [InlineData("preload2", "partial")]
    [InlineData("preload8", "partial")]
    [InlineData("preload7", "no-full")]
    [InlineData("preload8", "no-full")]
    [InlineData("preload7", "growth257")]
    [InlineData("preload8", "growth257")]
    [InlineData("preload21", "small-row")]
    [InlineData("preload24", "small-row")]
    [InlineData("preload7", "same-group")]
    [InlineData("preload8", "same-group")]
    [InlineData("preload1", "tree7")]
    [InlineData("preload1", "tree8")]
    [InlineData("preload1", "tree8-bulk")]
    [InlineData("preload1", "tree8-bulk-noorder")]
    [InlineData("preload1", "tree8-bulk-sentinel1")]
    [InlineData("preload1", "tree8-bulk-sentinel2")]
    [InlineData("preload1", "tree8-tx")]
    [InlineData("preload1", "tree8-dao0")]
    [InlineData("preload1", "tree8-dao1")]
    [InlineData("preload1", "tree8-dao2")]
    [InlineData("preload1", "tree8-dao3")]
    [InlineData("preload1", "tree8-rs0")]
    [InlineData("preload1", "tree8-rs1")]
    [InlineData("preload1", "tree8-rs2")]
    [InlineData("preload1", "tree9")]
    [InlineData("preload1", "tree7-prime")]
    [InlineData("preload1", "tree8-prime")]
    [InlineData("order-both", "heap")]
    [InlineData("order-before", "heap")]
    [InlineData("order-after", "heap")]
    [InlineData("order-current", "heap")]
    [InlineData("order-nearer-before", "heap")]
    [InlineData("ref-shared", "heap")]
    [InlineData("ref-groups", "heap")]
    [InlineData("ref-shared", "index")]
    [InlineData("ref-groups", "heap-then-index")]
    [InlineData("ref-groups", "heap-reopen-index")]
    [InlineData("owner-ref", "heap")]
    [InlineData("preload1", "lv7-packed")]
    [InlineData("preload1", "lv8-packed")]
    [InlineData("preload1", "lv7-chain")]
    [InlineData("preload1", "lv8-chain")]
    [InlineData("preload1", "lv7-memo")]
    [InlineData("preload1", "lv8-memo")]
    [InlineData("preload1", "lv64-packed")]
    [InlineData("preload1", "lv64-chain")]
    [InlineData("ref-shared", "lv8-packed")]
    [InlineData("ref-shared", "lv8-chain")]
    [InlineData("ref-groups", "lv8-packed")]
    [InlineData("ref-native-order", "heap")]
    [InlineData("ref-shared", "lv8-release")]
    [InlineData("ref-empty-first", "index")]
    [InlineData("ref-exhaust", "heap-then-index")]
    [InlineData("ref-held", "heap-side-heap")]
    [InlineData("ref-held8", "heap-with-long")]
    [InlineData("ref-held8", "heap-then-long")]
    public void Controlled_free_page_selection(string layout, string operation)
    {
        object? engine = AceTestDatabase.CreateDaoEngine();
        Assert.SkipWhen(engine is null, "DAO is not registered in this bitness.");
        string origin = TemporaryDatabase.CreatePath("allocator-layout-");
        object workspace = Invoke(engine, "CreateWorkspace", "", "admin", "", 2)!;
        object db = Invoke(workspace, "CreateDatabase", origin, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128)!;
        Invoke(db, "Close");
        int fields = operation == "small-row" ? 5 : 9;
        string columns = string.Join(", ", Enumerable.Range(0, fields).Select(i => $"F{i} BINARY(255)"));
        string values = string.Join(", ", Enumerable.Repeat("0x" + new string('A', 510), fields));
        // A long value in the same row: its page comes from a shared reservation, the row's from Bulk's own group.
        // heap-then-long inserts two rows in one statement, the long value in the second, so Bulk's own group is taken
        // before the long value asks for a shared reservation.
        bool withLong = operation is "heap-with-long" or "heap-then-long";
        string thenLong = $"INSERT INTO Bulk (Id, {string.Join(", ", Enumerable.Range(0, fields).Select(i => $"F{i}"))}, Payload) " +
            $"SELECT Id, {values}, P FROM Src ORDER BY Id";
        string longValue = withLong ? ", 0x" + new string('B', 6000) : "";
        int preloaded = layout.StartsWith("preload", StringComparison.Ordinal)
            ? int.Parse(layout.AsSpan(7), CultureInfo.InvariantCulture) : layout.StartsWith("order-", StringComparison.Ordinal) || layout is "ref-groups" or "ref-native-order" or "ref-exhaust" or "ref-held" or "ref-held8" ? 8 : 1;
        bool referenceMap = layout.StartsWith("ref-", StringComparison.Ordinal);
        bool referenceOwner = referenceMap || layout == "owner-ref";
        int mapLimit = referenceMap ? 65472 : 512;
        int longPreloaded = operation.StartsWith("lv", StringComparison.Ordinal)
            ? int.Parse(operation.AsSpan(2, operation.IndexOf('-', StringComparison.Ordinal) - 2), CultureInfo.InvariantCulture) : 0;
        var report = new StringBuilder();
        int treeRows = 0;
        try
        {
            using (OleDbConnection connection = AceTestDatabase.Open(origin))
            {
                using OleDbCommand command = connection.CreateCommand();
                if (layout.StartsWith("order-", StringComparison.Ordinal) || layout == "ref-native-order")
                {
                    command.CommandText = $"CREATE TABLE Padding (Id LONG, {columns})";
                    command.ExecuteNonQuery();
                    int paddingRows = layout == "ref-native-order" ? 32700 : 126;
                    for (int id = 0; id < paddingRows; id++)
                    {
                        command.CommandText = $"INSERT INTO Padding VALUES ({id}, {values})";
                        command.ExecuteNonQuery();
                    }
                }
                command.CommandText = $"CREATE TABLE Bulk (Id LONG, {columns}{(withLong ? ", Payload LONGBINARY" : "")})";
                command.ExecuteNonQuery();
                for (int id = 0; id < preloaded; id++)
                {
                    command.CommandText = $"INSERT INTO Bulk VALUES ({-id}, {values}{(withLong ? ", NULL" : "")})";
                    command.ExecuteNonQuery();
                }
                if (operation == "heap-then-long")
                {
                    command.CommandText = "CREATE TABLE Src (Id LONG, P LONGBINARY)";
                    command.ExecuteNonQuery();
                    command.CommandText = "INSERT INTO Src VALUES (1, NULL)";
                    command.ExecuteNonQuery();
                    command.CommandText = $"INSERT INTO Src VALUES (2{longValue})";
                    command.ExecuteNonQuery();
                }
                if (layout.StartsWith("order-", StringComparison.Ordinal) || layout == "ref-native-order")
                {
                    command.CommandText = "DROP TABLE Padding";
                    command.ExecuteNonQuery();
                }
                if (operation is "second-table" or "heap-side-heap")
                {
                    command.CommandText = $"CREATE TABLE Side (Id LONG, {columns})";
                    command.ExecuteNonQuery();
                    command.CommandText = $"INSERT INTO Side VALUES (0, {values})";
                    command.ExecuteNonQuery();
                }
                if (operation == "index-pages")
                {
                    command.CommandText = "CREATE TABLE Indexed (Id LONG CONSTRAINT ProbePk PRIMARY KEY)";
                    command.ExecuteNonQuery();
                    for (int id = 0; id < 600; id++)
                    {
                        command.CommandText = $"INSERT INTO Indexed VALUES ({id})";
                        command.ExecuteNonQuery();
                    }
                }
                if (operation.StartsWith("lv", StringComparison.Ordinal))
                {
                    bool memo = operation.EndsWith("memo", StringComparison.Ordinal);
                    command.CommandText = $"CREATE TABLE Longs (Payload {(memo ? "MEMO" : "LONGBINARY")})";
                    command.ExecuteNonQuery();
                    command.CommandText = "INSERT INTO Longs VALUES (?)";
                    command.Parameters.Add("payload", memo ? OleDbType.LongVarWChar : OleDbType.LongVarBinary).Value =
                        memo ? new string('x', 1500) : new byte[3000];
                    for (int id = 0; id < longPreloaded; id++) command.ExecuteNonQuery();
                    if (operation.EndsWith("release", StringComparison.Ordinal))
                    {
                        command.Parameters[0].Value = new byte[32000];
                        command.ExecuteNonQuery();
                    }
                    command.Parameters.Clear();
                }
            }
            if (operation.StartsWith("tree", StringComparison.Ordinal))
            {
                int targetPages = int.Parse(operation.AsSpan(4, 1), CultureInfo.InvariantCulture);
                using (OleDbConnection connection = AceTestDatabase.Open(origin))
                {
                    using OleDbCommand command = connection.CreateCommand();
                    command.CommandText = operation.Contains("-sentinel", StringComparison.Ordinal)
                        ? "CREATE TABLE Indexed (K TEXT(200), P1 BINARY(255), P2 BINARY(255), P3 BINARY(255), P4 BINARY(255), P5 BINARY(255))"
                        : "CREATE TABLE Indexed (K TEXT(200))";
                    command.ExecuteNonQuery();
                    command.CommandText = "CREATE INDEX ProbeIndex ON Indexed (K)";
                    command.ExecuteNonQuery();
                    if (operation.Contains("-sentinel", StringComparison.Ordinal))
                        for (int id = 0; id < operation[^1] - '0'; id++)
                        {
                            command.CommandText = $"INSERT INTO Indexed (K) VALUES ('z{id}{new string('x', 180)}')";
                            command.ExecuteNonQuery();
                        }
                }
                for (; treeRows < 500;)
                {
                    using (OleDbConnection connection = AceTestDatabase.Open(origin))
                    {
                        using OleDbCommand command = connection.CreateCommand();
                        command.CommandText = $"INSERT INTO Indexed (K) VALUES ('{treeRows:D4}{new string('x', 180)}')";
                        command.ExecuteNonQuery();
                    }
                    treeRows++;
                    using var database = JetDatabase.Open(origin, readOnly: true);
                    Table indexed = database.OpenTable("Indexed");
                    var index = indexed.Definition.Indexes.Single();
                    int pages = indexed.UsageMap.PagesInMap(index.UsageMap.Row, index.UsageMap.Page).Count();
                    if (pages >= targetPages)
                    {
                        Assert.Equal(targetPages, pages);
                        report.AppendLine($"setup: {treeRows} rows, {indexed.UsageMap.DataPages().Count()} data pages, {pages} index pages");
                        break;
                    }
                }
                Assert.True(treeRows < 500);
                if (operation.Contains("-bulk", StringComparison.Ordinal))
                {
                    using OleDbConnection connection = AceTestDatabase.Open(origin);
                    using OleDbCommand command = connection.CreateCommand();
                    command.CommandText = "CREATE TABLE Source (Id LONG)";
                    command.ExecuteNonQuery();
                    for (int id = 0; id < 400; id++)
                    {
                        command.CommandText = $"INSERT INTO Source VALUES ({id})";
                        command.ExecuteNonQuery();
                    }
                }
            }
            int lastDataPage;
            using (var database = JetDatabase.Open(origin, readOnly: true))
            {
                lastDataPage = database.OpenTable("Bulk").UsageMap.DataPages().Max();
                if (operation.StartsWith("lv", StringComparison.Ordinal))
                {
                    Table longs = database.OpenTable("Longs");
                    var pointer = longs.Definition.LongValueOwnedMaps.Values.Single();
                    int owned = longs.UsageMap.PagesInMap(pointer.Row, pointer.Page).Count();
                    Assert.Equal(longPreloaded + (operation.EndsWith("release", StringComparison.Ordinal) ? 8 : 0), owned);
                    report.AppendLine($"setup: LVAL [{Ranges(longs.UsageMap.PagesInMap(pointer.Row, pointer.Page))}]; table data [{Ranges(longs.UsageMap.DataPages())}]");
                }
            }
            int currentGroup = lastDataPage & ~7;
            HashSet<int> offered = layout switch
            {
                "contiguous" => [.. Enumerable.Range(64, 192)],
                "unaligned" => [.. Enumerable.Range(65, 31)],
                "singleton-before-run" => [65, .. Enumerable.Range(80, 32)],
                "fragmented" => [65, 67, 70, 73, 75, 78, .. Enumerable.Range(96, 16)],
                "short-run" => [.. Enumerable.Range(67, 3), .. Enumerable.Range(80, 8)],
                "growth" => [],
                "shift72" => [.. Enumerable.Range(72, 96)],
                "shift80" => [.. Enumerable.Range(80, 96)],
                "shift93" => [.. Enumerable.Range(93, 96)],
                "shift97" => [.. Enumerable.Range(97, 96)],
                "after-gap" => [.. Enumerable.Range(64, 32), .. Enumerable.Range(97, 31)],
                "after-singleton" => [.. Enumerable.Range(64, 32), 97, .. Enumerable.Range(104, 32)],
                "order-both" => [.. Enumerable.Range(64, 8), .. Enumerable.Range(currentGroup - 16, 8),
                    .. Enumerable.Range(currentGroup + 8, 8), .. Enumerable.Range(currentGroup + 24, 8)],
                "order-before" => [.. Enumerable.Range(64, 8), .. Enumerable.Range(currentGroup - 16, 8)],
                "order-after" => [.. Enumerable.Range(currentGroup + 8, 8), .. Enumerable.Range(currentGroup + 24, 8)],
                "order-current" => [.. Enumerable.Range(lastDataPage + 1, 7 - (lastDataPage & 7)),
                    .. Enumerable.Range(64, 8), .. Enumerable.Range(currentGroup + 8, 8)],
                "order-nearer-before" => [.. Enumerable.Range(currentGroup - 16, 8), .. Enumerable.Range(currentGroup + 32, 8)],
                "ref-shared" => [.. Enumerable.Range(32729, 39)],
                "ref-groups" => [32729, .. Enumerable.Range(32736, 32)],
                "ref-empty-first" => [.. Enumerable.Range(32736, 32)],
                "ref-exhaust" => [32729, .. Enumerable.Range(32736, 8)],
                // Nothing offered: the only free pages are the 16 past the end of the file, up to range 2.
                "ref-held" or "ref-held8" => [],
                "ref-native-order" => [.. Enumerable.Range(64, 8), .. Enumerable.Range(160, 8),
                    .. Enumerable.Range(currentGroup + 8, 8), .. Enumerable.Range(currentGroup + 24, 8)],
                "owner-ref" => [65, .. Enumerable.Range(80, 32)],
                _ when layout.StartsWith("preload", StringComparison.Ordinal) && operation == "partial" => [129, .. Enumerable.Range(136, 32)],
                _ when layout.StartsWith("preload", StringComparison.Ordinal) && operation == "no-full" => [129, 137, 145],
                _ when layout.StartsWith("preload", StringComparison.Ordinal) && operation == "growth257" => [],
                _ when layout.StartsWith("preload", StringComparison.Ordinal) && operation == "same-group" =>
                    [.. Enumerable.Range(lastDataPage + 1, 7 - (lastDataPage & 7)), 129, .. Enumerable.Range(136, 32)],
                _ when layout.StartsWith("preload", StringComparison.Ordinal) => [.. Enumerable.Range(128, 128)],
                _ => throw new ArgumentException("Unknown layout", nameof(layout)),
            };
            // Materialize the controlled tail, then offer selected pages plus the ordinary growth region.
            using (var database = JetDatabase.Open(origin, readOnly: false))
            {
                Table table = database.OpenTable("Bulk");
                Assert.True(table.Channel.PageCount <= (layout == "ref-native-order" ? 32800 : layout.StartsWith("order-", StringComparison.Ordinal) ? 224 : preloaded > 1 || treeRows > 0 || longPreloaded > 0 ? 128 : 64));
                int originalPages = table.Channel.PageCount;
                HashSet<int> originalFree = GlobalFree(table, mapLimit);
                int frontier = layout == "ref-exhaust" ? mapLimit : layout == "ref-held" ? mapLimit - 16 : layout == "ref-held8" ? mapLimit - 8 : referenceMap ? 32800 : referenceOwner ? 264 : operation == "growth257" ? 257 : 256;
                for (int p = table.Channel.PageCount; p < frontier; p++)
                    table.Channel.WritePage(p, new byte[database.Format.PageSize]);
                (int row, int holderPage) = database.DefinitionPage.FreePagesMap;
                byte[] bytes = table.Channel.ReadPage(holderPage).Span.ToArray();
                var holder = new DataPage();
                holder.Read(table.Channel.ReadPageShared(holderPage), database.Format);
                DataPage.RowSlot slot = holder.Rows[row];
                Span<byte> map = bytes.AsSpan(slot.Offset, slot.Length);
                report.AppendLine($"before seeding: {originalPages} pages; global map={UsageMap.RecordType(map)}");
                if (layout == "ref-native-order") Assert.Equal(UsageMapType.Reference, UsageMap.RecordType(map));
                Assert.True(UsageMap.RecordType(map) == UsageMapType.Inline || referenceMap && UsageMap.RecordType(map) == UsageMapType.Reference);
                if (referenceMap)
                {
                    Assert.Equal(database.Format.UsageMapReferenceRecordSize, map.Length);
                    UsageMap.NewReferenceRecord(database.Format).CopyTo(map);
                    Assert.Equal(32736, database.Format.UsageMapPagesPerBitmapPage);
                    for (int segment = 0; segment < 2; segment++)
                    {
                        // These were materialized as unused tail pages, outside every offered range.
                        int bitmapPage = 256 + segment;
                        if (layout == "ref-native-order") Assert.Contains(bitmapPage, originalFree);
                        if (layout == "ref-empty-first" && segment == 0) continue;
                        UsageMap.WriteReferencePointer(map, segment, database.Format, bitmapPage);
                        byte[] bitmap = UsageMap.NewBitmapPage(database.Format);
                        Span<byte> bits = UsageMap.BitmapPageBits(bitmap, database.Format);
                        int start = segment * database.Format.UsageMapPagesPerBitmapPage;
                        for (int bit = 0; bit < bits.Length * 8; bit++)
                        {
                            int p = start + bit;
                            BitmapBits.Set(bits, bit, offered.Contains(p) || p >= frontier);
                        }
                        table.Channel.WritePage(bitmapPage, bitmap);
                    }
                }
                else
                {
                    int start = UsageMap.StartPage(map, database.Format);
                    Span<byte> bits = UsageMap.InlineBits(map, database.Format);
                    if (treeRows > 0 || layout.StartsWith("order-", StringComparison.Ordinal))
                        foreach (int p in GlobalFree(table, originalPages))
                            BitmapBits.Set(bits, p - start, offered.Contains(p));
                    for (int p = originalPages; p < mapLimit; p++)
                        BitmapBits.Set(bits, p - start, offered.Contains(p) || p >= frontier);
                }
                table.Channel.WritePage(holderPage, bytes);
                if (referenceOwner)
                {
                    // Keep the table's own map from allocating bitmap pages during the measurement.
                    // The data pages are intentionally spread across two bitmap ranges.
                    var ownerMaps = new List<(Table Owner, int Row, int Page)>();
                    Table[] owners = longPreloaded > 0 ? [table, database.OpenTable("Longs")] : [table];
                    foreach (Table owner in owners)
                    {
                        foreach (int offset in new[] { database.Format.TdefOwnedPagesOffset, database.Format.TdefFreePagesOffset })
                        {
                            (int rowNumber, int pageNumber) = table.Channel.ReadPage(owner.Definition.DefinitionPage).ReadRecordPointer(offset);
                            ownerMaps.Add((owner, rowNumber, pageNumber));
                        }
                        foreach (var pointer in owner.Definition.LongValueOwnedMaps.Values.Concat(owner.Definition.LongValueFreeMaps.Values))
                            if (pointer.Page != 0) ownerMaps.Add((owner, pointer.Row, pointer.Page));
                    }
                    int nextBitmap = 258;
                    foreach ((Table owner, int ownedRow, int ownedHolder) in ownerMaps)
                    {
                        int[] ownedPages = [.. owner.UsageMap.PagesInMap(ownedRow, ownedHolder)];
                        byte[] ownedBytes = table.Channel.ReadPage(ownedHolder).Span.ToArray();
                        var ownedPage = new DataPage();
                        ownedPage.Read(table.Channel.ReadPageShared(ownedHolder), database.Format);
                        DataPage.RowSlot ownedSlot = ownedPage.Rows[ownedRow];
                        Span<byte> ownedMap = ownedBytes.AsSpan(ownedSlot.Offset, ownedSlot.Length);
                        Assert.Equal(database.Format.UsageMapReferenceRecordSize, ownedMap.Length);
                        UsageMap.NewReferenceRecord(database.Format).CopyTo(ownedMap);
                        for (int segment = 0; segment < 2; segment++)
                        {
                            int bitmapPage = nextBitmap++;
                            if (layout == "ref-native-order") Assert.Contains(bitmapPage, originalFree);
                            UsageMap.WriteReferencePointer(ownedMap, segment, database.Format, bitmapPage);
                            byte[] bitmap = UsageMap.NewBitmapPage(database.Format);
                            Span<byte> bits = UsageMap.BitmapPageBits(bitmap, database.Format);
                            foreach (int p in ownedPages.Where(p => p / database.Format.UsageMapPagesPerBitmapPage == segment))
                                BitmapBits.Set(bits, p % database.Format.UsageMapPagesPerBitmapPage, true);
                            table.Channel.WritePage(bitmapPage, bitmap);
                        }
                        table.Channel.WritePage(ownedHolder, ownedBytes);
                    }
                }
            }
            using (var database = JetDatabase.Open(origin, readOnly: true))
                offered = GlobalFree(database.OpenTable("Bulk"), int.MaxValue);
            report.AppendLine($"layout={layout}; operation={operation}; before free [{Ranges(offered)}]");
            report.AppendLine($"last data page={lastDataPage}; current group={currentGroup}-{currentGroup + 7}");
            int[] counts = operation switch
            {
                _ when operation.Contains("-sentinel", StringComparison.Ordinal) => [40, 80, 100, 200],
                _ when operation.Contains("-dao", StringComparison.Ordinal) || operation.Contains("-rs", StringComparison.Ordinal) => [100],
                _ when operation.EndsWith("-tx", StringComparison.Ordinal) => [40, 100],
                _ when operation.Contains("-bulk", StringComparison.Ordinal) => [19, 40, 60, 100, 200, 300, 400],
                _ when treeRows > 0 => [18, 19, 20, 100, 200, 300, 400],
                _ when layout == "ref-exhaust" => [8],
                "index" => [1],
                // 1000 + n repeats n heap inserts with a reopen after the UPDATE.
                _ when operation.EndsWith("release", StringComparison.Ordinal) => [8, 9, 31, 32, 1032],
                _ when layout == "owner-ref" => [1, 2, 9],
                _ when longPreloaded == 64 && operation.EndsWith("chain", StringComparison.Ordinal) => [1, 11, 12],
                _ when longPreloaded == 64 => [1, 32, 33, 65],
                _ when operation.StartsWith("lv", StringComparison.Ordinal) => [1, 2, 9],
                "heap-side-heap" => [9, 12, 20],
                "heap-with-long" => [1],
                "heap-then-long" => [2],
                _ when referenceMap => [1, 2, 9, 33],
                _ when layout.StartsWith("order-", StringComparison.Ordinal) => [1, 2, 9, 17],
                _ when layout.StartsWith("preload", StringComparison.Ordinal) => [1, 2],
                _ when layout.StartsWith("after-", StringComparison.Ordinal) => [32, 33, 34],
                _ when layout.StartsWith("shift", StringComparison.Ordinal) => [1, 33],
                "index-pages" => [1, 2, 3, 8],
                "heap-then-index" or "second-table" or "index-then-heap" => [1, 32, 33],
                _ when layout is "contiguous" or "growth" => [1, 32, 33, 64, 65, 97],
                _ => [1, 2, 8, 9, 17],
            };
            foreach (int count in counts)
            {
                int releaseHeapRows = count >= 1000 ? count - 1000 : count;
                string ace = TemporaryDatabase.CopyPath(origin, "allocator-ace-");
                string open = TemporaryDatabase.CreatePath("allocator-open-");
                try
                {
                    if (operation.Contains("-dao", StringComparison.Ordinal) || operation.Contains("-rs", StringComparison.Ordinal))
                    {
                        object? daoEngine = AceTestDatabase.CreateDaoEngine();
                        Assert.NotNull(daoEngine);
                        object daoWorkspace = Invoke(daoEngine, "CreateWorkspace", "", "admin", "", 2)!;
                        object database = Invoke(daoWorkspace, "OpenDatabase", ace, false, false, "")!;
                        bool recordset = operation.Contains("-rs", StringComparison.Ordinal);
                        int depth = recordset ? 1 : operation[^1] - '0';
                        for (int level = 0; level < depth; level++) Invoke(daoWorkspace, "BeginTrans");
                        if (recordset)
                        {
                            dynamic cursor = Invoke(database, "OpenRecordset", "Indexed", 1)!;
                            if (operation[^1] == '2') cursor.Index = "ProbeIndex";
                            for (int id = treeRows; id < treeRows + count; id++)
                            {
                                cursor.AddNew();
                                cursor.Fields.Item("K").Value = $"{id:D4}{new string('x', 180)}";
                                cursor.Update();
                                if (operation[^1] != '0') cursor.Bookmark = cursor.LastModified;
                            }
                            cursor.Close();
                        }
                        else for (int id = treeRows; id < treeRows + count; id++)
                            Invoke(database, "Execute", $"INSERT INTO Indexed (K) VALUES ('{id:D4}{new string('x', 180)}')", 128);
                        for (int level = 0; level < depth; level++) Invoke(daoWorkspace, "CommitTrans", 1);
                        Invoke(database, "Close");
                        Invoke(daoWorkspace, "Close");
                        TemporaryDatabase.Delete(open);
                        File.Copy(ace, open);
                    }
                    else using (OleDbConnection connection = AceTestDatabase.Open(ace))
                    {
                        using OleDbTransaction? transaction = operation.EndsWith("-tx", StringComparison.Ordinal) ? connection.BeginTransaction() : null;
                        using OleDbCommand command = connection.CreateCommand();
                        command.Transaction = transaction;
                        if (operation is "index" or "index-then-heap")
                        {
                            command.CommandText = "CREATE INDEX ProbeIndex ON Bulk (Id)";
                            command.ExecuteNonQuery();
                        }
                        if (operation.EndsWith("release", StringComparison.Ordinal))
                        {
                            for (int id = 1; id <= 8; id++)
                            {
                                command.CommandText = $"INSERT INTO Bulk VALUES ({id}, {values})";
                                Assert.Equal(1, command.ExecuteNonQuery());
                            }
                            if (count > 8)
                            {
                                command.CommandText = "UPDATE Longs SET Payload = NULL";
                                Assert.Equal(longPreloaded + 1, command.ExecuteNonQuery());
                                if (count >= 1000)
                                {
                                    connection.Close();
                                    connection.Open();
                                }
                                for (int id = 9; id <= releaseHeapRows; id++)
                                {
                                    command.CommandText = $"INSERT INTO Bulk VALUES ({id}, {values})";
                                    Assert.Equal(1, command.ExecuteNonQuery());
                                }
                            }
                        }
                        else if (operation.StartsWith("lv", StringComparison.Ordinal))
                        {
                            bool memo = operation.EndsWith("memo", StringComparison.Ordinal);
                            int bytes = operation.EndsWith("chain", StringComparison.Ordinal) ? 9000 : 3000;
                            command.CommandText = "INSERT INTO Longs VALUES (?)";
                            command.Parameters.Add("payload", memo ? OleDbType.LongVarWChar : OleDbType.LongVarBinary).Value =
                                memo ? new string('x', 1500) : new byte[bytes];
                            for (int id = 0; id < count; id++) Assert.Equal(1, command.ExecuteNonQuery());
                        }
                        else if (treeRows > 0)
                        {
                            if (operation.EndsWith("-prime", StringComparison.Ordinal))
                            {
                                command.CommandText = $"INSERT INTO Bulk VALUES (1, {values})";
                                Assert.Equal(1, command.ExecuteNonQuery());
                            }
                            if (operation.Contains("-bulk", StringComparison.Ordinal))
                            {
                                command.CommandText = $"INSERT INTO Indexed (K) SELECT Right('0000' & (Id + {treeRows}), 4) & String(180, 'x') FROM Source WHERE Id < {count} ORDER BY Id";
                                if (operation.EndsWith("-noorder", StringComparison.Ordinal)) command.CommandText = command.CommandText.Replace(" ORDER BY Id", "", StringComparison.Ordinal);
                                Assert.Equal(count, command.ExecuteNonQuery());
                            }
                            else for (int id = treeRows; id < treeRows + count; id++)
                            {
                                command.CommandText = $"INSERT INTO Indexed (K) VALUES ('{id:D4}{new string('x', 180)}')";
                                Assert.Equal(1, command.ExecuteNonQuery());
                            }
                        }
                        else if (operation != "index")
                        {
                            int heapCount = operation == "index-pages" ? 32 : operation == "heap-then-long" ? 0 : count;
                            if (operation == "heap-then-long")
                            {
                                command.CommandText = thenLong;
                                Assert.Equal(2, command.ExecuteNonQuery());
                            }
                            for (int id = 1; id <= heapCount; id++)
                            {
                                command.CommandText = $"INSERT INTO Bulk VALUES ({id}, {values}{longValue})";
                                Assert.Equal(1, command.ExecuteNonQuery());
                                // After Bulk's first row has taken a group of its own, a small inline-map owner takes
                                // the session's shared reservation over the rest of range 1, holding it unwritten
                                // when Bulk outgrows its group and has to open range 2.
                                if (operation == "heap-side-heap" && id == 1)
                                {
                                    command.CommandText = $"INSERT INTO Side VALUES (1, {values})";
                                    command.ExecuteNonQuery();
                                }
                            }
                            if (operation == "index-pages")
                                for (int id = 600; id < 600 + count; id++)
                                {
                                    command.CommandText = $"INSERT INTO Indexed VALUES ({id})";
                                    Assert.Equal(1, command.ExecuteNonQuery());
                                }
                            if (operation is "heap-then-index" or "heap-reopen-index" or "second-table")
                            {
                                if (operation == "heap-reopen-index")
                                {
                                    connection.Close();
                                    connection.Open();
                                }
                                command.CommandText = operation == "second-table"
                                    ? $"INSERT INTO Side VALUES (1, {values})"
                                    : "CREATE INDEX ProbeIndex ON Bulk (Id)";
                                command.ExecuteNonQuery();
                            }
                        }
                        Thread.Sleep(3000);
                        transaction?.Commit();
                        using var source = new FileStream(ace, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var target = File.Create(open);
                        source.CopyTo(target);
                    }
                    HashSet<int> openFree;
                    HashSet<int> closedFree;
                    using (var database = JetDatabase.Open(open, readOnly: true))
                        openFree = GlobalFree(database.OpenTable("Bulk"), int.MaxValue);
                    using (var database = JetDatabase.Open(ace, readOnly: true))
                    {
                        Table table = database.OpenTable("Bulk");
                        closedFree = GlobalFree(table, int.MaxValue, referenceMap ? report : null);
                        int expectedPages = operation.EndsWith("release", StringComparison.Ordinal) ? preloaded + releaseHeapRows : operation.StartsWith("lv", StringComparison.Ordinal) ? preloaded : treeRows > 0 ? preloaded + (operation.EndsWith("-prime", StringComparison.Ordinal) ? 1 : 0) : operation == "small-row" ? (count + preloaded + 2) / 3 :
                            operation == "index-pages" ? 33 : operation == "index" ? preloaded : count + preloaded;
                        Assert.Equal(expectedPages, table.UsageMap.DataPages().Count());
                        report.AppendLine($"prefix={count}: Bulk data [{Ranges(table.UsageMap.DataPages())}]");
                        report.AppendLine($"  file pages={table.Channel.PageCount}; indexes [{string.Join(", ", table.Definition.Indexes.Select(i => $"{i.Name}:root={i.RootPage}"))}]");
                        if (operation is "heap-side-heap" or "heap-with-long" or "heap-then-long") report.AppendLine($"  Bulk map bitmaps {OwnerBitmaps(table)}");
                        if (withLong) report.AppendLine($"  Bulk LVAL [{Ranges(LongPages(table))}]");
                        if (operation.StartsWith("lv", StringComparison.Ordinal))
                        {
                            Table longs = database.OpenTable("Longs");
                            var pointer = longs.Definition.LongValueOwnedMaps.Values.Single();
                            int perValue = operation.EndsWith("chain", StringComparison.Ordinal) ? 3 : 1;
                            int expectedLongPages = operation.EndsWith("release", StringComparison.Ordinal)
                                ? count == 8 ? longPreloaded + 8 : 0 : longPreloaded + count * perValue;
                            Assert.Equal(expectedLongPages, longs.UsageMap.PagesInMap(pointer.Row, pointer.Page).Count());
                            report.AppendLine($"  LVAL [{Ranges(longs.UsageMap.PagesInMap(pointer.Row, pointer.Page))}]; table data [{Ranges(longs.UsageMap.DataPages())}]");
                        }
                        if (treeRows > 0)
                        {
                            Table indexed = database.OpenTable("Indexed");
                            var index = indexed.Definition.Indexes.Single();
                            report.AppendLine($"  Indexed data [{Ranges(indexed.UsageMap.DataPages())}]; index [{Ranges(indexed.UsageMap.PagesInMap(index.UsageMap.Row, index.UsageMap.Page))}]");
                        }
                    }
                    report.AppendLine($"  taken [{Ranges(offered.Except(openFree))}]; used [{Ranges(offered.Except(closedFree))}]; returned [{Ranges(closedFree.Except(openFree))}]");
                    if (operation is "heap-side-heap" or "heap-with-long" or "heap-then-long")
                    {
                        string libred = TemporaryDatabase.CopyPath(origin, "allocator-lib-");
                        try
                        {
                            using (var database = JetDatabase.Open(libred, readOnly: false))
                            {
                                var queries = new QueryEngine(database);
                                if (operation == "heap-then-long") queries.ExecuteNonQuery(thenLong);
                                else for (int id = 1; id <= count; id++)
                                {
                                    queries.ExecuteNonQuery($"INSERT INTO Bulk VALUES ({id}, {values}{longValue})");
                                    if (operation == "heap-side-heap" && id == 1) queries.ExecuteNonQuery($"INSERT INTO Side VALUES (1, {values})");
                                }
                            }
                            using var reopened = JetDatabase.Open(libred, readOnly: true);
                            Table table = reopened.OpenTable("Bulk");
                            report.Append("LibRed:");
                            GlobalFree(table, int.MaxValue, report);
                            report.AppendLine($"  file pages={table.Channel.PageCount}; Bulk data [{Ranges(table.UsageMap.DataPages())}]");
                            if (operation == "heap-side-heap") report.AppendLine($"  Side data [{Ranges(reopened.OpenTable("Side").UsageMap.DataPages())}]");
                            report.AppendLine($"  Bulk map bitmaps {OwnerBitmaps(table)}");
                            if (withLong) report.AppendLine($"  Bulk LVAL [{Ranges(LongPages(table))}]");
                        }
                        finally { TemporaryDatabase.Delete(libred); }
                    }
                    if (operation.EndsWith("release", StringComparison.Ordinal))
                        report.AppendLine($"  newly free while open [{Ranges(openFree.Except(offered))}]; reopen={count >= 1000}");
                }
                finally
                {
                    TemporaryDatabase.Delete(ace);
                    TemporaryDatabase.Delete(open);
                }
            }
            output.WriteLine(report.ToString());
        }
        finally { TemporaryDatabase.Delete(origin); }
    }

    /// <summary>Identical ordered rows in one session, varying only the number per statement. Samples both
    /// reservations and the final placement; a batch-size effect would distinguish statement state from session state.</summary>
    [Theory(Explicit = true)]
    [InlineData(1, false)]
    [InlineData(50, false)]
    [InlineData(250, false)]
    [InlineData(1000, false)]
    [InlineData(50, true)]
    [InlineData(250, true)]
    public void Statement_size_reservations(int batchSize, bool reopen) =>
        Reservations(FullColumns, LabelIndex, 0, 1000, [(240, 240), (480, 480), (610, 610), (1000, 1000)], batchSize, reopen);

    private void Reservations(string columns, string index, int preloaded, int last, (int From, int To)[] windows,
        int batchSize = 0, bool reopen = false)
    {
        object? engine = AceTestDatabase.CreateDaoEngine();
        Assert.SkipWhen(engine is null, "DAO is not registered in this bitness.");
        string origin = TemporaryDatabase.CreatePath("pagegroup-origin-");
        object workspace = Invoke(engine, "CreateWorkspace", "", "admin", "", 2)!;
        object db = Invoke(workspace, "CreateDatabase", origin, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128)!;
        Invoke(db, "Close");
        var setup = new List<string> { "CREATE TABLE Digits (D BYTE)" };
        for (int d = 0; d <= 9; d++) setup.Add($"INSERT INTO Digits (D) VALUES ({d})");
        setup.Add($"CREATE TABLE Bulk ({columns})");
        if (index.Length > 0) setup.Add(index);
        const string select =
            "INSERT INTO Bulk (Id, Grp, Label, Payload, Amount) SELECT a.D * 100 + b.D * 10 + c.D, a.D, " +
            "'Bulk label number ' & (a.D * 100 + b.D * 10 + c.D) & ' ' & String(30, 'L'), String(150, 'p'), " +
            "a.D + b.D * 0.5 FROM Digits AS a, Digits AS b, Digits AS c WHERE a.D * 100 + b.D * 10 + c.D ";
        if (preloaded > 0) setup.Add($"{select}< {preloaded} ORDER BY 1");

        var samples = new SortedSet<int>();
        samples.Add(preloaded); // the session open, nothing inserted: the state it starts from
        if (batchSize == 0)
            for (int rows = preloaded + 25; rows <= last; rows += 25) samples.Add(rows);
        foreach ((int from, int to) in windows)
            for (int rows = from; rows <= to; rows++) samples.Add(rows);

        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"statement batch size: {batchSize} (0 = one statement); reopen: {reopen}");
        try
        {
            using (OleDbConnection connection = AceTestDatabase.Open(origin))
                foreach (string statement in setup)
                {
                    using OleDbCommand command = connection.CreateCommand();
                    command.CommandText = statement;
                    command.ExecuteNonQuery();
                }

            int pageSize;
            using (FileStream stream = File.OpenRead(origin)) pageSize = JetFormatBase.Detect(stream).PageSize;
            Dictionary<int, string> previous = [];
            string previousReserved = "";
            foreach (int rows in samples)
            {
                string ace = TemporaryDatabase.CopyPath(origin, "pagegroup-res-");
                string open = TemporaryDatabase.CreatePath("pagegroup-rescopy-");
                try
                {
                    using (OleDbConnection connection = AceTestDatabase.Open(ace))
                    {
                        using OleDbCommand command = connection.CreateCommand();
                        // Not BETWEEN: Jet swaps reversed bounds, so the empty range of the first sample would not be empty.
                        int size = batchSize == 0 ? Math.Max(1, rows - preloaded) : batchSize;
                        for (int from = preloaded; from < rows; from += size)
                        {
                            command.CommandText = $"{select}>= {from} AND a.D * 100 + b.D * 10 + c.D < {Math.Min(rows, from + size)} ORDER BY 1";
                            command.ExecuteNonQuery();
                            if (reopen && from + size < rows)
                            {
                                connection.Close();
                                connection.Open();
                            }
                        }
                        Thread.Sleep(3000); // past ACE's flush timeout, so the committed pages are on disk
                        using var source = new FileStream(ace, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var target = File.Create(open);
                        source.CopyTo(target);
                    }

                    Dictionary<int, string> used = Used(open);
                    byte[] file = File.ReadAllBytes(open);
                    int pages = file.Length / pageSize;
                    HashSet<int> free;
                    using (var database = JetDatabase.Open(open, readOnly: true))
                        free = GlobalFree(database.OpenTable("Bulk"), 512);
                    int first = used.Keys.Min();
                    string reserved = Ranges(Enumerable.Range(first, 512 - first)
                        .Where(p => !free.Contains(p) && (p >= pages || PageHeader.ReadType(file.AsSpan(p * pageSize)) == 0)));
                    string freeInFile = Ranges(free.Where(p => p < pages));

                    var changes = new List<string>();
                    foreach (var (page, kind) in used.OrderBy(p => p.Key))
                        if (!previous.TryGetValue(page, out string? before) || before != kind) changes.Add($"+{page}{kind}");
                    if (changes.Count > 0 || reserved != previousReserved)
                        report.AppendLine(CultureInfo.InvariantCulture,
                            $"{rows,5} ({pages}): {string.Join(" ", changes),-28} reserved [{reserved}]  free in file [{freeInFile}]");
                    previous = used;
                    previousReserved = reserved;
                    report.AppendLine(CultureInfo.InvariantCulture, $"      after close: {Maps(ace)}\n      {Runs(ace)}");
                }
                finally
                {
                    TemporaryDatabase.Delete(ace);
                    TemporaryDatabase.Delete(open);
                }
            }
            output.WriteLine(report.ToString());
        }
        finally { TemporaryDatabase.Delete(origin); }
    }

    /// <summary>The full table's 1,000-row insert under different <c>Max Locks Per File</c> limits: the page map each
    /// leaves, to see whether intermediate commits are what start a new extent.</summary>
    [Fact(Explicit = true)]
    public void Bulk_insert_under_lock_limits()
    {
        object? engine = AceTestDatabase.CreateDaoEngine();
        Assert.SkipWhen(engine is null, "DAO is not registered in this bitness.");
        string origin = TemporaryDatabase.CreatePath("pagegroup-origin-");
        object workspace = Invoke(engine, "CreateWorkspace", "", "admin", "", 2)!;
        object db = Invoke(workspace, "CreateDatabase", origin, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128)!;
        Invoke(db, "Close");
        var setup = new List<string> { "CREATE TABLE Digits (D BYTE)" };
        for (int d = 0; d <= 9; d++) setup.Add($"INSERT INTO Digits (D) VALUES ({d})");
        setup.Add("CREATE TABLE Bulk (Id LONG CONSTRAINT pkBulk PRIMARY KEY, Grp LONG, Label TEXT(60), Payload TEXT(255), " +
                  "Code BINARY(8), Amount DECIMAL(10,2))");
        setup.Add("CREATE INDEX ixBulkLabel ON Bulk (Label)");
        var report = new StringBuilder();
        try
        {
            using (OleDbConnection connection = AceTestDatabase.Open(origin))
                foreach (string statement in setup)
                {
                    using OleDbCommand command = connection.CreateCommand();
                    command.CommandText = statement;
                    command.ExecuteNonQuery();
                }

            report.AppendLine("(OLE DB default, for comparison, is the 1000-row line of Bulk_insert_snapshots)");
            foreach (int? limit in new int?[] { null, 1_000_000, 4000, 1000 })
            {
                string ace = TemporaryDatabase.CopyPath(origin, "pagegroup-locks-");
                try
                {
                    object dao = AceTestDatabase.CreateDaoEngine()!;
                    if (limit is not null) Invoke(dao, "SetOption", 62, limit.Value); // dbMaxLocksPerFile
                    object session = Invoke(dao, "CreateWorkspace", "", "admin", "", 2)!;
                    object database = Invoke(session, "OpenDatabase", ace)!;
                    Invoke(database, "Execute",
                        "INSERT INTO Bulk (Id, Grp, Label, Payload, Amount) SELECT a.D * 100 + b.D * 10 + c.D, a.D, " +
                        "'Bulk label number ' & (a.D * 100 + b.D * 10 + c.D) & ' ' & String(30, 'L'), String(150, 'p'), " +
                        "a.D + b.D * 0.5 FROM Digits AS a, Digits AS b, Digits AS c ORDER BY 1");
                    Invoke(database, "Close");
                    Invoke(session, "Close");
                    report.AppendLine(CultureInfo.InvariantCulture, $"limit {limit?.ToString(CultureInfo.InvariantCulture) ?? "default"}: {Runs(ace)}");
                }
                finally { TemporaryDatabase.Delete(ace); }
            }
            output.WriteLine(report.ToString());
        }
        finally { TemporaryDatabase.Delete(origin); }
    }

    /// <summary>The setup session itself — DDL, the Digits inserts, then the 200-row preload — each prefix run in one
    /// session on a fresh DAO-made file and read with the session still open: the pages each step brings into use,
    /// the pages reserved, and the free pages in the file.</summary>
    [Fact(Explicit = true)]
    public void Setup_session_reservations()
    {
        object? engine = AceTestDatabase.CreateDaoEngine();
        Assert.SkipWhen(engine is null, "DAO is not registered in this bitness.");
        string origin = TemporaryDatabase.CreatePath("pagegroup-origin-");
        object workspace = Invoke(engine, "CreateWorkspace", "", "admin", "", 2)!;
        object db = Invoke(workspace, "CreateDatabase", origin, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128)!;
        Invoke(db, "Close");

        const string select =
            "INSERT INTO Bulk (Id, Grp, Label, Payload, Amount) SELECT a.D * 100 + b.D * 10 + c.D, a.D, " +
            "'Bulk label number ' & (a.D * 100 + b.D * 10 + c.D) & ' ' & String(30, 'L'), String(150, 'p'), " +
            "a.D + b.D * 0.5 FROM Digits AS a, Digits AS b, Digits AS c WHERE a.D * 100 + b.D * 10 + c.D < ";
        var steps = new List<(string Label, string Sql)> { ("CREATE Digits", "CREATE TABLE Digits (D BYTE)") };
        for (int d = 0; d <= 9; d++) steps.Add(($"Digits {d}", $"INSERT INTO Digits (D) VALUES ({d})"));
        steps.Add(("CREATE Bulk", "CREATE TABLE Bulk (Id LONG CONSTRAINT pkBulk PRIMARY KEY, Grp LONG, Label TEXT(60), " +
                   "Payload TEXT(255), Code BINARY(8), Amount DECIMAL(10,2))"));
        steps.Add(("CREATE INDEX", "CREATE INDEX ixBulkLabel ON Bulk (Label)"));
        int ddl = steps.Count;

        // Prefixes: nothing, each DDL/insert step, then the preload at a few row counts after the whole DDL.
        var runs = new List<(string Label, string[] Sql)> { ("session only", []) };
        for (int k = 1; k <= ddl; k++) runs.Add((steps[k - 1].Label, [.. steps.Take(k).Select(s => s.Sql)]));
        foreach (int rows in new[] { 9, 10, 25, 100, 180, 185, 190, 191, 192, 195, 199, 200 })
            runs.Add(($"preload {rows}", [.. steps.Select(s => s.Sql), $"{select}{rows} ORDER BY 1"]));

        var report = new StringBuilder();
        int pageSize;
        using (FileStream stream = File.OpenRead(origin)) pageSize = JetFormatBase.Detect(stream).PageSize;
        report.AppendLine(CultureInfo.InvariantCulture, $"DAO-made origin: {new FileInfo(origin).Length / pageSize} pages");
        try
        {
            Dictionary<int, string> previous = Kinds(origin);
            foreach ((string label, string[] sql) in runs)
            {
                string ace = TemporaryDatabase.CopyPath(origin, "pagegroup-setup-");
                string open = TemporaryDatabase.CreatePath("pagegroup-setupcopy-");
                try
                {
                    using (OleDbConnection connection = AceTestDatabase.Open(ace))
                    {
                        foreach (string statement in sql)
                        {
                            using OleDbCommand command = connection.CreateCommand();
                            command.CommandText = statement;
                            command.ExecuteNonQuery();
                        }
                        Thread.Sleep(3000); // past ACE's flush timeout, so the committed pages are on disk
                        using var source = new FileStream(ace, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var target = File.Create(open);
                        source.CopyTo(target);
                    }

                    Dictionary<int, string> kinds = Kinds(open);
                    byte[] file = File.ReadAllBytes(open);
                    int pages = file.Length / pageSize;
                    HashSet<int> free;
                    using (var database = JetDatabase.Open(open, readOnly: true))
                        free = GlobalFree(database.OpenTable("MSysObjects"), 512);
                    string reserved = Ranges(Enumerable.Range(1, 511)
                        .Where(p => !free.Contains(p) && (p >= pages || !kinds.ContainsKey(p))));
                    var changes = kinds.OrderBy(p => p.Key)
                        .Where(p => !previous.TryGetValue(p.Key, out string? before) || before != p.Value)
                        .Select(p => $"+{p.Key}{p.Value}");
                    report.AppendLine(CultureInfo.InvariantCulture,
                        $"{label,-14} ({pages}): {string.Join(" ", changes)}\n{"",16}reserved [{reserved}]  free in file [{Ranges(free.Where(p => p < pages))}]");
                    previous = kinds;
                }
                finally
                {
                    TemporaryDatabase.Delete(ace);
                    TemporaryDatabase.Delete(open);
                }
            }
            output.WriteLine(report.ToString());
        }
        finally { TemporaryDatabase.Delete(origin); }
    }

    /// <summary>Every page that holds something, as its kind: D/L/I with the owning TDEF page for data and index
    /// pages, T for a table definition, otherwise the type byte.</summary>
    private static Dictionary<int, string> Kinds(string path)
    {
        byte[] file = File.ReadAllBytes(path);
        JetFormatBase format = JetFormatBase.Detect(new MemoryStream(file));
        int pageSize = format.PageSize;
        var kinds = new Dictionary<int, string>();
        for (int page = 1; page < file.Length / pageSize; page++)
        {
            ReadOnlySpan<byte> p = file.AsSpan(page * pageSize, pageSize);
            if (PageHeader.ReadType(p) == 0) continue;
            int owner = PageHeader.ReadType(p) == PageType.DataPage
                ? (int)DataPage.ReadOwner(p, format)
                : IndexTree.ReadOwner(p, format);
            kinds[page] = PageHeader.ReadType(p) switch
            {
                PageType.DataPage => $"D@{owner}",
                PageType.LeafIndexPage => $"L@{owner}",
                PageType.IntermediateIndexPage => $"I@{owner}",
                PageType.TableDefinition => "T",
                _ => $"x{p[0]:X2}",
            };
        }
        return kinds;
    }

    /// <summary>File length, pages whose type byte is 0 (never written), and the pages set in the global free and
    /// released maps — wherever page 0's pointers put them — whole map range.</summary>
    private static string Maps(string path)
    {
        byte[] file = File.ReadAllBytes(path);
        using var database = JetDatabase.Open(path, readOnly: true);
        JetFormatBase format = database.Format;
        int pages = file.Length / format.PageSize;
        Table any = database.OpenTable("Bulk");
        (int freeRow, int freePage) = database.DefinitionPage.FreePagesMap;
        (int releasedRow, int releasedPage) = database.DefinitionPage.ReleasedPagesMap;
        var freeHolder = new DataPage();
        freeHolder.Read(any.Channel.ReadPageShared(freePage), any.Channel.Format);
        var releasedHolder = new DataPage();
        releasedHolder.Read(any.Channel.ReadPageShared(releasedPage), any.Channel.Format);
        var zero = Enumerable.Range(1, pages - 1).Where(p => PageHeader.ReadType(file.AsSpan(p * format.PageSize)) == 0);
        return $"{pages} pages; zero [{Ranges(zero)}]; free [{Ranges(Bits(freeHolder.GetRow(freeRow), format))}]; " +
               $"released [{Ranges(Bits(releasedHolder.GetRow(releasedRow), format))}]";

        static List<int> Bits(ReadOnlySpan<byte> map, JetFormatBase format)
        {
            var set = new List<int>();
            if (UsageMap.RecordType(map) != UsageMapType.Inline) { set.Add(-1); return set; }
            int start = UsageMap.StartPage(map, format);
            int header = format.UsageMapInlineHeaderSize;
            for (int i = header; i < map.Length; i++)
                for (int bit = 0; bit < 8; bit++)
                    if ((map[i] & (1 << bit)) != 0) set.Add(start + (i - header) * 8 + bit);
            return set;
        }
    }

    /// <summary>ACE only, the same insert cut at <c>Id &lt; rows</c> for every row count, reporting the pages each
    /// extra row brought into use: the order ACE allocates in.</summary>
    [Theory(Explicit = true)]
    [InlineData("Id LONG, Grp LONG, Label TEXT(60), Payload TEXT(255), Code BINARY(8), Amount DECIMAL(10,2)", "")]
    [InlineData("Id LONG CONSTRAINT pkBulk PRIMARY KEY, Grp LONG, Label TEXT(60), Payload TEXT(255), Code BINARY(8), Amount DECIMAL(10,2)", "")]
    [InlineData("Id LONG, Grp LONG, Label TEXT(60), Payload TEXT(255), Code BINARY(8), Amount DECIMAL(10,2)", "CREATE INDEX ixBulkLabel ON Bulk (Label)")]
    [InlineData("Id LONG CONSTRAINT pkBulk PRIMARY KEY, Grp LONG, Label TEXT(60), Payload TEXT(255), Code BINARY(8), Amount DECIMAL(10,2)", "CREATE INDEX ixBulkLabel ON Bulk (Label)")]
    public void Bulk_insert_snapshots(string columns, string index) => Snapshots(columns, index, 0);

    /// <summary>The full table, with the first 200 rows inserted by an earlier session.</summary>
    [Fact(Explicit = true)]
    public void Bulk_insert_snapshots_after_an_earlier_session() =>
        Snapshots("Id LONG CONSTRAINT pkBulk PRIMARY KEY, Grp LONG, Label TEXT(60), Payload TEXT(255), Code BINARY(8), Amount DECIMAL(10,2)",
                  "CREATE INDEX ixBulkLabel ON Bulk (Label)", 200);

    private void Snapshots(string columns, string index, int preloaded)
    {
        object? engine = AceTestDatabase.CreateDaoEngine();
        Assert.SkipWhen(engine is null, "DAO is not registered in this bitness.");
        string origin = TemporaryDatabase.CreatePath("pagegroup-origin-");
        object workspace = Invoke(engine, "CreateWorkspace", "", "admin", "", 2)!;
        object db = Invoke(workspace, "CreateDatabase", origin, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128)!;
        Invoke(db, "Close");
        var setup = new List<string> { "CREATE TABLE Digits (D BYTE)" };
        for (int d = 0; d <= 9; d++) setup.Add($"INSERT INTO Digits (D) VALUES ({d})");
        setup.Add($"CREATE TABLE Bulk ({columns})");
        if (index.Length > 0) setup.Add(index);
        const string select =
            "INSERT INTO Bulk (Id, Grp, Label, Payload, Amount) SELECT a.D * 100 + b.D * 10 + c.D, a.D, " +
            "'Bulk label number ' & (a.D * 100 + b.D * 10 + c.D) & ' ' & String(30, 'L'), String(150, 'p'), " +
            "a.D + b.D * 0.5 FROM Digits AS a, Digits AS b, Digits AS c WHERE a.D * 100 + b.D * 10 + c.D ";
        if (preloaded > 0) setup.Add($"{select}< {preloaded} ORDER BY 1");
        var report = new StringBuilder();
        try
        {
            using (OleDbConnection connection = AceTestDatabase.Open(origin))
                foreach (string statement in setup)
                {
                    using OleDbCommand command = connection.CreateCommand();
                    command.CommandText = statement;
                    command.ExecuteNonQuery();
                }
            int pageSize;
            using (FileStream stream = File.OpenRead(origin)) pageSize = JetFormatBase.Detect(stream).PageSize;
            report.AppendLine(CultureInfo.InvariantCulture, $"before: {new FileInfo(origin).Length / pageSize} pages");

            Dictionary<int, string> previous = [];
            for (int rows = preloaded + 1; rows <= 1000; rows++)
            {
                string ace = TemporaryDatabase.CopyPath(origin, "pagegroup-snap-");
                try
                {
                    using (OleDbConnection connection = AceTestDatabase.Open(ace))
                    {
                        using OleDbCommand command = connection.CreateCommand();
                        command.CommandText = $"{select}BETWEEN {preloaded} AND {rows - 1} ORDER BY 1";
                        command.ExecuteNonQuery();
                    }
                    Dictionary<int, string> used = Used(ace);
                    var changes = new List<string>();
                    foreach (var (page, kind) in used.OrderBy(p => p.Key))
                        if (!previous.TryGetValue(page, out string? before) || before != kind) changes.Add($"+{page}{kind}");
                    foreach (int page in previous.Keys.Where(p => !used.ContainsKey(p)).Order()) changes.Add($"-{page}");
                    if (changes.Count > 0)
                        report.AppendLine(CultureInfo.InvariantCulture,
                            $"{rows,5} ({new FileInfo(ace).Length / pageSize}): {string.Join(" ", changes)}");
                    previous = used;
                    if (rows % 100 == 0) report.AppendLine(CultureInfo.InvariantCulture, $"      {Runs(ace)}");
                }
                finally { TemporaryDatabase.Delete(ace); }
            }
            output.WriteLine(report.ToString());
        }
        finally { TemporaryDatabase.Delete(origin); }
    }

    /// <summary>The file from Bulk's first page on as runs: <c>51-61 D0</c> is data pages whose rows continue one
    /// another from id 0, <c>L</c> leaf pages, <c>.</c> free, with the file length and the global free pages.</summary>
    internal static string Runs(string path)
    {
        byte[] file = File.ReadAllBytes(path);
        using var database = JetDatabase.Open(path, readOnly: true);
        Table bulk = database.OpenTable("Bulk");
        int tdef = bulk.Definition.DefinitionPage;
        JetFormatBase format = database.Format;
        int pageSize = format.PageSize;
        HashSet<int> free = GlobalFree(bulk, file.Length / pageSize);
        var parts = new List<string>();
        int start = -1, startId = -1, nextId = -1; string kind = "";
        for (int page = tdef + 1; page <= file.Length / pageSize; page++)
        {
            string k; int first = -1, last = -1;
            if (page == file.Length / pageSize) k = "end";
            else
            {
                ReadOnlySpan<byte> p = file.AsSpan(page * pageSize, pageSize);
                k = PageHeader.ReadType(p) switch
                {
                    PageType.DataPage when (int)DataPage.ReadOwner(p, format) == tdef => "D",
                    PageType.LeafIndexPage => "L",
                    PageType.IntermediateIndexPage => "I",
                    _ when free.Contains(page) => ".",
                    _ => $"x{p[0]:X2}",
                };
                if (k == "D") (first, last) = IdRange(bulk, page);
            }
            bool continues = k == kind && (k != "D" || first == nextId);
            if (!continues)
            {
                if (start >= 0) parts.Add(Run(start, page - 1, kind));
                start = page; kind = k; startId = first;
            }
            if (k == "D") nextId = last + 1;
        }
        return $"{file.Length / pageSize} pages | " + string.Join(" ", parts);

        string Run(int from, int to, string what) =>
            (from == to ? $"{from}" : $"{from}-{to}") + (what == "D" ? $"D{startId}" : what);
    }

    /// <summary>Every page past Bulk's definition that holds something: D for a Bulk data page, L leaf, I
    /// intermediate, or the type byte.</summary>
    private static Dictionary<int, string> Used(string path)
    {
        byte[] file = File.ReadAllBytes(path);
        using var database = JetDatabase.Open(path, readOnly: true);
        int tdef = database.OpenTable("Bulk").Definition.DefinitionPage;
        JetFormatBase format = database.Format;
        int pageSize = format.PageSize;
        var used = new Dictionary<int, string>();
        for (int page = tdef + 1; page < file.Length / pageSize; page++)
        {
            ReadOnlySpan<byte> p = file.AsSpan(page * pageSize, pageSize);
            string? kind = PageHeader.ReadType(p) switch
            {
                PageType.DataPage when (int)DataPage.ReadOwner(p, format) == tdef => "D",
                PageType.LeafIndexPage => "L",
                PageType.IntermediateIndexPage => "I",
                _ when p[0] == 0 => null,
                _ => $"x{p[0]:X2}",
            };
            if (kind is not null) used[page] = kind;
        }
        return used;
    }

    private static (int First, int Last) IdRange(Table bulk, int page)
    {
        var data = new DataPage();
        data.Read(bulk.Channel.ReadPageShared(page), bulk.Channel.Format);
        var ids = new List<int>();
        for (int row = 0; row < data.RowCount; row++)
            if (data.Rows[row] is { IsDeleted: false, HasOverflow: false })
                ids.Add(BinaryPrimitives.ReadInt32LittleEndian(data.GetRow(row)[bulk.Channel.Format.RowColumnCountSize..]));
        return ids.Count == 0 ? (-2, -2) : (ids.Min(), ids.Max());
    }

    private static void Describe(StringBuilder report, string name, string path)
    {
        byte[] file = File.ReadAllBytes(path);
        using var database = JetDatabase.Open(path, readOnly: true);
        Table bulk = database.OpenTable("Bulk");
        int bulkTdef = bulk.Definition.DefinitionPage;
        var owned = new HashSet<int>(bulk.UsageMap.DataPages());
        var freeSpace = new HashSet<int>(bulk.UsageMap.FreeDataPages());
        JetFormatBase format = database.Format;
        int pageSize = format.PageSize;
        HashSet<int> globalFree = GlobalFree(bulk, file.Length / pageSize);

        report.AppendLine(CultureInfo.InvariantCulture,
            $"==== {name}: {file.Length / pageSize} pages; Bulk tdef {bulkTdef}; owned [{Ranges(owned)}]; " +
            $"free-space [{Ranges(freeSpace)}]; global free (in file) [{Ranges(globalFree)}]");
        for (int page = 1; page < file.Length / pageSize; page++)
        {
            ReadOnlySpan<byte> p = file.AsSpan(page * pageSize, pageSize);
            PageType type = PageHeader.ReadType(p);
            int tdef = type == PageType.DataPage ? (int)DataPage.ReadOwner(p, format) : IndexTree.ReadOwner(p, format);
            string what = type switch
            {
                PageType.DataPage when tdef == bulkTdef => $"D  {BulkIds(bulk, page)}",
                PageType.DataPage => $"d  tdef {tdef}",
                PageType.LeafIndexPage => $"{(tdef == bulkTdef ? "L" : "l")}  tdef {tdef} entries-end 0x{IndexTree.ReadFreeSpace(p, format):X}",
                PageType.IntermediateIndexPage => $"{(tdef == bulkTdef ? "I" : "i")}  tdef {tdef}",
                _ => $"type 0x{p[0]:X2}",
            };
            string flags = (owned.Contains(page) ? " owned" : "") + (freeSpace.Contains(page) ? " has-space" : "") +
                           (globalFree.Contains(page) ? " FREE" : "");
            if (page < 8 && type != PageType.DataPage) continue;
            report.AppendLine(CultureInfo.InvariantCulture, $"  {page,4} {what}{flags}");
        }
    }

    private static string BulkIds(Table bulk, int page)
    {
        var data = new DataPage();
        data.Read(bulk.Channel.ReadPageShared(page), bulk.Channel.Format);
        var ids = new List<int>();
        for (int row = 0; row < data.RowCount; row++)
        {
            if (data.Rows[row] is not { IsDeleted: false, HasOverflow: false }) continue;
            ids.Add(BinaryPrimitives.ReadInt32LittleEndian(data.GetRow(row)[bulk.Channel.Format.RowColumnCountSize..]));
        }
        return ids.Count == 0 ? "empty" : $"{ids.Count} rows ids {ids.Min()}-{ids.Max()}";
    }

    /// <summary>The global free-pages map, wherever page 0's <c>0x18</c> puts it: either map form with a set bit for
    /// a free page.</summary>
    internal static HashSet<int> GlobalFree(Table any, int pages, StringBuilder? report = null)
    {
        var page0 = new DatabaseDefinitionPage();
        page0.Read(any.Channel.ReadPageShared(0), any.Channel.Format);
        (int row, int mapPage) = page0.FreePagesMap;
        var holder = new DataPage();
        holder.Read(any.Channel.ReadPageShared(mapPage), any.Channel.Format);
        ReadOnlySpan<byte> map = holder.GetRow(row);
        if (report is not null && UsageMap.RecordType(map) == UsageMapType.Reference)
        {
            var pointers = new List<string>();
            for (int slot = 0; slot < any.Channel.Format.UsageMapReferenceSlots; slot++)
            {
                int bitmap = UsageMap.ReferencePointer(map, slot, any.Channel.Format);
                if (bitmap != 0) pointers.Add($"{slot}:{bitmap}");
            }
            report.AppendLine($"  global bitmap pointers [{string.Join(", ", pointers)}]");
        }
        return [.. UsageMap.PagesInRecord(any.Channel, map, null, "Global free map").Where(p => p < pages)];
    }

    /// <summary>The pages a table's one long-value column owns.</summary>
    private static IEnumerable<int> LongPages(Table table)
    {
        (int row, int page) = table.Definition.LongValueOwnedMaps.Values.Single();
        return table.UsageMap.PagesInMap(row, page);
    }

    /// <summary>A table's owned and free maps' reference-form bitmap pointers, slot by slot.</summary>
    private static string OwnerBitmaps(Table table)
    {
        var tdef = table.Channel.ReadPage(table.Definition.DefinitionPage);
        var parts = new List<string>();
        foreach ((string name, int offset) in new[] { ("owned", table.Channel.Format.TdefOwnedPagesOffset), ("free", table.Channel.Format.TdefFreePagesOffset) })
        {
            (int row, int page) = tdef.ReadRecordPointer(offset);
            ReadOnlySpan<byte> record = table.UsageMap.ReadRecordAt(row, page);
            var slots = new List<string>();
            if (UsageMap.RecordType(record) == UsageMapType.Reference)
                for (int slot = 0; slot < table.Channel.Format.UsageMapReferenceSlots; slot++)
                    if (UsageMap.ReferencePointer(record, slot, table.Channel.Format) is int bitmap and not 0) slots.Add($"{slot}:{bitmap}");
            parts.Add($"{name} [{string.Join(", ", slots)}]");
        }
        return string.Join("; ", parts);
    }

    private static string Ranges(IEnumerable<int> pages)
    {
        var sorted = pages.Order().ToList();
        var parts = new List<string>();
        for (int i = 0; i < sorted.Count;)
        {
            int j = i;
            while (j + 1 < sorted.Count && sorted[j + 1] == sorted[j] + 1) j++;
            parts.Add(i == j ? $"{sorted[i]}" : $"{sorted[i]}-{sorted[j]}");
            i = j + 1;
        }
        return string.Join(",", parts);
    }

    private static object? Invoke(object target, string member, params object?[] args) =>
        target.GetType().InvokeMember(member, BindingFlags.InvokeMethod, null, target, args);
}
