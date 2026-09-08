namespace LibRed.Benchmarks.Harness;

/// <summary>Which database a case runs against.</summary>
public enum CaseSource
{
    /// <summary>The generated scale-factor corpus (<see cref="Corpus"/>): known cardinalities and selectivities.</summary>
    Synthetic,

    /// <summary>The tracked Northwind sample: small, but real-world shapes — text keys, spaces in names, skew.</summary>
    Northwind,
}

/// <summary>
/// One benchmarked statement. The name is what appears in the BenchmarkDotNet results table and what
/// <c>--filter</c> matches, so it is written as <c>category.what_it_does</c> — filtering the whole join
/// family is <c>--filter "*join.*"</c>.
/// </summary>
/// <param name="Name">Stable <c>category.name</c> id; also the display name in every report.</param>
/// <param name="Sql">The statement, in the dialect the engine is asked to run.</param>
/// <param name="Source">Which database it binds against.</param>
/// <param name="RunsOnAce">
/// False for a statement ACE cannot run (window functions, LibRed extensions). Such a case is skipped by the
/// head-to-head comparison rather than reported as an ACE failure — the whole point of several of them is that
/// ACE has no answer, see the capability-gap list in the LibRed README.
/// </param>
public sealed record SqlCase(string Name, string Sql, CaseSource Source = CaseSource.Synthetic, bool RunsOnAce = true)
{
    /// <summary>The part before the first dot — <c>join</c>, <c>agg</c>, … Used only for grouping in reports.</summary>
    public string Category => Name[..Math.Max(0, Name.IndexOf('.'))];

    /// <summary>BenchmarkDotNet displays a parameter by its <c>ToString</c>; keep it to the id alone so the
    /// results table stays readable and the filter strings stay short.</summary>
    public override string ToString() => Name;
}
