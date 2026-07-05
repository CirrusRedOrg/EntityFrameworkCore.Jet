using System.Data.OleDb;
using LibRed;
using Xunit;

namespace LibRed.Core.Tests;

// ACE's ALTER TABLE DROP COLUMN is a metadata-only TDEF edit: it removes the column descriptor + name and
// decrements the counts, but does NOT renumber the surviving columns or rewrite existing rows. Survivors
// keep their original column ids (a gap appears) and their original variable-table index. So a correct
// reader must read the STORED variable index (descriptor 0x07), not derive it by ranking column ids —
// otherwise a survivor after a dropped variable column decodes the wrong slot.
public class DropColumnAccessTests
{
    private static OleDbConnection OpenOleDb(string path)
    {
        foreach (string p in new[] { "Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0" })
        {
            try { var c = new OleDbConnection($"Provider={p};Data Source={path};OLE DB Services=-4;"); c.Open(); return c; }
            catch (Exception ex) when (ex is OleDbException or InvalidOperationException) { }
        }
        throw new InvalidOperationException("No ACE provider");
    }

    private static void Ace(string path, params string[] sqls)
    {
        using var c = OpenOleDb(path);
        foreach (string sql in sqls) { using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
    }

    private static string[][] ReadT(string path)
    {
        using var db = JetDatabase.Open(path);
        return db.OpenTable("T").Rows().Select(r => r.Select(v => v?.ToString() ?? "<null>").ToArray()).ToArray();
    }

    private static string[] ColsOfT(string path)
    {
        using var db = JetDatabase.Open(path);
        return db.Catalog.FindTable("T")!.Columns.Select(c => c.Name).ToArray();
    }

    [Fact]
    public void Reads_rows_correctly_after_ace_drops_a_column()
    {
        string path = Path.Combine(Path.GetTempPath(), $"dropcol-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            Ace(path,
                "CREATE TABLE T (A LONG, B TEXT(20), C LONG, D TEXT(20))",
                "INSERT INTO T (A, B, C, D) VALUES (1, 'bee', 10, 'dee')",
                "INSERT INTO T (A, B, C, D) VALUES (2, 'buzz', 20, 'doo')");

            Ace(path, "ALTER TABLE T DROP COLUMN B"); // variable column in the middle
            Assert.Equal(["A", "C", "D"], ColsOfT(path));
            Assert.Equal([["1", "10", "dee"], ["2", "20", "doo"]], ReadT(path)); // D still decodes to "dee"

            Ace(path, "ALTER TABLE T DROP COLUMN C"); // fixed column
            Assert.Equal(["A", "D"], ColsOfT(path));
            Assert.Equal([["1", "dee"], ["2", "doo"]], ReadT(path));
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
