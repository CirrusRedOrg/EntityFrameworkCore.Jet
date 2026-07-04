using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// An outer-query aggregate referenced inside a correlated subquery (e.g. Northwind's
// GroupBy_with_aggregate_containing_complex_where). MAX(o.OrderID) is the outer group's max; it must be
// precomputed per group and reachable from the subquery's scope.
public class CorrelatedOuterAggregateTests
{
    private static string Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"corragg-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        return path;
    }

    [Fact]
    public void Subquery_references_outer_group_aggregate()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path);
            var e = new QueryEngine(db);
            // Per employee: the group's MAX(OrderID), and a subquery that finds the max OrderID that is
            // <= the outer group's max (which is just the group's own max again) — exercises an outer
            // aggregate inside a correlated subquery predicate.
            var rows = e.ExecuteQuery(
                "SELECT o.EmployeeID AS K, " +
                "(SELECT MAX(o0.OrderID) FROM Orders AS o0 " +
                " WHERE o0.EmployeeID = o.EmployeeID AND o0.OrderID <= MAX(o.OrderID)) AS M " +
                "FROM Orders AS o GROUP BY o.EmployeeID").Rows.ToList();

            // Ground truth: the plain per-employee MAX(OrderID).
            var expected = e.ExecuteQuery("SELECT EmployeeID, MAX(OrderID) FROM Orders GROUP BY EmployeeID")
                .Rows.ToDictionary(r => Convert.ToInt32(r[0]), r => Convert.ToInt32(r[1]));

            Assert.Equal(expected.Count, rows.Count);
            foreach (var r in rows)
                Assert.Equal(expected[Convert.ToInt32(r[0])], Convert.ToInt32(r[1]));
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    // The exact Northwind shape: MAX(o.OrderID) inside the subquery WHERE, wrapped in IIF/CLNG.
    [Fact]
    public void Northwind_complex_where_shape_parses_and_runs()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path);
            var rows = new QueryEngine(db).ExecuteQuery(
                "SELECT o.EmployeeID AS `Key`, (" +
                "  SELECT MAX(o0.OrderID) FROM Orders AS o0 " +
                "  WHERE IIF(o0.EmployeeID IS NULL, NULL, CLNG(o0.EmployeeID)) = " +
                "        IIF((MAX(o.OrderID) * 6) IS NULL, NULL, CLNG(MAX(o.OrderID) * 6)) " +
                "     OR (o0.EmployeeID IS NULL AND MAX(o.OrderID) IS NULL)) AS `Max` " +
                "FROM Orders AS o GROUP BY o.EmployeeID").Rows.ToList();
            Assert.NotEmpty(rows); // it runs without "Function MAX is not supported"
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
