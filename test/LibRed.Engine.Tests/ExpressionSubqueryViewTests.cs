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
        string p = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "expr-view-");
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
        finally { TemporaryDatabase.Delete(path); }
    }

    /// <summary>What an INSERT, UPDATE or DELETE reads — an INSERT's source, a derived table in it, a VALUES subquery,
    /// a WHERE's subquery — goes through a view as a SELECT's does, stored as a procedure too.</summary>
    [Fact]
    public void View_read_by_an_insert_update_or_delete_is_expanded()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery("CREATE VIEW `LondonCust` AS SELECT CustomerID FROM Customers WHERE City = 'London'");
            e.ExecuteNonQuery("CREATE TABLE CopyOf (CustomerID TEXT(5))");
            long london = Scalar(e, "SELECT COUNT(*) FROM Customers WHERE City = 'London'");

            Assert.Equal(london, e.ExecuteNonQuery("INSERT INTO CopyOf (CustomerID) SELECT CustomerID FROM `LondonCust`"));
            Assert.Equal(2 * london, e.ExecuteNonQuery("INSERT INTO CopyOf (CustomerID) SELECT q.CustomerID FROM " +
                "(SELECT CustomerID FROM `LondonCust` UNION ALL SELECT CustomerID FROM `LondonCust`) AS q"));
            Assert.Equal(1, e.ExecuteNonQuery("INSERT INTO CopyOf (CustomerID) VALUES ((SELECT MIN(CustomerID) FROM `LondonCust`))"));
            e.ExecuteNonQuery("CREATE PROCEDURE CopyLondon AS INSERT INTO CopyOf (CustomerID) SELECT CustomerID FROM `LondonCust`");
            Assert.Equal(london, e.ExecuteNonQuery("EXECUTE CopyLondon"));

            long copied = 4 * london + 1;
            Assert.Equal(copied, e.ExecuteNonQuery("UPDATE CopyOf SET CustomerID = 'X' WHERE CustomerID IN (SELECT CustomerID FROM `LondonCust`)"));
            Assert.Equal(copied, e.ExecuteNonQuery(
                "DELETE FROM CopyOf WHERE NOT EXISTS (SELECT 1 FROM `LondonCust` WHERE `LondonCust`.CustomerID = CopyOf.CustomerID)"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
