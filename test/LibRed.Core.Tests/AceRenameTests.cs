using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

// Byte-faithful renames: ACE — not just LibRed — has to accept a file whose table/column names LibRed rewrote.
// This matters because a column rename relays out the TDEF's variable-length name pool and re-owns the column's
// LvProp property block; a byte-level mistake there is invisible to LibRed's own read-back (it would read its
// own malformed bytes back consistently) but fatal to Access. So the checks below are deliberately made
// *through ACE*: it resolves the new names, honours the carried-over DEFAULT, and still enforces the
// relationship whose by-name references were repointed.
public class AceRenameTests
{
    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    private static void Exec(OleDbConnection conn, string sql)
    {
        using var c = conn.CreateCommand();
        c.CommandText = sql;
        c.ExecuteNonQuery();
    }

    private static object? Scalar(OleDbConnection conn, string sql)
    {
        using var c = conn.CreateCommand();
        c.CommandText = sql;
        return c.ExecuteScalar();
    }

    [Fact]
    public void Access_reads_a_libred_renamed_table_and_column_and_still_applies_the_default()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "acerename-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("Doc",
                    [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("Title", JetDataType.Text, 100, IsFixedLength: false)],
                    primaryKey: ["Id"],
                    columnDefaults: [("Title", "'untitled'")]);
                db.CreateIndex("Doc", "IX_Doc_Title", [("Title", false)]);

                // A longer table name and a shorter column name, so both directions of the variable-length
                // name relayout are exercised.
                db.RenameTable("Doc", "DocumentArchive");
                db.RenameColumn("DocumentArchive", "Title", "Head");
            }

            using var conn = OpenOleDb(path);

            // ACE resolves both new names and round-trips a value through them.
            Exec(conn, "INSERT INTO DocumentArchive (Id, Head) VALUES (1, 'hello')");
            Assert.Equal("hello", Scalar(conn, "SELECT Head FROM DocumentArchive WHERE Id = 1"));

            // The DEFAULT survived in a form ACE honours — proof the LvProp owner rewrite is byte-correct,
            // not merely self-consistent.
            Exec(conn, "INSERT INTO DocumentArchive (Id) VALUES (2)");
            Assert.Equal("untitled", Scalar(conn, "SELECT Head FROM DocumentArchive WHERE Id = 2"));

            // The old column name is genuinely gone as far as ACE is concerned.
            using var stale = conn.CreateCommand();
            stale.CommandText = "SELECT Title FROM DocumentArchive";
            Assert.ThrowsAny<OleDbException>(() => stale.ExecuteScalar());
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Access_still_enforces_a_relationship_after_libred_renames_both_sides()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "acerenamefk-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                db.CreateTable("P",
                    [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true)],
                    primaryKey: ["Id"]);
                db.CreateTable("C",
                    [new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("PId", JetDataType.Int32, 4, IsFixedLength: true)],
                    primaryKey: ["Id"],
                    relationships: [new RelationshipSpec(
                        "FK_C_P", "P", [("PId", "Id")],
                        IsEnforced: true, CascadeUpdate: false, CascadeDelete: false)]);

                // Rename the parent table, the referenced key column, and the child's FK column — every
                // by-name reference in MSysRelationships moves.
                db.RenameTable("P", "ParentTable");
                db.RenameColumn("ParentTable", "Id", "ParentKey");
                db.RenameColumn("C", "PId", "OwnerId");
            }

            using var conn = OpenOleDb(path);

            Exec(conn, "INSERT INTO ParentTable (ParentKey) VALUES (1)");

            // ACE still enforces referential integrity through the renamed names: the orphan is rejected…
            using var orphan = conn.CreateCommand();
            orphan.CommandText = "INSERT INTO C (Id, OwnerId) VALUES (1, 99)";
            Assert.ThrowsAny<OleDbException>(() => orphan.ExecuteNonQuery());

            // …and the valid child is accepted.
            Exec(conn, "INSERT INTO C (Id, OwnerId) VALUES (1, 1)");
            Assert.Equal(1, Scalar(conn, "SELECT OwnerId FROM C WHERE Id = 1"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
