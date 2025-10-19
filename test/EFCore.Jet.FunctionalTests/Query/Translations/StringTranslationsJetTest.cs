using Microsoft.EntityFrameworkCore.Query.Translations;
using Microsoft.EntityFrameworkCore.TestModels.BasicTypesModel;
using Microsoft.EntityFrameworkCore.TestUtilities;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query.Translations;

public class StringTranslationsJetTest : StringTranslationsRelationalTestBase<BasicTypesQueryJetFixture>
{
    public StringTranslationsJetTest(BasicTypesQueryJetFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    protected override bool IsCaseSensitive
        => false;

    #region Equals

    public override async Task Equals()
    {
        await base.Equals();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` = 'seattle'
""");
    }

    public override async Task Equals_with_OrdinalIgnoreCase()
    {
        await base.Equals_with_OrdinalIgnoreCase();

        AssertSql();
    }

    public override async Task Equals_with_Ordinal()
    {
        await base.Equals_with_Ordinal();

        AssertSql();
    }

    public override async Task Static_Equals()
    {
        await base.Static_Equals();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` = 'seattle'
""");
    }

    public override async Task Static_Equals_with_OrdinalIgnoreCase()
    {
        await base.Static_Equals_with_OrdinalIgnoreCase();

        AssertSql();
    }

    public override async Task Static_Equals_with_Ordinal()
    {
        await base.Static_Equals_with_Ordinal();

        AssertSql();
    }

    #endregion Equals

    #region Miscellaneous

    public override async Task Length()
    {
        await base.Length();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE IIF(LEN(`b`.`String`) IS NULL, NULL, CLNG(LEN(`b`.`String`))) = 7
""");
    }

    public override async Task ToUpper()
    {
        await base.ToUpper();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE UCASE(`b`.`String`) = 'SEATTLE'
""",
            //
            """
SELECT UCASE(`b`.`String`)
FROM `BasicTypesEntities` AS `b`
""");
    }

    public override async Task ToLower()
    {
        await base.ToLower();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE LCASE(`b`.`String`) = 'seattle'
""",
            //
            """
SELECT LCASE(`b`.`String`)
FROM `BasicTypesEntities` AS `b`
""");
    }

    #endregion Miscellaneous

    #region IndexOf

    public override async Task IndexOf()
    {
        await base.IndexOf();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE (INSTR(1, `b`.`String`, 'Eattl', 1) - 1) <> -1
""");
    }

    public override async Task IndexOf_Char()
    {
        await base.IndexOf_Char();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE (INSTR(1, `b`.`String`, 'e', 1) - 1) <> -1
""");
    }

    public override async Task IndexOf_with_empty_string()
    {
        await base.IndexOf_with_empty_string();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
""");
    }

    public override async Task IndexOf_with_one_parameter_arg()
    {
        await base.IndexOf_with_one_parameter_arg();

        AssertSql(
            """
@pattern='Eattl' (Size = 255)
@pattern='Eattl' (Size = 255)

SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE (INSTR(1, `b`.`String`, @pattern, 1) - IIF(@pattern = '', 0, 1)) = 1
""");

    }

    public override async Task IndexOf_with_one_parameter_arg_char()
    {
        await base.IndexOf_with_one_parameter_arg_char();

        AssertSql(
            """
@pattern='e' (Size = 1) (DbType = String)
@pattern='e' (Size = 1) (DbType = String)

SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE (INSTR(1, `b`.`String`, @pattern, 1) - IIF(@pattern = '', 0, 1)) = 1
""");

    }

    public override async Task IndexOf_with_constant_starting_position()
    {
        await base.IndexOf_with_constant_starting_position();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE IIF(LEN(`b`.`String`) IS NULL, NULL, CLNG(LEN(`b`.`String`))) > 2 AND (INSTR(3, `b`.`String`, 'e', 1) - 1) = 6
""");
    }

    public override async Task IndexOf_with_constant_starting_position_char()
    {
        await base.IndexOf_with_constant_starting_position_char();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE IIF(LEN(`b`.`String`) IS NULL, NULL, CLNG(LEN(`b`.`String`))) > 2 AND (INSTR(3, `b`.`String`, 'e', 1) - 1) = 6
""");
    }

    public override async Task IndexOf_with_parameter_starting_position()
    {
        await base.IndexOf_with_parameter_starting_position();

        AssertSql(
            """
@start='2'

SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE IIF(LEN(`b`.`String`) IS NULL, NULL, CLNG(LEN(`b`.`String`))) > 2 AND (INSTR(@start + 1, `b`.`String`, 'E', 1) - 1) = 6
""");
    }

    public override async Task IndexOf_with_parameter_starting_position_char()
    {
        await base.IndexOf_with_parameter_starting_position_char();

        AssertSql(
            """
@start='2'

SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE IIF(LEN(`b`.`String`) IS NULL, NULL, CLNG(LEN(`b`.`String`))) > 2 AND (INSTR(@start + 1, `b`.`String`, 'e', 1) - 1) = 6
""");
    }

    public override async Task IndexOf_after_ToString()
    {
        await base.IndexOf_after_ToString();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE (INSTR(1, (`b`.`Int` & ''), '55', 1) - 1) = 1
""");
    }

    public override async Task IndexOf_over_ToString()
    {
        await base.IndexOf_over_ToString();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE (INSTR(1, '12559', (`b`.`Int` & ''), 1) - IIF((`b`.`Int` & '') = '', 0, 1)) = 1
""");
    }

    #endregion IndexOf

    #region Replace

    public override async Task Replace()
    {
        await base.Replace();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE REPLACE(`b`.`String`, 'sea', 'rea') = 'reattle'
""");
    }

    public override async Task Replace_Char()
    {
        await base.Replace_Char();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE REPLACE(`b`.`String`, 'S', 'R') = 'Reattle'
""");
    }

    public override async Task Replace_with_empty_string()
    {
        await base.Replace_with_empty_string();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` <> '' AND REPLACE(`b`.`String`, `b`.`String`, '') = ''
""");
    }

    public override async Task Replace_using_property_arguments()
    {
        await base.Replace_using_property_arguments();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` <> '' AND REPLACE(`b`.`String`, `b`.`String`, (`b`.`Int` & '')) = (`b`.`Int` & '')
""");
    }

    #endregion Replace

    #region Substring

    public override async Task Substring()
    {
        await base.Substring();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE IIF(LEN(`b`.`String`) IS NULL, NULL, CLNG(LEN(`b`.`String`))) >= 3 AND MID(`b`.`String`, 1 + 1, 2) = 'ea'
""");
    }

    public override async Task Substring_with_one_arg_with_zero_startIndex()
    {
        await base.Substring_with_one_arg_with_zero_startIndex();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE MID(`b`.`String`, 0 + 1, LEN(`b`.`String`)) = 'Seattle'
""");
    }

    public override async Task Substring_with_one_arg_with_constant()
    {
        await base.Substring_with_one_arg_with_constant();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE IIF(LEN(`b`.`String`) IS NULL, NULL, CLNG(LEN(`b`.`String`))) >= 1 AND MID(`b`.`String`, 1 + 1, LEN(`b`.`String`)) = 'eattle'
""");
    }

    public override async Task Substring_with_one_arg_with_parameter()
    {
        await base.Substring_with_one_arg_with_parameter();

        AssertSql(
            """
@start='2'

SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE IIF(LEN(`b`.`String`) IS NULL, NULL, CLNG(LEN(`b`.`String`))) >= 2 AND MID(`b`.`String`, @start + 1, LEN(`b`.`String`)) = 'attle'
""");
    }

    public override async Task Substring_with_two_args_with_zero_startIndex()
    {
        await base.Substring_with_two_args_with_zero_startIndex();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE IIF(LEN(`b`.`String`) IS NULL, NULL, CLNG(LEN(`b`.`String`))) >= 3 AND MID(`b`.`String`, 0 + 1, 3) = 'Sea'
""");
    }

    public override async Task Substring_with_two_args_with_zero_length()
    {
        await base.Substring_with_two_args_with_zero_length();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE IIF(LEN(`b`.`String`) IS NULL, NULL, CLNG(LEN(`b`.`String`))) >= 2 AND MID(`b`.`String`, 2 + 1, 0) = ''
""");
    }

    public override async Task Substring_with_two_args_with_parameter()
    {
        await base.Substring_with_two_args_with_parameter();

        AssertSql(
            """
@start='2'

SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE IIF(LEN(`b`.`String`) IS NULL, NULL, CLNG(LEN(`b`.`String`))) >= 5 AND MID(`b`.`String`, @start + 1, 3) = 'att'
""");
    }

    public override async Task Substring_with_two_args_with_IndexOf()
    {
        await base.Substring_with_two_args_with_IndexOf();

        AssertSql(
            """
SELECT [b].[Id], [b].[Bool], [b].[Byte], [b].[ByteArray], [b].[DateOnly], [b].[DateTime], [b].[DateTimeOffset], [b].[Decimal], [b].[Double], [b].[Enum], [b].[FlagsEnum], [b].[Float], [b].[Guid], [b].[Int], [b].[Long], [b].[Short], [b].[String], [b].[TimeOnly], [b].[TimeSpan]
FROM [BasicTypesEntities] AS [b]
WHERE [b].[String] LIKE N'%a%' AND SUBSTRING([b].[String], (CAST(CHARINDEX(N'a', [b].[String]) AS int) - 1) + 1, 3) = N'att'
""");
    }

    #endregion Substring

    #region IsNullOrEmpty/Whitespace

    public override async Task IsNullOrEmpty()
    {
        await base.IsNullOrEmpty();

        AssertSql(
            """
SELECT `n`.`Id`, `n`.`Bool`, `n`.`Byte`, `n`.`ByteArray`, `n`.`DateOnly`, `n`.`DateTime`, `n`.`DateTimeOffset`, `n`.`Decimal`, `n`.`Double`, `n`.`Enum`, `n`.`FlagsEnum`, `n`.`Float`, `n`.`Guid`, `n`.`Int`, `n`.`Long`, `n`.`Short`, `n`.`String`, `n`.`TimeOnly`, `n`.`TimeSpan`
FROM `NullableBasicTypesEntities` AS `n`
WHERE `n`.`String` IS NULL OR (`n`.`String` LIKE '')
""",
            //
            """
SELECT `n`.`String` IS NULL OR (`n`.`String` LIKE '')
FROM `NullableBasicTypesEntities` AS `n`
""");
    }

    public override async Task IsNullOrEmpty_negated()
    {
        await base.IsNullOrEmpty_negated();

        AssertSql(
            """
SELECT `n`.`Id`, `n`.`Bool`, `n`.`Byte`, `n`.`ByteArray`, `n`.`DateOnly`, `n`.`DateTime`, `n`.`DateTimeOffset`, `n`.`Decimal`, `n`.`Double`, `n`.`Enum`, `n`.`FlagsEnum`, `n`.`Float`, `n`.`Guid`, `n`.`Int`, `n`.`Long`, `n`.`Short`, `n`.`String`, `n`.`TimeOnly`, `n`.`TimeSpan`
FROM `NullableBasicTypesEntities` AS `n`
WHERE `n`.`String` IS NOT NULL AND `n`.`String` NOT LIKE ''
""",
            //
            """
SELECT `n`.`String` IS NOT NULL AND `n`.`String` NOT LIKE ''
FROM `NullableBasicTypesEntities` AS `n`
""");
    }

    public override async Task IsNullOrWhiteSpace()
    {
        await base.IsNullOrWhiteSpace();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` = ''
""");
    }

    #endregion IsNullOrEmpty/Whitespace

    #region StartsWith

    public override async Task StartsWith_Literal()
    {
        await base.StartsWith_Literal();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` LIKE 'se%'
""");
    }

    public override async Task StartsWith_Literal_Char()
    {
        await base.StartsWith_Literal_Char();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` LIKE 'S%'
""");
    }

    public override async Task StartsWith_Parameter()
    {
        await base.StartsWith_Parameter();

        AssertSql(
            """
@pattern_startswith='se%' (Size = 255)

SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` LIKE @pattern_startswith
""");
    }

    public override async Task StartsWith_Parameter_Char()
    {
        await base.StartsWith_Parameter_Char();

        AssertSql(
            """
@pattern_startswith='S%' (Size = 255)

SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` LIKE @pattern_startswith
""");
    }

    public override async Task StartsWith_Column()
    {
        await base.StartsWith_Column();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE LEFT(`b`.`String`, IIF(LEN(`b`.`String`) IS NULL, 0, LEN(`b`.`String`))) = `b`.`String`
""");
    }

    public override async Task StartsWith_with_StringComparison_Ordinal()
    {
        await base.StartsWith_with_StringComparison_Ordinal();

        AssertSql();
    }

    public override async Task StartsWith_with_StringComparison_OrdinalIgnoreCase()
    {
        await base.StartsWith_with_StringComparison_OrdinalIgnoreCase();

        AssertSql();
    }

    public override async Task StartsWith_with_StringComparison_unsupported()
    {
        await base.StartsWith_with_StringComparison_unsupported();

        AssertSql();
    }

    #endregion StartsWith

    #region EndsWith

    public override async Task EndsWith_Literal()
    {
        await base.EndsWith_Literal();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` LIKE '%Le'
""");
    }

    public override async Task EndsWith_Literal_Char()
    {
        await base.EndsWith_Literal_Char();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` LIKE '%e'
""");
    }

    public override async Task EndsWith_Parameter()
    {
        await base.EndsWith_Parameter();

        AssertSql(
            """
@pattern_endswith='%LE' (Size = 255)

SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` LIKE @pattern_endswith
""");
    }

    public override async Task EndsWith_Parameter_Char()
    {
        await base.EndsWith_Parameter_Char();

        AssertSql(
            """
@pattern_endswith='%e' (Size = 255)

SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` LIKE @pattern_endswith
""");
    }

    public override async Task EndsWith_Column()
    {
        // SQL Server trims trailing whitespace for length calculations, making our EndsWith() column translation not work reliably in that
        // case
        await AssertQuery(
            ss => ss.Set<BasicTypesEntity>().Where(b => b.String == "Seattle" && b.String.EndsWith(b.String)));

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` = 'Seattle' AND RIGHT(`b`.`String`, IIF(LEN(`b`.`String`) IS NULL, 0, LEN(`b`.`String`))) = `b`.`String`
""");
    }

    public override async Task EndsWith_with_StringComparison_Ordinal()
    {
        await base.EndsWith_with_StringComparison_Ordinal();

        AssertSql();
    }

    public override async Task EndsWith_with_StringComparison_OrdinalIgnoreCase()
    {
        await base.EndsWith_with_StringComparison_OrdinalIgnoreCase();

        AssertSql();
    }

    public override async Task EndsWith_with_StringComparison_unsupported()
    {
        await base.EndsWith_with_StringComparison_unsupported();

        AssertSql();
    }

    #endregion EndsWith

    #region Contains

    public override async Task Contains_Literal()
    {
        await AssertQuery(
            
            ss => ss.Set<BasicTypesEntity>().Where(c => c.String.Contains("eattl")), // SQL Server is case-insensitive by default
            ss => ss.Set<BasicTypesEntity>().Where(c => c.String.Contains("eattl", StringComparison.OrdinalIgnoreCase)));

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` LIKE '%eattl%'
""");
    }

    public override async Task Contains_Literal_Char()
    {
        await AssertQuery(
            
            ss => ss.Set<BasicTypesEntity>().Where(c => c.String.Contains('e')));

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` LIKE '%e%'
""");
    }

    public override async Task Contains_Column()
    {
        await base.Contains_Column();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE INSTR(1, `b`.`String`, `b`.`String`, 1) > 0 OR (`b`.`String` LIKE '')
""",
            //
            """
SELECT INSTR(1, `b`.`String`, `b`.`String`, 1) > 0 OR (`b`.`String` LIKE '')
FROM `BasicTypesEntities` AS `b`
""");
    }

    public override async Task Contains_negated()
    {
        await base.Contains_negated();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` NOT LIKE '%Eattle%'
""",
            //
            """
SELECT `b`.`String` NOT LIKE '%Eattle%'
FROM `BasicTypesEntities` AS `b`
""");
    }

    public override async Task Contains_with_StringComparison_Ordinal()
    {
        await base.Contains_with_StringComparison_Ordinal();

        AssertSql();
    }

    public override async Task Contains_with_StringComparison_OrdinalIgnoreCase()
    {
        await base.Contains_with_StringComparison_OrdinalIgnoreCase();

        AssertSql();
    }

    public override async Task Contains_with_StringComparison_unsupported()
    {
        await base.Contains_with_StringComparison_unsupported();

        AssertSql();
    }

    public override async Task Contains_constant_with_whitespace()
    {
        await base.Contains_constant_with_whitespace();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` LIKE '%     %'
""");
    }

    public override async Task Contains_parameter_with_whitespace()
    {
        await base.Contains_parameter_with_whitespace();

        AssertSql(
            """
@pattern_contains='%     %' (Size = 255)

SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` LIKE @pattern_contains
""");
    }

    #endregion Contains

    #region TrimStart

    public override async Task TrimStart_without_arguments()
    {
        await base.TrimStart_without_arguments();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE LTRIM(`b`.`String`) = 'Boston  '
""");
    }

    public override Task TrimStart_with_char_argument()
        => AssertTranslationFailed(() => base.TrimStart_with_char_argument());

    public override Task TrimStart_with_char_array_argument()
        => AssertTranslationFailed(() => base.TrimStart_with_char_array_argument());

    #endregion TrimStart

    #region TrimEnd

    public override async Task TrimEnd_without_arguments()
    {
        await base.TrimEnd_without_arguments();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE RTRIM(`b`.`String`) = '  Boston'
""");
    }

    public override Task TrimEnd_with_char_argument()
        => AssertTranslationFailed(() => base.TrimEnd_with_char_argument());

    public override Task TrimEnd_with_char_array_argument()
        => AssertTranslationFailed(() => base.TrimEnd_with_char_array_argument());

    #endregion TrimEnd

    #region Trim

    public override async Task Trim_without_argument_in_predicate()
    {
        await base.Trim_without_argument_in_predicate();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE TRIM(`b`.`String`) = 'Boston'
""");
    }

    public override async Task Trim_with_char_argument_in_predicate()
    {
        // String.Trim with parameters. Issue #22927.
        await AssertTranslationFailed(() => base.Trim_with_char_argument_in_predicate());

        AssertSql();
    }

    public override async Task Trim_with_char_array_argument_in_predicate()
    {
        // String.Trim with parameters. Issue #22927.
        await AssertTranslationFailed(() => base.Trim_with_char_array_argument_in_predicate());

        AssertSql();
    }

    #endregion Trim

    #region Compare

    public override async Task Compare_simple_zero()
    {
        await base.Compare_simple_zero();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` = 'seattle'
""",
            //
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` <> 'seattle'
""",
            //
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` > 'seattle'
""",
            //
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` <= 'seattle'
""",
            //
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` > 'seattle'
""",
            //
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` <= 'seattle'
""");
    }

    public override async Task Compare_simple_one()
    {
        await base.Compare_simple_one();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` > 'Seattle'
""",
            //
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` < 'Seattle'
""",
            //
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` <= 'Seattle'
""",
            //
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` <= 'Seattle'
""",
            //
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` >= 'Seattle'
""",
            //
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` >= 'Seattle'
""");
    }

    public override async Task Compare_with_parameter()
    {
        await base.Compare_with_parameter();

        AssertSql(
            """
@basicTypeEntity_String='Seattle' (Size = 255)

SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` > @basicTypeEntity_String
""",
            //
            """
@basicTypeEntity_String='Seattle' (Size = 255)

SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` < @basicTypeEntity_String
""",
            //
            """
@basicTypeEntity_String='Seattle' (Size = 255)

SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` <= @basicTypeEntity_String
""",
            //
            """
@basicTypeEntity_String='Seattle' (Size = 255)

SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` <= @basicTypeEntity_String
""",
            //
            """
@basicTypeEntity_String='Seattle' (Size = 255)

SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` >= @basicTypeEntity_String
""",
            //
            """
@basicTypeEntity_String='Seattle' (Size = 255)

SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` >= @basicTypeEntity_String
""");
    }

    public override async Task Compare_simple_more_than_one()
    {
        await base.Compare_simple_more_than_one();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE IIF(`b`.`String` = 'Seattle', 0, IIF(`b`.`String` > 'Seattle', 1, IIF(`b`.`String` < 'Seattle', -1, NULL))) = 42
""",
            //
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE IIF(`b`.`String` = 'Seattle', 0, IIF(`b`.`String` > 'Seattle', 1, IIF(`b`.`String` < 'Seattle', -1, NULL))) > 42
""",
            //
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE 42 > IIF(`b`.`String` = 'Seattle', 0, IIF(`b`.`String` > 'Seattle', 1, IIF(`b`.`String` < 'Seattle', -1, NULL)))
""");
    }

    public override async Task Compare_nested()
    {
        await base.Compare_nested();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` = ('M' & `b`.`String`)
""",
            //
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` <> MID(`b`.`String`, 0 + 1, 0)
""",
            //
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` > REPLACE('Seattle', 'Sea', `b`.`String`)
""",
            //
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` <= ('M' & `b`.`String`)
""",
            //
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` > MID(`b`.`String`, 0 + 1, 0)
""",
            //
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` < REPLACE('Seattle', 'Sea', `b`.`String`)
""");
    }

    public override async Task Compare_multi_predicate()
    {
        await base.Compare_multi_predicate();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` >= 'Seattle' AND `b`.`String` < 'Toronto'
""");
    }

    public override async Task CompareTo_simple_zero()
    {
        await base.CompareTo_simple_zero();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` = 'Seattle'
""",
            //
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` <> 'Seattle'
""",
            //
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` > 'Seattle'
""",
            //
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` <= 'Seattle'
""",
            //
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` > 'Seattle'
""",
            //
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` <= 'Seattle'
""");
    }

    public override async Task CompareTo_simple_one()
    {
        await base.CompareTo_simple_one();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` > 'Seattle'
""",
            //
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` < 'Seattle'
""",
            //
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` <= 'Seattle'
""",
            //
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` <= 'Seattle'
""",
            //
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` >= 'Seattle'
""",
            //
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` >= 'Seattle'
""");
    }

    public override async Task CompareTo_with_parameter()
    {
        await base.CompareTo_with_parameter();

        AssertSql(
            """
@basicTypesEntity_String='Seattle' (Size = 255)

SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` > @basicTypesEntity_String
""",
            //
            """
@basicTypesEntity_String='Seattle' (Size = 255)

SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` < @basicTypesEntity_String
""",
            //
            """
@basicTypesEntity_String='Seattle' (Size = 255)

SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` <= @basicTypesEntity_String
""",
            //
            """
@basicTypesEntity_String='Seattle' (Size = 255)

SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` <= @basicTypesEntity_String
""",
            //
            """
@basicTypesEntity_String='Seattle' (Size = 255)

SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` >= @basicTypesEntity_String
""",
            //
            """
@basicTypesEntity_String='Seattle' (Size = 255)

SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` >= @basicTypesEntity_String
""");
    }

    public override async Task CompareTo_simple_more_than_one()
    {
        await base.CompareTo_simple_more_than_one();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE IIF(`b`.`String` = 'Seattle', 0, IIF(`b`.`String` > 'Seattle', 1, IIF(`b`.`String` < 'Seattle', -1, NULL))) = 42
""",
            //
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE IIF(`b`.`String` = 'Seattle', 0, IIF(`b`.`String` > 'Seattle', 1, IIF(`b`.`String` < 'Seattle', -1, NULL))) > 42
""",
            //
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE 42 > IIF(`b`.`String` = 'Seattle', 0, IIF(`b`.`String` > 'Seattle', 1, IIF(`b`.`String` < 'Seattle', -1, NULL)))
""");
    }

    public override async Task CompareTo_nested()
    {
        await base.CompareTo_nested();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` = ('M' & `b`.`String`)
""",
            //
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` <> MID(`b`.`String`, 0 + 1, 0)
""",
            //
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` > REPLACE('Seattle', 'Sea', `b`.`String`)
""",
            //
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` <= ('M' & `b`.`String`)
""",
            //
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` > MID(`b`.`String`, 0 + 1, 0)
""",
            //
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` < REPLACE('Seattle', 'Sea', `b`.`String`)
""");
    }

    public override async Task Compare_to_multi_predicate()
    {
        await base.Compare_to_multi_predicate();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`String` >= 'Seattle' AND `b`.`String` < 'Toronto'
""");
    }

    #endregion Compare

    #region Join

    public override async Task Join_over_non_nullable_column()
    {
        await base.Join_over_non_nullable_column();

        AssertSql(
            """
SELECT `b1`.`Int`, `b0`.`String`, `b0`.`Id`
FROM (
    SELECT `b`.`Int`
    FROM `BasicTypesEntities` AS `b`
    GROUP BY `b`.`Int`
) AS `b1`
LEFT JOIN `BasicTypesEntities` AS `b0` ON `b1`.`Int` = `b0`.`Int`
ORDER BY `b1`.`Int`
""");
    }

    public override async Task Join_over_nullable_column()
    {
        await base.Join_over_nullable_column();

        AssertSql(
            """
SELECT `n3`.`Key`, `n1`.`String`, `n1`.`Id`
FROM (
    SELECT `n0`.`Key`
    FROM (
        SELECT IIF(`n`.`Int` IS NULL, 0, `n`.`Int`) AS `Key`
        FROM `NullableBasicTypesEntities` AS `n`
    ) AS `n0`
    GROUP BY `n0`.`Key`
) AS `n3`
LEFT JOIN (
    SELECT `n2`.`Id`, `n2`.`String`, IIF(`n2`.`Int` IS NULL, 0, `n2`.`Int`) AS `Key`
    FROM `NullableBasicTypesEntities` AS `n2`
) AS `n1` ON `n3`.`Key` = `n1`.`Key`
ORDER BY `n3`.`Key`
""");
    }

    public override async Task Join_with_predicate()
    {
        await base.Join_with_predicate();

        AssertSql(
            """
SELECT `b1`.`Int`, `b2`.`String`, `b2`.`Id`
FROM (
    SELECT `b`.`Int`
    FROM `BasicTypesEntities` AS `b`
    GROUP BY `b`.`Int`
) AS `b1`
LEFT JOIN (
    SELECT `b0`.`String`, `b0`.`Id`, `b0`.`Int`
    FROM `BasicTypesEntities` AS `b0`
    WHERE IIF(LEN(`b0`.`String`) IS NULL, NULL, CLNG(LEN(`b0`.`String`))) > 6
) AS `b2` ON `b1`.`Int` = `b2`.`Int`
ORDER BY `b1`.`Int`
""");
    }

    public override async Task Join_with_ordering()
    {
        await base.Join_with_ordering();

        AssertSql(
            """
SELECT `b1`.`Int`, `b0`.`String`, `b0`.`Id`
FROM (
    SELECT `b`.`Int`
    FROM `BasicTypesEntities` AS `b`
    GROUP BY `b`.`Int`
) AS `b1`
LEFT JOIN `BasicTypesEntities` AS `b0` ON `b1`.`Int` = `b0`.`Int`
ORDER BY `b1`.`Int`, `b0`.`Id` DESC
""");
    }

    public override async Task Join_non_aggregate()
    {
        await base.Join_non_aggregate();

        AssertSql(
            """
@foo='foo' (Size = 4000)

SELECT [b].[Id], [b].[Bool], [b].[Byte], [b].[ByteArray], [b].[DateOnly], [b].[DateTime], [b].[DateTimeOffset], [b].[Decimal], [b].[Double], [b].[Enum], [b].[FlagsEnum], [b].[Float], [b].[Guid], [b].[Int], [b].[Long], [b].[Short], [b].[String], [b].[TimeOnly], [b].[TimeSpan]
FROM [BasicTypesEntities] AS [b]
WHERE CONCAT_WS(N'|', [b].[String], @foo, N'', N'bar') = N'Seattle|foo||bar'
""");
    }

    #endregion Join

    #region Concatenation

    public override async Task Concat_operator()
    {
        await base.Concat_operator();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE (`b`.`String` & 'Boston') = 'SeattleBoston'
""");
    }

    public override async Task Concat_aggregate()
    {
        await base.Concat_aggregate();

        AssertSql(
            """
SELECT `b1`.`Int`, `b0`.`String`, `b0`.`Id`
FROM (
    SELECT `b`.`Int`
    FROM `BasicTypesEntities` AS `b`
    GROUP BY `b`.`Int`
) AS `b1`
LEFT JOIN `BasicTypesEntities` AS `b0` ON `b1`.`Int` = `b0`.`Int`
ORDER BY `b1`.`Int`
""");
    }

    public override async Task Concat_string_int_comparison1()
    {
        await base.Concat_string_int_comparison1();

        AssertSql(
            """
@i='10'

SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE (`b`.`String` & (@i & '')) = 'Seattle10'
""");
    }

    public override async Task Concat_string_int_comparison2()
    {
        await base.Concat_string_int_comparison2();

        AssertSql(
            """
@i='10'

SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE ((@i & '') & `b`.`String`) = '10Seattle'
""");
    }

    public override async Task Concat_string_int_comparison3()
    {
        await base.Concat_string_int_comparison3();

        AssertSql(
            """
@p='30'
@j='21'

SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE ((((@p & '') & `b`.`String`) & (@j & '')) & (42 & '')) = '30Seattle2142'
""");
    }

    public override async Task Concat_string_int_comparison4()
    {
        await base.Concat_string_int_comparison4();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE ((`b`.`Int` & '') & `b`.`String`) = '8Seattle'
""");
    }

    public override async Task Concat_string_string_comparison()
    {
        await base.Concat_string_string_comparison();

        AssertSql(
            """
@i='A' (Size = 255)

SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE (@i & `b`.`String`) = 'ASeattle'
""");
    }

    public override async Task Concat_method_comparison()
    {
        await base.Concat_method_comparison();

        AssertSql(
            """
@i='A' (Size = 255)

SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE (@i & `b`.`String`) = 'ASeattle'
""");
    }

    public override async Task Concat_method_comparison_2()
    {
        await base.Concat_method_comparison_2();

        AssertSql(
            """
@i='A' (Size = 255)
@j='B' (Size = 255)

SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE (@i & (@j & `b`.`String`)) = 'ABSeattle'
""");
    }

    public override async Task Concat_method_comparison_3()
    {
        await base.Concat_method_comparison_3();

        AssertSql(
            """
@i='A' (Size = 255)
@j='B' (Size = 255)
@k='C' (Size = 255)

SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE (@i & (@j & (@k & `b`.`String`))) = 'ABCSeattle'
""");
    }

    #endregion Concatenation

    #region LINQ Operators

    public override async Task FirstOrDefault()
    {
        await base.FirstOrDefault();
        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE MID(`b`.`String`, 1, 1) = 'S'
""");
    }

    public override async Task LastOrDefault()
    {
        await base.LastOrDefault();
        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE MID(`b`.`String`, IIF(LEN(`b`.`String`) = 0, 1, LEN(`b`.`String`)), 1) = 'e'
""");
    }

    #endregion LINQ Operators

    #region Like

    public override async Task Where_Like_and_comparison()
    {
        await base.Where_Like_and_comparison();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE (`b`.`String` LIKE 'S%') AND `b`.`Int` = 8
""");
    }

    public override async Task Where_Like_or_comparison()
    {
        await base.Where_Like_or_comparison();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE (`b`.`String` LIKE 'S%') OR `b`.`Int` = 2147483647
""");
    }

    public override async Task Like_with_non_string_column_using_ToString()
    {
        await base.Like_with_non_string_column_using_ToString();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE (`b`.`Int` & '') LIKE '%5%'
""");
    }

    public override async Task Like_with_non_string_column_using_double_cast()
    {
        await base.Like_with_non_string_column_using_double_cast();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE (`b`.`Int` & '') LIKE '%5%'
""");
    }

    #endregion Like

    #region Regex

    public override Task Regex_IsMatch()
        => AssertTranslationFailed(() => base.Regex_IsMatch());

    public override Task Regex_IsMatch_constant_input()
        => AssertTranslationFailed(() => base.Regex_IsMatch_constant_input());

    #endregion Regex

    [ConditionalFact]
    public virtual void Check_all_tests_overridden()
        => TestHelpers.AssertAllMethodsOverridden(GetType());

    private void AssertSql(params string[] expected)
        => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);

    protected override void ClearLog()
        => Fixture.TestSqlLoggerFactory.Clear();
}
