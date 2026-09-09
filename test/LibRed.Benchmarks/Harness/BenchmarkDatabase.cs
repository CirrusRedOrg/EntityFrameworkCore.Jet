using LibRed.Engine;

namespace LibRed.Benchmarks.Harness;

/// <summary>
/// Opens the databases the suites read from, and hands out disposable writable copies for the suites that
/// mutate. Read-only opens go straight at the cached corpus — no copy, so a 1M-row scale factor costs
/// nothing per run — while anything that writes gets its own copy and deletes it afterwards.
/// </summary>
public static class BenchmarkDatabase
{
    /// <summary>The tracked Northwind sample, copied next to the benchmark assembly by the build.</summary>
    public static string NorthwindPath { get; } =
        Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");

    /// <summary>Opens the corpus at <paramref name="scale"/> read-only, building it if this is its first use.</summary>
    public static JetDatabase OpenSynthetic(int scale) =>
        JetDatabase.Open(Corpus.EnsureBuilt(scale), readOnly: true);

    /// <summary>Opens the Northwind sample read-only.</summary>
    public static JetDatabase OpenNorthwind() =>
        File.Exists(NorthwindPath)
            ? JetDatabase.Open(NorthwindPath, readOnly: true)
            : throw new FileNotFoundException(
                $"Northwind.accdb was not copied to the output directory ({NorthwindPath}). Rebuild the project.",
                NorthwindPath);

    /// <summary>Opens the database a case binds against, read-only.</summary>
    public static JetDatabase Open(CaseSource source, int scale) => source switch
    {
        CaseSource.Synthetic => OpenSynthetic(scale),
        CaseSource.Northwind => OpenNorthwind(),
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
    };

    /// <summary>
    /// A private, writable copy of the corpus at <paramref name="scale"/>, deleted on dispose. Used by the
    /// write suite, which needs a database it can leave in any state — copying is what keeps one iteration's
    /// inserts out of the next one's numbers.
    /// </summary>
    public static ScratchDatabase CheckoutWritable(int scale) => new(Corpus.EnsureBuilt(scale));
}

/// <summary>A throwaway writable copy of a database file, with an engine over it.</summary>
public sealed class ScratchDatabase : IDisposable
{
    private readonly string _path;

    internal ScratchDatabase(string template)
    {
        _path = Path.Combine(Path.GetTempPath(), "libred-bench", $"scratch-{Guid.NewGuid():N}.accdb");
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.Copy(template, _path);
        Database = JetDatabase.Open(_path, readOnly: false);
        Engine = new QueryEngine(Database);
    }

    public JetDatabase Database { get; }

    public QueryEngine Engine { get; }

    public void Dispose()
    {
        Database.Dispose();
        try { File.Delete(_path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
