using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>Rollback coverage for page allocation, B-tree splits, relocation, and long-value ownership.</summary>
public class TransactionPhysicalRollbackAccessTests
{
    [Fact]
    public void Rollback_discards_index_splits_and_restores_seekable_tree_for_ace()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "rollback-split-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("SplitTxn",
                    [new("Id", JetDataType.Int32, 4, IsFixedLength: true),
                     new("S", JetDataType.Text, 40, IsFixedLength: false)],
                    primaryKey: ["Id"]);
                Table table = db.OpenTable("SplitTxn");
                for (int i = 1; i <= 10; i++) table.Insert([i, $"base-{i}"]);
            }
            byte[] before = File.ReadAllBytes(path);

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                Table table = db.OpenTable("SplitTxn");
                db.BeginTransaction();
                for (int i = 11; i <= 1300; i++) table.Insert([i, $"new-{i}"]);
                IndexDef changedPk = table.Definition.Indexes.Single(i => i.IsPrimaryKey);
                Assert.Equal("new-1300", Value(table, table.SeekRows(changedPk, [1300]).Single(), "S"));
                Assert.Equal(1300, table.Rows().Count());

                db.Rollback();
                table = db.OpenTable("SplitTxn");
                IndexDef restoredPk = table.Definition.Indexes.Single(i => i.IsPrimaryKey);
                Assert.Equal(10, table.Rows().Count());
                Assert.Empty(table.SeekRows(restoredPk, [1300]));
                Assert.Equal("base-10", Value(table, table.SeekRows(restoredPk, [10]).Single(), "S"));
            }

            Assert.Equal(before, File.ReadAllBytes(path));
            using var connection = AceTestDatabase.Open(path);
            AssertScalar(connection, "SELECT COUNT(*) FROM SplitTxn", 10);
            AssertScalar(connection, "SELECT COUNT(*) FROM SplitTxn WHERE Id = 1300", 0);
            Assert.Equal("base-10", ExecuteScalar(connection, "SELECT S FROM SplitTxn WHERE Id = 10"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Rollback_restores_relocated_row_and_moved_secondary_index_for_ace()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "rollback-relocate-");
        try
        {
            const string oldKey = "key-3";
            string large = new('R', 255);
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("RelocateTxn",
                [
                    new("Id", JetDataType.Int32, 4, IsFixedLength: true),
                    new("K", JetDataType.Text, 255 * 2, IsFixedLength: false),
                    new("A", JetDataType.Text, 255 * 2, IsFixedLength: false),
                    new("B", JetDataType.Text, 255 * 2, IsFixedLength: false),
                ], primaryKey: ["Id"], relationships: null,
                   uniqueConstraints: [new UniqueIndexSpec("IX_K", ["K"])]);
                Table table = db.OpenTable("RelocateTxn");
                for (int i = 1; i <= 12; i++) table.Insert([i, $"key-{i}", new string('a', 80), new string('b', 80)]);
            }
            byte[] before = File.ReadAllBytes(path);

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                Table table = db.OpenTable("RelocateTxn");
                IndexDef pk = table.Definition.Indexes.Single(i => i.IsPrimaryKey);
                IndexDef ixK = table.Definition.Indexes.Single(i => i.Name == "IX_K");
                (RowId rowId, object?[] oldValues) = table.SeekRowsWithIds(pk, [3]).Single();
                object?[] newValues = (object?[])oldValues.Clone();
                newValues[table.Definition.FindColumn("K")!.Index] = large;
                newValues[table.Definition.FindColumn("A")!.Index] = large;
                newValues[table.Definition.FindColumn("B")!.Index] = large;

                db.BeginTransaction();
                table.Update(rowId, newValues);
                table.MoveIndexEntry(ixK, oldValues, newValues, rowId);
                Assert.Single(table.SeekRows(ixK, [null, large]));
                Assert.Empty(table.SeekRows(ixK, [null, oldKey]));

                db.Rollback();
                table = db.OpenTable("RelocateTxn");
                ixK = table.Definition.Indexes.Single(i => i.Name == "IX_K");
                Assert.Single(table.SeekRows(ixK, [null, oldKey]));
                Assert.Empty(table.SeekRows(ixK, [null, large]));
                Assert.Equal(oldKey, Value(table, table.Rows().Single(r => Convert.ToInt32(r[0]) == 3), "K"));
            }

            Assert.Equal(before, File.ReadAllBytes(path));
            using var connection = AceTestDatabase.Open(path);
            AssertScalar(connection, $"SELECT COUNT(*) FROM RelocateTxn WHERE K = '{large}'", 0);
            Assert.Equal(oldKey, ExecuteScalar(connection, "SELECT K FROM RelocateTxn WHERE Id = 3"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Rollback_restores_lval_data_and_usage_maps_for_ace()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "rollback-lval-");
        try
        {
            string original = new('A', 20000);
            string replacement = new('B', 24000);
            int[] ownedBefore;
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("LvalTxn",
                    [new("Id", JetDataType.Int32, 4, IsFixedLength: true),
                     new("M", JetDataType.Memo, 0, IsFixedLength: false)],
                    primaryKey: ["Id"]);
                Table table = db.OpenTable("LvalTxn");
                table.Insert([1, original]);
                ownedBefore = table.UsageMap.DataPages().ToArray();
            }
            byte[] before = File.ReadAllBytes(path);

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                Table table = db.OpenTable("LvalTxn");
                IndexDef pk = table.Definition.Indexes.Single(i => i.IsPrimaryKey);
                (RowId rowId, object?[] values) = table.SeekRowsWithIds(pk, [1]).Single();
                object?[] updated = (object?[])values.Clone();
                int memoIndex = table.Definition.FindColumn("M")!.Index;
                updated[memoIndex] = replacement;

                db.BeginTransaction();
                table.Update(rowId, updated, new HashSet<int> { memoIndex });
                table.Insert([2, new string('C', 28000)]);
                Assert.Equal(replacement, table.SeekRows(pk, [1]).Single()[memoIndex]);
                Assert.Equal(2, table.Rows().Count());

                db.Rollback();
                table = db.OpenTable("LvalTxn");
                pk = table.Definition.Indexes.Single(i => i.IsPrimaryKey);
                Assert.Equal(original, table.SeekRows(pk, [1]).Single()[memoIndex]);
                Assert.Empty(table.SeekRows(pk, [2]));
                Assert.Equal(ownedBefore, table.UsageMap.DataPages().ToArray());
            }

            Assert.Equal(before, File.ReadAllBytes(path));
            using var connection = AceTestDatabase.Open(path);
            AssertScalar(connection, "SELECT COUNT(*) FROM LvalTxn", 1);
            Assert.Equal(original, ExecuteScalar(connection, "SELECT M FROM LvalTxn WHERE Id = 1"));
        }
        finally { TemporaryDatabase.Delete(path); }
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
