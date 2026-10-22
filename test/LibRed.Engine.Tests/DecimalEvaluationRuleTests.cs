using System.Globalization;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// The two ways ACE works a value out. A comparison, a logical operator and a function's arguments go to its
/// expression service, OLE Automation arithmetic in which a Decimal beats a Double. A result value — a column, a sort
/// or group key, an aggregate's argument, a value written — is the same but for a written number beside which the
/// result is a Double, which is a Double first. Every expectation was measured against ACE on these tables
/// (AceDecimalPrecisionProbeTest.Evaluation_rule_probe).
/// </summary>
public class DecimalEvaluationRuleTests(DecimalEvaluationRuleTests.Database database)
    : TempDatabaseTest, IClassFixture<DecimalEvaluationRuleTests.Database>
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    private static readonly string[] Setup =
    [
        "CREATE TABLE T (F DOUBLE, S REAL, L LONG, C CURRENCY, D DECIMAL(18,4))",
        "INSERT INTO T (F, S, L, C, D) VALUES (0.1, 0.1, 3, 1.1, 1.1)",
        "CREATE TABLE W (F DOUBLE, X DOUBLE, Y DECIMAL(28,10))",
        "INSERT INTO W (F) VALUES (0.1)",
        "UPDATE W SET X = F * 1.11, Y = F * 1.11",
        "INSERT INTO W (F, X, Y) SELECT F, F * 1.11, F * 1.11 FROM T",
        "INSERT INTO W (F, X, Y) VALUES (1, 0.1 * 1.11, 0.1 * 1.11)",
        "CREATE TABLE O (K LONG, F DOUBLE, G DOUBLE)",
        "INSERT INTO O (K, F, G) VALUES (1, 0, 0)",
        "INSERT INTO O (K, F, G) VALUES (2, 0.1, 0.111)",
    ];

    public sealed class Database() : SharedDatabase("decimal-rule-", Setup);

    private object? Query(string sql) => database.Scalar(sql, EnUs);

    private object?[] Column(string sql) => [.. database.Engine.ExecuteQuery(sql).Rows.Select(r => r[0])];

    // A result whose type is a Double works a written number out as a Double: 0.1 * 1.11 in Doubles.
    [Theory]
    [InlineData("SELECT F * 1.11 FROM T", 0.11100000000000002d)]
    [InlineData("SELECT 1.11 * F FROM T", 0.11100000000000002d)]
    [InlineData("SELECT F + 1.11 FROM T", 1.2100000000000002d)]
    [InlineData("SELECT S * 1.11 FROM T", 0.1110000016540289d)]
    [InlineData("SELECT C * 1.11 FROM T", 1.2210000000000003d)]
    [InlineData("SELECT 1.11 + 0.3 FROM T", 1.4100000000000001d)]
    [InlineData("SELECT (F * 1.11) * 1 FROM T", 0.11100000000000002d)]
    [InlineData("SELECT F * 1.11 + 0 FROM T", 0.11100000000000002d)]
    [InlineData("SELECT x.V FROM (SELECT F * 1.11 AS V FROM T) AS x", 0.11100000000000002d)]
    [InlineData("SELECT SUM(F * 1.11) FROM T", 0.11100000000000002d)]
    [InlineData("SELECT F * 1.11 * L FROM T", 0.3330000000000001d)]
    [InlineData("SELECT -1.11 * F FROM T", -0.11100000000000002d)]
    [InlineData("SELECT 1.11 * 0.3 * L FROM T", 0.9990000000000001d)]
    public void A_double_result_takes_a_written_number_as_a_double(string sql, double expected) =>
        Assert.Equal(expected, Assert.IsType<double>(Query(sql)));

    // Where the parts are Decimals first and then made a Double, the Double is the Decimal's: 1.221, not 1.2210000000000003.
    [Theory]
    [InlineData("SELECT D * 1.11 FROM T", 1.221d)]
    [InlineData("SELECT 1.11 * 0.3 FROM T", 0.333d)]
    [InlineData("SELECT F * (L * 1.11) FROM T", 0.333d)]
    [InlineData("SELECT L * 1.11 + F FROM T", 3.43d)]
    [InlineData("SELECT D * 1.11 + F FROM T", 1.321d)]
    [InlineData("SELECT IIf(L = 3, F * 1.11, 0) FROM T", 0.111d)]
    public void A_decimal_made_a_double_keeps_its_digits(string sql, double expected) =>
        Assert.Equal(expected, Assert.IsType<double>(Query(sql)));

    // A Decimal result stays exact.
    [Theory]
    [InlineData("SELECT L * 1.11 FROM T", "3.33")]
    [InlineData("SELECT L / 1.11 FROM T", "2.70")]
    [InlineData("SELECT D * L FROM T", "3.3000")]
    [InlineData("SELECT C + 1.11 FROM T", "2.2100")]
    [InlineData("SELECT L * 0.3 FROM T", "0.9")]
    public void A_decimal_result_is_exact(string sql, string expected) =>
        Assert.Equal(expected, Assert.IsType<decimal>(Query(sql)).ToString(CultureInfo.InvariantCulture));

    // A comparison goes to the expression service, where F * 1.11 is the Decimal 0.111.
    [Theory]
    [InlineData("SELECT COUNT(*) FROM T WHERE F * 1.11 = 0.111", 1)]
    [InlineData("SELECT COUNT(*) FROM T WHERE F * 1.11 > 0.111", 0)]
    [InlineData("SELECT COUNT(*) FROM T WHERE F < 0.10000000000000001", 1)]
    [InlineData("SELECT COUNT(*) FROM T WHERE F = 0.1", 1)]
    [InlineData("SELECT COUNT(*) FROM T WHERE 1.1 * 1.11 = 1.221", 1)]
    [InlineData("SELECT COUNT(*) FROM T WHERE S = 0.1", 1)]
    [InlineData("SELECT COUNT(*) FROM T WHERE F * 1.5 = 0.15", 1)]
    [InlineData("SELECT COUNT(*) FROM O WHERE F * 1.11 - G = 0", 2)]
    public void A_comparison_works_a_written_number_out_as_a_decimal(string sql, int expected) =>
        Assert.Equal(expected, Convert.ToInt32(Query(sql), CultureInfo.InvariantCulture));

    [Fact]
    public void A_comparison_in_the_select_list_and_a_function_argument_do_too()
    {
        Assert.Equal(true, Query("SELECT F * 1.11 = 0.111 FROM T"));
        Assert.Equal(true, Query("SELECT 1.1 * 1.11 = 1.221 FROM T"));
        Assert.Equal("eq", Query("SELECT IIf(F * 1.11 = 0.111, 'eq', 'ne') FROM T"));
        Assert.Equal("0.111", Query("SELECT CStr(F * 1.11) FROM T"));
    }

    // A value written — by UPDATE, INSERT … SELECT or INSERT … VALUES — is a result value: the Double column takes the
    // Doubles' product, the Decimal column the Decimals'.
    [Fact]
    public void A_value_written_is_a_result_value()
    {
        object?[][] rows = [.. database.Engine.ExecuteQuery("SELECT X, Y FROM W ORDER BY F").Rows];
        Assert.Equal(3, rows.Length);
        foreach (object?[] row in rows)
        {
            Assert.Equal(0.11100000000000002d, Assert.IsType<double>(row[0]));
            Assert.Equal("0.1110000000", Assert.IsType<decimal>(row[1]).ToString(CultureInfo.InvariantCulture));
        }
    }

    // Not ACE's: with the expression over the first table in the FROM clause and a bare column of the second, ACE
    // matches nothing — 0 * 1.5 = 0 included — a defect in its equality lookup (see the LibRed README, "ACE defects
    // LibRed does not reproduce"). LibRed compares as a WHERE does, so the FROM order and the join syntax change
    // nothing.
    [Theory]
    [InlineData("a.F * 1.5 = b.F", "11")]
    [InlineData("a.F * 1.11 - a.G = b.F", "11 21")]
    [InlineData("a.F = b.F * 1.11 - b.G", "11 12")]
    public void An_equality_across_tables_matches_whatever_the_from_order(string condition, string expected)
    {
        string Pairs(string sql) => string.Join(" ", database.Engine.ExecuteQuery(sql).Rows.Select(r => $"{r[0]}{r[1]}"));
        Assert.Equal(expected, Pairs($"SELECT a.K, b.K FROM O AS a INNER JOIN O AS b ON {condition} ORDER BY a.K, b.K"));
        Assert.Equal(expected, Pairs($"SELECT a.K, b.K FROM O AS a, O AS b WHERE {condition} ORDER BY a.K, b.K"));
        Assert.Equal(expected, Pairs($"SELECT a.K, b.K FROM O AS b, O AS a WHERE {condition} ORDER BY a.K, b.K"));
    }

    // A sort, group or HAVING key is a result value too — 1.4E-17 as Doubles on K = 2, 0 as Decimals — except where
    // it is compared.
    [Fact]
    public void A_sort_and_group_key_is_a_result_value()
    {
        Assert.Equal(2, Convert.ToInt32(Query("SELECT TOP 1 K FROM O ORDER BY F * 1.11 - G DESC, K"), CultureInfo.InvariantCulture));
        Assert.Equal(2, Convert.ToInt32(
            Query("SELECT COUNT(*) FROM (SELECT F * 1.11 - G AS V FROM O GROUP BY F * 1.11 - G) AS g"), CultureInfo.InvariantCulture));
        Assert.Equal([1, 2], Column("SELECT K FROM O GROUP BY K HAVING SUM(F) * 1.11 - SUM(G) = 0 ORDER BY K")
            .Select(k => Convert.ToInt32(k, CultureInfo.InvariantCulture)));
    }
}
