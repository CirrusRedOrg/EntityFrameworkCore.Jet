using System.Text;
using LibRed;
using LibRed.Catalog;
using Xunit;
using Xunit.Abstractions;

namespace LibRed.Core.Tests;

/// <summary>
/// Dumps every object in the database (user and system tables, in page order) with its
/// properties and columns/data types, then asserts the whole rendering against a golden
/// file (Expected/catalog-dump.txt). This pins the schema decode end to end; regenerate
/// the golden file if the decode intentionally changes.
/// Run with: dotnet test -l "console;verbosity=detailed" to see the dump.
/// </summary>
public class CatalogDumpTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    [Fact]
    public void Dump_all_objects()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);

        var sb = new StringBuilder();
        sb.Append($"Database: {db.DefinitionPage.FormatIdentifier}  ({db.Format.Version}, page size {db.Format.PageSize})\n");

        foreach (TableDef table in db.Catalog.Tables.OrderBy(t => t.DefinitionPage))
        {
            // Re-read the TDEF page for per-table properties (row count, type, index count).
            var tdef = db.ReadTableDefinition(table.DefinitionPage);

            sb.Append('\n');
            sb.Append($"{(table.IsSystem ? "[SYS] " : "      ")}{table.Name}\n");
            sb.Append($"        tdefPage={table.DefinitionPage}  type={tdef.TableType}  rows={tdef.RowCount}  columns={tdef.ColumnCount}  indexes={tdef.IndexCount}\n");

            foreach (ColumnDef c in table.Columns)
            {
                string store = c.IsFixedLength ? $"fixed@{c.FixedOffset}" : $"var#{c.VariableIndex}";
                string extra = c.IsAutoNumber ? " auto" : "";
                sb.Append($"          {c.Index,2}. {c.Name,-26} {c.Type,-9} len={c.Length,3} {store}{extra}\n");
            }
        }

        string actual = Normalize(sb.ToString());
        _output.WriteLine(actual);

        string expectedPath = Path.Combine(AppContext.BaseDirectory, "Expected", "catalog-dump.txt");
        string expected = Normalize(File.ReadAllText(expectedPath));

        Assert.Equal(expected, actual);
    }

    private static string Normalize(string s) => s.Replace("\r\n", "\n").TrimEnd() + "\n";
}
