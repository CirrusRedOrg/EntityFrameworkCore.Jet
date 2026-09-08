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
    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    // SQL aggregates (Sum, Count) AND the domain aggregate DCount are all rejected in a default — the default
    // expression evaluator has a restricted function whitelist. Smuggled via LibRed so the expression reaches
    // ACE's evaluator at insert (ACE's DDL parser would reject DCount's syntax earlier for a different reason).
    [Theory]
    [InlineData("Sum(1)")]
    [InlineData("Count(*)")]
    [InlineData("DCount('*','MSysObjects')")]
    public void Access_rejects_an_aggregate_function_in_a_default(string def)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "agg-");
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
        finally { TemporaryDatabase.Delete(path); }
    }

    // The DAO 255-char DefaultValue cap is an API limit, not an engine one: a 300-char string-literal default is
    // accepted and applied by ACE (proplen ~302). A single literal has no operators, isolating length from the
    // separate "Expression too complex" operator-count limit.
    [Fact]
    public void Access_accepts_a_default_expression_longer_than_255_chars()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "len-");
        try
        {
            using var conn = OpenOleDb(path);
            using (var c = conn.CreateCommand()) { c.CommandText = "CREATE TABLE T ( K LONG PRIMARY KEY, V MEMO DEFAULT \"" + new string('a', 300) + "\" )"; c.ExecuteNonQuery(); }
            using (var c = conn.CreateCommand()) { c.CommandText = "INSERT INTO T (K) VALUES (1)"; c.ExecuteNonQuery(); }
            object? v; using (var c = conn.CreateCommand()) { c.CommandText = "SELECT V FROM T"; v = c.ExecuteScalar(); }
            Assert.Equal(300, (v as string)?.Length);
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
