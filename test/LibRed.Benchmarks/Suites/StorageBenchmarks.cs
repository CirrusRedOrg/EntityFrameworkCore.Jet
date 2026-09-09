using BenchmarkDotNet.Attributes;
using LibRed.Benchmarks.Harness;
using LibRed.Catalog;
using LibRed.Storage;

namespace LibRed.Benchmarks.Suites;

/// <summary>
/// The storage layer on its own: cursors, index seeks and row decoding, with no SQL above them.
/// </summary>
/// <remarks>
/// These are the floor every query in <see cref="SyntheticQueryBenchmarks"/> is built on. A full scan through
/// <see cref="Table.Rows"/> here versus <c>SELECT * FROM Bench</c> there is the price of the whole SQL
/// pipeline for that shape; if the gap is large, the work to do is above the storage layer, and if the two
/// are close, no amount of planner work will help.
/// </remarks>
public class StorageBenchmarks
{
    private JetDatabase _database = null!;
    private Table _bench = null!;
    private IndexDef _primaryKey = null!;
    private IndexDef _onK = null!;

    // Seek values are addressed by the table's column ordinal, not by key position (IndexKeyEncoder reads
    // values[column.Index]), so a seek key is a full-width row with only the key columns filled in.
    private object?[] _idKey = null!;
    private object?[] _kKey = null!;
    private object?[] _kLow = null!;
    private object?[] _kHigh = null!;

    [ParamsSource(nameof(ScaleValues))]
    public int Scale { get; set; }

    public static IEnumerable<int> ScaleValues => BenchmarkOptions.ScaleFactors;

    [GlobalSetup]
    public void Setup()
    {
        _database = BenchmarkDatabase.OpenSynthetic(Scale);
        _bench = _database.OpenTable("Bench");
        _primaryKey = _bench.Definition.Indexes.First(i => i.IsPrimaryKey);
        _onK = _bench.Definition.Indexes.First(i => i.Name == "IX_Bench_K");

        _idKey = Key("Id", 4242);
        _kKey = Key("K", 500);
        _kLow = Key("K", 100);
        _kHigh = Key("K", 110);

        Consume.Drain(_bench.Rows());
    }

    private object?[] Key(string column, object value)
    {
        var key = new object?[_bench.Definition.Columns.Count];
        key[_bench.Definition.FindColumn(column)!.Index] = value;
        return key;
    }

    [GlobalCleanup]
    public void Cleanup() => _database.Dispose();

    /// <summary>Every row, decoded in full: the cost of the page walk plus <c>RowDecoder</c> for the whole
    /// table. The upper bound on how fast any unfiltered query over this table can be.</summary>
    [Benchmark]
    public long FullScan() => Consume.Drain(_bench.Rows());

    /// <summary>A single primary-key seek: B-tree descent plus one row read. Repeated at the same key, so the
    /// pages stay in the cache and the number is descent cost, not I/O.</summary>
    [Benchmark]
    public long PointSeek() => Consume.Drain(_bench.SeekRows(_primaryKey, _idKey));

    /// <summary>A non-unique equality seek: one descent, then a run of duplicate keys — <c>Bench.K</c> has
    /// <see cref="Corpus.DistinctK"/> distinct values, so this returns scale/1000 rows.</summary>
    [Benchmark]
    public long DuplicateKeySeek() => Consume.Drain(_bench.SeekRows(_onK, _kKey));

    /// <summary>A range seek over the same index: descent to the low bound then a forward walk. Ten times the
    /// rows of <see cref="DuplicateKeySeek"/>, so the two together separate descent from traversal.</summary>
    [Benchmark]
    public long RangeSeek() => Consume.Drain(_bench.SeekRangeRows(_onK, _kLow, _kHigh));
}
