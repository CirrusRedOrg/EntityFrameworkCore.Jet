using LibRed;
using LibRed.Catalog;
using LibRed.Storage;
using Xunit;

namespace LibRed.Core.Tests;

/// <summary>Synthesising a new database's pages from scratch (native, DAO/ADOX-free creation).</summary>
public class DatabaseCreatorTests
{
    [Theory]
    [InlineData(nameof(TestDatabases.NorthwindAccdb))]
    [InlineData(nameof(TestDatabases.BuiltInDataTypesAccdb))]
    public void Synthesized_page0_header_matches_a_real_file(string fixture)
    {
        string path = (string)typeof(TestDatabases).GetProperty(fixture)!.GetValue(null)!;
        byte[] real = File.ReadAllBytes(path);
        using var db = JetDatabase.Open(path);
        var dp = db.DefinitionPage;

        byte[] synth = DatabaseCreator.BuildDefinitionPage(
            dp.JetVersion, isAccdb: true, dp.CodePage, dp.Collation,
            (dp.DatabaseCreationDate - new DateTime(1899, 12, 30)).TotalDays);

        // The whole page-0 header (0x00–0x9F: identifier, version, the masked field block, and the
        // cleartext "4.0" tail) is reproduced byte-for-byte. (0xA0–0xDFF is zero; 0xE00+ is an
        // undecoded usage structure LibRed doesn't need — not asserted here.)
        Assert.Equal(real[0x00..0xA0], synth[0x00..0xA0]);
    }

    [Fact]
    public void Creates_an_empty_database_that_round_trips_a_user_table()
    {
        string path = TemporaryDatabase.CreatePath("libred_create_");
        try
        {
            DatabaseCreator.CreateEmpty(path);

            // Freshly created: opens, and the two bootstrap system tables are in the catalog.
            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                Assert.Equal(2, db.DefinitionPage.CatalogRootPage);
                Assert.NotNull(db.OpenTable("MSysObjects"));
                Assert.NotNull(db.OpenTable("MSysACEs"));

                // Create a user table, insert, and read back — through the ordinary writers.
                db.CreateTable("People", new[]
                {
                    new ColumnSpec("Id", LibRed.Catalog.JetDataType.Int32, 4, true),
                    new ColumnSpec("Name", LibRed.Catalog.JetDataType.Text, 100, false),
                });
                db.Catalog.Invalidate();
                var people = db.OpenTable("People");
                people.Insert([1, "Ada"]);
                people.Insert([2, "Alan"]);
            }

            // Reopen from scratch and verify the data survived.
            using (var db = JetDatabase.Open(path))
            {
                Assert.Contains("People", db.Catalog.UserTables.Select(t => t.Name));
                var rows = db.OpenTable("People").Rows().ToList();
                Assert.Equal(2, rows.Count);
                var names = rows.Select(r => r[db.OpenTable("People").Definition.FindColumn("Name")!.Index]).ToList();
                Assert.Contains("Ada", names);
                Assert.Contains("Alan", names);
            }
        }
        finally { if (File.Exists(path)) TemporaryDatabase.Delete(path); }
    }

    // Jet 4 — the Access 2000 / 2002-2003 .mdb — is creatable too, not just the ACCDB versions. The identifier
    // has to follow the version byte: "Standard Jet DB" with 0x01, because Detect refuses a mismatched pair
    // and the file would then be one this method wrote and could not reopen.
    [Fact]
    public void Creates_a_jet4_database_that_reopens()
    {
        string path = TemporaryDatabase.CreatePath("libred_jet4_")
            .Replace(".accdb", ".mdb", StringComparison.OrdinalIgnoreCase);
        try
        {
            DatabaseCreator.CreateEmpty(path, version: 0x01);

            using (var db = JetDatabase.Open(path, readOnly: false))
            {
                Assert.Equal("Standard Jet DB", db.DefinitionPage.FormatIdentifier);
                Assert.Equal(0x01, db.DefinitionPage.JetVersion);

                db.CreateTable("People", [
                    new ColumnSpec("Id", JetDataType.Int32, 4, true),
                    new ColumnSpec("Name", JetDataType.Text, 100, false)]);
                db.Catalog.Invalidate();
                db.OpenTable("People").Insert([1, "Ada"]);
            }

            using (var db = JetDatabase.Open(path))
            {
                Table people = db.OpenTable("People");
                Assert.Equal("Ada", people.Rows().Single()[people.Definition.FindColumn("Name")!.Index]);
            }
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    // The one byte that moves with the version: 0x15 is 0x01 only for the 2010 format. Both Jet 4 files and
    // ACE 12 leave it zero -- measured on an Access 2000 and an Access 2002 .mdb, which are byte-identical
    // from 0x14 to 0x23 and differ only in their catalog.
    [Theory]
    [InlineData((byte)0x01, "Standard Jet DB", (byte)0x00)]
    [InlineData((byte)0x02, "Standard ACE DB", (byte)0x00)]
    [InlineData((byte)0x03, "Standard ACE DB", (byte)0x01)]
    public void Synthesized_page0_pairs_the_identifier_with_the_version(byte version, string id, byte minor)
    {
        byte[] page = DatabaseCreator.BuildDefinitionPage(
            version, isAccdb: id.StartsWith("Standard ACE", StringComparison.Ordinal),
            1252, Collation.GeneralLegacy, 46000);

        Assert.Equal(id, System.Text.Encoding.ASCII.GetString(page, 0x04, id.Length));
        Assert.Equal(version, page[0x14]);
        Assert.Equal(minor, page[0x15]);
    }

    [Fact]
    public void Synthesized_page0_round_trips_through_the_reader()
    {
        var created = new DateTime(2026, 7, 14, 12, 0, 0);
        byte[] page = DatabaseCreator.BuildDefinitionPage(
            0x02, isAccdb: true, 1252, Collation.GeneralLegacy, (created - new DateTime(1899, 12, 30)).TotalDays);

        var dp = new LibRed.Pages.DatabaseDefinitionPage();
        dp.Read(new LibRed.IO.PageBuffer(page, 0), LibRed.Formats.JetFormatBase.FromVersionByte(0x02));

        Assert.Equal("Standard ACE DB", dp.FormatIdentifier);
        Assert.Equal(0x02, dp.JetVersion);
        Assert.Equal(1252, dp.CodePage);
        Assert.Equal(1033, dp.DefaultCollationLcid);
        Assert.Equal(0, dp.DefaultCollationVersion);
        Assert.Equal(0, dp.DatabaseKey);
        Assert.Equal(2, dp.CatalogRootPage);
        Assert.Equal(created, dp.DatabaseCreationDate, TimeSpan.FromSeconds(1));
    }
}
