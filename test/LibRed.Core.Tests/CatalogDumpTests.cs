using System.Text;
using LibRed;
using LibRed.Catalog;
using Xunit;
using Xunit.Abstractions;

namespace LibRed.Core.Tests;

/// <summary>
/// Not an assertion-heavy test: it dumps the whole catalog (every table, its
/// properties, and its columns with data types) so the schema decode can be eyeballed.
/// Run with: dotnet test -l "console;verbosity=detailed"
/// </summary>
public class CatalogDumpTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    [Fact]
    public void Dump_all_objects()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);

        var sb = new StringBuilder();
        sb.AppendLine($"Database: {db.DefinitionPage.FormatIdentifier}  ({db.Format.Version}, page size {db.Format.PageSize})");

        foreach (TableDef table in db.Catalog.Tables.OrderBy(t => t.IsSystem).ThenBy(t => t.Name))
        {
            // Re-read the TDEF page for per-table properties (row count, type, index count).
            var tdef = db.ReadTableDefinition(table.DefinitionPage);

            sb.AppendLine();
            sb.AppendLine($"{(table.IsSystem ? "[SYS] " : "      ")}{table.Name}");
            sb.AppendLine($"        tdefPage={table.DefinitionPage}  type={tdef.TableType}  rows={tdef.RowCount}  columns={tdef.ColumnCount}  indexes={tdef.IndexCount}");

            foreach (ColumnDef c in table.Columns)
            {
                string store = c.IsFixedLength ? $"fixed@{c.FixedOffset}" : $"var#{c.VariableIndex}";
                string extra = c.IsAutoNumber ? " auto" : "";
                sb.AppendLine($"          {c.Index,2}. {c.Name,-26} {c.Type,-9} len={c.Length,3} {store}{extra}");
            }
        }

        _output.WriteLine(sb.ToString());

        // Light sanity assertions so this still functions as a test.
        Assert.True(db.Catalog.UserTables.Count() >= 12);
        Assert.All(db.Catalog.Tables, t => Assert.NotEmpty(t.Columns));
    }
}
