using System.Globalization;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// Round, Abs, Int and Fix keep their operand's type, and are declared as they return: undeclared, a Round beside a
/// whole number in IIF, CASE or COALESCE let the whole number declare the column while the Round arm returned a
/// Decimal. Sgn is an Integer.
/// </summary>
public class KeptTypeFunctionDeclarationTests(KeptTypeFunctionDeclarationTests.Database database)
    : TempDatabaseTest, IClassFixture<KeptTypeFunctionDeclarationTests.Database>
{
    private static readonly string[] Setup =
    [
        "CREATE TABLE K (Id LONG, Y CURRENCY, E DECIMAL(18,4), D DOUBLE, R REAL, S SMALLINT, L BIGINT, T DATETIME, X TEXT(10))",
        "INSERT INTO K (Id, Y, E, D, R, S, L, T, X) VALUES (1, 2.5678, 2.5678, 2.5678, 2.5, -7, 5, #2020-01-02 12:00:00#, '2.5')",
    ];

    public sealed class Database() : SharedDatabase("kept-type-", Setup);

    [Theory]
    [InlineData("ROUND(Y, 2)", typeof(decimal))]
    [InlineData("ROUND(E)", typeof(decimal))]
    [InlineData("ROUND(D, 1)", typeof(double))]
    [InlineData("ROUND(R)", typeof(float))]
    [InlineData("ROUND(S)", typeof(int))]
    [InlineData("ROUND(L)", typeof(long))]
    [InlineData("ROUND(X)", typeof(double))]
    [InlineData("ROUND(T)", typeof(double))]
    [InlineData("ABS(Y)", typeof(decimal))]
    [InlineData("ABS(S)", typeof(int))]
    [InlineData("ABS(T)", typeof(double))]
    [InlineData("INT(D)", typeof(double))]
    [InlineData("INT(T)", typeof(DateTime))]
    [InlineData("FIX(Y)", typeof(decimal))]
    [InlineData("FIX(T)", typeof(DateTime))]
    [InlineData("SGN(Y)", typeof(int))]
    [InlineData("IIF(Id = 1, ROUND(Y), 0)", typeof(decimal))]
    [InlineData("IIF(Id = 2, ROUND(Y), 0)", typeof(decimal))]
    [InlineData("CASE WHEN Id = 1 THEN ROUND(Y, 2) ELSE 5 END", typeof(decimal))]
    [InlineData("COALESCE(ROUND(Y), S)", typeof(decimal))]
    public void The_column_is_declared_as_the_value_it_returns(string expression, Type expected)
    {
        var (types, rows) = database.Query($"SELECT {expression} AS c FROM K", CultureInfo.InvariantCulture);
        Assert.Equal(expected, types[0]);
        Assert.IsType(expected, rows.Single()[0]);
    }
}
