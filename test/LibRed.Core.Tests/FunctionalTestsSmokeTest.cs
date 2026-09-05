using LibRed;
using LibRed.Catalog;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>
/// Smoke test: open a database, enumerate every user table, and full-scan every row,
/// asserting nothing throws and every column has a recognised data type. Run against
/// EF Core's own functional-test databases — broad, adversarial schema coverage.
/// </summary>
public class FunctionalTestsSmokeTest(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    public static TheoryData<string> TrackedSchemaCorpus =>
    [
        TestDatabases.NorthwindAccdb,
        TestDatabases.BuiltInDataTypesAccdb,
        TestDatabases.EverythingIsBytesAccdb,
        TestDatabases.DecimalsAccdb,
        TestDatabases.WideTableAccdb,
        TestDatabases.Ace16TypesAccdb,
    ];

    [Theory]
    [MemberData(nameof(TrackedSchemaCorpus))]
    public void Reads_every_table_and_row_in_the_tracked_schema_corpus(string path)
    {
        var (tables, rows, failures) = Scan(path);

        _output.WriteLine($"file={Path.GetFileName(path)} tables={tables} rows={rows}");
        Assert.Empty(failures);
        Assert.True(tables > 0);
    }

    private static (int Tables, long Rows, List<string> Failures) Scan(string path)
    {
        var failures = new List<string>();
        int tables = 0;
        long rows = 0;
        string name = Path.GetFileNameWithoutExtension(path);

        using var db = JetDatabase.Open(path);
        foreach (TableDef table in db.Catalog.UserTables)
        {
            tables++;

            foreach (ColumnDef column in table.Columns)
                if (!Enum.IsDefined(column.Type))
                    failures.Add($"{name}.{table.Name}.{column.Name}: unknown type 0x{(byte)column.Type:X2}");

            try
            {
                foreach (var _ in db.OpenTable(table.Name).Rows())
                    rows++;
            }
            catch (Exception ex)
            {
                failures.Add($"{name}.{table.Name}: {ex.GetType().Name} {ex.Message.Split('\n')[0]}");
            }
        }

        return (tables, rows, failures);
    }
}
