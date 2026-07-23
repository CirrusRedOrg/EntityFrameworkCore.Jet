using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// ALTER TABLE … RENAME TO, executed natively. Jet/ACE has no rename SQL — Access renames through DAO/ADOX —
// so EFCore.Jet emits this as pseudo-SQL and intercepts it out-of-engine. LibRed has no COM to delegate to and
// does the catalog surgery itself, reproducing exactly what ACE does (measured in the Jet suite's
// RenameFanOutProbeTest): the object's MSysObjects.Name moves and MSysRelationships is repointed — and nothing
// else, because ACE touches nothing else.
public class AlterTableRenameTests
{
    private static string Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"rename-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        return path;
    }

    private static void CreateParentChild(QueryEngine e)
    {
        e.ExecuteNonQuery("CREATE TABLE Parent (Id LONG CONSTRAINT PK_Parent PRIMARY KEY, Name TEXT(50))");
        e.ExecuteNonQuery(
            "CREATE TABLE Child (Id LONG CONSTRAINT PK_Child PRIMARY KEY, ParentId LONG, " +
            "CONSTRAINT FK_Child_Parent FOREIGN KEY (ParentId) REFERENCES Parent (Id))");
        e.ExecuteNonQuery("CREATE INDEX IX_Parent_Name ON Parent (Name)");
        e.ExecuteNonQuery("INSERT INTO Parent (Id, Name) VALUES (1, 'one')");
        e.ExecuteNonQuery("INSERT INTO Child (Id, ParentId) VALUES (1, 1)");
    }

    [Fact]
    public void Rename_moves_the_catalog_name_and_repoints_the_relationship()
    {
        string path = Fresh();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var e = new QueryEngine(db);
                CreateParentChild(e);

                e.ExecuteNonQuery("ALTER TABLE Parent RENAME TO ParentRenamed");

                // The object moved: the new name resolves, the old one no longer does.
                Assert.NotNull(db.Catalog.FindTable("ParentRenamed"));
                Assert.Null(db.Catalog.FindTable("Parent"));

                // The relationship follows the new name, keeping its own name — exactly as ACE does.
                Assert.Collection(
                    db.Catalog.ForeignKeysOf("Child"),
                    fk =>
                    {
                        Assert.Equal("FK_Child_Parent", fk.Name);
                        Assert.Equal("ParentRenamed", fk.ReferencedTable);
                        Assert.True(fk.IsEnforced);
                    });

                // Indexes ride along and keep their own names (they reference the table by id, not name).
                var renamed = db.Catalog.FindTable("ParentRenamed")!;
                Assert.Contains(renamed.Indexes, i => i.Name == "PK_Parent");
                Assert.Contains(renamed.Indexes, i => i.Name == "IX_Parent_Name");

                // The rows are still there and readable under the new name.
                Assert.Equal("one", e.ExecuteQuery("SELECT Name FROM ParentRenamed WHERE Id = 1").Rows.Single()[0]);
            }

            // …and it all survives a reopen (the catalog edit really is on disk).
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                Assert.NotNull(db.Catalog.FindTable("ParentRenamed"));
                Assert.Null(db.Catalog.FindTable("Parent"));
                Assert.Equal("ParentRenamed", Assert.Single(db.Catalog.ForeignKeysOf("Child")).ReferencedTable);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    // The repointed relationship must still be a working FK, not just a tidy catalog row.
    [Fact]
    public void Renamed_table_still_enforces_its_foreign_key()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            CreateParentChild(e);

            e.ExecuteNonQuery("ALTER TABLE Parent RENAME TO ParentRenamed");

            // Still rejected: the child has no such parent.
            Assert.Throws<InvalidOperationException>(
                () => e.ExecuteNonQuery("INSERT INTO Child (Id, ParentId) VALUES (2, 99)"));

            // Still accepted through the renamed parent.
            e.ExecuteNonQuery("INSERT INTO ParentRenamed (Id, Name) VALUES (2, 'two')");
            Assert.Equal(1, e.ExecuteNonQuery("INSERT INTO Child (Id, ParentId) VALUES (2, 2)"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // A self-reference names the table on both sides of the same relationship row, so both must be repointed.
    [Fact]
    public void Rename_repoints_both_sides_of_a_self_reference()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery(
                "CREATE TABLE Node (Id LONG CONSTRAINT PK_Node PRIMARY KEY, ParentId LONG, " +
                "CONSTRAINT FK_Node_Node FOREIGN KEY (ParentId) REFERENCES Node (Id))");

            e.ExecuteNonQuery("ALTER TABLE Node RENAME TO Tree");

            var fk = Assert.Single(db.Catalog.ForeignKeysOf("Tree"));
            Assert.Equal("Tree", fk.Table);
            Assert.Equal("Tree", fk.ReferencedTable);
            Assert.Empty(db.Catalog.ForeignKeysOf("Node"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // A table is not a collision with itself. EF's "move table to another schema" degrades to a self-rename on
    // a schema-less engine (the generator emits RENAME TO the same name), and a case-only change is the same
    // object too — ACE allows both (verified in the Jet suite's RenameFanOutProbeTest).
    [Fact]
    public void Rename_allows_renaming_a_table_to_its_own_name_or_a_different_case()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            CreateParentChild(e);

            // Self-rename: a no-op, but it must not throw — this is what EF's Move_table emits.
            e.ExecuteNonQuery("ALTER TABLE Parent RENAME TO Parent");
            Assert.Equal("Parent", db.Catalog.FindTable("Parent")!.Name);

            // Case-only: allowed, and the stored name really does take the new casing.
            e.ExecuteNonQuery("ALTER TABLE Parent RENAME TO PARENT");
            Assert.Equal("PARENT", db.Catalog.FindTable("parent")!.Name);

            // The relationship survives both.
            Assert.Equal("PARENT", Assert.Single(db.Catalog.ForeignKeysOf("Child")).ReferencedTable);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void CreateDocSchema(QueryEngine e)
    {
        e.ExecuteNonQuery(
            "CREATE TABLE Doc (Id LONG CONSTRAINT PK_Doc PRIMARY KEY, Title TEXT(50) DEFAULT 'untitled')");
        e.ExecuteNonQuery("CREATE INDEX IX_Doc_Title ON Doc (Title)");
        e.ExecuteNonQuery(
            "CREATE TABLE DocChild (Id LONG CONSTRAINT PK_DocChild PRIMARY KEY, DocId LONG, " +
            "CONSTRAINT FK_DocChild_Doc FOREIGN KEY (DocId) REFERENCES Doc (Id))");
        e.ExecuteNonQuery("INSERT INTO Doc (Id, Title) VALUES (1, 'hello')");
    }

    // The measured ACE contract for a column rename: the name moves, the column keeps its DEFAULT (its LvProp
    // block is re-owned), the index over it survives keeping its own name, and the data is untouched.
    [Fact]
    public void Rename_column_moves_the_name_keeps_the_default_and_the_index()
    {
        string path = Fresh();
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var e = new QueryEngine(db);
                CreateDocSchema(e);

                e.ExecuteNonQuery("ALTER TABLE Doc RENAME COLUMN Title TO Heading");

                var doc = db.Catalog.FindTable("Doc")!;
                Assert.Contains(doc.Columns, c => c.Name == "Heading");
                Assert.DoesNotContain(doc.Columns, c => c.Name == "Title");

                // The DEFAULT rides along — this is the LvProp owner rewrite.
                Assert.Equal("'untitled'", doc.Columns.Single(c => c.Name == "Heading").DefaultValue);

                // The index keeps its own (now descriptively stale) name and needs no fixup.
                Assert.Contains(doc.Indexes, i => i.Name == "IX_Doc_Title");

                // Existing row data is unaffected and readable under the new name.
                Assert.Equal("hello", e.ExecuteQuery("SELECT Heading FROM Doc WHERE Id = 1").Rows.Single()[0]);

                // The default still applies to new rows.
                e.ExecuteNonQuery("INSERT INTO Doc (Id) VALUES (2)");
                Assert.Equal("untitled", e.ExecuteQuery("SELECT Heading FROM Doc WHERE Id = 2").Rows.Single()[0]);
            }

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var doc = db.Catalog.FindTable("Doc")!;
                Assert.Contains(doc.Columns, c => c.Name == "Heading");
                Assert.Equal("'untitled'", doc.Columns.Single(c => c.Name == "Heading").DefaultValue);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    // MSysRelationships names the FK's columns, so renaming either side's column must repoint it — and the FK
    // has to keep working afterwards, not merely look right in the catalog.
    [Fact]
    public void Rename_column_repoints_the_relationship_on_either_side()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            CreateDocSchema(e);

            // Child side (the FK column).
            e.ExecuteNonQuery("ALTER TABLE DocChild RENAME COLUMN DocId TO OwnerId");
            var fk = Assert.Single(db.Catalog.ForeignKeysOf("DocChild"));
            Assert.Equal("OwnerId", fk.Columns.Single().Column);
            Assert.Equal("Id", fk.Columns.Single().ReferencedColumn);

            // Parent side (the referenced key column).
            e.ExecuteNonQuery("ALTER TABLE Doc RENAME COLUMN Id TO DocKey");
            fk = Assert.Single(db.Catalog.ForeignKeysOf("DocChild"));
            Assert.Equal("OwnerId", fk.Columns.Single().Column);
            Assert.Equal("DocKey", fk.Columns.Single().ReferencedColumn);

            // Still a working foreign key, through both renamed names.
            Assert.Throws<InvalidOperationException>(
                () => e.ExecuteNonQuery("INSERT INTO DocChild (Id, OwnerId) VALUES (1, 99)"));
            Assert.Equal(1, e.ExecuteNonQuery("INSERT INTO DocChild (Id, OwnerId) VALUES (1, 1)"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Rename_column_rejects_a_name_already_on_the_table_and_a_column_that_does_not_exist()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            CreateDocSchema(e);

            Assert.Throws<InvalidOperationException>(
                () => e.ExecuteNonQuery("ALTER TABLE Doc RENAME COLUMN Title TO Id"));
            Assert.Throws<InvalidOperationException>(
                () => e.ExecuteNonQuery("ALTER TABLE Doc RENAME COLUMN NoSuchColumn TO Whatever"));

            // A column name only has to be unique within its own table, so this is fine.
            e.ExecuteNonQuery("ALTER TABLE DocChild RENAME COLUMN DocId TO Title");
            Assert.Contains(db.Catalog.FindTable("DocChild")!.Columns, c => c.Name == "Title");
            Assert.Contains(db.Catalog.FindTable("Doc")!.Columns, c => c.Name == "Title");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Rename_rejects_a_name_that_is_already_taken_and_a_table_that_does_not_exist()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            CreateParentChild(e);

            Assert.Throws<InvalidOperationException>(
                () => e.ExecuteNonQuery("ALTER TABLE Parent RENAME TO Child"));
            Assert.Throws<InvalidOperationException>(
                () => e.ExecuteNonQuery("ALTER TABLE NoSuchTable RENAME TO Whatever"));

            // Access keeps tables and saved queries in ONE namespace, so a query's name is taken too — ACE
            // rejects this (verified in the Jet suite's RenameFanOutProbeTest), and so must LibRed. The unique
            // (ParentId, Name) index alone would not catch it: queries sit in a different container.
            e.ExecuteNonQuery("CREATE VIEW vwParent AS SELECT Id, Name FROM Parent");
            Assert.Throws<InvalidOperationException>(
                () => e.ExecuteNonQuery("ALTER TABLE Child RENAME TO vwParent"));

            // The failed renames changed nothing.
            Assert.NotNull(db.Catalog.FindTable("Parent"));
            Assert.NotNull(db.Catalog.FindTable("Child"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
