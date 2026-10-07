using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// DDL committed by one connection must be visible to another connection open on the same file: a table it
// creates can be found and read, and a table it drops stops being found.
public class SchemaVisibilityTests
{
    [Fact]
    public void A_table_created_on_one_connection_is_visible_to_another()
    {
        string path = Fresh("schema-create-");
        try
        {
            using var firstDb = JetDatabase.Open(path, readOnly: false);
            using var secondDb = JetDatabase.Open(path, readOnly: false);
            var first = new QueryEngine(firstDb);
            var second = new QueryEngine(secondDb);

            // The second connection has already read the catalog before the new table exists.
            Assert.NotEmpty(second.ExecuteQuery("SELECT CustomerID FROM Customers").Rows);
            Assert.DoesNotContain("Later", secondDb.Catalog.Tables.Select(t => t.Name));

            first.ExecuteNonQuery("CREATE TABLE Later (Id LONG PRIMARY KEY, V TEXT(10))");
            first.ExecuteNonQuery("INSERT INTO Later (Id, V) VALUES (1, 'a')");

            Assert.Contains("Later", secondDb.Catalog.Tables.Select(t => t.Name));
            Assert.Equal("a", second.ExecuteQuery("SELECT V FROM Later").Rows.Single()[0]);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void A_table_dropped_on_one_connection_stops_being_visible_to_another()
    {
        string path = Fresh("schema-drop-");
        try
        {
            using var firstDb = JetDatabase.Open(path, readOnly: false);
            using var secondDb = JetDatabase.Open(path, readOnly: false);
            var first = new QueryEngine(firstDb);
            var second = new QueryEngine(secondDb);

            first.ExecuteNonQuery("CREATE TABLE Doomed (Id LONG PRIMARY KEY)");
            Assert.Contains("Doomed", secondDb.Catalog.Tables.Select(t => t.Name));

            first.ExecuteNonQuery("DROP TABLE Doomed");

            Assert.DoesNotContain("Doomed", secondDb.Catalog.Tables.Select(t => t.Name));
            Assert.ThrowsAny<Exception>(() => second.ExecuteQuery("SELECT Id FROM Doomed"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static string Fresh(string prefix) =>
        TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), prefix);
}