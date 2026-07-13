using LibRed.Catalog;
using LibRed.Formats;
using Xunit;

namespace LibRed.Core.Tests;

// Every DOCUMENTED column-descriptor field is modelled and round-trips explicitly (read into ColumnDef,
// written from it). Only the genuinely reserved/unknown bytes ride through RawDescriptor: the reserved words
// at 0x03/0x11 and the undocumented bits of the two flag bytes (0x0F/0x10). This pins that the documented
// flag bits — GUID-autonumber (0x40), hyperlink (0x80), compressed-Unicode (0x10/0x01) and calculated
// (0x10/0xC0) — survive a rebuild rather than being dropped when composed from the model.
public class ColumnDescriptorFlagTests
{
    [Fact]
    public void BuildColumnDescriptor_round_trips_documented_flags_and_reserved_bytes()
    {
        JetFormatBase format = JetFormatBase.FromVersionByte(0x02); // ACE 12
        int size = format.ColumnDescriptorSize;

        // A crafted original: updatable + GUID-autonumber + hyperlink, plus an UNDOCUMENTED bit (0x08) in 0x0F;
        // compressed-Unicode + calculated in 0x10; and non-zero RESERVED words at 0x03 and 0x11.
        var raw = new byte[size];
        raw[format.ColumnFlagsOffset] = (byte)(ColumnFlags(updatable: true, guid: true, hyperlink: true) | 0x08);
        raw[format.ColumnExtendedFlagsOffset] =
            JetFormatBase.ColumnExtFlagCompressedUnicode | JetFormatBase.ColumnExtFlagCalculated;
        raw[0x03] = 0xAB; raw[0x04] = 0xCD;                                  // reserved word
        raw[0x11] = 0xDE; raw[0x12] = 0xAD; raw[0x13] = 0xBE; raw[0x14] = 0xEF; // reserved word

        var col = new ColumnDef
        {
            Name = "H", Type = JetDataType.Memo, Index = 0, ColumnId = 3, Length = 4,
            IsUpdatable = true, IsGuidAutoNumber = true, IsHyperlink = true,
            SupportsCompressedUnicode = true, IsCalculated = true,
            RawDescriptor = raw,
        };

        byte[] d = TdefBuilder.BuildColumnDescriptor(col, format);

        // Both flag bytes reproduce the original exactly — documented bits composed from the model, the
        // undocumented 0x08 bit preserved from raw.
        Assert.Equal(raw[format.ColumnFlagsOffset], d[format.ColumnFlagsOffset]);
        Assert.Equal(raw[format.ColumnExtendedFlagsOffset], d[format.ColumnExtendedFlagsOffset]);

        // Reserved/unknown words survive verbatim.
        Assert.Equal(raw[0x03], d[0x03]); Assert.Equal(raw[0x04], d[0x04]);
        Assert.Equal(raw[0x11], d[0x11]); Assert.Equal(raw[0x12], d[0x12]);
        Assert.Equal(raw[0x13], d[0x13]); Assert.Equal(raw[0x14], d[0x14]);
    }

    [Fact]
    public void A_fresh_column_is_updatable_with_no_other_documented_flags()
    {
        JetFormatBase format = JetFormatBase.FromVersionByte(0x02);
        var col = new ColumnDef { Name = "C", Type = JetDataType.Int32, Index = 0, ColumnId = 0, Length = 4, IsFixedLength = true };

        byte[] d = TdefBuilder.BuildColumnDescriptor(col, format);

        Assert.Equal(JetFormatBase.ColumnFlagUpdatable | JetFormatBase.ColumnFlagFixedLength, d[format.ColumnFlagsOffset]);
        Assert.Equal(0, d[format.ColumnExtendedFlagsOffset]);
    }

    private static byte ColumnFlags(bool updatable, bool guid, bool hyperlink) => (byte)(
        (updatable ? JetFormatBase.ColumnFlagUpdatable : 0)
        | (guid ? JetFormatBase.ColumnFlagGuidAutoNumber : 0)
        | (hyperlink ? JetFormatBase.ColumnFlagHyperlink : 0));
}
