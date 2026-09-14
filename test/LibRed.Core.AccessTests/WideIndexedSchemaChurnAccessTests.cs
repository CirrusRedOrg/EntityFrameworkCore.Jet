using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// An ACE-oracle stress shape: a multi-page TDEF with split indexes and live rows is edited through both
/// metadata-only and full-rebuild paths. ACE must be able to read, seek, append and update the result.
/// </summary>
[Collection(AceCollection.Name)]
public class WideIndexedSchemaChurnAccessTests
{
    private const int ExtraColumns = 240;
    private const int RowCount = 600;

    [Fact]
    public void Ace_reads_and_extends_a_wide_indexed_table_after_schema_churn()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "wide-index-churn-");
        try
        {
            using (var database = JetDatabase.Open(path, readOnly: false))
            {
                var columns = new List<ColumnSpec>
                {
                    new("Id", JetDataType.Int32, 4, IsFixedLength: true),
                    new("LookupKey", JetDataType.Text, 80, IsFixedLength: false),
                    new("Notes", JetDataType.Memo, 0, IsFixedLength: false),
                };
                for (int i = 0; i < ExtraColumns; i++)
                    columns.Add(new ColumnSpec($"C{i}", JetDataType.Int32, 4, IsFixedLength: true));

                database.CreateTable("WideIndexed", columns, primaryKey: ["Id"]);
                database.CreateIndex("WideIndexed", "IX_LookupKey", [("LookupKey", false)], isUnique: true);
                database.CreateIndex("WideIndexed", "IX_C0", [("C0", false)]);

                Table table = database.OpenTable("WideIndexed");
                for (int id = 1; id <= RowCount; id++)
                {
                    var values = new object?[columns.Count];
                    values[0] = id;
                    values[1] = $"key-{id:D4}";
                    values[2] = id % 75 == 0 ? new string((char)('a' + id % 26), 5_000) : null;
                    values[3] = id * 10;
                    table.Insert(values);
                }

                // Metadata-only edits leave id/variable-index gaps; the indexed retypes force full rebuilds.
                Assert.True(database.AddColumn("WideIndexed", new ColumnSpec("Added", JetDataType.Int32, 4, IsFixedLength: true)));
                Assert.True(database.AddColumn("WideIndexed", new ColumnSpec("AddedMemo", JetDataType.Memo, 0, IsFixedLength: false)));
                Assert.True(database.DropColumn("WideIndexed", "C10"));
                database.RenameColumn("WideIndexed", "LookupKey", "SeekKey");
                database.AlterColumn("WideIndexed", "SeekKey", new ColumnSpec("SeekKey", JetDataType.Text, 160, IsFixedLength: false));
            }

            using var connection = AceTestDatabase.Open(path);
            AssertScalar(connection, "SELECT COUNT(*) FROM WideIndexed", RowCount);
            AssertScalar(connection, "SELECT C0 FROM WideIndexed WHERE SeekKey = 'key-0420'", 4200);
            AssertScalar(connection, "SELECT COUNT(*) FROM WideIndexed WHERE Added IS NULL", RowCount);

            using (var append = connection.CreateCommand())
            {
                append.CommandText = "INSERT INTO WideIndexed (Id, SeekKey, C0, Added, AddedMemo) VALUES (701, 'key-0701', 7010, 8, 'ACE wrote this')";
                Assert.Equal(1, append.ExecuteNonQuery());
            }
            using (var update = connection.CreateCommand())
            {
                update.CommandText = "UPDATE WideIndexed SET SeekKey = 'key-0702', C0 = 7011 WHERE Id = 701";
                Assert.Equal(1, update.ExecuteNonQuery());
            }
            AssertScalar(connection, "SELECT Id FROM WideIndexed WHERE SeekKey = 'key-0702'", 701);
            AssertScalar(connection, "SELECT C0 FROM WideIndexed WHERE C0 = 7011", 7011);
            AssertScalar(connection, "SELECT COUNT(*) FROM WideIndexed", RowCount + 1);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static void AssertScalar(OleDbConnection connection, string sql, int expected)
    {
        using OleDbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        Assert.Equal(expected, Convert.ToInt32(command.ExecuteScalar()));
    }
}
