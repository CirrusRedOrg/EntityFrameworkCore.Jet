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

    // MSysRelationships.szColumn's type byte damaged from Text (0x0A) to 0x0D, a code no type has. The catalog reads
    // that column as text and cannot, so the relationships are refused while every table still reads — as ACE
    // refuses its foreign-key list on this damage ("Cannot perform this operation") and reads the tables (measured).
    [Fact]
    public void A_catalog_column_of_another_type_refuses_that_catalog_table_only()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "catalog-type-");
        try
        {
            int page, userTables;
            byte[] raw;
            using (var db = JetDatabase.Open(path))
            {
                userTables = db.Catalog.UserTables.Count();
                Catalog.TableDefinition relationships = db.Catalog.FindTable("MSysRelationships")!;
                page = relationships.DefinitionPage;
                raw = relationships.FindColumn("szColumn")!.RawDescriptor!;
            }
            byte[] file = File.ReadAllBytes(path);
            Formats.JetFormatBase format = Formats.JetFormatBase.FromVersionByte(file[0x14]);
            int at = file.AsSpan(page * format.PageSize, format.PageSize).IndexOf(raw);
            Assert.True(at >= 0);
            Assert.Equal((byte)Catalog.JetDataType.Text, file[page * format.PageSize + at + format.ColumnTypeOffset]);
            file[page * format.PageSize + at + format.ColumnTypeOffset] = 0x0D;
            File.WriteAllBytes(path, file);

            using var damaged = JetDatabase.Open(path);
            Assert.Throws<InvalidDataException>(() => damaged.Catalog.Relationships);
            Assert.Equal(91, damaged.OpenTable("Customers").Rows().Count());
            Assert.Equal(userTables, damaged.Catalog.UserTables.Count());
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
