// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.EntityFrameworkCore.Query.Translations;
using Microsoft.EntityFrameworkCore.TestUtilities;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query.Translations;

public class GuidTranslationsJetTest : GuidTranslationsTestBase<BasicTypesQueryJetFixture>
{
    public GuidTranslationsJetTest(BasicTypesQueryJetFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    public override async Task New_with_constant(bool async)
    {
        await base.New_with_constant(async);

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`Guid` = {df36f493-463f-4123-83f9-6b135deeb7ba}
""");
    }

    public override async Task New_with_parameter(bool async)
    {
        await base.New_with_parameter(async);

        AssertSql(
            """
@p='df36f493-463f-4123-83f9-6b135deeb7ba'

SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE `b`.`Guid` = @p
""");
    }

    public override async Task ToString_projection(bool async)
    {
        await base.ToString_projection(async);

        AssertSql(
            """
SELECT CONVERT(varchar(36), [b].[Guid])
FROM [BasicTypesEntities] AS [b]
""");
    }

    public override async Task NewGuid(bool async)
    {
        await base.NewGuid(async);
    }

    [ConditionalFact]
    public virtual void Check_all_tests_overridden()
        => TestHelpers.AssertAllMethodsOverridden(GetType());

    private void AssertSql(params string[] expected)
        => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);
}
