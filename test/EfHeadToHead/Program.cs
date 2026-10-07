using System.Data.Common;
using System.Data.OleDb;
using System.Diagnostics;
using EntityFrameworkCore.LibRed.Infrastructure; // LibRedSqlMode
using Microsoft.EntityFrameworkCore;               // UseJet, UseLibRed
using Microsoft.EntityFrameworkCore.Diagnostics;

// args: <Northwind.accdb> [iterations] [--filter "<glob>"]
// The same LINQ queries through EFCore.Jet over ACE (OLE DB) and LibRed.EFCore in both SQL modes, each on its own
// copy of the fixture. Every execution is split into stages by EF's own interceptors; a second pass replays the
// exact SQL EF sent through the provider's plain ADO.NET command, to separate the provider's row fetch from EF's
// materialization. --filter runs only the queries whose name matches, as LibRed.Benchmarks' --filter does:
// --filter include, --filter "*join*".
int filterAt = Array.IndexOf(args, "--filter");
string filter = filterAt >= 0 ? args[filterAt + 1] : "*";
string[] positional = filterAt >= 0 ? [.. args[..filterAt], .. args[(filterAt + 2)..]] : args;
string source = positional[0];
int iterations = positional.Length > 1 ? int.Parse(positional[1]) : 300;
string work = Path.Combine(Path.GetTempPath(), "ef-h2h");
Directory.CreateDirectory(work);

var providers = new (string Name, Action<DbContextOptionsBuilder, string> Configure)[]
{
    ("Jet (ACE OLE DB)", (o, file) => o.UseJet($"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={file};Persist Security Info=False;", OleDbFactory.Instance)),
    ("LibRed compatible", (o, file) => o.UseLibRed($"Data Source={file}", LibRedSqlMode.Compatible)),
    ("LibRed extended", (o, file) => o.UseLibRed($"Data Source={file}", LibRedSqlMode.Extended)),
};

string customerId = "ALFKI", country = "Germany", ukCountry = "UK";
int take = 10;
var queries = new (string Name, Func<Northwind, int> Run)[]
{
    ("pk_lookup", c => c.Customers.AsNoTracking().Where(x => x.CustomerID == customerId).ToList().Count),
    ("filter", c => c.Orders.AsNoTracking().Where(o => o.ShipCountry == country).ToList().Count),
    ("order_take", c => c.Products.AsNoTracking().OrderBy(p => p.ProductName).Take(take).ToList().Count),
    ("scan_830", c => c.Orders.AsNoTracking().ToList().Count),
    ("join_project", c => (from o in c.Orders join cu in c.Customers on o.CustomerID equals cu.CustomerID
                           where cu.Country == ukCountry select new { o.OrderID, cu.CompanyName }).ToList().Count),
    ("group_sum", c => c.OrderDetails.GroupBy(d => d.ProductID)
                           .Select(g => new { g.Key, Total = g.Sum(d => (int)d.Quantity) }).ToList().Count),
    ("include", c => c.Orders.AsNoTracking().Include(o => o.Details).Where(o => o.CustomerID == customerId).ToList().Count),
};
queries = [.. queries.Where(q => System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(filter, q.Name))];
if (queries.Length == 0)
{
    Console.Error.WriteLine($"No query matches --filter \"{filter}\".");
    return;
}

var report = new List<string>();
var sqlReport = new List<string>();
var firstRun = new Dictionary<(string, string), double>();
var builtOptions = new Dictionary<string, DbContextOptions<Northwind>>();

// The whole matrix twice: the first pass warms every provider's code to its optimised form and supplies the first-run
// (compile) times; only the second is measured. Warming each query in place left whichever provider ran first after
// Jet — the first to run LibRed's code at all — measured partly on unoptimised code.
for (int pass = 0; pass < 2; pass++)
foreach (var (providerName, configure) in providers)
{
    if (!builtOptions.TryGetValue(providerName, out DbContextOptions<Northwind>? built))
    {
        string file = Path.Combine(work, $"{providerName.Split(' ')[0]}-{providerName.Length}.accdb");
        File.Copy(source, file, overwrite: true);
        var options = new DbContextOptionsBuilder<Northwind>();
        configure(options, file);
        options.AddInterceptors(new CommandTimer(), new ConnectionTimer());
        builtOptions[providerName] = built = options.Options;

        // Model building, provider startup and the first connection, so no query below pays for them.
        using var warm = new Northwind(built);
        warm.Customers.AsNoTracking().Count();
    }

    foreach (var (queryName, run) in queries)
    {
        // First execution, on the first pass only: EF compiles the query (LINQ → SQL) and caches it.
        Recorder.Reset();
        long firstStart = Stopwatch.GetTimestamp();
        int rows;
        using (var ctx = new Northwind(built)) rows = run(ctx);
        if (pass == 0) firstRun[(providerName, queryName)] = Ms(Stopwatch.GetTimestamp() - firstStart);
        double firstMs = firstRun[(providerName, queryName)];

        // As many warm-up runs as measured ones: twenty left the first queries measured still on unoptimised code.
        for (int w = 0; w < iterations; w++) using (var ctx = new Northwind(built)) run(ctx);

        // Steady state: the query comes from EF's cache. Each stage summed over the iterations.
        var sum = new Stages();
        for (int i = 0; i < iterations; i++)
        {
            using var ctx = new Northwind(built);
            Recorder.Reset();
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            run(ctx);
            long end = Stopwatch.GetTimestamp();
            sum.Add(Recorder.Take(end - start, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore));
        }

        // Replay the captured SQL through the provider's own ADO.NET command on an open connection: the fetch alone.
        (double rawExec, double rawFetch) = Replay(built, iterations);

        if (pass == 0) continue;
        Stages avg = sum.Divide(iterations);
        double materialize = avg.Read - rawFetch;
        report.Add(string.Join('\t', providerName, queryName, rows, firstMs.ToString("F2"),
            Us(avg.Total), (avg.Allocated / 1024).ToString("F1"), Us(avg.Total - avg.Open - avg.Exec - avg.Read - avg.Close), Us(avg.Open), Us(avg.Exec),
            Us(rawFetch), Us(materialize), Us(avg.Close), Us(rawExec), Recorder.Commands));
        sqlReport.Add($"-- {providerName} / {queryName}  params: " + string.Join(", ", Recorder.LastParameters.Select(
            p => $"{p.Name}={p.Value} ({p.Type}, {p.Value?.GetType().Name})")) + $"\n{Recorder.LastSql}\n");
    }
}

Console.WriteLine(string.Join('\t', "provider", "query", "rows", "first_ms", "total_us", "alloc_kb", "ef_us", "open_us",
    "execute_us", "fetch_us", "materialize_us", "close_us", "raw_execute_us", "commands"));
foreach (string line in report) Console.WriteLine(line);
Console.WriteLine();
foreach (string sql in sqlReport) Console.WriteLine(sql);
return;

static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
static string Us(double ticks) => (ticks * 1_000_000.0 / Stopwatch.Frequency).ToString("F1");

// The last captured command, run again through the same provider on a connection held open, timing execute and the
// read of every value separately. Averaged in ticks.
static (double Exec, double Fetch) Replay(DbContextOptions<Northwind> options, int iterations)
{
    using var ctx = new Northwind(options);
    DbConnection connection = ctx.Database.GetDbConnection();
    connection.Open();
    long exec = 0, fetch = 0;
    for (int i = 0; i < iterations * 2; i++)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = Recorder.LastSql!;
        foreach (var (name, value, type) in Recorder.LastParameters)
        {
            DbParameter p = command.CreateParameter();
            p.ParameterName = name;
            p.Value = value;
            p.DbType = type;
            command.Parameters.Add(p);
        }
        long t0 = Stopwatch.GetTimestamp();
        using DbDataReader reader = command.ExecuteReader();
        long t1 = Stopwatch.GetTimestamp();
        // IsDBNull before GetValue, as EF's materializer reads: JetDataReader.GetValue throws on a null instead of
        // returning DBNull.
        while (reader.Read())
            for (int f = 0; f < reader.FieldCount; f++)
                if (!reader.IsDBNull(f)) reader.GetValue(f);
        long t2 = Stopwatch.GetTimestamp();
        if (i >= iterations) { exec += t1 - t0; fetch += t2 - t1; }
    }
    connection.Close();
    return ((double)exec / iterations, (double)fetch / iterations);
}

/// <summary>Timestamps of the stages of one execution, as EF's interceptors see them.</summary>
static class Recorder
{
    public static long OpenStart, OpenEnd, ExecStart, ExecEnd, ReadEnd, CloseStart, CloseEnd;
    public static long Open, Exec, Read, Close;
    public static int CommandCount;
    public static string Commands = "";
    public static string? LastSql;
    public static List<(string Name, object? Value, System.Data.DbType Type)> LastParameters = [];

    public static void Reset() { Open = Exec = Read = Close = 0; CommandCount = 0; }

    public static Stages Take(long total, long allocated) =>
        new() { Total = total, Allocated = allocated, Open = Open, Exec = Exec, Read = Read, Close = Close };
}

sealed class Stages
{
    public double Total, Allocated, Open, Exec, Read, Close;
    public void Add(Stages s) { Total += s.Total; Allocated += s.Allocated; Open += s.Open; Exec += s.Exec; Read += s.Read; Close += s.Close; }
    public Stages Divide(int n) => new() { Total = Total / n, Allocated = Allocated / n, Open = Open / n, Exec = Exec / n, Read = Read / n, Close = Close / n };
}

sealed class CommandTimer : DbCommandInterceptor
{
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Recorder.LastSql = command.CommandText;
        Recorder.LastParameters = [.. command.Parameters.Cast<DbParameter>().Select(p => (p.ParameterName, p.Value, p.DbType))];
        Recorder.CommandCount++;
        Recorder.Commands = Recorder.CommandCount.ToString();
        Recorder.ExecStart = Stopwatch.GetTimestamp();
        return result;
    }

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        Recorder.ExecEnd = Stopwatch.GetTimestamp();
        Recorder.Exec += Recorder.ExecEnd - Recorder.ExecStart;
        return result;
    }

    public override InterceptionResult DataReaderClosing(DbCommand command, DataReaderClosingEventData eventData, InterceptionResult result)
    {
        Recorder.ReadEnd = Stopwatch.GetTimestamp();
        Recorder.Read += Recorder.ReadEnd - Recorder.ExecEnd;
        return result;
    }
}

sealed class ConnectionTimer : DbConnectionInterceptor
{
    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        Recorder.OpenStart = Stopwatch.GetTimestamp();
        return result;
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        Recorder.OpenEnd = Stopwatch.GetTimestamp();
        Recorder.Open += Recorder.OpenEnd - Recorder.OpenStart;
    }

    public override InterceptionResult ConnectionClosing(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        Recorder.CloseStart = Stopwatch.GetTimestamp();
        return result;
    }

    public override void ConnectionClosed(DbConnection connection, ConnectionEndEventData eventData)
    {
        Recorder.CloseEnd = Stopwatch.GetTimestamp();
        Recorder.Close += Recorder.CloseEnd - Recorder.CloseStart;
    }
}

// --- the model: four Northwind tables, typed exactly as the fixture stores them ----------------------------------

sealed class Northwind(DbContextOptions<Northwind> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderDetail> OrderDetails => Set<OrderDetail>();
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Customer>().ToTable("Customers").HasKey(c => c.CustomerID);
        b.Entity<Order>().ToTable("Orders").HasKey(o => o.OrderID);
        b.Entity<Order>().Property(o => o.OrderID).ValueGeneratedOnAdd();
        b.Entity<Order>().Property(o => o.Freight).HasColumnType("currency");
        b.Entity<Order>().HasOne(o => o.Customer).WithMany().HasForeignKey(o => o.CustomerID);
        b.Entity<OrderDetail>().ToTable("Order Details").HasKey(d => new { d.OrderID, d.ProductID });
        b.Entity<OrderDetail>().Property(d => d.UnitPrice).HasColumnType("currency");
        b.Entity<OrderDetail>().HasOne(d => d.Order).WithMany(o => o.Details).HasForeignKey(d => d.OrderID);
        b.Entity<OrderDetail>().HasOne(d => d.Product).WithMany().HasForeignKey(d => d.ProductID);
        b.Entity<Product>().ToTable("Products").HasKey(p => p.ProductID);
        b.Entity<Product>().Property(p => p.ProductID).ValueGeneratedOnAdd();
        b.Entity<Product>().Property(p => p.UnitPrice).HasColumnType("currency");
    }
}

sealed class Customer
{
    public string CustomerID { get; set; } = "";
    public string? CompanyName { get; set; }
    public string? ContactName { get; set; }
    public string? ContactTitle { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
    public string? Phone { get; set; }
    public string? Fax { get; set; }
}

sealed class Order
{
    public int OrderID { get; set; }
    public string? CustomerID { get; set; }
    public int? EmployeeID { get; set; }
    public DateTime? OrderDate { get; set; }
    public DateTime? RequiredDate { get; set; }
    public DateTime? ShippedDate { get; set; }
    public int? ShipVia { get; set; }
    public decimal? Freight { get; set; }
    public string? ShipName { get; set; }
    public string? ShipAddress { get; set; }
    public string? ShipCity { get; set; }
    public string? ShipRegion { get; set; }
    public string? ShipPostalCode { get; set; }
    public string? ShipCountry { get; set; }
    public Customer? Customer { get; set; }
    public List<OrderDetail> Details { get; set; } = [];
}

sealed class OrderDetail
{
    public int OrderID { get; set; }
    public int ProductID { get; set; }
    public decimal UnitPrice { get; set; }
    public short Quantity { get; set; }
    public float Discount { get; set; }
    public Order? Order { get; set; }
    public Product? Product { get; set; }
}

sealed class Product
{
    public int ProductID { get; set; }
    public string? ProductName { get; set; }
    public int? SupplierID { get; set; }
    public int? CategoryID { get; set; }
    public string? QuantityPerUnit { get; set; }
    public decimal? UnitPrice { get; set; }
    public short? UnitsInStock { get; set; }
    public short? UnitsOnOrder { get; set; }
    public short? ReorderLevel { get; set; }
    public bool Discontinued { get; set; }
}