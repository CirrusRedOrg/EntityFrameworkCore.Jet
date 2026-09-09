using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// DATETIME2 (Date/Time Extended) index keys, byte-for-byte against ACE.
//
// ACE does permit an index on the type, and it keys the whole 42-byte stored value through the same 8-byte
// chunking it uses for Binary - start flag, 8 bytes, 0x09 while more follow, then the final chunk and its
// real-byte count - rather than folding the value to a number the way an ordinary DATETIME folds to its OA
// double. That works because the stored form is already order-preserving: both numeric fields are zero-padded
// to 19 digits, so byte order is chronological order.
//
// The fixture is Northwind (ACE 12) raised to version byte 0x06, which is the entire upgrade - see
// AceDateTime2UpgradeTests, which proves ACE asks for nothing more.
public class DateTime2KeyEncodingTests
{
    // Spread across the range, and deliberately including a January date: ACE's own OLE DB reader cannot
    // return January for this type at all, so anything that round-trips a value through ACE rather than
    // reading the page would fail here for a reason that has nothing to do with index keys.
    private static readonly string[] Literals =
    [
        "#2021-03-04 05:06:07#",
        "#2021-01-15 05:06:07#",
        "#1900-01-01 00:00:00#",
        "#2099-12-31 23:59:59#",
    ];

    [Fact]
    public void Encoded_datetime2_keys_match_access_byte_for_byte_ascending()
        => AssertKeysMatchAccess("CREATE INDEX IX_EKey ON EKey (K)");

    [Fact]
    public void Encoded_datetime2_keys_match_access_byte_for_byte_descending()
        => AssertKeysMatchAccess("CREATE INDEX IX_EKey ON EKey (K DESC)");

    private static void AssertKeysMatchAccess(string indexDdl)
    {
        // ACE 17 / Access 2019+ only. CI installs the 2016 redistributable, which cannot create the column at
        // all — a failure there would say nothing about LibRed's encoding.
        Assert.SkipUnless(
            AceTestDatabase.SupportsColumnType(TestDatabases.NorthwindAccdb, "DATETIME2"),
            AceTestDatabase.UnsupportedColumnTypeReason("DATETIME2"));

        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "libred-dt2key-");
        try
        {
            SetVersionByte(path, 0x06);

            using (OleDbConnection connection = AceTestDatabase.Open(path))
            {
                Exec(connection, "CREATE TABLE EKey (K DATETIME2, V LONG)");
                Exec(connection, indexDdl);
                for (int i = 0; i < Literals.Length; i++)
                    Exec(connection, $"INSERT INTO EKey (K, V) VALUES ({Literals[i]}, {i})");
            }

            using var db = JetDatabase.Open(path);
            var table = db.OpenTable("EKey");
            var def = table.Definition;
            IndexDef index = def.Indexes.Single(i => i.Columns.Any(c => c.Column.Name == "K"));
            int kIdx = def.FindColumn("K")!.Index;
            var decoder = new RowDecoder(def.Columns, db.Format);

            int checkedKeys = 0;
            foreach ((byte[] accessKey, RowId rowId) in new IndexCursor(table.Channel, index.RootPage).RawEntries())
            {
                var value = (DateTime)decoder.Decode(db.ReadDataPage(rowId.Page).GetRow(rowId.Row))[kIdx]!;

                var values = new object?[def.Columns.Count];
                values[kIdx] = value;
                byte[] ours = IndexKeyEncoder.Encode(index.Columns, values);

                Assert.True(accessKey.AsSpan().SequenceEqual(ours),
                    $"{value:O}: access={Convert.ToHexString(accessKey)} ours={Convert.ToHexString(ours)}");
                checkedKeys++;
            }

            Assert.Equal(Literals.Length, checkedKeys);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static void Exec(OleDbConnection connection, string sql)
    {
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>Raises a copied file to the ACE 17 format. Page 0 offset 0x14 is the whole upgrade.</summary>
    private static void SetVersionByte(string path, byte version)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write);
        stream.Seek(0x14, SeekOrigin.Begin);
        stream.WriteByte(version);
    }
}
