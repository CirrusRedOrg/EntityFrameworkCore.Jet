using System.Diagnostics;
using LibRed.Engine;
using LibRed.Storage;

namespace LibRed.Benchmarks.Harness;

/// <summary>
/// Builds the synthetic benchmark database: three tables whose cardinalities and selectivities are known
/// exactly, at a caller-chosen scale factor, generated deterministically so two runs of the same commit
/// measure the same bytes.
/// </summary>
/// <remarks>
/// Two deliberate choices, both of which change the numbers:
/// <list type="bullet">
/// <item><description>Rows are loaded through <see cref="Table.Insert"/> — the storage API — not through
/// <c>INSERT</c> statements. A million SQL inserts would spend most of their time in ANTLR, and building the
/// corpus is not what we are measuring. (The write suite times the SQL path on purpose; that is a different
/// question.)</description></item>
/// <item><description>Secondary indexes are built <b>after</b> the load, so their B-trees are laid out
/// compactly rather than grown page-split by page-split. That is the best case for a seek, and it is the
/// state a compacted database is in — a benchmark against an incrementally-grown index would be measuring
/// fragmentation history rather than the seek.</description></item>
/// </list>
/// The file is generated from scratch by LibRed itself (<see cref="DatabaseCreator.CreateEmpty"/>), so the
/// corpus needs no Access engine and the benchmarks build on Linux and macOS exactly as on Windows.
/// </remarks>
public static class Corpus
{
    /// <summary>Bump when the schema or the generated data changes — it is part of the cache file name, so a
    /// stale corpus from an older shape is never silently reused.</summary>
    private const int SchemaVersion = 2;

    /// <summary>Rows in <c>BenchSmall</c>, at every scale factor: the small side of a hash join.</summary>
    public const int SmallRows = 100;

    /// <summary>Distinct values of <c>Bench.K</c> — the indexed, low-cardinality grouping/seek column.</summary>
    public const int DistinctK = 1000;

    /// <summary>Distinct values of <c>Bench.Label</c> — the indexed text column.</summary>
    public const int DistinctLabels = 5000;

    /// <summary>Distinct values of <c>Bench.Bucket</c>, the second column of the composite index.</summary>
    public const int Buckets = 8;

    /// <summary>Distinct values of the unindexed <c>Bench.G</c>, as a fraction of the scale factor — the
    /// high-cardinality grouping column, four rows per group.</summary>
    public static int DistinctG(int scale) => Math.Max(2, scale / 4);

    /// <summary>Every row's <c>Created</c> is this many minutes after the epoch below, so a one-day window is
    /// a predictable slice of the table.</summary>
    private static readonly DateTime DateEpoch = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    private static readonly object BuildGate = new();

    /// <summary>
    /// Returns the path of a read-only corpus at <paramref name="scale"/>, building and caching it on first
    /// use. Concurrent callers (BenchmarkDotNet can start several) share one build.
    /// </summary>
    public static string EnsureBuilt(int scale)
    {
        string cached = CachePath(scale);
        if (File.Exists(cached)) return cached;

        lock (BuildGate)
        {
            if (File.Exists(cached)) return cached;

            Directory.CreateDirectory(Path.GetDirectoryName(cached)!);
            // Build under a unique name and move into place, so a run interrupted half-way through a
            // 1M-row load cannot leave a truncated file that the next run happily benchmarks.
            string staging = cached + "." + Environment.ProcessId + ".tmp";
            try
            {
                Build(staging, scale);
                File.Move(staging, cached, overwrite: true);
            }
            finally
            {
                if (File.Exists(staging)) TryDelete(staging);
            }

            return cached;
        }
    }

    /// <summary>Where a corpus of this scale lives once built. Outside the repo — it is regenerable, and at
    /// the larger scale factors it is far too big to track.</summary>
    public static string CachePath(int scale) =>
        Path.Combine(Path.GetTempPath(), "libred-bench", $"corpus-v{SchemaVersion}-sf{scale}.accdb");

    private static void Build(string path, int scale)
    {
        var sw = Stopwatch.StartNew();
        DatabaseCreator.CreateEmpty(path);

        using var db = JetDatabase.Open(path, readOnly: false);
        var engine = new QueryEngine(db);

        // Tables first, indexes last — see the remarks above.
        engine.ExecuteNonQuery(
            """
            CREATE TABLE Bench (
                Id LONG NOT NULL, K LONG NOT NULL, Bucket LONG NOT NULL, G LONG NOT NULL,
                Flag BIT NOT NULL, Amount CURRENCY, Ratio DOUBLE, Created DATETIME,
                Label TEXT(50), Note TEXT(200))
            """);
        engine.ExecuteNonQuery(
            "CREATE TABLE BenchChild (ChildId LONG NOT NULL, ParentId LONG NOT NULL, Qty LONG, Price CURRENCY, Descr TEXT(40))");
        engine.ExecuteNonQuery(
            "CREATE TABLE BenchSmall (SmallId LONG NOT NULL, K LONG NOT NULL, Descr TEXT(30))");

        FillBench(db, scale);
        FillChild(db, scale);
        FillSmall(db);

        db.CreateIndex("Bench", "PK_Bench", [("Id", false)], isUnique: true, isPrimary: true);
        db.CreateIndex("Bench", "IX_Bench_K", [("K", false)]);
        db.CreateIndex("Bench", "IX_Bench_KBucket", [("K", false), ("Bucket", false)]);
        db.CreateIndex("Bench", "IX_Bench_Label", [("Label", false)]);
        db.CreateIndex("BenchChild", "PK_BenchChild", [("ChildId", false)], isUnique: true, isPrimary: true);
        db.CreateIndex("BenchChild", "IX_Child_Parent", [("ParentId", false)]);
        db.CreateIndex("BenchSmall", "PK_BenchSmall", [("SmallId", false)], isUnique: true, isPrimary: true);

        Console.Error.WriteLine(
            $"[corpus] built scale {scale:N0} ({scale * 3 + SmallRows:N0} rows) in {sw.Elapsed.TotalSeconds:N1}s → {path}");
    }

    private static void FillBench(JetDatabase db, int scale)
    {
        Table table = db.OpenTable("Bench");
        var row = new RowWriter(table);
        var rnd = new Random(42);
        int distinctG = DistinctG(scale);

        for (int i = 0; i < scale; i++)
        {
            row.Set("Id", i);
            row.Set("K", i % DistinctK);
            // Bucket varies WITHIN a K group (K repeats every DistinctK rows, so i / DistinctK is the group's
            // row number). If both were i % something the composite index would be pointless: every row with
            // a given K would share one Bucket and the second key column would never discriminate.
            row.Set("Bucket", i / DistinctK % Buckets);
            row.Set("G", i % distinctG);
            row.Set("Flag", i % 7 == 0);
            row.Set("Amount", Math.Round((decimal)(i % 10_000) / 100m, 2));
            row.Set("Ratio", rnd.NextDouble() * 1000.0);
            row.Set("Created", DateEpoch.AddMinutes(i));
            row.Set("Label", $"label-{i % DistinctLabels:D5}");
            // One row in eight has no note, so IS NULL and the LIKE '%…%' scan both have something to find.
            row.Set("Note", i % 8 == 0 ? null : Note(rnd, i));
            row.Insert();
        }
    }

    private static void FillChild(JetDatabase db, int scale)
    {
        Table table = db.OpenTable("BenchChild");
        var row = new RowWriter(table);

        // Two children per parent: a join with real fan-out, and a predictable 2x row count.
        for (int i = 0; i < scale * 2; i++)
        {
            row.Set("ChildId", i);
            row.Set("ParentId", i / 2);
            row.Set("Qty", i % 13);
            row.Set("Price", Math.Round((decimal)(i % 500) / 4m, 2));
            row.Set("Descr", $"child-{i % 997:D4}");
            row.Insert();
        }
    }

    private static void FillSmall(JetDatabase db)
    {
        Table table = db.OpenTable("BenchSmall");
        var row = new RowWriter(table);

        for (int i = 0; i < SmallRows; i++)
        {
            row.Set("SmallId", i);
            row.Set("K", i);
            row.Set("Descr", $"small-{i:D3}");
            row.Insert();
        }
    }

    /// <summary>A note whose text is stable per row and contains the <c>qx</c> needle in roughly one row in
    /// twenty — enough for the unanchored-LIKE case to return rows without returning most of the table.</summary>
    private static string Note(Random rnd, int i)
    {
        string body = $"note {i} {rnd.Next(100_000):D5} lorem ipsum dolor sit amet consectetur";
        return i % 20 == 0 ? body + " qx" : body;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>
    /// Writes rows by column name into the value array <see cref="Table.Insert"/> wants (aligned to
    /// <c>ColumnDef.Index</c>), reusing one buffer for the whole load. Named access keeps the generator
    /// readable and immune to a column being reordered in the DDL.
    /// </summary>
    private sealed class RowWriter(Table table)
    {
        private readonly Dictionary<string, int> _ordinals =
            table.Definition.Columns.ToDictionary(c => c.Name, c => c.Index, StringComparer.OrdinalIgnoreCase);

        private readonly object?[] _values = new object?[table.Definition.Columns.Count];

        public void Set(string column, object? value) => _values[_ordinals[column]] = value;

        public void Insert() => table.Insert(_values);
    }
}
