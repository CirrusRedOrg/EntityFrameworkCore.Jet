using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>Cross-engine update/delete coverage for split indexes and relocated indexed rows.</summary>
public class IndexMaintenanceAccessTests
{
    private const int RowCount = 900;

    [Fact]
    public void Ace_sees_libred_index_moves_deletes_and_relocation_after_splits()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "index-maint-lr-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("Maint",
                [
                    new("Id", JetDataType.Int32, 4, IsFixedLength: true),
                    new("K", JetDataType.Int32, 4, IsFixedLength: true),
                    new("S", JetDataType.Text, 255 * 2, IsFixedLength: false),
                ], primaryKey: ["Id"], relationships: null,
                   uniqueConstraints: [new UniqueIndexSpec("IX_K", ["K"])]);
                Table table = db.OpenTable("Maint");
                for (int i = 1; i <= RowCount; i++) table.Insert([i, i, "x"]);

                IndexDef pk = table.Definition.Indexes.Single(i => i.IsPrimaryKey);
                IndexDef ixK = table.Definition.Indexes.Single(i => i.Name == "IX_K");
                Move(table, pk, ixK, 300, newId: 1300, newKey: 2300, new string('R', 255));
                Delete(table, pk, ixK, 400);
            }

            using var connection = AceTestDatabase.Open(path);
            AssertScalar(connection, "SELECT K FROM Maint WHERE Id = 1300", 2300);
            AssertScalar(connection, "SELECT Id FROM Maint WHERE K = 2300", 1300);
            AssertScalar(connection, "SELECT COUNT(*) FROM Maint WHERE Id = 300 OR K = 300", 0);
            AssertScalar(connection, "SELECT COUNT(*) FROM Maint WHERE Id = 400 OR K = 400", 0);
            AssertScalar(connection, "SELECT COUNT(*) FROM Maint", RowCount - 1);
            AssertScalar(connection, "SELECT COUNT(*) FROM Maint WHERE Id BETWEEN 895 AND 900", 6);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Libred_sees_ace_index_moves_deletes_and_relocation_after_splits()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "index-maint-ace-");
        try
        {
            using (var connection = AceTestDatabase.Open(path))
            {
                Execute(connection, "CREATE TABLE Maint (Id INT CONSTRAINT PK_Maint PRIMARY KEY, K INT, S VARCHAR(255))");
                Execute(connection, "CREATE UNIQUE INDEX IX_K ON Maint (K)");
                using var insert = connection.CreateCommand();
                insert.CommandText = "INSERT INTO Maint (Id, K, S) VALUES (?, ?, 'x')";
                insert.Parameters.Add("id", OleDbType.Integer);
                insert.Parameters.Add("k", OleDbType.Integer);
                for (int i = 1; i <= RowCount; i++)
                {
                    insert.Parameters[0].Value = i;
                    insert.Parameters[1].Value = i;
                    insert.ExecuteNonQuery();
                }
                Execute(connection, $"UPDATE Maint SET Id = 1300, K = 2300, S = '{new string('R', 255)}' WHERE Id = 300");
                Execute(connection, "DELETE FROM Maint WHERE Id = 400");
            }

            using var db = JetDatabase.Open(path);
            Table table = db.OpenTable("Maint");
            IndexDef pk = table.Definition.Indexes.Single(i => i.IsPrimaryKey);
            IndexDef ixK = table.Definition.Indexes.Single(i => i.Name == "IX_K");
            Assert.Equal(2300, Value(table, table.SeekRows(pk, [1300]).Single(), "K"));
            Assert.Equal(1300, Value(table, table.SeekRows(ixK, [null, 2300]).Single(), "Id"));
            Assert.Empty(table.SeekRows(pk, [300]));
            Assert.Empty(table.SeekRows(ixK, [null, 300]));
            Assert.Empty(table.SeekRows(pk, [400]));
            Assert.Empty(table.SeekRows(ixK, [null, 400]));
            Assert.Equal(RowCount - 1, table.Rows().Count());

            AssertStoredKeysMatchRows(table, pk);
            AssertStoredKeysMatchRows(table, ixK);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static void Move(Table table, IndexDef pk, IndexDef ixK, int id, int newId, int newKey, string text)
    {
        int idIndex = table.Definition.FindColumn("Id")!.Index;
        int keyIndex = table.Definition.FindColumn("K")!.Index;
        int textIndex = table.Definition.FindColumn("S")!.Index;
        (RowId rowId, object?[] oldValues) = table.SeekRowsWithIds(pk, [id]).Single();
        var newValues = (object?[])oldValues.Clone();
        newValues[idIndex] = newId;
        newValues[keyIndex] = newKey;
        newValues[textIndex] = text;
        table.Update(rowId, newValues);
        table.MoveIndexEntry(pk, oldValues, newValues, rowId);
        table.MoveIndexEntry(ixK, oldValues, newValues, rowId);
    }

    private static void Delete(Table table, IndexDef pk, IndexDef ixK, int id)
    {
        (RowId rowId, object?[] values) = table.SeekRowsWithIds(pk, [id]).Single();
        table.RemoveIndexEntry(pk, values, rowId);
        table.RemoveIndexEntry(ixK, values, rowId);
        table.Delete(rowId);
    }

    private static void AssertStoredKeysMatchRows(Table table, IndexDef index)
    {
        int entries = 0;
        foreach ((byte[] stored, RowId rowId) in new IndexCursor(table.Channel, index.RootPage).RawEntries())
        {
            object?[] row = Assert.IsType<object?[]>(table.GetRow(rowId));
            Assert.Equal(stored, IndexKeyEncoder.Encode(index.Columns, row));
            entries++;
        }
        Assert.Equal(RowCount - 1, entries);
    }

    private static int Value(Table table, object?[] row, string column) =>
        Convert.ToInt32(row[table.Definition.FindColumn(column)!.Index]);

    private static void AssertScalar(OleDbConnection connection, string sql, int expected)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        Assert.Equal(expected, Convert.ToInt32(command.ExecuteScalar()));
    }

    private static void Execute(OleDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
