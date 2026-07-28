using System.Text;
using LibRed.Formats;
using Xunit;

namespace LibRed.Core.Tests;

public class VersionByteDetectTests
{
    // A minimal page-0 header: ACE identifier at 0x04, a given version byte at 0x14, and an engine-version
    // string at 0x9C. Enough for JetFormatBase.Detect (which reads through 0x9C).
    private static MemoryStream Header(byte versionByte, string engine = "4.0", string identifier = "Standard ACE DB")
    {
        byte[] page = new byte[4096];
        Encoding.ASCII.GetBytes(identifier).CopyTo(page, JetFormatBase.FormatIdentifierOffset);
        page[JetFormatBase.VersionOffset] = versionByte;
        Encoding.ASCII.GetBytes(engine).CopyTo(page, JetFormatBase.EngineVersionOffset);
        return new MemoryStream(page, writable: false);
    }

    [Fact]
    public void Byte_0x04_maps_to_the_2010_format()
    {
        // ACE 15 (Access 2013) is byte-identical to 0x03 / ACE 14; no clone class.
        var f = JetFormatBase.FromVersionByte(0x04);
        Assert.Equal(JetVersion.Version14_2010, f.Version);
        Assert.True(f.IsAccdb);
        Assert.Equal(4096, f.PageSize);
    }

    [Fact]
    public void Unknown_byte_on_a_4_0_accdb_falls_back_to_latest_ACE()
    {
        // A future ACE byte (0x07) on a file still carrying the "4.0" engine string reads as the latest known ACE.
        var f = JetFormatBase.Detect(Header(0x07, engine: "4.0"));
        Assert.Equal(JetVersion.Version17_2019, f.Version);
        Assert.True(f.IsAccdb);
    }

    [Fact]
    public void Unknown_byte_without_the_4_0_engine_string_is_rejected()
    {
        // A different engine string ("5.0") must NOT be mistaken for ACE — the 4.0 guard rejects it.
        Assert.Throws<NotSupportedException>(() => JetFormatBase.Detect(Header(0x07, engine: "5.0")));
    }

    [Fact]
    public void Known_bytes_still_detect_normally()
    {
        Assert.Equal(JetVersion.Version12_2007, JetFormatBase.Detect(Header(0x02)).Version);
        Assert.Equal(JetVersion.Version16_2016, JetFormatBase.Detect(Header(0x05)).Version);
        Assert.Equal(JetVersion.Version17_2019, JetFormatBase.Detect(Header(0x06)).Version);
    }

    [Theory]
    [InlineData(0x02, "Standard Jet DB")]
    [InlineData(0x01, "Standard ACE DB")]
    [InlineData(0x02, "Jet System DB")]
    public void Mismatched_identifier_and_version_are_rejected(byte version, string identifier)
    {
        Assert.Throws<NotSupportedException>(() => JetFormatBase.Detect(Header(version, identifier: identifier)));
    }

    [Fact]
    public void Jet3_is_rejected_until_its_distinct_layout_is_implemented()
    {
        Assert.Throws<NotSupportedException>(() =>
            JetFormatBase.Detect(Header(0x00, identifier: "Standard Jet DB")));
        Assert.Throws<NotSupportedException>(() => JetFormatBase.FromVersionByte(0x00));
    }

    [Theory]
    [InlineData("Standard Jet DB")]
    [InlineData("Jet System DB")]
    public void Supported_jet4_identifiers_still_detect(string identifier)
    {
        var format = JetFormatBase.Detect(Header(0x01, identifier: identifier));
        Assert.Equal(JetVersion.Version4, format.Version);
        Assert.False(format.IsAccdb);
    }
}
