using System.Buffers.Binary;
using System.Data.OleDb;
using System.Globalization;
using System.Reflection;
using System.Text;
using LibRed;
using LibRed.Formats;
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

    private void Reservations(string columns, string index, int preloaded, int last, (int From, int To)[] windows)
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
        for (int rows = preloaded + 25; rows <= last; rows += 25) samples.Add(rows);
        foreach ((int from, int to) in windows)
            for (int rows = from; rows <= to; rows++) samples.Add(rows);

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
                        command.CommandText = $"{select}>= {preloaded} AND a.D * 100 + b.D * 10 + c.D < {rows} ORDER BY 1";
                        command.ExecuteNonQuery();
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
    private static string Runs(string path)
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

    /// <summary>The global free-pages map, wherever page 0's <c>0x18</c> puts it: an inline map with a set bit for
    /// a free page.</summary>
    private static HashSet<int> GlobalFree(Table any, int pages)
    {
        var page0 = new DatabaseDefinitionPage();
        page0.Read(any.Channel.ReadPageShared(0), any.Channel.Format);
        (int row, int mapPage) = page0.FreePagesMap;
        var holder = new DataPage();
        holder.Read(any.Channel.ReadPageShared(mapPage), any.Channel.Format);
        ReadOnlySpan<byte> map = holder.GetRow(row);
        var free = new HashSet<int>();
        if (UsageMap.RecordType(map) != UsageMapType.Inline) return free;
        int start = UsageMap.StartPage(map, any.Channel.Format);
        int header = any.Channel.Format.UsageMapInlineHeaderSize;
        for (int i = header; i < map.Length; i++)
            for (int bit = 0; bit < 8; bit++)
                if ((map[i] & (1 << bit)) != 0 && start + (i - header) * 8 + bit < pages) free.Add(start + (i - header) * 8 + bit);
        return free;
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