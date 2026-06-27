using LibRed;
using Xunit;

namespace LibRed.Core.Tests;

public class DataPageTests
{
    [Fact]
    public void Reads_MSysObjects_data_page()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);

        // Page 17 holds MSysObjects rows (owner = its TDEF page 2).
        var page = db.ReadDataPage(17);

        Assert.False(page.IsLongValuePage);
        Assert.Equal(2, page.OwningTablePage);
        Assert.Equal(41, page.RowCount);
        Assert.Equal(page.RowCount, page.Rows.Count);
    }

    [Fact]
    public void Row_slots_are_within_page_and_packed_from_the_end()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);
        var page = db.ReadDataPage(17);

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

        // Page 42 carries the "LVAL" owner marker.
        var page = db.ReadDataPage(42);

        Assert.True(page.IsLongValuePage);
        Assert.Equal(0, page.OwningTablePage);
    }
}
