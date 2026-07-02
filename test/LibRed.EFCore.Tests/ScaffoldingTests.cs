using EntityFrameworkCore.LibRed.Scaffolding.Internal;
using LibRed.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Scaffolding;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LibRed.EFCore.Tests;

public class ScaffoldingTests
{
    private static readonly string Northwind = Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb");

    // Resolve the factory through the design-time container so the scaffolding logger is injected.
    private static IDatabaseModelFactory Factory()
    {
        var services = new ServiceCollection();
        new LibRedDesignTimeServices().ConfigureDesignTimeServices(services);
        return services.BuildServiceProvider().GetRequiredService<IDatabaseModelFactory>();
    }

    [Fact]
    public void Model_factory_reads_tables_columns_keys_from_the_catalog()
    {
        var model = Factory().Create($"Data Source={Northwind}", new DatabaseModelFactoryOptions());

        // User tables are present; system (MSys*) tables are excluded.
        var tableNames = model.Tables.Select(t => t.Name).ToList();
        Assert.Contains("Customers", tableNames);
        Assert.Contains("Orders", tableNames);
        Assert.DoesNotContain(tableNames, n => n.StartsWith("MSys", StringComparison.OrdinalIgnoreCase));

        var orders = model.Tables.Single(t => t.Name == "Orders");

        // Columns with store types.
        var orderId = orders.Columns.Single(c => c.Name == "OrderID");
        Assert.Equal("counter", orderId.StoreType); // autonumber PK
        Assert.Equal("datetime", orders.Columns.Single(c => c.Name == "OrderDate").StoreType);
        Assert.StartsWith("varchar(", orders.Columns.Single(c => c.Name == "CustomerID").StoreType);

        // Primary key.
        Assert.NotNull(orders.PrimaryKey);
        Assert.Equal(["OrderID"], orders.PrimaryKey!.Columns.Select(c => c.Name));

        // Foreign keys: Orders references Customers and Employees.
        var principals = orders.ForeignKeys.Select(f => f.PrincipalTable.Name).ToList();
        Assert.Contains("Customers", principals);
        Assert.Contains("Employees", principals);
    }

    [Fact]
    public void Composite_primary_key_is_read()
    {
        var model = Factory()
            .Create($"Data Source={Northwind}", new DatabaseModelFactoryOptions());

        var orderDetails = model.Tables.Single(t => t.Name == "Order Details");
        Assert.Equal(["OrderID", "ProductID"], orderDetails.PrimaryKey!.Columns.Select(c => c.Name));
    }

    [Fact]
    public void Table_filter_is_honoured()
    {
        var model = Factory()
            .Create($"Data Source={Northwind}",
                new DatabaseModelFactoryOptions(tables: ["Shippers"], schemas: []));

        Assert.Equal(["Shippers"], model.Tables.Select(t => t.Name));
    }

    [Fact]
    public void Design_time_services_resolve_the_libred_model_factory()
    {
        var services = new ServiceCollection();
        new LibRedDesignTimeServices().ConfigureDesignTimeServices(services);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<LibRedDatabaseModelFactory>(provider.GetRequiredService<IDatabaseModelFactory>());
    }
}
