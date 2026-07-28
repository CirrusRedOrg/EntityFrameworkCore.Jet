using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// Arithmetic result CLR types follow C# promotion (input type = output type), matching the EF contract —
// int+int is Int32, not Decimal. (Access '/' is floating division → Double/Decimal.)
public class ArithmeticTypeTests
{
    private static string Fresh()
    {
        string path = Path.Combine(Path.GetTempPath(), $"arith-{Guid.NewGuid():N}.accdb");
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
    public void Integer_arithmetic_stays_int()
    {
        Assert.IsType<int>(Scalar("1 + 2"));
        Assert.Equal(3, Scalar("1 + 2"));
        Assert.IsType<int>(Scalar("10 - 3"));
        Assert.IsType<int>(Scalar("4 * 5"));
    }

    [Fact]
    public void Order_id_plus_one_is_int()
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path);
            object? v = new QueryEngine(db).ExecuteQuery("SELECT OrderID + 1 AS c FROM Orders").Rows.First()[0];
            Assert.IsType<int>(v); // the failing Union_over_binary_binary shape
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void Unary_negate_preserves_type()
    {
        Assert.IsType<int>(Scalar("-Id"));                    // int → int
        Assert.Equal(-1, Scalar("-Id"));
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path);
            var e = new QueryEngine(db);
            Assert.IsType<int>(e.ExecuteQuery("SELECT -OrderID AS c FROM Orders").Rows.First()[0]);   // int
            Assert.IsType<decimal>(e.ExecuteQuery("SELECT -UnitPrice AS c FROM Products").Rows.First()[0]); // currency
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void Promotion_to_wider_types()
    {
        Assert.IsType<double>(Scalar("1 + 2.5"));     // int + double → double
        Assert.IsType<double>(Scalar("5 / 2"));       // '/' is floating division
        Assert.Equal(2.5d, Scalar("5 / 2"));
    }

    [Fact]
    public void Integer_division_and_mod_stay_int()
    {
        Assert.IsType<int>(Scalar("7 \\ 2"));   // integer division
        Assert.Equal(3, Scalar("7 \\ 2"));
        Assert.IsType<int>(Scalar("7 MOD 3"));
        Assert.Equal(1, Scalar("7 MOD 3"));

        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path);
            // The failing shape: o.OrderID \ o.OrderID \ 2  (left-assoc integer division).
            object? v = new QueryEngine(db).ExecuteQuery(
                "SELECT (OrderID \\ OrderID) \\ 2 AS A FROM Orders").Rows.First()[0];
            Assert.IsType<int>(v);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void Currency_arithmetic_stays_decimal()
    {
        // UnitPrice is Currency (decimal); a decimal operand promotes the whole expression to decimal.
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path);
            var e = new QueryEngine(db);
            Assert.IsType<decimal>(e.ExecuteQuery("SELECT UnitPrice + 1 AS c FROM Products").Rows.First()[0]);
            Assert.IsType<decimal>(e.ExecuteQuery("SELECT UnitPrice / 2 AS c FROM Products").Rows.First()[0]);
        }
        finally { try { File.Delete(path); } catch (IOException) { } }
    }
}
