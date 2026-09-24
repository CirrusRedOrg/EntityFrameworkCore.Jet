using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// Byte-faithful: a FixedPoint (Numeric/Decimal) index key is a sign byte plus the 16-byte big-endian unscaled
// magnitude (|value| * 10^scale). Non-negative uses sign 0xFF; a negative value is the bitwise complement of
// the whole 17-byte positive form. Verified against keys Access itself wrote.
[Collection(AceCollection.Name)]
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

    // -0.00001 in a DECIMAL(18,4) is truncated to a magnitude of zero, and ACE keeps its sign: the row holds a
    // negative zero and the key is the complemented zero, 7F 00 FF…FF. A decimal can carry that sign too, but
    // "< 0" is false for it — so a sign taken from a comparison rewrites the row as +0 on an unrelated UPDATE
    // and rebuilds a +0 key that matches no entry, and the row can no longer be deleted.
    [Fact]
    public void A_negative_zero_keeps_its_sign_and_its_key_through_a_libred_rewrite()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "libred-negzero-");
        try
        {
            using (var conn = OpenOleDb(path))
                foreach (string sql in new[]
                {
                    "CREATE TABLE DKey (K DECIMAL(18,4), V int)",
                    "CREATE INDEX IX_DKey_K ON DKey (K)",
                    "INSERT INTO DKey (K, V) VALUES (-0.00001, 1)",
                })
                    using (var c = conn.CreateCommand()) { c.CommandText = sql; c.ExecuteNonQuery(); }

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var table = db.OpenTable("DKey");
                var def = table.Definition;
                IndexDef index = def.Indexes.Single(i => i.Name == "IX_DKey_K");
                int kIdx = def.FindColumn("K")!.Index, vIdx = def.FindColumn("V")!.Index;
                (RowId rowId, object?[] values) = table.Rows().WithIds().Single();
                (byte[] accessKey, _) = new IndexCursor(table.Channel, index.RootPage).RawEntries().Single();

                Assert.True(decimal.IsNegative((decimal)values[kIdx]!), "ACE stored the sign");
                Assert.Equal(Convert.ToHexString(accessKey), Convert.ToHexString(IndexKeyEncoder.Encode(index.Columns, values)));

                var updated = (object?[])values.Clone();
                updated[vIdx] = 2;
                table.Update(rowId, updated);
                Assert.True(decimal.IsNegative((decimal)table.GetRow(rowId)![kIdx]!), "the rewrite kept the sign");

                table.RemoveIndexEntry(index, updated, rowId);
                table.Delete(rowId);
            }

            using var conn2 = OpenOleDb(path);
            using var count = conn2.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM DKey";
            Assert.Equal(0, Convert.ToInt32(count.ExecuteScalar()));
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
