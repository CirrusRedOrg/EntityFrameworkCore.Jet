using System.Data.OleDb;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Engine.Tests;

// GUID maps to a VARIABLE-length column, matching what ACE's own DDL writes — the same rule as BIGINT
// (see BigIntCreatedDatabaseAccessTests and docs/format/data-types.md), which GUID was missing.
//
// ACE declares every GUID column variable whatever the table's shape, measured on the ACE side in
// LibRed.Core.Tests' GuidColumnStorageAccessTests. Unlike BIGINT this was not a wrong-value bug — ACE
// reads a fixed GUID column back correctly — but 16 fixed bytes per column spend record budget ACE does
// not spend, so a wide GUID table ACE creates happily used to exceed LibRed's declared-record limit.
[Collection(AceCollection.Name)]
public class GuidColumnStorageAccessTests : TempDatabaseTest
{
    [Fact]
    public void Guid_and_uniqueidentifier_both_declare_a_variable_column()
    {
        string path = Copy("guid-mapping-");
        using var database = JetDatabase.Open(path, readOnly: false);
        new QueryEngine(database).ExecuteNonQuery(
            "CREATE TABLE `W` (`Id` INTEGER PRIMARY KEY, `G` GUID NULL, `H` UNIQUEIDENTIFIER NULL)");

        TableDef table = database.Catalog.FindTable("W")!;
        Assert.All(table.Columns.Where(c => c.Name is "G" or "H"), column =>
        {
            Assert.False(column.IsFixedLength);
            Assert.Equal(16, column.Length);
        });
        Assert.Equal(4, table.Columns.Where(c => c.IsFixedLength).Sum(c => c.Length));   // only Id
    }

    // The point of the change: a wide GUID table ACE creates without complaint is one LibRed now creates
    // too, where 250 fixed columns would have been 4000 bytes of record budget.
    [Fact]
    public void A_wide_guid_table_no_longer_spends_fixed_record_budget()
    {
        var value = Guid.Parse("11111111-1111-1111-1111-111111111111");
        string path = Copy("guid-wide-");
        using (var database = JetDatabase.Open(path, readOnly: false))
        {
            var engine = new QueryEngine(database);
            engine.ExecuteNonQuery("CREATE TABLE `W` (`Id` INTEGER PRIMARY KEY, " + string.Join(", ",
                Enumerable.Range(0, 250).Select(i => $"`G{i}` GUID NULL")) + ")");
            engine.ExecuteNonQuery("INSERT INTO `W` (`Id`, `G0`) VALUES (@id, @g)",
                new Dictionary<string, object?> { ["id"] = 1, ["g"] = value });
        }

        using OleDbConnection connection = AceTestDatabase.Open(path);
        using OleDbCommand read = connection.CreateCommand();
        read.CommandText = "SELECT G0 FROM W WHERE Id = 1";
        Assert.Equal(value, read.ExecuteScalar());
    }

    private static string Copy(string prefix) => TemporaryDatabase.CopyPath(
        Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), prefix);
}
