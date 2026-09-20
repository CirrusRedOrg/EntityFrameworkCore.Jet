using System.Globalization;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// The aggregates <c>Sum</c> <c>Avg</c> <c>Min</c> <c>Max</c> <c>StDev</c> <c>StDevP</c> <c>Var</c> <c>VarP</c> over
/// columns of every kind. The expected values were measured against ACE; result types follow LibRed's contract (as
/// LINQ's aggregates type them) rather than ACE's widened types.
/// </summary>
public class AggregateFunctionTests(AggregateFunctionTests.Database database)
    : TempDatabaseTest, IClassFixture<AggregateFunctionTests.Database>
{
    private static readonly string[] Setup =
    [
        "CREATE TABLE A (Id LONG, I LONG, S SHORT, B BYTE, R REAL, F FLOAT, C CURRENCY, D DECIMAL(18,4), T TEXT(20), "
            + "TN TEXT(20), DT DATETIME, Y YESNO, G GUID, N LONG, TM TEXT(20))",
        "INSERT INTO A (Id, I, S, B, R, F, C, D, T, TN, DT, Y, TM) "
            + "VALUES (1, 1, 1, 1, 1.5, 2.5, 1.25, 1.5, 'b', '10', #2020-01-02 12:00#, TRUE, '5')",
        "INSERT INTO A (Id, I, S, B, R, F, C, D, T, TN, DT, Y, TM) "
            + "VALUES (2, 2, -3, 255, 0.1, -1.5, 2.5, 2.25, 'A', '2', #1999-12-31 13:00#, FALSE, 'x')",
        "INSERT INTO A (Id) VALUES (3)",
        "INSERT INTO A (Id, I, S, B, R, F, C, D, T, TN, DT, Y, TM) "
            + "VALUES (4, 2, 5, 0, -2, 1E-10, -0.0001, -1, 'a', '1e3', #1899-12-29 06:00#, TRUE, '')",
        "INSERT INTO A (Id, TM) VALUES (5, '7')",
        "UPDATE A SET G = {00112233-4455-6677-8899-AABBCCDDEEFF} WHERE Id = 1",
    ];

    public sealed class Database() : SharedDatabase("aggregate-", Setup);

    private object? Scalar(string select) => database.Scalar($"SELECT {select}", CultureInfo.GetCultureInfo("en-US"));

    [Theory]
    [InlineData("SUM(I) FROM A", 5)]
    [InlineData("SUM(S) FROM A", 3)]
    [InlineData("SUM(B) FROM A", 256)]
    [InlineData("SUM(Y) FROM A", -2)]
    [InlineData("SUM(1) FROM A", 5)]
    [InlineData("COUNT(*) FROM A WHERE Id > 99", 0)]
    [InlineData("COUNT(TM) FROM A", 4)]
    public void Whole_number_sums_are_integers(string select, int expected) =>
        Assert.Equal(expected, Assert.IsType<int>(Scalar(select)));

    [Theory]
    [InlineData("SUM(TN) FROM A", 1012.0)]
    [InlineData("SUM('3') FROM A", 15.0)]
    [InlineData("SUM(DT) FROM A", 80356.79166666666)]
    [InlineData("AVG(DT) FROM A", 26785.59722222222)]
    [InlineData("AVG(Y) FROM A", -0.4)]
    [InlineData("AVG(TRUE) FROM A", -1.0)]
    [InlineData("SUM(F) FROM A WHERE Id IN (1, 2)", 1.0)]
    public void Text_dates_and_booleans_sum_as_their_numbers(string select, double expected) =>
        Assert.Equal(expected, Assert.IsType<double>(Scalar(select)));

    [Fact]
    public void A_currency_sum_is_exact() =>
        Assert.Equal(3.7499m, Scalar("SUM(C) FROM A"));

    [Theory]
    [InlineData("STDEV(I) FROM A", 0.5773502691896257)]
    [InlineData("STDEVP(I) FROM A", 0.4714045207910317)]
    [InlineData("VAR(S) FROM A", 16.0)]
    [InlineData("VARP(S) FROM A", 10.666666666666666)]
    [InlineData("VAR(B) FROM A", 21590.333333333332)]
    [InlineData("VARP(B) FROM A", 14393.555555555555)]
    [InlineData("VAR(R) FROM A", 3.1033333338859177)]
    [InlineData("VARP(R) FROM A", 2.0688888892572788)]
    [InlineData("STDEV(R) FROM A", 1.7616280350533473)]
    [InlineData("STDEVP(R) FROM A", 1.4383632674874864)]
    [InlineData("VAR(F) FROM A", 4.0833333333)]
    [InlineData("VARP(F) FROM A", 2.7222222222)]
    [InlineData("STDEVP(F) FROM A", 1.6499158227618766)]
    [InlineData("VAR(C) FROM A", 1.5626166666666668)]
    [InlineData("VARP(C) FROM A", 1.0417444444444444)]
    [InlineData("STDEV(C) FROM A", 1.2500466657955882)]
    [InlineData("STDEVP(C) FROM A", 1.0206588286222014)]
    [InlineData("VAR(D) FROM A", 2.8958333333333335)]
    [InlineData("VARP(D) FROM A", 1.9305555555555556)]
    [InlineData("STDEV(D) FROM A", 1.7017148213885114)]
    [InlineData("VAR(TN) FROM A", 329361.3333333333)]
    [InlineData("VARP(TN) FROM A", 219574.22222222222)]
    [InlineData("STDEV(TN) FROM A", 573.9001074519269)]
    [InlineData("STDEVP(TN) FROM A", 468.58747552855294)]
    [InlineData("VAR(DT) FROM A", 551499298.1012732)]
    [InlineData("VARP(DT) FROM A", 367666198.7341821)]
    [InlineData("STDEV(DT) FROM A", 23484.022187463397)]
    [InlineData("VAR(Y) FROM A", 0.3)]
    [InlineData("VARP(Y) FROM A", 0.24)]
    [InlineData("STDEV(Y) FROM A", 0.5477225575051661)]
    [InlineData("VAR(F) FROM A WHERE Id IN (1, 2)", 8.0)]
    [InlineData("STDEVP(I) FROM A WHERE Id = 1", 0.0)]
    [InlineData("VARP(I) FROM A WHERE Id = 1", 0.0)]
    public void Statistics_match_ace_to_the_last_bit(string select, double expected) =>
        Assert.Equal(expected, Assert.IsType<double>(Scalar(select)));

    [Theory]
    [InlineData("MIN(TM) FROM A", "7")]
    [InlineData("MAX(TM) FROM A", "x")]
    [InlineData("MAX(TM) FROM A WHERE Id IN (4, 5)", "7")]
    [InlineData("MIN(TM) FROM A WHERE Id IN (2, 4, 5)", "7")]
    [InlineData("MIN(IIF(Id = 4, 'a', TM)) FROM A", "5")]
    [InlineData("MIN(T) FROM A", "A")]
    [InlineData("MAX(T) FROM A", "b")]
    public void Empty_text_counts_as_no_value_once_it_wins(string select, string expected) =>
        Assert.Equal(expected, Scalar(select));

    [Theory]
    [InlineData("MIN(TM) FROM A WHERE Id IN (1, 4)")]
    [InlineData("MIN(TM) FROM A WHERE Id = 4")]
    [InlineData("MIN(TM) FROM A WHERE Id IN (1, 2, 4)")]
    [InlineData("SUM(I) FROM A WHERE Id > 99")]
    [InlineData("AVG(I) FROM A WHERE Id > 99")]
    [InlineData("MAX(DT) FROM A WHERE Id > 99")]
    [InlineData("SUM(N) FROM A")]
    [InlineData("VAR(N) FROM A")]
    [InlineData("STDEV(I) FROM A WHERE Id = 1")]
    [InlineData("VAR(I) FROM A WHERE Id = 1")]
    public void Null_results(string select) =>
        Assert.Null(Scalar(select));

    [Theory]
    [InlineData("SUM(T) FROM A")]
    [InlineData("AVG(TM) FROM A")]
    [InlineData("STDEV(T) FROM A")]
    [InlineData("SUM(G) FROM A")]
    [InlineData("VAR(G) FROM A")]
    public void Values_that_are_not_numbers_are_a_type_mismatch(string select) =>
        Assert.Throws<InvalidCastException>(() => Scalar(select));
}
