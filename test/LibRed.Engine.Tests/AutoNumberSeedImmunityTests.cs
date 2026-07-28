using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// LibRed is immune to the KB 884185 AutoNumber-reseed bug: the 0x14 high-water only advances (max for an
// ascending counter), so an explicit INSERT of a LOWER value never lowers the counter, and the next auto id
// stays collision-free. (ACE takes the last-inserted value instead — see AutoNumberSeedTests in
// LibRed.Core.Tests.)
public class AutoNumberSeedImmunityTests
{
    [Fact]
    public void Explicit_lower_value_does_not_lower_the_counter_or_cause_a_collision()
    {
        string path = Path.Combine(Path.GetTempPath(), $"anl-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery("CREATE TABLE Table1 (Field1 COUNTER CONSTRAINT PK_T1 PRIMARY KEY, Field2 TEXT(10))");
            for (char ch = 'A'; ch <= 'F'; ch++) e.ExecuteNonQuery($"INSERT INTO Table1 (Field2) VALUES ('{ch}')");   // 1..6
            e.ExecuteNonQuery("DELETE FROM Table1 WHERE Field1 = 3");
            e.ExecuteNonQuery("INSERT INTO Table1 (Field1, Field2) VALUES (3, 'C')");   // explicit lower value

            // Immunity: unlike ACE (which would re-derive 3+1=4 and collide), LibRed's counter stayed at 6, so
            // the next auto row gets 7 and inserts cleanly.
            e.ExecuteNonQuery("INSERT INTO Table1 (Field2) VALUES ('G')");
            var got = e.ExecuteQuery("SELECT Field1 FROM Table1 WHERE Field2 = 'G'").Rows.Single()[0];
            Assert.Equal(7, Convert.ToInt32(got));
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
