using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class AlterTableDropConstraintTests
{
    private static string Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "dropc-");
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

                // Now the same insert is accepted; the relationship AND its backing index are gone.
                Assert.Equal(1, e.ExecuteNonQuery("INSERT INTO Child (Id, ParentId) VALUES (1, 99)"));
                Assert.Empty(db.Catalog.ForeignKeysOf("Child"));
                Assert.DoesNotContain(db.Catalog.FindTable("Child")!.Indexes, i => i.Name == "FK_Child_Parent");
            }

            // Reopen: the drop persisted (soft-deleted MSysRelationships row + removed TDEF blocks).
            using (var db = JetDatabase.Open(path))
            {
                Assert.Empty(db.Catalog.ForeignKeysOf("Child"));
                var child = db.Catalog.FindTable("Child")!;
                Assert.Single(child.Indexes);            // only the primary key remains
                Assert.True(child.Indexes[0].IsPrimaryKey);
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Drop then re-add the same FK: because the drop fully removes the old backing index + TDEF blocks,
    // the re-add doesn't collide with an orphan — enforcement is back afterward.
    [Fact]
    public void Drop_then_re_add_same_foreign_key_works()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery("CREATE TABLE Parent (Id LONG CONSTRAINT PK_Parent PRIMARY KEY)");
            e.ExecuteNonQuery("CREATE TABLE Child (Id LONG CONSTRAINT PK_Child PRIMARY KEY, ParentId LONG, " +
                "CONSTRAINT FK_Child_Parent FOREIGN KEY (ParentId) REFERENCES Parent (Id))");
            e.ExecuteNonQuery("INSERT INTO Parent (Id) VALUES (1)");

            e.ExecuteNonQuery("ALTER TABLE Child DROP CONSTRAINT FK_Child_Parent");
            e.ExecuteNonQuery("ALTER TABLE Child ADD CONSTRAINT FK_Child_Parent FOREIGN KEY (ParentId) REFERENCES Parent (Id)");

            // The re-added FK is a single, enforced relationship again.
            Assert.Single(db.Catalog.ForeignKeysOf("Child"));
            Assert.Single(db.Catalog.FindTable("Child")!.Indexes.Where(i => i.Name == "FK_Child_Parent"));
            Assert.Throws<InvalidOperationException>(() => e.ExecuteNonQuery("INSERT INTO Child (Id, ParentId) VALUES (1, 99)"));
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // A self-referencing FK hosts both ends (outgoing + incoming block) in one TDEF; dropping it removes
    // both and the backing index.
    [Fact]
    public void Drop_self_referencing_foreign_key()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery("CREATE TABLE Emp (Id LONG CONSTRAINT PK_Emp PRIMARY KEY, MgrId LONG, " +
                "CONSTRAINT FK_Emp_Emp FOREIGN KEY (MgrId) REFERENCES Emp (Id))");
            e.ExecuteNonQuery("INSERT INTO Emp (Id, MgrId) VALUES (1, NULL)");
            Assert.Throws<InvalidOperationException>(() => e.ExecuteNonQuery("INSERT INTO Emp (Id, MgrId) VALUES (2, 99)"));

            e.ExecuteNonQuery("ALTER TABLE Emp DROP CONSTRAINT FK_Emp_Emp");

            Assert.Empty(db.Catalog.ForeignKeysOf("Emp"));
            Assert.Single(db.Catalog.FindTable("Emp")!.Indexes); // only PK_Emp
            Assert.Equal(1, e.ExecuteNonQuery("INSERT INTO Emp (Id, MgrId) VALUES (2, 99)")); // now allowed
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // Dropping a name that is neither a relationship nor an index throws a clear error. (A real FK/PK/unique
    // name would be dropped — DROP CONSTRAINT falls through to the index-drop path; see DropConstraintIndexTests.)
    [Fact]
    public void Drop_unknown_constraint_throws()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery("CREATE TABLE T (Id LONG CONSTRAINT PK_T PRIMARY KEY)");
            var ex = Assert.Throws<InvalidOperationException>(
                () => e.ExecuteNonQuery("ALTER TABLE T DROP CONSTRAINT NoSuchThing"));
            Assert.Contains("NoSuchThing", ex.Message);
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
