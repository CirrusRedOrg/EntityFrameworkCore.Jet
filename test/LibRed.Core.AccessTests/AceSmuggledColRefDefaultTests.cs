using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

// LibRed writes a column-reference default ([A] + 2) straight to LvProp — a default ACE's own DDL parser refuses
// to CREATE. The point: this does NOT produce a corrupt file. ACE opens it fine and reads the stored default;
// the column-reference prohibition is enforced by ACE's expression service at INSERT (evaluation) time, not by
// its DDL parser. So ACE inserts nothing — it rejects the row with the same "field in a default" error it gives
// at create time. There is no way to smuggle a working column-ref default past the engine.
public class AceSmuggledColRefDefaultTests
{
    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    [Theory]
    [InlineData("[A] + 2", "does not recognize")]  // engine names the field reference in the default
    [InlineData("A + 2", "Type mismatch")]          // bare A parses as something else → type mismatch
    public void Access_opens_the_file_but_rejects_an_insert_using_a_smuggled_column_ref_default(string def, string expectedError)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "smug-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                db.CreateTable("T",
                [
                    new ColumnSpec("K", JetDataType.Int32, 4, IsFixedLength: true),
                    new ColumnSpec("A", JetDataType.Int32, 4, IsFixedLength: true),
                    new ColumnSpec("B", JetDataType.Int32, 4, IsFixedLength: true),
                ],
                primaryKey: ["K"],
                columnDefaults: [("B", def)]);

            // LibRed persisted the column-ref default text verbatim, and re-reads it — the file is well-formed.
            using (var reopened = JetDatabase.Open(path))
                Assert.Equal(def, reopened.Catalog.UserTables.Single(t => t.Name == "T")
                    .Columns.Single(c => c.Name == "B").DefaultValue);

            // ACE opens the file (no repair/corruption) but rejects the insert when it evaluates the default.
            using var conn = OpenOleDb(path);
            using var insert = conn.CreateCommand();
            insert.CommandText = "INSERT INTO T (K, A) VALUES (1, 5)";
            var ex = Assert.ThrowsAny<OleDbException>(() => insert.ExecuteNonQuery());
            Assert.Contains(expectedError, ex.Message);

            // No row was written.
            using var count = conn.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM T";
            Assert.Equal(0, Convert.ToInt32(count.ExecuteScalar()));
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
