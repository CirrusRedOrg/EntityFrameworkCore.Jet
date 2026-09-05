using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// A LibRed-written ON DELETE SET NULL relationship (grbit 0x2000) is byte-faithful: Access reads it and
/// applies the SET NULL itself when it deletes the parent.
/// </summary>
public class RefActionAccessTests
{
    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    [Fact]
    public void Access_applies_a_libred_written_on_delete_set_null()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "ri-ace-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("P", [new("Id", JetDataType.Int32, 4, IsFixedLength: true)], primaryKey: ["Id"]);
                db.CreateTable("C",
                    [new("Id", JetDataType.Int32, 4, IsFixedLength: true), new("ParentId", JetDataType.Int32, 4, IsFixedLength: true)],
                    primaryKey: ["Id"],
                    relationships: [new RelationshipSpec("FK_C", "P", [("ParentId", "Id")],
                        IsEnforced: true, CascadeUpdate: false, CascadeDelete: false, DeleteSetNull: true)]);

                var p = db.OpenTable("P"); p.Insert([1]);
                var c = db.OpenTable("C"); c.Insert([100, 1]); c.Insert([101, 1]);
            }

            using var conn = OpenOleDb(path);
            using (var cmd = conn.CreateCommand())
            { cmd.CommandText = "DELETE FROM P WHERE Id = 1"; Assert.Equal(1, cmd.ExecuteNonQuery()); } // ACE applies SET NULL
            using (var cmd = conn.CreateCommand())
            { cmd.CommandText = "SELECT COUNT(*) FROM C"; Assert.Equal(2, Convert.ToInt32(cmd.ExecuteScalar())); }        // children kept
            using (var cmd = conn.CreateCommand())
            { cmd.CommandText = "SELECT COUNT(*) FROM C WHERE ParentId IS NULL"; Assert.Equal(2, Convert.ToInt32(cmd.ExecuteScalar())); } // both nulled
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
