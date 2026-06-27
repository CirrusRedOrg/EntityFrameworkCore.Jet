using LibRed;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

public class IndexTests
{
    [Fact]
    public void Parses_index_definitions()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);

        var categories = db.Catalog.FindTable("Categories")!;
        Assert.Equal(2, categories.Indexes.Count);

        var pk = Assert.Single(categories.Indexes, i => i.IsPrimaryKey);
        Assert.Equal("PK_Categories", pk.Name);
        Assert.True(pk.IsUnique);
        Assert.Equal(["CategoryID"], pk.Columns.Select(c => c.Column.Name));
        Assert.True(pk.RootPage > 0);

        var byName = Assert.Single(categories.Indexes, i => !i.IsPrimaryKey);
        Assert.Equal("CategoryName", byName.Name);
        Assert.False(byName.IsUnique);
        Assert.Equal(["CategoryName"], byName.Columns.Select(c => c.Column.Name));
    }

    [Fact]
    public void Resolves_index_names_including_relationships()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);

        string[] Names(string table) =>
            db.Catalog.FindTable(table)!.Indexes.Select(i => i.Name).OrderBy(n => n).ToArray();

        // Composite primary key.
        var orderDetails = db.Catalog.FindTable("Order Details")!;
        var pk = Assert.Single(orderDetails.Indexes, i => i.IsPrimaryKey);
        Assert.Equal("PK_Order_Details", pk.Name);
        Assert.Equal(["OrderID", "ProductID"], pk.Columns.Select(c => c.Column.Name));

        // Real-index names win over the relationship sharing the same data block, and
        // relationship/FK indexes keep their own names.
        Assert.Contains("PK_Orders", Names("Orders"));
        Assert.Contains("ShippersOrders", Names("Orders"));            // relationship to Shippers
        Assert.Contains("FK_Employees_Employees", Names("Employees")); // self-referencing FK
        Assert.All(db.Catalog.FindTable("Customers")!.Indexes, i => Assert.NotEqual("", i.Name));
    }

    [Fact]
    public void Traverses_leaf_index_in_key_order()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);

        var table = db.OpenTable("Categories");
        var pk = table.Definition.Indexes.First(i => i.IsPrimaryKey);
        int idIdx = table.Definition.Columns.First(c => c.Name == "CategoryID").Index;
        var decoder = NewDecoder(db, table);

        var ids = new IndexCursor(table.Channel, pk.RootPage)
            .RowIds()
            .Select(r => (int)decoder.Decode(db.ReadDataPage(r.Page).GetRow(r.Row))[idIdx]!)
            .ToList();

        Assert.Equal(Enumerable.Range(1, 8), ids);
    }

    [Fact]
    public void Traverses_multilevel_index_in_key_order()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);

        // Orders' primary key is large enough that its B-tree root is a node page,
        // exercising recursion into child entries and the child-tail page.
        var table = db.OpenTable("Orders");
        var pk = table.Definition.Indexes.First(i => i.IsPrimaryKey && i.Columns.Count == 1
                                                     && i.Columns[0].Column.Name == "OrderID");
        int idIdx = table.Definition.Columns.First(c => c.Name == "OrderID").Index;
        var decoder = NewDecoder(db, table);

        var ids = new IndexCursor(table.Channel, pk.RootPage)
            .RowIds()
            .Select(r => (int)decoder.Decode(db.ReadDataPage(r.Page).GetRow(r.Row))[idIdx]!)
            .ToList();

        Assert.Equal(830, ids.Count);
        Assert.Equal(ids.OrderBy(x => x), ids); // strictly ascending OrderID
    }

    [Theory]
    [InlineData("Categories")]
    [InlineData("Order Details")] // node-rooted indexes
    [InlineData("Orders")]
    public void Every_index_has_one_entry_per_row(string tableName)
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);

        var table = db.OpenTable(tableName);
        int rowCount = table.Rows().Count();

        Assert.NotEmpty(table.Definition.Indexes);
        Assert.All(table.Definition.Indexes, ix =>
            Assert.Equal(rowCount, new IndexCursor(table.Channel, ix.RootPage).RowIds().Count()));
    }

    private static RowDecoder NewDecoder(JetDatabase db, Table table) =>
        new(table.Definition.Columns, db.Format, new LongValueReader(table.Channel));
}
