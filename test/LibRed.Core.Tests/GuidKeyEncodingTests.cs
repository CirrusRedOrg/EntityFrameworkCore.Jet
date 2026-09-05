using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

public class GuidKeyEncodingTests
{
    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    private static readonly Guid[] Guids =
    [
        new("00000000-0000-0000-0000-000000000000"),
        new("00000000-0000-0000-0000-000000000001"),
        new("01020304-0506-0708-090a-0b0c0d0e0f10"),
        new("ffffffff-ffff-ffff-ffff-ffffffffffff"),
        new("12345678-9abc-def0-1234-56789abcdef0"),
        Guid.NewGuid(),
    ];

    [Fact]
    public void Encoded_guid_keys_match_access_byte_for_byte()
    {
        // Build a real GUID-PK table via Access, then check our encoder reproduces each stored key.
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "libred-guidkey-");
        try
        {
            using (var conn = OpenOleDb(path))
            {
                using (var c = conn.CreateCommand())
                { c.CommandText = "CREATE TABLE GKey (K GUID PRIMARY KEY, V int)"; c.ExecuteNonQuery(); }
                for (int i = 0; i < Guids.Length; i++)
                {
                    using var c = conn.CreateCommand();
                    c.CommandText = "INSERT INTO GKey (K, V) VALUES (?, ?)";
                    c.Parameters.AddWithValue("k", Guids[i]);
                    c.Parameters.AddWithValue("v", i);
                    c.ExecuteNonQuery();
                }
            }

            using var db = JetDatabase.Open(path);
            var table = db.OpenTable("GKey");
            var def = table.Definition;
            IndexDef pk = def.Indexes.Single(i => i.IsPrimaryKey);
            int kIdx = def.FindColumn("K")!.Index;
            var decoder = new RowDecoder(def.Columns, db.Format);

            int checkd = 0;
            foreach (var (accessKey, rowId) in new IndexCursor(table.Channel, pk.RootPage).RawEntries())
            {
                var g = (Guid)decoder.Decode(db.ReadDataPage(rowId.Page).GetRow(rowId.Row))[kIdx]!;

                var values = new object?[def.Columns.Count];
                values[kIdx] = g;
                byte[] ours = IndexKeyEncoder.Encode(pk.Columns, values);

                Assert.True(accessKey.AsSpan().SequenceEqual(ours),
                    $"'{g}': access={Convert.ToHexString(accessKey)} ours={Convert.ToHexString(ours)}");

                // The decoder is the inverse.
                Assert.Equal(g, (Guid)IndexKeyDecoder.Decode(pk.Columns, accessKey)[0]!);
                checkd++;
            }
            Assert.Equal(Guids.Length, checkd);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Encoded_descending_guid_keys_match_access_byte_for_byte()
    {
        // A DESCENDING GUID index: ACE inverts every byte of the ascending key except the 0x09 field
        // marker. Build it via Access, then confirm our encoder reproduces each stored key (and decodes).
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "libred-guiddesc-");
        try
        {
            using (var conn = OpenOleDb(path))
            {
                using (var c = conn.CreateCommand())
                { c.CommandText = "CREATE TABLE GKey (K GUID, V int)"; c.ExecuteNonQuery(); }
                using (var c = conn.CreateCommand())
                { c.CommandText = "CREATE INDEX ixd ON GKey (K DESC)"; c.ExecuteNonQuery(); }
                for (int i = 0; i < Guids.Length; i++)
                {
                    using var c = conn.CreateCommand();
                    c.CommandText = "INSERT INTO GKey (K, V) VALUES (?, ?)";
                    c.Parameters.AddWithValue("k", Guids[i]);
                    c.Parameters.AddWithValue("v", i);
                    c.ExecuteNonQuery();
                }
            }

            using var db = JetDatabase.Open(path);
            var table = db.OpenTable("GKey");
            var def = table.Definition;
            IndexDef ixd = def.Indexes.First(i => !i.IsPrimaryKey);
            Assert.False(ixd.Columns[0].Ascending); // it really is descending
            int kIdx = def.FindColumn("K")!.Index;
            var decoder = new RowDecoder(def.Columns, db.Format);

            int checkd = 0;
            foreach (var (accessKey, rowId) in new IndexCursor(table.Channel, ixd.RootPage).RawEntries())
            {
                var g = (Guid)decoder.Decode(db.ReadDataPage(rowId.Page).GetRow(rowId.Row))[kIdx]!;

                var values = new object?[def.Columns.Count];
                values[kIdx] = g;
                byte[] ours = IndexKeyEncoder.Encode(ixd.Columns, values);

                Assert.True(accessKey.AsSpan().SequenceEqual(ours),
                    $"'{g}': access={Convert.ToHexString(accessKey)} ours={Convert.ToHexString(ours)}");
                Assert.Equal(g, (Guid)IndexKeyDecoder.Decode(ixd.Columns, accessKey)[0]!);
                checkd++;
            }
            Assert.Equal(Guids.Length, checkd);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Access_reads_a_libred_created_guid_key_table()
    {
        // The mirror direction: LibRed writes the GUID-PK table + rows, and ACE opens it, seeks by the
        // key, and returns every row in key order — proving the index keys we wrote are well-ordered.
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "libred-guidwr-");
        try
        {
            var target = Guids[4]; // 12345678-...
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("GKey",
                    [new("K", JetDataType.Guid, 16, IsFixedLength: true), new("V", JetDataType.Int32, 4, IsFixedLength: true)],
                    primaryKey: ["K"]);
                var table = db.OpenTable("GKey");
                for (int i = 0; i < Guids.Length; i++)
                    table.Insert([Guids[i], i]);
            }

            using var conn = OpenOleDb(path);
            using (var c = conn.CreateCommand())
            {
                c.CommandText = "SELECT COUNT(*) FROM GKey";
                Assert.Equal(Guids.Length, Convert.ToInt32(c.ExecuteScalar()));
            }
            using (var c = conn.CreateCommand())
            {
                c.CommandText = "SELECT V FROM GKey WHERE K = ?";
                c.Parameters.AddWithValue("k", target);
                Assert.Equal(4, Convert.ToInt32(c.ExecuteScalar())); // seek by the GUID key finds the row
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
