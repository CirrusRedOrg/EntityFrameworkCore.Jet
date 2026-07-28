using LibRed;
using Xunit;

namespace LibRed.Core.Tests;

public class TableScanTests
{
    [Fact]
    public void Scans_categories_rows_by_name()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);

        var categories = db.OpenTable("Categories");
        int nameIdx = categories.Definition.Columns.First(c => c.Name == "CategoryName").Index;
        int idIdx = categories.Definition.Columns.First(c => c.Name == "CategoryID").Index;

        var rows = categories.Rows().ToList();

        Assert.Equal(8, rows.Count);
        var names = rows.Select(r => (string)r[nameIdx]!).ToList();
        Assert.Equal(
            ["Beverages", "Condiments", "Confections", "Dairy Products",
             "Grains/Cereals", "Meat/Poultry", "Produce", "Seafood"],
            names);

        // CategoryID is an AutoNumber Long.
        Assert.All(rows, r => Assert.IsType<int>(r[idIdx]));
    }

    [Theory]
    [InlineData("Customers", 91)]   // 43 rows are >= 256 bytes
    [InlineData("Employees", 9)]    // every row is >= 256 bytes
    [InlineData("Orders", 830)]
    [InlineData("Order Details", 2155)]
    public void Scans_all_rows_including_large_ones(string table, int expectedRows)
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);
        Assert.Equal(expectedRows, db.OpenTable(table).Rows().Count());
    }

    [Fact]
    public void Decodes_boolean_columns_from_the_null_bitmap()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);

        var products = db.OpenTable("Products");
        int discontinued = products.Definition.Columns.First(c => c.Name == "Discontinued").Index;

        var rows = products.Rows().ToList();

        Assert.All(rows, r => Assert.IsType<bool>(r[discontinued]));
        // Northwind has exactly 8 discontinued products.
        Assert.Equal(8, rows.Count(r => (bool)r[discontinued]!));
    }

    [Fact]
    public void Resolves_memo_long_values_including_compressed_unicode()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);

        var categories = db.OpenTable("Categories");
        int nameIdx = categories.Definition.Columns.First(c => c.Name == "CategoryName").Index;
        int descIdx = categories.Definition.Columns.First(c => c.Name == "Description").Index;

        var byName = categories.Rows().ToDictionary(r => (string)r[nameIdx]!, r => (string)r[descIdx]!);

        // UTF-16 stored memo:
        Assert.Equal("Soft drinks, coffees, teas, beers, and ales", byName["Beverages"]);
        // Compressed-Unicode (0xFF 0xFE marker, 1 byte/char) memos:
        Assert.Equal("Cheeses", byName["Dairy Products"]);
        Assert.Equal("Prepared meats", byName["Meat/Poultry"]);
    }

    [Fact]
    public void Resolves_ole_long_values_across_chained_pages()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);

        var categories = db.OpenTable("Categories");
        int picIdx = categories.Definition.Columns.First(c => c.Name == "Picture").Index;

        // Each Picture is a ~10 KB OLE blob chained across multiple LVAL pages.
        Assert.All(categories.Rows(), r =>
        {
            var blob = Assert.IsType<byte[]>(r[picIdx]);
            Assert.Equal(10746, blob.Length);
        });
    }

    [Fact]
    public void Scans_a_small_lookup_table()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);

        var shippers = db.OpenTable("Shippers");
        int company = shippers.Definition.Columns.First(c => c.Name == "CompanyName").Index;

        var names = shippers.Rows().Select(r => (string)r[company]!).ToList();

        Assert.Equal(3, names.Count);
        Assert.Contains("Speedy Express", names);
        Assert.Contains("United Package", names);
        Assert.Contains("Federal Shipping", names);
    }
}
