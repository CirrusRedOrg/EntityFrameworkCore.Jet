using System.Globalization;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// Access's <c>Nz(value [, valueIfNull])</c>, which the Access application has and ACE's OLE DB provider does not. Every
/// expected value here is what Access itself returned for the same query over the same rows: a Variant, written out as
/// text, sorted and grouped as its text, and kept as its own value where it is an operand; and, with one argument, VBA's
/// Empty for a Null — written out as "", read as 0 by arithmetic.
/// </summary>
public class NzTests(NzTests.Database database) : TempDatabaseTest, IClassFixture<NzTests.Database>
{
    private static readonly string[] Setup =
    [
        "CREATE TABLE T (Id LONG, K LONG, K2 TEXT(10))",
        "INSERT INTO T VALUES (1, 1, 'a')",
        "INSERT INTO T VALUES (2, 2, 'b')",
        "INSERT INTO T VALUES (7, 10, 'c')",
        "INSERT INTO T VALUES (8, NULL, NULL)",
    ];

    public sealed class Database() : SharedDatabase("nz-", Setup);

    private string ById(string expression)
    {
        var (types, rows) = database.Query($"SELECT Id, {expression} AS r FROM T ORDER BY Id", CultureInfo.InvariantCulture);
        return $"{types[1].Name} " + string.Join(" ", rows.Select(row =>
            $"{row[0]}:{(row[1] is null ? "Null" : Convert.ToString(row[1], CultureInfo.InvariantCulture))}"));
    }

    [Theory]
    [InlineData("Nz(K, 0)", "String 1:1 2:2 7:10 8:0")]
    [InlineData("Nz(K2, '-')", "String 1:a 2:b 7:c 8:-")]
    [InlineData("Nz(K, 'none')", "String 1:1 2:2 7:10 8:none")]
    [InlineData("Nz(K, 0.5)", "String 1:1 2:2 7:10 8:0.5")]
    [InlineData("Nz(Null, 5)", "String 1:5 2:5 7:5 8:5")]
    [InlineData("Nz(K)", "String 1:1 2:2 7:10 8:")]
    [InlineData("Nz(K2)", "String 1:a 2:b 7:c 8:")]
    [InlineData("Nz(Null)", "String 1: 2: 7: 8:")]
    [InlineData("Nz(Null, Null)", "String 1:Null 2:Null 7:Null 8:Null")]
    public void Nz_is_written_out_as_text(string expression, string expected) =>
        Assert.Equal(expected, ById(expression));

    [Theory]
    [InlineData("Nz(K, 0) + 1", "Double 1:2 2:3 7:11 8:1")]
    [InlineData("Nz(K) + 2", "Double 1:3 2:4 7:12 8:2")]
    [InlineData("Nz(K) * 3", "Double 1:3 2:6 7:30 8:0")]
    [InlineData("Nz(K2) & 'x'", "String 1:ax 2:bx 7:cx 8:x")]
    [InlineData("Len(Nz(K))", "Int32 1:1 2:1 7:2 8:0")]
    [InlineData("Nz(K) = 0", "Boolean 1:False 2:False 7:False 8:True")]
    [InlineData("Nz(Nz(K))", "String 1:1 2:2 7:10 8:")]
    public void As_an_operand_nz_is_its_value_and_empty_is_zero_or_empty_text(string expression, string expected) =>
        Assert.Equal(expected, ById(expression));

    [Fact]
    public void Nz_sorts_and_groups_as_its_text() =>
        Assert.Equal([8, 1, 7, 2], database.Query("SELECT Id FROM T ORDER BY Nz(K, 0)", CultureInfo.InvariantCulture)
            .Rows.Select(row => (int)row[0]!));

    [Fact]
    public void Nz_compares_as_a_number_beside_one() =>
        Assert.Equal([7], database.Query("SELECT Id FROM T WHERE Nz(K, 0) > 2 ORDER BY Id", CultureInfo.InvariantCulture)
            .Rows.Select(row => (int)row[0]!));

    [Fact]
    public void Nz_takes_one_or_two_arguments() =>
        Assert.Throws<InvalidOperationException>(() => database.Query("SELECT Nz(K, 0, 1) FROM T", CultureInfo.InvariantCulture));
}
