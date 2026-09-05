using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class GuidLiteralTests
{
    private static string Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "guidlit-");
        return path;
    }

    // Access writes GUID values as {8-4-4-4-12} brace literals. LibRed parses the literal, stores it, and
    // reads it back as a System.Guid — and it round-trips as a primary key (uses the GUID index-key path).
    [Fact]
    public void Guid_brace_literal_inserts_and_round_trips()
    {
        const string g = "{3c56082a-005a-4ffb-a9cf-f5ebd641e07d}";
        var expected = Guid.Parse(g);
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery("CREATE TABLE `EmailTemplate` (`Id` GUID PRIMARY KEY, `TemplateType` int)");
            Assert.Equal(1, e.ExecuteNonQuery($"INSERT INTO `EmailTemplate` (`Id`, `TemplateType`) VALUES ({g}, 0)"));

            // Read the value back.
            var row = e.ExecuteQuery("SELECT `Id`, `TemplateType` FROM `EmailTemplate`").Rows.Single();
            Assert.Equal(expected, row[0]);
            Assert.Equal(0, Convert.ToInt32(row[1]));

            // And the literal works in a WHERE against the GUID key.
            var hit = e.ExecuteQuery($"SELECT `TemplateType` FROM `EmailTemplate` WHERE `Id` = {g}").Rows.Single();
            Assert.Equal(0, Convert.ToInt32(hit[0]));
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
