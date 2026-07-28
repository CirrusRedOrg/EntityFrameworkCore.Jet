using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

public class DecimalTests
{
    [Fact]
    public void Reads_precision_and_scale()
    {
        using var db = JetDatabase.Open(TestDatabases.DecimalsAccdb);

        var cols = db.Catalog.FindTable("Nums")!.Columns;
        var price = cols.First(c => c.Name == "Price");
        var big = cols.First(c => c.Name == "Big");

        Assert.Equal(JetDataType.FixedPoint, price.Type);
        Assert.Equal((12, 3), (price.Precision, price.Scale));
        Assert.Equal((28, 4), (big.Precision, big.Scale));
    }

    [Fact]
    public void Decodes_decimal_values()
    {
        using var db = JetDatabase.Open(TestDatabases.DecimalsAccdb);

        var table = db.OpenTable("Nums");
        int price = table.Definition.Columns.First(c => c.Name == "Price").Index;
        int big = table.Definition.Columns.First(c => c.Name == "Big").Index;

        var rows = table.Rows().ToList();

        // Every value is a System.Decimal.
        Assert.All(rows, r => { Assert.IsType<decimal>(r[price]); Assert.IsType<decimal>(r[big]); });

        var prices = rows.Select(r => (decimal)r[price]!).ToList();
        var bigs = rows.Select(r => (decimal)r[big]!).ToList();

        Assert.Equal([12.345m, -9.999m, 0.000m], prices);
        // Includes a negative 20-digit value that spans the high 32-bit word.
        Assert.Equal([123456789012.3456m, -9876543210987654.3210m, 0.0000m], bigs);
    }
}
