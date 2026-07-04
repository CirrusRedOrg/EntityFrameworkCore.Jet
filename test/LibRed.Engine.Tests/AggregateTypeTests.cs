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
        string path = Path.Combine(Path.GetTempPath(), $"agg-{Guid.NewGuid():N}.accdb");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), path);
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
        finally { try { File.Delete(path); } catch (IOException) { } }
    }

    [Fact]
    public void Count_is_int32()
    {
        Assert.IsType<int>(Scalar("SELECT COUNT(*) FROM Products"));
        Assert.Equal(77, Scalar("SELECT COUNT(*) FROM Products"));
    }

    [Fact]
    public void Sum_and_avg_are_double_for_integer_columns()
    {
        Assert.IsType<double>(Scalar("SELECT SUM(UnitsInStock) FROM Products"));
        Assert.IsType<double>(Scalar("SELECT AVG(UnitsInStock) FROM Products"));
    }

    [Fact]
    public void Sum_and_avg_are_decimal_for_currency_columns()
    {
        Assert.IsType<decimal>(Scalar("SELECT SUM(UnitPrice) FROM Products"));
        Assert.IsType<decimal>(Scalar("SELECT AVG(UnitPrice) FROM Products"));
    }

    [Fact]
    public void Min_max_preserve_the_column_type()
    {
        Assert.IsType<short>(Scalar("SELECT MAX(UnitsInStock) FROM Products")); // smallint
        Assert.IsType<decimal>(Scalar("SELECT MIN(UnitPrice) FROM Products"));  // currency
    }
}
