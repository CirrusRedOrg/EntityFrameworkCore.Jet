// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query;

public class TPCFiltersInheritanceQueryJetTest : TPCFiltersInheritanceQueryTestBase<TPCFiltersInheritanceQueryJetFixture>
{
    public TPCFiltersInheritanceQueryJetTest(
        TPCFiltersInheritanceQueryJetFixture fixture,
        ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    [ConditionalFact]
    public virtual void Check_all_tests_overridden()
        => TestHelpers.AssertAllMethodsOverridden(GetType());

    public override async Task Can_use_of_type_animal(bool async)
    {
        await base.Can_use_of_type_animal(async);

        AssertSql(
            """
SELECT `u`.`Id`, `u`.`CountryId`, `u`.`Name`, `u`.`Species`, `u`.`EagleId`, `u`.`IsFlightless`, `u`.`Group`, `u`.`FoundOn`, `u`.`Discriminator`
FROM (
    SELECT `e`.`Id`, `e`.`CountryId`, `e`.`Name`, `e`.`Species`, `e`.`EagleId`, `e`.`IsFlightless`, `e`.`Group`, CVar(NULL) AS `FoundOn`, 'Eagle' AS `Discriminator`
    FROM `Eagle` AS `e`
    UNION ALL
    SELECT `k`.`Id`, `k`.`CountryId`, `k`.`Name`, `k`.`Species`, `k`.`EagleId`, `k`.`IsFlightless`, CVar(NULL) AS `Group`, `k`.`FoundOn`, 'Kiwi' AS `Discriminator`
    FROM `Kiwi` AS `k`
) AS `u`
WHERE `u`.`CountryId` = 1
ORDER BY `u`.`Species`
""");
    }

    public override async Task Can_use_is_kiwi(bool async)
    {
        await base.Can_use_is_kiwi(async);

        AssertSql(
            """
SELECT `u`.`Id`, `u`.`CountryId`, `u`.`Name`, `u`.`Species`, `u`.`EagleId`, `u`.`IsFlightless`, `u`.`Group`, `u`.`FoundOn`, `u`.`Discriminator`
FROM (
    SELECT `k`.`Id`, `k`.`CountryId`, `k`.`Name`, `k`.`Species`, `k`.`EagleId`, `k`.`IsFlightless`, CVar(NULL) AS `Group`, `k`.`FoundOn`, 'Kiwi' AS `Discriminator`
    FROM `Kiwi` AS `k`
) AS `u`
WHERE `u`.`CountryId` = 1
""");
    }

    public override async Task Can_use_is_kiwi_with_other_predicate(bool async)
    {
        await base.Can_use_is_kiwi_with_other_predicate(async);

        AssertSql(
            """
SELECT `u`.`Id`, `u`.`CountryId`, `u`.`Name`, `u`.`Species`, `u`.`EagleId`, `u`.`IsFlightless`, `u`.`Group`, `u`.`FoundOn`, `u`.`Discriminator`
FROM (
    SELECT `e`.`Id`, `e`.`CountryId`, `e`.`Name`, `e`.`Species`, `e`.`EagleId`, `e`.`IsFlightless`, `e`.`Group`, CVar(NULL) AS `FoundOn`, 'Eagle' AS `Discriminator`
    FROM `Eagle` AS `e`
    UNION ALL
    SELECT `k`.`Id`, `k`.`CountryId`, `k`.`Name`, `k`.`Species`, `k`.`EagleId`, `k`.`IsFlightless`, CVar(NULL) AS `Group`, `k`.`FoundOn`, 'Kiwi' AS `Discriminator`
    FROM `Kiwi` AS `k`
) AS `u`
WHERE `u`.`CountryId` = 1 AND `u`.`Discriminator` = 'Kiwi' AND `u`.`CountryId` = 1
""");
    }

    public override async Task Can_use_is_kiwi_in_projection(bool async)
    {
        await base.Can_use_is_kiwi_in_projection(async);

        AssertSql(
            """
SELECT IIF(`u`.`Discriminator` = 'Kiwi', TRUE, FALSE)
FROM (
    SELECT `e`.`CountryId`, 'Eagle' AS `Discriminator`
    FROM `Eagle` AS `e`
    UNION ALL
    SELECT `k`.`CountryId`, 'Kiwi' AS `Discriminator`
    FROM `Kiwi` AS `k`
) AS `u`
WHERE `u`.`CountryId` = 1
""");
    }

    public override async Task Can_use_of_type_bird(bool async)
    {
        await base.Can_use_of_type_bird(async);

        AssertSql(
            """
SELECT `u`.`Id`, `u`.`CountryId`, `u`.`Name`, `u`.`Species`, `u`.`EagleId`, `u`.`IsFlightless`, `u`.`Group`, `u`.`FoundOn`, `u`.`Discriminator`
FROM (
    SELECT `e`.`Id`, `e`.`CountryId`, `e`.`Name`, `e`.`Species`, `e`.`EagleId`, `e`.`IsFlightless`, `e`.`Group`, CVar(NULL) AS `FoundOn`, 'Eagle' AS `Discriminator`
    FROM `Eagle` AS `e`
    UNION ALL
    SELECT `k`.`Id`, `k`.`CountryId`, `k`.`Name`, `k`.`Species`, `k`.`EagleId`, `k`.`IsFlightless`, CVar(NULL) AS `Group`, `k`.`FoundOn`, 'Kiwi' AS `Discriminator`
    FROM `Kiwi` AS `k`
) AS `u`
WHERE `u`.`CountryId` = 1
ORDER BY `u`.`Species`
""");
    }

    public override async Task Can_use_of_type_bird_predicate(bool async)
    {
        await base.Can_use_of_type_bird_predicate(async);

        AssertSql(
            """
SELECT `u`.`Id`, `u`.`CountryId`, `u`.`Name`, `u`.`Species`, `u`.`EagleId`, `u`.`IsFlightless`, `u`.`Group`, `u`.`FoundOn`, `u`.`Discriminator`
FROM (
    SELECT `e`.`Id`, `e`.`CountryId`, `e`.`Name`, `e`.`Species`, `e`.`EagleId`, `e`.`IsFlightless`, `e`.`Group`, CVar(NULL) AS `FoundOn`, 'Eagle' AS `Discriminator`
    FROM `Eagle` AS `e`
    UNION ALL
    SELECT `k`.`Id`, `k`.`CountryId`, `k`.`Name`, `k`.`Species`, `k`.`EagleId`, `k`.`IsFlightless`, CVar(NULL) AS `Group`, `k`.`FoundOn`, 'Kiwi' AS `Discriminator`
    FROM `Kiwi` AS `k`
) AS `u`
WHERE `u`.`CountryId` = 1
ORDER BY `u`.`Species`
""");
    }

    public override async Task Can_use_of_type_bird_with_projection(bool async)
    {
        await base.Can_use_of_type_bird_with_projection(async);

        AssertSql(
            """
SELECT `u`.`Name`
FROM (
    SELECT `e`.`CountryId`, `e`.`Name`
    FROM `Eagle` AS `e`
    UNION ALL
    SELECT `k`.`CountryId`, `k`.`Name`
    FROM `Kiwi` AS `k`
) AS `u`
WHERE `u`.`CountryId` = 1
""");
    }

    public override async Task Can_use_of_type_bird_first(bool async)
    {
        await base.Can_use_of_type_bird_first(async);

        AssertSql(
            """
SELECT TOP 1 `u`.`Id`, `u`.`CountryId`, `u`.`Name`, `u`.`Species`, `u`.`EagleId`, `u`.`IsFlightless`, `u`.`Group`, `u`.`FoundOn`, `u`.`Discriminator`
FROM (
    SELECT `e`.`Id`, `e`.`CountryId`, `e`.`Name`, `e`.`Species`, `e`.`EagleId`, `e`.`IsFlightless`, `e`.`Group`, CVar(NULL) AS `FoundOn`, 'Eagle' AS `Discriminator`
    FROM `Eagle` AS `e`
    UNION ALL
    SELECT `k`.`Id`, `k`.`CountryId`, `k`.`Name`, `k`.`Species`, `k`.`EagleId`, `k`.`IsFlightless`, CVar(NULL) AS `Group`, `k`.`FoundOn`, 'Kiwi' AS `Discriminator`
    FROM `Kiwi` AS `k`
) AS `u`
WHERE `u`.`CountryId` = 1
ORDER BY `u`.`Species`
""");
    }

    public override async Task Can_use_of_type_kiwi(bool async)
    {
        await base.Can_use_of_type_kiwi(async);

        AssertSql(
            """
SELECT `u`.`Id`, `u`.`CountryId`, `u`.`Name`, `u`.`Species`, `u`.`EagleId`, `u`.`IsFlightless`, `u`.`FoundOn`, `u`.`Discriminator`
FROM (
    SELECT `k`.`Id`, `k`.`CountryId`, `k`.`Name`, `k`.`Species`, `k`.`EagleId`, `k`.`IsFlightless`, `k`.`FoundOn`, 'Kiwi' AS `Discriminator`
    FROM `Kiwi` AS `k`
) AS `u`
WHERE `u`.`CountryId` = 1
""");
    }

    public override async Task Can_use_derived_set(bool async)
    {
        await base.Can_use_derived_set(async);

        AssertSql(
            """
SELECT `e`.`Id`, `e`.`CountryId`, `e`.`Name`, `e`.`Species`, `e`.`EagleId`, `e`.`IsFlightless`, `e`.`Group`
FROM `Eagle` AS `e`
WHERE `e`.`CountryId` = 1
""");
    }

    public override async Task Can_use_IgnoreQueryFilters_and_GetDatabaseValues(bool async)
    {
        await base.Can_use_IgnoreQueryFilters_and_GetDatabaseValues(async);

        AssertSql(
            """
SELECT TOP 2 `e`.`Id`, `e`.`CountryId`, `e`.`Name`, `e`.`Species`, `e`.`EagleId`, `e`.`IsFlightless`, `e`.`Group`
FROM `Eagle` AS `e`
""",
            //
            $"""
@__p_0='2'

SELECT TOP 1 `e`.`Id`, `e`.`CountryId`, `e`.`Name`, `e`.`Species`, `e`.`EagleId`, `e`.`IsFlightless`, `e`.`Group`
FROM `Eagle` AS `e`
WHERE `e`.`Id` = {AssertSqlHelper.Parameter("@__p_0")}
""");
    }

    private void AssertSql(params string[] expected)
        => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);
}
