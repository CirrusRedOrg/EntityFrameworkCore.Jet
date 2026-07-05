using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class AlterTableDropConstraintTests
{
    private static string Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"dropc-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        return path;
    }

    // ALTER TABLE … DROP CONSTRAINT <fk>: LibRed enforces FKs from MSysRelationships, so dropping the
    // relationship there stops enforcement. Verified: a row that violated the FK is rejected before the
    // drop and accepted after, the relationship is gone from the catalog, and it stays gone on reopen.
    [Fact]
    public void Drop_foreign_key_constraint_stops_enforcement_and_persists()
    {
        string path = Fresh();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var e = new QueryEngine(db);
                e.ExecuteNonQuery("CREATE TABLE Parent (Id LONG CONSTRAINT PK_Parent PRIMARY KEY)");
                e.ExecuteNonQuery(
                    "CREATE TABLE Child (Id LONG CONSTRAINT PK_Child PRIMARY KEY, ParentId LONG, " +
                    "CONSTRAINT FK_Child_Parent FOREIGN KEY (ParentId) REFERENCES Parent (Id))");
                e.ExecuteNonQuery("INSERT INTO Parent (Id) VALUES (1)");

                // The FK is enforced: a child pointing at a non-existent parent is rejected.
                Assert.Throws<InvalidOperationException>(
                    () => e.ExecuteNonQuery("INSERT INTO Child (Id, ParentId) VALUES (1, 99)"));

                e.ExecuteNonQuery("ALTER TABLE Child DROP CONSTRAINT FK_Child_Parent");

                // Now the same insert is accepted, and the relationship is gone from the catalog.
                Assert.Equal(1, e.ExecuteNonQuery("INSERT INTO Child (Id, ParentId) VALUES (1, 99)"));
                Assert.Empty(db.Catalog.ForeignKeysOf("Child"));
            }

            // Reopen: the drop persisted (the soft-deleted MSysRelationships row stays skipped).
            using (var db = JetDatabase.Open(path))
                Assert.Empty(db.Catalog.ForeignKeysOf("Child"));
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    // Dropping a name that isn't a foreign key throws a clear error (index/PK drop not implemented yet).
    [Fact]
    public void Drop_unknown_constraint_throws()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery("CREATE TABLE T (Id LONG CONSTRAINT PK_T PRIMARY KEY)");
            var ex = Assert.Throws<NotSupportedException>(
                () => e.ExecuteNonQuery("ALTER TABLE T DROP CONSTRAINT NoSuchThing"));
            Assert.Contains("NoSuchThing", ex.Message);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
