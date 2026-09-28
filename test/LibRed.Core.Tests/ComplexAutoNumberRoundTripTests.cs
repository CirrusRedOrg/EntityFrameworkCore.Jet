using System.Buffers.Binary;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;
using Xunit;

namespace LibRed.Core.Tests;

// The TDEF header's complex-type AutoNumber high-water (0x1C) is a documented, meaningful field (the next id
// for a complex multi-value/attachment column). LibRed reads it into the model, and TdefBuilder writes 0 there
// for every table it creates (no complex columns), so the read path is pinned with a non-zero value directly.
public class ComplexAutoNumberRoundTripTests
{
    [Fact]
    public void Complex_autonumber_high_water_is_read_back()
    {
        JetFormatBase format = JetFormatBase.FromVersionByte(0x02); // ACE 12
        var specs = new[] { new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true) };

        byte[] page = TdefBuilder.Build(format, TableType.User, specs, Collation.GeneralLegacy).Page;
        BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(format.TdefComplexAutoNumberOffset, 4), 42);

        var tdef = new TableDefinitionPage();
        tdef.Read(new PageBuffer(page, 0), format);
        Assert.Equal(42, tdef.ComplexAutoNumber);
    }

    [Fact]
    public void Complex_autonumber_defaults_to_zero_for_an_ordinary_table()
    {
        JetFormatBase format = JetFormatBase.FromVersionByte(0x02);
        var specs = new[] { new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true) };

        byte[] page = TdefBuilder.Build(format, TableType.User, specs, Collation.GeneralLegacy).Page;

        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(page.AsSpan(format.TdefComplexAutoNumberOffset, 4)));
    }
}
