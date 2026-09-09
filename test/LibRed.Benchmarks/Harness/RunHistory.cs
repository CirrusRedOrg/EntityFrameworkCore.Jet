using System.Diagnostics;
using System.Globalization;
using System.Text;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace LibRed.Benchmarks.Harness;

/// <summary>
/// Appends one row per benchmark to a tracked tab-separated file, so a number can be compared with the same
/// number from a month ago instead of from memory.
/// </summary>
/// <remarks>
/// <para>BenchmarkDotNet's own reports are written per run and are not kept: the per-suite ones carry no
/// timestamp, so each run overwrites the last, and the whole <c>Results/</c> directory is deliberately
/// untracked so a casual run never shows up in <c>git status</c>. That leaves no way to answer "was this
/// different before?", which is the question that actually comes up.</para>
/// <para>This is the performance counterpart of <c>GreenTests/</c>: a committed baseline a later run is read
/// against. It is append-only and one line per benchmark, so a regression shows up as an added line next to
/// the old one rather than as a rewritten file, and <c>git log -p</c> on it reads as the history of a number.</para>
/// <para>Every row carries the commit and whether the tree was <b>dirty</b>, because a measurement from a tree
/// with uncommitted changes cannot be attributed to anything — treat those rows as anecdotes. It also carries
/// the machine name: these numbers are not comparable across machines, and a row that silently came from a
/// different one would be worse than no row.</para>
/// </remarks>
internal static class RunHistory
{
    private const string FileName = "History.tsv";

    private const string Header =
        "utc\tcommit\tdirty\thost\tsuite\tbenchmark\tparams\tmean_us\tstddev_us\talloc_kb";

    /// <summary>Appends every benchmark in <paramref name="summaries"/>. Silent when a run produced nothing
    /// (a filter that matched no benchmarks), and never throws — losing a history row must not fail a run that
    /// otherwise succeeded.</summary>
    public static void Append(IEnumerable<Summary> summaries)
    {
        try
        {
            var rows = new List<string>();
            string utc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            (string commit, bool dirty) = GitState();
            string host = Environment.MachineName;

            foreach (Summary summary in summaries)
            foreach (BenchmarkCase benchmark in summary.BenchmarksCases)
            {
                BenchmarkReport? report = summary[benchmark];
                if (report?.ResultStatistics is not { } stats)
                    continue;   // did not run, or ran and failed — no number to record

                rows.Add(string.Join('\t',
                    utc,
                    commit,
                    dirty ? "dirty" : "clean",
                    host,
                    benchmark.Descriptor.Type.Name,
                    benchmark.Descriptor.WorkloadMethod.Name,
                    Parameters(benchmark),
                    Number(stats.Mean / 1000.0),                        // BenchmarkDotNet reports nanoseconds
                    Number(stats.StandardDeviation / 1000.0),
                    // Null when the memory diagnoser did not run for this case; recorded as "-" rather than 0,
                    // which would read as "allocated nothing".
                    Number(report.GcStats.GetBytesAllocatedPerOperation(benchmark) is { } bytes
                        ? bytes / 1024.0
                        : double.NaN)));
            }

            if (rows.Count == 0)
                return;

            string path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", FileName);
            bool fresh = !File.Exists(path);
            using var writer = new StreamWriter(path, append: true, Encoding.UTF8);
            if (fresh) writer.WriteLine(Header);
            foreach (string row in rows) writer.WriteLine(row);

            Console.Error.WriteLine($"[history] appended {rows.Count} row(s) at {commit}{(dirty ? "+dirty" : "")}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[history] not recorded: {ex.Message}");
        }
    }

    /// <summary>The case and scale a row belongs to, flattened — <c>Case=sort.top_10 Scale=10000</c>. Tabs are
    /// the only separator that matters here, and a parameter cannot contain one.</summary>
    private static string Parameters(BenchmarkCase benchmark) =>
        benchmark.Parameters.Items.Count == 0
            ? "-"
            : string.Join(' ', benchmark.Parameters.Items.Select(p => $"{p.Name}={p.Value}"));

    private static string Number(double value) =>
        double.IsNaN(value) || double.IsInfinity(value)
            ? "-"
            : value.ToString("F2", CultureInfo.InvariantCulture);

    /// <summary>The commit these numbers describe, and whether anything was uncommitted when they were taken.
    /// Both come from git rather than from build metadata so that a dirty tree cannot be mistaken for its
    /// nearest commit.</summary>
    private static (string Commit, bool Dirty) GitState()
    {
        string commit = Git("rev-parse --short HEAD");
        // Tracked modifications only. Counting untracked files would mark every run dirty for a stray scratch
        // directory sitting in the working tree, which says nothing about the code that was measured — and a
        // dirty flag that is always set carries no information at all.
        return (commit.Length > 0 ? commit : "unknown", Git("status --porcelain --untracked-files=no").Length > 0);
    }

    private static string Git(string arguments)
    {
        try
        {
            using Process? git = Process.Start(new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = AppContext.BaseDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (git is null) return string.Empty;
            string output = git.StandardOutput.ReadToEnd();
            git.WaitForExit(10_000);
            return git.ExitCode == 0 ? output.Trim() : string.Empty;
        }
        catch
        {
            return string.Empty;   // no git on PATH, or not a repository — the row still records the numbers
        }
    }
}
