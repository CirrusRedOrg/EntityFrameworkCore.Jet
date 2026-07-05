using EntityFrameworkCore.LibRed.Design.Internal;
using EntityFrameworkCore.LibRed.Scaffolding.Internal;
using LibRed.Data;
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
    public void Nullability_and_defaults_are_read_from_the_catalog()
    {
        var model = Factory().Create($"Data Source={Northwind}", new DatabaseModelFactoryOptions());

        var customers = model.Tables.Single(t => t.Name == "Customers");
        // A Required (NOT NULL) non-counter column reads as non-nullable; an optional one as nullable.
        Assert.False(customers.Columns.Single(c => c.Name == "CompanyName").IsNullable);
        Assert.True(customers.Columns.Single(c => c.Name == "ContactName").IsNullable);

        // A counter is never nullable — even though it carries no Required property.
        var orders = model.Tables.Single(t => t.Name == "Orders");
        Assert.False(orders.Columns.Single(c => c.Name == "OrderID").IsNullable);

        // Column DefaultValue (expression source text) is surfaced as DefaultValueSql.
        var od = model.Tables.Single(t => t.Name == "Order Details");
        Assert.Equal("1", od.Columns.Single(c => c.Name == "Quantity").DefaultValueSql);
        Assert.Equal("0", od.Columns.Single(c => c.Name == "UnitPrice").DefaultValueSql);
        Assert.Null(customers.Columns.Single(c => c.Name == "ContactName").DefaultValueSql);
    }

    [Fact]
    public void On_delete_set_null_foreign_key_scaffolds_as_set_null()
    {
        string path = Path.Combine(Path.GetTempPath(), $"scaffold-fk-{Guid.NewGuid():N}.accdb");
        File.Copy(Northwind, path);
        try
        {
            using (var conn = new LibRedConnection($"Data Source={path}"))
            {
                conn.Open();
                Exec(conn, "CREATE TABLE P (Id long PRIMARY KEY, N long)");
                Exec(conn, "CREATE TABLE C (Id long PRIMARY KEY, ParentId long, " +
                           "CONSTRAINT FK_C FOREIGN KEY (ParentId) REFERENCES P (Id) ON DELETE SET NULL)");
            }

            var model = Factory().Create($"Data Source={path}", new DatabaseModelFactoryOptions());
            var fk = model.Tables.Single(t => t.Name == "C").ForeignKeys.Single();
            Assert.Equal("P", fk.PrincipalTable.Name);
            Assert.Equal("SetNull", fk.OnDelete?.ToString());
        }
        finally { try { File.Delete(path); } catch (IOException) { } }

        static void Exec(LibRedConnection c, string sql)
        { using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
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
