using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// Byte-faithfulness of the binary index-key encoding, checked against ACE's own stored keys in the
// EverythingIsBytes fixture (every entity has a byte[] primary key; some are 3/4/5/8/16-byte values,
// covering the single-chunk, full-chunk, and multi-chunk cases). For each stored index entry we fetch
// the row's actual key-column value and re-encode it — it must reproduce ACE's bytes exactly.
public class BinaryIndexKeyAccessTests
{
    [Fact]
    public void Reencoding_binary_keys_reproduces_ace_bytes()
    {
        using var db = JetDatabase.Open(TestDatabases.EverythingIsBytesAccdb);

        int checkedEntries = 0;
        foreach (TableDef tdef in db.Catalog.Tables)
        {
            var table = db.OpenTable(tdef.Name);
            if (!table.Definition.Indexes.Any(i => i.Columns.Any(c => c.Column.Type == JetDataType.Binary)))
                continue;

            // Map every live row to its column values, keyed by RowId, so a stored index entry's row
            // can be resolved back to the value that produced its key.
            var rows = table.Rows().WithIds().ToDictionary(r => r.Id, r => r.Values);

            foreach (IndexDef idx in table.Definition.Indexes)
            {
                if (idx.RootPage <= 0 || !idx.Columns.Any(c => c.Column.Type == JetDataType.Binary))
                    continue;

                var cursor = new IndexCursor(table.Channel, idx.RootPage);
                foreach ((byte[] stored, RowId row) in cursor.RawEntries())
                {
                    if (!rows.TryGetValue(row, out object?[]? values))
                        continue; // entry points at a row we didn't scan (shouldn't happen); skip defensively

                    byte[] reEncoded = IndexKeyEncoder.Encode(idx.Columns, values);
                    Assert.Equal(stored, reEncoded);
                    checkedEntries++;
                }
            }
        }

        Assert.True(checkedEntries > 20, $"expected to check many binary keys, only saw {checkedEntries}");
    }

    [Theory]
    [InlineData(new byte[] { }, "7F")]                                                   // empty → start marker only (live ACE oracle)
    [InlineData(new byte[] { 0x01, 0x02, 0x03 }, "7F 01 02 03 00 00 00 00 00 03")]    // 3 bytes, single chunk
    [InlineData(new byte[] { 0x47, 0x75, 0x6D, 0x62, 0x61, 0x6C, 0x6C, 0x21 }, "7F 47 75 6D 62 61 6C 6C 21 08")] // full 8-byte chunk
    [InlineData(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, "7F 01 02 03 04 05 06 07 08 09 09 00 00 00 00 00 00 00 01")] // 9 bytes → two chunks
    public void Ascending_binary_key_matches_the_chunked_layout(byte[] data, string expectedHex)
    {
        var column = new ColumnDef { Name = "b", Type = JetDataType.Binary, Index = 0 };
        byte[] key = IndexKeyEncoder.Encode([(column, true)], [data]);
        Assert.Equal(expectedHex, string.Join(" ", key.Select(x => x.ToString("X2"))));
    }

    [Fact]
    public void Encoded_binary_keys_sort_in_value_order_both_directions()
    {
        // For non-empty values, lexicographic byte order of the encoded keys must match value order ascending,
        // and reverse it descending. Empty Binary has a special start-only key and is covered against live ACE
        // in IndexOrderingAccessTests rather than being forced through this ordinary chunk-prefix property.
        var col = new ColumnDef { Name = "b", Type = JetDataType.Binary, Index = 0 };
        byte[][] values =
        [
            [0x00], [0x00, 0x00], [0x01], [0x01, 0x02, 0x03], [0x01, 0x02, 0x03, 0x04],
            [0x02], [.. Enumerable.Repeat((byte)0xAB, 9)], [0xFF], [0xFF, 0x00],
        ];

        var asc = values.Select(v => IndexKeyEncoder.Encode([(col, true)], [v])).ToList();
        var desc = values.Select(v => IndexKeyEncoder.Encode([(col, false)], [v])).ToList();

        for (int i = 0; i + 1 < values.Length; i++)
        {
            Assert.True(Compare(asc[i], asc[i + 1]) < 0, $"ascending: {Show(values[i])} should sort before {Show(values[i + 1])}");
            Assert.True(Compare(desc[i], desc[i + 1]) > 0, $"descending: {Show(values[i])} should sort after {Show(values[i + 1])}");
        }

        static int Compare(byte[] a, byte[] b)
        {
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++) if (a[i] != b[i]) return a[i].CompareTo(b[i]);
            return a.Length.CompareTo(b.Length);
        }
        static string Show(byte[] b) => "[" + string.Join(",", b.Select(x => x.ToString("X2"))) + "]";
    }
}
