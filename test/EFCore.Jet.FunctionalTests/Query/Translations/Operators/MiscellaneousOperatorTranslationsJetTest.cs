// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.EntityFrameworkCore.Query.Translations.Operators;
using Microsoft.EntityFrameworkCore.TestUtilities;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query.Translations.Operators;

public class MiscellaneousOperatorTranslationsJetTest : MiscellaneousOperatorTranslationsTestBase<BasicTypesQueryJetFixture>
{
    public MiscellaneousOperatorTranslationsJetTest(BasicTypesQueryJetFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    public override async Task Conditional(bool async)
    {
        await base.Conditional(async);

        AssertSql(
            """
SELECT `b`.`Id`, `b`.`Bool`, `b`.`Byte`, `b`.`ByteArray`, `b`.`DateOnly`, `b`.`DateTime`, `b`.`DateTimeOffset`, `b`.`Decimal`, `b`.`Double`, `b`.`Enum`, `b`.`FlagsEnum`, `b`.`Float`, `b`.`Guid`, `b`.`Int`, `b`.`Long`, `b`.`Short`, `b`.`String`, `b`.`TimeOnly`, `b`.`TimeSpan`
FROM `BasicTypesEntities` AS `b`
WHERE IIF(`b`.`Int` = 8, `b`.`String`, 'Foo') = 'Seattle'
""");
    }

    public override async Task Coalesce(bool async)
    {
        await base.Coalesce(async);

        AssertSql(
            """
SELECT `n`.`Id`, `n`.`Bool`, `n`.`Byte`, `n`.`ByteArray`, `n`.`DateOnly`, `n`.`DateTime`, `n`.`DateTimeOffset`, `n`.`Decimal`, `n`.`Double`, `n`.`Enum`, `n`.`FlagsEnum`, `n`.`Float`, `n`.`Guid`, `n`.`Int`, `n`.`Long`, `n`.`Short`, `n`.`String`, `n`.`TimeOnly`, `n`.`TimeSpan`
FROM `NullableBasicTypesEntities` AS `n`
WHERE IIF(`n`.`String` IS NULL, 'Unknown', `n`.`String`) = 'Seattle'
""");
    }

    [ConditionalFact]
    public virtual void Check_all_tests_overridden()
        => TestHelpers.AssertAllMethodsOverridden(GetType());

    private void AssertSql(params string[] expected)
        => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);
}
