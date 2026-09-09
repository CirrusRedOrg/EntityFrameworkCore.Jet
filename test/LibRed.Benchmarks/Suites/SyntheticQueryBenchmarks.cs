using BenchmarkDotNet.Attributes;
using LibRed.Benchmarks.Harness;
using LibRed.Engine;

namespace LibRed.Benchmarks.Suites;

/// <summary>
/// End-to-end cost of every read in <see cref="SqlCorpus.Synthetic"/> — text in, rows drained — across the
/// configured scale factors. This is the broad regression net: one number per query shape per size.
/// </summary>
/// <remarks>
/// Deliberately end-to-end. Attribution (is it the parser, the planner, or the cursors?) is
/// <see cref="PipelineBenchmarks"/>'s job on a smaller set of shapes; running the four-phase breakdown over
/// the whole corpus would quadruple a run that is already the longest one here.
/// </remarks>
public class SyntheticQueryBenchmarks
{
    private JetDatabase _database = null!;
    private QueryEngine _engine = null!;

    [ParamsSource(nameof(ScaleValues))]
    public int Scale { get; set; }

    [ParamsSource(nameof(Cases))]
    public SqlCase Case { get; set; } = null!;

    public static IEnumerable<int> ScaleValues => BenchmarkOptions.ScaleFactors;

    public static IEnumerable<SqlCase> Cases => SqlCorpus.Synthetic;

    [GlobalSetup]
    public void Setup()
    {
        _database = BenchmarkDatabase.OpenSynthetic(Scale);
        _engine = new QueryEngine(_database);
        // Touch the query once outside the measurement so the first timed run is not paying for the catalog
        // load, the ANTLR DFA warm-up, or the first read of every page it touches.
        Consume.Drain(_engine.ExecuteQuery(Case.Sql));
    }

    [GlobalCleanup]
    public void Cleanup() => _database.Dispose();

    [Benchmark]
    public long Query() => Consume.Drain(_engine.ExecuteQuery(Case.Sql));
}
