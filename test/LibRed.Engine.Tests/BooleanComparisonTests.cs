using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class BooleanComparisonTests
{
    // EF Core maps a CLR bool to a numeric (smallint) column, so a boolean predicate must compare
    // equal to the stored numeric value: `BoolA = (NullableBoolB IS NOT NULL)` (from the EF
    // NullSemantics suite) requires bool ↔ numeric equality.
    [Fact]
    public void Numeric_bool_column_compares_equal_to_a_boolean_predicate()
    {
        string path = Path.Combine(Path.GetTempPath(), $"boolcmp-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var engine = new QueryEngine(db);

            engine.ExecuteNonQuery("CREATE TABLE `Ent` (`Id` INTEGER PRIMARY KEY, `BoolA` SMALLINT, `NullableBoolB` SMALLINT NULL)");
            engine.ExecuteNonQuery("INSERT INTO `Ent` (`Id`, `BoolA`, `NullableBoolB`) VALUES (1, TRUE, TRUE)");   // true  == not-null
            engine.ExecuteNonQuery("INSERT INTO `Ent` (`Id`, `BoolA`, `NullableBoolB`) VALUES (2, FALSE, NULL)");  // false == null
            engine.ExecuteNonQuery("INSERT INTO `Ent` (`Id`, `BoolA`, `NullableBoolB`) VALUES (3, TRUE, NULL)");
            engine.ExecuteNonQuery("INSERT INTO `Ent` (`Id`, `BoolA`, `NullableBoolB`) VALUES (4, FALSE, TRUE)");

            var ids = engine.ExecuteQuery(
                "SELECT `Id` FROM `Ent` WHERE `BoolA` = (`NullableBoolB` IS NOT NULL) ORDER BY `Id`")
                .Rows.Select(r => Convert.ToInt32(r[0])).ToList();

            Assert.Equal([1, 2], ids);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
