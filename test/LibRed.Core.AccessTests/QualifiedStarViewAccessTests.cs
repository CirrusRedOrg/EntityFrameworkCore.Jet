using System.Data.OleDb;
using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// Access runs a LibRed-created view whose projection uses a table-qualified star (<c>Products.*</c>),
/// stored as the column's verbatim Expression — the shape of Northwind's "Alphabetical list of products".
/// </summary>
public class QualifiedStarViewAccessTests
{
    private static OleDbConnection OpenOleDb(string path) => AceTestDatabase.Open(path);

    [Fact]
    public void Access_runs_a_qualified_star_view()
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "qstar-view-");
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                db.CreateView("MyAlph", new ViewSpec(
                    Distinct: false,
                    Columns: [new ViewColumnSpec("Products.*", null), new ViewColumnSpec("Categories.CategoryName", null)],
                    Tables: [new ViewTableSpec("Categories", null), new ViewTableSpec("Products", null)],
                    Joins: [new ViewJoinSpec(ViewJoinType.Inner, "Categories.CategoryID = Products.CategoryID", "Categories", "Products")],
                    Where: "(((Products.Discontinued)=0))"));

            using var conn = OpenOleDb(path);
            using var count = conn.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM MyAlph";
            Assert.Equal(69, Convert.ToInt32(count.ExecuteScalar())); // the not-discontinued products

            using var star = conn.CreateCommand();
            star.CommandText = "SELECT ProductName, CategoryName FROM MyAlph WHERE ProductID = 1";
            using var reader = star.ExecuteReader();
            Assert.True(reader.Read());
            Assert.False(reader.IsDBNull(0)); // Products.* projected ProductName
            Assert.False(reader.IsDBNull(1)); // and the joined CategoryName
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
