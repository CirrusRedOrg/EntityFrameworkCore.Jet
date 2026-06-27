using LibRed;
using Xunit;

namespace LibRed.Core.Tests;

public class MultiPageTableDefinitionTests
{
    [Fact]
    public void Parses_definition_that_spans_multiple_pages()
    {
        using var db = JetDatabase.Open(TestDatabases.WideTableAccdb);

        var def = db.Catalog.FindTable("WideTable");
        Assert.NotNull(def);

        var tdef = db.ReadTableDefinition(def!.DefinitionPage);

        // The definition continues onto another page.
        Assert.NotEqual(0, tdef.NextDefinitionPage);
        Assert.Equal(200, tdef.ColumnCount);
        Assert.Equal(200, tdef.Columns.Count);

        // Column names continue seamlessly across the page boundary (C000..C199).
        Assert.Equal(
            Enumerable.Range(0, 200).Select(i => $"C{i:D3}"),
            tdef.Columns.Select(c => c.Name));
    }

    [Fact]
    public void Reads_rows_from_a_wide_table()
    {
        using var db = JetDatabase.Open(TestDatabases.WideTableAccdb);

        var table = db.OpenTable("WideTable");
        int Idx(string n) => table.Definition.Columns.First(c => c.Name == n).Index;

        var row = Assert.Single(table.Rows());
        Assert.Equal(1000, row[Idx("C000")]);
        Assert.Equal(1100, row[Idx("C100")]);
        Assert.Equal(1199, row[Idx("C199")]);
        Assert.Null(row[Idx("C001")]); // not inserted
    }
}
