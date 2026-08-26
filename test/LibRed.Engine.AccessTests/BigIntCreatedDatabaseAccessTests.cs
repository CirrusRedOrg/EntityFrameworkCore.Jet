using System.Data.OleDb;
using LibRed;
using LibRed.Data;
using LibRed.Engine;
using LibRed.Formats;
using Xunit;

namespace LibRed.Engine.Tests;

// BIGINT (Large Number) written by LibRed into a file LibRed created and then upgraded in place, read back by
// Access's own engine.
//
// Large Number forces a DIFFERENT format from Date/Time Extended — ACE 16 (0x05), not ACE 17 (0x06) — and ACE
// keeps it in the row's VARIABLE region despite it always being 8 bytes. Both of those are places a mismatch
// would put the value somewhere Access does not look, and neither shows up in a LibRed-only round trip: LibRed
// reading back what LibRed wrote agrees with itself either way.
//
// Read through ACE's ordinary reader here rather than through scalar functions. Unlike Date/Time Extended, the
// provider materialises this type correctly. Its BIGINT defect is on the parameter side instead, and narrower
// than it first looks: OleDbType.BigInt converts nothing at all, but Numeric, Decimal and Variant each carry
// the full range exactly (see BigIntKeyEncodingTests).
[Collection(AceCollection.Name)]
public class BigIntCreatedDatabaseAccessTests : TempDatabaseTest
{
    [Fact]
    public void Ace_reads_bigint_values_libred_wrote_into_a_database_libred_upgraded()
    {
        // Both extremes and both signs: the index/storage transforms are sign-sensitive, and a positives-only
        // sample would agree with almost any encoding.
        long[] values = [0L, 1L, -1L, 42L, -42L, long.MaxValue, long.MinValue];

        string path = TemporaryDatabase.CreatePath("libred-bigint-ace-");
        File.Delete(path);   // CreateDatabase synthesises the file and refuses an existing one
        try
        {
            LibRedConnection.CreateDatabase($"Data Source={path}");   // the ACE 12 default, which cannot hold it
            Assert.Equal(0x02, VersionByte(path));

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var engine = new QueryEngine(db);
                engine.ExecuteNonQuery("CREATE TABLE `B` (`Id` INTEGER PRIMARY KEY, `V` BIGINT NULL)");
                Assert.Equal(JetVersion.Version16_2016, db.Format.Version);

                for (int i = 0; i < values.Length; i++)
                    engine.ExecuteNonQuery("INSERT INTO `B` (`Id`, `V`) VALUES (@id, @v)",
                        new Dictionary<string, object?> { ["id"] = i, ["v"] = values[i] });
            }

            Assert.Equal(0x05, VersionByte(path));

            using var connection = AceTestDatabase.Open(path);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, V FROM B ORDER BY Id";
            using OleDbDataReader reader = command.ExecuteReader();

            var read = new List<long>();
            while (reader.Read()) read.Add(Convert.ToInt64(reader.GetValue(1)));
            Assert.Equal(values, read);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static byte VersionByte(string path)
    {
        using var stream = File.OpenRead(path);
        stream.Seek(0x14, SeekOrigin.Begin);
        return (byte)stream.ReadByte();
    }
}
