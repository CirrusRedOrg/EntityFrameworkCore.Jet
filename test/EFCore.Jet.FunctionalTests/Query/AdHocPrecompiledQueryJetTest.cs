// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using EntityFrameworkCore.Jet.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query;

public class AdHocPrecompiledQueryJetTest(ITestOutputHelper testOutputHelper)
    : AdHocPrecompiledQueryRelationalTestBase(testOutputHelper)
{
    protected override bool AlwaysPrintGeneratedSources
        => false;

    [ConditionalTheory(Skip = "Not supported in Jet")]
    public override async Task Index_no_evaluatability()
    {
        await base.Index_no_evaluatability();

        AssertSql("""
SELECT [j].[Id], [j].[IntList], [j].[JsonThing]
FROM [JsonEntities] AS [j]
WHERE CAST(JSON_VALUE([j].[IntList], '$[' + CAST([j].[Id] AS nvarchar(max)) + ']') AS int) = 2
""");
    }

    [ConditionalTheory(Skip = "Not supported in Jet")]
    public override async Task Index_with_captured_variable()
    {
        await base.Index_with_captured_variable();

        AssertSql("""
@__id_0='1'

SELECT [j].[Id], [j].[IntList], [j].[JsonThing]
FROM [JsonEntities] AS [j]
WHERE CAST(JSON_VALUE([j].[IntList], '$[' + CAST(@__id_0 AS nvarchar(max)) + ']') AS int) = 2
""");
    }

    public override async Task JsonScalar()
    {
        await base.JsonScalar();

        AssertSql("""
SELECT [j].[Id], [j].[IntList], [j].[JsonThing]
FROM [JsonEntities] AS [j]
WHERE JSON_VALUE([j].[JsonThing], '$.StringProperty') = N'foo'
""");
    }

    public override async Task Materialize_non_public()
    {
        await base.Materialize_non_public();

        AssertSql(
            """"
@p0='10' (Nullable = true)
@p1='9' (Nullable = true)
@p2='8' (Nullable = true)

INSERT INTO `NonPublicEntities` (`PrivateAutoProperty`, `PrivateProperty`, `_privateField`)
VALUES (@p0, @p1, @p2);
SELECT `Id`
FROM `NonPublicEntities`
WHERE @@ROWCOUNT = 1 AND `Id` = @@identity;
"""",
//
""""
SELECT TOP 2 `n`.`Id`, `n`.`PrivateAutoProperty`, `n`.`PrivateProperty`, `n`.`_privateField`
FROM `NonPublicEntities` AS `n`
"""");
    }

    [ConditionalFact]
    public virtual void Check_all_tests_overridden()
        => TestHelpers.AssertAllMethodsOverridden(GetType());

    protected override ITestStoreFactory TestStoreFactory
        => JetTestStoreFactory.Instance;

    protected override PrecompiledQueryTestHelpers PrecompiledQueryTestHelpers
        => JetPrecompiledQueryTestHelpers.Instance;

    protected override DbContextOptionsBuilder AddOptions(DbContextOptionsBuilder builder)
    {
        builder = base.AddOptions(builder);

        // TODO: Figure out if there's a nice way to continue using the retrying strategy
        var sqlServerOptionsBuilder = new JetDbContextOptionsBuilder(builder);
        sqlServerOptionsBuilder.ExecutionStrategy(d => new NonRetryingExecutionStrategy(d));
        return builder;
    }
}
