using System.Globalization;
using LibRed;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

// A value stored into a number or Yes/No column is converted as ACE converts it: text is read as VBA reads text as a
// number, in the regional separators and currency symbol; a whole-number column rounds half to even; a Byte keeps the
// low byte of the whole number; a Yes/No column rounds half to even and stores whether that is 0. Measured against
// ACE under en-AU, each value read back from the file ACE wrote. They used to convert with .NET's invariant parsing,
// which refused all of this text, overflowed a Byte, and made 0.5 True.
public class StoredNumberConversionTests : TempDatabaseTest
{
    private static readonly CultureInfo EnAu = CultureInfo.GetCultureInfo("en-AU");

    private static QueryEngine Fresh(params string[] setup)
    {
        string path = TemporaryDatabase.CreatePath("stored-number-");
        JetDatabase.Create(path);
        var engine = new QueryEngine(TemporaryDatabase.OpenTracked(path, readOnly: false));
        foreach (string statement in setup) engine.ExecuteNonQuery(statement);
        return engine;
    }

    private static void Execute(QueryEngine engine, string sql) =>
        SharedDatabase.Under(EnAu, () => engine.ExecuteNonQuery(sql));

    private static object? Stored(string type, string value)
    {
        QueryEngine engine = Fresh($"CREATE TABLE T (Id COUNTER PRIMARY KEY, V {type})");
        Execute(engine, $"INSERT INTO T (V) VALUES ({value})");
        return engine.ExecuteQuery("SELECT V FROM T").Rows.Single()[0];
    }

    [Theory]
    [InlineData("LONG", "'1.5'", "2")]
    [InlineData("LONG", "'2.5'", "2")]
    [InlineData("LONG", "'3.5'", "4")]
    [InlineData("LONG", "'-2.5'", "-2")]
    [InlineData("LONG", "'1,000'", "1000")]
    [InlineData("LONG", "'1,5'", "15")]
    [InlineData("LONG", "'$5'", "5")]
    [InlineData("LONG", "'($5)'", "-5")]
    [InlineData("LONG", "'(5)'", "-5")]
    [InlineData("LONG", "'5-'", "-5")]
    [InlineData("LONG", "'- 5'", "-5")]
    [InlineData("LONG", "'&H10'", "16")]
    [InlineData("LONG", "'&O10'", "8")]
    [InlineData("LONG", "'1e2'", "100")]
    [InlineData("LONG", "'1.5E1'", "15")]
    [InlineData("LONG", "'1d2'", "100")]
    [InlineData("LONG", "'1.23456'", "1")]
    [InlineData("SHORT", "'1,000'", "1000")]
    [InlineData("SHORT", "'(5)'", "-5")]
    [InlineData("DOUBLE", "'$5'", "5")]
    [InlineData("DOUBLE", "'5-'", "-5")]
    [InlineData("DOUBLE", "'&H10'", "16")]
    [InlineData("DOUBLE", "'1d2'", "100")]
    [InlineData("REAL", "'(5)'", "-5")]
    [InlineData("CURRENCY", "'$5'", "5")]
    [InlineData("CURRENCY", "'(5)'", "-5")]
    [InlineData("CURRENCY", "'1d2'", "100")]
    [InlineData("DECIMAL(18,4)", "'1e2'", "100.0000")]
    [InlineData("DECIMAL(18,4)", "'($5)'", "-5.0000")]
    public void Text_into_a_number_column_is_read_as_VBA_reads_a_number(string type, string value, string stored) =>
        Assert.Equal(stored, Convert.ToString(Stored(type, value), CultureInfo.InvariantCulture));

    [Theory]
    [InlineData("256", 0)]
    [InlineData("-1", 255)]
    [InlineData("300", 44)]
    [InlineData("255.5", 0)]
    [InlineData("-0.5", 0)]
    [InlineData("CLNG(256)", 0)]
    [InlineData("'256'", 0)]
    [InlineData("'-1'", 255)]
    [InlineData("'1,000'", 232)]
    [InlineData("'-2.5'", 254)]
    [InlineData("'1.5'", 2)]
    public void A_byte_keeps_the_low_byte_of_the_whole_number(string value, byte stored) =>
        Assert.Equal(stored, Stored("BYTE", value));

    [Theory]
    [InlineData("0.5", false)]
    [InlineData("-0.5", false)]
    [InlineData("0.4", false)]
    [InlineData("0.0001", false)]
    [InlineData("CCUR(0.5)", false)]
    [InlineData("0.6", true)]
    [InlineData("-0.6", true)]
    [InlineData("1.5", true)]
    [InlineData("2.5", true)]
    [InlineData("'5'", true)]
    [InlineData("'0'", false)]
    [InlineData("'0.5'", false)]
    [InlineData("'0.6'", true)]
    [InlineData("'$5'", true)]
    public void A_yes_no_column_stores_whether_the_rounded_number_is_not_zero(string value, bool stored) =>
        Assert.Equal(stored, Stored("YESNO", value));

    [Theory]
    [InlineData("YESNO", "'True'")]
    [InlineData("YESNO", "'Yes'")]
    [InlineData("LONG", "'abc'")]
    [InlineData("SHORT", "'32768'")]
    [InlineData("LONG", "'2147483648'")]
    public void Text_that_is_not_a_number_or_does_not_fit_is_refused(string type, string value) =>
        Assert.ThrowsAny<Exception>(() => Stored(type, value));

    [Theory]
    [InlineData("SHORT", new[] { "5", "2", "5", "-5", "-5", "1000", "16", "100", "-1", "0" })]
    [InlineData("LONG", new[] { "5", "2", "5", "-5", "-5", "1000", "16", "100", "-1", "0" })]
    [InlineData("DOUBLE", new[] { "5", "2.5", "5", "-5", "-5", "1000", "16", "100", "-1", "0" })]
    [InlineData("CURRENCY", new[] { "5", "2.5", "5", "-5", "-5", "1000", "16", "100", "-1", "0" })]
    [InlineData("YESNO", new[] { "True", "True", "True", "True", "True", "True", "True", "True", "True", "False" })]
    public void Altering_text_to_a_number_reads_each_value_as_VBA_reads_a_number(string type, string[] stored)
    {
        QueryEngine engine = Fresh("CREATE TABLE T (Id COUNTER PRIMARY KEY, X TEXT(30))");
        foreach (string text in new[] { "5", "2.5", "$5", "(5)", "5-", "1,000", "&H10", "1e2", "-1", "0" })
            engine.ExecuteNonQuery($"INSERT INTO T (X) VALUES ('{text}')");
        Execute(engine, $"ALTER TABLE T ALTER COLUMN X {type}");
        Assert.Equal(stored, engine.ExecuteQuery("SELECT X FROM T ORDER BY Id").Rows
            .Select(r => Convert.ToString(r[0], CultureInfo.InvariantCulture)));
    }

    // A range bound on an indexed whole number is converted to the column's type, and one that would lose its fraction
    // is ACE's "Data type mismatch"; the same column unindexed compares exactly. Measured against ACE, over 0..4, 254, 255.
    [Theory]
    [InlineData("B > CDbl(2.5)")]
    [InlineData("L > CDbl(2.5)")]
    [InlineData("L > CSng(2.5)")]
    [InlineData("B > CCur(2.5)")]
    public void A_fractional_range_bound_on_an_indexed_whole_number_is_a_type_mismatch(string where)
    {
        QueryEngine engine = Fresh(
            "CREATE TABLE V (Id COUNTER PRIMARY KEY, B BYTE, L LONG)", "CREATE INDEX IB ON V (B)", "CREATE INDEX IL ON V (L)");
        foreach (int v in new[] { 0, 1, 2, 3, 4, 254, 255 })
            engine.ExecuteNonQuery($"INSERT INTO V (B, L) VALUES ({v}, {v})");
        Assert.Throws<InvalidCastException>(() => engine.ExecuteQuery($"SELECT Id FROM V WHERE {where}").Rows.ToList());
    }

    [Fact]
    public void A_fractional_bound_on_an_unindexed_whole_number_compares_exactly()
    {
        QueryEngine engine = Fresh("CREATE TABLE V (Id COUNTER PRIMARY KEY, C BYTE)");
        foreach (int v in new[] { 0, 1, 2, 3, 4, 254, 255 })
            engine.ExecuteNonQuery($"INSERT INTO V (C) VALUES ({v})");
        Assert.Equal([3, 4, 254, 255], engine.ExecuteQuery("SELECT C FROM V WHERE C > CDbl(2.5) ORDER BY Id").Rows
            .Select(r => Convert.ToInt32(r[0], CultureInfo.InvariantCulture)));
    }

    [Theory]
    [InlineData("B = 300")]
    [InlineData("B = 256")]
    [InlineData("B = -1")]
    [InlineData("L = 2147483650")]
    public void An_indexed_whole_number_compared_with_a_value_past_its_range_selects_nothing(string where)
    {
        QueryEngine engine = Fresh(
            "CREATE TABLE V (Id COUNTER PRIMARY KEY, B BYTE, L LONG)", "CREATE INDEX IB ON V (B)",
            "CREATE INDEX IL ON V (L)", "INSERT INTO V (B, L) VALUES (44, 2)", "INSERT INTO V (B, L) VALUES (0, 3)");
        Assert.Empty(engine.ExecuteQuery($"SELECT Id FROM V WHERE {where}").Rows);
    }
}
