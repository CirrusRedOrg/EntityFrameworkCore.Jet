using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Crypto;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

public class TransactionSavepointAndEncryptionAccessTests
{
    [Theory]
    [InlineData(AccessEncryption.OfficeStandardRc4)]
    [InlineData(AccessEncryption.OfficeStandardAes)]
    [InlineData(AccessEncryption.Agile)]
    public void Encrypted_commit_publishes_splits_relocation_and_lval_changes_to_other_readers_and_ace(
        AccessEncryption scheme)
    {
        const string password = "Commit-S3cret!";
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "encrypted-commit-");
        string large = new('C', 255);
        string committedMemo = new('D', 20000);
        try
        {
            using (var setup = JetDatabase.Open(path, readOnly: false))
            {
                CreatePhysicalTable(setup, "EncryptedCommit");
                Table table = setup.OpenTable("EncryptedCommit");
                for (int i = 1; i <= 10; i++) table.Insert([i, $"key-{i}", "short", "short", new string('A', 5000)]);
            }
            DatabaseEncryption.SetPassword(path, password, scheme);
            byte[] encryptedBefore = File.ReadAllBytes(path);

            using (var writer = JetDatabase.Open(path, readOnly: false, password: password))
            using (var reader = JetDatabase.Open(path, password: password))
            {
                Table writerTable = writer.OpenTable("EncryptedCommit");
                writer.BeginTransaction();
                for (int i = 11; i <= 900; i++) writerTable.Insert([i, $"new-{i}", "x", "x", "memo"]);
                MoveLargeIndexedRow(writerTable, id: 3, large, committedMemo);

                Assert.Equal(900, writerTable.Rows().Count());
                Assert.Equal(10, reader.OpenTable("EncryptedCommit").Rows().Count());

                writer.Commit();
                Table readerTable = reader.OpenTable("EncryptedCommit");
                IndexDef pk = readerTable.Definition.Indexes.Single(i => i.IsPrimaryKey);
                IndexDef ixK = readerTable.Definition.Indexes.Single(i => i.Name == "IX_K");
                Assert.Equal(900, readerTable.Rows().Count());
                Assert.Single(readerTable.SeekRows(pk, [900]));
                Assert.Single(readerTable.SeekRows(ixK, [null, large]));
                Assert.Equal(committedMemo, Value(readerTable, readerTable.SeekRows(pk, [3]).Single(), "M"));
            }

            Assert.NotEqual(encryptedBefore, File.ReadAllBytes(path));
            using var connection = AceTestDatabase.Open(path, password);
            AssertScalar(connection, "SELECT COUNT(*) FROM EncryptedCommit", 900);
            AssertScalar(connection, "SELECT COUNT(*) FROM EncryptedCommit WHERE Id = 900", 1);
            Assert.Equal(large, ExecuteScalar(connection, "SELECT K FROM EncryptedCommit WHERE Id = 3"));
            Assert.Equal(committedMemo, ExecuteScalar(connection, "SELECT M FROM EncryptedCommit WHERE Id = 3"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Inner_rollback_discards_splits_relocation_and_lval_pages_but_outer_commit_survives_for_ace()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "savepoint-physical-");
        string oldKey = "key-3";
        string large = new('R', 255);
        string originalMemo = new('A', 5000);
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                CreatePhysicalTable(db, "SavepointTxn");
                Table table = db.OpenTable("SavepointTxn");
                for (int i = 1; i <= 10; i++) table.Insert([i, $"key-{i}", "short", "short", originalMemo]);

                db.BeginNested();
                table.Insert([11, "outer", "outer", "outer", "outer"]);

                db.BeginNested();
                for (int i = 12; i <= 1000; i++) table.Insert([i, $"inner-{i}", "x", "x", new string('M', 6000)]);
                MoveLargeIndexedRow(table, id: 3, large, new string('B', 20000));
                IndexDef pk = table.Definition.Indexes.Single(i => i.IsPrimaryKey);
                IndexDef ixK = table.Definition.Indexes.Single(i => i.Name == "IX_K");
                Assert.Single(table.SeekRows(pk, [1000]));
                Assert.Single(table.SeekRows(ixK, [null, large]));

                db.RollbackNested();
                Assert.True(db.InTransaction);
                Assert.Equal(1, db.TransactionDepth);
                table = db.OpenTable("SavepointTxn");
                pk = table.Definition.Indexes.Single(i => i.IsPrimaryKey);
                ixK = table.Definition.Indexes.Single(i => i.Name == "IX_K");
                Assert.Single(table.SeekRows(pk, [11]));
                Assert.Empty(table.SeekRows(pk, [12]));
                Assert.Empty(table.SeekRows(pk, [1000]));
                Assert.Single(table.SeekRows(ixK, [null, oldKey]));
                Assert.Empty(table.SeekRows(ixK, [null, large]));
                Assert.Equal(originalMemo, Value(table, table.SeekRows(pk, [3]).Single(), "M"));

                db.CommitNested();
                Assert.False(db.InTransaction);
            }

            using var connection = AceTestDatabase.Open(path);
            AssertScalar(connection, "SELECT COUNT(*) FROM SavepointTxn", 11);
            AssertScalar(connection, "SELECT COUNT(*) FROM SavepointTxn WHERE Id = 11 AND K = 'outer'", 1);
            AssertScalar(connection, "SELECT COUNT(*) FROM SavepointTxn WHERE Id >= 12", 0);
            Assert.Equal(oldKey, ExecuteScalar(connection, "SELECT K FROM SavepointTxn WHERE Id = 3"));
            Assert.Equal(originalMemo, ExecuteScalar(connection, "SELECT M FROM SavepointTxn WHERE Id = 3"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Theory]
    [InlineData(AccessEncryption.OfficeStandardRc4)]
    [InlineData(AccessEncryption.OfficeStandardAes)]
    [InlineData(AccessEncryption.Agile)]
    public void Encrypted_rollback_discards_splits_relocation_and_lval_writes_byte_for_byte(AccessEncryption scheme)
    {
        const string password = "Txn-S3cret!";
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "encrypted-rollback-");
        string oldKey = "key-3";
        string large = new('Z', 255);
        string originalMemo = new('A', 5000);
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                CreatePhysicalTable(db, "EncryptedTxn");
                Table table = db.OpenTable("EncryptedTxn");
                for (int i = 1; i <= 10; i++) table.Insert([i, $"key-{i}", "short", "short", originalMemo]);
            }
            DatabaseEncryption.SetPassword(path, password, scheme);
            byte[] encryptedBefore = File.ReadAllBytes(path);

            using (var db = JetDatabase.Open(path, readOnly: false, password: password))
            {
                Table table = db.OpenTable("EncryptedTxn");
                db.BeginTransaction();
                for (int i = 11; i <= 900; i++) table.Insert([i, $"new-{i}", "x", "x", new string('N', 6000)]);
                MoveLargeIndexedRow(table, id: 3, large, new string('B', 20000));
                IndexDef pk = table.Definition.Indexes.Single(i => i.IsPrimaryKey);
                IndexDef ixK = table.Definition.Indexes.Single(i => i.Name == "IX_K");
                Assert.Single(table.SeekRows(pk, [900]));
                Assert.Single(table.SeekRows(ixK, [null, large]));

                db.Rollback();
                table = db.OpenTable("EncryptedTxn");
                pk = table.Definition.Indexes.Single(i => i.IsPrimaryKey);
                ixK = table.Definition.Indexes.Single(i => i.Name == "IX_K");
                Assert.Equal(10, table.Rows().Count());
                Assert.Empty(table.SeekRows(pk, [900]));
                Assert.Single(table.SeekRows(ixK, [null, oldKey]));
                Assert.Empty(table.SeekRows(ixK, [null, large]));
                Assert.Equal(originalMemo, Value(table, table.SeekRows(pk, [3]).Single(), "M"));
            }

            Assert.Equal(encryptedBefore, File.ReadAllBytes(path));
            using (var db = JetDatabase.Open(path, password: password))
                Assert.Equal(10, db.OpenTable("EncryptedTxn").Rows().Count());

            using var connection = AceTestDatabase.Open(path, password);
            AssertScalar(connection, "SELECT COUNT(*) FROM EncryptedTxn", 10);
            AssertScalar(connection, "SELECT COUNT(*) FROM EncryptedTxn WHERE Id = 900", 0);
            Assert.Equal(oldKey, ExecuteScalar(connection, "SELECT K FROM EncryptedTxn WHERE Id = 3"));
            Assert.Equal(originalMemo, ExecuteScalar(connection, "SELECT M FROM EncryptedTxn WHERE Id = 3"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static void CreatePhysicalTable(JetDatabase db, string name) =>
        db.CreateTable(name,
        [
            new("Id", JetDataType.Int32, 4, IsFixedLength: true),
            new("K", JetDataType.Text, 255 * 2, IsFixedLength: false),
            new("A", JetDataType.Text, 255 * 2, IsFixedLength: false),
            new("B", JetDataType.Text, 255 * 2, IsFixedLength: false),
            new("M", JetDataType.Memo, 0, IsFixedLength: false),
        ], primaryKey: ["Id"], relationships: null,
           uniqueConstraints: [new UniqueIndexSpec("IX_K", ["K"])]);

    private static void MoveLargeIndexedRow(Table table, int id, string newKey, string newMemo)
    {
        IndexDef pk = table.Definition.Indexes.Single(i => i.IsPrimaryKey);
        IndexDef ixK = table.Definition.Indexes.Single(i => i.Name == "IX_K");
        (RowId rowId, object?[] oldValues) = table.SeekRowsWithIds(pk, [id]).Single();
        object?[] newValues = (object?[])oldValues.Clone();
        newValues[table.Definition.FindColumn("K")!.Index] = newKey;
        newValues[table.Definition.FindColumn("A")!.Index] = newKey;
        newValues[table.Definition.FindColumn("B")!.Index] = newKey;
        int memoIndex = table.Definition.FindColumn("M")!.Index;
        newValues[memoIndex] = newMemo;
        table.Update(rowId, newValues, new HashSet<int>
        {
            table.Definition.FindColumn("K")!.Index,
            table.Definition.FindColumn("A")!.Index,
            table.Definition.FindColumn("B")!.Index,
            memoIndex,
        });
        table.MoveIndexEntry(ixK, oldValues, newValues, rowId);
    }

    private static object? Value(Table table, object?[] row, string column) =>
        row[table.Definition.FindColumn(column)!.Index];

    private static object? ExecuteScalar(OleDbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static void AssertScalar(OleDbConnection connection, string sql, int expected) =>
        Assert.Equal(expected, Convert.ToInt32(ExecuteScalar(connection, sql)));
}
