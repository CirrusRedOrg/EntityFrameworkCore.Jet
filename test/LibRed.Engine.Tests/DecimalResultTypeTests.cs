using System.Globalization;
using LibRed.Engine;
using LibRed.Engine.Execution;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// The precision and scale of a Decimal result, and where one becomes a Double. Beside a Decimal an Integer or a
/// Boolean counts five whole digits, a Byte, a Long and a whole result ten, a decimal literal, text and a date 28; two
/// Decimals are one of the larger places and the most whole digits where that fits 28 digits, and a Double where it
/// does not. Measured against ACE on this table: the precision and scale its OLE DB schema reports and the value
/// (AceDecimalPrecisionProbeTest.Computed_decimal_schema_probe, Make_table_expressions_select_type_probe).
/// </summary>
public class DecimalResultTypeTests(DecimalResultTypeTests.Database database)
    : TempDatabaseTest, IClassFixture<DecimalResultTypeTests.Database>
{
    private static readonly string[] Setup =
    [
        "CREATE TABLE T (C CURRENCY, D DECIMAL(10,2), F DOUBLE, S REAL, L LONG, I SHORT, B BYTE, Y BIT, "
            + "E DECIMAL(18,4), G DECIMAL(4,1), H DECIMAL(28,6), N BIGINT, X TEXT(10), W DATETIME, K DECIMAL(20,10))",
        "INSERT INTO T (C, D, F, S, L, I, B, Y, E, G, H, N, X, W, K) "
            + "VALUES (1.1, 12.34, 0.1, 0.1, 3, 2, 4, TRUE, 1.2345, 1.5, 1.123456, 7, '5', #2020-01-02#, 1.5)",
    ];

    public sealed class Database() : SharedDatabase("decimal-result-", Setup);

    private (ResultColumn Column, object? Value) Result(string expression)
    {
        ResultSet result = database.Engine.ExecuteQuery($"SELECT {expression} AS R FROM T");
        return (result.Columns[0], result.Rows.Single()[0]);
    }

    private void AssertDecimal(string expression, int precision, int scale, string value)
    {
        (ResultColumn column, object? result) = Result(expression);
        Assert.Equal(typeof(decimal), column.ClrType);
        Assert.Equal(precision, column.Precision);
        Assert.Equal(scale, column.Scale);
        Assert.Equal(value, Assert.IsType<decimal>(result).ToString(CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("D * 2", 12, 2, "24.68")]
    [InlineData("G * I", 6, 1, "3.0")]
    [InlineData("G * Y", 6, 1, "-1.5")]
    [InlineData("G * B", 11, 1, "6.0")]
    [InlineData("E * I", 18, 4, "2.4690")]
    [InlineData("D * L * L", 12, 2, "111.06")]
    [InlineData("D * (I * I)", 12, 2, "49.36")]
    [InlineData("G / G", 4, 1, "1.0")]
    [InlineData("G * G * G", 4, 1, "3.3")]
    [InlineData("G * X", 28, 1, "7.5")]
    [InlineData("G * W", 28, 1, "65748.0")]
    [InlineData("G * 0.5", 28, 1, "0.7")]
    public void A_decimal_beside_a_whole_number_keeps_its_places(string expression, int precision, int scale, string value) =>
        AssertDecimal(expression, precision, scale, value);

    [Theory]
    [InlineData("E * D", 18, 4, "15.2337")]
    [InlineData("G * D", 10, 2, "18.51")]
    [InlineData("G * K", 20, 10, "2.2500000000")]
    [InlineData("(G * 2) * D", 12, 2, "37.02")]
    [InlineData("H - G", 28, 6, "-0.376544")]
    [InlineData("K + E", 24, 10, "2.7345000000")]
    [InlineData("H * E", 28, 6, "1.386906")]
    [InlineData("H * D", 28, 6, "13.863447")]
    [InlineData("G + 0.25", 28, 2, "1.75")]
    [InlineData("G * 2.25", 28, 2, "3.37")]
    public void Two_decimals_within_28_digits_are_a_decimal(string expression, int precision, int scale, string value) =>
        AssertDecimal(expression, precision, scale, value);

    [Theory]
    [InlineData("D * 2.5", 30.85d)]
    [InlineData("H * K", 1.685184d)]
    [InlineData("K * H", 1.685184d)]
    [InlineData("(G * 1.5) * D", 27.765d)]
    [InlineData("E * 1.11", 1.370295d)]
    [InlineData("D * 334.90", 4132.666d)]
    [InlineData("K * 1.5", 2.25d)]
    [InlineData("0.25 + 0.5", 0.75d)]
    public void Two_decimals_past_28_digits_are_a_double(string expression, double value)
    {
        (ResultColumn column, object? result) = Result(expression);
        Assert.Equal(typeof(double), column.ClrType);
        Assert.Equal(value, Assert.IsType<double>(result));
    }

    [Theory]
    [InlineData("D + C", 28, 4, "13.4400")]
    [InlineData("H + C", 28, 6, "2.223456")]
    [InlineData("SUM(G)", 28, 1, "1.5")]
    public void A_currency_beside_a_decimal_and_a_sum_are_28_digits(string expression, int precision, int scale, string value) =>
        AssertDecimal(expression, precision, scale, value);

    // Not ACE's: ACE makes each of these a Double, and LibRed keeps the Decimal .NET gives — Round, Abs, Int and Fix
    // keep their operand's type, and a choice its Decimal arms', a choice holding either arm whatever their digits.
    [Theory]
    [InlineData("ROUND(D, 1)", 10, 2, "12.30")]
    [InlineData("ABS(D)", 10, 2, "12.34")]
    [InlineData("INT(D)", 10, 2, "12.00")]
    [InlineData("IIF(Y, D, 0)", 12, 2, "12.34")]
    [InlineData("IIF(Y, D, G)", 10, 2, "12.34")]
    [InlineData("IIF(Y, D, 1.5)", 28, 2, "12.34")]
    [InlineData("IIF(Y, H, K)", 28, 10, "1.1234560000")]
    public void A_function_or_choice_keeps_its_decimal(string expression, int precision, int scale, string value) =>
        AssertDecimal(expression, precision, scale, value);
}
