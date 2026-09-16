using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// The logical operators <c>NOT</c> <c>AND</c> <c>OR</c> <c>XOR</c> <c>EQV</c> <c>IMP</c> and a <c>WHERE</c>
/// condition: every value is False when it reads as 0 and True otherwise, Null follows the VBA truth tables, and the
/// operators bind in that order. The expected values were measured against ACE.
/// </summary>
public class LogicalOperatorTests : TempDatabaseTest
{
    private static QueryEngine Fresh()
    {
        string path = TemporaryDatabase.CopyPath(
            Path.Combine(AppContext.BaseDirectory, "Data", "Northwind.accdb"), "logic-ops-");
        var engine = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        engine.ExecuteNonQuery("CREATE TABLE T (Id LONG, TN TEXT(60), NT TEXT(60), D DATETIME, G GUID, B BINARY(4))");
        engine.ExecuteNonQuery("INSERT INTO T (Id, TN, D) VALUES (1, '7', #2020-01-02 12:00:00#)");
        engine.ExecuteNonQuery("UPDATE T SET G = {00112233-4455-6677-8899-AABBCCDDEEFF}");
        engine.ExecuteNonQuery("UPDATE T SET B = 0x41004200");
        return engine;
    }

    private static object? Query(string sql) => Fresh().ExecuteQuery(sql).Rows.First()[0];

    private static object? Scalar(string expression) => Query($"SELECT {expression} FROM T");

    [Theory]
    [InlineData("NOT 10", false)]
    [InlineData("NOT 0", true)]
    [InlineData("NOT 0.5", false)]
    [InlineData("NOT '0'", true)]
    [InlineData("NOT '1'", false)]
    [InlineData("NOT 'abc'", false)]
    [InlineData("NOT ''", false)]
    [InlineData("NOT 'True'", false)]
    [InlineData("NOT TN", false)]
    [InlineData("NOT D", false)]
    [InlineData("NOT G", false)]
    [InlineData("NOT B", false)]
    [InlineData("NOT LEFT('1', 1)", false)]
    [InlineData("NOT UCASE('abc')", false)]
    public void A_value_is_false_only_when_it_reads_as_zero(string expression, bool expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("12 AND 10", true)]
    [InlineData("10 AND '0'", false)]
    [InlineData("10 AND 'abc'", true)]
    [InlineData("10 AND ''", true)]
    [InlineData("10 AND G", true)]
    [InlineData("D AND 1", true)]
    [InlineData("0 AND NULL", false)]
    [InlineData("NULL AND 0", false)]
    [InlineData("12 OR 10", true)]
    [InlineData("0 OR '0'", false)]
    [InlineData("1 OR NULL", true)]
    [InlineData("NULL OR -1", true)]
    [InlineData("12 XOR 10", false)]
    [InlineData("FALSE XOR TRUE", true)]
    [InlineData("'0' XOR 'abc'", true)]
    [InlineData("12 EQV 10", true)]
    [InlineData("TRUE EQV FALSE", false)]
    [InlineData("FALSE EQV '0'", true)]
    [InlineData("TRUE IMP FALSE", false)]
    [InlineData("FALSE IMP FALSE", true)]
    [InlineData("0 IMP NULL", true)]
    [InlineData("NULL IMP -1", true)]
    [InlineData("NULL IMP 'abc'", true)]
    public void The_operators_are_logical_not_bitwise(string expression, bool expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("NOT NULL")]
    [InlineData("NOT NT")]
    [InlineData("1 AND NULL")]
    [InlineData("NULL AND 0.5")]
    [InlineData("0 OR NULL")]
    [InlineData("NULL XOR 0")]
    [InlineData("NULL EQV -1")]
    [InlineData("-1 IMP NULL")]
    [InlineData("NULL IMP 0")]
    [InlineData("NULL IMP '0'")]
    [InlineData("NULL IMP NULL")]
    public void Null_follows_the_truth_tables(string expression) =>
        Assert.Null(Scalar(expression));

    [Theory]
    [InlineData("1 = 1 OR 1 = 2 AND 1 = 2", true)]
    [InlineData("(1 = 1 OR 1 = 2) AND 1 = 2", false)]
    [InlineData("NOT 1 = 1 OR 1 = 1", true)]
    [InlineData("NOT TRUE AND FALSE", false)]
    [InlineData("TRUE XOR TRUE OR TRUE", false)]
    [InlineData("TRUE OR TRUE XOR TRUE", false)]
    [InlineData("TRUE EQV FALSE XOR TRUE", true)]
    [InlineData("FALSE IMP FALSE EQV FALSE", true)]
    [InlineData("TRUE IMP FALSE IMP FALSE", true)]
    [InlineData("10 AND 8 = 8", true)]
    [InlineData("(10 AND 8) = 8", false)]
    public void Not_and_or_xor_eqv_imp_bind_in_that_order_left_to_right(string expression, bool expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("'abc'", 1)]
    [InlineData("''", 1)]
    [InlineData("'0'", 0)]
    [InlineData("TN", 1)]
    [InlineData("D", 1)]
    [InlineData("G", 1)]
    [InlineData("NT", 0)]
    [InlineData("NOT '0'", 1)]
    [InlineData("NOT NULL", 0)]
    [InlineData("1 AND NULL", 0)]
    [InlineData("NULL IMP -1", 1)]
    [InlineData("4 IMP NULL", 0)]
    public void A_where_condition_uses_the_same_truth(string condition, int expected) =>
        Assert.Equal(expected, Query($"SELECT COUNT(*) FROM T WHERE {condition}"));
}
