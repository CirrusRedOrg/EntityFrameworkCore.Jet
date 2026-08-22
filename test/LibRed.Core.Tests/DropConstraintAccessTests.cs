using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// After LibRed drops a foreign key (soft-deleting its MSysRelationships rows), Access still opens the
/// file and reads the tables — the soft delete is a normal Jet delete, not corruption.
/// </summary>
public class DropConstraintAccessTests
{
    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    [Fact]
    public void Access_opens_a_libred_file_after_dropping_a_foreign_key()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "dropc-ace-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("Parent", [new("Id", JetDataType.Int32, 4, IsFixedLength: true)], primaryKey: ["Id"]);
                db.CreateTable("Child",
                    [new("Id", JetDataType.Int32, 4, IsFixedLength: true), new("ParentId", JetDataType.Int32, 4, IsFixedLength: true)],
                    primaryKey: ["Id"],
                    relationships: [new RelationshipSpec("FK_Child_Parent", "Parent", [("ParentId", "Id")], IsEnforced: true, CascadeUpdate: false, CascadeDelete: false)]);

                db.OpenTable("Parent").Insert([1]);
                db.OpenTable("Child").Insert([1, 1]);

                Assert.Single(db.Catalog.ForeignKeysOf("Child"));
                Assert.True(db.DropConstraint("Child", "FK_Child_Parent"));
                Assert.Empty(db.Catalog.ForeignKeysOf("Child"));
            }

            // Access opens the post-drop file and reads both tables without complaint.
            using var conn = OpenOleDb(path);
            using (var c = conn.CreateCommand())
            { c.CommandText = "SELECT COUNT(*) FROM Parent"; Assert.Equal(1, Convert.ToInt32(c.ExecuteScalar())); }
            using (var c = conn.CreateCommand())
            { c.CommandText = "SELECT COUNT(*) FROM Child"; Assert.Equal(1, Convert.ToInt32(c.ExecuteScalar())); }

            // Because LibRed removed the TDEF relationship blocks byte-faithfully, ACE no longer enforces
            // the FK either: a child row pointing at a non-existent parent is accepted.
            using (var c = conn.CreateCommand())
            { c.CommandText = "INSERT INTO Child (Id, ParentId) VALUES (2, 99)"; Assert.Equal(1, c.ExecuteNonQuery()); }
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
