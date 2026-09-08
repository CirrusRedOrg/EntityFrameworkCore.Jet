using System.Reflection;
using BenchmarkDotNet.Running;
using LibRed.Benchmarks.Harness;

// LibRed engine benchmarks. SQL goes straight into QueryEngine — no EF Core, no ADO layer above it — so what
// is measured is the engine, not a stack.
//
//   dotnet run -c Release                          pick a suite from the menu
//   dotnet run -c Release -- --filter "*"          everything (long)
//   dotnet run -c Release -- --filter "*join.*"    one family of query shapes
//   dotnet run -c Release -- --filter "*Pipeline*" the parse/plan/execute breakdown
//   dotnet run -c Release -- --scale 10000,200000  two corpus sizes, to see growth rather than a constant
//   dotnet run -c Release -- --long                publishable iteration counts, not the quick loop
//   dotnet run -c Release -- --validate            run every corpus case once and report rows/failures
//   dotnet run -c Release -- --filter "*Ace*"      head-to-head against ACE OLE DB (Windows + ACE only)
//
// Results land in Results/ as GitHub markdown and CSV; commit them when you want a run to be diffable later.

bool longRun = false;
bool validate = false;
var passthrough = new List<string>();

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--long":
            longRun = true;
            break;
        case "--validate":
            validate = true;
            break;
        case "--scale" when i + 1 < args.Length:
            BenchmarkOptions.ScaleFactors = ParseScales(args[++i]);
            break;
        default:
            // Anything else belongs to BenchmarkDotNet (--filter, --list, --job, --runtimes, …), which
            // rejects arguments it does not know — so ours are stripped here rather than passed on.
            passthrough.Add(args[i]);
            break;
    }
}

if (validate) return CorpusValidator.Run(BenchmarkOptions.SmallestScale);

// Build every configured corpus before BenchmarkDotNet starts timing anything: generating a scale factor can
// take a while, and it must not land inside a measured setup.
foreach (int scale in BenchmarkOptions.ScaleFactors) Corpus.EnsureBuilt(scale);

BenchmarkSwitcher
    .FromAssembly(Assembly.GetExecutingAssembly())
    .Run([.. passthrough], new BenchmarkConfig(longRun));

return 0;

static int[] ParseScales(string value)
{
    int[] scales = [.. value
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(part => int.TryParse(part, out int n) && n > 0
            ? n
            : throw new ArgumentException($"--scale expects positive row counts; got '{part}'."))];

    return scales.Length > 0 ? scales : throw new ArgumentException("--scale expects at least one row count.");
}
