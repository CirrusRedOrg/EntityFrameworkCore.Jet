using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// DROP TABLE on a table that participates in a relationship: dropping the CHILD (referencing) table is
// allowed and removes the relationship (ACE lets you drop the referencing table while the parent stays);
// dropping a table still REFERENCED as a parent by a surviving child is rejected. Mirrors the order the
// scaffolding cleanup uses (child first, then parent).
public class DropTableRelationshipTests
{
    private static QueryEngine Setup()
    {
        string path = Path.Combine(Path.GetTempPath(), $"drop-rel-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE K2 ( Id int, A varchar, UNIQUE (A) )");
        e.ExecuteNonQuery("CREATE TABLE Kilimanjaro ( Id int, B varchar, UNIQUE (B), FOREIGN KEY (B) REFERENCES K2 (A) )");
        return e;
    }

    [Fact]
    public void Dropping_the_child_table_first_then_the_parent_succeeds()
    {
        var e = Setup();

        // Child (referencing) table drops directly — the relationship goes with it.
        e.ExecuteNonQuery("DROP TABLE Kilimanjaro");
        // Parent now has no surviving child, so it drops too.
        e.ExecuteNonQuery("DROP TABLE K2");

        // Both names are free again (a re-create with the same name is the strongest proof they're gone).
        e.ExecuteNonQuery("CREATE TABLE K2 ( Id int )");
        e.ExecuteNonQuery("CREATE TABLE Kilimanjaro ( Id int )");
    }

    [Fact]
    public void Dropping_the_parent_while_a_child_still_references_it_is_rejected()
    {
        var e = Setup();

        var ex = Assert.Throws<InvalidOperationException>(() => e.ExecuteNonQuery("DROP TABLE K2"));
        Assert.Contains("referenced by a relationship", ex.Message);

        // The parent is untouched: dropping the child first still works afterwards.
        e.ExecuteNonQuery("DROP TABLE Kilimanjaro");
        e.ExecuteNonQuery("DROP TABLE K2");
    }
}
