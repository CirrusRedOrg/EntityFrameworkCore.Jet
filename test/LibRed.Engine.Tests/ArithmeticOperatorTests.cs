using System.Globalization;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// The <c>-</c> <c>*</c> <c>/</c> <c>\</c> <c>MOD</c> and <c>^</c> operators as ACE evaluates them: their precedence,
/// how they read text and dates, their errors, and the decimal places ACE keeps in a Decimal result. The expected
/// values were measured against ACE.
/// </summary>
public class ArithmeticOperatorTests : TempDatabaseTest
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "arith-ops-");
        var engine = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        engine.ExecuteNonQuery(
            "CREATE TABLE T (Id LONG, N LONG, SI SHORT, SG REAL, CY CURRENCY, DC DECIMAL(18,4), D DATETIME, "
            + "S TEXT(60), NT TEXT(60), G GUID, B BINARY(4))");
        engine.ExecuteNonQuery(
            "INSERT INTO T (Id, N, SI, SG, CY, DC, D, S) VALUES (1, 3, 2, 1.5, 3.25, 4.5, #2020-01-02 12:00:00#, 'abc')");
        engine.ExecuteNonQuery("UPDATE T SET G = {00112233-4455-6677-8899-AABBCCDDEEFF}");
        engine.ExecuteNonQuery("UPDATE T SET B = 0x41004200");
        return engine;
    }

    private static object? Scalar(string expression) => Query($"SELECT {expression} FROM T");

    // Text is read in the regional separators and currency symbol, so each query runs under en-US whatever the
    // machine's culture.
    private static object? Query(string sql)
    {
        QueryEngine engine = Fresh();
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = EnUs;
        try
        {
            return engine.ExecuteQuery(sql).Rows.First()[0];
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("7 \\ 2 * 3", 1)]
    [InlineData("7 \\ 2 / 2", 7)]
    [InlineData("5 MOD 3 * 2", 5)]
    [InlineData("10 MOD 4 \\ 2", 0)]
    [InlineData("10 \\ 4 MOD 3", 2)]
    [InlineData("2 * 3 MOD 4", 2)]
    [InlineData("2 + 3 MOD 2", 3)]
    [InlineData("8 \\ 4 \\ 2", 1)]
    [InlineData("1 - 2 - 3", -4)]
    public void Integer_division_and_mod_each_bind_looser_than_the_one_before(string expression, int expected) =>
        Assert.Equal(expected, Assert.IsType<int>(Scalar(expression)));

    [Theory]
    [InlineData("-2 ^ 2", 4d)]
    [InlineData("2 ^ 3 ^ 2", 64d)]
    [InlineData("3 * 2 ^ 2", 12d)]
    [InlineData("8 / 4 / 2", 1d)]
    public void Power_binds_tighter_than_multiplication_but_looser_than_negation(string expression, double expected) =>
        Assert.Equal(expected, Assert.IsType<double>(Scalar(expression)));

    [Theory]
    [InlineData("'2.5' - N", -0.5d)]
    [InlineData("'1e2' - 1", 99d)]
    [InlineData("'5-' - 1", -6d)]
    [InlineData("'&H10' * 2", 32d)]
    [InlineData("'$5' / 2", 2.5d)]
    [InlineData("'2' ^ '3'", 8d)]
    [InlineData("TRUE - '1'", -2d)]
    public void Text_is_read_as_a_number(string expression, double expected) =>
        Assert.Equal(expected, Assert.IsType<double>(Scalar(expression)));

    [Theory]
    [InlineData("'(5)' \\ 2", -2)]
    [InlineData("'1,000' MOD 7", 6)]
    [InlineData("'2.5' \\ 1", 2)]
    [InlineData("'3.5' MOD 2", 0)]
    public void Text_is_read_as_a_number_before_rounding_for_integer_division(string expression, int expected) =>
        Assert.Equal(expected, Assert.IsType<int>(Scalar(expression)));

    [Theory]
    [InlineData("'abc' - 1")]
    [InlineData("' ' * 1")]
    [InlineData("'abc' * NULL")]
    [InlineData("NULL - S")]
    [InlineData("G / NULL")]
    [InlineData("B MOD NULL")]
    [InlineData("NULL ^ G")]
    [InlineData("N \\ G")]
    [InlineData("D - B")]
    public void Text_that_is_not_a_number_or_a_guid_or_binary_value_is_a_type_mismatch_even_with_null(string expression) =>
        Assert.Throws<InvalidCastException>(() => Scalar(expression));

    [Theory]
    [InlineData("'1' * NULL")]
    [InlineData("NT - 1")]
    [InlineData("NULL \\ '1'")]
    public void Null_propagates_once_the_operands_are_numbers(string expression) =>
        Assert.Null(Scalar(expression));

    [Theory]
    [InlineData("D / 2", 21916.25d)]
    [InlineData("D ^ 1", 43832.5d)]
    [InlineData("D * 2", 87665d)]
    [InlineData("D - D", 0d)]
    [InlineData("D - #2020-01-01#", 1.5d)]
    public void A_date_is_its_serial_where_the_result_is_a_number(string expression, double expected) =>
        Assert.Equal(expected, Assert.IsType<double>(Scalar(expression)));

    [Theory]
    [InlineData("D \\ 2", 21916)]
    [InlineData("D MOD 7", 5)]
    [InlineData("1 MOD D", 1)]
    public void A_date_rounds_to_its_serial_for_integer_division(string expression, int expected) =>
        Assert.Equal(expected, Assert.IsType<int>(Scalar(expression)));

    [Theory]
    [InlineData("D - 1", "2020-01-01T12:00:00")]
    [InlineData("1 - D", "1779-12-28T12:00:00")]
    [InlineData("D - '1'", "2020-01-01T12:00:00")]
    [InlineData("'1' - D", "1779-12-28T12:00:00")]
    public void Subtracting_a_number_and_a_date_is_a_date(string expression, string expected) =>
        Assert.Equal(DateTime.Parse(expected, CultureInfo.InvariantCulture), Assert.IsType<DateTime>(Scalar(expression)));

    [Theory]
    [InlineData("1 / 0")]
    [InlineData("0 / 0")]
    [InlineData("1.5 / 0")]
    [InlineData("SG / 0")]
    [InlineData("DC / 0")]
    [InlineData("CY / 0")]
    [InlineData("1 \\ 0")]
    [InlineData("1 MOD 0")]
    [InlineData("0 ^ -1")]
    public void Dividing_by_zero_is_an_error(string expression) =>
        Assert.Throws<DivideByZeroException>(() => Scalar(expression));

    [Theory]
    [InlineData("2147483647 * 2")]
    [InlineData("N * 2147483647")]
    [InlineData("-2147483647 - 2")]
    [InlineData("N - -2147483647")]
    [InlineData("1E300 * 1E300")]
    [InlineData("10 ^ 400")]
    [InlineData("2 ^ 1024")]
    [InlineData("'1e400' * 1")]
    [InlineData("CLNG(-2147483648) \\ -1")]
    [InlineData("2147483647.5 \\ 1")]
    [InlineData("1E10 \\ 1")]
    [InlineData("#0100-01-01# - 1")]
    [InlineData("#9999-12-31# - -1")]
    public void A_result_past_its_type_is_an_overflow(string expression) =>
        Assert.Throws<OverflowException>(() => Scalar(expression));

    [Theory]
    [InlineData("(-8) ^ (1/3)")]
    [InlineData("(-2) ^ 0.5")]
    public void A_negative_base_with_a_fractional_exponent_is_an_invalid_procedure_call(string expression) =>
        Assert.Throws<ArgumentException>(() => Scalar(expression));

    [Fact]
    public void The_smallest_long_mod_minus_one_is_zero() =>
        Assert.Equal(0, Scalar("CLNG(-2147483648) MOD -1"));

    [Theory]
    [InlineData("2.5 \\ 1", 2)]
    [InlineData("3.5 \\ 1", 4)]
    [InlineData("-2.5 \\ 1", -2)]
    [InlineData("0.5 \\ 1", 0)]
    [InlineData("-7 \\ 2", -3)]
    [InlineData("7 \\ -2", -3)]
    [InlineData("7.5 MOD 2", 0)]
    [InlineData("19 MOD 6.7", 5)]
    [InlineData("12.6 MOD 5", 3)]
    [InlineData("12 MOD 4.3", 0)]
    [InlineData("-7 MOD 3", -1)]
    [InlineData("7 MOD -3", 1)]
    [InlineData("SG \\ 1", 2)]
    [InlineData("CY \\ 1", 3)]
    [InlineData("DC \\ 1", 4)]
    public void Integer_division_rounds_its_operands_half_to_even_and_truncates(string expression, int expected) =>
        Assert.Equal(expected, Assert.IsType<int>(Scalar(expression)));

    [Theory]
    [InlineData("TRUE / SG", -0.6666666865348816d)]
    [InlineData("SI / SG", 1.3333333730697632d)]
    public void A_single_divided_with_integers_or_booleans_divides_in_single_precision(string expression, double expected) =>
        Assert.Equal(expected, Assert.IsType<double>(Scalar(expression)));

    [Theory]
    [InlineData("1 / 1.5", 0.6d)]
    [InlineData("1.5 / '2.5'", 0.6d)]
    [InlineData("1.5 * 1.5", 2.2d)]
    [InlineData("1.5 * 1.5 * 1.5", 3.3d)]
    [InlineData("459.35 / 3", 153.11d)]
    [InlineData("40000 / 0.3", 133333.3d)]
    [InlineData("D * 0.25", 10958.12d)]
    [InlineData("1.5 / LEFT('12', 2)", 0.1d)]
    [InlineData("CSTR(5) / 1.5", 3.3d)]
    [InlineData("1.5 * 1.25", 1.875d)]
    [InlineData("459.35 * 334.90", 153836.315d)]
    [InlineData("1.1 * 1.11", 1.2210000000000003d)]
    [InlineData("1 / 3.0", 0.3333333333333333d)]
    public void A_number_written_with_a_decimal_point_keeps_its_places_unless_another_decimal_has_different_ones(
        string expression, double expected) =>
        Assert.Equal(expected, Assert.IsType<double>(Scalar(expression)));

    [Theory]
    [InlineData("1 / DC", "0.2222")]
    [InlineData("TRUE / DC", "-0.2222")]
    [InlineData("DC / 40000", "0.0001")]
    [InlineData("DC / '7'", "0.6428")]
    [InlineData("DC * DC", "20.2500")]
    [InlineData("D / DC", "9740.5555")]
    [InlineData("CY * CY", "10.5625")]
    [InlineData("CY / 3", "1.0833333333333333333333333333")]
    [InlineData("1.5 / DC", "0.3333333333333333333333333333")]
    public void A_decimal_column_keeps_its_places_but_currency_divided_is_whole(string expression, string expected) =>
        Assert.Equal(expected, Assert.IsType<decimal>(Scalar(expression)).ToString(CultureInfo.InvariantCulture));

    [Fact]
    public void A_decimal_literal_meets_a_decimal_as_written() =>
        Assert.Equal(1507.05m, Assert.IsType<decimal>(Scalar("DC * 334.90")));

    [Theory]
    [InlineData("SELECT d.X / 7 FROM (SELECT DC AS X FROM T) AS d", "0.6428")]
    [InlineData("SELECT d.X / 3 FROM (SELECT CY AS X FROM T) AS d", "1.0833333333333333333333333333")]
    [InlineData("SELECT SUM(DC) / 7 FROM T", "0.6428")]
    public void A_column_keeps_its_decimal_kind_through_a_derived_table_and_an_aggregate(string sql, string expected) =>
        Assert.Equal(expected, Assert.IsType<decimal>(Query(sql)).ToString(CultureInfo.InvariantCulture));

    [Fact]
    public void Only_the_result_column_is_cut_not_an_argument() =>
        Assert.Equal(-188.71233644010991d, Assert.IsType<double>(Scalar("PMT(0.05 / 12, 60, 10000)")), 9);
}
