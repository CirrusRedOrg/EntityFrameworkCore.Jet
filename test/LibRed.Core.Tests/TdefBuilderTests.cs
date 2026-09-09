using System.Buffers.Binary;
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

    [Fact]
    public void Builder_rejects_more_than_255_columns_before_narrowing_the_count()
    {
        ColumnSpec[] specs = Enumerable.Range(0, 256)
            .Select(i => new ColumnSpec($"C{i}", JetDataType.Int32, 4, IsFixedLength: true))
            .ToArray();

        Assert.Throws<NotSupportedException>(() => TdefBuilder.Build(Format, TableType.User, specs));
    }

    [Theory]
    [InlineData(65536, null)]
    [InlineData(4, 256)]
    public void Builder_rejects_column_fields_that_do_not_fit_the_verified_domain(int length, int? columnId)
    {
        ColumnSpec[] specs = [new("C", JetDataType.Int32, length, IsFixedLength: true, ColumnId: columnId)];

        Assert.Throws<NotSupportedException>(() => TdefBuilder.Build(Format, TableType.User, specs));
    }

    [Fact]
    public void Builder_rejects_duplicate_column_ids()
    {
        ColumnSpec[] specs =
        [
            new("A", JetDataType.Int32, 4, IsFixedLength: true, ColumnId: 3),
            new("B", JetDataType.Int32, 4, IsFixedLength: true, ColumnId: 3),
        ];

        Assert.Throws<NotSupportedException>(() => TdefBuilder.Build(Format, TableType.User, specs));
    }

    [Fact]
    public void Builder_rejects_a_column_wider_than_ace_stores()
    {
        ColumnSpec[] specs = [new("A", JetDataType.Binary, 40000, IsFixedLength: true)];

        Assert.Throws<NotSupportedException>(() => TdefBuilder.Build(Format, TableType.User, specs));
    }

    // Every column within the per-field limit, yet the fixed region as a whole is past what a record can
    // hold — the boundary itself is measured against ACE in ColumnWidthLimitAccessTests.
    [Fact]
    public void Builder_rejects_a_fixed_region_past_the_record_cap()
    {
        ColumnSpec[] specs = [.. Enumerable.Range(0, 252)
            .Select(i => new ColumnSpec($"G{i}", JetDataType.Guid, 16, IsFixedLength: true))];

        Assert.Throws<NotSupportedException>(() => TdefBuilder.Build(Format, TableType.User, specs));
    }

    [Fact]
    public void Builder_sizes_the_definition_from_the_actual_encoded_names()
    {
        ColumnSpec[] columns = Enumerable.Range(0, 255)
            .Select(i => new ColumnSpec($"C{i:D3}" + new string('N', 60), JetDataType.Boolean, 0, IsFixedLength: true))
            .ToArray();
        var result = TdefBuilder.Build(Format, TableType.User, columns);
        int declared = BinaryPrimitives.ReadInt32LittleEndian(result.Page.AsSpan(Format.TdefLengthOffset, 4));

        var definition = new TableDefinitionPage();
        definition.Read(new PageBuffer(result.Page, 99), Format);
        Assert.Equal(255, definition.Columns.Count);
        Assert.Equal(declared, result.Page.Length);
    }

    [Fact]
    public void Builder_rejects_names_that_do_not_fit_their_16_bit_byte_lengths()
    {
        string tooLong = new('N', 65);
        Assert.Throws<NotSupportedException>(() => TdefBuilder.Build(Format, TableType.User,
            [new(tooLong, JetDataType.Int32, 4, IsFixedLength: true)]));

        ColumnSpec[] columns = [new("Id", JetDataType.Int32, 4, IsFixedLength: true)];
        IndexSpec[] indexes = [new(tooLong, ["Id"], true, true, RootPage: 2)];
        Assert.Throws<NotSupportedException>(() => TdefBuilder.Build(Format, TableType.User, columns, indexes));
    }

    [Theory]
    [InlineData(2, 0, 0, 5)]
    [InlineData(0, 256, 0, 5)]
    [InlineData(0, 0, 256, 5)]
    [InlineData(0, 0, 0, 16777216)]
    public void Builder_rejects_invalid_long_value_usage_map_fields(
        int columnId, int usedRow, int freeRow, int mapPage)
    {
        ColumnSpec[] columns = [new("M", JetDataType.Memo, 0, IsFixedLength: false, ColumnId: 0)];
        LongValueColumnSpec[] longValues = [new(columnId, usedRow, freeRow, mapPage)];

        Assert.Throws<NotSupportedException>(() =>
            TdefBuilder.Build(Format, TableType.User, columns, longValueColumns: longValues));
    }

    [Fact]
    public void Builder_rejects_duplicate_long_value_entries_and_unknown_index_columns()
    {
        ColumnSpec[] columns = [new("M", JetDataType.Memo, 0, IsFixedLength: false)];
        LongValueColumnSpec[] duplicate = [new(0, 1, 2, 5), new(0, 1, 2, 5)];
        Assert.Throws<NotSupportedException>(() =>
            TdefBuilder.Build(Format, TableType.User, columns, longValueColumns: duplicate));

        IndexSpec[] indexes = [new("IX", ["Missing"], false, false, RootPage: 2)];
        Assert.Throws<NotSupportedException>(() => TdefBuilder.Build(Format, TableType.User, columns, indexes));
    }
}
