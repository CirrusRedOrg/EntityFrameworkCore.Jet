using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// Each connection parses the catalog once and caches it, so DDL committed by one connection has to reach the
// others somehow: PageChannel keeps a per-file schema generation that a schema-changing commit advances, and
// JetCatalog re-reads when the generation it last saw has moved on. Without that a second connection keeps
// serving a catalog from before the CREATE and reports the table as missing.
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

            // Make the second connection cache a catalog that predates the new table.
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
            Assert.Contains("Doomed", secondDb.Catalog.Tables.Select(t => t.Name));   // caches it

            first.ExecuteNonQuery("DROP TABLE Doomed");

            Assert.DoesNotContain("Doomed", secondDb.Catalog.Tables.Select(t => t.Name));
            Assert.ThrowsAny<Exception>(() => second.ExecuteQuery("SELECT Id FROM Doomed"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The counterpart guard: plain DML must not invalidate anyone's catalog, or every INSERT would cost every
    // other connection a full re-parse of MSysObjects.
    [Fact]
    public void Ordinary_dml_on_one_connection_does_not_invalidate_another_catalog()
    {
        string path = Fresh("schema-dml-");
        try
        {
            using var firstDb = JetDatabase.Open(path, readOnly: false);
            using var secondDb = JetDatabase.Open(path, readOnly: false);
            var first = new QueryEngine(firstDb);

            first.ExecuteNonQuery("CREATE TABLE Rows1 (Id LONG PRIMARY KEY)");
            var cached = secondDb.Catalog.Tables.Single(t => t.Name == "Rows1");

            first.ExecuteNonQuery("INSERT INTO Rows1 (Id) VALUES (1)");

            // Same TableDef instance: the DML did not force the second connection to re-read the catalog.
            Assert.Same(cached, secondDb.Catalog.Tables.Single(t => t.Name == "Rows1"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static string Fresh(string prefix) =>
        TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), prefix);
}
