using System.Data.OleDb;
using Xunit;

namespace LibRed.Core.Tests;

// Ground truth for LibRed's composite-FK enforcement: ACE applies MATCH FULL — a partial null in a composite
// foreign key (one column null, the other not) is rejected, only all-null or a fully-matching key is allowed.
// (SQL Server's MATCH SIMPLE would skip the check when any column is null; ACE does not.)
public class CompositeFkNullAccessTests
{
    private static OleDbConnection Open(string path)
    {
        foreach (string p in new[] { "Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0" })
            try { var c = new OleDbConnection($"Provider={p};Data Source={path};OLE DB Services=-4;"); c.Open(); return c; }
            catch (Exception ex) when (ex is OleDbException or InvalidOperationException) { }
        throw new InvalidOperationException("no ace");
    }
    private static void Ok(OleDbConnection c, string sql) { using var m = c.CreateCommand(); m.CommandText = sql; m.ExecuteNonQuery(); }
    private static void Rejects(OleDbConnection c, string sql) =>
        Assert.ThrowsAny<OleDbException>(() => { using var m = c.CreateCommand(); m.CommandText = sql; m.ExecuteNonQuery(); });

    [Fact]
    public void Access_applies_match_full_to_a_composite_foreign_key()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cfk-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using var c = Open(path);
            Ok(c, "CREATE TABLE P (A LONG, B LONG, CONSTRAINT PK_P PRIMARY KEY (A, B))");
            Ok(c, "CREATE TABLE C (Id LONG PRIMARY KEY, X LONG, Y LONG, " +
                  "CONSTRAINT FK_C FOREIGN KEY (X, Y) REFERENCES P (A, B))");
            Ok(c, "INSERT INTO P (A, B) VALUES (1, 2)");

            Ok(c, "INSERT INTO C (Id, X, Y) VALUES (1, 1, 2)");        // full match → allowed
            Ok(c, "INSERT INTO C (Id, X, Y) VALUES (2, NULL, NULL)");  // all null → allowed
            Rejects(c, "INSERT INTO C (Id, X, Y) VALUES (3, 1, NULL)"); // partial null → rejected (MATCH FULL)
            Rejects(c, "INSERT INTO C (Id, X, Y) VALUES (4, NULL, 2)"); // partial null → rejected
            Rejects(c, "INSERT INTO C (Id, X, Y) VALUES (5, 1, 99)");   // no matching parent → rejected
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
