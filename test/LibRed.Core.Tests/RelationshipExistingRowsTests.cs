using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

// ACE refuses to create an enforced relationship the rows already in the child table break: an orphan, or a
// composite key only partly null (MATCH FULL). The check used to be made by the SQL statement alone, after the
// relationship was written, so JetDatabase.AddForeignKey created one over orphan rows — a relationship ACE then
// enforced on every later write while its own rows violated it. It is now made in Core, before anything is
// written, so every route is held to it. (The SQL statement's version is in the Engine's relationship tests.)
public class RelationshipExistingRowsTests : TempDatabaseTest
{
    private static readonly RelationshipSpec Fk =
        new("fkCP", "P", [("A", "A"), ("B", "B")], IsEnforced: true, CascadeUpdate: false, CascadeDelete: false);

    private static JetDatabase Database(params (int? A, int? B)[] children)
    {
        string path = TemporaryDatabase.CopyPath(TestDatabases.NorthwindAccdb, "relrows-");
        JetDatabase db = TemporaryDatabase.OpenTracked(path, readOnly: false);
        ColumnSpec Long(string name) => new(name, JetDataType.Int32, 4, IsFixedLength: true);
        db.CreateTable("P", [Long("A"), Long("B")], primaryKey: ["A", "B"]);
        db.CreateTable("C", [Long("Id"), Long("A"), Long("B")]);
        db.OpenTable("P").Insert([1, 1]);
        Table child = db.OpenTable("C");
        for (int i = 0; i < children.Length; i++) child.Insert([i, children[i].A, children[i].B]);
        return db;
    }

    [Theory]
    [InlineData(2, 2)]       // no such parent
    [InlineData(1, null)]    // partly null: can never match a full parent key
    public void An_enforced_relationship_the_existing_rows_break_is_refused_before_it_is_written(int? a, int? b)
    {
        JetDatabase db = Database((1, 1), (a, b));

        var refused = Assert.Throws<InvalidOperationException>(() => db.AddForeignKey("C", Fk));
        Assert.Equal(
            "Cannot create relationships to enforce referential integrity. Existing data in table 'C' "
            + "violates referential integrity rules in table 'P'.",
            refused.Message);
        Assert.DoesNotContain(db.Catalog.Relationships, r => r.Name == "fkCP");
        Assert.DoesNotContain(db.Catalog.FindTable("C")!.Indexes, i => i.Name == "fkCP");
    }

    [Fact]
    public void Rows_with_a_parent_or_an_entirely_null_key_allow_it()
    {
        JetDatabase db = Database((1, 1), (null, null));
        db.AddForeignKey("C", Fk);
        Assert.Contains(db.Catalog.Relationships, r => r.Name == "fkCP");
    }

    [Fact]
    public void An_unenforced_relationship_is_only_a_declaration_and_is_not_checked()
    {
        JetDatabase db = Database((2, 2));
        db.AddForeignKey("C", Fk with { IsEnforced = false });
        Assert.Contains(db.Catalog.Relationships, r => r.Name == "fkCP");
    }
}
