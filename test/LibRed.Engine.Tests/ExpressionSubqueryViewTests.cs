using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// A view referenced inside a scalar or EXISTS subquery (not just a FROM clause) is expanded, so the
/// query resolves the view the same as its base-table equivalent. Before this, only FROM-clause view
/// references were rewritten and a view named inside an expression subquery failed to resolve.
/// </summary>
public class ExpressionSubqueryViewTests
{
    private static string Fresh()
    {
        string p = Path.Combine(Path.GetTempPath(), $"expr-view-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), p);
        return p;
    }

    private static long Scalar(QueryEngine e, string sql) =>
        Convert.ToInt64(e.ExecuteQuery(sql).Rows.First()[0]);

    [Fact]
    public void View_inside_exists_and_scalar_subqueries_is_expanded()
    {
        string path = Fresh();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                new QueryEngine(db).ExecuteNonQuery(
                    "CREATE VIEW `LondonCust` AS SELECT CustomerID FROM Customers WHERE City = 'London'");

            using (var db = JetDatabase.Open(path))
            {
                var e = new QueryEngine(db);

                // EXISTS over the view === EXISTS over the base table.
                Assert.Equal(
                    Scalar(e, "SELECT COUNT(*) FROM Customers c WHERE EXISTS " +
                              "(SELECT 1 FROM Customers v WHERE v.City = 'London' AND v.CustomerID = c.CustomerID)"),
                    Scalar(e, "SELECT COUNT(*) FROM Customers c WHERE EXISTS " +
                              "(SELECT 1 FROM `LondonCust` v WHERE v.CustomerID = c.CustomerID)"));

                // Scalar subquery over the view === over the base table.
                Assert.Equal(
                    Scalar(e, "SELECT COUNT(*) FROM Shippers WHERE (SELECT COUNT(*) FROM Customers WHERE City = 'London') > 0"),
                    Scalar(e, "SELECT COUNT(*) FROM Shippers WHERE (SELECT COUNT(*) FROM `LondonCust`) > 0"));
            }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
