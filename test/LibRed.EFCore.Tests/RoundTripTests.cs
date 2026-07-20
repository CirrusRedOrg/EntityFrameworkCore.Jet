using LibRed.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace LibRed.EFCore.Tests;

public class Customer
{
    public string CustomerID { get; set; } = "";
    public string? CompanyName { get; set; }
    public string? City { get; set; }
}

public class NorthwindContext(DbContextOptions<NorthwindContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(b =>
        {
            b.ToTable("Customers");
            b.HasKey(c => c.CustomerID);
        });
    }
}

public class RoundTripTests
{
    private static readonly string Northwind = Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");

    private static NorthwindContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<NorthwindContext>()
            .UseLibRed($"Data Source={Northwind}")
            .Options;
        return new NorthwindContext(options);
    }

    [Fact]
    public void SaveChanges_inserts_a_new_entity()
    {
        // The path the user hit: EF Core executes the INSERT through ExecuteReader and inspects
        // RecordsAffected. Insert on a copy so the shared Northwind isn't mutated.
        string path = Path.Combine(Path.GetTempPath(), $"libred-save-{Guid.NewGuid():N}.accdb");
        File.Copy(Northwind, path);
        try
        {
            var options = new DbContextOptionsBuilder<NorthwindContext>()
                .UseLibRed($"Data Source={path}").Options;

            using (var context = new NorthwindContext(options))
            {
                context.Customers.Add(new Customer { CustomerID = "ZZTOP", CompanyName = "LibRed Co", City = "Testville" });
                Assert.Equal(1, context.SaveChanges());
            }
            using (var context = new NorthwindContext(options))
            {
                var c = context.Customers.Single(x => x.CustomerID == "ZZTOP");
                Assert.Equal("LibRed Co", c.CompanyName);
                Assert.Equal("Testville", c.City);
            }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void Provider_uses_the_libred_connection_not_oledb()
    {
        using var context = CreateContext();

        // The DbConnection EF actually uses must be ours, not the OLE DB/ODBC JetConnection.
        var dbConnection = context.Database.GetDbConnection();
        Assert.IsType<LibRedConnection>(dbConnection);

        // And the relational connection service resolved to the LibRed override.
        var relational = context.GetService<IRelationalConnection>();
        Assert.Equal("LibRedRelationalConnection", relational.GetType().Name);

        // Force the query to execute and confirm the same LibRed connection opened.
        _ = context.Customers.Where(c => c.City == "Berlin").ToList();
        Assert.IsType<LibRedConnection>(context.Database.GetDbConnection());
    }

    [Fact]
    public void EnsureCreated_uses_the_catalog_not_information_schema()
    {
        // Copy Northwind so the file exists and already has tables. EnsureCreated must consult
        // LibRed's catalog (Exists + HasTables) and return without issuing an INFORMATION_SCHEMA
        // query (which LibRed doesn't implement and which would otherwise route to DAO/ADOX).
        string path = Path.Combine(Path.GetTempPath(), $"libred-ensure-{Guid.NewGuid():N}.accdb");
        File.Copy(Northwind, path);
        try
        {
            var options = new DbContextOptionsBuilder<NorthwindContext>()
                .UseLibRed($"Data Source={path}")
                .Options;
            using var context = new NorthwindContext(options);

            // The creator service is ours.
            Assert.Equal("LibRedDatabaseCreator", context.GetService<IDatabaseCreator>().GetType().Name);

            // Existing file with tables -> EnsureCreated reports "already created" (false), no throw.
            Assert.False(context.Database.EnsureCreated());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task HasTablesAsync_honors_pre_cancellation()
    {
        using var context = CreateContext();
        var creator = Assert.IsAssignableFrom<RelationalDatabaseCreator>(
            context.GetService<IDatabaseCreator>());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => creator.HasTablesAsync(cancellation.Token));
    }

    [Fact]
    public void EnsureCreated_creates_a_new_database_natively_and_round_trips()
    {
        // A brand-new file (no copy): EnsureCreated must create the .accdb natively (DatabaseCreator),
        // then create the model's schema, then the context is usable for insert + query.
        string path = Path.Combine(Path.GetTempPath(), $"libred-newdb-{Guid.NewGuid():N}.accdb");
        var options = new DbContextOptionsBuilder<NorthwindContext>()
            .UseLibRed($"Data Source={path}").Options;
        try
        {
            using (var context = new NorthwindContext(options))
            {
                Assert.True(context.Database.EnsureCreated()); // created from scratch
                context.Customers.Add(new Customer { CustomerID = "ADA01", CompanyName = "Analytical Engines", City = "London" });
                Assert.Equal(1, context.SaveChanges());
            }
            using (var context = new NorthwindContext(options))
            {
                var c = context.Customers.Single();
                Assert.Equal("ADA01", c.CustomerID);
                Assert.Equal("Analytical Engines", c.CompanyName);
            }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void Where_query_round_trips_through_the_provider()
    {
        using var context = CreateContext();

        var berlin = context.Customers
            .Where(c => c.City == "Berlin")
            .ToList();

        var only = Assert.Single(berlin);
        Assert.Equal("ALFKI", only.CustomerID);
        Assert.Equal("Berlin", only.City);
    }

    [Fact]
    public void Parameterized_where_round_trips()
    {
        using var context = CreateContext();
        string city = "London";

        var customers = context.Customers
            .Where(c => c.City == city) // EF parameterizes the captured variable
            .ToList();

        Assert.Equal(6, customers.Count);
        Assert.All(customers, c => Assert.Equal("London", c.City));
    }

    [Fact]
    public void Ordered_projection_materializes_multiple_rows()
    {
        using var context = CreateContext();

        var customers = context.Customers
            .Where(c => c.City == "London")
            .OrderBy(c => c.CustomerID)
            .Select(c => new { c.CustomerID, c.CompanyName })
            .ToList();

        Assert.Equal(["AROUT", "BSBEV", "CONSH", "EASTC", "NORTS", "SEVES"], customers.Select(c => c.CustomerID));
        Assert.Equal("Around the Horn", customers[0].CompanyName);
    }

    [Fact]
    public void Find_by_key_round_trips()
    {
        using var context = CreateContext();
        var c = context.Customers.Single(x => x.CustomerID == "AROUT");
        Assert.Equal("Around the Horn", c.CompanyName);
    }
}
