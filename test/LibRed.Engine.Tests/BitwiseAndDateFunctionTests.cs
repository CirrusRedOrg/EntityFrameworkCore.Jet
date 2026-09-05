using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class BitwiseAndDateFunctionTests
{
    private static string Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "bitdate-");
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
        finally { TemporaryDatabase.Delete(path); }
    }

    // DateValue / TimeValue / IsDate (VBA/Access), semantics verified against ACE.
    [Fact]
    public void DateValue_TimeValue_IsDate()
    {
        // DateValue keeps the date at midnight; TimeValue puts the time on the Jet epoch (1899-12-30).
        Assert.Equal(new DateTime(2020, 3, 15), Scalar("DateValue('2020-03-15 13:45:30')"));
        Assert.Equal(new DateTime(1899, 12, 30, 13, 45, 30), Scalar("TimeValue('2020-03-15 13:45:30')"));
        Assert.Null(Scalar("DateValue(NULL)"));  // NULL-propagating
        Assert.Null(Scalar("TimeValue(NULL)"));

        // IsDate: true for a date value or a date/time-parseable string; false for a number, NULL, or junk.
        Assert.Equal(true, Scalar("IsDate('2020-03-15')"));
        Assert.Equal(true, Scalar("IsDate('13:45:30')"));
        Assert.Equal(true, Scalar("IsDate(DateValue('2020-03-15'))")); // a real DateTime
        Assert.Equal(false, Scalar("IsDate('not a date')"));
        Assert.Equal(false, Scalar("IsDate(38718)"));                  // a bare number is NOT a date
        Assert.Equal(false, Scalar("IsDate(NULL)"));
    }

    // Access bitwise operators (infix BAND/BOR/BXOR, prefix BNOT) — verified vs ACE.
    [Fact]
    public void Bitwise_operators()
    {
        Assert.Equal(2, Scalar("6 BAND 3"));
        Assert.Equal(7, Scalar("6 BOR 3"));
        Assert.Equal(5, Scalar("6 BXOR 3"));
        Assert.Equal(-6, Scalar("BNOT 5"));
        Assert.Equal(10, Scalar("6 BAND 3 BOR 8")); // BAND binds tighter, left-assoc
        Assert.IsType<int>(Scalar("6 BAND 3"));       // result keeps int type
        Assert.Null(Scalar("NULL BAND 3"));           // NULL-propagating
    }

    // Bitwise on a byte/short operand promotes to Int32 (as C# does — matching the EF/LINQ contract, not
    // ACE's inconsistent narrowing); the value is still correct.
    [Fact]
    public void Bitwise_on_byte_or_short_promotes_to_int()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery("CREATE TABLE Bits (B BYTE, B2 BYTE, S SMALLINT, S2 SMALLINT)");
            e.ExecuteNonQuery("INSERT INTO Bits (B, B2, S, S2) VALUES (5, 3, 6, 3)");

            // Mixed narrow+int, and both-operands-narrow (byte&byte, short&short) — all promote to Int32.
            var r = e.ExecuteQuery("SELECT B BAND 3, BNOT B, S BAND 3, BNOT S, B BAND B2, S BAND S2 FROM Bits").Rows.First();
            Assert.Equal(1, r[0]); Assert.IsType<int>(r[0]);   // 5 & 3
            Assert.Equal(-6, r[1]); Assert.IsType<int>(r[1]);  // ~5 (promoted, not 250)
            Assert.Equal(2, r[2]); Assert.IsType<int>(r[2]);   // 6 & 3
            Assert.Equal(-7, r[3]); Assert.IsType<int>(r[3]);  // ~6
            Assert.Equal(1, r[4]); Assert.IsType<int>(r[4]);   // byte 5 & byte 3
            Assert.Equal(2, r[5]); Assert.IsType<int>(r[5]);   // short 6 & short 3
        }
        finally { TemporaryDatabase.Delete(path); }
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
