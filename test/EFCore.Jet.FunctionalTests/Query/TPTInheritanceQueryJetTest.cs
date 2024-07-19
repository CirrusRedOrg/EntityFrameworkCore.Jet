// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// ReSharper disable InconsistentNaming

using System.Threading.Tasks;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query;

public class TPTInheritanceQueryJetTest : TPTInheritanceQueryTestBase<TPTInheritanceQueryJetFixture>
{
    public TPTInheritanceQueryJetTest(TPTInheritanceQueryJetFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture, testOutputHelper)
    {
    }

    [ConditionalFact]
    public virtual void Check_all_tests_overridden()
        => TestHelpers.AssertAllMethodsOverridden(GetType());

    public override async Task Byte_enum_value_constant_used_in_projection(bool async)
    {
        await base.Byte_enum_value_constant_used_in_projection(async);

        AssertSql(
            """
SELECT IIF(`b`.`IsFlightless` = TRUE, CBYTE(0), CBYTE(1))
FROM (`Animals` AS `a`
INNER JOIN `Birds` AS `b` ON `a`.`Id` = `b`.`Id`)
INNER JOIN `Kiwi` AS `k` ON `a`.`Id` = `k`.`Id`
""");
    }

    public override async Task Can_filter_all_animals(bool async)
    {
        await base.Can_filter_all_animals(async);

        AssertSql(
"""
SELECT `a`.`Id`, `a`.`CountryId`, `a`.`Name`, `a`.`Species`, `b`.`EagleId`, `b`.`IsFlightless`, `e`.`Group`, `k`.`FoundOn`, IIF(`k`.`Id` IS NOT NULL, 'Kiwi', IIF(`e`.`Id` IS NOT NULL, 'Eagle', NULL)) AS `Discriminator`
FROM ((`Animals` AS `a`
LEFT JOIN `Birds` AS `b` ON `a`.`Id` = `b`.`Id`)
LEFT JOIN `Eagle` AS `e` ON `a`.`Id` = `e`.`Id`)
LEFT JOIN `Kiwi` AS `k` ON `a`.`Id` = `k`.`Id`
WHERE `a`.`Name` = 'Great spotted kiwi'
ORDER BY `a`.`Species`
""");
    }

    public override async Task Can_include_animals(bool async)
    {
        await base.Can_include_animals(async);

        AssertSql(
"""
SELECT `c`.`Id`, `c`.`Name`, `t`.`Id`, `t`.`CountryId`, `t`.`Name`, `t`.`Species`, `t`.`EagleId`, `t`.`IsFlightless`, `t`.`Group`, `t`.`FoundOn`, `t`.`Discriminator`
FROM `Countries` AS `c`
LEFT JOIN (
    SELECT `a`.`Id`, `a`.`CountryId`, `a`.`Name`, `a`.`Species`, `b`.`EagleId`, `b`.`IsFlightless`, `e`.`Group`, `k`.`FoundOn`, IIF(`k`.`Id` IS NOT NULL, 'Kiwi', IIF(`e`.`Id` IS NOT NULL, 'Eagle', NULL)) AS `Discriminator`
    FROM ((`Animals` AS `a`
    LEFT JOIN `Birds` AS `b` ON `a`.`Id` = `b`.`Id`)
    LEFT JOIN `Eagle` AS `e` ON `a`.`Id` = `e`.`Id`)
    LEFT JOIN `Kiwi` AS `k` ON `a`.`Id` = `k`.`Id`
) AS `t` ON `c`.`Id` = `t`.`CountryId`
ORDER BY `c`.`Name`, `c`.`Id`
""");
    }

    public override async Task Can_include_prey(bool async)
    {
        await base.Can_include_prey(async);

        AssertSql(
"""
SELECT `t`.`Id`, `t`.`CountryId`, `t`.`Name`, `t`.`Species`, `t`.`EagleId`, `t`.`IsFlightless`, `t`.`Group`, `t0`.`Id`, `t0`.`CountryId`, `t0`.`Name`, `t0`.`Species`, `t0`.`EagleId`, `t0`.`IsFlightless`, `t0`.`Group`, `t0`.`FoundOn`, `t0`.`Discriminator`
FROM (
    SELECT TOP 2 `a`.`Id`, `a`.`CountryId`, `a`.`Name`, `a`.`Species`, `b`.`EagleId`, `b`.`IsFlightless`, `e`.`Group`
    FROM (`Animals` AS `a`
    INNER JOIN `Birds` AS `b` ON `a`.`Id` = `b`.`Id`)
    INNER JOIN `Eagle` AS `e` ON `a`.`Id` = `e`.`Id`
) AS `t`
LEFT JOIN (
    SELECT `a0`.`Id`, `a0`.`CountryId`, `a0`.`Name`, `a0`.`Species`, `b0`.`EagleId`, `b0`.`IsFlightless`, `e0`.`Group`, `k`.`FoundOn`, IIF(`k`.`Id` IS NOT NULL, 'Kiwi', IIF(`e0`.`Id` IS NOT NULL, 'Eagle', NULL)) AS `Discriminator`
    FROM ((`Animals` AS `a0`
    INNER JOIN `Birds` AS `b0` ON `a0`.`Id` = `b0`.`Id`)
    LEFT JOIN `Eagle` AS `e0` ON `a0`.`Id` = `e0`.`Id`)
    LEFT JOIN `Kiwi` AS `k` ON `a0`.`Id` = `k`.`Id`
) AS `t0` ON `t`.`Id` = `t0`.`EagleId`
ORDER BY `t`.`Id`
""");
    }

    public override async Task Can_insert_update_delete()
        => await base.Can_insert_update_delete();

    public override async Task Can_query_all_animals(bool async)
    {
        await base.Can_query_all_animals(async);

        AssertSql(
"""
SELECT `a`.`Id`, `a`.`CountryId`, `a`.`Name`, `a`.`Species`, `b`.`EagleId`, `b`.`IsFlightless`, `e`.`Group`, `k`.`FoundOn`, IIF(`k`.`Id` IS NOT NULL, 'Kiwi', IIF(`e`.`Id` IS NOT NULL, 'Eagle', NULL)) AS `Discriminator`
FROM ((`Animals` AS `a`
LEFT JOIN `Birds` AS `b` ON `a`.`Id` = `b`.`Id`)
LEFT JOIN `Eagle` AS `e` ON `a`.`Id` = `e`.`Id`)
LEFT JOIN `Kiwi` AS `k` ON `a`.`Id` = `k`.`Id`
ORDER BY `a`.`Species`
""");
    }

    public override async Task Can_query_all_birds(bool async)
    {
        await base.Can_query_all_birds(async);

        AssertSql(
"""
SELECT `a`.`Id`, `a`.`CountryId`, `a`.`Name`, `a`.`Species`, `b`.`EagleId`, `b`.`IsFlightless`, `e`.`Group`, `k`.`FoundOn`, IIF(`k`.`Id` IS NOT NULL, 'Kiwi', IIF(`e`.`Id` IS NOT NULL, 'Eagle', NULL)) AS `Discriminator`
FROM ((`Animals` AS `a`
INNER JOIN `Birds` AS `b` ON `a`.`Id` = `b`.`Id`)
LEFT JOIN `Eagle` AS `e` ON `a`.`Id` = `e`.`Id`)
LEFT JOIN `Kiwi` AS `k` ON `a`.`Id` = `k`.`Id`
ORDER BY `a`.`Species`
""");
    }

    public override async Task Can_query_all_plants(bool async)
    {
        await base.Can_query_all_plants(async);

        AssertSql(
            """
SELECT `p`.`Species`, `p`.`CountryId`, `p`.`Genus`, `p`.`Name`, `r`.`HasThorns`, `d`.`AdditionalInfo_Nickname`, `d`.`AdditionalInfo_LeafStructure_AreLeavesBig`, `d`.`AdditionalInfo_LeafStructure_NumLeaves`, IIF(`r`.`Species` IS NOT NULL, 'Rose', IIF(`d`.`Species` IS NOT NULL, 'Daisy', NULL)) AS `Discriminator`
FROM (`Plants` AS `p`
LEFT JOIN `Daisies` AS `d` ON `p`.`Species` = `d`.`Species`)
LEFT JOIN `Roses` AS `r` ON `p`.`Species` = `r`.`Species`
ORDER BY `p`.`Species`
""");
    }

    public override async Task Filter_on_property_inside_complex_type_on_derived_type(bool async)
    {
        await base.Filter_on_property_inside_complex_type_on_derived_type(async);

        AssertSql(
            """
SELECT `p`.`Species`, `p`.`CountryId`, `p`.`Genus`, `p`.`Name`, `d`.`AdditionalInfo_Nickname`, `d`.`AdditionalInfo_LeafStructure_AreLeavesBig`, `d`.`AdditionalInfo_LeafStructure_NumLeaves`
FROM (`Plants` AS `p`
INNER JOIN `Flowers` AS `f` ON `p`.`Species` = `f`.`Species`)
INNER JOIN `Daisies` AS `d` ON `p`.`Species` = `d`.`Species`
WHERE `d`.`AdditionalInfo_LeafStructure_AreLeavesBig` = TRUE
""");
    }

    public override async Task Can_query_all_types_when_shared_column(bool async)
    {
        await base.Can_query_all_types_when_shared_column(async);

        AssertSql(
"""
SELECT `d`.`Id`, `d`.`SortIndex`, `c`.`CaffeineGrams`, `c`.`CokeCO2`, `c`.`SugarGrams`, `l`.`LiltCO2`, `l`.`SugarGrams`, `t`.`CaffeineGrams`, `t`.`HasMilk`, IIF(`t`.`Id` IS NOT NULL, 'Tea', IIF(`l`.`Id` IS NOT NULL, 'Lilt', IIF(`c`.`Id` IS NOT NULL, 'Coke', NULL))) AS `Discriminator`
FROM ((`Drinks` AS `d`
LEFT JOIN `Coke` AS `c` ON `d`.`Id` = `c`.`Id`)
LEFT JOIN `Lilt` AS `l` ON `d`.`Id` = `l`.`Id`)
LEFT JOIN `Tea` AS `t` ON `d`.`Id` = `t`.`Id`
""");
    }

    public override async Task Can_query_just_kiwis(bool async)
    {
        await base.Can_query_just_kiwis(async);

        AssertSql(
"""
SELECT TOP 2 `a`.`Id`, `a`.`CountryId`, `a`.`Name`, `a`.`Species`, `b`.`EagleId`, `b`.`IsFlightless`, `k`.`FoundOn`
FROM (`Animals` AS `a`
INNER JOIN `Birds` AS `b` ON `a`.`Id` = `b`.`Id`)
INNER JOIN `Kiwi` AS `k` ON `a`.`Id` = `k`.`Id`
""");
    }

    public override async Task Can_query_just_roses(bool async)
    {
        await base.Can_query_just_roses(async);

        AssertSql(
"""
SELECT TOP 2 `p`.`Species`, `p`.`CountryId`, `p`.`Genus`, `p`.`Name`, `r`.`HasThorns`
FROM (`Plants` AS `p`
INNER JOIN `Flowers` AS `f` ON `p`.`Species` = `f`.`Species`)
INNER JOIN `Roses` AS `r` ON `p`.`Species` = `r`.`Species`
""");
    }

    public override async Task Can_query_when_shared_column(bool async)
    {
        await base.Can_query_when_shared_column(async);

        AssertSql(
"""
SELECT TOP 2 `d`.`Id`, `d`.`SortIndex`, `c`.`CaffeineGrams`, `c`.`CokeCO2`, `c`.`SugarGrams`
FROM `Drinks` AS `d`
INNER JOIN `Coke` AS `c` ON `d`.`Id` = `c`.`Id`
""",
//
"""
SELECT TOP 2 `d`.`Id`, `d`.`SortIndex`, `l`.`LiltCO2`, `l`.`SugarGrams`
FROM `Drinks` AS `d`
INNER JOIN `Lilt` AS `l` ON `d`.`Id` = `l`.`Id`
""",
//
"""
SELECT TOP 2 `d`.`Id`, `d`.`SortIndex`, `t`.`CaffeineGrams`, `t`.`HasMilk`
FROM `Drinks` AS `d`
INNER JOIN `Tea` AS `t` ON `d`.`Id` = `t`.`Id`
""");
    }

    public override async Task Can_use_backwards_is_animal(bool async)
    {
        await base.Can_use_backwards_is_animal(async);

        AssertSql(
"""
SELECT `a`.`Id`, `a`.`CountryId`, `a`.`Name`, `a`.`Species`, `b`.`EagleId`, `b`.`IsFlightless`, `k`.`FoundOn`
FROM (`Animals` AS `a`
INNER JOIN `Birds` AS `b` ON `a`.`Id` = `b`.`Id`)
INNER JOIN `Kiwi` AS `k` ON `a`.`Id` = `k`.`Id`
""");
    }

    public override async Task Can_use_backwards_of_type_animal(bool async)
    {
        await base.Can_use_backwards_of_type_animal(async);

        AssertSql(
"""
SELECT `a`.`Id`, `a`.`CountryId`, `a`.`Name`, `a`.`Species`, `b`.`EagleId`, `b`.`IsFlightless`, `k`.`FoundOn`
FROM (`Animals` AS `a`
INNER JOIN `Birds` AS `b` ON `a`.`Id` = `b`.`Id`)
INNER JOIN `Kiwi` AS `k` ON `a`.`Id` = `k`.`Id`
""");
    }

    public override async Task Can_use_is_kiwi(bool async)
    {
        await base.Can_use_is_kiwi(async);

        AssertSql(
"""
SELECT `a`.`Id`, `a`.`CountryId`, `a`.`Name`, `a`.`Species`, `b`.`EagleId`, `b`.`IsFlightless`, `e`.`Group`, `k`.`FoundOn`, IIF(`k`.`Id` IS NOT NULL, 'Kiwi', IIF(`e`.`Id` IS NOT NULL, 'Eagle', NULL)) AS `Discriminator`
FROM ((`Animals` AS `a`
LEFT JOIN `Birds` AS `b` ON `a`.`Id` = `b`.`Id`)
LEFT JOIN `Eagle` AS `e` ON `a`.`Id` = `e`.`Id`)
LEFT JOIN `Kiwi` AS `k` ON `a`.`Id` = `k`.`Id`
WHERE `k`.`Id` IS NOT NULL
""");
    }

    public override async Task Can_use_is_kiwi_with_cast(bool async)
    {
        await base.Can_use_is_kiwi_with_cast(async);

        AssertSql(
            """
SELECT IIF(`k`.`Id` IS NOT NULL, `k`.`FoundOn`, CBYTE(0)) AS `Value`
FROM `Animals` AS `a`
LEFT JOIN `Kiwi` AS `k` ON `a`.`Id` = `k`.`Id`
""");
    }

    public override async Task Can_use_is_kiwi_in_projection(bool async)
    {
        await base.Can_use_is_kiwi_in_projection(async);

        AssertSql(
"""
SELECT IIF(`k`.`Id` IS NOT NULL, TRUE, FALSE)
FROM `Animals` AS `a`
LEFT JOIN `Kiwi` AS `k` ON `a`.`Id` = `k`.`Id`
""");
    }

    public override async Task Can_use_is_kiwi_with_other_predicate(bool async)
    {
        await base.Can_use_is_kiwi_with_other_predicate(async);

        AssertSql(
"""
SELECT `a`.`Id`, `a`.`CountryId`, `a`.`Name`, `a`.`Species`, `b`.`EagleId`, `b`.`IsFlightless`, `e`.`Group`, `k`.`FoundOn`, IIF(`k`.`Id` IS NOT NULL, 'Kiwi', IIF(`e`.`Id` IS NOT NULL, 'Eagle', NULL)) AS `Discriminator`
FROM ((`Animals` AS `a`
LEFT JOIN `Birds` AS `b` ON `a`.`Id` = `b`.`Id`)
LEFT JOIN `Eagle` AS `e` ON `a`.`Id` = `e`.`Id`)
LEFT JOIN `Kiwi` AS `k` ON `a`.`Id` = `k`.`Id`
WHERE `k`.`Id` IS NOT NULL AND `a`.`CountryId` = 1
""");
    }

    public override async Task Can_use_of_type_animal(bool async)
    {
        await base.Can_use_of_type_animal(async);

        AssertSql(
"""
SELECT `a`.`Id`, `a`.`CountryId`, `a`.`Name`, `a`.`Species`, `b`.`EagleId`, `b`.`IsFlightless`, `e`.`Group`, `k`.`FoundOn`, IIF(`k`.`Id` IS NOT NULL, 'Kiwi', IIF(`e`.`Id` IS NOT NULL, 'Eagle', NULL)) AS `Discriminator`
FROM ((`Animals` AS `a`
LEFT JOIN `Birds` AS `b` ON `a`.`Id` = `b`.`Id`)
LEFT JOIN `Eagle` AS `e` ON `a`.`Id` = `e`.`Id`)
LEFT JOIN `Kiwi` AS `k` ON `a`.`Id` = `k`.`Id`
ORDER BY `a`.`Species`
""");
    }

    public override async Task Can_use_of_type_bird(bool async)
    {
        await base.Can_use_of_type_bird(async);

        AssertSql(
"""
SELECT `a`.`Id`, `a`.`CountryId`, `a`.`Name`, `a`.`Species`, `b`.`EagleId`, `b`.`IsFlightless`, `e`.`Group`, `k`.`FoundOn`, IIF(`k`.`Id` IS NOT NULL, 'Kiwi', IIF(`e`.`Id` IS NOT NULL, 'Eagle', NULL)) AS `Discriminator`
FROM ((`Animals` AS `a`
LEFT JOIN `Birds` AS `b` ON `a`.`Id` = `b`.`Id`)
LEFT JOIN `Eagle` AS `e` ON `a`.`Id` = `e`.`Id`)
LEFT JOIN `Kiwi` AS `k` ON `a`.`Id` = `k`.`Id`
WHERE `k`.`Id` IS NOT NULL OR `e`.`Id` IS NOT NULL
ORDER BY `a`.`Species`
""");
    }

    public override async Task Can_use_of_type_bird_first(bool async)
    {
        await base.Can_use_of_type_bird_first(async);

        AssertSql(
"""
SELECT TOP 1 `a`.`Id`, `a`.`CountryId`, `a`.`Name`, `a`.`Species`, `b`.`EagleId`, `b`.`IsFlightless`, `e`.`Group`, `k`.`FoundOn`, IIF(`k`.`Id` IS NOT NULL, 'Kiwi', IIF(`e`.`Id` IS NOT NULL, 'Eagle', NULL)) AS `Discriminator`
FROM ((`Animals` AS `a`
LEFT JOIN `Birds` AS `b` ON `a`.`Id` = `b`.`Id`)
LEFT JOIN `Eagle` AS `e` ON `a`.`Id` = `e`.`Id`)
LEFT JOIN `Kiwi` AS `k` ON `a`.`Id` = `k`.`Id`
WHERE `k`.`Id` IS NOT NULL OR `e`.`Id` IS NOT NULL
ORDER BY `a`.`Species`
""");
    }

    public override async Task Can_use_of_type_bird_predicate(bool async)
    {
        await base.Can_use_of_type_bird_predicate(async);

        AssertSql(
"""
SELECT `a`.`Id`, `a`.`CountryId`, `a`.`Name`, `a`.`Species`, `b`.`EagleId`, `b`.`IsFlightless`, `e`.`Group`, `k`.`FoundOn`, IIF(`k`.`Id` IS NOT NULL, 'Kiwi', IIF(`e`.`Id` IS NOT NULL, 'Eagle', NULL)) AS `Discriminator`
FROM ((`Animals` AS `a`
LEFT JOIN `Birds` AS `b` ON `a`.`Id` = `b`.`Id`)
LEFT JOIN `Eagle` AS `e` ON `a`.`Id` = `e`.`Id`)
LEFT JOIN `Kiwi` AS `k` ON `a`.`Id` = `k`.`Id`
WHERE `a`.`CountryId` = 1 AND (`k`.`Id` IS NOT NULL OR `e`.`Id` IS NOT NULL)
ORDER BY `a`.`Species`
""");
    }

    public override async Task Can_use_of_type_bird_with_projection(bool async)
    {
        await base.Can_use_of_type_bird_with_projection(async);

        AssertSql(
"""
SELECT `b`.`EagleId`
FROM ((`Animals` AS `a`
LEFT JOIN `Birds` AS `b` ON `a`.`Id` = `b`.`Id`)
LEFT JOIN `Eagle` AS `e` ON `a`.`Id` = `e`.`Id`)
LEFT JOIN `Kiwi` AS `k` ON `a`.`Id` = `k`.`Id`
WHERE `k`.`Id` IS NOT NULL OR `e`.`Id` IS NOT NULL
""");
    }

    public override async Task Can_use_of_type_kiwi(bool async)
    {
        await base.Can_use_of_type_kiwi(async);

        AssertSql(
"""
SELECT `a`.`Id`, `a`.`CountryId`, `a`.`Name`, `a`.`Species`, `b`.`EagleId`, `b`.`IsFlightless`, `k`.`FoundOn`, IIF(`k`.`Id` IS NOT NULL, 'Kiwi', NULL) AS `Discriminator`
FROM (`Animals` AS `a`
LEFT JOIN `Birds` AS `b` ON `a`.`Id` = `b`.`Id`)
LEFT JOIN `Kiwi` AS `k` ON `a`.`Id` = `k`.`Id`
WHERE `k`.`Id` IS NOT NULL
""");
    }

    public override async Task Can_use_of_type_kiwi_where_north_on_derived_property(bool async)
    {
        await base.Can_use_of_type_kiwi_where_north_on_derived_property(async);

        AssertSql(
            """
SELECT `a`.`Id`, `a`.`CountryId`, `a`.`Name`, `a`.`Species`, `b`.`EagleId`, `b`.`IsFlightless`, `k`.`FoundOn`, IIF(`k`.`Id` IS NOT NULL, 'Kiwi', NULL) AS `Discriminator`
FROM (`Animals` AS `a`
LEFT JOIN `Birds` AS `b` ON `a`.`Id` = `b`.`Id`)
LEFT JOIN `Kiwi` AS `k` ON `a`.`Id` = `k`.`Id`
WHERE `k`.`Id` IS NOT NULL AND `k`.`FoundOn` = CBYTE(0)
""");
    }

    public override async Task Can_use_of_type_kiwi_where_south_on_derived_property(bool async)
    {
        await base.Can_use_of_type_kiwi_where_south_on_derived_property(async);

        AssertSql(
            """
SELECT `a`.`Id`, `a`.`CountryId`, `a`.`Name`, `a`.`Species`, `b`.`EagleId`, `b`.`IsFlightless`, `k`.`FoundOn`, IIF(`k`.`Id` IS NOT NULL, 'Kiwi', NULL) AS `Discriminator`
FROM (`Animals` AS `a`
LEFT JOIN `Birds` AS `b` ON `a`.`Id` = `b`.`Id`)
LEFT JOIN `Kiwi` AS `k` ON `a`.`Id` = `k`.`Id`
WHERE `k`.`Id` IS NOT NULL AND `k`.`FoundOn` = CBYTE(1)
""");
    }

    public override async Task Can_use_of_type_rose(bool async)
    {
        await base.Can_use_of_type_rose(async);

        AssertSql(
"""
SELECT `p`.`Species`, `p`.`CountryId`, `p`.`Genus`, `p`.`Name`, `r`.`HasThorns`, IIF(`r`.`Species` IS NOT NULL, 'Rose', NULL) AS `Discriminator`
FROM `Plants` AS `p`
LEFT JOIN `Roses` AS `r` ON `p`.`Species` = `r`.`Species`
WHERE `r`.`Species` IS NOT NULL
""");
    }

    public override async Task Member_access_on_intermediate_type_works()
    {
        await base.Member_access_on_intermediate_type_works();

        AssertSql(
"""
SELECT `a`.`Name`
FROM (`Animals` AS `a`
INNER JOIN `Birds` AS `b` ON `a`.`Id` = `b`.`Id`)
INNER JOIN `Kiwi` AS `k` ON `a`.`Id` = `k`.`Id`
ORDER BY `a`.`Name`
""");
    }

    public override async Task OfType_Union_OfType(bool async)
    {
        await base.OfType_Union_OfType(async);

        AssertSql(" ");
    }

    public override async Task OfType_Union_subquery(bool async)
    {
        await base.OfType_Union_subquery(async);

        AssertSql(" ");
    }

    public override async Task Setting_foreign_key_to_a_different_type_throws()
    {
        await base.Setting_foreign_key_to_a_different_type_throws();

        AssertSql(
"""
SELECT TOP 2 `a`.`Id`, `a`.`CountryId`, `a`.`Name`, `a`.`Species`, `b`.`EagleId`, `b`.`IsFlightless`, `k`.`FoundOn`
FROM (`Animals` AS `a`
INNER JOIN `Birds` AS `b` ON `a`.`Id` = `b`.`Id`)
INNER JOIN `Kiwi` AS `k` ON `a`.`Id` = `k`.`Id`
""",
//
$"""
@p0='0'
@p1='Bald eagle' (Size = 255)
@p2='Haliaeetus leucocephalus' (Size = 100)

INSERT INTO `Animals` (`CountryId`, `Name`, `Species`)
VALUES ({AssertSqlHelper.Parameter("@p0")}, {AssertSqlHelper.Parameter("@p1")}, {AssertSqlHelper.Parameter("@p2")});
SELECT `Id`
FROM `Animals`
WHERE @@ROWCOUNT = 1 AND `Id` = @@identity;
""");
    }

    public override async Task Subquery_OfType(bool async)
    {
        await base.Subquery_OfType(async);

        AssertSql(
"""
SELECT DISTINCT `t`.`Id`, `t`.`CountryId`, `t`.`Name`, `t`.`Species`, `t`.`EagleId`, `t`.`IsFlightless`, `t`.`FoundOn`, `t`.`Discriminator`
FROM (
    SELECT TOP 5 `a`.`Id`, `a`.`CountryId`, `a`.`Name`, `a`.`Species`, `b`.`EagleId`, `b`.`IsFlightless`, `k`.`FoundOn`, IIF(`k`.`Id` IS NOT NULL, 'Kiwi', IIF(`e`.`Id` IS NOT NULL, 'Eagle', NULL)) AS `Discriminator`
    FROM ((`Animals` AS `a`
    INNER JOIN `Birds` AS `b` ON `a`.`Id` = `b`.`Id`)
    LEFT JOIN `Eagle` AS `e` ON `a`.`Id` = `e`.`Id`)
    LEFT JOIN `Kiwi` AS `k` ON `a`.`Id` = `k`.`Id`
    ORDER BY `a`.`Species`
) AS `t`
WHERE `t`.`Discriminator` = 'Kiwi'
""");
    }

    public override async Task Union_entity_equality(bool async)
    {
        await base.Union_entity_equality(async);

        AssertSql(" ");
    }

    public override async Task Union_siblings_with_duplicate_property_in_subquery(bool async)
    {
        await base.Union_siblings_with_duplicate_property_in_subquery(async);

        AssertSql(
"""
SELECT [a].[Id], [a].[CountryId], [a].[Name], [a].[Species], [b].[EagleId], [b].[IsFlightless], [e].[Group], [k].[FoundOn], CASE
    WHEN [k].[Id] IS NOT NULL THEN N'Kiwi'
    WHEN [e].[Id] IS NOT NULL THEN N'Eagle'
END AS [Discriminator]
FROM [Animals] AS [a]
LEFT JOIN [Birds] AS [b] ON [a].[Id] = [b].[Id]
LEFT JOIN [Eagle] AS [e] ON [a].[Id] = [e].[Id]
LEFT JOIN [Kiwi] AS [k] ON [a].[Id] = [k].[Id]
WHERE EXISTS (
    SELECT 1
    FROM (
        SELECT TOP(1) [a0].[Id], [a0].[CountryId], [a0].[Name], [a0].[Species], [b0].[EagleId], [b0].[IsFlightless], [e0].[Group], [k0].[FoundOn], CASE
            WHEN [k0].[Id] IS NOT NULL THEN N'Kiwi'
            WHEN [e0].[Id] IS NOT NULL THEN N'Eagle'
        END AS [Discriminator], [k0].[Id] AS [Id0]
        FROM [Animals] AS [a0]
        LEFT JOIN [Birds] AS [b0] ON [a0].[Id] = [b0].[Id]
        LEFT JOIN [Eagle] AS [e0] ON [a0].[Id] = [e0].[Id]
        LEFT JOIN [Kiwi] AS [k0] ON [a0].[Id] = [k0].[Id]
        WHERE [a0].[Name] = N'Great spotted kiwi'
    ) AS [t]
    WHERE [t].[Id0] IS NOT NULL)
ORDER BY [a].[Species]
""");
    }

    public override async Task Is_operator_on_result_of_FirstOrDefault(bool async)
    {
        await base.Is_operator_on_result_of_FirstOrDefault(async);

        AssertSql(
"""
SELECT `a`.`Id`, `a`.`CountryId`, `a`.`Name`, `a`.`Species`, `b`.`EagleId`, `b`.`IsFlightless`, `e`.`Group`, `k`.`FoundOn`, IIF(`k`.`Id` IS NOT NULL, 'Kiwi', IIF(`e`.`Id` IS NOT NULL, 'Eagle', NULL)) AS `Discriminator`
FROM ((`Animals` AS `a`
LEFT JOIN `Birds` AS `b` ON `a`.`Id` = `b`.`Id`)
LEFT JOIN `Eagle` AS `e` ON `a`.`Id` = `e`.`Id`)
LEFT JOIN `Kiwi` AS `k` ON `a`.`Id` = `k`.`Id`
WHERE EXISTS (
    SELECT 1
    FROM (
        SELECT TOP 1 `a0`.`Id`, `a0`.`CountryId`, `a0`.`Name`, `a0`.`Species`, `b0`.`EagleId`, `b0`.`IsFlightless`, `e0`.`Group`, `k0`.`FoundOn`, IIF(`k0`.`Id` IS NOT NULL, 'Kiwi', IIF(`e0`.`Id` IS NOT NULL, 'Eagle', NULL)) AS `Discriminator`, `k0`.`Id` AS `Id0`
        FROM ((`Animals` AS `a0`
        LEFT JOIN `Birds` AS `b0` ON `a0`.`Id` = `b0`.`Id`)
        LEFT JOIN `Eagle` AS `e0` ON `a0`.`Id` = `e0`.`Id`)
        LEFT JOIN `Kiwi` AS `k0` ON `a0`.`Id` = `k0`.`Id`
        WHERE `a0`.`Name` = 'Great spotted kiwi'
    ) AS `t`
    WHERE `t`.`Id0` IS NOT NULL)
ORDER BY `a`.`Species`
""");
    }

    public override async Task Selecting_only_base_properties_on_base_type(bool async)
    {
        await base.Selecting_only_base_properties_on_base_type(async);

        AssertSql(
"""
SELECT `a`.`Name`
FROM `Animals` AS `a`
""");
    }

    public override async Task Selecting_only_base_properties_on_derived_type(bool async)
    {
        await base.Selecting_only_base_properties_on_derived_type(async);

        AssertSql(
"""
SELECT `a`.`Name`
FROM `Animals` AS `a`
INNER JOIN `Birds` AS `b` ON `a`.`Id` = `b`.`Id`
""");
    }

    public override async Task Can_query_all_animal_views(bool async)
    {
        await base.Can_query_all_animal_views(async);

        AssertSql();
    }

    public override async Task Discriminator_used_when_projection_over_derived_type(bool async)
    {
        await base.Discriminator_used_when_projection_over_derived_type(async);

        AssertSql();
    }

    public override async Task Discriminator_used_when_projection_over_derived_type2(bool async)
    {
        await base.Discriminator_used_when_projection_over_derived_type2(async);

        AssertSql();
    }

    public override async Task Discriminator_used_when_projection_over_of_type(bool async)
    {
        await base.Discriminator_used_when_projection_over_of_type(async);

        AssertSql();
    }

    public override async Task Discriminator_with_cast_in_shadow_property(bool async)
    {
        await base.Discriminator_with_cast_in_shadow_property(async);

        AssertSql();
    }

    public override void Using_from_sql_throws()
    {
        base.Using_from_sql_throws();

        AssertSql();
    }

    public override async Task Using_is_operator_on_multiple_type_with_no_result(bool async)
    {
        await base.Using_is_operator_on_multiple_type_with_no_result(async);

        AssertSql(
"""
SELECT `a`.`Id`, `a`.`CountryId`, `a`.`Name`, `a`.`Species`, `b`.`EagleId`, `b`.`IsFlightless`, `e`.`Group`, `k`.`FoundOn`, IIF(`k`.`Id` IS NOT NULL, 'Kiwi', IIF(`e`.`Id` IS NOT NULL, 'Eagle', NULL)) AS `Discriminator`
FROM ((`Animals` AS `a`
LEFT JOIN `Birds` AS `b` ON `a`.`Id` = `b`.`Id`)
LEFT JOIN `Eagle` AS `e` ON `a`.`Id` = `e`.`Id`)
LEFT JOIN `Kiwi` AS `k` ON `a`.`Id` = `k`.`Id`
WHERE `k`.`Id` IS NOT NULL AND `e`.`Id` IS NOT NULL
""");
    }

    public override async Task Using_is_operator_with_of_type_on_multiple_type_with_no_result(bool async)
    {
        await base.Using_is_operator_with_of_type_on_multiple_type_with_no_result(async);

        AssertSql(
"""
SELECT `a`.`Id`, `a`.`CountryId`, `a`.`Name`, `a`.`Species`, `b`.`EagleId`, `b`.`IsFlightless`, `e`.`Group`, IIF(`e`.`Id` IS NOT NULL, 'Eagle', NULL) AS `Discriminator`
FROM ((`Animals` AS `a`
LEFT JOIN `Birds` AS `b` ON `a`.`Id` = `b`.`Id`)
LEFT JOIN `Eagle` AS `e` ON `a`.`Id` = `e`.`Id`)
LEFT JOIN `Kiwi` AS `k` ON `a`.`Id` = `k`.`Id`
WHERE `k`.`Id` IS NOT NULL AND `e`.`Id` IS NOT NULL
""");
    }

    public override async Task Using_OfType_on_multiple_type_with_no_result(bool async)
    {
        await base.Using_OfType_on_multiple_type_with_no_result(async);

        AssertSql();
    }

    public override async Task GetType_in_hierarchy_in_abstract_base_type(bool async)
    {
        await base.GetType_in_hierarchy_in_abstract_base_type(async);

        AssertSql(
"""
SELECT `a`.`Id`, `a`.`CountryId`, `a`.`Name`, `a`.`Species`, `b`.`EagleId`, `b`.`IsFlightless`, `e`.`Group`, `k`.`FoundOn`, IIF(`k`.`Id` IS NOT NULL, 'Kiwi', IIF(`e`.`Id` IS NOT NULL, 'Eagle', NULL)) AS `Discriminator`
FROM ((`Animals` AS `a`
LEFT JOIN `Birds` AS `b` ON `a`.`Id` = `b`.`Id`)
LEFT JOIN `Eagle` AS `e` ON `a`.`Id` = `e`.`Id`)
LEFT JOIN `Kiwi` AS `k` ON `a`.`Id` = `k`.`Id`
WHERE 0 = 1
""");
    }

    public override async Task GetType_in_hierarchy_in_intermediate_type(bool async)
    {
        await base.GetType_in_hierarchy_in_intermediate_type(async);

        AssertSql(
"""
SELECT `a`.`Id`, `a`.`CountryId`, `a`.`Name`, `a`.`Species`, `b`.`EagleId`, `b`.`IsFlightless`, `e`.`Group`, `k`.`FoundOn`, IIF(`k`.`Id` IS NOT NULL, 'Kiwi', IIF(`e`.`Id` IS NOT NULL, 'Eagle', NULL)) AS `Discriminator`
FROM ((`Animals` AS `a`
LEFT JOIN `Birds` AS `b` ON `a`.`Id` = `b`.`Id`)
LEFT JOIN `Eagle` AS `e` ON `a`.`Id` = `e`.`Id`)
LEFT JOIN `Kiwi` AS `k` ON `a`.`Id` = `k`.`Id`
WHERE 0 = 1
""");
    }

    public override async Task GetType_in_hierarchy_in_leaf_type_with_sibling(bool async)
    {
        await base.GetType_in_hierarchy_in_leaf_type_with_sibling(async);

        AssertSql(
"""
SELECT `a`.`Id`, `a`.`CountryId`, `a`.`Name`, `a`.`Species`, `b`.`EagleId`, `b`.`IsFlightless`, `e`.`Group`, `k`.`FoundOn`, IIF(`k`.`Id` IS NOT NULL, 'Kiwi', IIF(`e`.`Id` IS NOT NULL, 'Eagle', NULL)) AS `Discriminator`
FROM ((`Animals` AS `a`
LEFT JOIN `Birds` AS `b` ON `a`.`Id` = `b`.`Id`)
LEFT JOIN `Eagle` AS `e` ON `a`.`Id` = `e`.`Id`)
LEFT JOIN `Kiwi` AS `k` ON `a`.`Id` = `k`.`Id`
WHERE `e`.`Id` IS NOT NULL
""");
    }

    public override async Task GetType_in_hierarchy_in_leaf_type_with_sibling2(bool async)
    {
        await base.GetType_in_hierarchy_in_leaf_type_with_sibling2(async);

        AssertSql(
"""
SELECT `a`.`Id`, `a`.`CountryId`, `a`.`Name`, `a`.`Species`, `b`.`EagleId`, `b`.`IsFlightless`, `e`.`Group`, `k`.`FoundOn`, IIF(`k`.`Id` IS NOT NULL, 'Kiwi', IIF(`e`.`Id` IS NOT NULL, 'Eagle', NULL)) AS `Discriminator`
FROM ((`Animals` AS `a`
LEFT JOIN `Birds` AS `b` ON `a`.`Id` = `b`.`Id`)
LEFT JOIN `Eagle` AS `e` ON `a`.`Id` = `e`.`Id`)
LEFT JOIN `Kiwi` AS `k` ON `a`.`Id` = `k`.`Id`
WHERE `k`.`Id` IS NOT NULL
""");
    }

    public override async Task GetType_in_hierarchy_in_leaf_type_with_sibling2_reverse(bool async)
    {
        await base.GetType_in_hierarchy_in_leaf_type_with_sibling2_reverse(async);

        AssertSql(
"""
SELECT `a`.`Id`, `a`.`CountryId`, `a`.`Name`, `a`.`Species`, `b`.`EagleId`, `b`.`IsFlightless`, `e`.`Group`, `k`.`FoundOn`, IIF(`k`.`Id` IS NOT NULL, 'Kiwi', IIF(`e`.`Id` IS NOT NULL, 'Eagle', NULL)) AS `Discriminator`
FROM ((`Animals` AS `a`
LEFT JOIN `Birds` AS `b` ON `a`.`Id` = `b`.`Id`)
LEFT JOIN `Eagle` AS `e` ON `a`.`Id` = `e`.`Id`)
LEFT JOIN `Kiwi` AS `k` ON `a`.`Id` = `k`.`Id`
WHERE `k`.`Id` IS NOT NULL
""");
    }

    public override async Task GetType_in_hierarchy_in_leaf_type_with_sibling2_not_equal(bool async)
    {
        await base.GetType_in_hierarchy_in_leaf_type_with_sibling2_not_equal(async);

        AssertSql(
"""
SELECT `a`.`Id`, `a`.`CountryId`, `a`.`Name`, `a`.`Species`, `b`.`EagleId`, `b`.`IsFlightless`, `e`.`Group`, `k`.`FoundOn`, IIF(`k`.`Id` IS NOT NULL, 'Kiwi', IIF(`e`.`Id` IS NOT NULL, 'Eagle', NULL)) AS `Discriminator`
FROM ((`Animals` AS `a`
LEFT JOIN `Birds` AS `b` ON `a`.`Id` = `b`.`Id`)
LEFT JOIN `Eagle` AS `e` ON `a`.`Id` = `e`.`Id`)
LEFT JOIN `Kiwi` AS `k` ON `a`.`Id` = `k`.`Id`
WHERE `k`.`Id` IS NULL
""");
    }

    private void AssertSql(params string[] expected)
        => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);
}
