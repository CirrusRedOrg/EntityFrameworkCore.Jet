using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

// The DAO Field2.DefaultValue doc says a default expression may not contain SQL aggregate functions and the
// property text maxes at 255 chars. Probing ACE refines both:
//  - Defaults share a RESTRICTED expression service with field validation rules ("... in validation expression
//    or default value ...") whose function whitelist excludes not just SQL aggregates (Sum/Count) but DOMAIN
//    aggregates (DCount) too — all rejected as "Unknown function".
//  - The 255-char cap is a DAO-API limit, NOT an engine/file-format limit: ACE accepts and applies a 300+ char
//    default expression. LibRed writes such defaults to LvProp and they round-trip.
public class AceDefaultExpressionLimitsTests
{
    private static OleDbConnection OpenOleDb(string path)
    {
        Exception? last = null;
        for (int attempt = 0; attempt < 12; attempt++)
            foreach (string p in new[] { "Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0" })
            {
                try { var c = new OleDbConnection($"Provider={p};Data Source={path};OLE DB Services=-4;"); c.Open(); return c; }
                catch (Exception ex) when (ex is OleDbException or InvalidOperationException) { last = ex; Thread.Sleep(40); }
            }
        throw new InvalidOperationException("no provider", last);
    }

    // SQL aggregates (Sum, Count) AND the domain aggregate DCount are all rejected in a default — the default
    // expression evaluator has a restricted function whitelist. Smuggled via LibRed so the expression reaches
    // ACE's evaluator at insert (ACE's DDL parser would reject DCount's syntax earlier for a different reason).
    [Theory]
    [InlineData("Sum(1)")]
    [InlineData("Count(*)")]
    [InlineData("DCount('*','MSysObjects')")]
    public void Access_rejects_an_aggregate_function_in_a_default(string def)
    {
        string path = Path.Combine(Path.GetTempPath(), $"agg-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                db.CreateTable("T",
                    [new ColumnSpec("K", JetDataType.Int32, 4, IsFixedLength: true),
                     new ColumnSpec("V", JetDataType.Int32, 4, IsFixedLength: true)],
                    primaryKey: ["K"], columnDefaults: [("V", def)]);

            using var conn = OpenOleDb(path);
            using var insert = conn.CreateCommand();
            insert.CommandText = "INSERT INTO T (K) VALUES (1)";
            var ex = Assert.ThrowsAny<OleDbException>(() => insert.ExecuteNonQuery());
            Assert.Contains("Unknown function", ex.Message);
            Assert.Contains("default value", ex.Message);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    // The DAO 255-char DefaultValue cap is an API limit, not an engine one: a 300-char string-literal default is
    // accepted and applied by ACE (proplen ~302). A single literal has no operators, isolating length from the
    // separate "Expression too complex" operator-count limit.
    [Fact]
    public void Access_accepts_a_default_expression_longer_than_255_chars()
    {
        string path = Path.Combine(Path.GetTempPath(), $"len-{Guid.NewGuid():N}.accdb");
        File.Copy(TestDatabases.NorthwindAccdb, path);
        try
        {
            using var conn = OpenOleDb(path);
            using (var c = conn.CreateCommand()) { c.CommandText = "CREATE TABLE T ( K LONG PRIMARY KEY, V MEMO DEFAULT \"" + new string('a', 300) + "\" )"; c.ExecuteNonQuery(); }
            using (var c = conn.CreateCommand()) { c.CommandText = "INSERT INTO T (K) VALUES (1)"; c.ExecuteNonQuery(); }
            object? v; using (var c = conn.CreateCommand()) { c.CommandText = "SELECT V FROM T"; v = c.ExecuteScalar(); }
            Assert.Equal(300, (v as string)?.Length);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
