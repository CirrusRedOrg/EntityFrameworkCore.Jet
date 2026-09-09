using BenchmarkDotNet.Attributes;
using LibRed.Benchmarks.Harness;
using LibRed.Engine;
using LibRed.Engine.Execution;
using LibRed.Engine.Plan;
using LibRed.Engine.Planning;
using LibRed.Engine.Schema;
using LibRed.Sql.Ast;
using LibRed.Sql.Binding;
using LibRed.Sql.Parsing;

namespace LibRed.Benchmarks.Suites;

/// <summary>
/// Splits <c>text → AST → bound → plan → rows</c> into the phases it is actually made of, so a regression can
/// be attributed instead of guessed at.
/// </summary>
/// <remarks>
/// This is the suite that answers the question the old one-number harness could not: a point lookup that got
/// 20% slower may have got slower in ANTLR, in index selection, or in the cursors, and those have nothing to
/// do with each other. Read the four numbers together — <see cref="Parse"/> + <see cref="BindAndPlan"/> +
/// <see cref="ExecutePrepared"/> should account for <see cref="EndToEnd"/>, and the phase that grew is the
/// one to look at.
/// <para>It also puts a number on what a prepared-statement cache would be worth: for a small result set,
/// <see cref="ExecutePrepared"/> against <see cref="EndToEnd"/> is the whole prize.</para>
/// </remarks>
public class PipelineBenchmarks
{
    private JetDatabase _database = null!;
    private QueryEngine _engine = null!;
    private ISqlParser _parser = null!;
    private Binder _binder = null!;
    private readonly QueryPlanner _planner = new();

    private SqlStatement _ast = null!;
    private PlanNode _plan = null!;

    [ParamsSource(nameof(ScaleValues))]
    public int Scale { get; set; }

    [ParamsSource(nameof(Cases))]
    public SqlCase Case { get; set; } = null!;

    public static IEnumerable<int> ScaleValues => BenchmarkOptions.ScaleFactors;

    /// <summary>A representative spread rather than the whole corpus: four phases over sixty cases is a run
    /// nobody waits for.</summary>
    public static IEnumerable<SqlCase> Cases => SqlCorpus.Representative;

    [GlobalSetup]
    public void Setup()
    {
        _database = BenchmarkDatabase.Open(Case.Source, Scale);
        _engine = new QueryEngine(_database);
        _parser = new AntlrSqlParser();
        _binder = new Binder(new CatalogSchemaProvider(_database.Catalog));

        _ast = _parser.ParseStatement(Case.Sql);
        _plan = IndexSelection.Apply(_planner.Plan(_binder.Bind(_ast)), _database.Catalog);

        Consume.Drain(_engine.ExecuteQuery(Case.Sql));
    }

    [GlobalCleanup]
    public void Cleanup() => _database.Dispose();

    /// <summary>Text → AST. Independent of the data, so it is a flat cost per statement — and on a point
    /// lookup it can be most of the total.</summary>
    [Benchmark]
    public object Parse() => _parser.ParseStatement(Case.Sql);

    /// <summary>AST → bound → plan → access paths chosen. Also data-independent, but it is where index
    /// selection lives, so this is the number that moves when the optimiser changes.</summary>
    [Benchmark]
    public object BindAndPlan() => IndexSelection.Apply(_planner.Plan(_binder.Bind(_ast)), _database.Catalog);

    /// <summary>Executing an already-built plan and draining the rows: the part that scales with the data,
    /// and the only part a prepared-statement cache would leave behind.</summary>
    [Benchmark]
    public long ExecutePrepared() =>
        Consume.Drain(new QueryExecutor(_database).ExecuteQuery(_plan));

    /// <summary>The whole pipeline, as the ADO layer drives it. Should be close to the sum of the three above
    /// plus the page scope; a gap means work is happening somewhere none of them cover.</summary>
    [Benchmark(Baseline = true)]
    public long EndToEnd() => Consume.Drain(_engine.ExecuteQuery(Case.Sql));
}
