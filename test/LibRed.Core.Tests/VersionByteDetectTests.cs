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

    // The fallback has to hold for the whole life of the channel, not just its open. Every rollback re-derives
    // the format from the version byte then on disk, and re-deriving it STRICTLY contradicts the open: a file
    // that opened and read perfectly well threw on its first rollback, because 0x07 has no format class.
    [Fact]
    public void A_future_version_byte_survives_a_rollback()
    {
        string path = TemporaryDatabase.CreatePath("future-version-");
        try
        {
            Storage.DatabaseCreator.CreateEmpty(path);
            byte[] file = File.ReadAllBytes(path);
            file[JetFormatBase.VersionOffset] = 0x07;      // an ACE this build has never heard of
            File.WriteAllBytes(path, file);

            using var db = JetDatabase.Open(path, readOnly: false);
            Assert.Equal(JetVersion.Version17_2019, db.Format.Version);   // read as the latest known layout

            db.BeginTransaction();
            db.CreateTable("T", [new Catalog.ColumnSpec("Id", Catalog.JetDataType.Int32, 4, IsFixedLength: true)]);
            db.Rollback();

            Assert.Null(db.Catalog.FindTable("T"));
            Assert.Equal(JetVersion.Version17_2019, db.Format.Version);   // and still reads the same way
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // A raise is something ANOTHER connection can do to the file, and the version it leaves decides which
    // types this one will accept. A handle open across that change used to keep reporting the version the
    // file had when it opened, so it would refuse a column the file had just been made able to hold.
    [Fact]
    public void A_raise_by_another_handle_is_picked_up()
    {
        string path = TemporaryDatabase.CreatePath("raise-other-handle-");
        try
        {
            Storage.DatabaseCreator.CreateEmpty(path, version: 0x02);   // ACE 12

            using var watcher = JetDatabase.Open(path, readOnly: false);
            Assert.Equal(JetVersion.Version12_2007, watcher.Format.Version);

            using (var raiser = JetDatabase.Open(path, readOnly: false))
            {
                Assert.True(raiser.EnsureFormatAtLeast(JetVersion.Version16_2016));
                raiser.CreateTable("Big", [new Catalog.ColumnSpec("N", Catalog.JetDataType.Int64, 8, IsFixedLength: true)]);
            }

            // The other handle sees the new table, and the version that came with it.
            Assert.NotNull(watcher.Catalog.FindTable("Big"));
            Assert.Equal(JetVersion.Version16_2016, watcher.Format.Version);
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}
