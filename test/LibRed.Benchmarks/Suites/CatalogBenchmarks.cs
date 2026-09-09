using BenchmarkDotNet.Attributes;
using LibRed.Benchmarks.Harness;
using LibRed.Catalog;

namespace LibRed.Benchmarks.Suites;

/// <summary>
/// What it costs to open a database and read its metadata — the fixed price every connection, model build and
/// scaffolding pass pays before a single row is read.
/// </summary>
/// <remarks>
/// A caveat on <see cref="OpenAndLoadCatalog"/>: it is a cold <i>decode</i>, not cold I/O. LibRed keeps a
/// per-file page cache that is released when the last handle on that path closes, so opening here really does
/// re-read and re-decode page 0, MSysObjects and every TDEF — but the operating system's own file cache stays
/// warm, and no portable benchmark can change that. Read it as "catalog decode cost", not "cost of a cold
/// start on a spinning disk".
/// <para>This class deliberately holds no long-lived handle on the corpus while that benchmark runs; if it
/// did, the shared cache would still be alive and the open would measure almost nothing.</para>
/// </remarks>
public class CatalogBenchmarks
{
    private string _path = null!;

    [GlobalSetup]
    public void Setup()
    {
        _path = Corpus.EnsureBuilt(BenchmarkOptions.SmallestScale);
        using JetDatabase warm = JetDatabase.Open(_path);
        _ = warm.Catalog.Tables.Count;
    }

    /// <summary>Open, force the catalog to load, close. The per-connection floor.</summary>
    [Benchmark]
    public int OpenAndLoadCatalog()
    {
        using JetDatabase database = JetDatabase.Open(_path);
        return database.Catalog.Tables.Count;
    }

    /// <summary>Open alone, without touching the catalog — page 0 and the format decode. The difference
    /// against <see cref="OpenAndLoadCatalog"/> is what reading MSysObjects and the TDEFs costs.</summary>
    [Benchmark]
    public int OpenOnly()
    {
        using JetDatabase database = JetDatabase.Open(_path);
        return database.Format.PageSize;
    }

    /// <summary>Walking every user table's columns and indexes off an already-loaded catalog — roughly what a
    /// scaffolding pass does before it emits anything.</summary>
    [Benchmark]
    public int EnumerateSchema()
    {
        using JetDatabase database = JetDatabase.Open(_path);
        int total = 0;
        foreach (TableDef table in database.Catalog.UserTables)
            total += table.Columns.Count + table.Indexes.Count;
        return total;
    }
}
