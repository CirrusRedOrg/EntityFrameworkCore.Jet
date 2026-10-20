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

    [Fact]
    public void Repeated_sql_uses_current_parameters_and_schema()
    {
        string path = Fresh("schema-repeat-");
        try
        {
            using var writerDb = JetDatabase.Open(path, readOnly: false);
            using var readerDb = JetDatabase.Open(path, readOnly: false);
            var writer = new QueryEngine(writerDb);
            var reader = new QueryEngine(readerDb);
            const string sql = "SELECT Id FROM Later WHERE Id = @id";

            writer.ExecuteNonQuery("CREATE TABLE Later (Id LONG PRIMARY KEY)");
            writer.ExecuteNonQuery("INSERT INTO Later (Id) VALUES (1)");
            Assert.Equal(1, reader.ExecuteQuery(sql, new Dictionary<string, object?> { ["id"] = 1 }).Rows.Single()[0]);

            writer.ExecuteNonQuery("INSERT INTO Later (Id) VALUES (2)");
            Assert.Equal(2, reader.ExecuteQuery(sql, new Dictionary<string, object?> { ["id"] = 2 }).Rows.Single()[0]);

            writer.ExecuteNonQuery("DROP TABLE Later");
            Assert.ThrowsAny<Exception>(() => reader.ExecuteQuery(sql, new Dictionary<string, object?> { ["id"] = 1 }));

            writer.ExecuteNonQuery("CREATE TABLE Later (Id TEXT(20) PRIMARY KEY)");
            writer.ExecuteNonQuery("INSERT INTO Later (Id) VALUES ('changed')");
            var result = reader.ExecuteQuery(sql, new Dictionary<string, object?> { ["id"] = "changed" });
            Assert.Equal(typeof(string), result.ColumnTypes.Single());
            Assert.Equal("changed", result.Rows.Single()[0]);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Table_metadata_follows_other_handle_writes_and_local_rollback()
    {
        string path = Fresh("schema-data-");
        try
        {
            using var writerDb = JetDatabase.Open(path, readOnly: false);
            using var readerDb = JetDatabase.Open(path, readOnly: false);
            var writer = new QueryEngine(writerDb);
            writer.ExecuteNonQuery("CREATE TABLE CounterShape (Id COUNTER PRIMARY KEY, V LONG DEFAULT 7)");
            var before = readerDb.Catalog.FindTable("CounterShape")!;
            Assert.Equal(0, before.RowCount);
            Assert.Equal(1, before.RequireColumn("Id").Seed);
            Assert.Equal("7", before.RequireColumn("V").DefaultValue);

            writer.ExecuteNonQuery("INSERT INTO CounterShape (V) VALUES (1)");
            var after = readerDb.Catalog.FindTable("CounterShape")!;
            Assert.Equal(1, after.RowCount);
            Assert.Equal(2, after.RequireColumn("Id").Seed);
            Assert.Equal("7", after.RequireColumn("V").DefaultValue);
            Assert.Equal(1, after.Indexes.Single().UniqueEntryCount);
            Assert.Equal(0, before.Indexes.Single().UniqueEntryCount);
            Assert.Same(after.RequireColumn("Id"), after.Indexes.Single().Columns.Single().Column);
            Assert.NotSame(before.RequireColumn("V"), after.RequireColumn("V"));
            Assert.Equal(0, before.RowCount);
            Assert.Equal(1, before.RequireColumn("Id").Seed);

            writerDb.BeginTransaction();
            writer.ExecuteNonQuery("INSERT INTO CounterShape (V) VALUES (2)");
            Assert.Equal(2, writerDb.Catalog.FindTable("CounterShape")!.RowCount);
            Assert.Equal(1, readerDb.Catalog.FindTable("CounterShape")!.RowCount);
            writerDb.Rollback();
            Assert.Equal(1, writerDb.Catalog.FindTable("CounterShape")!.RowCount);
            Assert.Equal(1, readerDb.Catalog.FindTable("CounterShape")!.RowCount);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void A_catalog_row_write_refreshes_a_kept_definition_without_a_ddl_notification()
    {
        string path = Fresh("schema-row-");
        try
        {
            using var writer = JetDatabase.Open(path, readOnly: false);
            using var reader = JetDatabase.Open(path, readOnly: false);
            var before = reader.Catalog.FindTable("Orders")!;
            Assert.False(before.IsSystem);

            var objects = writer.OpenTable("MSysObjects");
            int name = objects.Definition.RequireColumn("Name").Index;
            int flags = objects.Definition.RequireColumn("Flags").Index;
            var row = objects.Rows().WithIds().Single(r => Equals(r.Values[name], "Orders"));
            row.Values[flags] = (int)row.Values[flags]! | (int)LibRed.Formats.ObjectAttributes.Hidden;
            objects.Update(row.Id, row.Values, new HashSet<int> { flags });

            Assert.True(reader.Catalog.FindTable("Orders")!.IsSystem);
            Assert.False(before.IsSystem);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    private static string Fresh(string prefix) =>
        TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), prefix);
}
