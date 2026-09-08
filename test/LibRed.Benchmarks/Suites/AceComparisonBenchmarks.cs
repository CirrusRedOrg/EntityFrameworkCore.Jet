using System.Data.OleDb;
using BenchmarkDotNet.Attributes;
using LibRed.Benchmarks.Harness;
using LibRed.Engine;

namespace LibRed.Benchmarks.Suites;

/// <summary>
/// The head-to-head: identical SQL, identical file, LibRed's managed engine against Microsoft's ACE OLE DB
/// provider. ACE is the baseline, so BenchmarkDotNet's ratio column reads directly as "how many times ACE's
/// time LibRed takes" — under 1.00 is a win.
/// </summary>
/// <remarks>
/// Windows-only and opt-in: it needs an installed ACE provider, and the ACE OLE DB provider is prone to
/// crashing the host process under sustained load (see the repo's notes on running ACE tests sequentially).
/// Nothing else in this project needs a driver, so run it deliberately:
/// <code>dotnet run -c Release -- --filter "*AceComparison*"</code>
/// <para>Only cases marked <see cref="SqlCase.RunsOnAce"/> appear here. A window function or an
/// <c>INFORMATION_SCHEMA</c> query is not a LibRed win to be measured — ACE cannot express it at all, and
/// reporting that as a failed comparison would be noise.</para>
/// </remarks>
public class AceComparisonBenchmarks
{
    private JetDatabase _database = null!;
    private QueryEngine _engine = null!;
    private OleDbConnection _ace = null!;

    [ParamsSource(nameof(Cases))]
    public SqlCase Case { get; set; } = null!;

    public static IEnumerable<SqlCase> Cases =>
        SqlCorpus.Representative.Where(c => c.RunsOnAce && c.Source == CaseSource.Synthetic);

    [GlobalSetup]
    public void Setup()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The ACE comparison needs Microsoft's OLE DB provider, which is Windows-only.");

        string path = Corpus.EnsureBuilt(BenchmarkOptions.SmallestScale);
        _database = JetDatabase.Open(path, readOnly: true);
        _engine = new QueryEngine(_database);
        _ace = OpenAce(path);

        Consume.Drain(_engine.ExecuteQuery(Case.Sql));
        RunAce();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _ace.Dispose();
        _database.Dispose();
    }

    [Benchmark(Baseline = true, Description = "ACE OLE DB")]
    public long Ace() => RunAce();

    [Benchmark(Description = "LibRed")]
    public long LibRed() => Consume.Drain(_engine.ExecuteQuery(Case.Sql));

    private long RunAce()
    {
        using OleDbCommand command = _ace.CreateCommand();
        command.CommandText = Case.Sql;
        using OleDbDataReader reader = command.ExecuteReader();
        return Consume.Drain(reader);
    }

    /// <summary>Opens the newest installed ACE provider. <c>OLE DB Services=-4</c> disables the OLE DB
    /// resource pooler, so a benchmark measures the provider and not the pool.</summary>
    private static OleDbConnection OpenAce(string path)
    {
        foreach (string provider in new[] { "Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0" })
        {
            try
            {
                var connection = new OleDbConnection($"Provider={provider};Data Source={path};OLE DB Services=-4;");
                connection.Open();
                return connection;
            }
            catch (Exception ex) when (ex is OleDbException or InvalidOperationException)
            {
                // Not installed, or this bitness of it is not — try the next.
            }
        }

        throw new InvalidOperationException(
            "No Microsoft.ACE.OLEDB provider is available. Install the Access Database Engine matching this "
            + "process's bitness, or run without the ACE comparison.");
    }
}
