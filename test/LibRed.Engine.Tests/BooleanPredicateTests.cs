using LibRed;
using LibRed.Engine;
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

    // A boolean stored as a -1/0 integer (the nullable-bool convention) works as a bare predicate, negated,
    // and combined — Access truthiness treats any non-zero number as true.
    [Fact]
    public void Integer_backed_boolean_is_truthy_as_a_predicate()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery("CREATE TABLE Flags (Id LONG, Active SMALLINT)");
            e.ExecuteNonQuery("INSERT INTO Flags (Id, Active) VALUES (1, -1)");
            e.ExecuteNonQuery("INSERT INTO Flags (Id, Active) VALUES (2, 0)");
            e.ExecuteNonQuery("INSERT INTO Flags (Id, Active) VALUES (3, -1)");
            e.ExecuteNonQuery("INSERT INTO Flags (Id, Active) VALUES (4, 0)");

            Assert.Equal(2, e.ExecuteQuery("SELECT Id FROM Flags WHERE Active").Rows.Count());
            Assert.Equal(2, e.ExecuteQuery("SELECT Id FROM Flags WHERE NOT Active").Rows.Count());
            Assert.Equal(1, e.ExecuteQuery("SELECT Id FROM Flags WHERE Active AND Id = 3").Rows.Count());
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
