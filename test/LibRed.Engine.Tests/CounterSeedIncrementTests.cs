using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// COUNTER(seed, increment): the first generated AutoNumber id is the seed, then +increment per row (verified
// vs ACE, which stores increment at TDEF 0x18 and last-value = seed-increment at 0x14). A plain COUNTER is 1/1.
public class CounterSeedIncrementTests
{
    private static (QueryEngine Engine, JetDatabase Db) Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"counter-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var db = JetDatabase.Open(path, readOnly: false);
        return (new QueryEngine(db), db);
    }

    [Theory]
    [InlineData("COUNTER(5, 3)", 5, 8, 11)]
    [InlineData("AUTOINCREMENT(100, 10)", 100, 110, 120)]
    [InlineData("INTEGER IDENTITY(1000, 7)", 1000, 1007, 1014)]
    [InlineData("COUNTER", 1, 2, 3)]
    public void Generates_seeded_ids(string type, int a, int b, int c)
    {
        var (e, _) = Fresh();
        e.ExecuteNonQuery($"CREATE TABLE C ( Id {type}, Name text(10) )");
        e.ExecuteNonQuery("INSERT INTO C (Name) VALUES ('a')");
        e.ExecuteNonQuery("INSERT INTO C (Name) VALUES ('b')");
        e.ExecuteNonQuery("INSERT INTO C (Name) VALUES ('c')");

        int[] ids = e.ExecuteQuery("SELECT Id FROM C ORDER BY Id").Rows
            .Select(r => Convert.ToInt32(r[0])).ToArray();
        Assert.Equal(new[] { a, b, c }, ids);
    }

    [Fact]
    public void Seed_and_increment_round_trip_through_the_catalog()
    {
        var (e, db) = Fresh();
        e.ExecuteNonQuery("CREATE TABLE C ( Id COUNTER(42, 5), Name text(10) )");

        var col = db.Catalog.UserTables.Single(t => t.Name == "C").Columns.Single(c => c.Name == "Id");
        Assert.True(col.IsAutoNumber);
        Assert.Equal(42, col.Seed);
        Assert.Equal(5, col.Increment);
    }
}
