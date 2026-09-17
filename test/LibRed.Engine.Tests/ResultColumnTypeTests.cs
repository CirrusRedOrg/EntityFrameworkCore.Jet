using System.Globalization;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// An expression that chooses among values — CASE, IIF, COALESCE — and a set operation declare one type for their
/// column, and every value comes back as that type. CASE, IIF and COALESCE widen numbers on one ladder; a UNION
/// types its columns as ACE does (verified vs ACE), including its binary and text columns for mixed kinds.
/// </summary>
public class ResultColumnTypeTests(ResultColumnTypeTests.Database database)
    : TempDatabaseTest, IClassFixture<ResultColumnTypeTests.Database>
{
    private static readonly string[] Setup =
    [
        "CREATE TABLE T (Id INT, M CURRENCY, B BYTE, S SMALLINT, D DATETIME, F DOUBLE, R REAL, E DECIMAL(18,4), "
            + "L LONG, X TEXT(10), Y YESNO, G GUID, N BINARY(4))",
        "INSERT INTO T (Id, M, B, S, D, F, R, E, L, X, Y) "
            + "VALUES (1, 10.5, 3, 7, #2020-01-02#, 1.5, 1.5, 4.25, 70000, 'abc', TRUE)",
        "INSERT INTO T (Id, M, B, S, D, F, R, E, L, X, Y) "
            + "VALUES (2, 2, 4, 8, #1899-12-30 06:00:00#, 2.5, 0.75, 5.5, 200000, 'xyz', FALSE)",
        "UPDATE T SET G = {00112233-4455-6677-8899-AABBCCDDEEFF}",
        "UPDATE T SET N = 0x41004200",
    ];

    public sealed class Database() : SharedDatabase("result-types-", Setup);

    /// <summary>The query's last column: its declared type, and its values, which must all be of that type.</summary>
    private (Type Declared, object?[] Values) Column(string sql)
    {
        var (types, rows) = database.Query(sql, CultureInfo.GetCultureInfo("en-US"));
        Type declared = types[^1];
        object?[] values = rows.Select(row => row[^1]).ToArray();
        Assert.All(values, value => Assert.True(value is null || value.GetType() == declared,
            $"{value} is a {value?.GetType().Name}, declared {declared.Name}"));
        return (declared, values);
    }

    private static string Union(string left, string right) =>
        $"SELECT {left} AS c FROM T WHERE Id = 1 UNION ALL SELECT {right} FROM T WHERE Id = 1";

    [Theory]
    [InlineData("CASE WHEN M >= 5.1 THEN M ELSE 5.1 END", typeof(double))]
    [InlineData("IIF(M >= 5.1, M, 5.1)", typeof(double))]
    [InlineData("CASE WHEN Id = 1 THEN Id ELSE F END", typeof(double))]
    [InlineData("IIF(Id = 1, B, S)", typeof(short))]
    [InlineData("IIF(Id = 1, B, 5)", typeof(int))]
    [InlineData("IIF(Id = 1, R, S)", typeof(float))]
    [InlineData("IIF(Id = 1, R, L)", typeof(double))]
    [InlineData("IIF(Id = 1, M, L)", typeof(decimal))]
    [InlineData("IIF(Id = 1, NULL, B)", typeof(byte))]
    [InlineData("COALESCE(NULL, B, 5)", typeof(int))]
    [InlineData("CASE WHEN Id = 1 THEN NULL ELSE S END", typeof(short))]
    public void A_choice_of_numbers_widens_on_one_ladder(string expression, Type expected) =>
        Assert.Equal(expected, Column($"SELECT Id, {expression} AS c FROM T ORDER BY Id").Declared);

    [Theory]
    [InlineData("B", "5", typeof(int))]
    [InlineData("5", "B", typeof(int))]
    [InlineData("S", "70000", typeof(int))]
    [InlineData("B", "S", typeof(short))]
    [InlineData("NULL", "B", typeof(byte))]
    [InlineData("B", "NULL", typeof(byte))]
    [InlineData("M", "F", typeof(double))]
    [InlineData("E", "F", typeof(double))]
    [InlineData("M", "E", typeof(decimal))]
    [InlineData("M", "L", typeof(decimal))]
    [InlineData("R", "S", typeof(float))]
    [InlineData("R", "L", typeof(double))]
    [InlineData("Y", "B", typeof(short))]
    [InlineData("Y", "F", typeof(double))]
    [InlineData("Y", "Y", typeof(bool))]
    [InlineData("D", "D", typeof(DateTime))]
    [InlineData("G", "G", typeof(Guid))]
    public void A_union_types_its_columns_as_ace_does(string left, string right, Type expected) =>
        Assert.Equal(expected, Column(Union(left, right)).Declared);

    [Fact]
    public void A_union_reads_a_boolean_as_minus_one()
    {
        (_, object?[] values) = Column(Union("Y", "B"));
        Assert.Equal(new object?[] { (short)-1, (short)3 }, values);
    }

    [Theory]
    [InlineData("X", "B", "abc", "3")]
    [InlineData("Y", "X", "-1", "abc")]
    [InlineData("D", "F", "1/2/2020", "1.5")]
    [InlineData("D", "X", "1/2/2020", "abc")]
    [InlineData("Y", "D", "-1", "1/2/2020")]
    public void A_union_of_text_or_a_date_with_another_kind_is_text(string left, string right, string first, string second)
    {
        (Type declared, object?[] values) = Column(Union(left, right));
        Assert.Equal(typeof(string), declared);
        Assert.Equal(new object?[] { first, second }, values);
    }

    [Theory]
    [InlineData("G", "X", "33221100554477668899AABBCCDDEEFF", "610062006300")]
    [InlineData("G", "B", "33221100554477668899AABBCCDDEEFF", "03000000")]
    [InlineData("N", "S", "41004200", "0700")]
    [InlineData("N", "L", "41004200", "70110100")]
    [InlineData("N", "F", "41004200", "000000000000F83F")]
    [InlineData("N", "R", "41004200", "0000C03F")]
    [InlineData("N", "D", "41004200", "000000000067E540")]
    [InlineData("N", "Y", "41004200", "FFFF")]
    [InlineData("N", "M", "41004200", "289A010000000000")]
    [InlineData("N", "E", "41004200", "34002E0032003500")]
    public void A_union_of_binary_or_a_guid_with_another_kind_is_binary(string left, string right, string first, string second)
    {
        (Type declared, object?[] values) = Column(Union(left, right));
        Assert.Equal(typeof(byte[]), declared);
        Assert.Equal([first, second], values.Select(v => Convert.ToHexString((byte[])v!)));
    }

    [Fact]
    public void A_union_compares_values_after_converting_them()
    {
        (_, object?[] values) = Column("SELECT B AS c FROM T WHERE Id = 1 UNION SELECT 3 FROM T WHERE Id = 1");
        Assert.Equal(new object?[] { 3 }, values);
    }

    [Fact]
    public void Values_keep_their_widened_value()
    {
        (_, object?[] values) = Column("SELECT Id, IIF(M >= 5.1, M, 5.1) AS c FROM T ORDER BY Id");
        Assert.Equal(new object?[] { 10.5, 5.1 }, values);
    }
}
