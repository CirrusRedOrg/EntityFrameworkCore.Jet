using BenchmarkDotNet.Attributes;
using LibRed.Benchmarks.Harness;
using LibRed.Engine;

namespace LibRed.Benchmarks.Suites;

/// <summary>
/// The same end-to-end measurement over the tracked Northwind sample. Northwind is tiny, so these numbers are
/// dominated by fixed per-query cost rather than by data volume — which is exactly what makes them useful:
/// they are the shapes real applications emit (text primary keys, a table name with a space in it, badly
/// skewed group sizes) and they are where a fixed-overhead regression shows up first.
/// </summary>
public class NorthwindQueryBenchmarks
{
    private JetDatabase _database = null!;
    private QueryEngine _engine = null!;

    [ParamsSource(nameof(Cases))]
    public SqlCase Case { get; set; } = null!;

    public static IEnumerable<SqlCase> Cases => SqlCorpus.Northwind;

    [GlobalSetup]
    public void Setup()
    {
        _database = BenchmarkDatabase.OpenNorthwind();
        _engine = new QueryEngine(_database);
        Consume.Drain(_engine.ExecuteQuery(Case.Sql));
    }

    [GlobalCleanup]
    public void Cleanup() => _database.Dispose();

    [Benchmark]
    public long Query() => Consume.Drain(_engine.ExecuteQuery(Case.Sql));
}
