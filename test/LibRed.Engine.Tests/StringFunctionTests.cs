using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class StringFunctionTests
{
    private static string Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"strfn-{Guid.NewGuid():N}.accdb");
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
    public void Length_case_and_trim()
    {
        Assert.Equal(5, Scalar("LEN('hello')"));
        Assert.Equal("hello", Scalar("LCASE('HeLLo')"));
        Assert.Equal("HELLO", Scalar("UCASE('HeLLo')"));
        Assert.Equal("hi", Scalar("TRIM('  hi  ')"));
        Assert.Equal("hi  ", Scalar("LTRIM('  hi  ')"));
        Assert.Equal("  hi", Scalar("RTRIM('  hi  ')"));
    }

    [Fact]
    public void Left_right_mid_are_one_based()
    {
        Assert.Equal("he", Scalar("LEFT('hello', 2)"));
        Assert.Equal("hi", Scalar("LEFT('hi', 5)"));      // n > length → whole
        Assert.Equal("", Scalar("LEFT('hi', 0)"));
        Assert.Equal("lo", Scalar("RIGHT('hello', 2)"));
        Assert.Equal("ello", Scalar("MID('hello', 2)"));   // 1-based, to end
        Assert.Equal("ell", Scalar("MID('hello', 2, 3)"));
        Assert.Equal("", Scalar("MID('hello', 10)"));      // start past end
    }

    [Fact]
    public void Instr_is_one_based_with_optional_start_and_compare()
    {
        Assert.Equal(3, Scalar("INSTR('hello', 'l')"));
        Assert.Equal(0, Scalar("INSTR('hello', 'z')"));
        Assert.Equal(4, Scalar("INSTR(4, 'hello', 'l')")); // optional leading start
        Assert.Equal(1, Scalar("INSTR('Hello', 'h')"));    // case-insensitive by default
        Assert.Equal(0, Scalar("INSTR(1, 'Hello', 'h', 0)")); // compare 0 = binary/case-sensitive
    }

    [Fact]
    public void Replace_with_optional_start_and_count()
    {
        Assert.Equal("aXcaXc", Scalar("REPLACE('abcabc', 'b', 'X')"));
        Assert.Equal("Jello", Scalar("REPLACE('Hello', 'h', 'J')")); // case-insensitive
        Assert.Equal("bba", Scalar("REPLACE('aaa', 'a', 'b', 1, 2)")); // at most 2
    }

    [Fact]
    public void String_functions_propagate_null()
    {
        Assert.Null(Scalar("LEN(NULL)"));
        Assert.Null(Scalar("LEFT(NULL, 2)"));
        Assert.Null(Scalar("MID(NULL, 1)"));
        Assert.Null(Scalar("INSTR('a', NULL)"));
        Assert.Null(Scalar("REPLACE(NULL, 'a', 'b')"));
    }

    // The Left()/Right() functions coexist with LEFT/RIGHT JOIN in the same query.
    [Fact]
    public void Left_function_and_left_join_coexist()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path);
            int rows = new QueryEngine(db).ExecuteQuery(
                "SELECT LEFT(c.CompanyName, 3) FROM Customers AS c " +
                "LEFT JOIN Orders AS o ON c.CustomerID = o.CustomerID").Rows.Count();
            Assert.True(rows > 0);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
