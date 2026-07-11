using System.Data.OleDb;
using System.Diagnostics;
using LibRed;
using LibRed.Engine;

// Lightweight head-to-head benchmark: LibRed's managed engine vs Microsoft ACE OLE DB, on the same .accdb
// and the same SQL. Reports the median wall-clock per operation and the LibRed/ACE ratio (goal: < 1).
//
//   dotnet run -c Release                 LibRed only (fast, reliable — the iterate-optimize loop)
//   dotnet run -c Release -- --ace        add the ACE comparison (slower, needs the ACE driver; may crash)
//   dotnet run -c Release -- --rows 100000  synthetic-table size (default 10000)

bool withAce = args.Contains("--ace");
int synRows = ReadInt(args, "--rows", 10_000);

string source = Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");
string db = Path.Combine(Path.GetTempPath(), $"bench-{Guid.NewGuid():N}.accdb");
File.Copy(source, db);

Console.WriteLine($"LibRed benchmarks — synthetic rows = {synRows:N0}, ACE comparison = {withAce}");
Console.WriteLine($"file: {db}");

// --- Setup: a synthetic indexed table alongside the real Northwind schema. -------------------------------
using (var setupDb = JetDatabase.Open(db, readOnly: false))
{
    var setup = new QueryEngine(setupDb);
    setup.ExecuteNonQuery("CREATE TABLE Bench (Id LONG PRIMARY KEY, K LONG, V TEXT(50))");
    setup.ExecuteNonQuery("CREATE INDEX IX_K ON Bench (K)"); // index built on the empty table, before the load
    var sw = Stopwatch.StartNew();
    for (int i = 0; i < synRows; i++)
        setup.ExecuteNonQuery($"INSERT INTO Bench (Id, K, V) VALUES ({i}, {i % 1000}, 'row{i}')");
    Console.WriteLine($"seeded {synRows:N0} rows in {sw.ElapsedMilliseconds:N0} ms ({synRows * 1000.0 / Math.Max(1, sw.ElapsedMilliseconds):N0} rows/s)\n");
}

// --- Read operations (identical SQL for both engines; a random PK per iteration where noted). -------------
var rnd = new Random(42);
int[] randomIds = Enumerable.Range(0, 256).Select(_ => rnd.Next(synRows)).ToArray();

var ops = new Op[]
{
    new("PK point lookup",   i => $"SELECT Id, K, V FROM Bench WHERE Id = {randomIds[i % randomIds.Length]}", Warmup: 20, Iters: 200),
    new("Repeated PK lookup", _ => "SELECT Id, K, V FROM Bench WHERE Id = 500",                               Warmup: 20, Iters: 200),
    new("Indexed range (K)", _ => "SELECT Id FROM Bench WHERE K BETWEEN 100 AND 110",                          Warmup: 10, Iters: 100),
    new("2-table join",      _ => "SELECT o.OrderID, d.ProductID FROM Orders o INNER JOIN `Order Details` d ON o.OrderID = d.OrderID", Warmup: 3, Iters: 20),
    new("GROUP BY aggregate", _ => "SELECT CustomerID, COUNT(*) AS N FROM Orders GROUP BY CustomerID",         Warmup: 3, Iters: 30),
    new("Full table scan",   _ => "SELECT Id, K, V FROM Bench",                                                Warmup: 3, Iters: 20),
};

var libredDb = JetDatabase.Open(db, readOnly: false);
var libred = new QueryEngine(libredDb);
var results = new List<(string Label, double LibRed, double? Ace, long Rows)>();

foreach (Op op in ops)
{
    long rows = 0;
    double lr = Median(op, i => rows = DrainLibRed(libred, op.Sql(i)));
    results.Add((op.Label, lr, (double?)null, rows));
}

if (withAce)
{
    using var conn = OpenAce(db);
    for (int idx = 0; idx < ops.Length; idx++)
    {
        Op op = ops[idx];
        double a = Median(op, i => DrainAce(conn, op.Sql(i)));
        results[idx] = results[idx] with { Ace = a };
    }
}

// --- Report --------------------------------------------------------------------------------------------
Console.WriteLine($"{"operation",-22}{"LibRed ms",12}{"ACE ms",12}{"ratio",10}{"rows",10}");
Console.WriteLine(new string('-', 66));
foreach (var (label, lr, ace, rows) in results)
{
    string aceCol = ace is { } a ? a.ToString("F3") : "-";
    string ratio = ace is { } av && av > 0 ? (lr / av).ToString("F2") + "x" : "-";
    Console.WriteLine($"{label,-22}{lr,12:F3}{aceCol,12}{ratio,10}{rows,10:N0}");
}

libredDb.Dispose();
try { File.Delete(db); } catch (IOException) { }
return;

// --- Helpers -------------------------------------------------------------------------------------------
static long DrainLibRed(QueryEngine e, string sql)
{
    long n = 0;
    foreach (object?[] row in e.ExecuteQuery(sql).Rows) { _ = row.Length; n++; }
    return n;
}

static void DrainAce(OleDbConnection conn, string sql)
{
    using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    using OleDbDataReader reader = cmd.ExecuteReader();
    while (reader.Read()) { }
}

// Median wall-clock over `iters` runs after `warmup` untimed runs.
static double Median(Op op, Action<int> run)
{
    for (int i = 0; i < op.Warmup; i++) run(i);
    var times = new double[op.Iters];
    for (int i = 0; i < op.Iters; i++)
    {
        var sw = Stopwatch.StartNew();
        run(i);
        times[i] = sw.Elapsed.TotalMilliseconds;
    }
    Array.Sort(times);
    return times[op.Iters / 2];
}

static OleDbConnection OpenAce(string path)
{
    foreach (string p in new[] { "Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0" })
    {
        try { var c = new OleDbConnection($"Provider={p};Data Source={path};OLE DB Services=-4;"); c.Open(); return c; }
        catch (Exception ex) when (ex is OleDbException or InvalidOperationException) { }
    }
    throw new InvalidOperationException("No Microsoft.ACE.OLEDB provider available for the --ace comparison.");
}

static int ReadInt(string[] args, string flag, int fallback)
{
    int i = Array.IndexOf(args, flag);
    return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int v) ? v : fallback;
}

internal sealed record Op(string Label, Func<int, string> Sql, int Warmup, int Iters);
