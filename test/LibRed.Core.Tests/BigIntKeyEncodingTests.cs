using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// BIGINT (Large Number, JetDataType 0x13) as ACE stores it: the format upgrade it forces, the row layout it
// takes, and its index keys byte-for-byte.
//
// Northwind is ACE 12 (version byte 0x02); Large Number needs ACE 16 (0x05). Unlike the DATETIME2 tests
// nothing flips the byte by hand here — ACE is left to do it, because whether it does was the open question.
public class BigIntKeyEncodingTests
{
    // Both extremes and both signs. The key transform is a sign-bit flip, so a sample of positives would pass
    // against almost any encoding; Northwind itself has no negative data to catch that by accident.
    private static readonly long[] Values =
    [
        0L, 1L, -1L, 42L, -42L, long.MaxValue, long.MinValue,
    ];

    // Numeric, not BigInt. OleDbType.BigInt carries NO value at all into a Large Number column through this
    // provider — every one of these fails with "data value could not be converted", zero included — so the one
    // type named for the job is the only one that cannot do it. EFCore.Jet already forces the same workaround
    // for its own long parameters (JetLongTypeMapping.ConfigureParameter sets OLE DB 131 / ODBC 7, commented
    // "Using BigInt doesn't always work ... When running in x64 it fails to convert"), though that mapping
    // targets a decimal(20,0) column rather than a real 0x13 one.
    //
    // Measured across both extremes: Numeric, Decimal and Variant each round-trip the full range exactly;
    // VarNumeric is rejected outright ("Type name is invalid"); Double is the trap — it succeeds quietly for
    // small values and overflows near ±2^63.
    private const OleDbType ParameterType = OleDbType.Numeric;

    [Fact]
    public void Adding_a_bigint_column_makes_ace_raise_the_file_to_ace16()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "libred-bigint-ver-");
        try
        {
            Assert.Equal(0x02, VersionByte(path));

            using (OleDbConnection connection = AceTestDatabase.Open(path))
                Exec(connection, "CREATE TABLE BVer (K BIGINT, V LONG)");

            // 0x05, not the 0x06 that DATETIME2 forces: the two types arrived in different formats.
            Assert.Equal(0x05, VersionByte(path));
            using var db = JetDatabase.Open(path);
            Assert.Equal(JetVersion.Version16_2016, db.Format.Version);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Always 8 bytes, yet ACE keeps it in the row's VARIABLE region rather than the fixed one. LibRed has to
    // create the column the same way or it writes the value somewhere ACE does not look for it.
    [Fact]
    public void Ace_stores_a_bigint_column_as_variable_length()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "libred-bigint-shape-");
        try
        {
            using (OleDbConnection connection = AceTestDatabase.Open(path))
                Exec(connection, "CREATE TABLE BShape (K BIGINT, V LONG)");

            using var db = JetDatabase.Open(path);
            ColumnDef k = db.OpenTable("BShape").Definition.FindColumn("K")!;

            Assert.Equal(JetDataType.Int64, k.Type);
            Assert.Equal(8, k.Length);
            Assert.False(k.IsFixedLength);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Encoded_bigint_keys_match_access_byte_for_byte_ascending()
        => AssertKeysMatchAccess("CREATE INDEX IX_BKey ON BKey (K)");

    [Fact]
    public void Encoded_bigint_keys_match_access_byte_for_byte_descending()
        => AssertKeysMatchAccess("CREATE INDEX IX_BKey ON BKey (K DESC)");

    private static void AssertKeysMatchAccess(string indexDdl)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "libred-bigintkey-");
        try
        {
            using (OleDbConnection connection = AceTestDatabase.Open(path))
            {
                Exec(connection, "CREATE TABLE BKey (K BIGINT, V LONG)");
                Exec(connection, indexDdl);

                for (int i = 0; i < Values.Length; i++)
                {
                    using OleDbCommand insert = connection.CreateCommand();
                    insert.CommandText = "INSERT INTO BKey (K, V) VALUES (?, ?)";
                    insert.Parameters.Add(new OleDbParameter("k", ParameterType) { Value = Values[i] });
                    insert.Parameters.AddWithValue("v", i);
                    insert.ExecuteNonQuery();
                }
            }

            using var db = JetDatabase.Open(path);
            var table = db.OpenTable("BKey");
            var def = table.Definition;
            IndexDef index = def.Indexes.Single(i => i.Columns.Any(c => c.Column.Name == "K"));
            int kIdx = def.FindColumn("K")!.Index;
            var decoder = new RowDecoder(def.Columns, db.Format);

            int checkedKeys = 0;
            foreach ((byte[] accessKey, RowId rowId) in new IndexCursor(table.Channel, index.RootPage).RawEntries())
            {
                var value = (long)decoder.Decode(db.ReadDataPage(rowId.Page).GetRow(rowId.Row))[kIdx]!;

                var values = new object?[def.Columns.Count];
                values[kIdx] = value;
                byte[] ours = IndexKeyEncoder.Encode(index.Columns, values);

                Assert.True(accessKey.AsSpan().SequenceEqual(ours),
                    $"{value}: access={Convert.ToHexString(accessKey)} ours={Convert.ToHexString(ours)}");
                checkedKeys++;
            }

            Assert.Equal(Values.Length, checkedKeys);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static void Exec(OleDbConnection connection, string sql)
    {
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static byte VersionByte(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(0x14, SeekOrigin.Begin);
        return (byte)stream.ReadByte();
    }
}
