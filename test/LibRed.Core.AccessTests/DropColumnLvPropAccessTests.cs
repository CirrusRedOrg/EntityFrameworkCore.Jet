using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

// DROP COLUMN must remove the dropped column's DefaultValue/Required entries from the table's
// MSysObjects.LvProp blob — verified this is what ACE does. LibRed's drop does the same (surgical removal
// of the column's property block), and ACE still opens/reads the result.
public class DropColumnLvPropAccessTests
{
    private static OleDbConnection Open(string path) => AceTestDatabase.Open(path);
    private static void Ace(string path, params string[] sqls)
    { using var c = Open(path); foreach (var s in sqls) { using var m = c.CreateCommand(); m.CommandText = s; m.ExecuteNonQuery(); } }

    private static (IReadOnlyDictionary<string, string> Defaults, IReadOnlySet<string> Required) Props(string path, string table)
    {
        using var db = JetDatabase.Open(path);
        int tid = db.Catalog.FindTable(table)!.DefinitionPage;
        var mo = db.OpenTable("MSysObjects");
        int idIdx = mo.Definition.FindColumn("Id")!.Index, lvIdx = mo.Definition.FindColumn("LvProp")!.Index;
        byte[] blob = mo.Rows().Where(r => r[idIdx] is not null && Convert.ToInt32(r[idIdx]) == tid)
            .Select(r => r[lvIdx] as byte[] ?? []).FirstOrDefault() ?? [];
        return (PropertyBlob.ReadColumnDefaults(blob), PropertyBlob.ReadRequiredColumns(blob));
    }

    [Fact]
    public void Libred_drop_removes_the_columns_lvprop_entries_and_access_still_reads()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "dclv-");
        try
        {
            // ACE creates the table so the LvProp blob is authentic (B defaulted, N required, K also defaulted).
            Ace(path, "CREATE TABLE T (A LONG, B LONG DEFAULT 5, N TEXT(20) NOT NULL, K LONG DEFAULT 7)");
            var before = Props(path, "T");
            Assert.Equal("5", before.Defaults["B"]);
            Assert.Contains("N", before.Required);

            using (var db = JetDatabase.Open(path, readOnly: false))
                Assert.True(db.DropColumn("T", "B")); // drop the defaulted column via LibRed

            var after = Props(path, "T");
            Assert.False(after.Defaults.ContainsKey("B"));   // B's DefaultValue is gone
            Assert.Equal("7", after.Defaults["K"]);           // K's default preserved
            Assert.Contains("N", after.Required);             // N's Required preserved

            // ACE opens the LibRed-edited file without repair and still reads it.
            using var conn = Open(path);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM T";
            Assert.Equal(0, Convert.ToInt32(cmd.ExecuteScalar()));
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
