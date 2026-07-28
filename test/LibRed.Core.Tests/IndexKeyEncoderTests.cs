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
