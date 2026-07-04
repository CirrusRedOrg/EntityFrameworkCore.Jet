using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class ConversionFunctionTests
{
    private static string Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"conv-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
        return path;
    }

    private static object? Scalar(string expr)
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery("CREATE TABLE One (Id LONG)");
            e.ExecuteNonQuery("INSERT INTO One (Id) VALUES (1)");
            return e.ExecuteQuery($"SELECT {expr} FROM One").Rows.First()[0];
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void Integer_conversions_round_half_to_even()
    {
        Assert.Equal((short)2, Scalar("CInt(2.5)"));   // banker's rounding
        Assert.Equal((short)4, Scalar("CInt(3.5)"));
        Assert.Equal(-2, Scalar("CLng(-2.5)"));
        Assert.Equal((byte)2, Scalar("CByte(2.5)"));
        Assert.Equal((byte)255, Scalar("CByte(255)"));
    }

    [Fact]
    public void Float_and_decimal_conversions()
    {
        Assert.Equal(1.5f, Scalar("CSng(1.5)"));
        Assert.Equal(3.0d, Scalar("CDbl(3)"));
        Assert.Equal(1.25m, Scalar("CDec(1.25)"));
        Assert.Equal(1.5m.ToString(), Scalar("CCur(1.5)")!.ToString()); // currency (existing)
    }

    [Fact]
    public void String_bool_date_var_conversions()
    {
        Assert.Equal("1.5", Scalar("CStr(1.5)"));
        Assert.Equal("True", Scalar("CStr(True)"));
        Assert.Equal(false, Scalar("CBool(0)"));
        Assert.Equal(true, Scalar("CBool(5)"));
        Assert.Equal(new DateTime(2020, 1, 15), Scalar("CDate('2020-01-15')"));
        Assert.Equal(42, Scalar("CVar(42)")); // passthrough
    }

    [Fact]
    public void Conversions_propagate_null()
    {
        Assert.Null(Scalar("CInt(NULL)"));
        Assert.Null(Scalar("CDbl(NULL)"));
        Assert.Null(Scalar("CStr(NULL)"));
        Assert.Null(Scalar("CDate(NULL)"));
        Assert.Null(Scalar("CVar(NULL)"));
    }
}
