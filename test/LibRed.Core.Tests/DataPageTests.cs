using LibRed;
using Xunit;

namespace LibRed.Core.Tests;

public class DataPageTests
{
    [Fact]
    public void Reads_MSysObjects_data_page()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);

        // MSysObjects' first data page, owned by its TDEF wherever that is. Northwind's holds 41 catalog rows.
        Storage.Table msysObjects = db.OpenTable("MSysObjects");
        var page = db.ReadDataPage(msysObjects.UsageMap.DataPages().First());

        Assert.False(page.IsLongValuePage);
        Assert.Equal(msysObjects.Definition.DefinitionPage, page.OwningTablePage);
        Assert.Equal(41, page.RowCount);
        Assert.Equal(page.RowCount, page.Rows.Count);
    }

    [Fact]
    public void Row_slots_are_within_page_and_packed_from_the_end()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);
        var page = db.ReadDataPage(db.OpenTable("MSysObjects").UsageMap.DataPages().First());

        int pageSize = db.Format.PageSize;
        int prevEnd = pageSize;
        foreach (var slot in page.Rows)
        {
            Assert.InRange(slot.Offset, 0, pageSize);
            Assert.True(slot.Length > 0);
            Assert.Equal(prevEnd, slot.Offset + slot.Length); // contiguous, end-packed
            prevEnd = slot.Offset;
        }
    }

    [Fact]
    public void Detects_long_value_page()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);

        // A page from one of MSysObjects' long-value maps, found through its TDEF: it carries the "LVAL" owner marker.
        Storage.Table msysObjects = db.OpenTable("MSysObjects");
        int lvalPage = db.ReadTableDefinition(msysObjects.Definition.DefinitionPage).LongValueOwnedMaps.Values
            .SelectMany(map => msysObjects.UsageMap.PagesInMap(map.Row, map.Page))
            .First();
        var page = db.ReadDataPage(lvalPage);

        Assert.True(page.IsLongValuePage);
        Assert.Equal(0, page.OwningTablePage);
    }
}