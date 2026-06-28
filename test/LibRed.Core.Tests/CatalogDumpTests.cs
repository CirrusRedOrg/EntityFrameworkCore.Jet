using System.Text;
using LibRed;
using LibRed.Catalog;
using Xunit;
using Xunit.Abstractions;

namespace LibRed.Core.Tests;

/// <summary>
/// Dumps every object in a database (user and system, in page order) with its properties,
/// columns/data types, and indexes (name, key columns, unique/primary, root page), then
/// asserts the whole rendering against a golden file. Pins the schema decode end to end;
/// regenerate the golden files if the decode intentionally changes.
/// Run with: dotnet test -l "console;verbosity=detailed" to see the dump.
/// </summary>
public class CatalogDumpTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    [Fact]
    public void Dump_northwind() => VerifyDump(TestDatabases.NorthwindAccdb, "catalog-dump.txt");

    [Fact]
    public void Dump_widetable() => VerifyDump(TestDatabases.WideTableAccdb, "widetable-dump.txt");

    private void VerifyDump(string databasePath, string goldenFile)
    {
        string actual = BuildDump(databasePath);
        _output.WriteLine(actual);

        string expected = Normalize(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Expected", goldenFile)));
        Assert.Equal(expected, actual);
    }

    private static string BuildDump(string path)
    {
        using var db = JetDatabase.Open(path);

        var sb = new StringBuilder();
        sb.Append($"Database: {db.DefinitionPage.FormatIdentifier}  ({db.Format.Version}, page size {db.Format.PageSize})\n");

        foreach (TableDef table in db.Catalog.Tables.OrderBy(t => t.DefinitionPage))
        {
            var tdef = db.ReadTableDefinition(table.DefinitionPage);

            sb.Append('\n');
            sb.Append($"{(table.IsSystem ? "[SYS] " : "      ")}{table.Name}\n");
            sb.Append($"        tdefPage={table.DefinitionPage}  type={tdef.TableType}  rows={tdef.RowCount}  columns={tdef.ColumnCount}  indexes={table.Indexes.Count}\n");

            foreach (ColumnDef c in table.Columns)
            {
                string store = c.IsFixedLength ? $"fixed@{c.FixedOffset}" : $"var#{c.VariableIndex}";
                string extra = c.IsAutoNumber ? " auto" : "";
                sb.Append($"          {c.Index,2}. {c.Name,-26} {c.Type,-9} len={c.Length,3} {store}{extra}\n");
            }

            if (table.Indexes.Count > 0)
            {
                sb.Append("        --- indexes ---\n");
                foreach (IndexDef ix in table.Indexes)
                {
                    string kind = ix.IsPrimaryKey ? "PK " : ix.IsUnique ? "U  " : "   ";
                    string cols = string.Join(", ", ix.Columns.Select(c => c.Column.Name + (c.Ascending ? "" : " DESC")));
                    sb.Append($"          {kind}{("\"" + ix.Name + "\""),-26} [{cols}] root={ix.RootPage} distinct={ix.UniqueValueCount}\n");
                }
            }

            var fks = db.Catalog.ForeignKeysOf(table.Name).OrderBy(f => f.Name).ToList();
            if (fks.Count > 0)
            {
                sb.Append("        --- foreign keys ---\n");
                foreach (ForeignKey fk in fks)
                {
                    string cols = string.Join(", ", fk.Columns.Select(c => c.Column));
                    string refcols = string.Join(", ", fk.Columns.Select(c => c.ReferencedColumn));
                    string extra = (fk.IsEnforced ? "" : " (not enforced)")
                                 + (fk.CascadeUpdate ? " cascadeUpdate" : "")
                                 + (fk.CascadeDelete ? " cascadeDelete" : "");
                    sb.Append($"          {("\"" + fk.Name + "\""),-44} [{cols}] -> {fk.ReferencedTable} [{refcols}]{extra}\n");
                }
            }
        }

        return Normalize(sb.ToString());
    }

    private static string Normalize(string s) => s.Replace("\r\n", "\n").TrimEnd() + "\n";
}
