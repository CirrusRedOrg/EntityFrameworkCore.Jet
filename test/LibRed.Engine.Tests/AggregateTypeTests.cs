using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// Aggregate result CLR types must match Access (verified vs ACE), so EF reads them with the expected
// GetInt32/GetDouble/GetDecimal without a cast error.
public class AggregateTypeTests
{
    private static string Fresh()
    {
        string path = TemporaryDatabase.CopyPath(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "agg-");
        return path;
    }

    private static object? Scalar(string sql)
    {
        string path = Fresh();
        try
        {
            using var db = JetDatabase.Open(path);
            return new QueryEngine(db).ExecuteQuery(sql).Rows.First()[0];
        }
        finally { TemporaryDatabase.Delete(path); }
    }

    [Fact]
    public void Count_is_int32()
    {
        Assert.IsType<int>(Scalar("SELECT COUNT(*) FROM Products"));
        Assert.Equal(77, Scalar("SELECT COUNT(*) FROM Products"));
    }

    [Fact]
    public void Sum_preserves_the_input_type()
    {
        // Integer columns sum to Int32 (the EF provider emits a bare SUM and reads by the operand type).
        Assert.IsType<int>(Scalar("SELECT SUM(UnitsInStock) FROM Products")); // smallint
        Assert.IsType<int>(Scalar("SELECT SUM(OrderID) FROM Orders"));        // long/int
        Assert.IsType<decimal>(Scalar("SELECT SUM(UnitPrice) FROM Products")); // currency
    }

    [Fact]
    public void Avg_is_double_for_integers_and_decimal_for_currency()
    {
        Assert.IsType<double>(Scalar("SELECT AVG(UnitsInStock) FROM Products"));
        Assert.IsType<decimal>(Scalar("SELECT AVG(UnitPrice) FROM Products"));
    }

    [Fact]
    public void Min_max_preserve_the_column_type()
    {
        Assert.IsType<short>(Scalar("SELECT MAX(UnitsInStock) FROM Products")); // smallint
        Assert.IsType<decimal>(Scalar("SELECT MIN(UnitPrice) FROM Products"));  // currency
    }
}
