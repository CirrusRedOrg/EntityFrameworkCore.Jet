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
