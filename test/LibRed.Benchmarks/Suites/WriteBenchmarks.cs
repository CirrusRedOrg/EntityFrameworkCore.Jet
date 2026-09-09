using BenchmarkDotNet.Attributes;
using LibRed.Benchmarks.Harness;
using LibRed.Storage;

namespace LibRed.Benchmarks.Suites;

/// <summary>
/// Insert, update and delete throughput, on a private writable copy of the corpus.
/// </summary>
/// <remarks>
/// Writes need care that reads do not, and the shape of this class is all consequence of that:
/// <list type="bullet">
/// <item><description>Each iteration gets a <b>fresh copy</b> of the database (<c>[IterationSetup]</c>) —
/// otherwise the second iteration inserts into a table the first one already grew, and the numbers drift
/// upward for reasons that have nothing to do with the code. <c>[InvocationCount(1)]</c> keeps one timed
/// invocation per iteration so that copy actually isolates it.</description></item>
/// <item><description>Each benchmark does a <b>batch</b> of <see cref="Batch"/> statements with
/// <c>OperationsPerInvoke</c>, so the reported number is per statement while the per-iteration file copy is
/// amortised across the batch instead of dominating it.</description></item>
/// <item><description>Row ids only ever increase, across every iteration and benchmark, so a repeated
/// invocation can never collide with the primary key and turn a timing into an exception.</description></item>
/// </list>
/// Writes run at <see cref="WriteScale"/> only, independent of <c>--scale</c>. What matters here is the per-row
/// cost with indexes of a realistic depth, not how the file size scales.
/// </remarks>
[InvocationCount(invocationCount: 1, unrollFactor: 1)]
public class WriteBenchmarks
{
    /// <summary>
    /// Statements per timed invocation. Sized so one sample lasts around a second: with
    /// <c>[InvocationCount(1, 1)]</c> each iteration is a single burst, so a short batch gives a short sample,
    /// and a short sample sitting next to a file copy measures the operating system's cache mood as much as the
    /// engine. At 200 this benchmark swung ±35%; the batch is what buys that back.
    /// </summary>
    private const int Batch = 1000;

    /// <summary>
    /// Rows in the database the write benchmarks mutate — deliberately not the read corpus.
    /// </summary>
    /// <remarks>
    /// Each iteration gets a fresh copy, so the file is copied 13 times per benchmark; at the read corpus's
    /// scale that is megabytes of churn per iteration, and it lands right beside the work being timed. Small
    /// enough to copy cheaply, large enough that all four indexes still have a real multi-level B-tree — which
    /// is what these numbers are about, since index maintenance dominates a write.
    /// </remarks>
    private const int WriteScale = 2_000;

    private ScratchDatabase _scratch = null!;

    /// <summary>Ids for inserted rows, above anything the corpus contains and never reset.</summary>
    private int _nextId = 100_000_000;

    /// <summary>Rolling target for the update/delete cases, so a batch touches <see cref="Batch"/> different
    /// rows rather than the same one repeatedly (which would measure a fully cached single page).</summary>
    private int _nextTarget;

    [IterationSetup]
    public void IterationSetup()
    {
        _scratch = BenchmarkDatabase.CheckoutWritable(WriteScale);
        _nextTarget = 0;
    }

    [IterationCleanup]
    public void IterationCleanup() => _scratch.Dispose();

    /// <summary>The path an application actually takes: one <c>INSERT</c> statement at a time, each parsed,
    /// planned and autocommitted on its own.</summary>
    [Benchmark(OperationsPerInvoke = Batch)]
    public void InsertSqlAutocommit()
    {
        for (int i = 0; i < Batch; i++)
        {
            int id = _nextId++;
            _scratch.Engine.ExecuteNonQuery(
                $"INSERT INTO Bench (Id, K, Bucket, G, Flag, Amount, Ratio, Created, Label, Note) VALUES ({id}, {id % 1000}, 1, 1, False, 1.25, 0.5, #2020-01-01#, 'label-00001', 'bench')");
        }
    }

    /// <summary>The same inserts inside one explicit transaction. The difference against
    /// <see cref="InsertSqlAutocommit"/> is what the per-statement implicit transaction costs.</summary>
    [Benchmark(OperationsPerInvoke = Batch)]
    public void InsertSqlInTransaction()
    {
        _scratch.Database.BeginTransaction();
        for (int i = 0; i < Batch; i++)
        {
            int id = _nextId++;
            _scratch.Engine.ExecuteNonQuery(
                $"INSERT INTO Bench (Id, K, Bucket, G, Flag, Amount, Ratio, Created, Label, Note) VALUES ({id}, {id % 1000}, 1, 1, False, 1.25, 0.5, #2020-01-01#, 'label-00001', 'bench')");
        }

        _scratch.Database.Commit(flush: false);
    }

    /// <summary>Straight to storage, bypassing SQL entirely: row encode plus index maintenance and nothing
    /// else. The floor the two above are measured against — the gap is what the SQL pipeline costs per
    /// write.</summary>
    [Benchmark(OperationsPerInvoke = Batch)]
    public void InsertStorage()
    {
        Table table = _scratch.Database.OpenTable("Bench");
        var ordinals = table.Definition.Columns.ToDictionary(c => c.Name, c => c.Index, StringComparer.OrdinalIgnoreCase);
        var values = new object?[table.Definition.Columns.Count];

        for (int i = 0; i < Batch; i++)
        {
            int id = _nextId++;
            values[ordinals["Id"]] = id;
            values[ordinals["K"]] = id % 1000;
            values[ordinals["Bucket"]] = 1;
            values[ordinals["G"]] = 1;
            values[ordinals["Flag"]] = false;
            values[ordinals["Amount"]] = 1.25m;
            values[ordinals["Ratio"]] = 0.5;
            values[ordinals["Created"]] = new DateTime(2020, 1, 1);
            values[ordinals["Label"]] = "label-00001";
            values[ordinals["Note"]] = "bench";
            table.Insert(values);
        }
    }

    /// <summary>The same storage-level inserts wrapped in one transaction. Against <see cref="InsertStorage"/>
    /// this isolates what a transaction changes about the cost of the identical page writes — the one
    /// difference between the raw path and the SQL paths above, which run inside one either way.</summary>
    [Benchmark(OperationsPerInvoke = Batch)]
    public void InsertStorageInTransaction()
    {
        _scratch.Database.BeginTransaction();
        InsertStorage();
        _scratch.Database.Commit(flush: false);
    }

    /// <summary>Update of an unindexed column, located by primary key: seek, rewrite in place, no index
    /// maintenance.</summary>
    [Benchmark(OperationsPerInvoke = Batch)]
    public void UpdateByPrimaryKey()
    {
        for (int i = 0; i < Batch; i++)
            _scratch.Engine.ExecuteNonQuery($"UPDATE Bench SET Note = 'updated' WHERE Id = {_nextTarget++}");
    }

    /// <summary>Update of an <b>indexed</b> column. Against <see cref="UpdateByPrimaryKey"/> this isolates the
    /// cost of maintaining the B-tree — delete the old key, insert the new one.</summary>
    [Benchmark(OperationsPerInvoke = Batch)]
    public void UpdateIndexedColumn()
    {
        for (int i = 0; i < Batch; i++)
        {
            int id = _nextTarget++;
            _scratch.Engine.ExecuteNonQuery($"UPDATE Bench SET Label = 'relabel-{id}' WHERE Id = {id}");
        }
    }

    /// <summary>Delete by primary key: seek, then remove the row and its entry from every index on the
    /// table.</summary>
    [Benchmark(OperationsPerInvoke = Batch)]
    public void DeleteByPrimaryKey()
    {
        for (int i = 0; i < Batch; i++)
            _scratch.Engine.ExecuteNonQuery($"DELETE FROM Bench WHERE Id = {_nextTarget++}");
    }

    /// <summary>A single set-based update touching many rows, rather than many single-row statements. The
    /// comparison against <see cref="UpdateByPrimaryKey"/> is the per-statement overhead an application pays
    /// for not expressing the work as one statement.</summary>
    [Benchmark]
    public void UpdateSetBased() =>
        _scratch.Engine.ExecuteNonQuery("UPDATE Bench SET Note = 'bulk' WHERE K = 500");
}
