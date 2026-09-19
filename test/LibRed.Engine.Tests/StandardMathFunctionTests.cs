using System.Globalization;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// The standard SQL math functions: Floor, Ceiling, Sign, Sqrt, Ln, Log10, two-argument Log, Power, the inverse and
/// hyperbolic trigonometric functions, Degrees, Radians and Pi. Access has none of them; they are LibRed extensions.
/// </summary>
public class StandardMathFunctionTests(StandardMathFunctionTests.Database database)
    : TempDatabaseTest, IClassFixture<StandardMathFunctionTests.Database>
{
    private static readonly string[] Setup =
    [
        "CREATE TABLE M (Id LONG, DB FLOAT, SG REAL, CY CURRENCY, DC DECIMAL(18,4), LG LONG, DT DATETIME)",
        "INSERT INTO M (Id, DB, SG, CY, DC, LG, DT) VALUES (1, -2.5, 1.5, -1.25, 2.75, -7, #2020-01-02 18:00#)",
        "INSERT INTO M (Id) VALUES (2)",
    ];

    public sealed class Database() : SharedDatabase("standard-math-", Setup);

    private object? Scalar(string expression, int id = 1) =>
        database.Scalar($"SELECT {expression} FROM M WHERE Id = {id}", CultureInfo.InvariantCulture);

    private Type ColumnType(string expression) =>
        database.Query($"SELECT {expression} FROM M", CultureInfo.InvariantCulture).ColumnTypes[0];

    [Theory]
    [InlineData("FLOOR(DB)", -3d)]
    [InlineData("CEILING(DB)", -2d)]
    [InlineData("CEIL(2.5)", 3d)]
    [InlineData("SQRT(6.25)", 2.5)]
    [InlineData("LN(1)", 0d)]
    [InlineData("LOG10(1000)", 3d)]
    [InlineData("LOG(2, 8)", 3d)]
    [InlineData("POWER(2, 10)", 1024d)]
    [InlineData("POWER(DB, 2)", 6.25)]
    [InlineData("ASIN(1)", Math.PI / 2)]
    [InlineData("ACOS(-1)", Math.PI)]
    [InlineData("ATAN(1)", Math.PI / 4)]
    [InlineData("ATAN2(1, -1)", 3 * Math.PI / 4)]
    [InlineData("ATAN2(-1, 0)", -Math.PI / 2)]
    [InlineData("ATAN2(0, 0)", 0d)]
    [InlineData("SINH(1)", 1.1752011936438014)]
    [InlineData("COSH(0)", 1d)]
    [InlineData("TANH(0.5)", 0.46211715726000974)]
    [InlineData("DEGREES(PI())", 180d)]
    [InlineData("RADIANS(180)", Math.PI)]
    [InlineData("PI()", Math.PI)]
    public void A_function_gives_its_value(string expression, double expected) =>
        Assert.Equal(expected, Assert.IsType<double>(Scalar(expression)), 1e-12);

    [Theory]
    [InlineData("SIGN(DB)", -1)]
    [InlineData("SIGN(0)", 0)]
    [InlineData("SIGN(LG)", -1)]
    [InlineData("SIGN('3')", 1)]
    public void Sign_is_sgn(string expression, int expected) => Assert.Equal(expected, Scalar(expression));

    [Fact]
    public void Floor_and_ceiling_keep_the_operands_type()
    {
        Assert.Equal(-2m, Scalar("FLOOR(CY)"));
        Assert.Equal(3m, Scalar("CEILING(DC)"));
        Assert.Equal(-7, Scalar("FLOOR(LG)"));
        Assert.Equal(2f, Scalar("CEILING(SG)"));
        Assert.Equal(new DateTime(2020, 1, 2), Scalar("FLOOR(DT)"));
        Assert.Equal(new DateTime(2020, 1, 3), Scalar("CEILING(DT)"));
    }

    [Theory]
    [InlineData("FLOOR(CY)", typeof(decimal))]
    [InlineData("CEILING(SG)", typeof(float))]
    [InlineData("FLOOR(LG)", typeof(int))]
    [InlineData("CEIL(DT)", typeof(DateTime))]
    [InlineData("SIGN(DB)", typeof(int))]
    [InlineData("SQRT(DB * DB)", typeof(double))]
    [InlineData("LOG(10, LG * LG)", typeof(double))]
    [InlineData("POWER(LG, 2)", typeof(double))]
    [InlineData("ATAN2(DB, LG)", typeof(double))]
    [InlineData("TANH(CY)", typeof(double))]
    [InlineData("PI()", typeof(double))]
    public void The_column_is_declared_as_the_value_it_returns(string expression, Type expected)
    {
        Assert.Equal(expected, ColumnType(expression));
        Assert.True(Scalar(expression) is var value && value?.GetType() == expected);
    }

    [Theory]
    [InlineData("FLOOR(DB)")]
    [InlineData("SIGN(DB)")]
    [InlineData("SQRT(DB)")]
    [InlineData("LOG(DB, 2)")]
    [InlineData("LOG(2, DB)")]
    [InlineData("POWER(DB, 2)")]
    [InlineData("POWER(2, DB)")]
    [InlineData("ATAN2(DB, 1)")]
    [InlineData("ATAN2(1, DB)")]
    [InlineData("DEGREES(DB)")]
    public void Null_gives_null(string expression) => Assert.Null(Scalar(expression, id: 2));

    [Theory]
    [InlineData("SQRT(-1)")]
    [InlineData("LN(0)")]
    [InlineData("LOG10(-1)")]
    [InlineData("LOG(1, 5)")]
    [InlineData("LOG(0, 5)")]
    [InlineData("LOG(2, 0)")]
    [InlineData("ASIN(2)")]
    [InlineData("ACOS(-1.5)")]
    [InlineData("POWER(-8, 0.5)")]
    public void An_argument_outside_the_domain_is_an_invalid_procedure_call(string expression) =>
        Assert.Throws<ArgumentException>(() => Scalar(expression));

    [Theory]
    [InlineData("SINH(1000)")]
    [InlineData("POWER(10, 400)")]
    public void A_result_past_a_double_is_an_overflow(string expression) =>
        Assert.Throws<OverflowException>(() => Scalar(expression));

    [Theory]
    [InlineData("SQRT()")]
    [InlineData("LOG(1, 2, 3)")]
    [InlineData("POWER(2)")]
    [InlineData("ATAN2(1)")]
    [InlineData("PI(1)")]
    public void The_wrong_number_of_arguments_is_refused(string expression) =>
        Assert.Throws<InvalidOperationException>(() => Scalar(expression));
}
