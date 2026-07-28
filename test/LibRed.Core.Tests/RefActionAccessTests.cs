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
    private static OleDbConnection OpenOleDb(string path)
    {
        foreach (string p in new[] { "Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0" })
        {
            try { var c = new OleDbConnection($"Provider={p};Data Source={path};OLE DB Services=-4;"); c.Open(); return c; }
            catch (Exception ex) when (ex is OleDbException or InvalidOperationException) { }
        }
        throw new InvalidOperationException("No provider");
    }

    [Fact]
    public void Access_applies_a_libred_written_on_delete_set_null()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ri-ace-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
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
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
