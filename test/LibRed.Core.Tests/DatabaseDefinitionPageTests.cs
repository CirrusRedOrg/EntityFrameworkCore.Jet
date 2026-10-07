using LibRed;
using LibRed.Formats;
using Xunit;

namespace LibRed.Core.Tests;

public class DatabaseDefinitionPageTests
{
    /// <summary>Northwind's page 0.</summary>
    private static byte[] NorthwindPage0() =>
        File.ReadAllBytes(TestDatabases.NorthwindAccdb)[..TestDatabases.FormatOf(TestDatabases.NorthwindAccdb).PageSize];

    [Fact]
    public void Opens_accdb_and_detects_format()
    {
        using var db = JetDatabase.Open(TestDatabases.NorthwindAccdb);

        Assert.Equal(JetFormatBase.AceIdentifier, db.DefinitionPage.FormatIdentifier);
        Assert.Equal(0x02, db.DefinitionPage.JetVersion);
        Assert.Equal(JetVersion.Version12_2007, db.Format.Version);
        Assert.True(db.Format.IsAccdb);
        Assert.Equal(4096, db.Format.PageSize);
    }

    [Theory]
    [InlineData(nameof(TestDatabases.Ace16TypesAccdb))]       // ACE 17 (0x06)
    [InlineData(nameof(TestDatabases.BuiltInDataTypesAccdb))] // ACE 12 (0x02)
    [InlineData(nameof(TestDatabases.WideTableAccdb))]        // ACE 12 (0x02)
    public void Decodes_creation_date_matching_catalog(string fixture)
    {
        string path = (string)typeof(TestDatabases).GetProperty(fixture)!.GetValue(null)!;
        using var db = JetDatabase.Open(path);

        // The page-0 creation timestamp (obfuscated OLE double at 0x72) is decoded correctly when
        // it matches the earliest MSysObjects.DateCreate — the catalog's own unobfuscated record of
        // when the file's objects were first created. Verified across ACE 12–17.
        var msys = db.OpenTable("MSysObjects");
        int dcIdx = msys.Definition.FindColumn("DateCreate")!.Index;
        DateTime earliest = msys.Rows().Select(r => r[dcIdx] as DateTime?)
            .Where(d => d.HasValue).Min()!.Value;

        Assert.Equal(earliest, db.CreationDate, TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData(nameof(TestDatabases.NorthwindAccdb))]
    [InlineData(nameof(TestDatabases.Ace16TypesAccdb))]
    [InlineData(nameof(TestDatabases.BuiltInDataTypesAccdb))]
    public void Decodes_code_page_and_default_collation(string fixture)
    {
        string path = (string)typeof(TestDatabases).GetProperty(fixture)!.GetValue(null)!;
        using var db = JetDatabase.Open(path);

        // These fixtures are en-US General Legacy: code page 1252, LCID 1033, sort version 0,
        // no password (db key 0). Decoded from the obfuscated page-0 header via the fixed mask.
        Assert.Equal(1252, db.CodePage);
        Assert.Equal(1033, db.DefaultCollationLcid);
        Assert.Equal(0, db.DefaultCollationVersion);
        Assert.Equal(0, db.DefinitionPage.DatabaseKey);

        // The page-0 catalog-root pointer (0x20) names the MSysObjects TDEF, wherever that is.
        Assert.Equal(db.Catalog.RequireTable("MSysObjects").DefinitionPage, db.DefinitionPage.CatalogRootPage);
    }

    [Fact]
    public void Rejects_non_jet_file()
    {
        string bogus = TemporaryDatabase.CreatePath("libred_", ".bin");
        File.WriteAllBytes(bogus, new byte[4096]);
        try
        {
            Assert.Throws<NotSupportedException>(() => JetDatabase.Open(bogus));
        }
        finally
        {
            TemporaryDatabase.Delete(bogus);
        }
    }

    // A damaged file has to report damage: InvalidDataException is LibRed's signal for it, and
    // EndOfStreamException — which derives from IOException and shares no catchable base with it — is what a
    // truncated or empty file produced instead, from the two page-0 reads that run before any channel exists.
    [Theory]
    [InlineData(0)]        // empty
    [InlineData(64)]       // shorter than the page-0 header
    [InlineData(300)]      // header readable, shorter than one page
    public void A_truncated_file_reports_corruption_not_end_of_stream(int length)
    {
        string path = TemporaryDatabase.CreatePath($"truncated-{length}-");
        try
        {
            File.WriteAllBytes(path, NorthwindPage0()[..length]);

            Assert.ThrowsAny<InvalidDataException>(() => JetDatabase.Open(path).Dispose());
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The creation date is an OLE Automation double straight out of the file, decoded in the very first thing
    // an open does. NaN, infinity, or anything past DateTime's range escaped as ArgumentOutOfRangeException
    // from inside AddDays.
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1e18)]
    [InlineData(-1e18)]
    public void An_impossible_creation_date_reports_corruption(double days)
    {
        string path = TemporaryDatabase.CreatePath("baddate-");
        try
        {
            byte[] page0 = NorthwindPage0();
            JetFormatBase format = TestDatabases.FormatOf(TestDatabases.NorthwindAccdb);
            Pages.DatabaseDefinitionPage.WriteMasked(page0, format.CreationDateOffset, BitConverter.GetBytes(days), format);
            byte[] file = new byte[format.PageSize * 3];
            page0.CopyTo(file, 0);
            File.WriteAllBytes(path, file);

            Assert.ThrowsAny<InvalidDataException>(() => JetDatabase.Open(path).Dispose());
        }
        finally { TemporaryDatabase.Delete(path); }
    }
}