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

    // GenUniqueID() is also valid as a plain (non-AutoNumber) LONG column default (ACE accepts it only on LONG).
    // Unlike an AutoNumber, the column is user-writable: an omitted value gets a random Long, a supplied one is
    // kept verbatim.
    [Fact]
    public void Plain_long_default_generates_random_values_but_stays_user_writable()
    {
        var (e, _) = Fresh();
        e.ExecuteNonQuery("CREATE TABLE R ( K LONG PRIMARY KEY, V LONG DEFAULT GenUniqueID() )");
        e.ExecuteNonQuery("INSERT INTO R (K) VALUES (1)");            // V defaulted → random
        e.ExecuteNonQuery("INSERT INTO R (K) VALUES (2)");            // V defaulted → random
        e.ExecuteNonQuery("INSERT INTO R (K, V) VALUES (3, 777)");    // V supplied → kept

        var rows = e.ExecuteQuery("SELECT K, V FROM R ORDER BY K").Rows
            .Select(r => (K: Convert.ToInt32(r[0]), V: Convert.ToInt32(r[1]))).ToArray();

        Assert.Equal(777, rows.Single(r => r.K == 3).V);             // supplied value preserved
        var defaulted = rows.Where(r => r.K != 3).Select(r => r.V).ToArray();
        Assert.All(defaulted, v => Assert.NotEqual(0, v));
        Assert.Equal(2, defaulted.Distinct().Count());               // random, not a constant
    }

    // ACE accepts GenUniqueID() as a DEFAULT only on a LONG (Int32) column; every other type is rejected at
    // CREATE time ("Cannot place this validation expression on this field"). LibRed matches that validation.
    [Theory]
    [InlineData("BYTE")]
    [InlineData("SHORT")]
    [InlineData("DOUBLE")]
    [InlineData("CURRENCY")]
    [InlineData("GUID")]
    [InlineData("DATETIME")]
    [InlineData("BIT")]
    [InlineData("TEXT(20)")]
    public void GenUniqueID_default_is_rejected_on_a_non_long_column(string type)
    {
        var (e, _) = Fresh();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            e.ExecuteNonQuery($"CREATE TABLE R ( K LONG PRIMARY KEY, V {type} DEFAULT GenUniqueID() )"));
        Assert.Contains("Cannot place this validation expression on this field", ex.Message);
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
