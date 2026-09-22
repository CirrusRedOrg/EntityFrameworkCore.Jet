using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class CreateViewTests
{
    private static string Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "view-");
        return path;
    }

    [Fact]
    public void Create_view_executes()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery("CREATE VIEW `LondonCust` AS SELECT `CustomerID`, `CompanyName` FROM `Customers` WHERE `City` = 'London'");
            e.ExecuteNonQuery("CREATE VIEW `CustOrders` AS SELECT `c`.`CustomerID`, `o`.`OrderID` FROM `Customers` AS `c` INNER JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`");
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // A body with no FROM at all. Access stores and runs one — the rows are an ordinary query's minus the
    // table rows — so this must store, read back and run rather than fail. It used to throw a bare
    // NullReferenceException out of the decomposer.
    [Theory]
    [InlineData("CREATE VIEW `Const` AS SELECT 1 AS `n`")]
    [InlineData("CREATE PROCEDURE `Const` AS SELECT 1 AS `n`")]
    public void A_from_less_body_is_stored_and_queryable(string sql)
    {
        string path = Fresh();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                new QueryEngine(db).ExecuteNonQuery(sql);

            using (var db = JetDatabase.Open(path)) // fresh open: read from the file
            {
                Assert.Equal("SELECT 1 AS [n]", db.Catalog.Views["Const"]);
                Assert.Equal(1, new QueryEngine(db).ExecuteQuery("SELECT `n` FROM `Const`").Rows.Single()[0]);
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // A view is read back from the file (its MSysQueries rows), reconstructed to SQL, and resolved as a
    // derived table when queried through LibRed's own engine.
    [Fact]
    public void View_is_queryable_through_libred()
    {
        string path = Fresh();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var e = new QueryEngine(db);
                e.ExecuteNonQuery("CREATE VIEW `LondonCust` AS SELECT `CustomerID`, `CompanyName` FROM `Customers` WHERE `City` = 'London'");
                e.ExecuteNonQuery("CREATE VIEW `CustOrders` AS SELECT `c`.`CustomerID`, `o`.`OrderID` FROM `Customers` AS `c` INNER JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`");
            }
            using (var db = JetDatabase.Open(path)) // fresh open: the view is read from the file
            {
                var e = new QueryEngine(db);

                var viaView = e.ExecuteQuery("SELECT `CustomerID` FROM `LondonCust`").Rows.Select(r => r[0]).OrderBy(x => x).ToList();
                var viaTable = e.ExecuteQuery("SELECT `CustomerID` FROM `Customers` WHERE `City` = 'London'").Rows.Select(r => r[0]).OrderBy(x => x).ToList();
                Assert.Equal(6, viaView.Count);
                Assert.Equal(viaTable, viaView);

                // A predicate applied on top of the view.
                int filtered = e.ExecuteQuery("SELECT `CustomerID` FROM `LondonCust` WHERE `CustomerID` = 'AROUT'").Rows.Count();
                Assert.Equal(1, filtered);

                // The join view runs and returns the same count as the underlying join.
                int viaJoinView = e.ExecuteQuery("SELECT `CustomerID` FROM `CustOrders`").Rows.Count();
                int viaJoin = e.ExecuteQuery("SELECT `c`.`CustomerID` FROM `Customers` AS `c` INNER JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`").Rows.Count();
                Assert.Equal(830, viaJoinView);
                Assert.Equal(viaJoin, viaJoinView);
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // What is left that a view cannot be decomposed into: Access stores a UNION as segment rows rather than
    // the one query shape these build, and a parenthesised body has no query specification to take apart.
    [Theory]
    [InlineData("CREATE VIEW `V` AS SELECT `CustomerID` FROM `Customers` UNION SELECT `CustomerID` FROM `Customers`", "UNION")]
    [InlineData("CREATE VIEW `V` AS (SELECT `CustomerID` FROM `Customers`)", "parenthesised")]
    public void Non_simple_view_throws(string sql, string expected)
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var ex = Assert.Throws<NotSupportedException>(() => new QueryEngine(db).ExecuteNonQuery(sql));
            Assert.Contains(expected, ex.Message);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void A_group_by_totals_view_round_trips()
    {
        string path = Fresh();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                new QueryEngine(db).ExecuteNonQuery(
                    "CREATE VIEW `Subtotals` AS SELECT `Order Details`.OrderID, " +
                    "Sum(CCur(`Order Details`.UnitPrice*Quantity*(1-Discount)/100)*100) AS Subtotal " +
                    "FROM `Order Details` GROUP BY `Order Details`.OrderID");

            using (var db = JetDatabase.Open(path))
            {
                var e = new QueryEngine(db);
                Assert.Equal(830, e.ExecuteQuery("SELECT * FROM `Subtotals`").Rows.Count()); // one row per order
                Assert.Equal(440.00m, Convert.ToDecimal(
                    e.ExecuteQuery("SELECT Subtotal FROM `Subtotals` WHERE OrderID = 10248").Rows.First()[0]));
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // A totals view whose groups are filtered: the HAVING is stored as its own row beside the GROUP BY ones,
    // and has to come back in the same place. Filtering on the grouping key pins exact values; the aggregate
    // form is checked against the same query written inline, which is the only thing it must agree with.
    [Fact]
    public void A_having_totals_view_round_trips()
    {
        const string subtotal = "Sum(CCur(`Order Details`.UnitPrice*Quantity*(1-Discount)/100)*100)";
        string path = Fresh();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var e = new QueryEngine(db);
                e.ExecuteNonQuery(
                    $"CREATE VIEW `OneSubtotal` AS SELECT `Order Details`.OrderID, {subtotal} AS Subtotal " +
                    "FROM `Order Details` GROUP BY `Order Details`.OrderID HAVING `Order Details`.OrderID = 10248");
                e.ExecuteNonQuery(
                    $"CREATE VIEW `BigSubtotals` AS SELECT `Order Details`.OrderID, {subtotal} AS Subtotal " +
                    $"FROM `Order Details` GROUP BY `Order Details`.OrderID HAVING {subtotal} > 5000");
            }

            using (var db = JetDatabase.Open(path))
            {
                var e = new QueryEngine(db);

                object?[] only = e.ExecuteQuery("SELECT OrderID, Subtotal FROM `OneSubtotal`").Rows.Single();
                Assert.Equal(10248, Convert.ToInt32(only[0]));
                Assert.Equal(440.00m, Convert.ToDecimal(only[1]));

                var viaView = e.ExecuteQuery("SELECT OrderID FROM `BigSubtotals`")
                    .Rows.Select(r => Convert.ToInt32(r[0])).OrderBy(x => x).ToList();
                var inline = e.ExecuteQuery(
                        $"SELECT `Order Details`.OrderID FROM `Order Details` " +
                        $"GROUP BY `Order Details`.OrderID HAVING {subtotal} > 5000")
                    .Rows.Select(r => Convert.ToInt32(r[0])).OrderBy(x => x).ToList();
                Assert.Equal(inline, viaView);

                // The comparison above proves nothing if the HAVING dropped everything or nothing.
                Assert.NotEmpty(viaView);
                Assert.True(viaView.Count < 830, $"HAVING filtered nothing: {viaView.Count} groups.");
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void View_name_colliding_with_an_object_throws()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            // Northwind already has a Customers table.
            Assert.Throws<SchemaObjectExistsException>(() =>
                new QueryEngine(db).ExecuteNonQuery("CREATE VIEW `Customers` AS SELECT `CustomerID` FROM `Customers`"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
