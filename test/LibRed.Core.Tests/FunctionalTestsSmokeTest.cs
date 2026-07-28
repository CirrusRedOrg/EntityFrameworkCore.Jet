using System.Runtime.CompilerServices;
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

    [Fact]
    public void Reads_the_builtin_datatypes_database()
    {
        var (tables, rows, failures) = Scan(TestDatabases.BuiltInDataTypesAccdb);

        _output.WriteLine($"tables={tables} rows={rows}");
        Assert.Empty(failures);
        Assert.True(tables > 0);
    }

    [Fact]
    public void Reads_the_full_functional_test_corpus_when_present()
    {
        // A ~70 MB local-only corpus (gitignored); present on a dev box that has run the
        // EFCore.Jet functional tests, absent on CI.
        string dir = Path.Combine(SourceDirectory(), "FunctionalTestsData");
        var files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.accdb") : [];
        if (files.Length == 0)
        {
            _output.WriteLine("corpus not present — skipping");
            return;
        }

        long totalTables = 0, totalRows = 0;
        var allFailures = new List<string>();
        foreach (string file in files)
        {
            var (tables, rows, failures) = Scan(file);
            totalTables += tables;
            totalRows += rows;
            allFailures.AddRange(failures);
        }

        _output.WriteLine($"files={files.Length} tables={totalTables} rows={totalRows}");
        foreach (string failure in allFailures)
            _output.WriteLine(failure);
        Assert.Empty(allFailures);
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

    private static string SourceDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
