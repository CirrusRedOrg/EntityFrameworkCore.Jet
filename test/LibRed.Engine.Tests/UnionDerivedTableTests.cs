using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// A parenthesized derived table in FROM may be a set operation (UNION), not just a single SELECT —
/// e.g. Northwind's "Customer and Suppliers by City" view. The grammar/planner handle it as a query,
/// and a CREATE VIEW over it round-trips: it is stored, reconstructed from MSysQueries, and executed.
/// </summary>
public class UnionDerivedTableTests
{
    private const string ViewSql =
        "CREATE VIEW `CSbyCity` AS SELECT u.City, u.CompanyName, u.ContactName, u.Relationship FROM " +
        "(SELECT City, CompanyName, ContactName, 'Customers' AS Relationship FROM Customers " +
        "UNION SELECT City, CompanyName, ContactName, 'Suppliers' FROM Suppliers) AS u";

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
    public void Create_view_over_a_union_derived_table_round_trips()
    {
        string path = Fresh();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                new QueryEngine(db).ExecuteNonQuery(ViewSql);

            // Reopen so the view is read back from MSysQueries (reconstructed) and expanded on query.
            using (var db = JetDatabase.Open(path))
            {
                int viaView = new QueryEngine(db)
                    .ExecuteQuery("SELECT City, Relationship FROM `CSbyCity`").Rows.Count();
                int viaBase = new QueryEngine(db).ExecuteQuery(
                    "SELECT u.City FROM (SELECT City, CompanyName, ContactName, 'Customers' AS Relationship FROM Customers " +
                    "UNION SELECT City, CompanyName, ContactName, 'Suppliers' FROM Suppliers) AS u").Rows.Count();
                Assert.True(viaView > 0);
                Assert.Equal(viaBase, viaView);
            }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
