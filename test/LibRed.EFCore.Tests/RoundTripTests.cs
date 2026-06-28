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
    public void Find_by_key_round_trips()
    {
        using var context = CreateContext();
        var c = context.Customers.Single(x => x.CustomerID == "AROUT");
        Assert.Equal("Around the Horn", c.CompanyName);
    }
}
