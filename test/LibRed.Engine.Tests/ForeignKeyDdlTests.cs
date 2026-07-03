using LibRed;
using LibRed.Catalog;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class ForeignKeyDdlTests
{
    // CREATE TABLE with an inline FOREIGN KEY (the shape EF Core emits) persists the relationship to
    // MSysRelationships and a child-side index, enforces referential integrity on insert, and both
    // survive a reopen.
    [Fact]
    public void Foreign_key_persists_enforces_and_round_trips()
    {
        string path = Path.Combine(Path.GetTempPath(), $"fk-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var engine = new QueryEngine(db);
                engine.ExecuteNonQuery("CREATE TABLE `Parent` (`Id` INTEGER PRIMARY KEY, `Name` VARCHAR(50))");
                engine.ExecuteNonQuery(
                    "CREATE TABLE `Child` (`Id` INTEGER PRIMARY KEY, `ParentId` INTEGER, " +
                    "CONSTRAINT `FK_Child_Parent` FOREIGN KEY (`ParentId`) REFERENCES `Parent` (`Id`) ON DELETE CASCADE)");

                engine.ExecuteNonQuery("INSERT INTO `Parent` (`Id`, `Name`) VALUES (1, 'p1')");
                engine.ExecuteNonQuery("INSERT INTO `Child` (`Id`, `ParentId`) VALUES (1, 1)");    // valid
                engine.ExecuteNonQuery("INSERT INTO `Child` (`Id`, `ParentId`) VALUES (2, NULL)");  // null FK: allowed

                // Orphan reference is rejected.
                Assert.ThrowsAny<Exception>(() =>
                    engine.ExecuteNonQuery("INSERT INTO `Child` (`Id`, `ParentId`) VALUES (3, 99)"));
            }

            using (var db = JetDatabase.Open(path))
            {
                ForeignKey fk = db.Catalog.Relationships.Single(r => r.Name == "FK_Child_Parent");
                Assert.Equal("Child", fk.Table);
                Assert.Equal("Parent", fk.ReferencedTable);
                Assert.Equal(("ParentId", "Id"), fk.Columns.Single());
                Assert.True(fk.IsEnforced);
                Assert.True(fk.CascadeDelete);
                Assert.False(fk.CascadeUpdate);

                // The child has a non-unique index on the FK column.
                var child = db.Catalog.FindTable("Child")!;
                Assert.Contains(child.Indexes, ix => !ix.IsPrimaryKey && ix.Columns.Any(c => c.Column.Name == "ParentId"));

                // Only the two valid rows were committed.
                Assert.Equal(2, new QueryEngine(db).ExecuteQuery("SELECT `Id` FROM `Child`").Rows.Count());
            }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    // The column-level (single-field) REFERENCES form — `Pid INTEGER CONSTRAINT fk REFERENCES P (Id)` —
    // builds the same relationship as a table-level FOREIGN KEY, and enforces it.
    [Fact]
    public void Column_level_references_builds_and_enforces_the_relationship()
    {
        string path = Path.Combine(Path.GetTempPath(), $"colref-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var engine = new QueryEngine(db);
                engine.ExecuteNonQuery("CREATE TABLE `P` (`Id` INTEGER PRIMARY KEY)");
                engine.ExecuteNonQuery(
                    "CREATE TABLE `C` (`Id` INTEGER PRIMARY KEY, " +
                    "`Pid` INTEGER CONSTRAINT `FK_C_P` REFERENCES `P` (`Id`) ON DELETE CASCADE)");
                engine.ExecuteNonQuery("INSERT INTO `P` (`Id`) VALUES (1)");
                engine.ExecuteNonQuery("INSERT INTO `C` (`Id`, `Pid`) VALUES (1, 1)");
                Assert.ThrowsAny<Exception>(() =>
                    engine.ExecuteNonQuery("INSERT INTO `C` (`Id`, `Pid`) VALUES (2, 42)"));
            }
            using (var db = JetDatabase.Open(path))
            {
                ForeignKey fk = db.Catalog.Relationships.Single(r => r.Name == "FK_C_P");
                Assert.Equal("C", fk.Table);
                Assert.Equal(("Pid", "Id"), fk.Columns.Single());
                Assert.True(fk.CascadeDelete);
            }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    // Column-level and table-level UNIQUE constraints each create a unique (non-primary) index.
    [Fact]
    public void Unique_constraints_create_unique_indexes()
    {
        string path = Path.Combine(Path.GetTempPath(), $"uq-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
                new QueryEngine(db).ExecuteNonQuery(
                    "CREATE TABLE `U` (`Id` INTEGER PRIMARY KEY, `Code` VARCHAR(20) UNIQUE, " +
                    "`A` INTEGER, `B` INTEGER, CONSTRAINT `UQ_AB` UNIQUE (`A`, `B`))");

            using (var db = JetDatabase.Open(path))
            {
                var u = db.Catalog.FindTable("U")!;
                Assert.Contains(u.Indexes, ix => ix.IsUnique && !ix.IsPrimaryKey
                    && ix.Columns.Select(c => c.Column.Name).SequenceEqual(["Code"]));
                Assert.Contains(u.Indexes, ix => ix.IsUnique && !ix.IsPrimaryKey
                    && ix.Columns.Select(c => c.Column.Name).SequenceEqual(["A", "B"]));
            }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    // A multiple-field (composite) foreign key referencing a composite primary key: persists a pair of
    // MSysRelationships rows (icolumn 0/1, ccolumn 2), a single child index over both columns, and
    // enforces both-column referential integrity.
    [Fact]
    public void Composite_foreign_key_persists_and_enforces()
    {
        string path = Path.Combine(Path.GetTempPath(), $"fkcomp-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var engine = new QueryEngine(db);
                engine.ExecuteNonQuery("CREATE TABLE `P` (`A` INTEGER, `B` INTEGER, CONSTRAINT `PK_P` PRIMARY KEY (`A`, `B`))");
                engine.ExecuteNonQuery(
                    "CREATE TABLE `C` (`Id` INTEGER PRIMARY KEY, `A` INTEGER, `B` INTEGER, " +
                    "CONSTRAINT `FK_C_P` FOREIGN KEY (`A`, `B`) REFERENCES `P` (`A`, `B`))");
                engine.ExecuteNonQuery("INSERT INTO `P` (`A`, `B`) VALUES (1, 2)");
                engine.ExecuteNonQuery("INSERT INTO `C` (`Id`, `A`, `B`) VALUES (1, 1, 2)");     // matches (1,2)
                Assert.ThrowsAny<Exception>(() =>                                                 // (1,3) has no parent
                    engine.ExecuteNonQuery("INSERT INTO `C` (`Id`, `A`, `B`) VALUES (2, 1, 3)"));
            }
            using (var db = JetDatabase.Open(path))
            {
                ForeignKey fk = db.Catalog.Relationships.Single(r => r.Name == "FK_C_P");
                Assert.Equal(2, fk.Columns.Count);
                Assert.Equal(("A", "A"), fk.Columns[0]);
                Assert.Equal(("B", "B"), fk.Columns[1]);
                var child = db.Catalog.FindTable("C")!;
                Assert.Contains(child.Indexes, ix => !ix.IsPrimaryKey
                    && ix.Columns.Select(c => c.Column.Name).SequenceEqual(["A", "B"]));
            }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    // A self-referencing foreign key (as in the GearsOfWar model, a table whose FK targets itself). The
    // table is not yet in the catalog when its own FK is resolved, so this is handled inline.
    [Fact]
    public void Self_referencing_foreign_key_creates_and_enforces()
    {
        string path = Path.Combine(Path.GetTempPath(), $"fkself-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var engine = new QueryEngine(db);
                engine.ExecuteNonQuery(
                    "CREATE TABLE `Emp` (`Id` INTEGER PRIMARY KEY, `Mgr` INTEGER, " +
                    "CONSTRAINT `FK_Emp_Emp` FOREIGN KEY (`Mgr`) REFERENCES `Emp` (`Id`))");
                engine.ExecuteNonQuery("INSERT INTO `Emp` (`Id`, `Mgr`) VALUES (1, NULL)"); // top of chain
                engine.ExecuteNonQuery("INSERT INTO `Emp` (`Id`, `Mgr`) VALUES (2, 1)");    // reports to 1
                Assert.ThrowsAny<Exception>(() =>                                            // 99 doesn't exist
                    engine.ExecuteNonQuery("INSERT INTO `Emp` (`Id`, `Mgr`) VALUES (3, 99)"));
            }
            using (var db = JetDatabase.Open(path))
            {
                ForeignKey fk = db.Catalog.Relationships.Single(r => r.Name == "FK_Emp_Emp");
                Assert.Equal("Emp", fk.Table);
                Assert.Equal("Emp", fk.ReferencedTable);
                Assert.Equal(("Mgr", "Id"), fk.Columns.Single());
            }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    // Access documents ON UPDATE before ON DELETE; EF Core emits only ON DELETE. The grammar accepts
    // the clauses in either order, so both cascades must round-trip regardless of ordering.
    [Fact]
    public void On_update_and_on_delete_parse_in_access_order()
    {
        string path = Path.Combine(Path.GetTempPath(), $"fkord-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        try
        {
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                var engine = new QueryEngine(db);
                engine.ExecuteNonQuery("CREATE TABLE `P` (`Id` INTEGER PRIMARY KEY)");
                engine.ExecuteNonQuery(
                    "CREATE TABLE `C` (`Id` INTEGER PRIMARY KEY, `Pid` INTEGER, " +
                    "CONSTRAINT `FK_C_P` FOREIGN KEY (`Pid`) REFERENCES `P` (`Id`) ON UPDATE CASCADE ON DELETE CASCADE)");
            }
            using (var db = JetDatabase.Open(path))
            {
                ForeignKey fk = db.Catalog.Relationships.Single(r => r.Name == "FK_C_P");
                Assert.True(fk.CascadeUpdate);
                Assert.True(fk.CascadeDelete);
            }
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
