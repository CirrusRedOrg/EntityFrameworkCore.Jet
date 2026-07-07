using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// Functions added after the LibRed-vs-ACE function-whitelist cross-check. Each expected value is what ACE
// returned for the same call in the sweep.
public class AddedFunctionsTests
{
    private static QueryEngine Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"af-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        var e = new QueryEngine(JetDatabase.Open(path, readOnly: false));
        e.ExecuteNonQuery("CREATE TABLE T ( K LONG PRIMARY KEY )");
        e.ExecuteNonQuery("INSERT INTO T (K) VALUES (1)");
        return e;
    }

    private static object? Eval(string expr) => Fresh().ExecuteQuery($"SELECT {expr} FROM T").Rows.Single()[0];

    [Theory]
    [InlineData("Chr(65)", "A")]
    [InlineData("Space(3)", "   ")]
    [InlineData("String(3, 'x')", "xxx")]
    [InlineData("StrReverse('abc')", "cba")]
    [InlineData("Str(5)", " 5")]
    [InlineData("Hex(255)", "FF")]
    [InlineData("Oct(8)", "10")]
    [InlineData("MonthName(1)", "January")]
    [InlineData("TypeName(5)", "Long")]
    public void String_returning(string expr, string expected)
        => Assert.Equal(expected, Convert.ToString(Eval(expr)));

    [Theory]
    [InlineData("StrComp('a', 'b')", -1)]
    [InlineData("InStrRev('abc', 'b')", 2)]
    [InlineData("Val('12abc')", 12)]
    [InlineData("VarType(5)", 3)]
    public void Numeric_returning(string expr, int expected)
        => Assert.Equal(expected, Convert.ToInt32(Eval(expr)));

    [Theory]
    [InlineData("IsNull(Null)", true)]
    [InlineData("IsNumeric('12')", true)]
    [InlineData("IsNumeric('abc')", false)]
    [InlineData("IsError(1)", false)]
    public void Boolean_returning(string expr, bool expected)
        => Assert.Equal(expected, Convert.ToBoolean(Eval(expr)));

    [Fact]
    public void Rnd_and_Timer_are_in_range()
    {
        double rnd = Convert.ToDouble(Eval("Rnd(1)"));
        Assert.InRange(rnd, 0.0, 1.0);
        double timer = Convert.ToDouble(Eval("Timer()"));
        Assert.InRange(timer, 0.0, 86400.0);
    }
}
