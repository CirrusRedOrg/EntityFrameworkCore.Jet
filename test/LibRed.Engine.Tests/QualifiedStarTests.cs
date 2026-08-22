using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// A table-qualified star, <c>Table.*</c>, in a projection (e.g. Northwind's "Alphabetical list of
/// products": <c>SELECT Products.*, Categories.CategoryName FROM …</c>) expands to that source's columns.
/// This checks the direct query, a LibRed-created view over it, and reading back Access's own such view.
/// </summary>
public class QualifiedStarTests
{
    private const string Query =
        "SELECT Products.*, Categories.CategoryName FROM Categories " +
        "INNER JOIN Products ON Categories.CategoryID = Products.CategoryID WHERE Products.Discontinued = 0";

    private static string Fresh()
    {
        string p = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "qstar-");
        return p;
    }

    [Fact]
    public void Qualified_star_expands_to_the_sources_columns()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path);
            var rs = new QueryEngine(db).ExecuteQuery(Query);

            // Products' 10 columns, then Categories.CategoryName.
            Assert.Equal(11, rs.ColumnNames.Count);
            Assert.Equal("ProductID", rs.ColumnNames[0]);
            Assert.Equal("CategoryName", rs.ColumnNames[^1]);
            Assert.Equal(69, rs.Rows.Count()); // the not-discontinued products
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void A_qualified_star_view_round_trips_and_matches_access_own_view()
    {
        string path = Fresh();
        try
        {
            // Northwind already ships "Alphabetical list of products" (an Access-created qualified-star view);
            // reading it back through LibRed must resolve the Table.* the same as running the query directly.
            using (var db = JetDatabase.Open(path))
            {
                var e = new QueryEngine(db);
                int direct = e.ExecuteQuery(Query).Rows.Count();
                var view = e.ExecuteQuery("SELECT * FROM `Alphabetical list of products`");
                Assert.Equal(11, view.ColumnNames.Count);
                Assert.Equal(direct, view.Rows.Count());
            }

            // And a LibRed-created qualified-star view round-trips the same.
            using (var db = JetDatabase.Open(path, readOnly: false))
                new QueryEngine(db).ExecuteNonQuery("CREATE VIEW `MyAlph` AS " + Query.Replace("Products.Discontinued = 0", "(((Products.Discontinued)=0))"));
            using (var db = JetDatabase.Open(path))
            {
                var rs = new QueryEngine(db).ExecuteQuery("SELECT * FROM `MyAlph`");
                Assert.Equal(11, rs.ColumnNames.Count);
                Assert.Equal(69, rs.Rows.Count());
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
