using LibRed;
using Xunit;

namespace LibRed.Core.Tests;

public class CatalogTests
{
    [Fact]
    public void Enumerates_user_tables()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);

        var names = db.Catalog.UserTables.Select(t => t.Name).ToList();

        // The 12 Northwind user tables.
        string[] expected =
        [
            "Categories", "CustomerCustomerDemo", "CustomerDemographics", "Customers",
            "Employees", "Order Details", "Orders", "Products", "Region", "Shippers",
            "Suppliers", "Territories",
        ];
        Assert.All(expected, e => Assert.Contains(e, names));

        // System tables are excluded from UserTables but present overall.
        Assert.DoesNotContain("MSysObjects", names);
        Assert.Contains(db.Catalog.Tables, t => t.Name == "MSysObjects" && t.IsSystem);
    }

    [Fact]
    public void Resolves_full_column_lists()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);

        var customers = db.Catalog.FindTable("Customers");
        Assert.NotNull(customers);
        Assert.Equal(
            ["CustomerID", "CompanyName", "ContactName", "ContactTitle", "Address", "City",
             "Region", "PostalCode", "Country", "Phone", "Fax"],
            customers!.Columns.Select(c => c.Name));

        Assert.Equal(14, db.Catalog.FindTable("Orders")!.Columns.Count);
    }
}
