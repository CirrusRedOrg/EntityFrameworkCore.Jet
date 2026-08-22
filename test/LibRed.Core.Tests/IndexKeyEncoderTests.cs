using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

public class IndexKeyEncoderTests
{
    [Theory]
    [InlineData("Shippers")]
    [InlineData("Categories")]
    [InlineData("Orders")]
    [InlineData("Products")]
    public void Encodes_integer_keys_byte_for_byte_like_access(string tableName)
    {
        // The strongest check: re-encoding the value decoded from Access's own stored key bytes
        // must reproduce those exact bytes. Uses each table's integer primary key.
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);
        var table = db.OpenTable(tableName);
        IndexDef pk = table.Definition.Indexes.Single(i => i.IsPrimaryKey);

        var cursor = new IndexCursor(table.Channel, pk.RootPage);
        int checkd = 0;
        foreach ((byte[] stored, _) in cursor.RawEntries())
        {
            object?[] values = AlignToColumns(table.Definition, pk, IndexKeyDecoder.Decode(pk.Columns, stored));
            byte[] reEncoded = IndexKeyEncoder.Encode(pk.Columns, values);
            Assert.Equal(stored, reEncoded);
            checkd++;
        }
        Assert.True(checkd > 0);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Round_trips_integers_including_negatives_and_zero(bool ascending)
    {
        var column = new ColumnDef { Name = "n", Type = JetDataType.Int32, Index = 0 };
        var columns = new[] { (column, ascending) };

        foreach (int n in new[] { int.MinValue, -1000, -1, 0, 1, 42, 1000, int.MaxValue })
        {
            byte[] key = IndexKeyEncoder.Encode(columns, [n]);
            object?[] back = IndexKeyDecoder.Decode(columns, key);
            Assert.Equal(n, back[0]);
        }
    }

    [Fact]
    public void Encoded_integer_keys_sort_in_value_order()
    {
        var column = new ColumnDef { Name = "n", Type = JetDataType.Int32, Index = 0 };
        var columns = new[] { (column, true) };

        int[] sorted = [-5, -1, 0, 1, 5, 100];
        var keys = sorted.Select(n => IndexKeyEncoder.Encode(columns, [n])).ToList();

        // Lexicographic byte order must match ascending value order.
        for (int i = 1; i < keys.Count; i++)
            Assert.True(Compare(keys[i - 1], keys[i]) < 0, $"{sorted[i - 1]} should encode below {sorted[i]}");
    }

    [Fact]
    public void Round_trips_double_keys()
    {
        var column = new ColumnDef { Name = "d", Type = JetDataType.Double, Index = 0 };
        var columns = new[] { (column, true) };

        foreach (double d in new[] { -1e9, -3.5, -0.0, 0.0, 2.5, 1e9 })
        {
            byte[] key = IndexKeyEncoder.Encode(columns, [d]);
            Assert.Equal(d, (double)IndexKeyDecoder.Decode(columns, key)[0]!);
        }
    }

    public static TheoryData<JetDataType, object> ReversibleBoundaryValues => new()
    {
        { JetDataType.Byte, byte.MinValue },
        { JetDataType.Byte, byte.MaxValue },
        { JetDataType.Int16, short.MinValue },
        { JetDataType.Int16, short.MaxValue },
        { JetDataType.Int32, int.MinValue },
        { JetDataType.Int32, int.MaxValue },
        { JetDataType.Single, float.MinValue },
        { JetDataType.Single, -0.0f },
        { JetDataType.Single, float.MaxValue },
        { JetDataType.Double, double.MinValue },
        { JetDataType.Double, -0.0d },
        { JetDataType.Double, double.MaxValue },
        { JetDataType.Currency, -922337203685477.5808m },
        { JetDataType.Currency, 922337203685477.5807m },
        { JetDataType.DateTime, new DateTime(1800, 1, 1) },
        { JetDataType.DateTime, new DateTime(9999, 12, 31) },
    };

    [Theory]
    [MemberData(nameof(ReversibleBoundaryValues))]
    public void Reversible_boundary_values_round_trip_in_both_directions(JetDataType type, object value)
    {
        var column = new ColumnDef { Name = "K", Type = type, Index = 0 };
        foreach (bool ascending in new[] { true, false })
        {
            var columns = new[] { (column, ascending) };
            byte[] key = IndexKeyEncoder.Encode(columns, [value]);
            Assert.Equal(value, IndexKeyDecoder.Decode(columns, key)[0]);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Null_round_trips_for_each_reversible_key_kind(bool ascending)
    {
        foreach (JetDataType type in new[]
                 {
                     JetDataType.Byte, JetDataType.Int16, JetDataType.Int32, JetDataType.Single,
                     JetDataType.Double, JetDataType.Currency, JetDataType.DateTime, JetDataType.Guid,
                 })
        {
            var column = new ColumnDef { Name = "K", Type = type, Index = 0 };
            var columns = new[] { (column, ascending) };
            Assert.Null(IndexKeyDecoder.Decode(columns, IndexKeyEncoder.Encode(columns, [null]))[0]);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Guid_round_trips_in_both_directions(bool ascending)
    {
        var column = new ColumnDef { Name = "K", Type = JetDataType.Guid, Index = 0 };
        var columns = new[] { (column, ascending) };
        var values = new object?[] { Guid.Parse("00112233-4455-6677-8899-aabbccddeeff") };
        Assert.Equal(values[0], IndexKeyDecoder.Decode(columns, IndexKeyEncoder.Encode(columns, values))[0]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Boolean_null_is_normalized_to_false_because_boolean_keys_have_no_null_flag(bool ascending)
    {
        var column = new ColumnDef { Name = "K", Type = JetDataType.Boolean, Index = 0 };
        var columns = new[] { (column, ascending) };
        byte[] falseKey = IndexKeyEncoder.Encode(columns, [false]);
        byte[] trueKey = IndexKeyEncoder.Encode(columns, [true]);
        byte[] nullKey = IndexKeyEncoder.Encode(columns, [null]);

        Assert.NotEqual(falseKey, trueKey);
        Assert.NotEqual(trueKey, nullKey);
        Assert.Equal(falseKey, nullKey);
        Assert.False((bool)IndexKeyDecoder.Decode(columns, falseKey)[0]!);
        Assert.True((bool)IndexKeyDecoder.Decode(columns, trueKey)[0]!);
        Assert.False((bool)IndexKeyDecoder.Decode(columns, nullKey)[0]!);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Truncated_fixed_and_guid_keys_stop_without_reading_past_the_payload(bool ascending)
    {
        var integer = new ColumnDef { Name = "I", Type = JetDataType.Int32, Index = 0 };
        var integerColumns = new[] { (integer, ascending) };
        byte[] integerKey = IndexKeyEncoder.Encode(integerColumns, [123]);
        Assert.Null(IndexKeyDecoder.Decode(integerColumns, integerKey[..^1])[0]);

        var guid = new ColumnDef { Name = "G", Type = JetDataType.Guid, Index = 0 };
        var guidColumns = new[] { (guid, ascending) };
        byte[] guidKey = IndexKeyEncoder.Encode(guidColumns, [Guid.NewGuid()]);
        Assert.Null(IndexKeyDecoder.Decode(guidColumns, guidKey[..^1])[0]);
    }

    [Fact]
    public void Descending_integer_bytes_sort_in_reverse_value_order()
    {
        var column = new ColumnDef { Name = "K", Type = JetDataType.Int32, Index = 0 };
        var columns = new[] { (column, false) };
        int[] ascendingValues = [-5, -1, 0, 1, 5, 100];
        byte[][] keys = ascendingValues.Select(v => IndexKeyEncoder.Encode(columns, [v])).ToArray();

        for (int i = 1; i < keys.Length; i++)
            Assert.True(Compare(keys[i - 1], keys[i]) > 0);
    }

    private static object?[] AlignToColumns(TableDef table, IndexDef index, object?[] keyValues)
    {
        // IndexKeyDecoder returns values in index-column order; the encoder reads values[column.Index].
        var values = new object?[table.Columns.Count];
        for (int i = 0; i < index.Columns.Count; i++)
            values[index.Columns[i].Column.Index] = keyValues[i];
        return values;
    }

    private static int Compare(byte[] a, byte[] b)
    {
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++)
            if (a[i] != b[i]) return a[i] - b[i];
        return a.Length - b.Length;
    }
}
