using LibRed;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;
using System.Buffers.Binary;
using Xunit;

namespace LibRed.Core.Tests;

public class TableDefinitionPageTests
{
    [Fact]
    public void Reads_MSysObjects_table_definition()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);

        var tdef = db.ReadTableDefinition(db.DefinitionPage.CatalogRootPage);

        Assert.Equal(TableType.System, tdef.TableType);
        Assert.Equal(17, tdef.ColumnCount);
        Assert.Equal(17, tdef.Columns.Count);
        Assert.Equal(2, tdef.IndexCount);
        Assert.True(tdef.RowCount > 0);
        Assert.Equal(0, tdef.NextDefinitionPage); // fits in a single page
    }

    [Fact]
    public void Decodes_MSysObjects_column_names()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);

        var tdef = db.ReadTableDefinition(db.DefinitionPage.CatalogRootPage);
        var names = tdef.Columns.Select(c => c.Name).ToList();

        // Known MSysObjects columns (a few stable ones, observed in the file).
        Assert.Contains("Name", names);
        Assert.Contains("Type", names);
        Assert.Contains("Id", names);
        Assert.Contains("Flags", names);

        // Every column has a non-empty name and a recognised data type.
        Assert.All(tdef.Columns, c =>
        {
            Assert.False(string.IsNullOrEmpty(c.Name));
            Assert.True(Enum.IsDefined(c.Type));
        });
    }

    [Theory]
    [InlineData("columns")]
    [InlineData("variable-columns")]
    [InlineData("real-indexes")]
    [InlineData("logical-indexes")]
    public void Rejects_counts_outside_jet_ace_table_geometry_before_parsing(string field)
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);
        JetFormatBase format = db.Format;
        byte[] page = TableDefinition.Build(format, TableType.User,
            [new ColumnSpec("C", JetDataType.Int32, 4, IsFixedLength: true)], Collation.GeneralLegacy).Page;

        switch (field)
        {
            case "columns":
                BinaryPrimitives.WriteUInt16LittleEndian(
                    page.AsSpan(format.TdefColumnCountOffset, 2), (ushort)(format.MaxColumnsPerTable + 1));
                break;
            case "variable-columns":
                BinaryPrimitives.WriteUInt16LittleEndian(
                    page.AsSpan(format.TdefVariableColumnsOffset, 2), (ushort)(format.MaxColumnsPerTable + 1));
                break;
            case "real-indexes":
                BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(format.TdefIndexCountOffset, 4), format.MaxIndexesPerTable + 1);
                break;
            case "logical-indexes":
                BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(format.TdefLogicalIndexCountOffset, 4), -1);
                break;
        }

        var definition = new TableDefinition();
        Assert.Throws<InvalidDataException>(() => definition.Read(new PageBuffer(page, 99), format));
    }
}