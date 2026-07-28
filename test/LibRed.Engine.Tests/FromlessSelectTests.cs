using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>A FROM-less SELECT (e.g. <c>SELECT 2</c>) yields exactly one row. ACE accepts this (verified via the
/// OLE DB provider), and EF's CommandInterception scalar tests send a bare <c>SELECT 1</c>/<c>SELECT 2</c>.</summary>
public class FromlessSelectTests
{
    private static QueryEngine Engine()
    {
        string path = Path.Combine(Path.GetTempPath(), $"fl-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        return new QueryEngine(JetDatabase.Open(path, readOnly: false));
    }

    [Fact]
    public void Bare_select_constant_yields_one_row()
    {
        var r = Engine().ExecuteQuery("SELECT 2").Rows.ToList();
        Assert.Single(r);
        Assert.Equal(2, Convert.ToInt32(r[0][0]));
    }

    [Fact]
    public void Select_expression_with_alias_no_from()
    {
        var r = Engine().ExecuteQuery("SELECT 1 + 2 AS X, 'hi' AS Y").Rows.ToList();
        Assert.Single(r);
        Assert.Equal(3, Convert.ToInt32(r[0][0]));
        Assert.Equal("hi", r[0][1]);
    }

    [Fact]
    public void Select_function_no_from()
    {
        var r = Engine().ExecuteQuery("SELECT UCASE('abc') AS U").Rows.ToList();
        Assert.Single(r);
        Assert.Equal("ABC", r[0][0]);
    }
}
