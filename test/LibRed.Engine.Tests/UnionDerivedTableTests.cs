using LibRed;
using LibRed.Engine;
using LibRed.Sql.Parsing;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// A parenthesized derived table in FROM may itself be a set operation (UNION), not just a single
/// SELECT — e.g. Northwind's "Customer and Suppliers by City" view. The grammar/planner handle this
/// as a query. (Storing such a shape as a *view* is a separate, not-yet-supported feature — asserted
/// here only to show it now fails with a clear limitation rather than a parse crash.)
/// </summary>
public class UnionDerivedTableTests
{
    private static string Fresh()
    {
        string p = Path.Combine(Path.GetTempPath(), $"union-derived-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), p);
        return p;
    }

    [Fact]
    public void Union_in_a_derived_table_is_queryable()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path);
            var rs = new QueryEngine(db).ExecuteQuery(
                "SELECT u.City FROM (SELECT City FROM Customers UNION SELECT City FROM Suppliers) AS u");
            Assert.True(rs.Rows.Count() > 0);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void Create_view_over_a_union_derived_table_parses_but_is_not_stored_yet()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var ex = Record.Exception(() => new QueryEngine(db).ExecuteNonQuery(
                "CREATE VIEW `CSbyCity` AS SELECT u.City FROM " +
                "(SELECT City FROM Customers UNION SELECT City FROM Suppliers) AS u"));
            // The grammar bug is gone (no SqlParseException); the remaining gap is view *storage*.
            Assert.IsType<NotSupportedException>(ex);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
