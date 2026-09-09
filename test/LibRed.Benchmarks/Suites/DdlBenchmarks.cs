using BenchmarkDotNet.Attributes;
using LibRed.Benchmarks.Harness;
using LibRed.Storage;

namespace LibRed.Benchmarks.Suites;

/// <summary>
/// Schema operations: creating a database from nothing, creating tables, back-filling an index over data that
/// is already there, and widening a populated table. These are what a migration run and an EF model-creation
/// pass are made of, and the first is one LibRed has no ACE fallback for — it synthesises the file itself.
/// </summary>
/// <remarks>
/// Each iteration works on its own copy or its own new file, and <c>[InvocationCount(1, 1)]</c> is what makes
/// that isolation real — one timed invocation per fresh scratch.
/// <para>The batching is not decoration. With one short operation per sample sitting next to a per-iteration
/// file copy, these measured the operating system's cache mood as much as the engine: <c>CreateEmptyDatabase</c>
/// and <c>AddColumn</c> both swung about 2x run to run. Each benchmark now does enough work per invocation to
/// last roughly a second, and the scratch is a small database rather than the read corpus, so the copy is a
/// fraction of the sample instead of comparable to it — the same fix <see cref="WriteBenchmarks"/> needed.</para>
/// <para>The batch sizes are bounded by the format, not by taste: Access allows 255 columns and 32 indexes per
/// table, so the column and index batches have to stay well inside those.</para>
/// </remarks>
[InvocationCount(invocationCount: 1, unrollFactor: 1)]
public class DdlBenchmarks
{
    /// <summary>Rows in the database these mutate — small enough to copy cheaply per iteration, large enough
    /// that an index back-fill is real work over a multi-level B-tree.</summary>
    private const int DdlScale = 2_000;

    private const int DatabaseBatch = 32;   // ~15 ms each
    private const int TableBatch = 100;     // ~1 ms each
    private const int IndexBatch = 16;      // the table starts with 4 and may hold only 32
    // Deliberately not larger: ADD COLUMN's cost grows with the column count, so a long batch would measure
    // the table getting wider rather than the operation, and the table may hold only 255 columns anyway.
    private const int ColumnBatch = 100;

    private readonly List<string> _created = [];
    private ScratchDatabase _scratch = null!;
    private string _scratchDirectory = null!;
    private int _sequence;

    [GlobalSetup]
    public void Setup()
    {
        _scratchDirectory = Path.Combine(Path.GetTempPath(), "libred-bench", "ddl");
        Directory.CreateDirectory(_scratchDirectory);
    }

    [IterationSetup]
    public void IterationSetup() => _scratch = BenchmarkDatabase.CheckoutWritable(DdlScale);

    [IterationCleanup]
    public void IterationCleanup()
    {
        _scratch.Dispose();
        foreach (string path in _created)
        {
            try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        _created.Clear();
    }

    /// <summary>Writing a brand-new, empty <c>.accdb</c> page by page. No template file, no DAO, no ACE — the
    /// operation the COM-based database creators exist to do, on Windows only.</summary>
    [Benchmark(OperationsPerInvoke = DatabaseBatch)]
    public void CreateEmptyDatabase()
    {
        for (int i = 0; i < DatabaseBatch; i++)
        {
            string path = Path.Combine(_scratchDirectory, $"new-{_sequence++}.accdb");
            _created.Add(path);
            DatabaseCreator.CreateEmpty(path);
        }
    }

    /// <summary>A nine-column table with a primary key: TDEF page, catalog rows, index root.</summary>
    [Benchmark(OperationsPerInvoke = TableBatch)]
    public void CreateTable()
    {
        for (int i = 0; i < TableBatch; i++)
            _scratch.Engine.ExecuteNonQuery(
                $"""
                 CREATE TABLE Ddl{_sequence++} (
                     Id LONG NOT NULL PRIMARY KEY, A LONG, B TEXT(50), C DATETIME,
                     D CURRENCY, E DOUBLE, F BIT, G TEXT(255), H LONG)
                 """);
    }

    /// <summary>Building an index over a table that already holds rows — the back-fill, not the empty
    /// declaration. The expensive half of a migration that indexes a live table.</summary>
    [Benchmark(OperationsPerInvoke = IndexBatch)]
    public void CreateIndexOverExistingRows()
    {
        for (int i = 0; i < IndexBatch; i++)
            _scratch.Engine.ExecuteNonQuery($"CREATE INDEX IX_Bench_G{_sequence++} ON Bench (G)");
    }

    /// <summary>Adding a nullable column to a populated table.</summary>
    [Benchmark(OperationsPerInvoke = ColumnBatch)]
    public void AddColumn()
    {
        for (int i = 0; i < ColumnBatch; i++)
            _scratch.Engine.ExecuteNonQuery($"ALTER TABLE Bench ADD COLUMN Extra{_sequence++} LONG");
    }
}
