using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// SELECT DISTINCT, a BETWEEN predicate, and <c>#…#</c> date literals — the features in Northwind's
/// "Quarterly Orders" view (<c>SELECT DISTINCT … FROM Customers RIGHT JOIN Orders … WHERE
/// Orders.OrderDate BETWEEN #1/1/1997# And #12/31/1997#</c>).
/// </summary>
public class DistinctBetweenDateTests
{
    private const string Query =
        "SELECT DISTINCT Customers.CustomerID, Customers.CompanyName, Customers.City, Customers.Country " +
        "FROM Customers RIGHT JOIN Orders ON Customers.CustomerID = Orders.CustomerID " +
        "WHERE Orders.OrderDate BETWEEN #1/1/1997# And #12/31/1997#";

    private static string Fresh()
    {
        string p = Path.Combine(Path.GetTempPath(), $"qorders-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), p);
        return p;
    }

    [Fact]
    public void Distinct_between_and_date_literals_execute()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path);
            var rs = new QueryEngine(db).ExecuteQuery(Query);
            Assert.Equal(4, rs.ColumnNames.Count);
            Assert.Equal(86, rs.Rows.Count()); // distinct customers with a 1997 order
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void Distinct_dedupes_rows()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path);
            var e = new QueryEngine(db);
            long all = Convert.ToInt64(e.ExecuteQuery("SELECT Country FROM Customers").Rows.Count());
            long distinct = Convert.ToInt64(e.ExecuteQuery("SELECT DISTINCT Country FROM Customers").Rows.Count());
            Assert.True(distinct < all);   // Customers has many rows but few distinct countries
            Assert.Equal(21, distinct);    // Northwind customer countries
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void Access_own_quarterly_orders_view_reads_back()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path);
            var e = new QueryEngine(db);
            int direct = e.ExecuteQuery(Query).Rows.Count();
            var view = e.ExecuteQuery("SELECT * FROM `Quarterly Orders`");
            Assert.Equal(direct, view.Rows.Count());
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
