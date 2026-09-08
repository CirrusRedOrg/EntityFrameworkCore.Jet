using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Order;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

namespace LibRed.Benchmarks.Harness;

/// <summary>
/// The shared BenchmarkDotNet configuration.
/// </summary>
/// <remarks>
/// Two decisions worth knowing about:
/// <list type="bullet">
/// <item><description><b>In-process toolchain.</b> The default toolchain generates and builds a throwaway
/// project per benchmark, which needs BenchmarkDotNet to recognise the target framework — it does not yet
/// know this repo's preview <c>net11.0</c>. Running in-process sidesteps that entirely and removes a
/// build per benchmark from the loop. The cost is slightly weaker isolation between benchmarks; for
/// measuring a query engine against a file, that is not the dominant noise source.</description></item>
/// <item><description><b>Short jobs by default.</b> The corpus is large enough that a full run at default
/// iteration counts takes hours. <c>--long</c> switches to the statistically stronger job for the run you
/// actually publish.</description></item>
/// </list>
/// </remarks>
public sealed class BenchmarkConfig : ManualConfig
{
    public BenchmarkConfig(bool longRun = false)
    {
        Job job = (longRun ? Job.Default : Job.Default.WithWarmupCount(3).WithIterationCount(10))
            .WithToolchain(InProcessEmitToolchain.Instance)
            .WithId(longRun ? "long" : "short");

        AddJob(job);
        AddLogger(ConsoleLogger.Default);
        AddDiagnoser(MemoryDiagnoser.Default);   // allocation per operation: the other half of "is this faster"
        AddColumnProvider(DefaultColumnProviders.Instance);

        // Markdown for reading and pasting into an issue, CSV for diffing two runs mechanically.
        AddExporter(MarkdownExporter.GitHub);
        AddExporter(CsvMeasurementsExporter.Default);
        AddExporter(CsvExporter.Default);

        // Declared order keeps the results table in corpus order (scans, then predicates, then joins, …)
        // rather than sorted by time, so two runs line up row for row in a diff.
        WithOrderer(new DefaultOrderer(SummaryOrderPolicy.Declared));
        WithOptions(ConfigOptions.JoinSummary);
        WithArtifactsPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Results"));
    }
}
