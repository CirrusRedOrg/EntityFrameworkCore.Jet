using LibRed.Catalog;
using LibRed.Formats;
using Xunit;

namespace LibRed.Core.Tests;

// Every DOCUMENTED column-descriptor flag is modelled and composed from ColumnDef when a column is created.
public class ColumnDescriptorFlagTests
{
    [Fact]
    public void BuildColumnDescriptor_composes_the_documented_flags_from_the_model()
    {
        JetFormatBase format = JetFormatBase.FromVersionByte(0x02); // ACE 12

        var col = new ColumnDef
        {
            Name = "H", Type = JetDataType.Memo, Index = 0, ColumnId = 3, Length = 4,
            IsUpdatable = true, IsGuidAutoNumber = true, IsHyperlink = true,
            SupportsCompressedUnicode = true, IsCalculated = true,
        };

        byte[] d = TableDefinition.BuildColumnDescriptor(col, format);

        Assert.Equal(ColumnFlags.Updatable | ColumnFlags.GuidAutoNumber | ColumnFlags.Hyperlink,
            (ColumnFlags)d[format.ColumnFlagsOffset]);
        Assert.Equal(ColumnExtendedFlags.CompressedUnicode | ColumnExtendedFlags.Calculated,
            (ColumnExtendedFlags)d[format.ColumnExtendedFlagsOffset]);
    }

    [Fact]
    public void A_fresh_column_is_updatable_with_no_other_documented_flags()
    {
        JetFormatBase format = JetFormatBase.FromVersionByte(0x02);
        var col = new ColumnDef { Name = "C", Type = JetDataType.Int32, Index = 0, ColumnId = 0, Length = 4, IsFixedLength = true };

        byte[] d = TableDefinition.BuildColumnDescriptor(col, format);

        Assert.Equal(ColumnFlags.Updatable | ColumnFlags.FixedLength, (ColumnFlags)d[format.ColumnFlagsOffset]);
        Assert.Equal(0, d[format.ColumnExtendedFlagsOffset]);
    }
}