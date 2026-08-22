using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// Jet/ACE caps a char/varchar column at 255 characters and a binary/varbinary column at 510 bytes (verified
// vs ACE: char(255)/binary(510) accepted, char(256)/binary(511) rejected "Size of field is too long"). LibRed
// enforces the same caps at CREATE so it never writes a fixed column Access can't open.
public class ColumnSizeLimitTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "sizelim-");
        return new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
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

    [Theory]
    [InlineData("char(0)")]
    [InlineData("varchar(-1)")]
    [InlineData("binary(0)")]
    [InlineData("varbinary(-1)")]
    [InlineData("decimal(0,0)")]
    [InlineData("decimal(-1,0)")]
    [InlineData("decimal(29,0)")]
    [InlineData("decimal(10,-1)")]
    [InlineData("decimal(10,11)")]
    [InlineData("decimal(300,1)")]
    public void Invalid_dimensions_are_rejected_before_creating_the_table(string type)
    {
        var e = Fresh();
        Assert.Throws<InvalidOperationException>(() =>
            e.ExecuteNonQuery($"CREATE TABLE T ( col {type} )"));
        Assert.DoesNotContain(e.Database.Catalog.UserTables, t => t.Name == "T");
    }

    [Theory]
    [InlineData("decimal(1,0)", 1, 0)]
    [InlineData("decimal(28,28)", 28, 28)]
    public void Decimal_boundary_dimensions_are_preserved(string type, byte precision, byte scale)
    {
        var e = Fresh();
        e.ExecuteNonQuery($"CREATE TABLE T ( col {type} )");
        var column = e.Database.Catalog.UserTables.Single(t => t.Name == "T").Columns.Single();
        Assert.Equal(precision, column.Precision);
        Assert.Equal(scale, column.Scale);
    }
}
