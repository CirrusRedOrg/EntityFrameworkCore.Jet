using System.Globalization;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// SQL Server's <c>DATALENGTH</c>: the bytes a value takes as Access stores it. Access has no such function; this is
/// a LibRed extension.
/// </summary>
public class DataLengthTests(DataLengthTests.Database database)
    : TempDatabaseTest, IClassFixture<DataLengthTests.Database>
{
    private static readonly string[] Setup =
    [
        "CREATE TABLE D (Id LONG, BT BYTE, SI SHORT, LG LONG, BI BIGINT, SG REAL, DB FLOAT, CY CURRENCY, "
            + "DC DECIMAL(18,4), TX TEXT(20), MM MEMO, DT DATETIME, YN YESNO, GD GUID, BN BINARY(10), VB VARBINARY(10))",
        "INSERT INTO D (Id, BT, SI, LG, BI, SG, DB, CY, DC, TX, MM, DT, YN, GD, BN, VB) VALUES (1, 1, 2, 3, 4, 1.5, 2.5, 3.25, "
            + "4.5, 'abc ', 'hello', #2020-01-02#, TRUE, {00112233-4455-6677-8899-AABBCCDDEEFF}, 0x010203, 0x010203)",
        "INSERT INTO D (Id, TX) VALUES (2, '')",
    ];

    public sealed class Database() : SharedDatabase("datalength-", Setup);

    private object? Scalar(string expression, int id = 1) =>
        database.Scalar($"SELECT {expression} FROM D WHERE Id = {id}", CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("BT", 1)]
    [InlineData("SI", 2)]
    [InlineData("LG", 4)]
    [InlineData("BI", 8)]
    [InlineData("SG", 4)]
    [InlineData("DB", 8)]
    [InlineData("CY", 8)]
    [InlineData("DC", 17)]
    [InlineData("DT", 8)]
    [InlineData("YN", 1)]
    [InlineData("GD", 16)]
    public void A_value_takes_the_size_its_type_is_stored_in(string column, int expected) =>
        Assert.Equal(expected, Scalar($"DATALENGTH({column})"));

    [Theory]
    [InlineData("TX", 8)]          // 'abc ': the trailing space counts
    [InlineData("MM", 10)]
    [InlineData("'héllo'", 10)]
    [InlineData("ChrW(12288)", 2)]
    [InlineData("VB", 3)]          // variable binary: its own length
    [InlineData("BN", 10)]         // fixed binary: stored padded to the column's width
    [InlineData("0x010203", 3)]
    public void Text_is_two_bytes_a_character_and_binary_its_length(string expression, int expected) =>
        Assert.Equal(expected, Scalar($"DATALENGTH({expression})"));

    [Fact]
    public void Empty_text_is_zero_bytes() => Assert.Equal(0, Scalar("DATALENGTH(TX)", id: 2));

    [Theory]
    [InlineData("1", 4)]
    [InlineData("CCUR(1)", 8)]
    [InlineData("CDEC(1)", 17)]
    [InlineData("LG + 1", 4)]
    public void An_expression_has_its_result_types_size(string expression, int expected) =>
        Assert.Equal(expected, Scalar($"DATALENGTH({expression})"));

    [Fact]
    public void Null_has_no_length()
    {
        Assert.Null(Scalar("DATALENGTH(NULL)"));
        Assert.Null(Scalar("DATALENGTH(BT)", id: 2));
    }

    [Fact]
    public void The_column_is_a_long() =>
        Assert.Equal(typeof(int), database.Query("SELECT DATALENGTH(TX) FROM D", CultureInfo.InvariantCulture).ColumnTypes[0]);
}
