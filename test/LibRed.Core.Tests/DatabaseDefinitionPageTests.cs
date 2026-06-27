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
