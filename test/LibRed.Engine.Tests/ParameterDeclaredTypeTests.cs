using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// A parameter is typed by the value bound to it, as a literal is, so an expression over one declares its result type
// instead of leaving the reader to guess from the first row — which a Null there turns into Object.
public class ParameterDeclaredTypeTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "param-type-");
        var engine = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        engine.ExecuteNonQuery("CREATE TABLE Q (K LONG, A DATETIME, I LONG)");
        // The first row's date is Null: the declared type cannot come from it.
        engine.ExecuteNonQuery("INSERT INTO Q (K, A, I) VALUES (1, NULL, 1)");
        engine.ExecuteNonQuery("INSERT INTO Q (K, A, I) VALUES (2, #2020-01-02 12:00:00#, 2)");
        return engine;
    }

    private static readonly DateTime SixAm = new(1899, 12, 30, 6, 0, 0);

    [Theory]
    [InlineData("A + @p", typeof(DateTime))]
    [InlineData("@p + A", typeof(DateTime))]
    [InlineData("A - @p", typeof(double))]       // a date less a date is a day count
    public void An_expression_over_a_date_parameter_declares_its_type(string expression, Type expected)
    {
        var result = Fresh().ExecuteQuery($"SELECT {expression} AS c FROM Q ORDER BY K",
            new Dictionary<string, object?> { ["p"] = SixAm });

        var rows = result.Rows.ToList();
        Assert.Equal(expected, result.ColumnTypes[0]);
        Assert.Null(rows[0][0]);
        Assert.IsType(expected, rows[1][0]);
    }

    [Theory]
    [InlineData(5, typeof(int))]
    [InlineData(5L, typeof(long))]
    [InlineData(2.5, typeof(double))]
    public void A_number_parameter_types_the_arithmetic_it_is_in(object value, Type expected)
    {
        var result = Fresh().ExecuteQuery("SELECT I + @p AS c FROM Q ORDER BY K",
            new Dictionary<string, object?> { ["p"] = value });

        Assert.Equal(expected, result.ColumnTypes[0]);
        Assert.All(result.Rows, row => Assert.IsType(expected, row[0]));
    }

    [Fact]
    public void A_parameter_on_its_own_declares_its_value_type()
    {
        var result = Fresh().ExecuteQuery("SELECT @p AS c FROM Q",
            new Dictionary<string, object?> { ["p"] = SixAm });
        Assert.Equal(typeof(DateTime), result.ColumnTypes[0]);
        Assert.All(result.Rows, row => Assert.Equal(SixAm, row[0]));
    }

    [Fact]
    public void A_null_parameter_declares_nothing() =>
        Assert.Equal(typeof(object), Fresh().ExecuteQuery("SELECT @p AS c FROM Q",
            new Dictionary<string, object?> { ["p"] = null }).ColumnTypes[0]);
}
