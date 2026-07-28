using LibRed;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

public class RowEncoderTests
{
    [Theory]
    [InlineData("Shippers")]   // int autonumber + two text columns
    [InlineData("Customers")]  // all-text, several nullable columns (Region often null)
    [InlineData("Orders")]     // ints, DateTime, text, nullable foreign keys
    public void Encode_then_decode_round_trips_real_rows(string tableName)
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);
        var table = db.OpenTable(tableName);
        var columns = table.Definition.Columns;

        var encoder = new RowEncoder(columns, db.Format);
        var decoder = new RowDecoder(columns, db.Format);

        int rows = 0;
        foreach (object?[] original in table.Rows())
        {
            // Memo/OLE long values aren't encodable yet; skip tables that have them via this guard.
            byte[] encoded = encoder.Encode(original);
            object?[] roundTripped = decoder.Decode(encoded);
            Assert.Equal(original, roundTripped);
            rows++;
        }

        Assert.True(rows > 0, $"expected to read some rows from {tableName}");
    }

    [Fact]
    public void Null_variable_columns_round_trip()
    {
        // Customers.Region is null for many rows — make sure null var columns survive.
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);
        var table = db.OpenTable("Customers");
        var columns = table.Definition.Columns;
        int region = columns.Single(c => c.Name == "Region").Index;

        var encoder = new RowEncoder(columns, db.Format);
        var decoder = new RowDecoder(columns, db.Format);

        bool sawNull = false;
        foreach (object?[] original in table.Rows())
        {
            if (original[region] is null) sawNull = true;
            Assert.Equal(original, decoder.Decode(encoder.Encode(original)));
        }
        Assert.True(sawNull, "expected at least one null Region");
    }
}
