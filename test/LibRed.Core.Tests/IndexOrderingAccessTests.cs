using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>Checks boundary-value index byte encoding and traversal order against ACE's actual ORDER BY.</summary>
public class IndexOrderingAccessTests
{
    public static TheoryData<string, OleDbType, object?[]> KeyFamilies => new()
    {
        { "INT", OleDbType.Integer, [int.MinValue, -1, 0, 1, int.MaxValue, null] },
        { "SINGLE", OleDbType.Single, [-1000f, -0.25f, 0f, 0.25f, 1000f, null] },
        { "DOUBLE", OleDbType.Double, [-1e100, -0.5d, 0d, 0.5d, 1e100, null] },
        { "CURRENCY", OleDbType.Currency, [-922337203685477.5808m, -0.0001m, 0m, 0.0001m, 922337203685477.5807m, null] },
        { "DATETIME", OleDbType.Date, [new DateTime(1850, 6, 15, 10, 30, 0), new DateTime(1899, 12, 29, 6, 0, 0), new DateTime(1899, 12, 29, 18, 0, 0), new DateTime(1900, 1, 1), new DateTime(9999, 12, 31), null] },
        { "GUID", OleDbType.Guid, [Guid.Empty, new Guid("00000000-0000-0000-0000-000000000001"), new Guid("01020304-0506-0708-090a-0b0c0d0e0f10"), new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff"), null] },
        { "VARBINARY(16)", OleDbType.VarBinary, [new byte[] { 0 }, new byte[] { 1, 0 }, new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 }, new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 }, Enumerable.Range(0, 16).Select(i => (byte)i).ToArray(), null] },
        { "VARCHAR(50)", OleDbType.VarWChar, ["", "0", "A", "A-B", "O'Brien", "Z", null] },
    };

    [Theory]
    [MemberData(nameof(KeyFamilies))]
    public void Libred_key_bytes_and_traversal_match_ace_for_boundary_values(
        string storeType, OleDbType parameterType, object?[] values)
    {
        foreach (bool ascending in new[] { true, false })
            AssertFamily(storeType, parameterType, values, ascending);
    }

    [Theory]
    [InlineData(true, "7F")]
    [InlineData(false, "80")]
    public void Empty_binary_key_matches_ace_start_marker_only(bool ascending, string expectedHex)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "empty-binary-key-");
        try
        {
            using (var connection = AceTestDatabase.Open(path))
            {
                Execute(connection, "CREATE TABLE EmptyBinaryKey (K VARBINARY(16), V INT NOT NULL)");
                Execute(connection, $"CREATE INDEX IX_EmptyBinaryKey ON EmptyBinaryKey (K {(ascending ? "ASC" : "DESC")})");
                using var insert = connection.CreateCommand();
                insert.CommandText = "INSERT INTO EmptyBinaryKey (K, V) VALUES (?, 7)";
                insert.Parameters.Add(new OleDbParameter("k", OleDbType.VarBinary) { Value = Array.Empty<byte>() });
                insert.ExecuteNonQuery();
            }

            using var db = JetDatabase.Open(path);
            var table = db.OpenTable("EmptyBinaryKey");
            IndexDef index = table.Definition.Indexes.Single(i => i.Name == "IX_EmptyBinaryKey");
            (byte[] stored, RowId rowId) = Assert.Single(new IndexCursor(table.Channel, index.RootPage).RawEntries());
            Assert.Equal(expectedHex, Convert.ToHexString(stored));

            object?[] row = new RowDecoder(table.Definition.Columns, db.Format)
                .Decode(db.ReadDataPage(rowId.Page).GetRow(rowId.Row));
            var aligned = new object?[table.Definition.Columns.Count];
            aligned[table.Definition.FindColumn("K")!.Index] = row[table.Definition.FindColumn("K")!.Index];
            Assert.Equal(stored, IndexKeyEncoder.Encode(index.Columns, aligned));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static void AssertFamily(string storeType, OleDbType parameterType, object?[] values, bool ascending)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "index-order-oracle-");
        try
        {
            using (var connection = AceTestDatabase.Open(path))
            {
                Execute(connection, $"CREATE TABLE BoundaryKeys (K {storeType}, V INT NOT NULL)");
                Execute(connection, $"CREATE INDEX IX_BoundaryKeys ON BoundaryKeys (K {(ascending ? "ASC" : "DESC")})");
                for (int i = 0; i < values.Length; i++)
                {
                    using var insert = connection.CreateCommand();
                    insert.CommandText = "INSERT INTO BoundaryKeys (K, V) VALUES (?, ?)";
                    insert.Parameters.Add(new OleDbParameter("k", parameterType) { Value = values[i] ?? DBNull.Value });
                    insert.Parameters.Add(new OleDbParameter("v", OleDbType.Integer) { Value = i });
                    insert.ExecuteNonQuery();
                }
            }

            int[] aceOrder;
            using (var connection = AceTestDatabase.Open(path))
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"SELECT V FROM BoundaryKeys ORDER BY K {(ascending ? "ASC" : "DESC")}";
                using var reader = command.ExecuteReader();
                var ids = new List<int>();
                while (reader.Read()) ids.Add(Convert.ToInt32(reader[0]));
                aceOrder = [.. ids];
            }

            using var db = JetDatabase.Open(path);
            var table = db.OpenTable("BoundaryKeys");
            IndexDef index = table.Definition.Indexes.Single(i => i.Name == "IX_BoundaryKeys");
            int keyIndex = table.Definition.FindColumn("K")!.Index;
            int valueIndex = table.Definition.FindColumn("V")!.Index;
            var decoder = new RowDecoder(table.Definition.Columns, db.Format);
            var libredOrder = new List<int>();

            foreach ((byte[] storedKey, RowId rowId) in new IndexCursor(table.Channel, index.RootPage).RawEntries())
            {
                object?[] row = decoder.Decode(db.ReadDataPage(rowId.Page).GetRow(rowId.Row));
                var aligned = new object?[table.Definition.Columns.Count];
                aligned[keyIndex] = row[keyIndex];
                byte[] encoded = IndexKeyEncoder.Encode(index.Columns, aligned);
                Assert.True(storedKey.AsSpan().SequenceEqual(encoded),
                    $"V={row[valueIndex]}, K={Describe(row[keyIndex])}: ACE={Convert.ToHexString(storedKey)} LibRed={Convert.ToHexString(encoded)}");
                libredOrder.Add(Convert.ToInt32(row[valueIndex]));
            }

            Assert.True(aceOrder.SequenceEqual(libredOrder),
                $"ACE ORDER BY=[{string.Join(',', aceOrder)}], index traversal=[{string.Join(',', libredOrder)}]");
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static void Execute(OleDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string Describe(object? value) => value switch
    {
        null => "NULL",
        byte[] bytes => Convert.ToHexString(bytes),
        _ => value.ToString() ?? "NULL",
    };
}
