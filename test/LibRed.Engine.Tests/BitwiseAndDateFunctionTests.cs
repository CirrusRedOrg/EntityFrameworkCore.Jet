using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class BitwiseAndDateFunctionTests
{
    private static string Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"bitdate-{Guid.NewGuid():N}.accdb");
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
    public void Bitwise_operators()
    {
        Assert.Equal(2, Scalar("BAND(6, 3)"));
        Assert.Equal(7, Scalar("BOR(6, 3)"));
        Assert.Equal(5, Scalar("BXOR(6, 3)"));
        Assert.Equal(-1, Scalar("BNOT(0)"));
        Assert.Equal(-6, Scalar("BNOT(5)"));
        Assert.IsType<int>(Scalar("BAND(6, 3)"));      // result keeps int type
        Assert.Null(Scalar("BAND(NULL, 3)"));          // NULL-propagating
    }

    [Fact]
    public void DateAdd_and_DateDiff()
    {
        Assert.Equal(new DateTime(2020, 1, 6), Scalar("DateAdd('d', 5, #1/1/2020#)"));
        Assert.Equal(new DateTime(2020, 2, 29), Scalar("DateAdd('m', 1, #1/31/2020#)"));   // rolls to Feb 29
        Assert.Equal(new DateTime(2019, 3, 15), Scalar("DateAdd('yyyy', -1, #3/15/2020#)"));
        Assert.Equal(10, Scalar("DateDiff('d', #1/1/2020#, #1/11/2020#)"));
        Assert.Equal(1, Scalar("DateDiff('yyyy', #12/31/2019#, #1/1/2020#)"));              // boundary count
        Assert.Equal(2, Scalar("DateDiff('m', #1/15/2020#, #3/10/2020#)"));
        Assert.Equal(3, Scalar("DateDiff('q', #1/1/2020#, #12/31/2020#)"));
        Assert.Null(Scalar("DateDiff('d', NULL, #1/1/2020#)"));
    }

    [Fact]
    public void DateSerial_and_TimeSerial()
    {
        Assert.Equal(new DateTime(2020, 2, 29), Scalar("DateSerial(2020, 2, 29)"));
        Assert.Equal(new DateTime(2021, 1, 1), Scalar("DateSerial(2020, 13, 1)"));          // month rolls over
        var t = Assert.IsType<DateTime>(Scalar("TimeSerial(14, 30, 15)"));
        Assert.Equal(new TimeSpan(14, 30, 15), t.TimeOfDay);
    }

    [Fact]
    public void Date_part_functions_return_int()
    {
        Assert.Equal(2020, Scalar("Year(#3/15/2020#)"));
        Assert.Equal(3, Scalar("Month(#3/15/2020#)"));
        Assert.Equal(15, Scalar("Day(#3/15/2020#)"));
        Assert.Equal(4, Scalar("Weekday(#1/1/2020#)")); // Wed, with Sunday = 1
        Assert.IsType<int>(Scalar("Year(#3/15/2020#)"));  // int, matching EF's DateTime.Year
        Assert.Null(Scalar("Year(NULL)"));
    }
}
