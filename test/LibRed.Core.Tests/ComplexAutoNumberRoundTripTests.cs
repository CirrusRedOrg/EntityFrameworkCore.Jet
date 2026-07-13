using System.Buffers.Binary;
using LibRed.Catalog;
using LibRed.Formats;
using LibRed.IO;
using LibRed.Pages;
using Xunit;

namespace LibRed.Core.Tests;

// The TDEF header's complex-type AutoNumber high-water (0x1C) is a documented, meaningful field (the next id
// for a complex multi-value/attachment column). LibRed now reads it into the model and writes it explicitly
// through TdefBuilder, rather than leaving it to the raw surgery path. It is 0 for every table LibRed creates
// (no complex columns), so this pins the read/write path with a non-zero value directly.
public class ComplexAutoNumberRoundTripTests
{
    [Fact]
    public void Complex_autonumber_high_water_is_written_and_read_back()
    {
        JetFormatBase format = JetFormatBase.FromVersionByte(0x02); // ACE 12
        var specs = new[] { new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true) };

        byte[] page = TdefBuilder.Build(format, TableType.User, specs, complexAutoNumber: 42).Page;

        // Written explicitly at 0x1C…
        Assert.Equal(42, BinaryPrimitives.ReadInt32LittleEndian(page.AsSpan(format.TdefComplexAutoNumberOffset, 4)));

        // …and read back into the model.
        var tdef = new TableDefinitionPage();
        tdef.Read(new PageBuffer(page, 0), format);
        Assert.Equal(42, tdef.ComplexAutoNumber);
    }

    [Fact]
    public void Complex_autonumber_defaults_to_zero_for_an_ordinary_table()
    {
        JetFormatBase format = JetFormatBase.FromVersionByte(0x02);
        var specs = new[] { new ColumnSpec("Id", JetDataType.Int32, 4, IsFixedLength: true) };

        byte[] page = TdefBuilder.Build(format, TableType.User, specs).Page;

        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(page.AsSpan(format.TdefComplexAutoNumberOffset, 4)));
    }
}
