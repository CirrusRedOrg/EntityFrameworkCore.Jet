using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// Table.Insert writes the data page before it builds the row's index keys, so a key that fails to encode came
// after the row was already on disk. Called through the Core API — no QueryEngine statement around it — nothing
// took the row back: the file was left with a row no index held, a TDEF row count one short, and ACE then refused
// to delete that row ("The search key was not found in any record"). Update and Delete always ran in a
// transaction of their own; Insert now does too.
//
// The failing key here is a text column in a collation LibRed has no weights for (Irish, as CollationTests
// uses), which IndexKeyCodec refuses rather than encode with the English table.
public class TableInsertAtomicityTests
{
    [Fact]
    public void An_insert_whose_index_key_cannot_be_encoded_leaves_nothing_behind()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "insert-atomic-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var irish = new Collation((CollatingOrder)1084, 0);
                db.CreateTable("T",
                [
                    new("Id", JetDataType.Int32, 4, IsFixedLength: true),
                    new ColumnSpec("Name", JetDataType.Text, 100, IsFixedLength: false) { Collation = irish },
                ]);
                db.CreateIndex("T", "IX_Name", [("Name", false)]);

                Table table = db.OpenTable("T");
                var values = new object?[table.Definition.Columns.Count];
                values[table.Definition.RequireColumn("Id").Index] = 1;
                values[table.Definition.RequireColumn("Name").Index] = "abc";

                Assert.Throws<NotSupportedException>(() => table.Insert(values));
            }

            using var reopened = JetDatabase.Open(path);
            Assert.Empty(reopened.OpenTable("T").Rows());
            Assert.Equal(0, reopened.Catalog.FindTable("T")!.RowCount);
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
