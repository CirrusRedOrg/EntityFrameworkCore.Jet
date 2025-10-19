// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.EntityFrameworkCore.Query.Translations.Temporal;
using Microsoft.EntityFrameworkCore.TestUtilities;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query.Translations.Temporal;

public class DateTimeOffsetTranslationsJetTest : DateTimeOffsetTranslationsTestBase<BasicTypesQueryJetFixture>
{
    public DateTimeOffsetTranslationsJetTest(BasicTypesQueryJetFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    public override async Task Now()
    {
        await base.Now();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`DateTimeOffset` <> NOW()
""");
    }

    public override async Task UtcNow()
    {
        await base.UtcNow();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`DateTimeOffset` <> DATEADD('n', -480.0, NOW())
""");
    }

    public override async Task Date()
    {
        await base.Date();

        AssertSql(
            """
@Date='0001-01-01T00:00:00.0000000'

SELECT [b].[Id], [b].[Bool], [b].[Byte], [b].[ByteArray], [b].[DateOnly], [b].[DateTime], [b].[DateTimeOffset], [b].[Decimal], [b].[Double], [b].[Enum], [b].[FlagsEnum], [b].[Float], [b].[Guid], [b].[Int], [b].[Long], [b].[Short], [b].[String], [b].[TimeOnly], [b].[TimeSpan]
FROM [BasicTypesEntities] AS [b]
WHERE CONVERT(date, [b].[DateTimeOffset]) > @Date
""");
    }

    public override async Task Year()
    {
        await base.Year();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE DATEPART('yyyy', `b`.`DateTimeOffset`) = 1998
""");
    }

    public override async Task Month()
    {
        await base.Month();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE DATEPART('m', `b`.`DateTimeOffset`) = 5
""");
    }

    public override async Task DayOfYear()
    {
        await base.DayOfYear();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE DATEPART('y', `b`.`DateTimeOffset`) = 124
""");
    }

    public override async Task Day()
    {
        await base.Day();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE DATEPART('d', `b`.`DateTimeOffset`) = 4
""");
    }

    public override async Task Hour()
    {
        await base.Hour();

        AssertSql(
            """
SELECT [b].[Id], [b].[Bool], [b].[Byte], [b].[ByteArray], [b].[DateOnly], [b].[DateTime], [b].[DateTimeOffset], [b].[Decimal], [b].[Double], [b].[Enum], [b].[FlagsEnum], [b].[Float], [b].[Guid], [b].[Int], [b].[Long], [b].[Short], [b].[String], [b].[TimeOnly], [b].[TimeSpan]
FROM [BasicTypesEntities] AS [b]
WHERE DATEPART(hour, [b].[DateTimeOffset]) = 15
""");
    }

    public override async Task Minute()
    {
        await base.Minute();

        AssertSql(
            """
SELECT [b].[Id], [b].[Bool], [b].[Byte], [b].[ByteArray], [b].[DateOnly], [b].[DateTime], [b].[DateTimeOffset], [b].[Decimal], [b].[Double], [b].[Enum], [b].[FlagsEnum], [b].[Float], [b].[Guid], [b].[Int], [b].[Long], [b].[Short], [b].[String], [b].[TimeOnly], [b].[TimeSpan]
FROM [BasicTypesEntities] AS [b]
WHERE DATEPART(minute, [b].[DateTimeOffset]) = 30
""");
    }

    public override async Task Second()
    {
        await base.Second();

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE DATEPART('s', `b`.`DateTimeOffset`) = 10
""");
    }

    public override async Task Millisecond()
    {
        await base.Millisecond();

        AssertSql(
            """
SELECT [b].[Id], [b].[Bool], [b].[Byte], [b].[ByteArray], [b].[DateOnly], [b].[DateTime], [b].[DateTimeOffset], [b].[Decimal], [b].[Double], [b].[Enum], [b].[FlagsEnum], [b].[Float], [b].[Guid], [b].[Int], [b].[Long], [b].[Short], [b].[String], [b].[TimeOnly], [b].[TimeSpan]
FROM [BasicTypesEntities] AS [b]
WHERE DATEPART(millisecond, [b].[DateTimeOffset]) = 123
""");
    }

    public override async Task Microsecond()
    {
        await base.Microsecond();

        AssertSql(
            """
SELECT [b].[Id], [b].[Bool], [b].[Byte], [b].[ByteArray], [b].[DateOnly], [b].[DateTime], [b].[DateTimeOffset], [b].[Decimal], [b].[Double], [b].[Enum], [b].[FlagsEnum], [b].[Float], [b].[Guid], [b].[Int], [b].[Long], [b].[Short], [b].[String], [b].[TimeOnly], [b].[TimeSpan]
FROM [BasicTypesEntities] AS [b]
WHERE DATEPART(microsecond, [b].[DateTimeOffset]) % 1000 = 456
""");
    }

    public override async Task Nanosecond()
    {
        await base.Nanosecond();

        AssertSql(
            """
SELECT [b].[Id], [b].[Bool], [b].[Byte], [b].[ByteArray], [b].[DateOnly], [b].[DateTime], [b].[DateTimeOffset], [b].[Decimal], [b].[Double], [b].[Enum], [b].[FlagsEnum], [b].[Float], [b].[Guid], [b].[Int], [b].[Long], [b].[Short], [b].[String], [b].[TimeOnly], [b].[TimeSpan]
FROM [BasicTypesEntities] AS [b]
WHERE DATEPART(nanosecond, [b].[DateTimeOffset]) % 1000 = 400
""");
    }

    public override async Task TimeOfDay()
    {
        await base.TimeOfDay();

        AssertSql(
            """
SELECT CONVERT(time, [b].[DateTimeOffset])
FROM [BasicTypesEntities] AS [b]
""");
    }

    public override async Task AddYears()
    {
        await base.AddYears();

        AssertSql(
            """
SELECT DATEADD('yyyy', 1, `b`.`DateTimeOffset`)
FROM `BasicTypesEntities` AS `b`
""");
    }

    public override async Task AddMonths()
    {
        await base.AddMonths();

        AssertSql(
            """
SELECT DATEADD('m', 1, `b`.`DateTimeOffset`)
FROM `BasicTypesEntities` AS `b`
""");
    }

    public override async Task AddDays()
    {
        await base.AddDays();

        AssertSql(
            """
SELECT DATEADD('d', 1.0, `b`.`DateTimeOffset`)
FROM `BasicTypesEntities` AS `b`
""");
    }

    public override async Task AddHours()
    {
        await base.AddHours();

        AssertSql(
            """
SELECT DATEADD('h', 1.0, `b`.`DateTimeOffset`)
FROM `BasicTypesEntities` AS `b`
""");
    }

    public override async Task AddMinutes()
    {
        await base.AddMinutes();

        AssertSql(
            """
SELECT DATEADD('n', 1.0, `b`.`DateTimeOffset`)
FROM `BasicTypesEntities` AS `b`
""");
    }

    public override async Task AddSeconds()
    {
        await base.AddSeconds();

        AssertSql(
            """
SELECT DATEADD('s', 1.0, `b`.`DateTimeOffset`)
FROM `BasicTypesEntities` AS `b`
""");
    }

    public override async Task AddMilliseconds()
    {
        await base.AddMilliseconds();

        AssertSql(
            """
SELECT DATEADD(millisecond, CAST(300.0E0 AS int), [b].[DateTimeOffset])
FROM [BasicTypesEntities] AS [b]
""");
    }

    public override async Task ToUnixTimeMilliseconds()
    {
        await base.ToUnixTimeMilliseconds();

        AssertSql(
            """
@unixEpochMilliseconds='894295810000'

SELECT [b].[Id], [b].[Bool], [b].[Byte], [b].[ByteArray], [b].[DateOnly], [b].[DateTime], [b].[DateTimeOffset], [b].[Decimal], [b].[Double], [b].[Enum], [b].[FlagsEnum], [b].[Float], [b].[Guid], [b].[Int], [b].[Long], [b].[Short], [b].[String], [b].[TimeOnly], [b].[TimeSpan]
FROM [BasicTypesEntities] AS [b]
WHERE DATEDIFF_BIG(millisecond, '1970-01-01T00:00:00.0000000+00:00', [b].[DateTimeOffset]) = @unixEpochMilliseconds
""");
    }

    public override async Task ToUnixTimeSecond()
    {
        await base.ToUnixTimeSecond();

        AssertSql(
            """
@unixEpochSeconds='894295810' (DbType = Decimal)

SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE DATEDIFF('s', CDATE('1970-01-01 00:00:00'), `b`.`DateTimeOffset`) = @unixEpochSeconds
""");
    }

    public override async Task Milliseconds_parameter_and_constant()
    {
        await base.Milliseconds_parameter_and_constant();

        AssertSql(
            """
SELECT COUNT(*)
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`DateTimeOffset` = CDATE('1902-01-02 08:30:00')
""");
    }

    [ConditionalFact]
    public virtual void Check_all_tests_overridden()
        => TestHelpers.AssertAllMethodsOverridden(GetType());

    private void AssertSql(params string[] expected)
        => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);
}
