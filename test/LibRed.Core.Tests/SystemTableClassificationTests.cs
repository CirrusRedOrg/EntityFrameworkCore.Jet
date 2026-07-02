using LibRed;
using Xunit;

namespace LibRed.Core.Tests;

public class SystemTableClassificationTests
{
    [Fact]
    public void Hidden_and_dual_tables_are_excluded_from_user_tables()
    {
        // BuiltInDataTypes.accdb was created by EFCore.Jet, so it carries the hidden #Dual helper
        // (flags 0x08, '#' prefix). If #Dual counts as a user table, HasTables() reports an otherwise
        // schema-less DB as "already populated", and EnsureCreated skips creating the model's tables.
        using var db = JetDatabase.Open(TestDatabases.BuiltInDataTypesAccdb, readOnly: true);
        var userTables = db.Catalog.UserTables.Select(t => t.Name).ToList();

        Assert.DoesNotContain("#Dual", userTables);
        Assert.DoesNotContain(userTables, n => n.StartsWith("MSys", StringComparison.Ordinal));
        Assert.DoesNotContain(userTables, n => n.StartsWith('#') || n.StartsWith('~'));

        // Real model tables are still user tables.
        Assert.Contains("BuiltInDataTypes", userTables);
        Assert.Contains("Animal", userTables);
    }
}
