using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// ROUND / FIX (truncate) / INT / ABS preserve the operand's type (double->double, decimal->decimal), so EF
// reads them with the type its Math.* call expects (the failing Truncate_double/Round_double shapes).
public class MathFunctionTypeTests
{
    private static string Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "mathfn-");
        return path;
    }

    private static object? Scalar(string expr)
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var e = new QueryEngine(db);
            e.ExecuteNonQuery("CREATE TABLE M (D DOUBLE, C CURRENCY)");
            e.ExecuteNonQuery("INSERT INTO M (D, C) VALUES (3.7, 3.7)");
            return e.ExecuteQuery($"SELECT {expr} FROM M").Rows.First()[0];
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Round_preserves_type()
    {
        Assert.IsType<double>(Scalar("ROUND(D, 1)"));
        Assert.Equal(3.7d, Scalar("ROUND(D, 1)"));
        Assert.IsType<decimal>(Scalar("ROUND(C, 1)"));
        Assert.Equal(3.7m, Scalar("ROUND(C, 1)"));
        Assert.IsType<double>(Scalar("ROUND(2.5, 0)")); // 2.5 literal is a double
        Assert.Equal(2d, Scalar("ROUND(2.5, 0)"));      // banker's rounding
    }

    [Fact]
    public void Fix_int_abs_preserve_type()
    {
        Assert.IsType<double>(Scalar("FIX(D)"));
        Assert.Equal(3d, Scalar("FIX(D)"));           // toward zero
        Assert.IsType<decimal>(Scalar("FIX(C)"));
        Assert.IsType<double>(Scalar("INT(D)"));
        Assert.IsType<double>(Scalar("ABS(D)"));
        Assert.IsType<int>(Scalar("ABS(-5)"));         // integer stays int
        Assert.Equal(5, Scalar("ABS(-5)"));
    }
}
