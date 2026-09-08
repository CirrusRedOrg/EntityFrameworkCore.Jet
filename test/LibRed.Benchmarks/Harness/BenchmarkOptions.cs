namespace LibRed.Benchmarks.Harness;

/// <summary>
/// Run-wide options set from the command line before BenchmarkDotNet starts. BenchmarkDotNet reads its
/// parameter values from static members, so the scale factors have to live somewhere it can reach them —
/// here, rather than baked into a <c>[Params]</c> attribute nobody can change without a rebuild.
/// </summary>
public static class BenchmarkOptions
{
    /// <summary>
    /// Row counts for the synthetic corpus, one benchmark run per value (<c>--scale 10000,200000</c>).
    /// One scale factor answers "how fast"; two answer "how does it grow", which is the question that
    /// catches an access-path regression — a nested loop where a hash join belongs looks fine at 10k.
    /// </summary>
    public static int[] ScaleFactors { get; set; } = [10_000];

    /// <summary>The smallest configured scale — used by the suites for which volume is beside the point
    /// (DDL, catalog open, write throughput) so they do not multiply out across every scale factor.</summary>
    public static int SmallestScale => ScaleFactors.Min();
}
