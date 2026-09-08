using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>Checks mixed-type, mixed-direction composite index keys against a live ACE-created B-tree.</summary>
public class CompositeIndexOrderingAccessTests
{
    [Fact]
    public void Libred_composite_key_bytes_and_traversal_match_ace()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "composite-index-oracle-");
        try
        {
            using (var connection = AceTestDatabase.Open(path))
            {
                Execute(connection, "CREATE TABLE CompositeKeys (A INT, B VARCHAR(30), C DATETIME, D VARBINARY(16), V INT NOT NULL)");
                Execute(connection, "CREATE INDEX IX_CompositeKeys ON CompositeKeys (A ASC, B DESC, C ASC, D DESC, V ASC)");

                object?[][] rows =
                [
                    [null, "same", new DateTime(1899, 12, 29, 18, 0, 0), new byte[] { 1, 2 }, 1],
                    [-1, "Zulu", new DateTime(1850, 6, 15), new byte[] { 0 }, 2],
                    [0, "same", new DateTime(1899, 12, 29, 6, 0, 0), Array.Empty<byte>(), 3],
                    [0, "same", new DateTime(1899, 12, 29, 6, 0, 0), new byte[] { 0 }, 4],
                    [0, "same", new DateTime(1899, 12, 29, 6, 0, 0), new byte[] { 0 }, 5],
                    [0, "Alpha", null, new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 }, 6],
                    [0, null, new DateTime(1900, 1, 1), null, 7],
                    [1, "O'Brien", new DateTime(9999, 12, 31), Enumerable.Range(0, 16).Select(i => (byte)i).ToArray(), 8],
                ];

                foreach (object?[] row in rows)
                {
                    using var insert = connection.CreateCommand();
                    insert.CommandText = "INSERT INTO CompositeKeys (A, B, C, D, V) VALUES (?, ?, ?, ?, ?)";
                    Add(insert, OleDbType.Integer, row[0]);
                    Add(insert, OleDbType.VarWChar, row[1]);
                    Add(insert, OleDbType.Date, row[2]);
                    Add(insert, OleDbType.VarBinary, row[3]);
                    Add(insert, OleDbType.Integer, row[4]);
                    insert.ExecuteNonQuery();
                }
            }

            int[] aceOrder;
            using (var connection = AceTestDatabase.Open(path))
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT V FROM CompositeKeys ORDER BY A ASC, B DESC, C ASC, D DESC, V ASC";
                using var reader = command.ExecuteReader();
                var values = new List<int>();
                while (reader.Read()) values.Add(Convert.ToInt32(reader[0]));
                aceOrder = [.. values];
            }

            using var db = JetDatabase.Open(path);
            Table table = db.OpenTable("CompositeKeys");
            IndexDef index = table.Definition.Indexes.Single(i => i.Name == "IX_CompositeKeys");
            var decoder = new RowDecoder(table.Definition.Columns, db.Format);
            int valueIndex = table.Definition.FindColumn("V")!.Index;
            var libredOrder = new List<int>();

            foreach ((byte[] storedKey, RowId rowId) in new IndexCursor(table.Channel, index.RootPage).RawEntries())
            {
                object?[] row = decoder.Decode(db.ReadDataPage(rowId.Page).GetRow(rowId.Row));
                byte[] encoded = IndexKeyEncoder.Encode(index.Columns, row);
                int value = Convert.ToInt32(row[valueIndex]);
                Assert.True(storedKey.AsSpan().SequenceEqual(encoded),
                    $"V={value}: ACE={Convert.ToHexString(storedKey)} LibRed={Convert.ToHexString(encoded)}");
                libredOrder.Add(value);
            }

            Assert.Equal(aceOrder, libredOrder);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static void Add(OleDbCommand command, OleDbType type, object? value) =>
        command.Parameters.Add(new OleDbParameter { OleDbType = type, Value = value ?? DBNull.Value });

    private static void Execute(OleDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
