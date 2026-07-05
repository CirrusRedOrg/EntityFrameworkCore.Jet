using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// DROP INDEX index ON table through LibRed's engine.
public class DropIndexTests
{
    private static QueryEngine Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"dropix-eng-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE T (Id long PRIMARY KEY, Name text(20), Code long)");
        e.ExecuteNonQuery("CREATE INDEX IX_Name ON T (Name)");
        e.ExecuteNonQuery("CREATE UNIQUE INDEX UX_Code ON T (Code)");
        e.ExecuteNonQuery("INSERT INTO T (Id, Name, Code) VALUES (1, 'a', 10)");
        e.ExecuteNonQuery("INSERT INTO T (Id, Name, Code) VALUES (2, 'b', 20)");
        return e;
    }

    private static string[] IndexNames(QueryEngine e, string table) =>
        e.Database.Catalog.FindTable(table)!.Indexes.Select(i => i.Name).ToArray();

    [Fact]
    public void Drops_a_secondary_index_leaving_the_others()
    {
        var e = Fresh();
        e.ExecuteNonQuery("DROP INDEX IX_Name ON T");

        Assert.DoesNotContain("IX_Name", IndexNames(e, "T"));
        Assert.Contains("UX_Code", IndexNames(e, "T"));
        // Data still intact and queryable after the index is gone.
        Assert.Equal(2, e.ExecuteQuery("SELECT Id FROM T").Rows.Count());
        Assert.Equal("b", e.ExecuteQuery("SELECT Name FROM T WHERE Id = 2").Rows.Single()[0]);
    }

    [Fact]
    public void Unique_and_primary_key_indexes_can_be_dropped()
    {
        var e = Fresh();
        e.ExecuteNonQuery("DROP INDEX UX_Code ON T");     // unique
        Assert.DoesNotContain("UX_Code", IndexNames(e, "T"));

        string pk = e.Database.Catalog.FindTable("T")!.Indexes.Single(i => i.IsPrimaryKey).Name;
        e.ExecuteNonQuery($"DROP INDEX `{pk}` ON T");     // even the primary key
        Assert.DoesNotContain(pk, IndexNames(e, "T"));
        Assert.Equal(2, e.ExecuteQuery("SELECT Id FROM T").Rows.Count()); // data intact
    }

    [Fact]
    public void Dropping_a_missing_index_or_a_relationship_index_is_rejected()
    {
        var e = Fresh();
        Assert.Throws<InvalidOperationException>(() => e.ExecuteNonQuery("DROP INDEX Nope ON T"));

        e.ExecuteNonQuery("CREATE TABLE C (Id long PRIMARY KEY, Tid long, " +
                          "CONSTRAINT FK FOREIGN KEY (Tid) REFERENCES T (Id))");
        var ex = Assert.Throws<InvalidOperationException>(() => e.ExecuteNonQuery("DROP INDEX FK ON C"));
        Assert.Contains("relationship", ex.Message);
    }
}
