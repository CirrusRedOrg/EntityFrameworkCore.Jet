using LibRed;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;
using Xunit;

namespace LibRed.Core.Tests;

public class TdefBuilderTests
{
    // The ACE 12 (ACCDB) format, taken from a real database.
    private static readonly JetFormatBase Format = OpenFormat();

    private static JetFormatBase OpenFormat()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);
        return db.Format;
    }

    [Fact]
    public void Built_tdef_round_trips_through_the_reader()
    {
        var specs = new ColumnSpec[]
        {
            new("Id", JetDataType.Int32, 4, IsFixedLength: true, IsAutoNumber: true),
            new("Count", JetDataType.Int16, 2, IsFixedLength: true),
            new("When", JetDataType.DateTime, 8, IsFixedLength: true),
            new("Name", JetDataType.Text, 510, IsFixedLength: false),
            new("Notes", JetDataType.Text, 510, IsFixedLength: false),
        };

        var result = TdefBuilder.Build(Format, TableType.User, specs);

        var page = new TableDefinitionPage();
        page.Read(new PageBuffer(result.Page, 99), Format);

        Assert.Equal(TableType.User, page.TableType);
        Assert.Equal(0, page.RowCount);
        Assert.Equal(5, page.ColumnCount);
        Assert.Equal(2, page.VariableColumnCount);
        Assert.Empty(page.Indexes);

        Assert.Equal(
            specs.Select(s => s.Name),
            page.Columns.Select(c => c.Name));
        Assert.Equal(
            specs.Select(s => s.Type),
            page.Columns.Select(c => c.Type));

        var id = page.Columns.Single(c => c.Name == "Id");
        Assert.True(id.IsFixedLength);
        Assert.True(id.IsAutoNumber);

        // Variable columns keep their ascending-column-id rank.
        Assert.Equal(0, page.Columns.Single(c => c.Name == "Name").VariableIndex);
        Assert.Equal(1, page.Columns.Single(c => c.Name == "Notes").VariableIndex);

        // Fixed columns are packed in declaration order.
        Assert.Equal(0, page.Columns.Single(c => c.Name == "Id").FixedOffset);
        Assert.Equal(4, page.Columns.Single(c => c.Name == "Count").FixedOffset);
        Assert.Equal(6, page.Columns.Single(c => c.Name == "When").FixedOffset);
    }

    [Fact]
    public void Built_tdef_with_primary_key_index_round_trips()
    {
        var specs = new ColumnSpec[]
        {
            new("Id", JetDataType.Int32, 4, IsFixedLength: true),
            new("Name", JetDataType.Text, 510, IsFixedLength: false),
        };
        var indexes = new[] { new IndexSpec("PrimaryKey", ["Id"], IsPrimaryKey: true, IsUnique: true, RootPage: 42) };

        var result = TdefBuilder.Build(Format, TableType.User, specs, indexes);

        var page = new TableDefinitionPage();
        page.Read(new PageBuffer(result.Page, 7), Format);

        var pk = Assert.Single(page.Indexes);
        Assert.Equal("PrimaryKey", pk.Name);
        Assert.True(pk.IsPrimaryKey);
        Assert.True(pk.IsUnique);
        Assert.Equal(42, pk.RootPage);
        Assert.Equal(["Id"], pk.Columns.Select(c => c.Column.Name));
        Assert.True(pk.Columns[0].Ascending);

        // Columns still parse correctly after the index blocks shifted them.
        Assert.Equal(["Id", "Name"], page.Columns.Select(c => c.Name));
    }

    [Fact]
    public void Built_tdef_columns_can_encode_and_decode_a_row()
    {
        // The resolved column layout must round-trip an actual row through the row codec.
        var specs = new ColumnSpec[]
        {
            new("Id", JetDataType.Int32, 4, IsFixedLength: true),
            new("Name", JetDataType.Text, 510, IsFixedLength: false),
        };
        var result = TdefBuilder.Build(Format, TableType.User, specs);

        var encoder = new LibRed.Storage.RowEncoder(result.Columns, Format);
        var decoder = new LibRed.Storage.RowDecoder(result.Columns, Format);

        object?[] row = [42, "hello"];
        Assert.Equal(row, decoder.Decode(encoder.Encode(row)));
    }
}
