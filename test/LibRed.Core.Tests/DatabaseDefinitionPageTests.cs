using LibRed;
using LibRed.Formats;
using Xunit;

namespace LibRed.Core.Tests;

public class DatabaseDefinitionPageTests
{
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
    }

    [Fact]
    public void Rejects_non_jet_file()
    {
        string bogus = Path.Combine(Path.GetTempPath(), $"libred_{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(bogus, new byte[4096]);
        try
        {
            Assert.Throws<NotSupportedException>(() => JetDatabase.Open(bogus));
        }
        finally
        {
            File.Delete(bogus);
        }
    }
}
