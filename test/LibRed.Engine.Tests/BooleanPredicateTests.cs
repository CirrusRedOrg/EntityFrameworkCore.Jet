using LibRed;
using LibRed.Engine;
using LibRed.Engine.Execution;
using Xunit;

namespace LibRed.Engine.Tests;

public class BooleanPredicateTests
{
    private static string Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"boolpred-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        return path;
    }

    // Access truthiness (confirmed against ACE): ANY non-zero number is true (int or double), 0 is false,
    // NULL is not-true; NOT flips 0↔non-zero and leaves NULL not-selected. Covers the nullable-bool -1/0
    // convention plus arbitrary numeric predicates.
    [Fact]
    public void Any_nonzero_number_is_truthy_as_a_predicate()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery("CREATE TABLE Tvals (Id LONG, N LONG, D DOUBLE)");
            foreach (var (id, n, d) in new (int, string, string)[]
                { (1, "-1", "-1"), (2, "0", "0"), (3, "1", "1"), (4, "2", "0.5"), (5, "-5", "0"), (6, "NULL", "NULL") })
                e.ExecuteNonQuery($"INSERT INTO Tvals (Id, N, D) VALUES ({id}, {n}, {d})");

            static IEnumerable<int> Ids(ResultSet r) => r.Rows.Select(row => Convert.ToInt32(row[0])).OrderBy(x => x);

            Assert.Equal([1, 3, 4, 5], Ids(e.ExecuteQuery("SELECT Id FROM Tvals WHERE N")));       // every non-zero int
            Assert.Equal([2], Ids(e.ExecuteQuery("SELECT Id FROM Tvals WHERE NOT N")));            // only 0 (NULL not selected)
            Assert.Equal([1, 3, 4], Ids(e.ExecuteQuery("SELECT Id FROM Tvals WHERE D")));          // non-zero doubles incl. 0.5
            Assert.Equal(1, e.ExecuteQuery("SELECT Id FROM Tvals WHERE N AND Id = 3").Rows.Count()); // combined
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    // A native BIT column written by LibRed: the value may be inserted as 1/-1/0 or TRUE/FALSE (Northwind's
    // seed uses integer literals). The bit must be set for any truthy value, and read back / filtered right.
    [Fact]
    public void Bit_column_written_by_libred_round_trips_all_value_forms()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery("CREATE TABLE B (Id LONG, Flag BIT NOT NULL DEFAULT 0)");
            e.ExecuteNonQuery("INSERT INTO B (Id, Flag) VALUES (1, 1)");
            e.ExecuteNonQuery("INSERT INTO B (Id, Flag) VALUES (2, 0)");
            e.ExecuteNonQuery("INSERT INTO B (Id, Flag) VALUES (3, -1)");
            e.ExecuteNonQuery("INSERT INTO B (Id, Flag) VALUES (4, TRUE)");
            e.ExecuteNonQuery("INSERT INTO B (Id, Flag) VALUES (5, FALSE)");

            var trueIds = e.ExecuteQuery("SELECT Id FROM B WHERE Flag").Rows.Select(r => Convert.ToInt32(r[0])).OrderBy(x => x);
            Assert.Equal([1, 3, 4], trueIds);                 // 1, -1, TRUE are true
            Assert.Equal(2, e.ExecuteQuery("SELECT Id FROM B WHERE NOT Flag").Rows.Count()); // 0, FALSE

            // The decoded value is a real bool.
            Assert.Equal(true, e.ExecuteQuery("SELECT Flag FROM B WHERE Id = 1").Rows.First()[0]);
            Assert.Equal(false, e.ExecuteQuery("SELECT Flag FROM B WHERE Id = 2").Rows.First()[0]);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    // A native Jet YesNo (bool) column still works as a bare predicate (Northwind Products.Discontinued = 8).
    [Fact]
    public void Native_yesno_boolean_is_truthy_as_a_predicate()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path);
            var e = new QueryEngine(db);
            Assert.Equal(8, e.ExecuteQuery("SELECT ProductID FROM Products WHERE Discontinued").Rows.Count());
            Assert.Equal(69, e.ExecuteQuery("SELECT ProductID FROM Products WHERE NOT Discontinued").Rows.Count());
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
