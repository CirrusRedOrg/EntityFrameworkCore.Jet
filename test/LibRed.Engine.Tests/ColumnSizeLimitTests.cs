using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// Jet/ACE caps a char/varchar column at 255 characters and a binary/varbinary column at 510 bytes (verified
// vs ACE: char(255)/binary(510) accepted, char(256)/binary(511) rejected "Size of field is too long"). LibRed
// enforces the same caps at CREATE so it never writes a fixed column Access can't open.
public class ColumnSizeLimitTests
{
    private static QueryEngine Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"sizelim-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        return new QueryEngine(JetDatabase.Open(path, readOnly: false));
    }

    [Theory]
    [InlineData("char(255)")]
    [InlineData("varchar(255)")]
    [InlineData("nchar(255)")]
    [InlineData("binary(510)")]
    [InlineData("varbinary(510)")]
    public void Sizes_at_the_limit_are_accepted(string type)
    {
        var e = Fresh();
        Assert.Equal(0, e.ExecuteNonQuery($"CREATE TABLE T ( col {type} )"));
    }

    [Theory]
    [InlineData("char(256)")]
    [InlineData("varchar(256)")]
    [InlineData("national character varying(256)")]
    [InlineData("binary(511)")]
    [InlineData("varbinary(511)")]
    [InlineData("binary(8000)")]
    public void Sizes_over_the_limit_are_rejected(string type)
    {
        var e = Fresh();
        var ex = Assert.Throws<InvalidOperationException>(
            () => e.ExecuteNonQuery($"CREATE TABLE T ( col {type} )"));
        Assert.Contains("too long", ex.Message);
    }
}
