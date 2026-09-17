using System.Globalization;
using LibRed.Engine;
using Xunit;

namespace LibRed.Engine.Tests;

/// <summary>
/// <c>IIf</c> <c>Choose</c> <c>Switch</c> <c>IsNumeric</c> <c>IsError</c> <c>TypeName</c> <c>VarType</c>
/// <c>Partition</c> <c>RGB</c> <c>QBColor</c>. The expected values were measured against ACE, except that a Null
/// argument gives Null where ACE raises an error.
/// </summary>
public class ProgramFlowFunctionTests(ProgramFlowFunctionTests.Database database)
    : TempDatabaseTest, IClassFixture<ProgramFlowFunctionTests.Database>
{
    private static readonly string[] Setup =
    [
        "CREATE TABLE T (Id LONG, BT BYTE, SG REAL, YN BIT, CY CURRENCY, DC DECIMAL(18,4), NT TEXT(60), DT DATETIME, G GUID, B BINARY(4))",
        "INSERT INTO T (Id, BT, SG, YN, CY, DC, DT) VALUES (1, 1, 1.5, TRUE, 3.25, 4.5, #2020-01-02 12:00:00#)",
        "UPDATE T SET G = {00112233-4455-6677-8899-AABBCCDDEEFF}",
        "UPDATE T SET B = 0x41004200",
    ];

    public sealed class Database() : SharedDatabase("flow-", Setup);

    private object? Scalar(string expression) =>
        database.Scalar($"SELECT {expression} FROM T", CultureInfo.GetCultureInfo("en-AU"));

    [Theory]
    [InlineData("0.4", "F")]
    [InlineData("-0.4", "F")]
    [InlineData("0.5", "F")]
    [InlineData("0.6", "T")]
    [InlineData("-0.6", "T")]
    [InlineData("1.5", "T")]
    [InlineData("1E+20", "T")]
    [InlineData("CSNG(0.1)", "F")]
    [InlineData("'0.4'", "F")]
    [InlineData("'0'", "F")]
    [InlineData("'abc'", "T")]
    [InlineData("'False'", "T")]
    [InlineData("#1899-12-30 06:00#", "F")]
    [InlineData("DT", "T")]
    [InlineData("G", "T")]
    [InlineData("NULL", "F")]
    [InlineData("CDBL(0.4) AND TRUE", "T")]
    public void Iif_rounds_its_condition_to_a_whole_number(string condition, string expected) =>
        Assert.Equal(expected, Scalar($"IIF({condition}, 'T', 'F')"));

    [Theory]
    [InlineData("IIF(TRUE, 1, 1/0)", 1.0)]
    [InlineData("IIF(FALSE, 1/0, 2)", 2.0)]
    public void Iif_evaluates_only_the_branch_it_takes(string expression, double expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("0.4", "T")]
    [InlineData("'True'", "T")]
    [InlineData("'False'", "X")]
    [InlineData("'0'", "X")]
    [InlineData("'1'", "T")]
    [InlineData("NULL", "X")]
    [InlineData("DT", "T")]
    [InlineData("CCUR(0.00001)", "X")]
    public void Switch_reads_a_condition_as_cbool_does(string condition, string expected) =>
        Assert.Equal(expected, Scalar($"SWITCH({condition}, 'T', TRUE, 'X')"));

    [Theory]
    [InlineData("'abc'")]
    [InlineData("''")]
    [InlineData("'yes'")]
    [InlineData("G")]
    [InlineData("B")]
    public void Switch_condition_that_is_not_a_boolean_is_a_type_mismatch(string condition) =>
        Assert.Throws<InvalidCastException>(() => Scalar($"SWITCH({condition}, 'T', TRUE, 'X')"));

    [Theory]
    [InlineData("1", "a")]
    [InlineData("1.5", "a")]
    [InlineData("2.5", "b")]
    [InlineData("2.9", "b")]
    [InlineData("3.5", "c")]
    [InlineData("'2.5'", "b")]
    [InlineData("SG", "a")]
    [InlineData("0.9", null)]
    [InlineData("-0.5", null)]
    [InlineData("TRUE", null)]
    [InlineData("YN", null)]
    [InlineData("4", null)]
    [InlineData("DT", null)]
    [InlineData("1E+20", null)]
    public void Choose_truncates_its_index(string index, string? expected) =>
        Assert.Equal(expected, Scalar($"CHOOSE({index}, 'a', 'b', 'c')"));

    [Theory]
    [InlineData("SWITCH(TRUE, 1, 1/0, 2)")]
    [InlineData("SWITCH(FALSE, 1/0, TRUE, 2)")]
    [InlineData("CHOOSE(1, 'a', 1/0)")]
    [InlineData("CHOOSE(2, 1/0, 'b')")]
    [InlineData("ISERROR(1/0)")]
    public void Every_argument_is_evaluated(string expression) =>
        Assert.Throws<DivideByZeroException>(() => Scalar(expression));

    [Theory]
    [InlineData("1", true)]
    [InlineData("TRUE", true)]
    [InlineData("YN", true)]
    [InlineData("'1e3'", true)]
    [InlineData("'&HFF'", true)]
    [InlineData("'$5'", true)]
    [InlineData("'1,000'", true)]
    [InlineData("'1-'", true)]
    [InlineData("'(1)'", true)]
    [InlineData("'1d2'", true)]
    [InlineData("'+1'", true)]
    [InlineData("' 1 '", true)]
    [InlineData("'-'", false)]
    [InlineData("'.'", false)]
    [InlineData("'1e400'", false)]
    [InlineData("'Infinity'", false)]
    [InlineData("'NaN'", false)]
    [InlineData("'1 000'", false)]
    [InlineData("'0x1F'", false)]
    [InlineData("'True'", false)]
    [InlineData("''", false)]
    [InlineData("DT", false)]
    [InlineData("G", false)]
    [InlineData("B", false)]
    [InlineData("NULL", false)]
    public void Isnumeric_reads_text_as_a_number(string argument, bool expected) =>
        Assert.Equal(expected, Scalar($"ISNUMERIC({argument})"));

    [Theory]
    [InlineData("TRUE", "Boolean", 11)]
    [InlineData("YN", "Boolean", 11)]
    [InlineData("BT", "Byte", 17)]
    [InlineData("CBYTE(1)", "Byte", 17)]
    [InlineData("CINT(1)", "Integer", 2)]
    [InlineData("1", "Long", 3)]
    [InlineData("Id", "Long", 3)]
    [InlineData("SG", "Single", 4)]
    [InlineData("CDBL(1)", "Double", 5)]
    [InlineData("CCUR(1)", "Currency", 6)]
    [InlineData("CY", "Currency", 6)]
    [InlineData("CY * 2", "Currency", 6)]
    [InlineData("CY + 1", "Currency", 6)]
    [InlineData("DC", "Decimal", 14)]
    [InlineData("DC * 1", "Decimal", 14)]
    [InlineData("DC + CY", "Decimal", 14)]
    [InlineData("DT", "Date", 7)]
    [InlineData("'a'", "String", 8)]
    [InlineData("G", "String", 8)]
    [InlineData("B", "String", 8)]
    [InlineData("NULL", "Null", 1)]
    [InlineData("1/2", "Double", 5)]
    public void Typename_and_vartype_use_the_access_names(string argument, string typeName, int varType)
    {
        Assert.Equal(typeName, Scalar($"TYPENAME({argument})"));
        Assert.Equal(varType, Scalar($"VARTYPE({argument})"));
    }

    [Theory]
    [InlineData("PARTITION(-1, 0, 100, 10)", "   : -1")]
    [InlineData("PARTITION(5, 0, 100, 10)", "  0:  9")]
    [InlineData("PARTITION(101, 0, 100, 10)", "101:   ")]
    [InlineData("PARTITION(9.5, 0, 100, 10)", " 10: 19")]
    [InlineData("PARTITION('15', 0, 100, 10)", " 10: 19")]
    [InlineData("PARTITION(DT, 0, 50000, 1000)", "43000:43999")]
    public void Partition_labels_the_interval(string expression, string expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("RGB(255, 255, 255)", 16777215)]
    [InlineData("RGB(256, 0, 0)", 255)]
    [InlineData("RGB(0, 1000, 0)", 65280)]
    [InlineData("RGB(1.5, 2.5, 0)", 514)]
    [InlineData("RGB('10', 0, 0)", 10)]
    [InlineData("RGB(0, 0, -0.5)", 0)]
    [InlineData("RGB(255.5, 0, 0)", 255)]
    [InlineData("QBCOLOR(7)", 12632256)]
    [InlineData("QBCOLOR(15)", 16777215)]
    [InlineData("QBCOLOR(1.5)", 32768)]
    [InlineData("QBCOLOR('3')", 8421376)]
    [InlineData("QBCOLOR(-0.5)", 0)]
    public void Colours_read_their_parts_as_integers(string expression, int expected) =>
        Assert.Equal(expected, Scalar(expression));

    [Theory]
    [InlineData("PARTITION(5, 0, 100, 0)")]
    [InlineData("PARTITION(5, 0, 100, -1)")]
    [InlineData("PARTITION(5, 10, 10, 1)")]
    [InlineData("PARTITION(5, 10, 5, 1)")]
    [InlineData("PARTITION(5, -1, 100, 10)")]
    [InlineData("RGB(-1, 0, 0)")]
    [InlineData("RGB(TRUE, 0, 0)")]
    [InlineData("RGB(0, 0, -0.6)")]
    [InlineData("QBCOLOR(16)")]
    [InlineData("QBCOLOR(-1)")]
    [InlineData("QBCOLOR(TRUE)")]
    [InlineData("QBCOLOR(15.5)")]
    public void Out_of_range_arguments_are_an_invalid_procedure_call(string expression) =>
        Assert.Throws<ArgumentException>(() => Scalar(expression));

    [Theory]
    [InlineData("RGB(0, 0, 40000)")]
    [InlineData("RGB(0, 0, 2147483648)")]
    public void Colour_parts_past_an_integer_overflow(string expression) =>
        Assert.Throws<OverflowException>(() => Scalar(expression));

    [Theory]
    [InlineData("PARTITION(NULL, 0, 100, 10)")]
    [InlineData("PARTITION(5, NULL, 100, 10)")]
    [InlineData("PARTITION(5, 0, NULL, 10)")]
    [InlineData("PARTITION(5, 0, 100, NULL)")]
    [InlineData("RGB(NULL, 0, 0)")]
    [InlineData("QBCOLOR(NULL)")]
    [InlineData("CHOOSE(1, NULL, 'b')")]
    [InlineData("SWITCH(FALSE, 1)")]
    [InlineData("IIF(FALSE, 1)")]
    public void Null_results(string expression) =>
        Assert.Null(Scalar(expression));
}
