using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// Byte-faithful: a FixedPoint (Numeric/Decimal) index key is a sign byte plus the 16-byte big-endian unscaled
// magnitude (|value| * 10^scale). Non-negative uses sign 0xFF; a negative value is the bitwise complement of
// the whole 17-byte positive form. Verified against keys Access itself wrote.
public class DecimalKeyEncodingTests
{
    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    private static readonly decimal[] Values =
    [
        0m, 1m, -1m, 2m, -2m, 2.5m, -2.5m, 100m, -100m, 12345.6789m, -12345.6789m, 0.0001m, -0.0001m,
    ];

    private static void AssertKeysMatchAccess(string ddl, string indexPredicate)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "libred-deckey-");
        try
        {
            using (var conn = OpenOleDb(path))
            {
                using (var c = conn.CreateCommand()) { c.CommandText = ddl; c.ExecuteNonQuery(); }
                if (indexPredicate.Length > 0)
                {
                    using var ci = conn.CreateCommand();
                    ci.CommandText = indexPredicate;
                    ci.ExecuteNonQuery();
                }

                for (int i = 0; i < Values.Length; i++)
                {
                    using var c = conn.CreateCommand();
                    c.CommandText = "INSERT INTO DKey (K, V) VALUES (?, ?)";
                    c.Parameters.Add(new OleDbParameter("k", OleDbType.Numeric) { Value = Values[i], Precision = 18, Scale = 4 });
                    c.Parameters.AddWithValue("v", i);
                    c.ExecuteNonQuery();
                }
            }

            using var db = JetDatabase.Open(path);
            var table = db.OpenTable("DKey");
            var def = table.Definition;
            IndexDef index = def.Indexes.Single(i => i.Columns.Any(c => c.Column.Name == "K"));
            int kIdx = def.FindColumn("K")!.Index;
            var decoder = new RowDecoder(def.Columns, db.Format);

            int checkedKeys = 0;
            foreach (var (accessKey, rowId) in new IndexCursor(table.Channel, index.RootPage).RawEntries())
            {
                var d = (decimal)decoder.Decode(db.ReadDataPage(rowId.Page).GetRow(rowId.Row))[kIdx]!;

                var values = new object?[def.Columns.Count];
                values[kIdx] = d;
                byte[] ours = IndexKeyEncoder.Encode(index.Columns, values);

                Assert.True(accessKey.AsSpan().SequenceEqual(ours),
                    $"{d}: access={Convert.ToHexString(accessKey)} ours={Convert.ToHexString(ours)}");
                checkedKeys++;
            }

            Assert.Equal(Values.Length, checkedKeys);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Encoded_decimal_keys_match_access_byte_for_byte_ascending()
        => AssertKeysMatchAccess("CREATE TABLE DKey (K DECIMAL(18,4) CONSTRAINT PK PRIMARY KEY, V int)", "");

    [Fact]
    public void Encoded_decimal_keys_match_access_byte_for_byte_descending()
        => AssertKeysMatchAccess(
            "CREATE TABLE DKey (K DECIMAL(18,4), V int)",
            "CREATE UNIQUE INDEX IX_DKey_K ON DKey (K DESC)");
}
