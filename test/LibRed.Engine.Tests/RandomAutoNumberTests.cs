using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// A "Random" AutoNumber — an AutoNumber column with DEFAULT GenUniqueID() (Access's "New Values = Random").
// LibRed persists the GenUniqueID() default to the column's LvProp (byte-identical to a UI-authored one) and,
// on insert, assigns a random Int32 per row instead of the sequential seed/increment counter.
public class RandomAutoNumberTests
{
    private static (QueryEngine Engine, JetDatabase Db) Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"rand-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var db = JetDatabase.Open(path, readOnly: false);
        return (new QueryEngine(db), db);
    }

    [Theory]
    [InlineData("COUNTER DEFAULT GenUniqueID()")]
    [InlineData("AUTOINCREMENT DEFAULT GenUniqueID()")]
    public void Assigns_random_ids_not_the_sequential_counter(string type)
    {
        var (e, _) = Fresh();
        e.ExecuteNonQuery($"CREATE TABLE R ( Id {type}, Name text(10) )");
        foreach (var n in new[] { "a", "b", "c", "d", "e" })
            e.ExecuteNonQuery($"INSERT INTO R (Name) VALUES ('{n}')");

        int[] ids = e.ExecuteQuery("SELECT Id FROM R").Rows.Select(r => Convert.ToInt32(r[0])).ToArray();

        Assert.Equal(5, ids.Length);
        Assert.Equal(5, ids.Distinct().Count());              // unique per row
        Assert.DoesNotContain(0, ids);                        // GenUniqueID() never yields 0
        // Not the 1,2,3,4,5 sequential counter — random ids won't be a contiguous ascending run.
        Assert.False(ids.Zip(ids.Skip(1)).All(p => p.Second - p.First == 1), "ids should not be sequential");
    }

    [Fact]
    public void GenUniqueID_default_round_trips_as_a_random_autonumber()
    {
        var (e, db) = Fresh();
        e.ExecuteNonQuery("CREATE TABLE R ( Id COUNTER DEFAULT GenUniqueID(), Name text(10) )");

        var col = db.Catalog.UserTables.Single(t => t.Name == "R").Columns.Single(c => c.Name == "Id");
        Assert.True(col.IsAutoNumber);
        Assert.True(col.IsRandomAutoNumber);
        Assert.Equal("GenUniqueID()", col.DefaultValue);
    }

    [Fact]
    public void Retrieves_the_generated_random_id_as_identity()
    {
        var (e, _) = Fresh();
        e.ExecuteNonQuery("CREATE TABLE R ( Id COUNTER DEFAULT GenUniqueID(), Name text(10) )");
        e.ExecuteNonQuery("INSERT INTO R (Name) VALUES ('a')");

        object identity = e.ExecuteQuery("SELECT @@IDENTITY").Rows.Single()[0]!;
        int stored = e.ExecuteQuery("SELECT Id FROM R").Rows.Select(r => Convert.ToInt32(r[0])).Single();
        Assert.Equal(stored, Convert.ToInt32(identity));
    }
}
