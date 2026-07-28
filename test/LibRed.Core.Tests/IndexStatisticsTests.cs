using LibRed;
using Xunit;

namespace LibRed.Core.Tests;

public class IndexStatisticsTests
{
    // Note: UniqueEntryCount is cumulative and never decremented by Access, so these
    // assertions rely on Northwind having no deleted rows (uniqueEntryCount == current
    // distinct count). They would not hold on a database that has had deletions.

    [Fact]
    public void Unique_indexes_have_entry_count_equal_to_row_count()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);

        foreach (var table in db.Catalog.UserTables)
        {
            int rows = db.ReadTableDefinition(table.DefinitionPage).RowCount;
            foreach (var ix in table.Indexes)
            {
                Assert.InRange(ix.UniqueEntryCount, 0, rows);
                if (ix.IsUnique)
                    Assert.Equal(rows, ix.UniqueEntryCount); // a unique index cannot have duplicates
            }
        }
    }

    [Theory]
    [InlineData("EmployeeID", 9)]      // 9 employees
    [InlineData("ShippersOrders", 3)]  // 3 shippers (ShipVia)
    [InlineData("PK_Orders", 830)]     // unique = row count
    public void Unique_entry_count_matches_actual_distinct_values(string indexName, int expected)
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);

        var table = db.OpenTable("Orders");
        var index = table.Definition.Indexes.First(i => i.Name == indexName);
        int columnIndex = index.Columns[0].Column.Index;

        int actualDistinct = table.Rows()
            .Select(r => r[columnIndex])
            .Where(v => v is not null) // index entries exclude nulls
            .Distinct()
            .Count();

        Assert.Equal(expected, index.UniqueEntryCount);
        Assert.Equal(expected, actualDistinct);
    }
}
