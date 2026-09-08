using BenchmarkDotNet.Attributes;
using LibRed.Benchmarks.Harness;
using LibRed.Storage;

namespace LibRed.Benchmarks.Suites;

/// <summary>
/// Schema operations: creating a database from nothing, creating tables, and building an index over data that
/// is already there. These are what a migration run and an EF model-creation pass are made of, and they are
/// the operations for which LibRed has no ACE equivalent to fall back on — it synthesises the file itself.
/// </summary>
/// <remarks>
/// Each iteration works on its own copy or its own new file, and cleans up after itself; as in
/// <see cref="WriteBenchmarks"/>, <c>[InvocationCount(1)]</c> is what makes that isolation real.
/// </remarks>
[InvocationCount(invocationCount: 1, unrollFactor: 1)]
public class DdlBenchmarks
{
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
    public void IterationSetup() => _scratch = BenchmarkDatabase.CheckoutWritable(BenchmarkOptions.SmallestScale);

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

    /// <summary>Writing a brand-new, empty <c>.accdb</c> page by page. No template file, no DAO, no ACE — this
    /// is the operation the COM-based database creators exist to do on Windows only.</summary>
    [Benchmark]
    public void CreateEmptyDatabase()
    {
        string path = Path.Combine(_scratchDirectory, $"new-{_sequence++}.accdb");
        _created.Add(path);
        DatabaseCreator.CreateEmpty(path);
    }

    /// <summary>A nine-column table with a primary key: TDEF page, catalog rows, index root.</summary>
    [Benchmark]
    public void CreateTable() =>
        _scratch.Engine.ExecuteNonQuery(
            $"""
             CREATE TABLE Ddl{_sequence++} (
                 Id LONG NOT NULL PRIMARY KEY, A LONG, B TEXT(50), C DATETIME,
                 D CURRENCY, E DOUBLE, F BIT, G TEXT(255), H LONG)
             """);

    /// <summary>Building an index over a table that already holds the corpus — the back-fill, not the empty
    /// declaration. This is the expensive half of a migration that adds an index to a live table.</summary>
    [Benchmark]
    public void CreateIndexOverExistingRows() =>
        _scratch.Engine.ExecuteNonQuery($"CREATE INDEX IX_Bench_G{_sequence++} ON Bench (G)");

    /// <summary>Adding a nullable column to a populated table.</summary>
    [Benchmark]
    public void AddColumn() =>
        _scratch.Engine.ExecuteNonQuery($"ALTER TABLE Bench ADD COLUMN Extra{_sequence++} LONG");
}
