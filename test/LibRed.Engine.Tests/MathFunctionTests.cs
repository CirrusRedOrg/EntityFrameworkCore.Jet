using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

public class MathFunctionTests
{
    private static double Eval(string expr)
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "math-");
        try
        {
            using var db = JetDatabase.Open(path, readOnly: false);
            var engine = new QueryEngine(db);
            // Shippers has 3 rows; take the first — the expression is constant.
            object? v = engine.ExecuteQuery($"SELECT {expr} AS X FROM Shippers").Rows.First()[0];
            return Convert.ToDouble(v);
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Theory]
    [InlineData("SIN(0)", 0)]
    [InlineData("COS(0)", 1)]
    [InlineData("TAN(0)", 0)]
    [InlineData("ATN(0)", 0)]
    [InlineData("EXP(0)", 1)]
    [InlineData("LOG(1)", 0)]      // natural log
    [InlineData("SQR(9)", 3)]      // Jet SQR = square root
    [InlineData("SGN(-5)", -1)]
    [InlineData("ABS(-3)", 3)]
    [InlineData("2 ^ 10", 1024)]   // POW is rendered as the ^ operator
    public void Jet_math_functions_evaluate(string expr, double expected)
        => Assert.Equal(expected, Eval(expr), 6);
}
