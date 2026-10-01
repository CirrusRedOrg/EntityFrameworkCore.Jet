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

    /// <summary>Every Access-authored fixture in Data\ whose order LibRed can create: one per locale.</summary>
    public static TheoryData<string> LocaleFixtures()
    {
        var data = new TheoryData<string>();
        foreach (string path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Data"), "*.accdb").Order())
        {
            try
            {
                using var db = JetDatabase.Open(path);
                if (db.DefinitionPage.DatabaseKey == 0 && db.Collation.IsIndexKeyEncodable)
                    data.Add(Path.GetFileName(path));
            }
            catch (InvalidOperationException) { }   // password-encrypted
        }
        return data;
    }

    // The same, in every order: page 0 carries the code page of the order's language (1250 Czech, 936 Chinese,
    // 0 for Georgian), so creating in an order has to stamp the one Access stamps — from LibRed's own table,
    // not from the file being compared against.
    [Theory]
    [MemberData(nameof(LocaleFixtures))]
    public void Synthesized_page0_header_matches_an_access_file_in_its_order(string fixture)
    {
        string path = TestDatabases.Data(fixture);
        byte[] real = File.ReadAllBytes(path);
        using var db = JetDatabase.Open(path);
        var dp = db.DefinitionPage;

        byte[] synth = DatabaseCreator.BuildDefinitionPage(
            dp.JetVersion, isAccdb: true, JetCodePages.For(dp.Collation)!.Value, dp.Collation,
            (dp.DatabaseCreationDate - new DateTime(1899, 12, 30)).TotalDays);

        Assert.Equal(Convert.ToHexString(real[0x00..0xA0]), Convert.ToHexString(synth[0x00..0xA0]));
    }

    // Creation refuses an order without a measured code page, so every order LibRed can encode must have one.
    [Fact]
    public void Every_creatable_order_has_a_measured_code_page()
    {
        var missing = new List<string>();
        foreach (CollatingOrder order in Enum.GetValues<CollatingOrder>())
            foreach (byte version in (byte[])[0, 1])
                foreach (byte sortId in (byte[])[0, 1, 2, 3, 4])
                {
                    var collation = new Collation(order, version, sortId);
                    if (collation.IsIndexKeyEncodable && JetCodePages.For(collation) is null)
                        missing.Add($"0x{collation.Lcid:X5} v{version}");
                }
        Assert.Empty(missing);
    }

    // Every column of the system tables a new database carries is described byte-for-byte as Access describes it:
    // the catalog flags 0x10/0x20 on exactly the catalog tables' columns (and not on the complex templates, which
    // once carried them), the attachment template's extended flag 0x10, and the zero 0x09 of an engine table.
    [Fact]
    public void Synthesized_system_columns_match_a_real_file()
    {
        string path = TemporaryDatabase.CreatePath("libred_syscols_");
        try
        {
            DatabaseCreator.CreateEmpty(path);
            using var created = JetDatabase.Open(path);
            using var real = JetDatabase.Open(TestDatabases.NorthwindAccdb);

            string[] systemTables =
            [
                "MSysObjects", "MSysACEs", "MSysQueries", "MSysRelationships", "MSysComplexColumns",
                "MSysComplexType_Long", "MSysComplexType_Text", "MSysComplexType_Attachment",
            ];
            foreach (string name in systemTables)
            {
                TableDef expected = real.Catalog.FindTable(name)!;
                TableDef actual = created.Catalog.FindTable(name)!;
                foreach (ColumnDef column in expected.Columns)
                    Assert.True(column.RawDescriptor!.AsSpan().SequenceEqual(actual.FindColumn(column.Name)!.RawDescriptor),
                        $"{name}.{column.Name}: Access {Convert.ToHexString(column.RawDescriptor!)}, " +
                        $"LibRed {Convert.ToHexString(actual.FindColumn(column.Name)!.RawDescriptor!)}");
            }
        }
        finally { if (File.Exists(path)) TemporaryDatabase.Delete(path); }
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

    // A new database is dated when it is made, and its SIDs are the default workgroup's, masked with the keystream
    // that date and the rest of its page 0 give (page-00 §2.3): Engine owns the system tables, admin owns MSysDb,
    // and every grant names admin, the Users group or the Creator placeholder — in an .mdb and an .accdb alike.
    [Theory]
    [InlineData((byte)0x01, ".mdb")]
    [InlineData((byte)0x02, ".accdb")]
    [InlineData((byte)0x03, ".accdb")]
    public void A_created_database_is_dated_now_and_its_sids_follow_its_own_page0(byte version, string extension)
    {
        string path = TemporaryDatabase.CreatePath("libred_sids_", extension);
        try
        {
            DateTime before = DateTime.Now;
            DatabaseCreator.CreateEmpty(path, version);
            byte[] page0 = File.ReadAllBytes(path)[..4096];

            using var db = JetDatabase.Open(path);
            Assert.InRange(db.DefinitionPage.DatabaseCreationDate, before.AddSeconds(-1), DateTime.Now.AddSeconds(1));

            // The default workgroup's accounts, as System.mdw's MSysAccounts holds them.
            byte[] engine = LibRed.Crypto.SidKeystream.MaskAccount(page0, [0x02, 0x03]);
            byte[][] grantees =
            [
                LibRed.Crypto.SidKeystream.MaskAccount(page0, [0x03, 0x01]),  // admin user
                LibRed.Crypto.SidKeystream.MaskAccount(page0, [0x02, 0x01]),  // Users group
                LibRed.Crypto.SidKeystream.MaskAccount(page0, [0x02, 0x04]),  // Creator
            ];

            Table objects = db.OpenTable("MSysObjects");
            int name = objects.Definition.RequireColumn("Name").Index, owner = objects.Definition.RequireColumn("Owner").Index;
            foreach (object?[] row in objects.Rows())
                if (row[name] is string table && table.StartsWith("MSys", StringComparison.Ordinal))
                    Assert.Equal(table == "MSysDb" ? grantees[0] : engine, row[owner]);

            Table aces = db.OpenTable("MSysACEs");
            int sid = aces.Definition.RequireColumn("SID").Index;
            Assert.All(aces.Rows(), row => Assert.Contains(grantees, g => g.SequenceEqual((byte[])row[sid]!)));

            var (admin, users, creator) = db.Catalog.SecuritySids;
            Assert.Equal(grantees[0], admin);
            Assert.Equal(grantees[1], users);
            Assert.Equal(grantees[2], creator);
        }
        finally { TemporaryDatabase.Delete(path); }
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
