using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class AliaslessDerivedTableTests
{
    private static string Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"aliasless-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        return path;
    }

    // Access permits a derived table with no alias; EF emits one for `.Any()`:
    // SELECT EXISTS (SELECT 1 FROM Customers) FROM (SELECT COUNT(*) FROM <dual>)
    [Fact]
    public void Aliasless_derived_table_in_from()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path);
            var e = new QueryEngine(db);
            var rows = e.ExecuteQuery(
                "SELECT EXISTS (SELECT 1 FROM Customers) AS c FROM (SELECT COUNT(*) FROM Orders)").Rows.ToList();
            var only = Assert.Single(rows);
            Assert.Equal(true, only[0]); // Customers is non-empty → EXISTS true
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void Aliasless_derived_table_projecting_its_column()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path);
            // The unaliased derived table's column is reachable unqualified.
            var rows = new QueryEngine(db).ExecuteQuery(
                "SELECT n FROM (SELECT COUNT(*) AS n FROM Orders)").Rows.ToList();
            Assert.Equal(830, Assert.Single(rows)[0]);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
