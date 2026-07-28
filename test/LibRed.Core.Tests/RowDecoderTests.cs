using LibRed;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

public class RowDecoderTests
{
    private static List<object?[]> DecodeInlineRows(JetDatabase db, int dataPage, out IReadOnlyList<LibRed.Catalog.ColumnDef> columns)
    {
        var tdef = db.ReadTableDefinition(2); // MSysObjects schema
        columns = tdef.Columns;
        var decoder = new RowDecoder(columns, db.Format);
        var page = db.ReadDataPage(dataPage);

        var rows = new List<object?[]>();
        for (int i = 0; i < page.RowCount; i++)
        {
            var slot = page.Rows[i];
            if (slot.IsDeleted || slot.HasOverflow) continue;
            rows.Add(decoder.Decode(page.GetRow(i)));
        }
        return rows;
    }

    [Fact]
    public void Decodes_MSysObjects_rows_into_clr_values()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);
        var rows = DecodeInlineRows(db, 17, out var columns);

        int idIdx = columns.First(c => c.Name == "Id").Index;
        int typeIdx = columns.First(c => c.Name == "Type").Index;
        int nameIdx = columns.First(c => c.Name == "Name").Index;

        Assert.NotEmpty(rows);
        Assert.All(rows, r =>
        {
            Assert.IsType<int>(r[idIdx]);     // Id is Long (Int32)
            Assert.IsType<short>(r[typeIdx]); // Type is Integer (Int16)
            Assert.IsType<string>(r[nameIdx]);
        });

        var names = rows.Select(r => (string)r[nameIdx]!).ToList();
        Assert.Contains("MSysObjects", names);
        Assert.Contains("Categories", names);   // a real Northwind object
    }

    [Fact]
    public void Null_columns_decode_as_null()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);
        var rows = DecodeInlineRows(db, 17, out var columns);

        // For a plain table/query object, the Connect/Database memo fields are null.
        int connectIdx = columns.First(c => c.Name == "Connect").Index;
        Assert.Contains(rows, r => r[connectIdx] is null);
    }
}
