using LibRed;
using Xunit;

namespace LibRed.Core.Tests;

public class IndexStatisticsTests
{
    [Fact]
    public void Unique_indexes_have_cardinality_equal_to_row_count()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);

        foreach (var table in db.Catalog.UserTables)
        {
            int rows = db.ReadTableDefinition(table.DefinitionPage).RowCount;
            foreach (var ix in table.Indexes)
            {
                Assert.InRange(ix.UniqueValueCount, 0, rows);
                if (ix.IsUnique)
                    Assert.Equal(rows, ix.UniqueValueCount); // a unique index cannot have duplicates
            }
        }
    }

    [Theory]
    [InlineData("EmployeeID", 9)]      // 9 employees
    [InlineData("ShippersOrders", 3)]  // 3 shippers (ShipVia)
    [InlineData("PK_Orders", 830)]     // unique = row count
    public void Cardinality_matches_actual_distinct_values(string indexName, int expected)
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);

        var table = db.OpenTable("Orders");
        var index = table.Definition.Indexes.First(i => i.Name == indexName);
        int columnIndex = index.Columns[0].Column.Index;

        int actualDistinct = table.Rows()
            .Select(r => r[columnIndex])
            .Where(v => v is not null) // index cardinality excludes nulls
            .Distinct()
            .Count();

        Assert.Equal(expected, index.UniqueValueCount);
        Assert.Equal(expected, actualDistinct);
    }
}
