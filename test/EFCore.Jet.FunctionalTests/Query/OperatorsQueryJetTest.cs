// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.EntityFrameworkCore.TestModels.Operators;
using Microsoft.EntityFrameworkCore.TestUtilities;
using System.Linq;
using System.Threading.Tasks;
using System;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Xunit.Abstractions;
using Xunit;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query;

public class OperatorsQueryJetTest : OperatorsQueryTestBase
{
    protected override ITestStoreFactory TestStoreFactory
        => JetTestStoreFactory.Instance;

    protected void AssertSql(params string[] expected)
        => TestSqlLoggerFactory.AssertBaseline(expected);

    public override async Task Bitwise_and_on_expression_with_like_and_null_check_being_compared_to_false()
    {
        await base.Bitwise_and_on_expression_with_like_and_null_check_being_compared_to_false();

        AssertSql(
            """
SELECT `o`.`Value` AS `Value1`, `o0`.`Value` AS `Value2`, `o1`.`Value` AS `Value3`
FROM `OperatorEntityString` AS `o`,
`OperatorEntityString` AS `o0`,
`OperatorEntityBool` AS `o1`
WHERE (((`o0`.`Value` LIKE 'B') AND `o0`.`Value` IS NOT NULL) OR `o1`.`Value` = TRUE) AND `o`.`Value` IS NOT NULL
ORDER BY `o`.`Id`, `o0`.`Id`, `o1`.`Id`
""");
    }

    public override async Task Complex_predicate_with_bitwise_and_modulo_and_negation()
    {
        await base.Complex_predicate_with_bitwise_and_modulo_and_negation();

        AssertSql(
            """
SELECT `o`.`Value` AS `Value0`, `o0`.`Value` AS `Value1`, `o1`.`Value` AS `Value2`, `o2`.`Value` AS `Value3`
FROM `OperatorEntityLong` AS `o`,
`OperatorEntityLong` AS `o0`,
`OperatorEntityLong` AS `o1`,
`OperatorEntityLong` AS `o2`
WHERE (((`o0`.`Value` MOD 2) / `o`.`Value`) BAND (((`o2`.`Value` BOR `o1`.`Value`) - `o`.`Value`) - (`o1`.`Value` * `o1`.`Value`))) >= (((`o0`.`Value` /  (BNOT`o2`.`Value`)) MOD 2) MOD ( (BNOT`o`.`Value`) + 1))
ORDER BY `o`.`Id`, `o0`.`Id`, `o1`.`Id`, `o2`.`Id`
""");
    }

    public override async Task Complex_predicate_with_bitwise_and_arithmetic_operations()
    {
        await base.Complex_predicate_with_bitwise_and_arithmetic_operations();

        AssertSql(
            """
SELECT `o`.`Value` AS `Value0`, `o0`.`Value` AS `Value1`, `o1`.`Value` AS `Value2`
FROM `OperatorEntityInt` AS `o`,
`OperatorEntityInt` AS `o0`,
`OperatorEntityBool` AS `o1`
WHERE (((`o0`.`Value` BAND (`o`.`Value` + `o`.`Value`)) BAND `o`.`Value`) \ 1) > (`o0`.`Value` BAND 10) AND `o1`.`Value` = TRUE
ORDER BY `o`.`Id`, `o0`.`Id`, `o1`.`Id`
""");
    }

    public override async Task Projection_with_not_and_negation_on_integer()
    {
        await base.Projection_with_not_and_negation_on_integer();

        AssertSql(
            """
SELECT  (BNOT-(-((`o1`.`Value` + `o`.`Value`) + 2))) MOD (-(`o0`.`Value` + `o0`.`Value`) - `o`.`Value`)
FROM `OperatorEntityLong` AS `o`,
`OperatorEntityLong` AS `o0`,
`OperatorEntityLong` AS `o1`
ORDER BY `o`.`Id`, `o0`.`Id`, `o1`.`Id`
""");
    }

    public override async Task Negate_on_column(bool async)
    {
        await base.Negate_on_column(async);

        AssertSql(
"""
SELECT `o`.`Id`
FROM `OperatorEntityInt` AS `o`
WHERE `o`.`Id` = -`o`.`Value`
""");
    }

    public override async Task Double_negate_on_column()
    {
        await base.Double_negate_on_column();

        AssertSql(
"""
SELECT `o`.`Id`
FROM `OperatorEntityInt` AS `o`
WHERE -(-`o`.`Value`) = `o`.`Value`
""");
    }

    public override async Task Negate_on_binary_expression(bool async)
    {
        await base.Negate_on_binary_expression(async);

        AssertSql(
"""
SELECT `o`.`Id` AS `Id1`, `o0`.`Id` AS `Id2`
FROM `OperatorEntityInt` AS `o`,
`OperatorEntityInt` AS `o0`
WHERE -`o`.`Value` = -(`o`.`Id` + `o0`.`Value`)
""");
    }

    public override async Task Negate_on_like_expression(bool async)
    {
        await base.Negate_on_like_expression(async);

        AssertSql(
"""
SELECT `o`.`Id`
FROM `OperatorEntityString` AS `o`
WHERE `o`.`Value` NOT LIKE 'A%' OR `o`.`Value` IS NULL
""");
    }

    /*[ConditionalTheory]
    [MemberData(nameof(IsAsyncData))]
    public virtual async Task Where_AtTimeZone_datetimeoffset_constant(bool async)
    {
        var contextFactory = await InitializeAsync<OperatorsContext>(seed: Seed);
        using var context = contextFactory.CreateContext();

        var expected = (from e in ExpectedData.OperatorEntitiesDateTimeOffset
                        where e.Value.UtcDateTime == new DateTimeOffset(2000, 1, 1, 18, 0, 0, TimeSpan.Zero)
                        select e.Id).ToList();

        var actual = (from e in context.Set<OperatorEntityDateTimeOffset>()
                      where EF.Functions.AtTimeZone(e.Value, "UTC") == new DateTimeOffset(2000, 1, 1, 18, 0, 0, TimeSpan.Zero)
                      select e.Id).ToList();

        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i], actual[i]);
        }

        AssertSql(
"""
SELECT [o].[Id]
FROM [OperatorEntityDateTimeOffset] AS [o]
WHERE [o].[Value] AT TIME ZONE 'UTC' = '2000-01-01T18:00:00.0000000+00:00'
""");
    }*/

    /*[ConditionalTheory]
    [MemberData(nameof(IsAsyncData))]
    public virtual async Task Where_AtTimeZone_datetimeoffset_parameter(bool async)
    {
        var contextFactory = await InitializeAsync<OperatorsContext>(seed: Seed);
        using var context = contextFactory.CreateContext();

        var dateTime = new DateTimeOffset(2000, 1, 1, 18, 0, 0, TimeSpan.Zero);
        var timeZone = "UTC";

        var expected = (from e in ExpectedData.OperatorEntitiesDateTimeOffset
                        where e.Value.UtcDateTime == dateTime
                        select e.Id).ToList();

        var actual = (from e in context.Set<OperatorEntityDateTimeOffset>()
                      where EF.Functions.AtTimeZone(e.Value, timeZone) == dateTime
                      select e.Id).ToList();

        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i], actual[i]);
        }

        AssertSql(
"""
@__timeZone_1='UTC' (Size = 8000) (DbType = AnsiString)
@__dateTime_2='2000-01-01T18:00:00.0000000+00:00'

SELECT [o].[Id]
FROM [OperatorEntityDateTimeOffset] AS [o]
WHERE [o].[Value] AT TIME ZONE @__timeZone_1 = @__dateTime_2
""");
    }

    [ConditionalTheory]
    [MemberData(nameof(IsAsyncData))]
    public virtual async Task Where_AtTimeZone_datetimeoffset_column(bool async)
    {
        var contextFactory = await InitializeAsync<OperatorsContext>(seed: Seed);
        using var context = contextFactory.CreateContext();

        var expected = (from e1 in ExpectedData.OperatorEntitiesDateTimeOffset
                        from e2 in ExpectedData.OperatorEntitiesDateTimeOffset
                        where e1.Value == e2.Value.UtcDateTime
                        select new { Id1 = e1.Id, Id2 = e2.Id }).ToList();

        var actual = (from e1 in context.Set<OperatorEntityDateTimeOffset>()
                      from e2 in context.Set<OperatorEntityDateTimeOffset>()
                      where EF.Functions.AtTimeZone(e1.Value, "UTC") == e2.Value
                      select new { Id1 = e1.Id, Id2 = e2.Id }).ToList();

        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Id1, actual[i].Id1);
            Assert.Equal(expected[i].Id2, actual[i].Id2);
        }

        AssertSql(
"""
SELECT [o].[Id] AS [Id1], [o0].[Id] AS [Id2]
FROM [OperatorEntityDateTimeOffset] AS [o]
CROSS JOIN [OperatorEntityDateTimeOffset] AS [o0]
WHERE [o].[Value] AT TIME ZONE 'UTC' = [o0].[Value]
""");
    }*/
}