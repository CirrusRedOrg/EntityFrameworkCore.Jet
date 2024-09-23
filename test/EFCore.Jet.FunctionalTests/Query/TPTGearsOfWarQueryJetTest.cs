// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using System.Linq;
using System.Threading.Tasks;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Xunit;
using Xunit.Abstractions;
using Microsoft.EntityFrameworkCore.TestModels.GearsOfWarModel;
using System;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query;

public class TPTGearsOfWarQueryJetTest : TPTGearsOfWarQueryRelationalTestBase<TPTGearsOfWarQueryJetFixture>
{
#pragma warning disable IDE0060 // Remove unused parameter
    public TPTGearsOfWarQueryJetTest(TPTGearsOfWarQueryJetFixture fixture, ITestOutputHelper testOutputHelper)
#pragma warning restore IDE0060 // Remove unused parameter
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    [ConditionalFact]
    public virtual void Check_all_tests_overridden()
        => TestHelpers.AssertAllMethodsOverridden(GetType());

    public override async Task Non_string_concat_uses_appropriate_type_mapping(bool async)
    {
        await base.Non_string_concat_uses_appropriate_type_mapping(async);

        AssertSql(
            """
SELECT `m`.`Duration`
FROM `Missions` AS `m`
""");
    }

    public override async Task Entity_equality_empty(bool async)
    {
        await base.Entity_equality_empty(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE 0 = 1
""");
    }

    public override async Task Include_multiple_one_to_one_and_one_to_many(bool async)
    {
        await base.Include_multiple_one_to_one_and_one_to_many(async);

        AssertSql(
            """
SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`IssueDate`, `t`.`Note`, `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM (`Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`)
LEFT JOIN `Weapons` AS `w` ON `s`.`FullName` = `w`.`OwnerFullName`
ORDER BY `t`.`Id`, `s`.`Nickname`, `s`.`SquadId`
""");
    }

    public override async Task Include_multiple_one_to_one_optional_and_one_to_one_required(bool async)
    {
        await base.Include_multiple_one_to_one_optional_and_one_to_one_required(async);

        AssertSql(
            """
SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`IssueDate`, `t`.`Note`, `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`, `s0`.`Id`, `s0`.`Banner`, `s0`.`Banner5`, `s0`.`InternalNumber`, `s0`.`Name`
FROM (`Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`)
LEFT JOIN `Squads` AS `s0` ON `s`.`SquadId` = `s0`.`Id`
""");
    }

    public override async Task Include_multiple_circular(bool async)
    {
        await base.Include_multiple_circular(async);

        AssertSql(
            """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `c`.`Name`, `c`.`Location`, `c`.`Nation`, `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`
FROM ((`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
INNER JOIN `Cities` AS `c` ON `g`.`CityOfBirthName` = `c`.`Name`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g0`
    LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`
) AS `s` ON `c`.`Name` = `s`.`AssignedCityName`
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `c`.`Name`, `s`.`Nickname`
""");
    }

    public override async Task Include_multiple_circular_with_filter(bool async)
    {
        await base.Include_multiple_circular_with_filter(async);

        AssertSql(
            """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `c`.`Name`, `c`.`Location`, `c`.`Nation`, `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`
FROM ((`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
INNER JOIN `Cities` AS `c` ON `g`.`CityOfBirthName` = `c`.`Name`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g0`
    LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`
) AS `s` ON `c`.`Name` = `s`.`AssignedCityName`
WHERE `g`.`Nickname` = 'Marcus'
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `c`.`Name`, `s`.`Nickname`
""");
    }

    public override async Task Include_using_alternate_key(bool async)
    {
        await base.Include_using_alternate_key(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM (`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN `Weapons` AS `w` ON `g`.`FullName` = `w`.`OwnerFullName`
WHERE `g`.`Nickname` = 'Marcus'
ORDER BY `g`.`Nickname`, `g`.`SquadId`
""");
    }

    public override async Task Include_navigation_on_derived_type(bool async)
    {
        await base.Include_navigation_on_derived_type(async);

        AssertSql(
            """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`
FROM (`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g0`
    LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`
) AS `s` ON `g`.`Nickname` = `s`.`LeaderNickname` AND `g`.`SquadId` = `s`.`LeaderSquadId`
WHERE `o`.`Nickname` IS NOT NULL
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `s`.`Nickname`
""");
    }

    public override async Task String_based_Include_navigation_on_derived_type(bool async)
    {
        await base.String_based_Include_navigation_on_derived_type(async);

        AssertSql(
            """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`
FROM (`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g0`
    LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`
) AS `s` ON `g`.`Nickname` = `s`.`LeaderNickname` AND `g`.`SquadId` = `s`.`LeaderSquadId`
WHERE `o`.`Nickname` IS NOT NULL
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `s`.`Nickname`
""");
    }

    public override async Task Select_Where_Navigation_Included(bool async)
    {
        await base.Select_Where_Navigation_Included(async);

        AssertSql(
            """
SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`IssueDate`, `t`.`Note`, `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
WHERE `s`.`Nickname` = 'Marcus'
""");
    }

    public override async Task Include_with_join_reference1(bool async)
    {
        await base.Include_with_join_reference1(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `c`.`Name`, `c`.`Location`, `c`.`Nation`
FROM ((`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
INNER JOIN `Tags` AS `t` ON `g`.`SquadId` = `t`.`GearSquadId` AND `g`.`Nickname` = `t`.`GearNickName`)
INNER JOIN `Cities` AS `c` ON `g`.`CityOfBirthName` = `c`.`Name`
""");
    }

    public override async Task Include_with_join_reference2(bool async)
    {
        await base.Include_with_join_reference2(async);

        AssertSql(
            """
SELECT `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`, `c`.`Name`, `c`.`Location`, `c`.`Nation`
FROM (`Tags` AS `t`
INNER JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
) AS `s` ON `t`.`GearSquadId` = `s`.`SquadId` AND `t`.`GearNickName` = `s`.`Nickname`)
LEFT JOIN `Cities` AS `c` ON `s`.`CityOfBirthName` = `c`.`Name`
WHERE `s`.`CityOfBirthName` IS NOT NULL AND `c`.`Name` IS NOT NULL
""");
    }

    public override async Task Include_with_join_collection1(bool async)
    {
        await base.Include_with_join_collection1(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `t`.`Id`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM ((`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
INNER JOIN `Tags` AS `t` ON `g`.`SquadId` = `t`.`GearSquadId` AND `g`.`Nickname` = `t`.`GearNickName`)
LEFT JOIN `Weapons` AS `w` ON `g`.`FullName` = `w`.`OwnerFullName`
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `t`.`Id`
""");
    }

    public override async Task Include_with_join_collection2(bool async)
    {
        await base.Include_with_join_collection2(async);

        AssertSql(
            """
SELECT `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`, `t`.`Id`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM (`Tags` AS `t`
INNER JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
) AS `s` ON `t`.`GearSquadId` = `s`.`SquadId` AND `t`.`GearNickName` = `s`.`Nickname`)
LEFT JOIN `Weapons` AS `w` ON `s`.`FullName` = `w`.`OwnerFullName`
ORDER BY `t`.`Id`, `s`.`Nickname`, `s`.`SquadId`
""");
    }

    public override async Task Include_where_list_contains_navigation(bool async)
    {
        await base.Include_where_list_contains_navigation(async);

        AssertSql(
"""
SELECT `t`.`Id`
FROM `Tags` AS `t`
""",
//
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`IssueDate`, `t`.`Note`
FROM (`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN `Tags` AS `t` ON `g`.`Nickname` = `t`.`GearNickName` AND `g`.`SquadId` = `t`.`GearSquadId`
WHERE `t`.`Id` IS NOT NULL AND `t`.`Id` IN ('{b39a6fba-9026-4d69-828e-fd7068673e57}', '{34c8d86e-a4ac-4be5-827f-584dda348a07}', '{70534e05-782c-4052-8720-c2c54481ce5f}', '{a8ad98f9-e023-4e2a-9a70-c2728455bd34}', '{df36f493-463f-4123-83f9-6b135deeb7ba}', '{a7be028a-0cf2-448f-ab55-ce8bc5d8cf69}')
""");
    }

    public override async Task Include_where_list_contains_navigation2(bool async)
    {
        await base.Include_where_list_contains_navigation2(async);

        AssertSql(
"""
SELECT `t`.`Id`
FROM `Tags` AS `t`
""",
//
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`IssueDate`, `t`.`Note`
FROM ((`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
INNER JOIN `Cities` AS `c` ON `g`.`CityOfBirthName` = `c`.`Name`)
LEFT JOIN `Tags` AS `t` ON `g`.`Nickname` = `t`.`GearNickName` AND `g`.`SquadId` = `t`.`GearSquadId`
WHERE `c`.`Location` IS NOT NULL AND `t`.`Id` IN ('{b39a6fba-9026-4d69-828e-fd7068673e57}', '{34c8d86e-a4ac-4be5-827f-584dda348a07}', '{70534e05-782c-4052-8720-c2c54481ce5f}', '{a8ad98f9-e023-4e2a-9a70-c2728455bd34}', '{df36f493-463f-4123-83f9-6b135deeb7ba}', '{a7be028a-0cf2-448f-ab55-ce8bc5d8cf69}')
""");
    }

    public override async Task Navigation_accessed_twice_outside_and_inside_subquery(bool async)
    {
        await base.Navigation_accessed_twice_outside_and_inside_subquery(async);

        AssertSql(
"""
SELECT `t`.`Id`
FROM `Tags` AS `t`
""",
//
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM (`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN `Tags` AS `t` ON `g`.`Nickname` = `t`.`GearNickName` AND `g`.`SquadId` = `t`.`GearSquadId`
WHERE `t`.`Id` IS NOT NULL AND `t`.`Id` IN ('{b39a6fba-9026-4d69-828e-fd7068673e57}', '{34c8d86e-a4ac-4be5-827f-584dda348a07}', '{70534e05-782c-4052-8720-c2c54481ce5f}', '{a8ad98f9-e023-4e2a-9a70-c2728455bd34}', '{df36f493-463f-4123-83f9-6b135deeb7ba}', '{a7be028a-0cf2-448f-ab55-ce8bc5d8cf69}')
""");
    }

    public override async Task Include_with_join_multi_level(bool async)
    {
        await base.Include_with_join_multi_level(async);

        AssertSql(
            """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `c`.`Name`, `c`.`Location`, `c`.`Nation`, `t`.`Id`, `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`
FROM (((`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
INNER JOIN `Tags` AS `t` ON `g`.`SquadId` = `t`.`GearSquadId` AND `g`.`Nickname` = `t`.`GearNickName`)
INNER JOIN `Cities` AS `c` ON `g`.`CityOfBirthName` = `c`.`Name`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g0`
    LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`
) AS `s` ON `c`.`Name` = `s`.`AssignedCityName`
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `t`.`Id`, `c`.`Name`, `s`.`Nickname`
""");
    }

    public override async Task Include_with_join_and_inheritance1(bool async)
    {
        await base.Include_with_join_and_inheritance1(async);

        AssertSql(
            """
SELECT `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`, `c`.`Name`, `c`.`Location`, `c`.`Nation`
FROM (`Tags` AS `t`
INNER JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
    WHERE `o`.`Nickname` IS NOT NULL
) AS `s` ON `t`.`GearSquadId` = `s`.`SquadId` AND `t`.`GearNickName` = `s`.`Nickname`)
LEFT JOIN `Cities` AS `c` ON `s`.`CityOfBirthName` = `c`.`Name`
WHERE `s`.`CityOfBirthName` IS NOT NULL AND `c`.`Name` IS NOT NULL
""");
    }

    public override async Task Include_with_join_and_inheritance_with_orderby_before_and_after_include(bool async)
    {
        await base.Include_with_join_and_inheritance_with_orderby_before_and_after_include(async);

        AssertSql(
            """
SELECT `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`, `t`.`Id`, `s0`.`Nickname`, `s0`.`SquadId`, `s0`.`AssignedCityName`, `s0`.`CityOfBirthName`, `s0`.`FullName`, `s0`.`HasSoulPatch`, `s0`.`LeaderNickname`, `s0`.`LeaderSquadId`, `s0`.`Rank`, `s0`.`Discriminator`
FROM (`Tags` AS `t`
INNER JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
    WHERE `o`.`Nickname` IS NOT NULL
) AS `s` ON `t`.`GearSquadId` = `s`.`SquadId` AND `t`.`GearNickName` = `s`.`Nickname`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g0`
    LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`
) AS `s0` ON `s`.`Nickname` = `s0`.`LeaderNickname` AND `s`.`SquadId` = `s0`.`LeaderSquadId`
ORDER BY NOT (`s`.`HasSoulPatch`), `s`.`Nickname` DESC, `t`.`Id`, `s`.`SquadId`, `s0`.`Nickname`
""");
    }

    public override async Task Include_with_join_and_inheritance2(bool async)
    {
        await base.Include_with_join_and_inheritance2(async);

        AssertSql(
        """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `t`.`Id`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM ((`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
INNER JOIN `Tags` AS `t` ON `g`.`SquadId` = `t`.`GearSquadId` AND `g`.`Nickname` = `t`.`GearNickName`)
LEFT JOIN `Weapons` AS `w` ON `g`.`FullName` = `w`.`OwnerFullName`
WHERE `o`.`Nickname` IS NOT NULL
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `t`.`Id`
""");
    }

    public override async Task Include_with_join_and_inheritance3(bool async)
    {
        await base.Include_with_join_and_inheritance3(async);

        AssertSql(
            """
SELECT `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`, `t`.`Id`, `s0`.`Nickname`, `s0`.`SquadId`, `s0`.`AssignedCityName`, `s0`.`CityOfBirthName`, `s0`.`FullName`, `s0`.`HasSoulPatch`, `s0`.`LeaderNickname`, `s0`.`LeaderSquadId`, `s0`.`Rank`, `s0`.`Discriminator`
FROM (`Tags` AS `t`
INNER JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
    WHERE `o`.`Nickname` IS NOT NULL
) AS `s` ON `t`.`GearSquadId` = `s`.`SquadId` AND `t`.`GearNickName` = `s`.`Nickname`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g0`
    LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`
) AS `s0` ON `s`.`Nickname` = `s0`.`LeaderNickname` AND `s`.`SquadId` = `s0`.`LeaderSquadId`
ORDER BY `t`.`Id`, `s`.`Nickname`, `s`.`SquadId`, `s0`.`Nickname`
""");
    }

    public override async Task Include_with_nested_navigation_in_order_by(bool async)
    {
        await base.Include_with_nested_navigation_in_order_by(async);

        AssertSql(
            """
SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`, `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`
FROM (`Weapons` AS `w`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
) AS `s` ON `w`.`OwnerFullName` = `s`.`FullName`)
LEFT JOIN `Cities` AS `c` ON `s`.`CityOfBirthName` = `c`.`Name`
WHERE `s`.`Nickname` <> 'Paduk' OR `s`.`Nickname` IS NULL
ORDER BY `c`.`Name`, `w`.`Id`
""");
    }

    public override async Task Where_enum(bool async)
    {
        await base.Where_enum(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE `g`.`Rank` = 4
""");
    }

    public override async Task Where_nullable_enum_with_constant(bool async)
    {
        await base.Where_nullable_enum_with_constant(async);

        AssertSql(
"""
SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE `w`.`AmmunitionType` = 1
""");
    }

    public override async Task Where_nullable_enum_with_null_constant(bool async)
    {
        await base.Where_nullable_enum_with_null_constant(async);

        AssertSql(
"""
SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE `w`.`AmmunitionType` IS NULL
""");
    }

    public override async Task Where_nullable_enum_with_non_nullable_parameter(bool async)
    {
        await base.Where_nullable_enum_with_non_nullable_parameter(async);

        AssertSql(
"""
@__ammunitionType_0='1'

SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE `w`.`AmmunitionType` = @__ammunitionType_0
""");
    }

    public override async Task Where_nullable_enum_with_nullable_parameter(bool async)
    {
        await base.Where_nullable_enum_with_nullable_parameter(async);

        AssertSql(
"""
@__ammunitionType_0='1' (Nullable = true)

SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE `w`.`AmmunitionType` = @__ammunitionType_0
""",
//
"""
SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE `w`.`AmmunitionType` IS NULL
""");
    }

    public override async Task Where_bitwise_and_enum(bool async)
    {
        await base.Where_bitwise_and_enum(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE (`g`.`Rank` BAND 2) > 0
""",
//
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE (`g`.`Rank` BAND 2) = 2
""");
    }

    public override async Task Where_bitwise_and_integral(bool async)
    {
        await base.Where_bitwise_and_integral(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE (`g`.`Rank` BAND 1) = 1
""",
//
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE (CLNG(`g`.`Rank`) BAND 1) = 1
""",
//
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE (CINT(`g`.`Rank`) BAND 1) = 1
""");
    }

    public override async Task Where_bitwise_and_nullable_enum_with_constant(bool async)
    {
        await base.Where_bitwise_and_nullable_enum_with_constant(async);

        AssertSql(
"""
SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE (`w`.`AmmunitionType` BAND 1) > 0
""");
    }

    public override async Task Where_bitwise_and_nullable_enum_with_null_constant(bool async)
    {
        await base.Where_bitwise_and_nullable_enum_with_null_constant(async);

        AssertSql(
"""
SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE (`w`.`AmmunitionType` BAND NULL) > 0
""");
    }

    public override async Task Where_bitwise_and_nullable_enum_with_non_nullable_parameter(bool async)
    {
        await base.Where_bitwise_and_nullable_enum_with_non_nullable_parameter(async);

        AssertSql(
"""
@__ammunitionType_0='1'

SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE (`w`.`AmmunitionType` BAND @__ammunitionType_0) > 0
""");
    }

    public override async Task Where_bitwise_and_nullable_enum_with_nullable_parameter(bool async)
    {
        await base.Where_bitwise_and_nullable_enum_with_nullable_parameter(async);

        AssertSql(
"""
@__ammunitionType_0='1' (Nullable = true)

SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE (`w`.`AmmunitionType` BAND @__ammunitionType_0) > 0
""",
//
"""
SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE (`w`.`AmmunitionType` BAND NULL) > 0
""");
    }

    public override async Task Where_bitwise_or_enum(bool async)
    {
        await base.Where_bitwise_or_enum(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE (`g`.`Rank` BOR 2) > 0
""");
    }

    public override async Task Bitwise_projects_values_in_select(bool async)
    {
        await base.Bitwise_projects_values_in_select(async);

        AssertSql(
            """
SELECT TOP 1 CBOOL((`g`.`Rank` BAND 2) BXOR 2) BXOR TRUE AS `BitwiseTrue`, CBOOL((`g`.`Rank` BAND 2) BXOR 4) BXOR TRUE AS `BitwiseFalse`, `g`.`Rank` BAND 2 AS `BitwiseValue`
FROM `Gears` AS `g`
WHERE (`g`.`Rank` BAND 2) = 2
""");
    }

    public override async Task Where_enum_has_flag(bool async)
    {
        await base.Where_enum_has_flag(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE (`g`.`Rank` BAND 2) = 2
""",
//
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE (`g`.`Rank` BAND 18) = 18
""",
//
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE (`g`.`Rank` BAND 1) = 1
""",
//
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE (`g`.`Rank` BAND 1) = 1
""",
//
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE (2 BAND `g`.`Rank`) = `g`.`Rank`
""");
    }

    public override async Task Where_enum_has_flag_subquery(bool async)
    {
        await base.Where_enum_has_flag_subquery(async);

        AssertSql(
            """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE (`g`.`Rank` BAND IIF((
        SELECT TOP 1 `g0`.`Rank`
        FROM `Gears` AS `g0`
        ORDER BY `g0`.`Nickname`, `g0`.`SquadId`) IS NULL, 0, (
        SELECT TOP 1 `g0`.`Rank`
        FROM `Gears` AS `g0`
        ORDER BY `g0`.`Nickname`, `g0`.`SquadId`))) = IIF((
        SELECT TOP 1 `g0`.`Rank`
        FROM `Gears` AS `g0`
        ORDER BY `g0`.`Nickname`, `g0`.`SquadId`) IS NULL, 0, (
        SELECT TOP 1 `g0`.`Rank`
        FROM `Gears` AS `g0`
        ORDER BY `g0`.`Nickname`, `g0`.`SquadId`))
""",
            //
            """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE (2 BAND IIF((
        SELECT TOP 1 `g0`.`Rank`
        FROM `Gears` AS `g0`
        ORDER BY `g0`.`Nickname`, `g0`.`SquadId`) IS NULL, 0, (
        SELECT TOP 1 `g0`.`Rank`
        FROM `Gears` AS `g0`
        ORDER BY `g0`.`Nickname`, `g0`.`SquadId`))) = IIF((
        SELECT TOP 1 `g0`.`Rank`
        FROM `Gears` AS `g0`
        ORDER BY `g0`.`Nickname`, `g0`.`SquadId`) IS NULL, 0, (
        SELECT TOP 1 `g0`.`Rank`
        FROM `Gears` AS `g0`
        ORDER BY `g0`.`Nickname`, `g0`.`SquadId`))
""");
    }

    public override async Task Where_enum_has_flag_subquery_with_pushdown(bool async)
    {
        await base.Where_enum_has_flag_subquery_with_pushdown(async);

        AssertSql(
            """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE (`g`.`Rank` BAND (
    SELECT TOP 1 `g0`.`Rank`
    FROM `Gears` AS `g0`
    ORDER BY `g0`.`Nickname`, `g0`.`SquadId`)) = (
    SELECT TOP 1 `g0`.`Rank`
    FROM `Gears` AS `g0`
    ORDER BY `g0`.`Nickname`, `g0`.`SquadId`) OR (
    SELECT TOP 1 `g0`.`Rank`
    FROM `Gears` AS `g0`
    ORDER BY `g0`.`Nickname`, `g0`.`SquadId`) IS NULL
""",
            //
            """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE (2 BAND (
    SELECT TOP 1 `g0`.`Rank`
    FROM `Gears` AS `g0`
    ORDER BY `g0`.`Nickname`, `g0`.`SquadId`)) = (
    SELECT TOP 1 `g0`.`Rank`
    FROM `Gears` AS `g0`
    ORDER BY `g0`.`Nickname`, `g0`.`SquadId`) OR (
    SELECT TOP 1 `g0`.`Rank`
    FROM `Gears` AS `g0`
    ORDER BY `g0`.`Nickname`, `g0`.`SquadId`) IS NULL
""");
    }

    public override async Task Where_enum_has_flag_subquery_client_eval(bool async)
    {
        await base.Where_enum_has_flag_subquery_client_eval(async);

        AssertSql(
            """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE (`g`.`Rank` BAND (
    SELECT TOP 1 `g0`.`Rank`
    FROM `Gears` AS `g0`
    ORDER BY `g0`.`Nickname`, `g0`.`SquadId`)) = (
    SELECT TOP 1 `g0`.`Rank`
    FROM `Gears` AS `g0`
    ORDER BY `g0`.`Nickname`, `g0`.`SquadId`) OR (
    SELECT TOP 1 `g0`.`Rank`
    FROM `Gears` AS `g0`
    ORDER BY `g0`.`Nickname`, `g0`.`SquadId`) IS NULL
""");
    }

    public override async Task Where_enum_has_flag_with_non_nullable_parameter(bool async)
    {
        await base.Where_enum_has_flag_with_non_nullable_parameter(async);

        AssertSql(
"""
@__parameter_0='2'
@__parameter_0='2'

SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE (`g`.`Rank` BAND @__parameter_0) = @__parameter_0
""");
    }

    public override async Task Where_has_flag_with_nullable_parameter(bool async)
    {
        await base.Where_has_flag_with_nullable_parameter(async);

        AssertSql(
        """
@__parameter_0='2' (Nullable = true)
@__parameter_0='2' (Nullable = true)

SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE (`g`.`Rank` BAND @__parameter_0) = @__parameter_0
""");
    }

    public override async Task Select_enum_has_flag(bool async)
    {
        await base.Select_enum_has_flag(async);

        AssertSql(
            """
SELECT TOP 1 CBOOL((`g`.`Rank` BAND 2) BXOR 2) BXOR TRUE AS `hasFlagTrue`, CBOOL((`g`.`Rank` BAND 4) BXOR 4) BXOR TRUE AS `hasFlagFalse`
FROM `Gears` AS `g`
WHERE (`g`.`Rank` BAND 2) = 2
""");
    }

    public override async Task Where_count_subquery_without_collision(bool async)
    {
        await base.Where_count_subquery_without_collision(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE (
    SELECT COUNT(*)
    FROM `Weapons` AS `w`
    WHERE `g`.`FullName` = `w`.`OwnerFullName`) = 2
""");
    }

    public override async Task Where_any_subquery_without_collision(bool async)
    {
        await base.Where_any_subquery_without_collision(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE EXISTS (
    SELECT 1
    FROM `Weapons` AS `w`
    WHERE `g`.`FullName` = `w`.`OwnerFullName`)
""");
    }

    public override async Task Select_inverted_boolean(bool async)
    {
        await base.Select_inverted_boolean(async);

        AssertSql(
            """
SELECT `w`.`Id`, `w`.`IsAutomatic` BXOR TRUE AS `Manual`
FROM `Weapons` AS `w`
WHERE `w`.`IsAutomatic` = TRUE
""");
    }

    public override async Task Select_inverted_nullable_boolean(bool async)
    {
        await base.Select_inverted_nullable_boolean(async);

        AssertSql(
            """
SELECT `f`.`Id`, `l`.`Eradicated` BXOR TRUE AS `Alive`
FROM `Factions` AS `f`
INNER JOIN `LocustHordes` AS `l` ON `f`.`Id` = `l`.`Id`
""");
    }

    public override async Task Select_comparison_with_null(bool async)
    {
        await base.Select_comparison_with_null(async);

        AssertSql(
"""
@__ammunitionType_0='1' (Nullable = true)
@__ammunitionType_0='1' (Nullable = true)

SELECT `w`.`Id`, IIF(`w`.`AmmunitionType` = @__ammunitionType_0 AND `w`.`AmmunitionType` IS NOT NULL, TRUE, FALSE) AS `Cartridge`
FROM `Weapons` AS `w`
WHERE `w`.`AmmunitionType` = @__ammunitionType_0
""",
//
"""
SELECT `w`.`Id`, IIF(`w`.`AmmunitionType` IS NULL, TRUE, FALSE) AS `Cartridge`
FROM `Weapons` AS `w`
WHERE `w`.`AmmunitionType` IS NULL
""");
    }

    public override async Task Select_null_parameter(bool async)
    {
        await base.Select_null_parameter(async);

        AssertSql(
            """
@__ammunitionType_0='1' (Nullable = true)

SELECT `w`.`Id`, @__ammunitionType_0 AS `AmmoType`
FROM `Weapons` AS `w`
""",
            //
            """
SELECT `w`.`Id`, CVar(NULL) AS `AmmoType`
FROM `Weapons` AS `w`
""",
            //
            """
@__ammunitionType_0='2' (Nullable = true)

SELECT `w`.`Id`, @__ammunitionType_0 AS `AmmoType`
FROM `Weapons` AS `w`
""",
            //
            """
SELECT `w`.`Id`, CVar(NULL) AS `AmmoType`
FROM `Weapons` AS `w`
""");
    }

    public override async Task Select_ternary_operation_with_boolean(bool async)
    {
        await base.Select_ternary_operation_with_boolean(async);

        AssertSql(
"""
SELECT `w`.`Id`, IIF(`w`.`IsAutomatic` = TRUE, 1, 0) AS `Num`
FROM `Weapons` AS `w`
""");
    }

    public override async Task Select_ternary_operation_with_inverted_boolean(bool async)
    {
        await base.Select_ternary_operation_with_inverted_boolean(async);

        AssertSql(
            """
SELECT `w`.`Id`, IIF(`w`.`IsAutomatic` = FALSE, 1, 0) AS `Num`
FROM `Weapons` AS `w`
""");
    }

    public override async Task Select_ternary_operation_with_has_value_not_null(bool async)
    {
        await base.Select_ternary_operation_with_has_value_not_null(async);

        AssertSql(
"""
SELECT `w`.`Id`, IIF(`w`.`AmmunitionType` IS NOT NULL AND `w`.`AmmunitionType` = 1, 'Yes', 'No') AS `IsCartridge`
FROM `Weapons` AS `w`
WHERE `w`.`AmmunitionType` IS NOT NULL AND `w`.`AmmunitionType` = 1
""");
    }

    public override async Task Select_ternary_operation_multiple_conditions(bool async)
    {
        await base.Select_ternary_operation_multiple_conditions(async);

        AssertSql(
"""
SELECT `w`.`Id`, IIF(`w`.`AmmunitionType` = 2 AND `w`.`SynergyWithId` = 1, 'Yes', 'No') AS `IsCartridge`
FROM `Weapons` AS `w`
""");
    }

    public override async Task Select_ternary_operation_multiple_conditions_2(bool async)
    {
        await base.Select_ternary_operation_multiple_conditions_2(async);

        AssertSql(
            """
SELECT `w`.`Id`, IIF(`w`.`IsAutomatic` = FALSE AND `w`.`SynergyWithId` = 1, 'Yes', 'No') AS `IsCartridge`
FROM `Weapons` AS `w`
""");
    }

    public override async Task Select_multiple_conditions(bool async)
    {
        await base.Select_multiple_conditions(async);

        AssertSql(
            """
SELECT `w`.`Id`, IIF(`w`.`IsAutomatic` = FALSE AND `w`.`SynergyWithId` = 1 AND `w`.`SynergyWithId` IS NOT NULL, TRUE, FALSE) AS `IsCartridge`
FROM `Weapons` AS `w`
""");
    }

    public override async Task Select_nested_ternary_operations(bool async)
    {
        await base.Select_nested_ternary_operations(async);

        AssertSql(
            """
SELECT `w`.`Id`, IIF(`w`.`IsAutomatic` = FALSE, IIF(`w`.`AmmunitionType` = 1, 'ManualCartridge', 'Manual'), 'Auto') AS `IsManualCartridge`
FROM `Weapons` AS `w`
""");
    }

    public override async Task Null_propagation_optimization1(bool async)
    {
        await base.Null_propagation_optimization1(async);

        AssertSql(
            """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE `g`.`LeaderNickname` = 'Marcus'
""");
    }

    public override async Task Null_propagation_optimization2(bool async)
    {
        await base.Null_propagation_optimization2(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE IIF(`g`.`LeaderNickname` IS NULL, NULL, IIF(`g`.`LeaderNickname` LIKE '%us', TRUE, FALSE)) = TRUE
""");
    }

    public override async Task Null_propagation_optimization3(bool async)
    {
        await base.Null_propagation_optimization3(async);

        AssertSql(
        """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE IIF(`g`.`LeaderNickname` IS NOT NULL, IIF(`g`.`LeaderNickname` LIKE '%us', TRUE, FALSE), NULL) = TRUE
""");
    }

    public override async Task Null_propagation_optimization4(bool async)
    {
        await base.Null_propagation_optimization4(async);

        // issue #16050
        AssertSql(
            """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE IIF(`g`.`LeaderNickname` IS NULL, NULL, IIF(LEN(`g`.`LeaderNickname`) IS NULL, NULL, CLNG(LEN(`g`.`LeaderNickname`)))) = 5
""");
    }

    public override async Task Null_propagation_optimization5(bool async)
    {
        await base.Null_propagation_optimization5(async);

        AssertSql(
            """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE IIF(`g`.`LeaderNickname` IS NOT NULL, IIF(LEN(`g`.`LeaderNickname`) IS NULL, NULL, CLNG(LEN(`g`.`LeaderNickname`))), NULL) = 5
""");
    }

    public override async Task Null_propagation_optimization6(bool async)
    {
        await base.Null_propagation_optimization6(async);

        AssertSql(
            """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE IIF(`g`.`LeaderNickname` IS NOT NULL, IIF(LEN(`g`.`LeaderNickname`) IS NULL, NULL, CLNG(LEN(`g`.`LeaderNickname`))), NULL) = 5
""");
    }

    public override async Task Select_null_propagation_optimization7(bool async)
    {
        await base.Select_null_propagation_optimization7(async);

        AssertSql(
"""
SELECT IIF(`g`.`LeaderNickname` IS NOT NULL, `g`.`LeaderNickname` & `g`.`LeaderNickname`, NULL)
FROM `Gears` AS `g`
""");
    }

    public override async Task Select_null_propagation_optimization8(bool async)
    {
        await base.Select_null_propagation_optimization8(async);

        AssertSql(
            """
SELECT IIF(`g`.`LeaderNickname` IS NULL, '', `g`.`LeaderNickname`) & IIF(`g`.`LeaderNickname` IS NULL, '', `g`.`LeaderNickname`)
FROM `Gears` AS `g`
""");
    }

    public override async Task Select_null_propagation_optimization9(bool async)
    {
        await base.Select_null_propagation_optimization9(async);

        AssertSql(
            """
SELECT IIF(LEN(`g`.`FullName`) IS NULL, NULL, CLNG(LEN(`g`.`FullName`)))
FROM `Gears` AS `g`
""");
    }

    public override async Task Select_null_propagation_negative1(bool async)
    {
        await base.Select_null_propagation_negative1(async);

        AssertSql(
            """
SELECT IIF(`g`.`LeaderNickname` IS NOT NULL, CBOOL(IIF(LEN(`g`.`Nickname`) IS NULL, NULL, CLNG(LEN(`g`.`Nickname`))) BXOR 5) BXOR TRUE, NULL)
FROM `Gears` AS `g`
""");
    }

    public override async Task Select_null_propagation_negative2(bool async)
    {
        await base.Select_null_propagation_negative2(async);

        AssertSql(
            """
SELECT IIF(`g`.`LeaderNickname` IS NOT NULL, `s`.`LeaderNickname`, NULL)
FROM `Gears` AS `g`,
(
    SELECT `g0`.`LeaderNickname`
    FROM `Gears` AS `g0`
) AS `s`
""");
    }

    public override async Task Select_null_propagation_negative3(bool async)
    {
        await base.Select_null_propagation_negative3(async);

        AssertSql(
"""
SELECT [t].[Nickname], CASE
    WHEN ([t].[Nickname] IS NOT NULL) AND ([t].[SquadId] IS NOT NULL) THEN CASE
        WHEN [t].[LeaderNickname] IS NOT NULL THEN CAST(1 AS bit)
        ELSE CAST(0 AS bit)
    END
    ELSE NULL
END AS [Condition]
FROM [Gears] AS [g]
LEFT JOIN (
    SELECT [g0].[Nickname], [g0].[SquadId], [g0].[LeaderNickname]
    FROM [Gears] AS [g0]
) AS [t] ON [g].[HasSoulPatch] = CAST(1 AS bit)
ORDER BY [t].[Nickname]
""");
    }

    public override async Task Select_null_propagation_negative4(bool async)
    {
        await base.Select_null_propagation_negative4(async);

        AssertSql(
"""
SELECT CASE
    WHEN ([t].[Nickname] IS NOT NULL) AND ([t].[SquadId] IS NOT NULL) THEN CAST(1 AS bit)
    ELSE CAST(0 AS bit)
END, [t].[Nickname]
FROM [Gears] AS [g]
LEFT JOIN (
    SELECT [g0].[Nickname], [g0].[SquadId]
    FROM [Gears] AS [g0]
) AS [t] ON [g].[HasSoulPatch] = CAST(1 AS bit)
ORDER BY [t].[Nickname]
""");
    }

    public override async Task Select_null_propagation_negative5(bool async)
    {
        await base.Select_null_propagation_negative5(async);

        AssertSql(
"""
SELECT IIF(`g`.`LeaderNickname` IS NOT NULL, IIF(IIF(LEN(`g`.`LeaderNickname`) IS NULL, NULL, CLNG(LEN(`g`.`LeaderNickname`))) <> IIF(LEN(`g`.`LeaderNickname`) IS NULL, NULL, CLNG(LEN(`g`.`LeaderNickname`))), TRUE, FALSE), NULL)
FROM `Gears` AS `g`
""");
    }

    public override async Task Select_null_propagation_negative6(bool async)
    {
        await base.Select_null_propagation_negative6(async);

        AssertSql(
            """
SELECT IIF(`g`.`LeaderNickname` IS NOT NULL, FALSE, NULL)
FROM `Gears` AS `g`
""");
    }

    public override async Task Select_null_propagation_negative7(bool async)
    {
        await base.Select_null_propagation_negative7(async);

        AssertSql(
"""
SELECT IIF(`g`.`LeaderNickname` IS NOT NULL, TRUE, NULL)
FROM `Gears` AS `g`
""");
    }

    public override async Task Select_null_propagation_negative8(bool async)
    {
        await base.Select_null_propagation_negative8(async);

        AssertSql(
            """
SELECT IIF(`s0`.`Id` IS NOT NULL, `c`.`Name`, NULL)
FROM ((`Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`)
LEFT JOIN `Squads` AS `s0` ON `s`.`SquadId` = `s0`.`Id`)
LEFT JOIN `Cities` AS `c` ON `s`.`AssignedCityName` = `c`.`Name`
""");
    }

    public override async Task Select_null_propagation_negative9(bool async)
    {
        await base.Select_null_propagation_negative9(async);

        AssertSql(
            """
SELECT IIF(`g`.`LeaderNickname` IS NOT NULL, CBOOL(IIF(LEN(`g`.`Nickname`) IS NULL, NULL, CLNG(LEN(`g`.`Nickname`))) BXOR 5) BXOR TRUE, NULL)
FROM `Gears` AS `g`
""");
    }

    public override async Task Select_null_propagation_works_for_navigations_with_composite_keys(bool async)
    {
        await base.Select_null_propagation_works_for_navigations_with_composite_keys(async);

        AssertSql(
            """
SELECT `s`.`Nickname`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
""");
    }

    public override async Task Select_null_propagation_works_for_multiple_navigations_with_composite_keys(bool async)
    {
        await base.Select_null_propagation_works_for_multiple_navigations_with_composite_keys(async);

        AssertSql(
            """
SELECT IIF(`c`.`Name` IS NOT NULL, `c`.`Name`, NULL)
FROM (((`Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`)
LEFT JOIN `Tags` AS `t0` ON (`s`.`Nickname` = `t0`.`GearNickName` OR (`s`.`Nickname` IS NULL AND `t0`.`GearNickName` IS NULL)) AND (`s`.`SquadId` = `t0`.`GearSquadId` OR (`s`.`SquadId` IS NULL AND `t0`.`GearSquadId` IS NULL)))
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`
    FROM `Gears` AS `g0`
) AS `s0` ON `t0`.`GearNickName` = `s0`.`Nickname` AND `t0`.`GearSquadId` = `s0`.`SquadId`)
LEFT JOIN `Cities` AS `c` ON `s0`.`AssignedCityName` = `c`.`Name`
""");
    }

    public override async Task Select_conditional_with_anonymous_type_and_null_constant(bool async)
    {
        await base.Select_conditional_with_anonymous_type_and_null_constant(async);

        AssertSql(
            """
SELECT IIF(`g`.`LeaderNickname` IS NOT NULL, TRUE, FALSE), `g`.`HasSoulPatch`
FROM `Gears` AS `g`
ORDER BY `g`.`Nickname`
""");
    }

    public override async Task Select_conditional_with_anonymous_types(bool async)
    {
        await base.Select_conditional_with_anonymous_types(async);

        AssertSql(
            """
SELECT IIF(`g`.`LeaderNickname` IS NOT NULL, TRUE, FALSE), `g`.`Nickname`, `g`.`FullName`
FROM `Gears` AS `g`
ORDER BY `g`.`Nickname`
""");
    }

    public override async Task Where_conditional_equality_1(bool async)
    {
        await base.Where_conditional_equality_1(async);

        AssertSql(
"""
SELECT `g`.`Nickname`
FROM `Gears` AS `g`
WHERE `g`.`LeaderNickname` IS NULL
ORDER BY `g`.`Nickname`
""");
    }

    public override async Task Where_conditional_equality_2(bool async)
    {
        await base.Where_conditional_equality_2(async);

        AssertSql(
"""
SELECT `g`.`Nickname`
FROM `Gears` AS `g`
WHERE `g`.`LeaderNickname` IS NULL
ORDER BY `g`.`Nickname`
""");
    }

    public override async Task Where_conditional_equality_3(bool async)
    {
        await base.Where_conditional_equality_3(async);

        AssertSql(
            """
SELECT `g`.`Nickname`
FROM `Gears` AS `g`
ORDER BY `g`.`Nickname`
""");
    }

    public override async Task Select_coalesce_with_anonymous_types(bool async)
    {
        await base.Select_coalesce_with_anonymous_types(async);

        AssertSql(
"""
SELECT `g`.`LeaderNickname`, `g`.`FullName`
FROM `Gears` AS `g`
ORDER BY `g`.`Nickname`
""");
    }

    public override async Task Where_compare_anonymous_types(bool async)
    {
        await base.Where_compare_anonymous_types(async);

        AssertSql();
    }

    public override async Task Where_member_access_on_anonymous_type(bool async)
    {
        await base.Where_member_access_on_anonymous_type(async);

        AssertSql(
"""
SELECT `g`.`Nickname`
FROM `Gears` AS `g`
WHERE `g`.`LeaderNickname` = 'Marcus'
""");
    }

    public override async Task Where_compare_anonymous_types_with_uncorrelated_members(bool async)
    {
        await base.Where_compare_anonymous_types_with_uncorrelated_members(async);

        AssertSql(
            """
SELECT `g`.`Nickname`
FROM `Gears` AS `g`
WHERE 0 = 1
""");
    }

    public override async Task Select_Where_Navigation_Scalar_Equals_Navigation_Scalar(bool async)
    {
        await base.Select_Where_Navigation_Scalar_Equals_Navigation_Scalar(async);

        AssertSql(
            """
SELECT `s`.`Id`, `s`.`GearNickName`, `s`.`GearSquadId`, `s`.`IssueDate`, `s`.`Note`, `s`.`Id0`, `s`.`GearNickName0`, `s`.`GearSquadId0`, `s`.`IssueDate0`, `s`.`Note0`
FROM ((
    SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`IssueDate`, `t`.`Note`, `t0`.`Id` AS `Id0`, `t0`.`GearNickName` AS `GearNickName0`, `t0`.`GearSquadId` AS `GearSquadId0`, `t0`.`IssueDate` AS `IssueDate0`, `t0`.`Note` AS `Note0`
    FROM `Tags` AS `t`,
    `Tags` AS `t0`
) AS `s`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`
    FROM `Gears` AS `g`
) AS `s0` ON `s`.`GearNickName` = `s0`.`Nickname` AND `s`.`GearSquadId` = `s0`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`
    FROM `Gears` AS `g0`
) AS `s1` ON `s`.`GearNickName0` = `s1`.`Nickname` AND `s`.`GearSquadId0` = `s1`.`SquadId`
WHERE `s0`.`Nickname` = `s1`.`Nickname` OR (`s0`.`Nickname` IS NULL AND `s1`.`Nickname` IS NULL)
""");
    }

    public override async Task Select_Singleton_Navigation_With_Member_Access(bool async)
    {
        await base.Select_Singleton_Navigation_With_Member_Access(async);

        AssertSql(
            """
SELECT `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
WHERE `s`.`Nickname` = 'Marcus' AND (`s`.`CityOfBirthName` <> 'Ephyra' OR `s`.`CityOfBirthName` IS NULL)
""");
    }

    public override async Task Select_Where_Navigation(bool async)
    {
        await base.Select_Where_Navigation(async);

        AssertSql(
            """
SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`IssueDate`, `t`.`Note`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
WHERE `s`.`Nickname` = 'Marcus'
""");
    }

    public override async Task Select_Where_Navigation_Equals_Navigation(bool async)
    {
        await base.Select_Where_Navigation_Equals_Navigation(async);

        AssertSql(
            """
SELECT `s`.`Id`, `s`.`GearNickName`, `s`.`GearSquadId`, `s`.`IssueDate`, `s`.`Note`, `s`.`Id0`, `s`.`GearNickName0`, `s`.`GearSquadId0`, `s`.`IssueDate0`, `s`.`Note0`
FROM ((
    SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`IssueDate`, `t`.`Note`, `t0`.`Id` AS `Id0`, `t0`.`GearNickName` AS `GearNickName0`, `t0`.`GearSquadId` AS `GearSquadId0`, `t0`.`IssueDate` AS `IssueDate0`, `t0`.`Note` AS `Note0`
    FROM `Tags` AS `t`,
    `Tags` AS `t0`
) AS `s`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`
    FROM `Gears` AS `g`
) AS `s0` ON `s`.`GearNickName` = `s0`.`Nickname` AND `s`.`GearSquadId` = `s0`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`
    FROM `Gears` AS `g0`
) AS `s1` ON `s`.`GearNickName0` = `s1`.`Nickname` AND `s`.`GearSquadId0` = `s1`.`SquadId`
WHERE (`s0`.`Nickname` = `s1`.`Nickname` OR (`s0`.`Nickname` IS NULL AND `s1`.`Nickname` IS NULL)) AND (`s0`.`SquadId` = `s1`.`SquadId` OR (`s0`.`SquadId` IS NULL AND `s1`.`SquadId` IS NULL))
""");
    }

    public override async Task Select_Where_Navigation_Null(bool async)
    {
        await base.Select_Where_Navigation_Null(async);

        AssertSql(
            """
SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`IssueDate`, `t`.`Note`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
WHERE `s`.`Nickname` IS NULL OR `s`.`SquadId` IS NULL
""");
    }

    public override async Task Select_Where_Navigation_Null_Reverse(bool async)
    {
        await base.Select_Where_Navigation_Null_Reverse(async);

        AssertSql(
            """
SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`IssueDate`, `t`.`Note`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
WHERE `s`.`Nickname` IS NULL OR `s`.`SquadId` IS NULL
""");
    }

    public override async Task Select_Where_Navigation_Scalar_Equals_Navigation_Scalar_Projected(bool async)
    {
        await base.Select_Where_Navigation_Scalar_Equals_Navigation_Scalar_Projected(async);

        AssertSql(
            """
SELECT `s`.`Id` AS `Id1`, `s`.`Id0` AS `Id2`
FROM ((
    SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t0`.`Id` AS `Id0`, `t0`.`GearNickName` AS `GearNickName0`, `t0`.`GearSquadId` AS `GearSquadId0`
    FROM `Tags` AS `t`,
    `Tags` AS `t0`
) AS `s`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`
    FROM `Gears` AS `g`
) AS `s0` ON `s`.`GearNickName` = `s0`.`Nickname` AND `s`.`GearSquadId` = `s0`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`
    FROM `Gears` AS `g0`
) AS `s1` ON `s`.`GearNickName0` = `s1`.`Nickname` AND `s`.`GearSquadId0` = `s1`.`SquadId`
WHERE `s0`.`Nickname` = `s1`.`Nickname` OR (`s0`.`Nickname` IS NULL AND `s1`.`Nickname` IS NULL)
""");
    }

    public override async Task Optional_Navigation_Null_Coalesce_To_Clr_Type(bool async)
    {
        await base.Optional_Navigation_Null_Coalesce_To_Clr_Type(async);

        AssertSql(
"""
SELECT TOP 1 IIF(`w0`.`IsAutomatic` IS NULL, FALSE, `w0`.`IsAutomatic`) AS `IsAutomatic`
FROM `Weapons` AS `w`
LEFT JOIN `Weapons` AS `w0` ON `w`.`SynergyWithId` = `w0`.`Id`
ORDER BY `w`.`Id`
""");
    }

    public override async Task Where_subquery_boolean(bool async)
    {
        await base.Where_subquery_boolean(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE IIF((
        SELECT TOP 1 `w`.`IsAutomatic`
        FROM `Weapons` AS `w`
        WHERE `g`.`FullName` = `w`.`OwnerFullName`
        ORDER BY `w`.`Id`) IS NULL, FALSE, (
        SELECT TOP 1 `w`.`IsAutomatic`
        FROM `Weapons` AS `w`
        WHERE `g`.`FullName` = `w`.`OwnerFullName`
        ORDER BY `w`.`Id`)) = TRUE
""");
    }

    public override async Task Where_subquery_boolean_with_pushdown(bool async)
    {
        await base.Where_subquery_boolean_with_pushdown(async);

        AssertSql(
            """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE (
    SELECT TOP 1 `w`.`IsAutomatic`
    FROM `Weapons` AS `w`
    WHERE `g`.`FullName` = `w`.`OwnerFullName`
    ORDER BY `w`.`Id`) = TRUE
""");
    }

    public override async Task Where_subquery_distinct_firstordefault_boolean(bool async)
    {
        await base.Where_subquery_distinct_firstordefault_boolean(async);

        AssertSql(
"""
SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOfBirthName], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], CASE
    WHEN [o].[Nickname] IS NOT NULL THEN N'Officer'
END AS [Discriminator]
FROM [Gears] AS [g]
LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
WHERE [g].[HasSoulPatch] = CAST(1 AS bit) AND COALESCE((
    SELECT TOP(1) [t].[IsAutomatic]
    FROM (
        SELECT DISTINCT [w].[Id], [w].[AmmunitionType], [w].[IsAutomatic], [w].[Name], [w].[OwnerFullName], [w].[SynergyWithId]
        FROM [Weapons] AS [w]
        WHERE [g].[FullName] = [w].[OwnerFullName]
    ) AS [t]
    ORDER BY [t].[Id]), CAST(0 AS bit)) = CAST(1 AS bit)
""");
    }

    public override async Task Where_subquery_distinct_firstordefault_boolean_with_pushdown(bool async)
    {
        await base.Where_subquery_distinct_firstordefault_boolean_with_pushdown(async);

        AssertSql(
"""
SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOfBirthName], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], CASE
    WHEN [o].[Nickname] IS NOT NULL THEN N'Officer'
END AS [Discriminator]
FROM [Gears] AS [g]
LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
WHERE [g].[HasSoulPatch] = CAST(1 AS bit) AND (
    SELECT TOP(1) [t].[IsAutomatic]
    FROM (
        SELECT DISTINCT [w].[Id], [w].[AmmunitionType], [w].[IsAutomatic], [w].[Name], [w].[OwnerFullName], [w].[SynergyWithId]
        FROM [Weapons] AS [w]
        WHERE [g].[FullName] = [w].[OwnerFullName]
    ) AS [t]
    ORDER BY [t].[Id]) = CAST(1 AS bit)
""");
    }

    public override async Task Where_subquery_distinct_first_boolean(bool async)
    {
        await base.Where_subquery_distinct_first_boolean(async);

        AssertSql(
"""
SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOfBirthName], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], CASE
    WHEN [o].[Nickname] IS NOT NULL THEN N'Officer'
END AS [Discriminator]
FROM [Gears] AS [g]
LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
WHERE [g].[HasSoulPatch] = CAST(1 AS bit) AND (
    SELECT TOP(1) [t].[IsAutomatic]
    FROM (
        SELECT DISTINCT [w].[Id], [w].[AmmunitionType], [w].[IsAutomatic], [w].[Name], [w].[OwnerFullName], [w].[SynergyWithId]
        FROM [Weapons] AS [w]
        WHERE [g].[FullName] = [w].[OwnerFullName]
    ) AS [t]
    ORDER BY [t].[Id]) = CAST(1 AS bit)
ORDER BY [g].[Nickname]
""");
    }

    public override async Task Where_subquery_distinct_singleordefault_boolean1(bool async)
    {
        await base.Where_subquery_distinct_singleordefault_boolean1(async);

        AssertSql(
"""
SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOfBirthName], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], CASE
    WHEN [o].[Nickname] IS NOT NULL THEN N'Officer'
END AS [Discriminator]
FROM [Gears] AS [g]
LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
WHERE [g].[HasSoulPatch] = CAST(1 AS bit) AND COALESCE((
    SELECT TOP(1) [t].[IsAutomatic]
    FROM (
        SELECT DISTINCT [w].[Id], [w].[AmmunitionType], [w].[IsAutomatic], [w].[Name], [w].[OwnerFullName], [w].[SynergyWithId]
        FROM [Weapons] AS [w]
        WHERE [g].[FullName] = [w].[OwnerFullName] AND ([w].[Name] LIKE N'%Lancer%')
    ) AS [t]), CAST(0 AS bit)) = CAST(1 AS bit)
ORDER BY [g].[Nickname]
""");
    }

    public override async Task Where_subquery_distinct_singleordefault_boolean2(bool async)
    {
        await base.Where_subquery_distinct_singleordefault_boolean2(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE `g`.`HasSoulPatch` = TRUE AND IIF((
        SELECT DISTINCT TOP 1 `w`.`IsAutomatic`
        FROM `Weapons` AS `w`
        WHERE `g`.`FullName` = `w`.`OwnerFullName` AND (`w`.`Name` LIKE '%Lancer%')) IS NULL, FALSE, (
        SELECT DISTINCT TOP 1 `w`.`IsAutomatic`
        FROM `Weapons` AS `w`
        WHERE `g`.`FullName` = `w`.`OwnerFullName` AND (`w`.`Name` LIKE '%Lancer%'))) = TRUE
ORDER BY `g`.`Nickname`
""");
    }

    public override async Task Where_subquery_distinct_singleordefault_boolean_with_pushdown(bool async)
    {
        await base.Where_subquery_distinct_singleordefault_boolean_with_pushdown(async);

        AssertSql(
"""
SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOfBirthName], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], CASE
    WHEN [o].[Nickname] IS NOT NULL THEN N'Officer'
END AS [Discriminator]
FROM [Gears] AS [g]
LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
WHERE [g].[HasSoulPatch] = CAST(1 AS bit) AND (
    SELECT TOP(1) [t].[IsAutomatic]
    FROM (
        SELECT DISTINCT [w].[Id], [w].[AmmunitionType], [w].[IsAutomatic], [w].[Name], [w].[OwnerFullName], [w].[SynergyWithId]
        FROM [Weapons] AS [w]
        WHERE [g].[FullName] = [w].[OwnerFullName] AND ([w].[Name] LIKE N'%Lancer%')
    ) AS [t]) = CAST(1 AS bit)
ORDER BY [g].[Nickname]
""");
    }

    public override async Task Where_subquery_distinct_lastordefault_boolean(bool async)
    {
        await base.Where_subquery_distinct_lastordefault_boolean(async);

        AssertSql(
"""
SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOfBirthName], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], CASE
    WHEN [o].[Nickname] IS NOT NULL THEN N'Officer'
END AS [Discriminator]
FROM [Gears] AS [g]
LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
WHERE (
    SELECT TOP(1) [t].[IsAutomatic]
    FROM (
        SELECT DISTINCT [w].[Id], [w].[AmmunitionType], [w].[IsAutomatic], [w].[Name], [w].[OwnerFullName], [w].[SynergyWithId]
        FROM [Weapons] AS [w]
        WHERE [g].[FullName] = [w].[OwnerFullName]
    ) AS [t]
    ORDER BY [t].[Id] DESC) = CAST(0 AS bit)
ORDER BY [g].[Nickname]
""");
    }

    public override async Task Where_subquery_distinct_last_boolean(bool async)
    {
        await base.Where_subquery_distinct_last_boolean(async);

        AssertSql(
"""
SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOfBirthName], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], CASE
    WHEN [o].[Nickname] IS NOT NULL THEN N'Officer'
END AS [Discriminator]
FROM [Gears] AS [g]
LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
WHERE [g].[HasSoulPatch] = CAST(0 AS bit) AND (
    SELECT TOP(1) [t].[IsAutomatic]
    FROM (
        SELECT DISTINCT [w].[Id], [w].[AmmunitionType], [w].[IsAutomatic], [w].[Name], [w].[OwnerFullName], [w].[SynergyWithId]
        FROM [Weapons] AS [w]
        WHERE [g].[FullName] = [w].[OwnerFullName]
    ) AS [t]
    ORDER BY [t].[Id] DESC) = CAST(1 AS bit)
ORDER BY [g].[Nickname]
""");
    }

    public override async Task Where_subquery_distinct_orderby_firstordefault_boolean(bool async)
    {
        await base.Where_subquery_distinct_orderby_firstordefault_boolean(async);

        AssertSql(
"""
SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOfBirthName], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], CASE
    WHEN [o].[Nickname] IS NOT NULL THEN N'Officer'
END AS [Discriminator]
FROM [Gears] AS [g]
LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
WHERE [g].[HasSoulPatch] = CAST(1 AS bit) AND COALESCE((
    SELECT TOP(1) [t].[IsAutomatic]
    FROM (
        SELECT DISTINCT [w].[Id], [w].[AmmunitionType], [w].[IsAutomatic], [w].[Name], [w].[OwnerFullName], [w].[SynergyWithId]
        FROM [Weapons] AS [w]
        WHERE [g].[FullName] = [w].[OwnerFullName]
    ) AS [t]
    ORDER BY [t].[Id]), CAST(0 AS bit)) = CAST(1 AS bit)
""");
    }

    public override async Task Where_subquery_distinct_orderby_firstordefault_boolean_with_pushdown(bool async)
    {
        await base.Where_subquery_distinct_orderby_firstordefault_boolean_with_pushdown(async);

        AssertSql(
"""
SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOfBirthName], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], CASE
    WHEN [o].[Nickname] IS NOT NULL THEN N'Officer'
END AS [Discriminator]
FROM [Gears] AS [g]
LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
WHERE [g].[HasSoulPatch] = CAST(1 AS bit) AND (
    SELECT TOP(1) [t].[IsAutomatic]
    FROM (
        SELECT DISTINCT [w].[Id], [w].[AmmunitionType], [w].[IsAutomatic], [w].[Name], [w].[OwnerFullName], [w].[SynergyWithId]
        FROM [Weapons] AS [w]
        WHERE [g].[FullName] = [w].[OwnerFullName]
    ) AS [t]
    ORDER BY [t].[Id]) = CAST(1 AS bit)
""");
    }

    public override async Task Where_subquery_union_firstordefault_boolean(bool async)
    {
        await base.Where_subquery_union_firstordefault_boolean(async);

        AssertSql(
"""
SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOfBirthName], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], CASE
    WHEN [o].[Nickname] IS NOT NULL THEN N'Officer'
END AS [Discriminator]
FROM [Gears] AS [g]
LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
WHERE [g].[HasSoulPatch] = CAST(1 AS bit) AND (
    SELECT TOP(1) [t].[IsAutomatic]
    FROM (
        SELECT [w].[Id], [w].[AmmunitionType], [w].[IsAutomatic], [w].[Name], [w].[OwnerFullName], [w].[SynergyWithId]
        FROM [Weapons] AS [w]
        WHERE [g].[FullName] = [w].[OwnerFullName]
        UNION
        SELECT [w0].[Id], [w0].[AmmunitionType], [w0].[IsAutomatic], [w0].[Name], [w0].[OwnerFullName], [w0].[SynergyWithId]
        FROM [Weapons] AS [w0]
        WHERE [g].[FullName] = [w0].[OwnerFullName]
    ) AS [t]
    ORDER BY [t].[Id]) = CAST(1 AS bit)
""");
    }

    public override async Task Where_subquery_join_firstordefault_boolean(bool async)
    {
        await base.Where_subquery_join_firstordefault_boolean(async);

        AssertSql(
"""
SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOfBirthName], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], CASE
    WHEN [o].[Nickname] IS NOT NULL THEN N'Officer'
END AS [Discriminator]
FROM [Gears] AS [g]
LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
WHERE [g].[HasSoulPatch] = CAST(1 AS bit) AND (
    SELECT TOP(1) [w].[IsAutomatic]
    FROM [Weapons] AS [w]
    INNER JOIN (
        SELECT [w0].[Id], [w0].[AmmunitionType], [w0].[IsAutomatic], [w0].[Name], [w0].[OwnerFullName], [w0].[SynergyWithId]
        FROM [Weapons] AS [w0]
        WHERE [g].[FullName] = [w0].[OwnerFullName]
    ) AS [t] ON [w].[Id] = [t].[Id]
    WHERE [g].[FullName] = [w].[OwnerFullName]
    ORDER BY [w].[Id]) = CAST(1 AS bit)
""");
    }

    public override async Task Where_subquery_left_join_firstordefault_boolean(bool async)
    {
        await base.Where_subquery_left_join_firstordefault_boolean(async);

        AssertSql(
"""
SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOfBirthName], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], CASE
    WHEN [o].[Nickname] IS NOT NULL THEN N'Officer'
END AS [Discriminator]
FROM [Gears] AS [g]
LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
WHERE [g].[HasSoulPatch] = CAST(1 AS bit) AND (
    SELECT TOP(1) [w].[IsAutomatic]
    FROM [Weapons] AS [w]
    LEFT JOIN (
        SELECT [w0].[Id], [w0].[AmmunitionType], [w0].[IsAutomatic], [w0].[Name], [w0].[OwnerFullName], [w0].[SynergyWithId]
        FROM [Weapons] AS [w0]
        WHERE [g].[FullName] = [w0].[OwnerFullName]
    ) AS [t] ON [w].[Id] = [t].[Id]
    WHERE [g].[FullName] = [w].[OwnerFullName]
    ORDER BY [w].[Id]) = CAST(1 AS bit)
""");
    }

    public override async Task Where_subquery_concat_firstordefault_boolean(bool async)
    {
        await base.Where_subquery_concat_firstordefault_boolean(async);

        AssertSql(
"""
SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOfBirthName], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], CASE
    WHEN [o].[Nickname] IS NOT NULL THEN N'Officer'
END AS [Discriminator]
FROM [Gears] AS [g]
LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
WHERE [g].[HasSoulPatch] = CAST(1 AS bit) AND (
    SELECT TOP(1) [t].[IsAutomatic]
    FROM (
        SELECT [w].[Id], [w].[AmmunitionType], [w].[IsAutomatic], [w].[Name], [w].[OwnerFullName], [w].[SynergyWithId]
        FROM [Weapons] AS [w]
        WHERE [g].[FullName] = [w].[OwnerFullName]
        UNION ALL
        SELECT [w0].[Id], [w0].[AmmunitionType], [w0].[IsAutomatic], [w0].[Name], [w0].[OwnerFullName], [w0].[SynergyWithId]
        FROM [Weapons] AS [w0]
        WHERE [g].[FullName] = [w0].[OwnerFullName]
    ) AS [t]
    ORDER BY [t].[Id]) = CAST(1 AS bit)
""");
    }

    public override async Task Concat_with_count(bool async)
    {
        await base.Concat_with_count(async);

        AssertSql(
            """
SELECT COUNT(*)
FROM (
    SELECT 1
    FROM `Gears` AS `g`
    UNION ALL
    SELECT 1
    FROM `Gears` AS `g0`
) AS `u`
""");
    }

    public override async Task Concat_scalars_with_count(bool async)
    {
        await base.Concat_scalars_with_count(async);

        AssertSql(
            """
SELECT COUNT(*)
FROM (
    SELECT 1
    FROM `Gears` AS `g`
    UNION ALL
    SELECT 1
    FROM `Gears` AS `g0`
) AS `u`
""");
    }

    public override async Task Concat_anonymous_with_count(bool async)
    {
        await base.Concat_anonymous_with_count(async);

        AssertSql(
            """
SELECT COUNT(*)
FROM (
    SELECT 1
    FROM `Gears` AS `g`
    UNION ALL
    SELECT 1
    FROM `Gears` AS `g0`
) AS `u`
""");
    }

    public override async Task Concat_with_scalar_projection(bool async)
    {
        await base.Concat_with_scalar_projection(async);

        AssertSql(
"""
SELECT `g`.`Nickname`
FROM `Gears` AS `g`
UNION ALL
SELECT `g0`.`Nickname`
FROM `Gears` AS `g0`
""");
    }

    public override async Task Select_navigation_with_concat_and_count(bool async)
    {
        await base.Select_navigation_with_concat_and_count(async);

        AssertSql(
"""
SELECT (
    SELECT COUNT(*)
    FROM (
        SELECT [w].[Id], [w].[AmmunitionType], [w].[IsAutomatic], [w].[Name], [w].[OwnerFullName], [w].[SynergyWithId]
        FROM [Weapons] AS [w]
        WHERE [g].[FullName] = [w].[OwnerFullName]
        UNION ALL
        SELECT [w0].[Id], [w0].[AmmunitionType], [w0].[IsAutomatic], [w0].[Name], [w0].[OwnerFullName], [w0].[SynergyWithId]
        FROM [Weapons] AS [w0]
        WHERE [g].[FullName] = [w0].[OwnerFullName]
    ) AS [t])
FROM [Gears] AS [g]
WHERE [g].[HasSoulPatch] = CAST(0 AS bit)
""");
    }

    public override async Task Concat_with_collection_navigations(bool async)
    {
        await base.Concat_with_collection_navigations(async);

        AssertSql(
"""
SELECT (
    SELECT COUNT(*)
    FROM (
        SELECT [w].[Id], [w].[AmmunitionType], [w].[IsAutomatic], [w].[Name], [w].[OwnerFullName], [w].[SynergyWithId]
        FROM [Weapons] AS [w]
        WHERE [g].[FullName] = [w].[OwnerFullName]
        UNION
        SELECT [w0].[Id], [w0].[AmmunitionType], [w0].[IsAutomatic], [w0].[Name], [w0].[OwnerFullName], [w0].[SynergyWithId]
        FROM [Weapons] AS [w0]
        WHERE [g].[FullName] = [w0].[OwnerFullName]
    ) AS [t])
FROM [Gears] AS [g]
WHERE [g].[HasSoulPatch] = CAST(1 AS bit)
""");
    }

    public override async Task Union_with_collection_navigations(bool async)
    {
        await base.Union_with_collection_navigations(async);

        AssertSql(
"""
SELECT (
    SELECT COUNT(*)
    FROM (
        SELECT [g0].[Nickname], [g0].[SquadId], [g0].[AssignedCityName], [g0].[CityOfBirthName], [g0].[FullName], [g0].[HasSoulPatch], [g0].[LeaderNickname], [g0].[LeaderSquadId], [g0].[Rank], CASE
            WHEN [o0].[Nickname] IS NOT NULL THEN N'Officer'
        END AS [Discriminator]
        FROM [Gears] AS [g0]
        LEFT JOIN [Officers] AS [o0] ON [g0].[Nickname] = [o0].[Nickname] AND [g0].[SquadId] = [o0].[SquadId]
        WHERE [g].[Nickname] = [g0].[LeaderNickname] AND [g].[SquadId] = [g0].[LeaderSquadId]
        UNION
        SELECT [g1].[Nickname], [g1].[SquadId], [g1].[AssignedCityName], [g1].[CityOfBirthName], [g1].[FullName], [g1].[HasSoulPatch], [g1].[LeaderNickname], [g1].[LeaderSquadId], [g1].[Rank], CASE
            WHEN [o1].[Nickname] IS NOT NULL THEN N'Officer'
        END AS [Discriminator]
        FROM [Gears] AS [g1]
        LEFT JOIN [Officers] AS [o1] ON [g1].[Nickname] = [o1].[Nickname] AND [g1].[SquadId] = [o1].[SquadId]
        WHERE [g].[Nickname] = [g1].[LeaderNickname] AND [g].[SquadId] = [g1].[LeaderSquadId]
    ) AS [t])
FROM [Gears] AS [g]
LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
WHERE [o].[Nickname] IS NOT NULL
""");
    }

    public override async Task Select_subquery_distinct_firstordefault(bool async)
    {
        await base.Select_subquery_distinct_firstordefault(async);

        AssertSql(
"""
SELECT (
    SELECT TOP(1) [t].[Name]
    FROM (
        SELECT DISTINCT [w].[Id], [w].[AmmunitionType], [w].[IsAutomatic], [w].[Name], [w].[OwnerFullName], [w].[SynergyWithId]
        FROM [Weapons] AS [w]
        WHERE [g].[FullName] = [w].[OwnerFullName]
    ) AS [t]
    ORDER BY [t].[Id])
FROM [Gears] AS [g]
WHERE [g].[HasSoulPatch] = CAST(1 AS bit)
""");
    }

    public override async Task Singleton_Navigation_With_Member_Access(bool async)
    {
        await base.Singleton_Navigation_With_Member_Access(async);

        AssertSql(
            """
SELECT `s`.`CityOfBirthName` AS `B`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`CityOfBirthName`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
WHERE `s`.`Nickname` = 'Marcus' AND (`s`.`CityOfBirthName` <> 'Ephyra' OR `s`.`CityOfBirthName` IS NULL)
""");
    }

    public override async Task GroupJoin_Composite_Key(bool async)
    {
        await base.GroupJoin_Composite_Key(async);

        AssertSql(
            """
SELECT `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`
FROM `Tags` AS `t`
INNER JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
""");
    }

    public override async Task Join_navigation_translated_to_subquery_composite_key(bool async)
    {
        await base.Join_navigation_translated_to_subquery_composite_key(async);

        AssertSql(
            """
SELECT `g`.`FullName`, `s0`.`Note`
FROM `Gears` AS `g`
INNER JOIN (
    SELECT `t`.`Note`, `s`.`FullName`
    FROM `Tags` AS `t`
    LEFT JOIN (
        SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`FullName`
        FROM `Gears` AS `g0`
    ) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
) AS `s0` ON `g`.`FullName` = `s0`.`FullName`
""");
    }

    public override async Task Join_with_order_by_on_inner_sequence_navigation_translated_to_subquery_composite_key(bool async)
    {
        await base.Join_with_order_by_on_inner_sequence_navigation_translated_to_subquery_composite_key(async);

        AssertSql(
            """
SELECT `g`.`FullName`, `s0`.`Note`
FROM `Gears` AS `g`
INNER JOIN (
    SELECT `t`.`Note`, `s`.`FullName`
    FROM `Tags` AS `t`
    LEFT JOIN (
        SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`FullName`
        FROM `Gears` AS `g0`
    ) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
) AS `s0` ON `g`.`FullName` = `s0`.`FullName`
""");
    }

    public override async Task Join_with_order_by_without_skip_or_take(bool async)
    {
        await base.Join_with_order_by_without_skip_or_take(async);

        AssertSql(
"""
SELECT `w`.`Name`, `g`.`FullName`
FROM `Gears` AS `g`
INNER JOIN `Weapons` AS `w` ON `g`.`FullName` = `w`.`OwnerFullName`
""");
    }

    public override async Task Join_with_order_by_without_skip_or_take_nested(bool async)
    {
        await base.Join_with_order_by_without_skip_or_take_nested(async);

        AssertSql(
            """
SELECT `w`.`Name`, `s0`.`FullName`
FROM (`Squads` AS `s`
INNER JOIN (
    SELECT `g`.`SquadId`, `g`.`FullName`
    FROM `Gears` AS `g`
) AS `s0` ON `s`.`Id` = `s0`.`SquadId`)
LEFT JOIN `Weapons` AS `w` ON `s0`.`FullName` = `w`.`OwnerFullName`
WHERE `s0`.`FullName` IS NOT NULL AND `w`.`OwnerFullName` IS NOT NULL
""");
    }

    public override async Task Collection_with_inheritance_and_join_include_joined(bool async)
    {
        await base.Collection_with_inheritance_and_join_include_joined(async);

        AssertSql(
            """
SELECT `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`, `t0`.`Id`, `t0`.`GearNickName`, `t0`.`GearSquadId`, `t0`.`IssueDate`, `t0`.`Note`
FROM (`Tags` AS `t`
INNER JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
    WHERE `o`.`Nickname` IS NOT NULL
) AS `s` ON `t`.`GearSquadId` = `s`.`SquadId` AND `t`.`GearNickName` = `s`.`Nickname`)
LEFT JOIN `Tags` AS `t0` ON `s`.`Nickname` = `t0`.`GearNickName` AND `s`.`SquadId` = `t0`.`GearSquadId`
""");
    }

    public override async Task Collection_with_inheritance_and_join_include_source(bool async)
    {
        await base.Collection_with_inheritance_and_join_include_source(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `t0`.`Id`, `t0`.`GearNickName`, `t0`.`GearSquadId`, `t0`.`IssueDate`, `t0`.`Note`
FROM ((`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
INNER JOIN `Tags` AS `t` ON `g`.`SquadId` = `t`.`GearSquadId` AND `g`.`Nickname` = `t`.`GearNickName`)
LEFT JOIN `Tags` AS `t0` ON `g`.`Nickname` = `t0`.`GearNickName` AND `g`.`SquadId` = `t0`.`GearSquadId`
WHERE `o`.`Nickname` IS NOT NULL
""");
    }

    public override async Task Non_unicode_string_literal_is_used_for_non_unicode_column(bool async)
    {
        await base.Non_unicode_string_literal_is_used_for_non_unicode_column(async);

        AssertSql(
"""
SELECT `c`.`Name`, `c`.`Location`, `c`.`Nation`
FROM `Cities` AS `c`
WHERE `c`.`Location` = 'Unknown'
""");
    }

    public override async Task Non_unicode_string_literal_is_used_for_non_unicode_column_right(bool async)
    {
        await base.Non_unicode_string_literal_is_used_for_non_unicode_column_right(async);

        AssertSql(
"""
SELECT `c`.`Name`, `c`.`Location`, `c`.`Nation`
FROM `Cities` AS `c`
WHERE 'Unknown' = `c`.`Location`
""");
    }

    public override async Task Non_unicode_parameter_is_used_for_non_unicode_column(bool async)
    {
        await base.Non_unicode_parameter_is_used_for_non_unicode_column(async);

        AssertSql(
"""
@__value_0='Unknown' (Size = 100)

SELECT `c`.`Name`, `c`.`Location`, `c`.`Nation`
FROM `Cities` AS `c`
WHERE `c`.`Location` = @__value_0
""");
    }

    public override async Task Non_unicode_string_literals_in_contains_is_used_for_non_unicode_column(bool async)
    {
        await base.Non_unicode_string_literals_in_contains_is_used_for_non_unicode_column(async);

        AssertSql(
"""
SELECT `c`.`Name`, `c`.`Location`, `c`.`Nation`
FROM `Cities` AS `c`
WHERE `c`.`Location` IN ('Unknown', 'Jacinto''s location', 'Ephyra''s location')
""");
    }

    public override async Task Non_unicode_string_literals_is_used_for_non_unicode_column_with_subquery(bool async)
    {
        await base.Non_unicode_string_literals_is_used_for_non_unicode_column_with_subquery(async);

        AssertSql(
            """
SELECT `c`.`Name`, `c`.`Location`, `c`.`Nation`
FROM `Cities` AS `c`
WHERE `c`.`Location` = 'Unknown' AND (
    SELECT COUNT(*)
    FROM `Gears` AS `g`
    WHERE `c`.`Name` = `g`.`CityOfBirthName` AND `g`.`Nickname` = 'Paduk') = 1
""");
    }

    public override async Task Non_unicode_string_literals_is_used_for_non_unicode_column_in_subquery(bool async)
    {
        await base.Non_unicode_string_literals_is_used_for_non_unicode_column_in_subquery(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM (`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
INNER JOIN `Cities` AS `c` ON `g`.`CityOfBirthName` = `c`.`Name`
WHERE `g`.`Nickname` = 'Marcus' AND `c`.`Location` = 'Jacinto''s location'
""");
    }

    public override async Task Non_unicode_string_literals_is_used_for_non_unicode_column_with_contains(bool async)
    {
        await base.Non_unicode_string_literals_is_used_for_non_unicode_column_with_contains(async);

        AssertSql(
"""
SELECT `c`.`Name`, `c`.`Location`, `c`.`Nation`
FROM `Cities` AS `c`
WHERE `c`.`Location` LIKE '%Jacinto%'
""");
    }

    public override async Task Unicode_string_literals_is_used_for_non_unicode_column_with_concat(bool async)
    {
        await base.Unicode_string_literals_is_used_for_non_unicode_column_with_concat(async);

        AssertSql(
"""
SELECT `c`.`Name`, `c`.`Location`, `c`.`Nation`
FROM `Cities` AS `c`
WHERE IIF(`c`.`Location` IS NULL, '', `c`.`Location`) & 'Added' LIKE '%Add%'
""");
    }

    public override void Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_coalesce_result1()
    {
        base.Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_coalesce_result1();

        // Issue#16897
        AssertSql(
            """
SELECT `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`, `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM ((`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g0`
    LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`
) AS `s` ON `g`.`LeaderNickname` = `s`.`Nickname`)
LEFT JOIN `Weapons` AS `w` ON `g`.`FullName` = `w`.`OwnerFullName`
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `s`.`Nickname`, `s`.`SquadId`
""");
    }

    public override void Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_coalesce_result2()
    {
        base.Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_coalesce_result2();

        // Issue#16897
        AssertSql(
            """
SELECT `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`, `g`.`Nickname`, `g`.`SquadId`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM ((`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g0`
    LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`
) AS `s` ON `g`.`LeaderNickname` = `s`.`Nickname`)
LEFT JOIN `Weapons` AS `w` ON `s`.`FullName` = `w`.`OwnerFullName`
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `s`.`Nickname`, `s`.`SquadId`
""");
    }

    public override async Task Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_coalesce_result3(bool async)
    {
        await base.Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_coalesce_result3(async);

        // Issue#16897
        AssertSql(
            """
SELECT `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`, `g`.`Nickname`, `g`.`SquadId`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
FROM (((`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g0`
    LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`
) AS `s` ON `g`.`LeaderNickname` = `s`.`Nickname`)
LEFT JOIN `Weapons` AS `w` ON `s`.`FullName` = `w`.`OwnerFullName`)
LEFT JOIN `Weapons` AS `w0` ON `g`.`FullName` = `w0`.`OwnerFullName`
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `s`.`Nickname`, `s`.`SquadId`, `w`.`Id`
""");
    }

    public override async Task Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_coalesce_result4(bool async)
    {
        await base.Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_coalesce_result4(async);

        // Issue#16897
        AssertSql(
            """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `s`.`Nickname`, `s`.`SquadId`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`, `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`, `w1`.`Id`, `w1`.`AmmunitionType`, `w1`.`IsAutomatic`, `w1`.`Name`, `w1`.`OwnerFullName`, `w1`.`SynergyWithId`, `w2`.`Id`, `w2`.`AmmunitionType`, `w2`.`IsAutomatic`, `w2`.`Name`, `w2`.`OwnerFullName`, `w2`.`SynergyWithId`
FROM (((((`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g0`
    LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`
) AS `s` ON `g`.`LeaderNickname` = `s`.`Nickname`)
LEFT JOIN `Weapons` AS `w` ON `g`.`FullName` = `w`.`OwnerFullName`)
LEFT JOIN `Weapons` AS `w0` ON `s`.`FullName` = `w0`.`OwnerFullName`)
LEFT JOIN `Weapons` AS `w1` ON `s`.`FullName` = `w1`.`OwnerFullName`)
LEFT JOIN `Weapons` AS `w2` ON `g`.`FullName` = `w2`.`OwnerFullName`
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `s`.`Nickname`, `s`.`SquadId`, `w`.`Id`, `w0`.`Id`, `w1`.`Id`
""");
    }

    public override async Task Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_inheritance_and_coalesce_result(bool async)
    {
        await base.Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_inheritance_and_coalesce_result(async);

        // Issue#16897
        AssertSql(
            """
SELECT `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`, `g`.`Nickname`, `g`.`SquadId`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
FROM (((`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g0`
    LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`
    WHERE `o0`.`Nickname` IS NOT NULL
) AS `s` ON `g`.`LeaderNickname` = `s`.`Nickname`)
LEFT JOIN `Weapons` AS `w` ON `s`.`FullName` = `w`.`OwnerFullName`)
LEFT JOIN `Weapons` AS `w0` ON `g`.`FullName` = `w0`.`OwnerFullName`
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `s`.`Nickname`, `s`.`SquadId`, `w`.`Id`
""");
    }

    public override async Task Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_conditional_result(bool async)
    {
        await base.Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_conditional_result(async);

        // Issue#16897
        AssertSql(
            """
SELECT IIF(`s`.`Nickname` IS NOT NULL AND `s`.`SquadId` IS NOT NULL, TRUE, FALSE), `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`, `g`.`Nickname`, `g`.`SquadId`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
FROM (((`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g0`
    LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`
) AS `s` ON `g`.`LeaderNickname` = `s`.`Nickname`)
LEFT JOIN `Weapons` AS `w` ON `s`.`FullName` = `w`.`OwnerFullName`)
LEFT JOIN `Weapons` AS `w0` ON `g`.`FullName` = `w0`.`OwnerFullName`
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `s`.`Nickname`, `s`.`SquadId`, `w`.`Id`
""");
    }

    public override async Task Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_complex_projection_result(bool async)
    {
        await base.Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_complex_projection_result(async);

        AssertSql(
            """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `s`.`Nickname`, `s`.`SquadId`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`, `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`, `w1`.`Id`, `w1`.`AmmunitionType`, `w1`.`IsAutomatic`, `w1`.`Name`, `w1`.`OwnerFullName`, `w1`.`SynergyWithId`, `w2`.`Id`, `w2`.`AmmunitionType`, `w2`.`IsAutomatic`, `w2`.`Name`, `w2`.`OwnerFullName`, `w2`.`SynergyWithId`, IIF(`s`.`Nickname` IS NOT NULL AND `s`.`SquadId` IS NOT NULL, TRUE, FALSE), `w3`.`Id`, `w3`.`AmmunitionType`, `w3`.`IsAutomatic`, `w3`.`Name`, `w3`.`OwnerFullName`, `w3`.`SynergyWithId`, `w4`.`Id`, `w4`.`AmmunitionType`, `w4`.`IsAutomatic`, `w4`.`Name`, `w4`.`OwnerFullName`, `w4`.`SynergyWithId`
FROM (((((((`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g0`
    LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`
) AS `s` ON `g`.`LeaderNickname` = `s`.`Nickname`)
LEFT JOIN `Weapons` AS `w` ON `g`.`FullName` = `w`.`OwnerFullName`)
LEFT JOIN `Weapons` AS `w0` ON `s`.`FullName` = `w0`.`OwnerFullName`)
LEFT JOIN `Weapons` AS `w1` ON `s`.`FullName` = `w1`.`OwnerFullName`)
LEFT JOIN `Weapons` AS `w2` ON `g`.`FullName` = `w2`.`OwnerFullName`)
LEFT JOIN `Weapons` AS `w3` ON `s`.`FullName` = `w3`.`OwnerFullName`)
LEFT JOIN `Weapons` AS `w4` ON `g`.`FullName` = `w4`.`OwnerFullName`
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `s`.`Nickname`, `s`.`SquadId`, `w`.`Id`, `w0`.`Id`, `w1`.`Id`, `w2`.`Id`, `w3`.`Id`
""");
    }

    public override async Task Coalesce_operator_in_predicate(bool async)
    {
        await base.Coalesce_operator_in_predicate(async);

        AssertSql(
            """
SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`IssueDate`, `t`.`Note`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`HasSoulPatch`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
WHERE IIF(`s`.`HasSoulPatch` IS NULL, FALSE, `s`.`HasSoulPatch`) = TRUE
""");
    }

    public override async Task Coalesce_operator_in_predicate_with_other_conditions(bool async)
    {
        await base.Coalesce_operator_in_predicate_with_other_conditions(async);

        AssertSql(
            """
SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`IssueDate`, `t`.`Note`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`HasSoulPatch`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
WHERE (`t`.`Note` <> 'K.I.A.' OR `t`.`Note` IS NULL) AND IIF(`s`.`HasSoulPatch` IS NULL, FALSE, `s`.`HasSoulPatch`) = TRUE
""");
    }

    public override async Task Coalesce_operator_in_projection_with_other_conditions(bool async)
    {
        await base.Coalesce_operator_in_projection_with_other_conditions(async);

        AssertSql(
            """
SELECT IIF((`t`.`Note` <> 'K.I.A.' OR `t`.`Note` IS NULL) AND IIF(`s`.`HasSoulPatch` IS NULL, FALSE, `s`.`HasSoulPatch`) = TRUE, TRUE, FALSE)
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`HasSoulPatch`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
""");
    }

    public override async Task Optional_navigation_type_compensation_works_with_predicate(bool async)
    {
        await base.Optional_navigation_type_compensation_works_with_predicate(async);

        AssertSql(
            """
SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`IssueDate`, `t`.`Note`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`HasSoulPatch`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
WHERE (`t`.`Note` <> 'K.I.A.' OR `t`.`Note` IS NULL) AND `s`.`HasSoulPatch` = TRUE
""");
    }

    public override async Task Optional_navigation_type_compensation_works_with_predicate2(bool async)
    {
        await base.Optional_navigation_type_compensation_works_with_predicate2(async);

        AssertSql(
            """
SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`IssueDate`, `t`.`Note`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`HasSoulPatch`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
WHERE `s`.`HasSoulPatch` = TRUE
""");
    }

    public override async Task Optional_navigation_type_compensation_works_with_predicate_negated(bool async)
    {
        await base.Optional_navigation_type_compensation_works_with_predicate_negated(async);

        AssertSql(
            """
SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`IssueDate`, `t`.`Note`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`HasSoulPatch`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
WHERE `s`.`HasSoulPatch` = FALSE
""");
    }

    public override async Task Optional_navigation_type_compensation_works_with_predicate_negated_complex1(bool async)
    {
        await base.Optional_navigation_type_compensation_works_with_predicate_negated_complex1(async);

        AssertSql(
            """
SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`IssueDate`, `t`.`Note`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`HasSoulPatch`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
WHERE IIF(`s`.`HasSoulPatch` = TRUE, TRUE, `s`.`HasSoulPatch`) = FALSE
""");
    }

    public override async Task Optional_navigation_type_compensation_works_with_predicate_negated_complex2(bool async)
    {
        await base.Optional_navigation_type_compensation_works_with_predicate_negated_complex2(async);

        AssertSql(
            """
SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`IssueDate`, `t`.`Note`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`HasSoulPatch`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
WHERE IIF(`s`.`HasSoulPatch` = FALSE, FALSE, `s`.`HasSoulPatch`) = FALSE
""");
    }

    public override async Task Optional_navigation_type_compensation_works_with_conditional_expression(bool async)
    {
        await base.Optional_navigation_type_compensation_works_with_conditional_expression(async);

        AssertSql(
            """
SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`IssueDate`, `t`.`Note`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`HasSoulPatch`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
WHERE IIF(`s`.`HasSoulPatch` = TRUE, TRUE, FALSE) = TRUE
""");
    }

    public override async Task Optional_navigation_type_compensation_works_with_binary_expression(bool async)
    {
        await base.Optional_navigation_type_compensation_works_with_binary_expression(async);

        AssertSql(
            """
SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`IssueDate`, `t`.`Note`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`HasSoulPatch`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
WHERE `s`.`HasSoulPatch` = TRUE OR (`t`.`Note` LIKE '%Cole%')
""");
    }

    public override async Task Optional_navigation_type_compensation_works_with_binary_and_expression(bool async)
    {
        await base.Optional_navigation_type_compensation_works_with_binary_and_expression(async);

        AssertSql(
            """
SELECT IIF(`s`.`HasSoulPatch` = TRUE AND (`t`.`Note` LIKE '%Cole%') AND `t`.`Note` IS NOT NULL, TRUE, FALSE)
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`HasSoulPatch`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
""");
    }

    public override async Task Optional_navigation_type_compensation_works_with_projection(bool async)
    {
        await base.Optional_navigation_type_compensation_works_with_projection(async);

        AssertSql(
            """
SELECT `s`.`SquadId`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
WHERE `t`.`Note` <> 'K.I.A.' OR `t`.`Note` IS NULL
""");
    }

    public override async Task Optional_navigation_type_compensation_works_with_projection_into_anonymous_type(bool async)
    {
        await base.Optional_navigation_type_compensation_works_with_projection_into_anonymous_type(async);

        AssertSql(
            """
SELECT `s`.`SquadId`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
WHERE `t`.`Note` <> 'K.I.A.' OR `t`.`Note` IS NULL
""");
    }

    public override async Task Optional_navigation_type_compensation_works_with_DTOs(bool async)
    {
        await base.Optional_navigation_type_compensation_works_with_DTOs(async);

        AssertSql(
            """
SELECT `s`.`SquadId` AS `Id`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
WHERE `t`.`Note` <> 'K.I.A.' OR `t`.`Note` IS NULL
""");
    }

    public override async Task Optional_navigation_type_compensation_works_with_list_initializers(bool async)
    {
        await base.Optional_navigation_type_compensation_works_with_list_initializers(async);

        AssertSql(
            """
SELECT `s`.`SquadId`, `s`.`SquadId` + 1
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
WHERE `t`.`Note` <> 'K.I.A.' OR `t`.`Note` IS NULL
ORDER BY `t`.`Note`
""");
    }

    public override async Task Optional_navigation_type_compensation_works_with_array_initializers(bool async)
    {
        await base.Optional_navigation_type_compensation_works_with_array_initializers(async);

        AssertSql(
            """
SELECT `s`.`SquadId`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
WHERE `t`.`Note` <> 'K.I.A.' OR `t`.`Note` IS NULL
""");
    }

    public override async Task Optional_navigation_type_compensation_works_with_orderby(bool async)
    {
        await base.Optional_navigation_type_compensation_works_with_orderby(async);

        AssertSql(
            """
SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`IssueDate`, `t`.`Note`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
WHERE `t`.`Note` <> 'K.I.A.' OR `t`.`Note` IS NULL
ORDER BY `s`.`SquadId`
""");
    }

    public override async Task Optional_navigation_type_compensation_works_with_all(bool async)
    {
        await base.Optional_navigation_type_compensation_works_with_all(async);

        AssertSql(
            """
SELECT IIF(NOT EXISTS (
        SELECT 1
        FROM `Tags` AS `t`
        LEFT JOIN (
            SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`HasSoulPatch`
            FROM `Gears` AS `g`
        ) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
        WHERE (`t`.`Note` <> 'K.I.A.' OR `t`.`Note` IS NULL) AND `s`.`HasSoulPatch` = FALSE), TRUE, FALSE)
FROM (SELECT COUNT(*) FROM `#Dual`)
""");
    }

    public override async Task Optional_navigation_type_compensation_works_with_negated_predicate(bool async)
    {
        await base.Optional_navigation_type_compensation_works_with_negated_predicate(async);
AssertSql(
    """
SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`IssueDate`, `t`.`Note`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`HasSoulPatch`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
WHERE (`t`.`Note` <> 'K.I.A.' OR `t`.`Note` IS NULL) AND `s`.`HasSoulPatch` = FALSE
""");
    }

    public override async Task Optional_navigation_type_compensation_works_with_contains(bool async)
    {
        await base.Optional_navigation_type_compensation_works_with_contains(async);
AssertSql(
    """
SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`IssueDate`, `t`.`Note`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
WHERE (`t`.`Note` <> 'K.I.A.' OR `t`.`Note` IS NULL) AND `s`.`SquadId` IN (
    SELECT `g0`.`SquadId`
    FROM `Gears` AS `g0`
)
""");
    }

    public override async Task Optional_navigation_type_compensation_works_with_skip(bool async)
    {
        await base.Optional_navigation_type_compensation_works_with_skip(async);

        AssertSql();
    }

    public override async Task Optional_navigation_type_compensation_works_with_take(bool async)
    {
        await base.Optional_navigation_type_compensation_works_with_take(async);

        AssertSql();
    }

    public override async Task Select_correlated_filtered_collection(bool async)
    {
        await base.Select_correlated_filtered_collection(async);
AssertSql(
    """
SELECT `g`.`Nickname`, `g`.`SquadId`, `c`.`Name`, `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
FROM (`Gears` AS `g`
INNER JOIN `Cities` AS `c` ON `g`.`CityOfBirthName` = `c`.`Name`)
LEFT JOIN (
    SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
    FROM `Weapons` AS `w`
    WHERE `w`.`Name` <> 'Lancer' OR `w`.`Name` IS NULL
) AS `w0` ON `g`.`FullName` = `w0`.`OwnerFullName`
WHERE `c`.`Name` IN ('Ephyra', 'Hanover')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `c`.`Name`
""");
    }

    public override async Task Select_correlated_filtered_collection_with_composite_key(bool async)
    {
        await base.Select_correlated_filtered_collection_with_composite_key(async);
AssertSql(
    """
SELECT `g`.`Nickname`, `g`.`SquadId`, `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`
FROM (`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g0`
    LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`
    WHERE `g0`.`Nickname` <> 'Dom'
) AS `s` ON `g`.`Nickname` = `s`.`LeaderNickname` AND `g`.`SquadId` = `s`.`LeaderSquadId`
WHERE `o`.`Nickname` IS NOT NULL
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `s`.`Nickname`
""");
    }

    public override async Task Select_correlated_filtered_collection_works_with_caching(bool async)
    {
        await base.Select_correlated_filtered_collection_works_with_caching(async);
AssertSql(
    """
SELECT `t`.`Id`, `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname`
ORDER BY `t`.`Note`, `t`.`Id`, `s`.`Nickname`
""");
    }

    public override async Task Join_predicate_value_equals_condition(bool async)
    {
        await base.Join_predicate_value_equals_condition(async);

        AssertSql(
"""
SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOfBirthName], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], CASE
    WHEN [o].[Nickname] IS NOT NULL THEN N'Officer'
END AS [Discriminator]
FROM [Gears] AS [g]
LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
INNER JOIN [Weapons] AS [w] ON [w].[SynergyWithId] IS NOT NULL
""");
    }

    public override async Task Join_predicate_value(bool async)
    {
        await base.Join_predicate_value(async);

        AssertSql(
"""
SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOfBirthName], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], CASE
    WHEN [o].[Nickname] IS NOT NULL THEN N'Officer'
END AS [Discriminator]
FROM [Gears] AS [g]
LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
INNER JOIN [Weapons] AS [w] ON [g].[HasSoulPatch] = CAST(1 AS bit)
""");
    }

    public override async Task Join_predicate_condition_equals_condition(bool async)
    {
        await base.Join_predicate_condition_equals_condition(async);

        AssertSql(
"""
SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOfBirthName], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], CASE
    WHEN [o].[Nickname] IS NOT NULL THEN N'Officer'
END AS [Discriminator]
FROM [Gears] AS [g]
LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
INNER JOIN [Weapons] AS [w] ON [w].[SynergyWithId] IS NOT NULL
""");
    }

    public override async Task Left_join_predicate_value_equals_condition(bool async)
    {
        await base.Left_join_predicate_value_equals_condition(async);

        AssertSql(
"""
SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOfBirthName], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], CASE
    WHEN [o].[Nickname] IS NOT NULL THEN N'Officer'
END AS [Discriminator]
FROM [Gears] AS [g]
LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
LEFT JOIN [Weapons] AS [w] ON [w].[SynergyWithId] IS NOT NULL
""");
    }

    public override async Task Left_join_predicate_value(bool async)
    {
        await base.Left_join_predicate_value(async);

        AssertSql(
"""
SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOfBirthName], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], CASE
    WHEN [o].[Nickname] IS NOT NULL THEN N'Officer'
END AS [Discriminator]
FROM [Gears] AS [g]
LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
LEFT JOIN [Weapons] AS [w] ON [g].[HasSoulPatch] = CAST(1 AS bit)
""");
    }

    public override async Task Left_join_predicate_condition_equals_condition(bool async)
    {
        await base.Left_join_predicate_condition_equals_condition(async);

        AssertSql(
"""
SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOfBirthName], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], CASE
    WHEN [o].[Nickname] IS NOT NULL THEN N'Officer'
END AS [Discriminator]
FROM [Gears] AS [g]
LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
LEFT JOIN [Weapons] AS [w] ON [w].[SynergyWithId] IS NOT NULL
""");
    }

    public override async Task Where_datetimeoffset_now(bool async)
    {
        await base.Where_datetimeoffset_now(async);

        AssertSql(
            """
SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Date`, `m`.`Difficulty`, `m`.`Duration`, `m`.`Rating`, `m`.`Time`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE `m`.`Timeline` <> NOW()
""");
    }

    public override async Task Where_datetimeoffset_utcnow(bool async)
    {
        var dtoffset = -1 * TimeZoneInfo.Local.BaseUtcOffset.TotalMinutes;
        await base.Where_datetimeoffset_utcnow(async);

        AssertSql(
            $"""
SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Date`, `m`.`Difficulty`, `m`.`Duration`, `m`.`Rating`, `m`.`Time`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE `m`.`Timeline` <> DATEADD('n', {dtoffset}.0, NOW())
""");
    }

    public override async Task Where_datetimeoffset_date_component(bool async)
    {
        //We have to specifically use 1 JAn 100 as that is the minimum in Jet.
        //The default 0 in DateTimeOffset is normally mapped to the OLE Automation date 30 Dec 1899
        //Investigate if we can pick the scenario up automatically and have a new DateTimeOffset().Date go to the required value if we have a comparison
        //await base.Where_datetimeoffset_date_component(async);
        await AssertQuery(
            async,
            ss => from m in ss.Set<Mission>()
                where m.Timeline.Date > new DateTimeOffset(100, 1, 1, 0, 0, 0, new TimeSpan()).Date
                select m);

        AssertSql(
            """
@__Date_0='0100-01-01T00:00:00.0000000' (DbType = DateTime)

SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Date`, `m`.`Difficulty`, `m`.`Duration`, `m`.`Rating`, `m`.`Time`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE DATEVALUE(`m`.`Timeline`) > CDATE(@__Date_0)
""");
    }

    public override async Task Where_datetimeoffset_year_component(bool async)
    {
        await AssertQuery(
            async,
            ss => from m in ss.Set<Mission>()
                  where m.Timeline.Year == 102
                  select m);

        AssertSql(
            """
SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Date`, `m`.`Difficulty`, `m`.`Duration`, `m`.`Rating`, `m`.`Time`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE DATEPART('yyyy', `m`.`Timeline`) = 102
""");
    }

    public override async Task Where_datetimeoffset_month_component(bool async)
    {
        await base.Where_datetimeoffset_month_component(async);

        AssertSql(
            """
SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Date`, `m`.`Difficulty`, `m`.`Duration`, `m`.`Rating`, `m`.`Time`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE DATEPART('m', `m`.`Timeline`) = 1
""");
    }

    public override async Task Where_datetimeoffset_dayofyear_component(bool async)
    {
        await base.Where_datetimeoffset_dayofyear_component(async);

        AssertSql(
            """
SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Date`, `m`.`Difficulty`, `m`.`Duration`, `m`.`Rating`, `m`.`Time`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE DATEPART('y', `m`.`Timeline`) = 2
""");
    }

    public override async Task Where_datetimeoffset_day_component(bool async)
    {
        await base.Where_datetimeoffset_day_component(async);

        AssertSql(
            """
SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Date`, `m`.`Difficulty`, `m`.`Duration`, `m`.`Rating`, `m`.`Time`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE DATEPART('d', `m`.`Timeline`) = 2
""");
    }

    public override async Task Where_datetimeoffset_hour_component(bool async)
    {
        await AssertQuery(
            async,
            ss => from m in ss.Set<Mission>()
                  where m.Timeline.Hour == 8
                  select m);

        AssertSql(
            """
SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Date`, `m`.`Difficulty`, `m`.`Duration`, `m`.`Rating`, `m`.`Time`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE DATEPART('h', `m`.`Timeline`) = 8
""");
    }

    public override async Task Where_datetimeoffset_minute_component(bool async)
    {
        await base.Where_datetimeoffset_minute_component(async);

        AssertSql(
            """
SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Date`, `m`.`Difficulty`, `m`.`Duration`, `m`.`Rating`, `m`.`Time`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE DATEPART('n', `m`.`Timeline`) = 0
""");
    }

    public override async Task Where_datetimeoffset_second_component(bool async)
    {
        await base.Where_datetimeoffset_second_component(async);

        AssertSql(
            """
SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Date`, `m`.`Difficulty`, `m`.`Duration`, `m`.`Rating`, `m`.`Time`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE DATEPART('s', `m`.`Timeline`) = 0
""");
    }

    public override async Task Where_datetimeoffset_millisecond_component(bool async)
    {
        await base.Where_datetimeoffset_millisecond_component(async);

        AssertSql(
"""
SELECT [m].[Id], [m].[CodeName], [m].[Duration], [m].[Rating], [m].[Timeline]
FROM [Missions] AS [m]
WHERE DATEPART(millisecond, [m].[Timeline]) = 0
""");
    }

    public override async Task DateTimeOffset_DateAdd_AddMonths(bool async)
    {
        await base.DateTimeOffset_DateAdd_AddMonths(async);

        AssertSql(
            """
SELECT DATEADD('m', 1, `m`.`Timeline`)
FROM `Missions` AS `m`
""");
    }

    public override async Task DateTimeOffset_DateAdd_AddDays(bool async)
    {
        await base.DateTimeOffset_DateAdd_AddDays(async);

        AssertSql(
            """
SELECT DATEADD('d', 1.0, `m`.`Timeline`)
FROM `Missions` AS `m`
""");
    }

    public override async Task DateTimeOffset_DateAdd_AddHours(bool async)
    {
        await base.DateTimeOffset_DateAdd_AddHours(async);

        AssertSql(
            """
SELECT DATEADD('h', 1.0, `m`.`Timeline`)
FROM `Missions` AS `m`
""");
    }

    public override async Task DateTimeOffset_DateAdd_AddMinutes(bool async)
    {
        await base.DateTimeOffset_DateAdd_AddMinutes(async);

        AssertSql(
            """
SELECT DATEADD('n', 1.0, `m`.`Timeline`)
FROM `Missions` AS `m`
""");
    }

    public override async Task DateTimeOffset_DateAdd_AddSeconds(bool async)
    {
        await base.DateTimeOffset_DateAdd_AddSeconds(async);

        AssertSql(
            """
SELECT DATEADD('s', 1.0, `m`.`Timeline`)
FROM `Missions` AS `m`
""");
    }

    public override async Task DateTimeOffset_DateAdd_AddMilliseconds(bool async)
    {
        await base.DateTimeOffset_DateAdd_AddMilliseconds(async);

        AssertSql(
"""
SELECT DATEADD(millisecond, CAST(300.0E0 AS int), [m].[Timeline])
FROM [Missions] AS [m]
""");
    }

    public override async Task Where_datetimeoffset_milliseconds_parameter_and_constant(bool async)
    {
        await base.Where_datetimeoffset_milliseconds_parameter_and_constant(async);

        AssertSql(
        """
SELECT COUNT(*)
FROM `Missions` AS `m`
WHERE `m`.`Timeline` = #1902-01-02 10:00:00#
""");
    }

    public override async Task Orderby_added_for_client_side_GroupJoin_composite_dependent_to_principal_LOJ_when_incomplete_key_is_used(
        bool async)
    {
        await base.Orderby_added_for_client_side_GroupJoin_composite_dependent_to_principal_LOJ_when_incomplete_key_is_used(async);

        AssertSql();
    }

    public override async Task Complex_predicate_with_AndAlso_and_nullable_bool_property(bool async)
    {
        await base.Complex_predicate_with_AndAlso_and_nullable_bool_property(async);

        AssertSql(
            """
SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
LEFT JOIN (
    SELECT `g`.`FullName`, `g`.`HasSoulPatch`
    FROM `Gears` AS `g`
) AS `s` ON `w`.`OwnerFullName` = `s`.`FullName`
WHERE `w`.`Id` <> 50 AND `s`.`HasSoulPatch` = FALSE
""");
    }

    public override async Task Distinct_with_optional_navigation_is_translated_to_sql(bool async)
    {
        await base.Distinct_with_optional_navigation_is_translated_to_sql(async);

        AssertSql(
"""
SELECT DISTINCT `g`.`HasSoulPatch`
FROM `Gears` AS `g`
LEFT JOIN `Tags` AS `t` ON `g`.`Nickname` = `t`.`GearNickName` AND `g`.`SquadId` = `t`.`GearSquadId`
WHERE `t`.`Note` <> 'Foo' OR `t`.`Note` IS NULL
""");
    }

    public override async Task Sum_with_optional_navigation_is_translated_to_sql(bool async)
    {
        await base.Sum_with_optional_navigation_is_translated_to_sql(async);

        AssertSql(
"""
SELECT IIF(SUM(`g`.`SquadId`) IS NULL, 0, SUM(`g`.`SquadId`))
FROM `Gears` AS `g`
LEFT JOIN `Tags` AS `t` ON `g`.`Nickname` = `t`.`GearNickName` AND `g`.`SquadId` = `t`.`GearSquadId`
WHERE `t`.`Note` <> 'Foo' OR `t`.`Note` IS NULL
""");
    }

    public override async Task Count_with_optional_navigation_is_translated_to_sql(bool async)
    {
        await base.Count_with_optional_navigation_is_translated_to_sql(async);

        AssertSql(
"""
SELECT COUNT(*)
FROM `Gears` AS `g`
LEFT JOIN `Tags` AS `t` ON `g`.`Nickname` = `t`.`GearNickName` AND `g`.`SquadId` = `t`.`GearSquadId`
WHERE `t`.`Note` <> 'Foo' OR `t`.`Note` IS NULL
""");
    }

    public override async Task FirstOrDefault_with_manually_created_groupjoin_is_translated_to_sql(bool async)
    {
        await base.FirstOrDefault_with_manually_created_groupjoin_is_translated_to_sql(async);

        AssertSql(
            """
SELECT TOP 1 `s`.`Id`, `s`.`Banner`, `s`.`Banner5`, `s`.`InternalNumber`, `s`.`Name`
FROM `Squads` AS `s`
LEFT JOIN (
    SELECT `g`.`SquadId`
    FROM `Gears` AS `g`
) AS `s0` ON `s`.`Id` = `s0`.`SquadId`
WHERE `s`.`Name` = 'Kilo'
""");
    }

    public override async Task Any_with_optional_navigation_as_subquery_predicate_is_translated_to_sql(bool async)
    {
        await base.Any_with_optional_navigation_as_subquery_predicate_is_translated_to_sql(async);

        AssertSql(
            """
SELECT `s`.`Name`
FROM `Squads` AS `s`
WHERE NOT EXISTS (
    SELECT 1
    FROM `Gears` AS `g`
    LEFT JOIN `Tags` AS `t` ON `g`.`Nickname` = `t`.`GearNickName` AND `g`.`SquadId` = `t`.`GearSquadId`
    WHERE `s`.`Id` = `g`.`SquadId` AND `t`.`Note` = 'Dom''s Tag')
""");
    }

    public override async Task All_with_optional_navigation_is_translated_to_sql(bool async)
    {
        await base.All_with_optional_navigation_is_translated_to_sql(async);

        AssertSql(
            """
SELECT IIF(NOT EXISTS (
        SELECT 1
        FROM `Gears` AS `g`
        LEFT JOIN `Tags` AS `t` ON `g`.`Nickname` = `t`.`GearNickName` AND `g`.`SquadId` = `t`.`GearSquadId`
        WHERE `t`.`Note` = 'Foo'), TRUE, FALSE)
FROM (SELECT COUNT(*) FROM `#Dual`)
""");
    }

    public override async Task Contains_with_local_nullable_guid_list_closure(bool async)
    {
        await base.Contains_with_local_nullable_guid_list_closure(async);

        AssertSql(
            """
SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`IssueDate`, `t`.`Note`
FROM `Tags` AS `t`
WHERE `t`.`Id` IN ('{df36f493-463f-4123-83f9-6b135deeb7ba}', '{23cbcf9b-ce14-45cf-aafa-2c2667ebfdd3}', '{ab1b82d7-88db-42bd-a132-7eef9aa68af4}')
""");
    }

    public override async Task Unnecessary_include_doesnt_get_added_complex_when_projecting_EF_Property(bool async)
    {
        await base.Unnecessary_include_doesnt_get_added_complex_when_projecting_EF_Property(async);

        AssertSql(
"""
SELECT `g`.`FullName`
FROM `Gears` AS `g`
WHERE `g`.`HasSoulPatch` = TRUE
ORDER BY `g`.`Rank`
""");
    }

    public override async Task Multiple_order_bys_are_properly_lifted_from_subquery_created_by_include(bool async)
    {
        await base.Multiple_order_bys_are_properly_lifted_from_subquery_created_by_include(async);

        AssertSql(
            """
SELECT `g`.`FullName`
FROM `Gears` AS `g`
WHERE `g`.`HasSoulPatch` = FALSE
ORDER BY `g`.`FullName`
""");
    }

    public override async Task Order_by_is_properly_lifted_from_subquery_with_same_order_by_in_the_outer_query(bool async)
    {
        await base.Order_by_is_properly_lifted_from_subquery_with_same_order_by_in_the_outer_query(async);

        AssertSql(
            """
SELECT `g`.`FullName`
FROM `Gears` AS `g`
WHERE `g`.`HasSoulPatch` = FALSE
ORDER BY `g`.`FullName`
""");
    }

    public override async Task Where_is_properly_lifted_from_subquery_created_by_include(bool async)
    {
        await base.Where_is_properly_lifted_from_subquery_created_by_include(async);

        AssertSql(
            """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`IssueDate`, `t`.`Note`
FROM (`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN `Tags` AS `t` ON `g`.`Nickname` = `t`.`GearNickName` AND `g`.`SquadId` = `t`.`GearSquadId`
WHERE `g`.`FullName` <> 'Augustus Cole' AND `g`.`HasSoulPatch` = FALSE
ORDER BY `g`.`FullName`
""");
    }

    public override async Task Subquery_is_lifted_from_main_from_clause_of_SelectMany(bool async)
    {
        await base.Subquery_is_lifted_from_main_from_clause_of_SelectMany(async);

        AssertSql(
            """
SELECT `g`.`FullName` AS `Name1`, `s`.`FullName` AS `Name2`
FROM `Gears` AS `g`,
(
    SELECT `g0`.`FullName`, `g0`.`HasSoulPatch`
    FROM `Gears` AS `g0`
) AS `s`
WHERE `g`.`HasSoulPatch` = TRUE AND `s`.`HasSoulPatch` = FALSE
ORDER BY `g`.`FullName`
""");
    }

    public override async Task Subquery_containing_SelectMany_projecting_main_from_clause_gets_lifted(bool async)
    {
        await base.Subquery_containing_SelectMany_projecting_main_from_clause_gets_lifted(async);

        AssertSql(
"""
SELECT `g`.`FullName`
FROM `Gears` AS `g`,
`Tags` AS `t`
WHERE `g`.`HasSoulPatch` = TRUE
ORDER BY `g`.`FullName`
""");
    }

    public override async Task Subquery_containing_join_projecting_main_from_clause_gets_lifted(bool async)
    {
        await base.Subquery_containing_join_projecting_main_from_clause_gets_lifted(async);

        AssertSql(
"""
SELECT `g`.`Nickname`
FROM `Gears` AS `g`
INNER JOIN `Tags` AS `t` ON `g`.`Nickname` = `t`.`GearNickName`
ORDER BY `g`.`Nickname`
""");
    }

    public override async Task Subquery_containing_left_join_projecting_main_from_clause_gets_lifted(bool async)
    {
        await base.Subquery_containing_left_join_projecting_main_from_clause_gets_lifted(async);

        AssertSql(
"""
SELECT `g`.`Nickname`
FROM `Gears` AS `g`
LEFT JOIN `Tags` AS `t` ON `g`.`Nickname` = `t`.`GearNickName`
ORDER BY `g`.`Nickname`
""");
    }

    public override async Task Subquery_containing_join_gets_lifted_clashing_names(bool async)
    {
        await base.Subquery_containing_join_gets_lifted_clashing_names(async);

        AssertSql(
"""
SELECT `g`.`Nickname`
FROM (`Gears` AS `g`
INNER JOIN `Tags` AS `t` ON `g`.`Nickname` = `t`.`GearNickName`)
INNER JOIN `Tags` AS `t0` ON `g`.`Nickname` = `t0`.`GearNickName`
WHERE `t`.`GearNickName` <> 'Cole Train' OR `t`.`GearNickName` IS NULL
ORDER BY `g`.`Nickname`, `t0`.`Id`
""");
    }

    public override async Task Subquery_created_by_include_gets_lifted_nested(bool async)
    {
        await base.Subquery_created_by_include_gets_lifted_nested(async);

        AssertSql(
            """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `c`.`Name`, `c`.`Location`, `c`.`Nation`
FROM (`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
INNER JOIN `Cities` AS `c` ON `g`.`CityOfBirthName` = `c`.`Name`
WHERE EXISTS (
    SELECT 1
    FROM `Weapons` AS `w`
    WHERE `g`.`FullName` = `w`.`OwnerFullName`) AND `g`.`HasSoulPatch` = FALSE
ORDER BY `g`.`Nickname`
""");
    }

    public override async Task Subquery_is_lifted_from_additional_from_clause(bool async)
    {
        await base.Subquery_is_lifted_from_additional_from_clause(async);

        AssertSql(
            """
SELECT `g`.`FullName` AS `Name1`, `s`.`FullName` AS `Name2`
FROM `Gears` AS `g`,
(
    SELECT `g0`.`FullName`, `g0`.`HasSoulPatch`
    FROM `Gears` AS `g0`
) AS `s`
WHERE `g`.`HasSoulPatch` = TRUE AND `s`.`HasSoulPatch` = FALSE
ORDER BY `g`.`FullName`
""");
    }

    public override async Task Subquery_with_result_operator_is_not_lifted(bool async)
    {
        await base.Subquery_with_result_operator_is_not_lifted(async);

        AssertSql(
            """
SELECT `s`.`FullName`
FROM (
    SELECT TOP 2 `g`.`FullName`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`HasSoulPatch` = FALSE
    ORDER BY `g`.`FullName`
) AS `s`
ORDER BY `s`.`Rank`
""");
    }

    public override async Task Skip_with_orderby_followed_by_orderBy_is_pushed_down(bool async)
    {
        await base.Skip_with_orderby_followed_by_orderBy_is_pushed_down(async);

        AssertSql(
"""
@__p_0='1'

SELECT [t].[FullName]
FROM (
    SELECT [g].[FullName], [g].[Rank]
    FROM [Gears] AS [g]
    WHERE [g].[HasSoulPatch] = CAST(0 AS bit)
    ORDER BY [g].[FullName]
    OFFSET @__p_0 ROWS
) AS [t]
ORDER BY [t].[Rank]
""");
    }

    public override async Task Take_without_orderby_followed_by_orderBy_is_pushed_down1(bool async)
    {
        await base.Take_without_orderby_followed_by_orderBy_is_pushed_down1(async);

        AssertSql(
"""
@__p_0='999'

SELECT [t].[FullName]
FROM (
    SELECT TOP(@__p_0) [g].[FullName], [g].[Rank]
    FROM [Gears] AS [g]
    WHERE [g].[HasSoulPatch] = CAST(0 AS bit)
) AS [t]
ORDER BY [t].[Rank]
""");
    }

    public override async Task Take_without_orderby_followed_by_orderBy_is_pushed_down2(bool async)
    {
        await base.Take_without_orderby_followed_by_orderBy_is_pushed_down2(async);

        AssertSql(
"""
SELECT `t`.`FullName`
FROM (
    SELECT TOP 999 `g`.`FullName`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`HasSoulPatch` <> TRUE
) AS `t`
ORDER BY `t`.`FullName`, `t`.`Rank`
""");
    }

    public override async Task Take_without_orderby_followed_by_orderBy_is_pushed_down3(bool async)
    {
        await base.Take_without_orderby_followed_by_orderBy_is_pushed_down3(async);

        AssertSql(
            """
SELECT `s`.`FullName`
FROM (
    SELECT TOP 999 `g`.`FullName`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`HasSoulPatch` = FALSE
) AS `s`
ORDER BY `s`.`FullName`, `s`.`Rank`
""");
    }

    public override async Task Select_length_of_string_property(bool async)
    {
        await base.Select_length_of_string_property(async);

        AssertSql(
"""
SELECT `w`.`Name`, IIF(LEN(`w`.`Name`) IS NULL, NULL, CLNG(LEN(`w`.`Name`))) AS `Length`
FROM `Weapons` AS `w`
""");
    }

    public override async Task Client_method_on_collection_navigation_in_outer_join_key(bool async)
    {
        await base.Client_method_on_collection_navigation_in_outer_join_key(async);

        AssertSql();
    }

    public override async Task Member_access_on_derived_entity_using_cast(bool async)
    {
        await base.Member_access_on_derived_entity_using_cast(async);

        AssertSql(
"""
SELECT `f`.`Name`, `l`.`Eradicated`
FROM `Factions` AS `f`
LEFT JOIN `LocustHordes` AS `l` ON `f`.`Id` = `l`.`Id`
WHERE `l`.`Id` IS NOT NULL
ORDER BY `f`.`Name`
""");
    }

    public override async Task Member_access_on_derived_materialized_entity_using_cast(bool async)
    {
        await base.Member_access_on_derived_materialized_entity_using_cast(async);

        AssertSql(
"""
SELECT `f`.`Id`, `f`.`CapitalName`, `f`.`Name`, `f`.`ServerAddress`, `l`.`CommanderName`, `l`.`Eradicated`, IIF(`l`.`Id` IS NOT NULL, 'LocustHorde', NULL) AS `Discriminator`
FROM `Factions` AS `f`
LEFT JOIN `LocustHordes` AS `l` ON `f`.`Id` = `l`.`Id`
WHERE `l`.`Id` IS NOT NULL
ORDER BY `f`.`Name`
""");
    }

    public override async Task Member_access_on_derived_entity_using_cast_and_let(bool async)
    {
        await base.Member_access_on_derived_entity_using_cast_and_let(async);

        AssertSql(
        """
SELECT `f`.`Name`, `l`.`Eradicated`
FROM `Factions` AS `f`
LEFT JOIN `LocustHordes` AS `l` ON `f`.`Id` = `l`.`Id`
WHERE `l`.`Id` IS NOT NULL
ORDER BY `f`.`Name`
""");
    }

    public override async Task Property_access_on_derived_entity_using_cast(bool async)
    {
        await base.Property_access_on_derived_entity_using_cast(async);

        AssertSql(
"""
SELECT `f`.`Name`, `l`.`Eradicated`
FROM `Factions` AS `f`
LEFT JOIN `LocustHordes` AS `l` ON `f`.`Id` = `l`.`Id`
WHERE `l`.`Id` IS NOT NULL
ORDER BY `f`.`Name`
""");
    }

    public override async Task Navigation_access_on_derived_entity_using_cast(bool async)
    {
        await base.Navigation_access_on_derived_entity_using_cast(async);

        AssertSql(
            """
SELECT `f`.`Name`, `s`.`ThreatLevel` AS `Threat`
FROM (`Factions` AS `f`
LEFT JOIN `LocustHordes` AS `l` ON `f`.`Id` = `l`.`Id`)
LEFT JOIN (
    SELECT `l0`.`Name`, `l0`.`ThreatLevel`
    FROM `LocustLeaders` AS `l0`
    INNER JOIN `LocustCommanders` AS `l1` ON `l0`.`Name` = `l1`.`Name`
) AS `s` ON `l`.`CommanderName` = `s`.`Name`
WHERE `l`.`Id` IS NOT NULL
ORDER BY `f`.`Name`
""");
    }

    public override async Task Navigation_access_on_derived_materialized_entity_using_cast(bool async)
    {
        await base.Navigation_access_on_derived_materialized_entity_using_cast(async);

        AssertSql(
            """
SELECT `f`.`Id`, `f`.`CapitalName`, `f`.`Name`, `f`.`ServerAddress`, `l`.`CommanderName`, `l`.`Eradicated`, IIF(`l`.`Id` IS NOT NULL, 'LocustHorde', NULL) AS `Discriminator`, `s`.`ThreatLevel` AS `Threat`
FROM (`Factions` AS `f`
LEFT JOIN `LocustHordes` AS `l` ON `f`.`Id` = `l`.`Id`)
LEFT JOIN (
    SELECT `l0`.`Name`, `l0`.`ThreatLevel`
    FROM `LocustLeaders` AS `l0`
    INNER JOIN `LocustCommanders` AS `l1` ON `l0`.`Name` = `l1`.`Name`
) AS `s` ON `l`.`CommanderName` = `s`.`Name`
WHERE `l`.`Id` IS NOT NULL
ORDER BY `f`.`Name`
""");
    }

    public override async Task Navigation_access_via_EFProperty_on_derived_entity_using_cast(bool async)
    {
        await base.Navigation_access_via_EFProperty_on_derived_entity_using_cast(async);

        AssertSql(
            """
SELECT `f`.`Name`, `s`.`ThreatLevel` AS `Threat`
FROM (`Factions` AS `f`
LEFT JOIN `LocustHordes` AS `l` ON `f`.`Id` = `l`.`Id`)
LEFT JOIN (
    SELECT `l0`.`Name`, `l0`.`ThreatLevel`
    FROM `LocustLeaders` AS `l0`
    INNER JOIN `LocustCommanders` AS `l1` ON `l0`.`Name` = `l1`.`Name`
) AS `s` ON `l`.`CommanderName` = `s`.`Name`
WHERE `l`.`Id` IS NOT NULL
ORDER BY `f`.`Name`
""");
    }

    public override async Task Navigation_access_fk_on_derived_entity_using_cast(bool async)
    {
        await base.Navigation_access_fk_on_derived_entity_using_cast(async);

        AssertSql(
            """
SELECT `f`.`Name`, `s`.`Name` AS `CommanderName`
FROM (`Factions` AS `f`
LEFT JOIN `LocustHordes` AS `l` ON `f`.`Id` = `l`.`Id`)
LEFT JOIN (
    SELECT `l0`.`Name`
    FROM `LocustLeaders` AS `l0`
    INNER JOIN `LocustCommanders` AS `l1` ON `l0`.`Name` = `l1`.`Name`
) AS `s` ON `l`.`CommanderName` = `s`.`Name`
WHERE `l`.`Id` IS NOT NULL
ORDER BY `f`.`Name`
""");
    }

    public override async Task Collection_navigation_access_on_derived_entity_using_cast(bool async)
    {
        await base.Collection_navigation_access_on_derived_entity_using_cast(async);

        AssertSql(
            """
SELECT `f`.`Name`, (
    SELECT COUNT(*)
    FROM `LocustLeaders` AS `l0`
    WHERE `f`.`Id` = `l0`.`LocustHordeId`) AS `LeadersCount`
FROM `Factions` AS `f`
LEFT JOIN `LocustHordes` AS `l` ON `f`.`Id` = `l`.`Id`
WHERE `l`.`Id` IS NOT NULL
ORDER BY `f`.`Name`
""");
    }

    public override async Task Collection_navigation_access_on_derived_entity_using_cast_in_SelectMany(bool async)
    {
        await base.Collection_navigation_access_on_derived_entity_using_cast_in_SelectMany(async);
AssertSql(
    """
SELECT `f`.`Name`, `s`.`Name` AS `LeaderName`
FROM (`Factions` AS `f`
LEFT JOIN `LocustHordes` AS `l` ON `f`.`Id` = `l`.`Id`)
INNER JOIN (
    SELECT `l0`.`Name`, `l0`.`LocustHordeId`
    FROM `LocustLeaders` AS `l0`
) AS `s` ON `f`.`Id` = `s`.`LocustHordeId`
WHERE `l`.`Id` IS NOT NULL
ORDER BY `s`.`Name`
""");
    }

    public override async Task Include_on_derived_entity_using_OfType(bool async)
    {
        await base.Include_on_derived_entity_using_OfType(async);
AssertSql(
    """
SELECT `f`.`Id`, `f`.`CapitalName`, `f`.`Name`, `f`.`ServerAddress`, `l`.`CommanderName`, `l`.`Eradicated`, IIF(`l`.`Id` IS NOT NULL, 'LocustHorde', NULL) AS `Discriminator`, `s`.`Name`, `s`.`LocustHordeId`, `s`.`ThreatLevel`, `s`.`ThreatLevelByte`, `s`.`ThreatLevelNullableByte`, `s`.`DefeatedByNickname`, `s`.`DefeatedBySquadId`, `s`.`HighCommandId`, `s0`.`Name`, `s0`.`LocustHordeId`, `s0`.`ThreatLevel`, `s0`.`ThreatLevelByte`, `s0`.`ThreatLevelNullableByte`, `s0`.`DefeatedByNickname`, `s0`.`DefeatedBySquadId`, `s0`.`HighCommandId`, `s0`.`Discriminator`
FROM ((`Factions` AS `f`
LEFT JOIN `LocustHordes` AS `l` ON `f`.`Id` = `l`.`Id`)
LEFT JOIN (
    SELECT `l0`.`Name`, `l0`.`LocustHordeId`, `l0`.`ThreatLevel`, `l0`.`ThreatLevelByte`, `l0`.`ThreatLevelNullableByte`, `l1`.`DefeatedByNickname`, `l1`.`DefeatedBySquadId`, `l1`.`HighCommandId`
    FROM `LocustLeaders` AS `l0`
    INNER JOIN `LocustCommanders` AS `l1` ON `l0`.`Name` = `l1`.`Name`
) AS `s` ON `l`.`CommanderName` = `s`.`Name`)
LEFT JOIN (
    SELECT `l2`.`Name`, `l2`.`LocustHordeId`, `l2`.`ThreatLevel`, `l2`.`ThreatLevelByte`, `l2`.`ThreatLevelNullableByte`, `l3`.`DefeatedByNickname`, `l3`.`DefeatedBySquadId`, `l3`.`HighCommandId`, IIF(`l3`.`Name` IS NOT NULL, 'LocustCommander', NULL) AS `Discriminator`
    FROM `LocustLeaders` AS `l2`
    LEFT JOIN `LocustCommanders` AS `l3` ON `l2`.`Name` = `l3`.`Name`
) AS `s0` ON `f`.`Id` = `s0`.`LocustHordeId`
WHERE `l`.`Id` IS NOT NULL
ORDER BY `f`.`Name`, `f`.`Id`, `s`.`Name`
""");
    }

    public override async Task Distinct_on_subquery_doesnt_get_lifted(bool async)
    {
        await base.Distinct_on_subquery_doesnt_get_lifted(async);
AssertSql(
    """
SELECT `s`.`HasSoulPatch`
FROM (
    SELECT DISTINCT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
) AS `s`
""");
    }

    public override async Task Cast_result_operator_on_subquery_is_properly_lifted_to_a_convert(bool async)
    {
        await base.Cast_result_operator_on_subquery_is_properly_lifted_to_a_convert(async);

        AssertSql(
"""
SELECT `l`.`Eradicated`
FROM `Factions` AS `f`
LEFT JOIN `LocustHordes` AS `l` ON `f`.`Id` = `l`.`Id`
""");
    }

    public override async Task Comparing_two_collection_navigations_composite_key(bool async)
    {
        await base.Comparing_two_collection_navigations_composite_key(async);
AssertSql(
    """
SELECT `g`.`Nickname` AS `Nickname1`, `s`.`Nickname` AS `Nickname2`
FROM `Gears` AS `g`,
(
    SELECT `g0`.`Nickname`, `g0`.`SquadId`
    FROM `Gears` AS `g0`
) AS `s`
WHERE `g`.`Nickname` = `s`.`Nickname` AND `g`.`SquadId` = `s`.`SquadId`
ORDER BY `g`.`Nickname`
""");
    }

    public override async Task Comparing_two_collection_navigations_inheritance(bool async)
    {
        await base.Comparing_two_collection_navigations_inheritance(async);

        AssertSql(
"""
SELECT [f].[Name], [t].[Nickname]
FROM [Factions] AS [f]
LEFT JOIN [LocustHordes] AS [l] ON [f].[Id] = [l].[Id]
CROSS JOIN (
    SELECT [g].[Nickname], [g].[SquadId], [g].[HasSoulPatch]
    FROM [Gears] AS [g]
    LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
    WHERE [o].[Nickname] IS NOT NULL
) AS [t]
LEFT JOIN (
    SELECT [l0].[Name], [l1].[DefeatedByNickname], [l1].[DefeatedBySquadId]
    FROM [LocustLeaders] AS [l0]
    INNER JOIN [LocustCommanders] AS [l1] ON [l0].[Name] = [l1].[Name]
) AS [t0] ON [l].[CommanderName] = [t0].[Name]
LEFT JOIN (
    SELECT [g0].[Nickname], [g0].[SquadId]
    FROM [Gears] AS [g0]
) AS [t1] ON [t0].[DefeatedByNickname] = [t1].[Nickname] AND [t0].[DefeatedBySquadId] = [t1].[SquadId]
WHERE ([l].[Id] IS NOT NULL) AND [t].[HasSoulPatch] = CAST(1 AS bit) AND [t1].[Nickname] = [t].[Nickname] AND [t1].[SquadId] = [t].[SquadId]
""");
    }

    public override async Task Comparing_entities_using_Equals_inheritance(bool async)
    {
        await base.Comparing_entities_using_Equals_inheritance(async);
AssertSql(
    """
SELECT `g`.`Nickname` AS `Nickname1`, `s`.`Nickname` AS `Nickname2`
FROM `Gears` AS `g`,
(
    SELECT `g0`.`Nickname`, `g0`.`SquadId`
    FROM `Gears` AS `g0`
    LEFT JOIN `Officers` AS `o` ON `g0`.`Nickname` = `o`.`Nickname` AND `g0`.`SquadId` = `o`.`SquadId`
    WHERE `o`.`Nickname` IS NOT NULL
) AS `s`
WHERE `g`.`Nickname` = `s`.`Nickname` AND `g`.`SquadId` = `s`.`SquadId`
ORDER BY `g`.`Nickname`, `s`.`Nickname`
""");
    }

    public override async Task Contains_on_nullable_array_produces_correct_sql(bool async)
    {
        await base.Contains_on_nullable_array_produces_correct_sql(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM (`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN `Cities` AS `c` ON `g`.`AssignedCityName` = `c`.`Name`
WHERE `g`.`SquadId` < 2 AND (`c`.`Name` IS NULL OR `c`.`Name` = 'Ephyra')
""");
    }

    public override async Task Optional_navigation_with_collection_composite_key(bool async)
    {
        await base.Optional_navigation_with_collection_composite_key(async);

        AssertSql(
    """
SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`IssueDate`, `t`.`Note`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
WHERE `s`.`Discriminator` = 'Officer' AND (
    SELECT COUNT(*)
    FROM `Gears` AS `g0`
    WHERE `s`.`Nickname` IS NOT NULL AND `s`.`SquadId` IS NOT NULL AND `s`.`Nickname` = `g0`.`LeaderNickname` AND `s`.`SquadId` = `g0`.`LeaderSquadId` AND `g0`.`Nickname` = 'Dom') > 0
""");
    }

    public override async Task Select_null_conditional_with_inheritance(bool async)
    {
        await base.Select_null_conditional_with_inheritance(async);

        AssertSql(
"""
SELECT IIF(`l`.`CommanderName` IS NOT NULL, `l`.`CommanderName`, NULL)
FROM `Factions` AS `f`
LEFT JOIN `LocustHordes` AS `l` ON `f`.`Id` = `l`.`Id`
WHERE `l`.`Id` IS NOT NULL
""");
    }

    public override async Task Select_null_conditional_with_inheritance_negative(bool async)
    {
        await base.Select_null_conditional_with_inheritance_negative(async);

        AssertSql(
"""
SELECT IIF(`l`.`CommanderName` IS NOT NULL, `l`.`Eradicated`, NULL)
FROM `Factions` AS `f`
LEFT JOIN `LocustHordes` AS `l` ON `f`.`Id` = `l`.`Id`
WHERE `l`.`Id` IS NOT NULL
""");
    }

    public override async Task Project_collection_navigation_with_inheritance1(bool async)
    {
        await base.Project_collection_navigation_with_inheritance1(async);

        AssertSql(
            """
SELECT `f`.`Id`, `s`.`Name`, `s0`.`Id`, `s1`.`Name`, `s1`.`LocustHordeId`, `s1`.`ThreatLevel`, `s1`.`ThreatLevelByte`, `s1`.`ThreatLevelNullableByte`, `s1`.`DefeatedByNickname`, `s1`.`DefeatedBySquadId`, `s1`.`HighCommandId`, `s1`.`Discriminator`
FROM (((`Factions` AS `f`
LEFT JOIN `LocustHordes` AS `l` ON `f`.`Id` = `l`.`Id`)
LEFT JOIN (
    SELECT `l0`.`Name`
    FROM `LocustLeaders` AS `l0`
    INNER JOIN `LocustCommanders` AS `l1` ON `l0`.`Name` = `l1`.`Name`
) AS `s` ON `l`.`CommanderName` = `s`.`Name`)
LEFT JOIN (
    SELECT `f0`.`Id`, `l2`.`CommanderName`
    FROM `Factions` AS `f0`
    INNER JOIN `LocustHordes` AS `l2` ON `f0`.`Id` = `l2`.`Id`
) AS `s0` ON `s`.`Name` = `s0`.`CommanderName`)
LEFT JOIN (
    SELECT `l3`.`Name`, `l3`.`LocustHordeId`, `l3`.`ThreatLevel`, `l3`.`ThreatLevelByte`, `l3`.`ThreatLevelNullableByte`, `l4`.`DefeatedByNickname`, `l4`.`DefeatedBySquadId`, `l4`.`HighCommandId`, IIF(`l4`.`Name` IS NOT NULL, 'LocustCommander', NULL) AS `Discriminator`
    FROM `LocustLeaders` AS `l3`
    LEFT JOIN `LocustCommanders` AS `l4` ON `l3`.`Name` = `l4`.`Name`
) AS `s1` ON `s0`.`Id` = `s1`.`LocustHordeId`
WHERE `l`.`Id` IS NOT NULL
ORDER BY `f`.`Id`, `s`.`Name`, `s0`.`Id`
""");
    }

    public override async Task Project_collection_navigation_with_inheritance2(bool async)
    {
        await base.Project_collection_navigation_with_inheritance2(async);

        AssertSql(
            """
SELECT `f`.`Id`, `s`.`Name`, `s0`.`Nickname`, `s0`.`SquadId`, `s1`.`Nickname`, `s1`.`SquadId`, `s1`.`AssignedCityName`, `s1`.`CityOfBirthName`, `s1`.`FullName`, `s1`.`HasSoulPatch`, `s1`.`LeaderNickname`, `s1`.`LeaderSquadId`, `s1`.`Rank`, `s1`.`Discriminator`
FROM (((`Factions` AS `f`
LEFT JOIN `LocustHordes` AS `l` ON `f`.`Id` = `l`.`Id`)
LEFT JOIN (
    SELECT `l0`.`Name`, `l1`.`DefeatedByNickname`, `l1`.`DefeatedBySquadId`
    FROM `LocustLeaders` AS `l0`
    INNER JOIN `LocustCommanders` AS `l1` ON `l0`.`Name` = `l1`.`Name`
) AS `s` ON `l`.`CommanderName` = `s`.`Name`)
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`
    FROM `Gears` AS `g`
) AS `s0` ON `s`.`DefeatedByNickname` = `s0`.`Nickname` AND `s`.`DefeatedBySquadId` = `s0`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g0`
    LEFT JOIN `Officers` AS `o` ON `g0`.`Nickname` = `o`.`Nickname` AND `g0`.`SquadId` = `o`.`SquadId`
) AS `s1` ON (`s0`.`Nickname` = `s1`.`LeaderNickname` OR (`s0`.`Nickname` IS NULL AND `s1`.`LeaderNickname` IS NULL)) AND `s0`.`SquadId` = `s1`.`LeaderSquadId`
WHERE `l`.`Id` IS NOT NULL
ORDER BY `f`.`Id`, `s`.`Name`, `s0`.`Nickname`, `s0`.`SquadId`, `s1`.`Nickname`
""");
    }

    public override async Task Project_collection_navigation_with_inheritance3(bool async)
    {
        await base.Project_collection_navigation_with_inheritance3(async);

        AssertSql(
            """
SELECT `f`.`Id`, `s`.`Name`, `s0`.`Nickname`, `s0`.`SquadId`, `s1`.`Nickname`, `s1`.`SquadId`, `s1`.`AssignedCityName`, `s1`.`CityOfBirthName`, `s1`.`FullName`, `s1`.`HasSoulPatch`, `s1`.`LeaderNickname`, `s1`.`LeaderSquadId`, `s1`.`Rank`, `s1`.`Discriminator`
FROM (((`Factions` AS `f`
LEFT JOIN `LocustHordes` AS `l` ON `f`.`Id` = `l`.`Id`)
LEFT JOIN (
    SELECT `l0`.`Name`, `l1`.`DefeatedByNickname`, `l1`.`DefeatedBySquadId`
    FROM `LocustLeaders` AS `l0`
    INNER JOIN `LocustCommanders` AS `l1` ON `l0`.`Name` = `l1`.`Name`
) AS `s` ON `l`.`CommanderName` = `s`.`Name`)
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`
    FROM `Gears` AS `g`
) AS `s0` ON `s`.`DefeatedByNickname` = `s0`.`Nickname` AND `s`.`DefeatedBySquadId` = `s0`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g0`
    LEFT JOIN `Officers` AS `o` ON `g0`.`Nickname` = `o`.`Nickname` AND `g0`.`SquadId` = `o`.`SquadId`
) AS `s1` ON (`s0`.`Nickname` = `s1`.`LeaderNickname` OR (`s0`.`Nickname` IS NULL AND `s1`.`LeaderNickname` IS NULL)) AND `s0`.`SquadId` = `s1`.`LeaderSquadId`
WHERE `l`.`Id` IS NOT NULL
ORDER BY `f`.`Id`, `s`.`Name`, `s0`.`Nickname`, `s0`.`SquadId`, `s1`.`Nickname`
""");
    }

    public override async Task Include_reference_on_derived_type_using_string(bool async)
    {
        await base.Include_reference_on_derived_type_using_string(async);

        AssertSql(
    """
SELECT `l`.`Name`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`ThreatLevelByte`, `l`.`ThreatLevelNullableByte`, `l0`.`DefeatedByNickname`, `l0`.`DefeatedBySquadId`, `l0`.`HighCommandId`, IIF(`l0`.`Name` IS NOT NULL, 'LocustCommander', NULL) AS `Discriminator`, `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`
FROM (`LocustLeaders` AS `l`
LEFT JOIN `LocustCommanders` AS `l0` ON `l`.`Name` = `l0`.`Name`)
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
) AS `s` ON `l0`.`DefeatedByNickname` = `s`.`Nickname` AND `l0`.`DefeatedBySquadId` = `s`.`SquadId`
""");
    }

    public override async Task Include_reference_on_derived_type_using_string_nested1(bool async)
    {
        await base.Include_reference_on_derived_type_using_string_nested1(async);

        AssertSql(
    """
SELECT `l`.`Name`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`ThreatLevelByte`, `l`.`ThreatLevelNullableByte`, `l0`.`DefeatedByNickname`, `l0`.`DefeatedBySquadId`, `l0`.`HighCommandId`, IIF(`l0`.`Name` IS NOT NULL, 'LocustCommander', NULL) AS `Discriminator`, `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`, `s0`.`Id`, `s0`.`Banner`, `s0`.`Banner5`, `s0`.`InternalNumber`, `s0`.`Name`
FROM ((`LocustLeaders` AS `l`
LEFT JOIN `LocustCommanders` AS `l0` ON `l`.`Name` = `l0`.`Name`)
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
) AS `s` ON `l0`.`DefeatedByNickname` = `s`.`Nickname` AND `l0`.`DefeatedBySquadId` = `s`.`SquadId`)
LEFT JOIN `Squads` AS `s0` ON `s`.`SquadId` = `s0`.`Id`
""");
    }

    public override async Task Include_reference_on_derived_type_using_string_nested2(bool async)
    {
        await base.Include_reference_on_derived_type_using_string_nested2(async); 
        
        AssertSql(
    """
SELECT `l`.`Name`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`ThreatLevelByte`, `l`.`ThreatLevelNullableByte`, `l0`.`DefeatedByNickname`, `l0`.`DefeatedBySquadId`, `l0`.`HighCommandId`, IIF(`l0`.`Name` IS NOT NULL, 'LocustCommander', NULL) AS `Discriminator`, `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`, `s0`.`Nickname`, `s0`.`SquadId`, `s0`.`AssignedCityName`, `s0`.`CityOfBirthName`, `s0`.`FullName`, `s0`.`HasSoulPatch`, `s0`.`LeaderNickname`, `s0`.`LeaderSquadId`, `s0`.`Rank`, `s0`.`Discriminator`, `s0`.`Name`, `s0`.`Location`, `s0`.`Nation`
FROM ((`LocustLeaders` AS `l`
LEFT JOIN `LocustCommanders` AS `l0` ON `l`.`Name` = `l0`.`Name`)
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
) AS `s` ON `l0`.`DefeatedByNickname` = `s`.`Nickname` AND `l0`.`DefeatedBySquadId` = `s`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `c`.`Name`, `c`.`Location`, `c`.`Nation`
    FROM (`Gears` AS `g0`
    LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`)
    INNER JOIN `Cities` AS `c` ON `g0`.`CityOfBirthName` = `c`.`Name`
) AS `s0` ON (`s`.`Nickname` = `s0`.`LeaderNickname` OR (`s`.`Nickname` IS NULL AND `s0`.`LeaderNickname` IS NULL)) AND `s`.`SquadId` = `s0`.`LeaderSquadId`
ORDER BY `l`.`Name`, `s`.`Nickname`, `s`.`SquadId`, `s0`.`Nickname`, `s0`.`SquadId`
""");
    }

    public override async Task Include_reference_on_derived_type_using_lambda(bool async)
    {
        await base.Include_reference_on_derived_type_using_lambda(async);

        AssertSql(
    """
SELECT `l`.`Name`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`ThreatLevelByte`, `l`.`ThreatLevelNullableByte`, `l0`.`DefeatedByNickname`, `l0`.`DefeatedBySquadId`, `l0`.`HighCommandId`, IIF(`l0`.`Name` IS NOT NULL, 'LocustCommander', NULL) AS `Discriminator`, `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`
FROM (`LocustLeaders` AS `l`
LEFT JOIN `LocustCommanders` AS `l0` ON `l`.`Name` = `l0`.`Name`)
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
) AS `s` ON `l0`.`DefeatedByNickname` = `s`.`Nickname` AND `l0`.`DefeatedBySquadId` = `s`.`SquadId`
""");
    }

    public override async Task Include_reference_on_derived_type_using_lambda_with_soft_cast(bool async)
    {
        await base.Include_reference_on_derived_type_using_lambda_with_soft_cast(async);

        AssertSql(
    """
SELECT `l`.`Name`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`ThreatLevelByte`, `l`.`ThreatLevelNullableByte`, `l0`.`DefeatedByNickname`, `l0`.`DefeatedBySquadId`, `l0`.`HighCommandId`, IIF(`l0`.`Name` IS NOT NULL, 'LocustCommander', NULL) AS `Discriminator`, `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`
FROM (`LocustLeaders` AS `l`
LEFT JOIN `LocustCommanders` AS `l0` ON `l`.`Name` = `l0`.`Name`)
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
) AS `s` ON `l0`.`DefeatedByNickname` = `s`.`Nickname` AND `l0`.`DefeatedBySquadId` = `s`.`SquadId`
""");
    }

    public override async Task Include_reference_on_derived_type_using_lambda_with_tracking(bool async)
    {
        await base.Include_reference_on_derived_type_using_lambda_with_tracking(async);

        AssertSql(
    """
SELECT `l`.`Name`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`ThreatLevelByte`, `l`.`ThreatLevelNullableByte`, `l0`.`DefeatedByNickname`, `l0`.`DefeatedBySquadId`, `l0`.`HighCommandId`, IIF(`l0`.`Name` IS NOT NULL, 'LocustCommander', NULL) AS `Discriminator`, `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`
FROM (`LocustLeaders` AS `l`
LEFT JOIN `LocustCommanders` AS `l0` ON `l`.`Name` = `l0`.`Name`)
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
) AS `s` ON `l0`.`DefeatedByNickname` = `s`.`Nickname` AND `l0`.`DefeatedBySquadId` = `s`.`SquadId`
""");
    }

    public override async Task Include_collection_on_derived_type_using_string(bool async)
    {
        await base.Include_collection_on_derived_type_using_string(async);

        AssertSql(
    """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`
FROM (`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g0`
    LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`
) AS `s` ON `g`.`Nickname` = `s`.`LeaderNickname` AND `g`.`SquadId` = `s`.`LeaderSquadId`
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `s`.`Nickname`
""");
    }

    public override async Task Include_collection_on_derived_type_using_lambda(bool async)
    {
        await base.Include_collection_on_derived_type_using_lambda(async);

        AssertSql(
    """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`
FROM (`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g0`
    LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`
) AS `s` ON `g`.`Nickname` = `s`.`LeaderNickname` AND `g`.`SquadId` = `s`.`LeaderSquadId`
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `s`.`Nickname`
""");
    }

    public override async Task Include_collection_on_derived_type_using_lambda_with_soft_cast(bool async)
    {
        await base.Include_collection_on_derived_type_using_lambda_with_soft_cast(async);

        AssertSql(
    """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`
FROM (`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g0`
    LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`
) AS `s` ON `g`.`Nickname` = `s`.`LeaderNickname` AND `g`.`SquadId` = `s`.`LeaderSquadId`
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `s`.`Nickname`
""");
    }

    public override async Task Include_base_navigation_on_derived_entity(bool async)
    {
        await base.Include_base_navigation_on_derived_entity(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`IssueDate`, `t`.`Note`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM ((`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN `Tags` AS `t` ON `g`.`Nickname` = `t`.`GearNickName` AND `g`.`SquadId` = `t`.`GearSquadId`)
LEFT JOIN `Weapons` AS `w` ON `g`.`FullName` = `w`.`OwnerFullName`
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `t`.`Id`
""");
    }

    public override async Task ThenInclude_collection_on_derived_after_base_reference(bool async)
    {
        await base.ThenInclude_collection_on_derived_after_base_reference(async);

        AssertSql(
            """
SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`IssueDate`, `t`.`Note`, `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM (`Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`)
LEFT JOIN `Weapons` AS `w` ON `s`.`FullName` = `w`.`OwnerFullName`
ORDER BY `t`.`Id`, `s`.`Nickname`, `s`.`SquadId`
""");
    }

    public override async Task ThenInclude_collection_on_derived_after_derived_reference(bool async)
    {
        await base.ThenInclude_collection_on_derived_after_derived_reference(async);

        AssertSql(
            """
SELECT `f`.`Id`, `f`.`CapitalName`, `f`.`Name`, `f`.`ServerAddress`, `l`.`CommanderName`, `l`.`Eradicated`, IIF(`l`.`Id` IS NOT NULL, 'LocustHorde', NULL) AS `Discriminator`, `s`.`Name`, `s`.`LocustHordeId`, `s`.`ThreatLevel`, `s`.`ThreatLevelByte`, `s`.`ThreatLevelNullableByte`, `s`.`DefeatedByNickname`, `s`.`DefeatedBySquadId`, `s`.`HighCommandId`, `s0`.`Nickname`, `s0`.`SquadId`, `s0`.`AssignedCityName`, `s0`.`CityOfBirthName`, `s0`.`FullName`, `s0`.`HasSoulPatch`, `s0`.`LeaderNickname`, `s0`.`LeaderSquadId`, `s0`.`Rank`, `s0`.`Discriminator`, `s1`.`Nickname`, `s1`.`SquadId`, `s1`.`AssignedCityName`, `s1`.`CityOfBirthName`, `s1`.`FullName`, `s1`.`HasSoulPatch`, `s1`.`LeaderNickname`, `s1`.`LeaderSquadId`, `s1`.`Rank`, `s1`.`Discriminator`
FROM (((`Factions` AS `f`
LEFT JOIN `LocustHordes` AS `l` ON `f`.`Id` = `l`.`Id`)
LEFT JOIN (
    SELECT `l0`.`Name`, `l0`.`LocustHordeId`, `l0`.`ThreatLevel`, `l0`.`ThreatLevelByte`, `l0`.`ThreatLevelNullableByte`, `l1`.`DefeatedByNickname`, `l1`.`DefeatedBySquadId`, `l1`.`HighCommandId`
    FROM `LocustLeaders` AS `l0`
    INNER JOIN `LocustCommanders` AS `l1` ON `l0`.`Name` = `l1`.`Name`
) AS `s` ON `l`.`CommanderName` = `s`.`Name`)
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
) AS `s0` ON `s`.`DefeatedByNickname` = `s0`.`Nickname` AND `s`.`DefeatedBySquadId` = `s0`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g0`
    LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`
) AS `s1` ON (`s0`.`Nickname` = `s1`.`LeaderNickname` OR (`s0`.`Nickname` IS NULL AND `s1`.`LeaderNickname` IS NULL)) AND `s0`.`SquadId` = `s1`.`LeaderSquadId`
ORDER BY `f`.`Id`, `s`.`Name`, `s0`.`Nickname`, `s0`.`SquadId`, `s1`.`Nickname`
""");
    }

    public override async Task ThenInclude_collection_on_derived_after_derived_collection(bool async)
    {
        await base.ThenInclude_collection_on_derived_after_derived_collection(async);

        AssertSql(
            """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `s0`.`Nickname`, `s0`.`SquadId`, `s0`.`AssignedCityName`, `s0`.`CityOfBirthName`, `s0`.`FullName`, `s0`.`HasSoulPatch`, `s0`.`LeaderNickname`, `s0`.`LeaderSquadId`, `s0`.`Rank`, `s0`.`Discriminator`, `s0`.`Nickname0`, `s0`.`SquadId0`, `s0`.`AssignedCityName0`, `s0`.`CityOfBirthName0`, `s0`.`FullName0`, `s0`.`HasSoulPatch0`, `s0`.`LeaderNickname0`, `s0`.`LeaderSquadId0`, `s0`.`Rank0`, `s0`.`Discriminator0`
FROM (`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `s`.`Nickname` AS `Nickname0`, `s`.`SquadId` AS `SquadId0`, `s`.`AssignedCityName` AS `AssignedCityName0`, `s`.`CityOfBirthName` AS `CityOfBirthName0`, `s`.`FullName` AS `FullName0`, `s`.`HasSoulPatch` AS `HasSoulPatch0`, `s`.`LeaderNickname` AS `LeaderNickname0`, `s`.`LeaderSquadId` AS `LeaderSquadId0`, `s`.`Rank` AS `Rank0`, `s`.`Discriminator` AS `Discriminator0`
    FROM (`Gears` AS `g0`
    LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`)
    LEFT JOIN (
        SELECT `g1`.`Nickname`, `g1`.`SquadId`, `g1`.`AssignedCityName`, `g1`.`CityOfBirthName`, `g1`.`FullName`, `g1`.`HasSoulPatch`, `g1`.`LeaderNickname`, `g1`.`LeaderSquadId`, `g1`.`Rank`, IIF(`o1`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
        FROM `Gears` AS `g1`
        LEFT JOIN `Officers` AS `o1` ON `g1`.`Nickname` = `o1`.`Nickname` AND `g1`.`SquadId` = `o1`.`SquadId`
    ) AS `s` ON `g0`.`Nickname` = `s`.`LeaderNickname` AND `g0`.`SquadId` = `s`.`LeaderSquadId`
) AS `s0` ON `g`.`Nickname` = `s0`.`LeaderNickname` AND `g`.`SquadId` = `s0`.`LeaderSquadId`
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `s0`.`Nickname`, `s0`.`SquadId`, `s0`.`Nickname0`
""");
    }

    public override async Task ThenInclude_reference_on_derived_after_derived_collection(bool async)
    {
        await base.ThenInclude_reference_on_derived_after_derived_collection(async);

        AssertSql(
            """
SELECT `f`.`Id`, `f`.`CapitalName`, `f`.`Name`, `f`.`ServerAddress`, `l`.`CommanderName`, `l`.`Eradicated`, IIF(`l`.`Id` IS NOT NULL, 'LocustHorde', NULL) AS `Discriminator`, `s0`.`Name`, `s0`.`LocustHordeId`, `s0`.`ThreatLevel`, `s0`.`ThreatLevelByte`, `s0`.`ThreatLevelNullableByte`, `s0`.`DefeatedByNickname`, `s0`.`DefeatedBySquadId`, `s0`.`HighCommandId`, `s0`.`Discriminator`, `s0`.`Nickname`, `s0`.`SquadId`, `s0`.`AssignedCityName`, `s0`.`CityOfBirthName`, `s0`.`FullName`, `s0`.`HasSoulPatch`, `s0`.`LeaderNickname`, `s0`.`LeaderSquadId`, `s0`.`Rank`, `s0`.`Discriminator0`
FROM (`Factions` AS `f`
LEFT JOIN `LocustHordes` AS `l` ON `f`.`Id` = `l`.`Id`)
LEFT JOIN (
    SELECT `l0`.`Name`, `l0`.`LocustHordeId`, `l0`.`ThreatLevel`, `l0`.`ThreatLevelByte`, `l0`.`ThreatLevelNullableByte`, `l1`.`DefeatedByNickname`, `l1`.`DefeatedBySquadId`, `l1`.`HighCommandId`, IIF(`l1`.`Name` IS NOT NULL, 'LocustCommander', NULL) AS `Discriminator`, `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator` AS `Discriminator0`
    FROM (`LocustLeaders` AS `l0`
    LEFT JOIN `LocustCommanders` AS `l1` ON `l0`.`Name` = `l1`.`Name`)
    LEFT JOIN (
        SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
        FROM `Gears` AS `g`
        LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
    ) AS `s` ON `l1`.`DefeatedByNickname` = `s`.`Nickname` AND `l1`.`DefeatedBySquadId` = `s`.`SquadId`
) AS `s0` ON `f`.`Id` = `s0`.`LocustHordeId`
ORDER BY `f`.`Id`, `s0`.`Name`, `s0`.`Nickname`
""");
    }

    public override async Task Multiple_derived_included_on_one_method(bool async)
    {
        await base.Multiple_derived_included_on_one_method(async);

        AssertSql(
    """
SELECT `f`.`Id`, `f`.`CapitalName`, `f`.`Name`, `f`.`ServerAddress`, `l`.`CommanderName`, `l`.`Eradicated`, IIF(`l`.`Id` IS NOT NULL, 'LocustHorde', NULL) AS `Discriminator`, `s`.`Name`, `s`.`LocustHordeId`, `s`.`ThreatLevel`, `s`.`ThreatLevelByte`, `s`.`ThreatLevelNullableByte`, `s`.`DefeatedByNickname`, `s`.`DefeatedBySquadId`, `s`.`HighCommandId`, `s0`.`Nickname`, `s0`.`SquadId`, `s0`.`AssignedCityName`, `s0`.`CityOfBirthName`, `s0`.`FullName`, `s0`.`HasSoulPatch`, `s0`.`LeaderNickname`, `s0`.`LeaderSquadId`, `s0`.`Rank`, `s0`.`Discriminator`, `s1`.`Nickname`, `s1`.`SquadId`, `s1`.`AssignedCityName`, `s1`.`CityOfBirthName`, `s1`.`FullName`, `s1`.`HasSoulPatch`, `s1`.`LeaderNickname`, `s1`.`LeaderSquadId`, `s1`.`Rank`, `s1`.`Discriminator`
FROM (((`Factions` AS `f`
LEFT JOIN `LocustHordes` AS `l` ON `f`.`Id` = `l`.`Id`)
LEFT JOIN (
    SELECT `l0`.`Name`, `l0`.`LocustHordeId`, `l0`.`ThreatLevel`, `l0`.`ThreatLevelByte`, `l0`.`ThreatLevelNullableByte`, `l1`.`DefeatedByNickname`, `l1`.`DefeatedBySquadId`, `l1`.`HighCommandId`
    FROM `LocustLeaders` AS `l0`
    INNER JOIN `LocustCommanders` AS `l1` ON `l0`.`Name` = `l1`.`Name`
) AS `s` ON `l`.`CommanderName` = `s`.`Name`)
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
) AS `s0` ON `s`.`DefeatedByNickname` = `s0`.`Nickname` AND `s`.`DefeatedBySquadId` = `s0`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g0`
    LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`
) AS `s1` ON (`s0`.`Nickname` = `s1`.`LeaderNickname` OR (`s0`.`Nickname` IS NULL AND `s1`.`LeaderNickname` IS NULL)) AND `s0`.`SquadId` = `s1`.`LeaderSquadId`
ORDER BY `f`.`Id`, `s`.`Name`, `s0`.`Nickname`, `s0`.`SquadId`, `s1`.`Nickname`
""");
    }

    public override async Task Include_on_derived_multi_level(bool async)
    {
        await base.Include_on_derived_multi_level(async);

        AssertSql(
    """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `s1`.`Nickname`, `s1`.`SquadId`, `s1`.`AssignedCityName`, `s1`.`CityOfBirthName`, `s1`.`FullName`, `s1`.`HasSoulPatch`, `s1`.`LeaderNickname`, `s1`.`LeaderSquadId`, `s1`.`Rank`, `s1`.`Discriminator`, `s1`.`Id`, `s1`.`Banner`, `s1`.`Banner5`, `s1`.`InternalNumber`, `s1`.`Name`, `s1`.`SquadId0`, `s1`.`MissionId`
FROM (`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `s`.`Id`, `s`.`Banner`, `s`.`Banner5`, `s`.`InternalNumber`, `s`.`Name`, `s0`.`SquadId` AS `SquadId0`, `s0`.`MissionId`
    FROM ((`Gears` AS `g0`
    LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`)
    INNER JOIN `Squads` AS `s` ON `g0`.`SquadId` = `s`.`Id`)
    LEFT JOIN `SquadMissions` AS `s0` ON `s`.`Id` = `s0`.`SquadId`
) AS `s1` ON `g`.`Nickname` = `s1`.`LeaderNickname` AND `g`.`SquadId` = `s1`.`LeaderSquadId`
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `s1`.`Nickname`, `s1`.`SquadId`, `s1`.`Id`, `s1`.`SquadId0`
""");
    }

    public override async Task Projecting_nullable_bool_in_conditional_works(bool async)
    {
        await base.Projecting_nullable_bool_in_conditional_works(async);

        AssertSql(
            """
SELECT IIF(`s`.`Nickname` IS NOT NULL AND `s`.`SquadId` IS NOT NULL, `s`.`HasSoulPatch`, FALSE) AS `Prop`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`HasSoulPatch`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
""");
    }

    public override async Task ToString_enum_property_projection(bool async)
    {
        await base.ToString_enum_property_projection(async);

        AssertSql(
            """
SELECT IIF(`g`.`Rank` = 0, 'None', IIF(`g`.`Rank` = 1, 'Private', IIF(`g`.`Rank` = 2, 'Corporal', IIF(`g`.`Rank` = 4, 'Sergeant', IIF(`g`.`Rank` = 8, 'Lieutenant', IIF(`g`.`Rank` = 16, 'Captain', IIF(`g`.`Rank` = 32, 'Major', IIF(`g`.`Rank` = 64, 'Colonel', IIF(`g`.`Rank` = 128, 'General', (`g`.`Rank` & ''))))))))))
FROM `Gears` AS `g`
""");
    }

    public override async Task ToString_nullable_enum_property_projection(bool async)
    {
        await base.ToString_nullable_enum_property_projection(async);

        AssertSql(
            """
SELECT IIF(`w`.`AmmunitionType` = 1, 'Cartridge', IIF(`w`.`AmmunitionType` = 2, 'Shell', IIF((`w`.`AmmunitionType` & '') IS NULL, '', (`w`.`AmmunitionType` & ''))))
FROM `Weapons` AS `w`
""");
    }

    public override async Task ToString_enum_contains(bool async)
    {
        await base.ToString_enum_contains(async);

        AssertSql(
            """
SELECT `m`.`CodeName`
FROM `Missions` AS `m`
WHERE (`m`.`Difficulty` & '') LIKE '%Med%'
""");
    }

    public override async Task ToString_nullable_enum_contains(bool async)
    {
        await base.ToString_nullable_enum_contains(async);

        AssertSql(
            """
SELECT `w`.`Name`
FROM `Weapons` AS `w`
WHERE IIF(`w`.`AmmunitionType` = 1, 'Cartridge', IIF(`w`.`AmmunitionType` = 2, 'Shell', IIF((`w`.`AmmunitionType` & '') IS NULL, '', (`w`.`AmmunitionType` & '')))) LIKE '%Cart%'
""");
    }

    public override async Task Correlated_collections_naked_navigation_with_ToList(bool async)
    {
        await base.Correlated_collections_naked_navigation_with_ToList(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Gears` AS `g`
LEFT JOIN `Weapons` AS `w` ON `g`.`FullName` = `w`.`OwnerFullName`
WHERE `g`.`Nickname` <> 'Marcus'
ORDER BY `g`.`Nickname`, `g`.`SquadId`
""");
    }

    public override async Task Correlated_collections_naked_navigation_with_ToList_followed_by_projecting_count(bool async)
    {
        await base.Correlated_collections_naked_navigation_with_ToList_followed_by_projecting_count(async);

        AssertSql(
"""
SELECT (
    SELECT COUNT(*)
    FROM `Weapons` AS `w`
    WHERE `g`.`FullName` = `w`.`OwnerFullName`)
FROM `Gears` AS `g`
WHERE `g`.`Nickname` <> 'Marcus'
ORDER BY `g`.`Nickname`
""");
    }

    public override async Task Correlated_collections_naked_navigation_with_ToArray(bool async)
    {
        await base.Correlated_collections_naked_navigation_with_ToArray(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Gears` AS `g`
LEFT JOIN `Weapons` AS `w` ON `g`.`FullName` = `w`.`OwnerFullName`
WHERE `g`.`Nickname` <> 'Marcus'
ORDER BY `g`.`Nickname`, `g`.`SquadId`
""");
    }

    public override async Task Correlated_collections_basic_projection(bool async)
    {
        await base.Correlated_collections_basic_projection(async);

        AssertSql(
    """
SELECT `g`.`Nickname`, `g`.`SquadId`, `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
    FROM `Weapons` AS `w`
    WHERE `w`.`IsAutomatic` = TRUE OR `w`.`Name` <> 'foo' OR `w`.`Name` IS NULL
) AS `w0` ON `g`.`FullName` = `w0`.`OwnerFullName`
WHERE `g`.`Nickname` <> 'Marcus'
ORDER BY `g`.`Nickname`, `g`.`SquadId`
""");
    }

    public override async Task Correlated_collections_basic_projection_explicit_to_list(bool async)
    {
        await base.Correlated_collections_basic_projection_explicit_to_list(async); 
        
        AssertSql(
    """
SELECT `g`.`Nickname`, `g`.`SquadId`, `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
    FROM `Weapons` AS `w`
    WHERE `w`.`IsAutomatic` = TRUE OR `w`.`Name` <> 'foo' OR `w`.`Name` IS NULL
) AS `w0` ON `g`.`FullName` = `w0`.`OwnerFullName`
WHERE `g`.`Nickname` <> 'Marcus'
ORDER BY `g`.`Nickname`, `g`.`SquadId`
""");
    }

    public override async Task Correlated_collections_basic_projection_explicit_to_array(bool async)
    {
        await base.Correlated_collections_basic_projection_explicit_to_array(async);

        AssertSql(
    """
SELECT `g`.`Nickname`, `g`.`SquadId`, `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
    FROM `Weapons` AS `w`
    WHERE `w`.`IsAutomatic` = TRUE OR `w`.`Name` <> 'foo' OR `w`.`Name` IS NULL
) AS `w0` ON `g`.`FullName` = `w0`.`OwnerFullName`
WHERE `g`.`Nickname` <> 'Marcus'
ORDER BY `g`.`Nickname`, `g`.`SquadId`
""");
    }

    public override async Task Correlated_collections_basic_projection_ordered(bool async)
    {
        await base.Correlated_collections_basic_projection_ordered(async);

        AssertSql(
    """
SELECT `g`.`Nickname`, `g`.`SquadId`, `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
    FROM `Weapons` AS `w`
    WHERE `w`.`IsAutomatic` = TRUE OR `w`.`Name` <> 'foo' OR `w`.`Name` IS NULL
) AS `w0` ON `g`.`FullName` = `w0`.`OwnerFullName`
WHERE `g`.`Nickname` <> 'Marcus'
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `w0`.`Name` DESC
""");
    }

    public override async Task Correlated_collections_basic_projection_composite_key(bool async)
    {
        await base.Correlated_collections_basic_projection_composite_key(async);

        AssertSql(
            """
SELECT `g`.`Nickname`, `g`.`SquadId`, `s`.`Nickname`, `s`.`FullName`, `s`.`SquadId`
FROM (`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`FullName`, `g0`.`SquadId`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`
    FROM `Gears` AS `g0`
    WHERE `g0`.`HasSoulPatch` = FALSE
) AS `s` ON `g`.`Nickname` = `s`.`LeaderNickname` AND `g`.`SquadId` = `s`.`LeaderSquadId`
WHERE `o`.`Nickname` IS NOT NULL AND `g`.`Nickname` <> 'Foo'
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `s`.`Nickname`
""");
    }

    public override async Task Correlated_collections_basic_projecting_single_property(bool async)
    {
        await base.Correlated_collections_basic_projecting_single_property(async);

        AssertSql(
    """
SELECT `g`.`Nickname`, `g`.`SquadId`, `w0`.`Name`, `w0`.`Id`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `w`.`Name`, `w`.`Id`, `w`.`OwnerFullName`
    FROM `Weapons` AS `w`
    WHERE `w`.`IsAutomatic` = TRUE OR `w`.`Name` <> 'foo' OR `w`.`Name` IS NULL
) AS `w0` ON `g`.`FullName` = `w0`.`OwnerFullName`
WHERE `g`.`Nickname` <> 'Marcus'
ORDER BY `g`.`Nickname`, `g`.`SquadId`
""");
    }

    public override async Task Correlated_collections_basic_projecting_constant(bool async)
    {
        await base.Correlated_collections_basic_projecting_constant(async);

        AssertSql(
    """
SELECT `g`.`Nickname`, `g`.`SquadId`, `w0`.`c`, `w0`.`Id`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT 'BFG' AS `c`, `w`.`Id`, `w`.`OwnerFullName`
    FROM `Weapons` AS `w`
    WHERE `w`.`IsAutomatic` = TRUE OR `w`.`Name` <> 'foo' OR `w`.`Name` IS NULL
) AS `w0` ON `g`.`FullName` = `w0`.`OwnerFullName`
WHERE `g`.`Nickname` <> 'Marcus'
ORDER BY `g`.`Nickname`, `g`.`SquadId`
""");
    }

    public override async Task Correlated_collections_basic_projecting_constant_bool(bool async)
    {
        await base.Correlated_collections_basic_projecting_constant_bool(async);

        AssertSql(
    """
SELECT `g`.`Nickname`, `g`.`SquadId`, `w0`.`c`, `w0`.`Id`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT TRUE AS `c`, `w`.`Id`, `w`.`OwnerFullName`
    FROM `Weapons` AS `w`
    WHERE `w`.`IsAutomatic` = TRUE OR `w`.`Name` <> 'foo' OR `w`.`Name` IS NULL
) AS `w0` ON `g`.`FullName` = `w0`.`OwnerFullName`
WHERE `g`.`Nickname` <> 'Marcus'
ORDER BY `g`.`Nickname`, `g`.`SquadId`
""");
    }

    public override async Task Correlated_collections_projection_of_collection_thru_navigation(bool async)
    {
        await base.Correlated_collections_projection_of_collection_thru_navigation(async);

        AssertSql(
    """
SELECT `g`.`Nickname`, `g`.`SquadId`, `s`.`Id`, `s1`.`SquadId`, `s1`.`MissionId`
FROM (`Gears` AS `g`
INNER JOIN `Squads` AS `s` ON `g`.`SquadId` = `s`.`Id`)
LEFT JOIN (
    SELECT `s0`.`SquadId`, `s0`.`MissionId`
    FROM `SquadMissions` AS `s0`
    WHERE `s0`.`MissionId` <> 17
) AS `s1` ON `s`.`Id` = `s1`.`SquadId`
WHERE `g`.`Nickname` <> 'Marcus'
ORDER BY `g`.`FullName`, `g`.`Nickname`, `g`.`SquadId`, `s`.`Id`, `s1`.`SquadId`
""");
    }

    public override async Task Correlated_collections_project_anonymous_collection_result(bool async)
    {
        await base.Correlated_collections_project_anonymous_collection_result(async);

        AssertSql(
    """
SELECT `s`.`Name`, `s`.`Id`, `s0`.`FullName`, `s0`.`Rank`, `s0`.`Nickname`, `s0`.`SquadId`
FROM `Squads` AS `s`
LEFT JOIN (
    SELECT `g`.`FullName`, `g`.`Rank`, `g`.`Nickname`, `g`.`SquadId`
    FROM `Gears` AS `g`
) AS `s0` ON `s`.`Id` = `s0`.`SquadId`
WHERE `s`.`Id` < 20
ORDER BY `s`.`Id`, `s0`.`Nickname`
""");
    }

    public override async Task Correlated_collections_nested(bool async)
    {
        await base.Correlated_collections_nested(async);

        AssertSql(
    """
SELECT `s`.`Id`, `s3`.`SquadId`, `s3`.`MissionId`, `s3`.`Id`, `s3`.`SquadId0`, `s3`.`MissionId0`
FROM `Squads` AS `s`
LEFT JOIN (
    SELECT `s0`.`SquadId`, `s0`.`MissionId`, `m`.`Id`, `s2`.`SquadId` AS `SquadId0`, `s2`.`MissionId` AS `MissionId0`
    FROM (`SquadMissions` AS `s0`
    INNER JOIN `Missions` AS `m` ON `s0`.`MissionId` = `m`.`Id`)
    LEFT JOIN (
        SELECT `s1`.`SquadId`, `s1`.`MissionId`
        FROM `SquadMissions` AS `s1`
        WHERE `s1`.`SquadId` < 7
    ) AS `s2` ON `m`.`Id` = `s2`.`MissionId`
    WHERE `s0`.`MissionId` < 42
) AS `s3` ON `s`.`Id` = `s3`.`SquadId`
ORDER BY `s`.`Id`, `s3`.`SquadId`, `s3`.`MissionId`, `s3`.`Id`, `s3`.`SquadId0`
""");
    }

    public override async Task Correlated_collections_nested_mixed_streaming_with_buffer1(bool async)
    {
        await base.Correlated_collections_nested_mixed_streaming_with_buffer1(async);

        AssertSql(
    """
SELECT `s`.`Id`, `s3`.`SquadId`, `s3`.`MissionId`, `s3`.`Id`, `s3`.`SquadId0`, `s3`.`MissionId0`
FROM `Squads` AS `s`
LEFT JOIN (
    SELECT `s0`.`SquadId`, `s0`.`MissionId`, `m`.`Id`, `s2`.`SquadId` AS `SquadId0`, `s2`.`MissionId` AS `MissionId0`
    FROM (`SquadMissions` AS `s0`
    INNER JOIN `Missions` AS `m` ON `s0`.`MissionId` = `m`.`Id`)
    LEFT JOIN (
        SELECT `s1`.`SquadId`, `s1`.`MissionId`
        FROM `SquadMissions` AS `s1`
        WHERE `s1`.`SquadId` < 2
    ) AS `s2` ON `m`.`Id` = `s2`.`MissionId`
    WHERE `s0`.`MissionId` < 3
) AS `s3` ON `s`.`Id` = `s3`.`SquadId`
ORDER BY `s`.`Id`, `s3`.`SquadId`, `s3`.`MissionId`, `s3`.`Id`, `s3`.`SquadId0`
""");
    }

    public override async Task Correlated_collections_nested_mixed_streaming_with_buffer2(bool async)
    {
        await base.Correlated_collections_nested_mixed_streaming_with_buffer2(async);

        AssertSql(
    """
SELECT `s`.`Id`, `s3`.`SquadId`, `s3`.`MissionId`, `s3`.`Id`, `s3`.`SquadId0`, `s3`.`MissionId0`
FROM `Squads` AS `s`
LEFT JOIN (
    SELECT `s0`.`SquadId`, `s0`.`MissionId`, `m`.`Id`, `s2`.`SquadId` AS `SquadId0`, `s2`.`MissionId` AS `MissionId0`
    FROM (`SquadMissions` AS `s0`
    INNER JOIN `Missions` AS `m` ON `s0`.`MissionId` = `m`.`Id`)
    LEFT JOIN (
        SELECT `s1`.`SquadId`, `s1`.`MissionId`
        FROM `SquadMissions` AS `s1`
        WHERE `s1`.`SquadId` < 7
    ) AS `s2` ON `m`.`Id` = `s2`.`MissionId`
    WHERE `s0`.`MissionId` < 42
) AS `s3` ON `s`.`Id` = `s3`.`SquadId`
ORDER BY `s`.`Id`, `s3`.`SquadId`, `s3`.`MissionId`, `s3`.`Id`, `s3`.`SquadId0`
""");
    }

    public override async Task Correlated_collections_nested_with_custom_ordering(bool async)
    {
        await base.Correlated_collections_nested_with_custom_ordering(async);

        AssertSql(
    """
SELECT `g`.`FullName`, `g`.`Nickname`, `g`.`SquadId`, `s`.`FullName`, `s`.`Nickname`, `s`.`SquadId`, `s`.`Id`, `s`.`AmmunitionType`, `s`.`IsAutomatic`, `s`.`Name`, `s`.`OwnerFullName`, `s`.`SynergyWithId`
FROM (`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`FullName`, `g0`.`Nickname`, `g0`.`SquadId`, `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`, `g0`.`Rank`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`
    FROM `Gears` AS `g0`
    LEFT JOIN (
        SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
        FROM `Weapons` AS `w`
        WHERE `w`.`Name` <> 'Bar' OR `w`.`Name` IS NULL
    ) AS `w0` ON `g0`.`FullName` = `w0`.`OwnerFullName`
    WHERE `g0`.`FullName` <> 'Foo'
) AS `s` ON `g`.`Nickname` = `s`.`LeaderNickname` AND `g`.`SquadId` = `s`.`LeaderSquadId`
WHERE `o`.`Nickname` IS NOT NULL
ORDER BY NOT (`g`.`HasSoulPatch`) DESC, `g`.`Nickname`, `g`.`SquadId`, `s`.`Rank`, `s`.`Nickname`, `s`.`SquadId`, NOT (`s`.`IsAutomatic`)
""");
    }

    public override async Task Correlated_collections_same_collection_projected_multiple_times(bool async)
    {
        await base.Correlated_collections_same_collection_projected_multiple_times(async);

        AssertSql(
    """
SELECT `g`.`FullName`, `g`.`Nickname`, `g`.`SquadId`, `w1`.`Id`, `w1`.`AmmunitionType`, `w1`.`IsAutomatic`, `w1`.`Name`, `w1`.`OwnerFullName`, `w1`.`SynergyWithId`, `w2`.`Id`, `w2`.`AmmunitionType`, `w2`.`IsAutomatic`, `w2`.`Name`, `w2`.`OwnerFullName`, `w2`.`SynergyWithId`
FROM (`Gears` AS `g`
LEFT JOIN (
    SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
    FROM `Weapons` AS `w`
    WHERE `w`.`IsAutomatic` = TRUE
) AS `w1` ON `g`.`FullName` = `w1`.`OwnerFullName`)
LEFT JOIN (
    SELECT `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
    FROM `Weapons` AS `w0`
    WHERE `w0`.`IsAutomatic` = TRUE
) AS `w2` ON `g`.`FullName` = `w2`.`OwnerFullName`
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `w1`.`Id`
""");
    }

    public override async Task Correlated_collections_similar_collection_projected_multiple_times(bool async)
    {
        await base.Correlated_collections_similar_collection_projected_multiple_times(async);

        AssertSql(
            """
SELECT `g`.`FullName`, `g`.`Nickname`, `g`.`SquadId`, `w1`.`Id`, `w1`.`AmmunitionType`, `w1`.`IsAutomatic`, `w1`.`Name`, `w1`.`OwnerFullName`, `w1`.`SynergyWithId`, `w2`.`Id`, `w2`.`AmmunitionType`, `w2`.`IsAutomatic`, `w2`.`Name`, `w2`.`OwnerFullName`, `w2`.`SynergyWithId`
FROM (`Gears` AS `g`
LEFT JOIN (
    SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
    FROM `Weapons` AS `w`
    WHERE `w`.`IsAutomatic` = TRUE
) AS `w1` ON `g`.`FullName` = `w1`.`OwnerFullName`)
LEFT JOIN (
    SELECT `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
    FROM `Weapons` AS `w0`
    WHERE `w0`.`IsAutomatic` = FALSE
) AS `w2` ON `g`.`FullName` = `w2`.`OwnerFullName`
ORDER BY `g`.`Rank`, `g`.`Nickname`, `g`.`SquadId`, `w1`.`OwnerFullName`, `w1`.`Id`, NOT (`w2`.`IsAutomatic`)
""");
    }

    public override async Task Correlated_collections_different_collections_projected(bool async)
    {
        await base.Correlated_collections_different_collections_projected(async);

        AssertSql(
    """
SELECT `g`.`Nickname`, `g`.`SquadId`, `w0`.`Name`, `w0`.`IsAutomatic`, `w0`.`Id`, `s`.`Nickname`, `s`.`Rank`, `s`.`SquadId`
FROM ((`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN (
    SELECT `w`.`Name`, `w`.`IsAutomatic`, `w`.`Id`, `w`.`OwnerFullName`
    FROM `Weapons` AS `w`
    WHERE `w`.`IsAutomatic` = TRUE
) AS `w0` ON `g`.`FullName` = `w0`.`OwnerFullName`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`Rank`, `g0`.`SquadId`, `g0`.`FullName`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`
    FROM `Gears` AS `g0`
) AS `s` ON `g`.`Nickname` = `s`.`LeaderNickname` AND `g`.`SquadId` = `s`.`LeaderSquadId`
WHERE `o`.`Nickname` IS NOT NULL
ORDER BY `g`.`FullName`, `g`.`Nickname`, `g`.`SquadId`, `w0`.`Id`, `s`.`FullName`, `s`.`Nickname`
""");
    }

    public override async Task Multiple_orderby_with_navigation_expansion_on_one_of_the_order_bys(bool async)
    {
        await base.Multiple_orderby_with_navigation_expansion_on_one_of_the_order_bys(async);

        AssertSql(
    """
SELECT `g`.`FullName`
FROM (`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN `Tags` AS `t` ON `g`.`Nickname` = `t`.`GearNickName` AND `g`.`SquadId` = `t`.`GearSquadId`
WHERE `o`.`Nickname` IS NOT NULL AND EXISTS (
    SELECT 1
    FROM `Gears` AS `g0`
    WHERE `g`.`Nickname` = `g0`.`LeaderNickname` AND `g`.`SquadId` = `g0`.`LeaderSquadId`)
ORDER BY NOT (`g`.`HasSoulPatch`) DESC, `t`.`Note`
""");
    }

    public override async Task Multiple_orderby_with_navigation_expansion_on_one_of_the_order_bys_inside_subquery(bool async)
    {
        await base.Multiple_orderby_with_navigation_expansion_on_one_of_the_order_bys_inside_subquery(async);

        AssertSql(
            """
SELECT `g`.`FullName`, `g`.`Nickname`, `g`.`SquadId`, `t`.`Id`, `s`.`Nickname`, `s`.`SquadId`, `s1`.`Id`, `s1`.`AmmunitionType`, `s1`.`IsAutomatic`, `s1`.`Name`, `s1`.`OwnerFullName`, `s1`.`SynergyWithId`, `s1`.`Nickname`, `s1`.`SquadId`
FROM (((`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN `Tags` AS `t` ON `g`.`Nickname` = `t`.`GearNickName` AND `g`.`SquadId` = `t`.`GearSquadId`)
LEFT JOIN (
    SELECT `g1`.`Nickname`, `g1`.`SquadId`, `g1`.`FullName`
    FROM `Gears` AS `g1`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`)
LEFT JOIN (
    SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`, `s0`.`Nickname`, `s0`.`SquadId`
    FROM `Weapons` AS `w`
    LEFT JOIN (
        SELECT `g2`.`Nickname`, `g2`.`SquadId`, `g2`.`FullName`
        FROM `Gears` AS `g2`
    ) AS `s0` ON `w`.`OwnerFullName` = `s0`.`FullName`
) AS `s1` ON `s`.`FullName` = `s1`.`OwnerFullName`
WHERE `o`.`Nickname` IS NOT NULL AND EXISTS (
    SELECT 1
    FROM `Gears` AS `g0`
    WHERE `g`.`Nickname` = `g0`.`LeaderNickname` AND `g`.`SquadId` = `g0`.`LeaderSquadId`)
ORDER BY NOT (`g`.`HasSoulPatch`) DESC, `t`.`Note`, `g`.`Nickname`, `g`.`SquadId`, `t`.`Id`, `s`.`Nickname`, `s`.`SquadId`, NOT (`s1`.`IsAutomatic`), `s1`.`Nickname` DESC, `s1`.`Id`
""");
    }

    public override async Task Multiple_orderby_with_navigation_expansion_on_one_of_the_order_bys_inside_subquery_duplicated_orderings(
        bool async)
    {
        await base.Multiple_orderby_with_navigation_expansion_on_one_of_the_order_bys_inside_subquery_duplicated_orderings(async);

        AssertSql(
    """
SELECT `g`.`FullName`, `g`.`Nickname`, `g`.`SquadId`, `t`.`Id`, `s`.`Nickname`, `s`.`SquadId`, `s1`.`Id`, `s1`.`AmmunitionType`, `s1`.`IsAutomatic`, `s1`.`Name`, `s1`.`OwnerFullName`, `s1`.`SynergyWithId`, `s1`.`Nickname`, `s1`.`SquadId`
FROM (((`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN `Tags` AS `t` ON `g`.`Nickname` = `t`.`GearNickName` AND `g`.`SquadId` = `t`.`GearSquadId`)
LEFT JOIN (
    SELECT `g1`.`Nickname`, `g1`.`SquadId`, `g1`.`FullName`
    FROM `Gears` AS `g1`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`)
LEFT JOIN (
    SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`, `s0`.`Nickname`, `s0`.`SquadId`
    FROM `Weapons` AS `w`
    LEFT JOIN (
        SELECT `g2`.`Nickname`, `g2`.`SquadId`, `g2`.`FullName`
        FROM `Gears` AS `g2`
    ) AS `s0` ON `w`.`OwnerFullName` = `s0`.`FullName`
) AS `s1` ON `s`.`FullName` = `s1`.`OwnerFullName`
WHERE `o`.`Nickname` IS NOT NULL AND EXISTS (
    SELECT 1
    FROM `Gears` AS `g0`
    WHERE `g`.`Nickname` = `g0`.`LeaderNickname` AND `g`.`SquadId` = `g0`.`LeaderSquadId`)
ORDER BY NOT (`g`.`HasSoulPatch`) DESC, `t`.`Note`, `g`.`Nickname`, `g`.`SquadId`, `t`.`Id`, `s`.`Nickname`, `s`.`SquadId`, NOT (`s1`.`IsAutomatic`), `s1`.`Nickname` DESC, `s1`.`Id`
""");
    }

    public override async Task Multiple_orderby_with_navigation_expansion_on_one_of_the_order_bys_inside_subquery_complex_orderings(
        bool async)
    {
        await base.Multiple_orderby_with_navigation_expansion_on_one_of_the_order_bys_inside_subquery_complex_orderings(async);

        AssertSql(
    """
SELECT `g`.`FullName`, `g`.`Nickname`, `g`.`SquadId`, `t`.`Id`, `s`.`Nickname`, `s`.`SquadId`, `s1`.`Id`, `s1`.`AmmunitionType`, `s1`.`IsAutomatic`, `s1`.`Name`, `s1`.`OwnerFullName`, `s1`.`SynergyWithId`, `s1`.`Nickname`, `s1`.`SquadId`
FROM (((`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN `Tags` AS `t` ON `g`.`Nickname` = `t`.`GearNickName` AND `g`.`SquadId` = `t`.`GearSquadId`)
LEFT JOIN (
    SELECT `g1`.`Nickname`, `g1`.`SquadId`, `g1`.`FullName`
    FROM `Gears` AS `g1`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`)
LEFT JOIN (
    SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`, `s0`.`Nickname`, `s0`.`SquadId`, (
        SELECT COUNT(*)
        FROM `Weapons` AS `w0`
        WHERE `s0`.`FullName` IS NOT NULL AND `s0`.`FullName` = `w0`.`OwnerFullName`) AS `c`
    FROM `Weapons` AS `w`
    LEFT JOIN (
        SELECT `g2`.`Nickname`, `g2`.`SquadId`, `g2`.`FullName`
        FROM `Gears` AS `g2`
    ) AS `s0` ON `w`.`OwnerFullName` = `s0`.`FullName`
) AS `s1` ON `s`.`FullName` = `s1`.`OwnerFullName`
WHERE `o`.`Nickname` IS NOT NULL AND EXISTS (
    SELECT 1
    FROM `Gears` AS `g0`
    WHERE `g`.`Nickname` = `g0`.`LeaderNickname` AND `g`.`SquadId` = `g0`.`LeaderSquadId`)
ORDER BY NOT (`g`.`HasSoulPatch`) DESC, `t`.`Note`, `g`.`Nickname`, `g`.`SquadId`, `t`.`Id`, `s`.`Nickname`, `s`.`SquadId`, `s1`.`Id` DESC, `s1`.`c`, `s1`.`Nickname`
""");
    }

    public override async Task Correlated_collections_multiple_nested_complex_collections(bool async)
    {
        await base.Correlated_collections_multiple_nested_complex_collections(async);

        AssertSql(
    """
SELECT `g`.`FullName`, `g`.`Nickname`, `g`.`SquadId`, `t`.`Id`, `s`.`Nickname`, `s`.`SquadId`, `s5`.`FullName`, `s5`.`Nickname`, `s5`.`SquadId`, `s5`.`Id`, `s5`.`Nickname0`, `s5`.`SquadId0`, `s5`.`Id0`, `s5`.`Name`, `s5`.`IsAutomatic`, `s5`.`Id1`, `s5`.`Nickname00`, `s5`.`HasSoulPatch`, `s5`.`SquadId00`, `s6`.`Id`, `s6`.`AmmunitionType`, `s6`.`IsAutomatic`, `s6`.`Name`, `s6`.`OwnerFullName`, `s6`.`SynergyWithId`, `s6`.`Nickname`, `s6`.`SquadId`
FROM ((((`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN `Tags` AS `t` ON `g`.`Nickname` = `t`.`GearNickName` AND `g`.`SquadId` = `t`.`GearSquadId`)
LEFT JOIN (
    SELECT `g1`.`Nickname`, `g1`.`SquadId`, `g1`.`FullName`
    FROM `Gears` AS `g1`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`)
LEFT JOIN (
    SELECT `g2`.`FullName`, `g2`.`Nickname`, `g2`.`SquadId`, `s4`.`Id`, `s4`.`Nickname` AS `Nickname0`, `s4`.`SquadId` AS `SquadId0`, `s4`.`Id0`, `s4`.`Name`, `s4`.`IsAutomatic`, `s4`.`Id1`, `s4`.`Nickname0` AS `Nickname00`, `s4`.`HasSoulPatch`, `s4`.`SquadId0` AS `SquadId00`, `g2`.`Rank`, `s4`.`IsAutomatic0`, `g2`.`LeaderNickname`, `g2`.`LeaderSquadId`
    FROM `Gears` AS `g2`
    LEFT JOIN (
        SELECT `w`.`Id`, `s0`.`Nickname`, `s0`.`SquadId`, `s1`.`Id` AS `Id0`, `w0`.`Name`, `w0`.`IsAutomatic`, `w0`.`Id` AS `Id1`, `s3`.`Nickname` AS `Nickname0`, `s3`.`HasSoulPatch`, `s3`.`SquadId` AS `SquadId0`, `w`.`IsAutomatic` AS `IsAutomatic0`, `w`.`OwnerFullName`
        FROM (((`Weapons` AS `w`
        LEFT JOIN (
            SELECT `g3`.`Nickname`, `g3`.`SquadId`, `g3`.`FullName`
            FROM `Gears` AS `g3`
        ) AS `s0` ON `w`.`OwnerFullName` = `s0`.`FullName`)
        LEFT JOIN `Squads` AS `s1` ON `s0`.`SquadId` = `s1`.`Id`)
        LEFT JOIN `Weapons` AS `w0` ON `s0`.`FullName` = `w0`.`OwnerFullName`)
        LEFT JOIN (
            SELECT `g4`.`Nickname`, `g4`.`HasSoulPatch`, `g4`.`SquadId`
            FROM `Gears` AS `g4`
        ) AS `s3` ON `s1`.`Id` = `s3`.`SquadId`
        WHERE `w`.`Name` <> 'Bar' OR `w`.`Name` IS NULL
    ) AS `s4` ON `g2`.`FullName` = `s4`.`OwnerFullName`
    WHERE `g2`.`FullName` <> 'Foo'
) AS `s5` ON `g`.`Nickname` = `s5`.`LeaderNickname` AND `g`.`SquadId` = `s5`.`LeaderSquadId`)
LEFT JOIN (
    SELECT `w1`.`Id`, `w1`.`AmmunitionType`, `w1`.`IsAutomatic`, `w1`.`Name`, `w1`.`OwnerFullName`, `w1`.`SynergyWithId`, `s2`.`Nickname`, `s2`.`SquadId`
    FROM `Weapons` AS `w1`
    LEFT JOIN (
        SELECT `g5`.`Nickname`, `g5`.`SquadId`, `g5`.`FullName`
        FROM `Gears` AS `g5`
    ) AS `s2` ON `w1`.`OwnerFullName` = `s2`.`FullName`
) AS `s6` ON `s`.`FullName` = `s6`.`OwnerFullName`
WHERE `o`.`Nickname` IS NOT NULL AND EXISTS (
    SELECT 1
    FROM `Gears` AS `g0`
    WHERE `g`.`Nickname` = `g0`.`LeaderNickname` AND `g`.`SquadId` = `g0`.`LeaderSquadId`)
ORDER BY NOT (`g`.`HasSoulPatch`) DESC, `t`.`Note`, `g`.`Nickname`, `g`.`SquadId`, `t`.`Id`, `s`.`Nickname`, `s`.`SquadId`, `s5`.`Rank`, `s5`.`Nickname`, `s5`.`SquadId`, NOT (`s5`.`IsAutomatic0`), `s5`.`Id`, `s5`.`Nickname0`, `s5`.`SquadId0`, `s5`.`Id0`, `s5`.`Id1`, `s5`.`Nickname00`, `s5`.`SquadId00`, NOT (`s6`.`IsAutomatic`), `s6`.`Nickname` DESC, `s6`.`Id`
""");
    }

    public override async Task Correlated_collections_inner_subquery_selector_references_outer_qsre(bool async)
    {
        await base.Correlated_collections_inner_subquery_selector_references_outer_qsre(async);

        AssertSql(
"""
SELECT [g].[FullName], [g].[Nickname], [g].[SquadId], [t].[ReportName], [t].[OfficerName], [t].[Nickname], [t].[SquadId]
FROM [Gears] AS [g]
LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
OUTER APPLY (
    SELECT [g0].[FullName] AS [ReportName], [g].[FullName] AS [OfficerName], [g0].[Nickname], [g0].[SquadId]
    FROM [Gears] AS [g0]
    WHERE [g].[Nickname] = [g0].[LeaderNickname] AND [g].[SquadId] = [g0].[LeaderSquadId]
) AS [t]
WHERE [o].[Nickname] IS NOT NULL
ORDER BY [g].[Nickname], [g].[SquadId], [t].[Nickname]
""");
    }

    public override async Task Correlated_collections_inner_subquery_predicate_references_outer_qsre(bool async)
    {
        await base.Correlated_collections_inner_subquery_predicate_references_outer_qsre(async);

        AssertSql(
"""
SELECT [g].[FullName], [g].[Nickname], [g].[SquadId], [t].[ReportName], [t].[Nickname], [t].[SquadId]
FROM [Gears] AS [g]
LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
OUTER APPLY (
    SELECT [g0].[FullName] AS [ReportName], [g0].[Nickname], [g0].[SquadId]
    FROM [Gears] AS [g0]
    WHERE [g].[Nickname] = [g0].[LeaderNickname] AND [g].[SquadId] = [g0].[LeaderSquadId] AND [g].[FullName] <> N'Foo'
) AS [t]
WHERE [o].[Nickname] IS NOT NULL
ORDER BY [g].[Nickname], [g].[SquadId], [t].[Nickname]
""");
    }

    public override async Task Correlated_collections_nested_inner_subquery_references_outer_qsre_one_level_up(bool async)
    {
        await base.Correlated_collections_nested_inner_subquery_references_outer_qsre_one_level_up(async);

        AssertSql(
"""
SELECT [g].[FullName], [g].[Nickname], [g].[SquadId], [t0].[FullName], [t0].[Nickname], [t0].[SquadId], [t0].[Name], [t0].[Nickname0], [t0].[Id]
FROM [Gears] AS [g]
LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
LEFT JOIN (
    SELECT [g0].[FullName], [g0].[Nickname], [g0].[SquadId], [t].[Name], [t].[Nickname] AS [Nickname0], [t].[Id], [g0].[LeaderNickname], [g0].[LeaderSquadId]
    FROM [Gears] AS [g0]
    OUTER APPLY (
        SELECT [w].[Name], [g0].[Nickname], [w].[Id]
        FROM [Weapons] AS [w]
        WHERE [g0].[FullName] = [w].[OwnerFullName] AND ([w].[Name] <> N'Bar' OR ([w].[Name] IS NULL))
    ) AS [t]
    WHERE [g0].[FullName] <> N'Foo'
) AS [t0] ON [g].[Nickname] = [t0].[LeaderNickname] AND [g].[SquadId] = [t0].[LeaderSquadId]
WHERE [o].[Nickname] IS NOT NULL
ORDER BY [g].[Nickname], [g].[SquadId], [t0].[Nickname], [t0].[SquadId]
""");
    }

    public override async Task Correlated_collections_nested_inner_subquery_references_outer_qsre_two_levels_up(bool async)
    {
        await base.Correlated_collections_nested_inner_subquery_references_outer_qsre_two_levels_up(async);

        AssertSql(
"""
SELECT [g].[FullName], [g].[Nickname], [g].[SquadId], [t0].[FullName], [t0].[Nickname], [t0].[SquadId], [t0].[Name], [t0].[Nickname0], [t0].[Id]
FROM [Gears] AS [g]
LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
OUTER APPLY (
    SELECT [g0].[FullName], [g0].[Nickname], [g0].[SquadId], [t].[Name], [t].[Nickname] AS [Nickname0], [t].[Id]
    FROM [Gears] AS [g0]
    LEFT JOIN (
        SELECT [w].[Name], [g].[Nickname], [w].[Id], [w].[OwnerFullName]
        FROM [Weapons] AS [w]
        WHERE [w].[Name] <> N'Bar' OR ([w].[Name] IS NULL)
    ) AS [t] ON [g0].[FullName] = [t].[OwnerFullName]
    WHERE [g].[Nickname] = [g0].[LeaderNickname] AND [g].[SquadId] = [g0].[LeaderSquadId] AND [g0].[FullName] <> N'Foo'
) AS [t0]
WHERE [o].[Nickname] IS NOT NULL
ORDER BY [g].[Nickname], [g].[SquadId], [t0].[Nickname], [t0].[SquadId]
""");
    }

    public override async Task Correlated_collections_on_select_many(bool async)
    {
        await base.Correlated_collections_on_select_many(async);

        AssertSql(
"""
SELECT [g].[Nickname], [s].[Name], [g].[SquadId], [s].[Id], [t].[Id], [t].[AmmunitionType], [t].[IsAutomatic], [t].[Name], [t].[OwnerFullName], [t].[SynergyWithId], [t0].[Nickname], [t0].[SquadId], [t0].[AssignedCityName], [t0].[CityOfBirthName], [t0].[FullName], [t0].[HasSoulPatch], [t0].[LeaderNickname], [t0].[LeaderSquadId], [t0].[Rank], [t0].[Discriminator]
FROM [Gears] AS [g]
CROSS JOIN [Squads] AS [s]
LEFT JOIN (
    SELECT [w].[Id], [w].[AmmunitionType], [w].[IsAutomatic], [w].[Name], [w].[OwnerFullName], [w].[SynergyWithId]
    FROM [Weapons] AS [w]
    WHERE [w].[IsAutomatic] = CAST(1 AS bit) OR [w].[Name] <> N'foo' OR ([w].[Name] IS NULL)
) AS [t] ON [g].[FullName] = [t].[OwnerFullName]
LEFT JOIN (
    SELECT [g0].[Nickname], [g0].[SquadId], [g0].[AssignedCityName], [g0].[CityOfBirthName], [g0].[FullName], [g0].[HasSoulPatch], [g0].[LeaderNickname], [g0].[LeaderSquadId], [g0].[Rank], CASE
        WHEN [o0].[Nickname] IS NOT NULL THEN N'Officer'
    END AS [Discriminator]
    FROM [Gears] AS [g0]
    LEFT JOIN [Officers] AS [o0] ON [g0].[Nickname] = [o0].[Nickname] AND [g0].[SquadId] = [o0].[SquadId]
    WHERE [g0].[HasSoulPatch] = CAST(0 AS bit)
) AS [t0] ON [s].[Id] = [t0].[SquadId]
WHERE [g].[HasSoulPatch] = CAST(1 AS bit)
ORDER BY [g].[Nickname], [s].[Id] DESC, [g].[SquadId], [t].[Id], [t0].[Nickname]
""");
    }

    public override async Task Correlated_collections_with_Skip(bool async)
    {
        await base.Correlated_collections_with_Skip(async);

        AssertSql(
"""
SELECT [s].[Id], [t0].[Nickname], [t0].[SquadId], [t0].[AssignedCityName], [t0].[CityOfBirthName], [t0].[FullName], [t0].[HasSoulPatch], [t0].[LeaderNickname], [t0].[LeaderSquadId], [t0].[Rank], [t0].[Discriminator]
FROM [Squads] AS [s]
LEFT JOIN (
    SELECT [t].[Nickname], [t].[SquadId], [t].[AssignedCityName], [t].[CityOfBirthName], [t].[FullName], [t].[HasSoulPatch], [t].[LeaderNickname], [t].[LeaderSquadId], [t].[Rank], [t].[Discriminator]
    FROM (
        SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOfBirthName], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], CASE
            WHEN [o].[Nickname] IS NOT NULL THEN N'Officer'
        END AS [Discriminator], ROW_NUMBER() OVER(PARTITION BY [g].[SquadId] ORDER BY [g].[Nickname]) AS [row]
        FROM [Gears] AS [g]
        LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
    ) AS [t]
    WHERE 1 < [t].[row]
) AS [t0] ON [s].[Id] = [t0].[SquadId]
ORDER BY [s].[Name], [s].[Id], [t0].[SquadId], [t0].[Nickname]
""");
    }

    public override async Task Correlated_collections_with_Take(bool async)
    {
        await base.Correlated_collections_with_Take(async);

        AssertSql(
"""
SELECT [s].[Id], [t0].[Nickname], [t0].[SquadId], [t0].[AssignedCityName], [t0].[CityOfBirthName], [t0].[FullName], [t0].[HasSoulPatch], [t0].[LeaderNickname], [t0].[LeaderSquadId], [t0].[Rank], [t0].[Discriminator]
FROM [Squads] AS [s]
LEFT JOIN (
    SELECT [t].[Nickname], [t].[SquadId], [t].[AssignedCityName], [t].[CityOfBirthName], [t].[FullName], [t].[HasSoulPatch], [t].[LeaderNickname], [t].[LeaderSquadId], [t].[Rank], [t].[Discriminator]
    FROM (
        SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOfBirthName], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], CASE
            WHEN [o].[Nickname] IS NOT NULL THEN N'Officer'
        END AS [Discriminator], ROW_NUMBER() OVER(PARTITION BY [g].[SquadId] ORDER BY [g].[Nickname]) AS [row]
        FROM [Gears] AS [g]
        LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
    ) AS [t]
    WHERE [t].[row] <= 2
) AS [t0] ON [s].[Id] = [t0].[SquadId]
ORDER BY [s].[Name], [s].[Id], [t0].[SquadId], [t0].[Nickname]
""");
    }

    public override async Task Correlated_collections_with_Distinct(bool async)
    {
        await base.Correlated_collections_with_Distinct(async);

        AssertSql(
"""
SELECT [s].[Id], [t0].[Nickname], [t0].[SquadId], [t0].[AssignedCityName], [t0].[CityOfBirthName], [t0].[FullName], [t0].[HasSoulPatch], [t0].[LeaderNickname], [t0].[LeaderSquadId], [t0].[Rank], [t0].[Discriminator]
FROM [Squads] AS [s]
OUTER APPLY (
    SELECT DISTINCT [t].[Nickname], [t].[SquadId], [t].[AssignedCityName], [t].[CityOfBirthName], [t].[FullName], [t].[HasSoulPatch], [t].[LeaderNickname], [t].[LeaderSquadId], [t].[Rank], [t].[Discriminator]
    FROM (
        SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOfBirthName], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], CASE
            WHEN [o].[Nickname] IS NOT NULL THEN N'Officer'
        END AS [Discriminator]
        FROM [Gears] AS [g]
        LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
        WHERE [s].[Id] = [g].[SquadId]
        ORDER BY [g].[Nickname]
        OFFSET 0 ROWS
    ) AS [t]
) AS [t0]
ORDER BY [s].[Name], [s].[Id], [t0].[Nickname]
""");
    }

    public override async Task Correlated_collections_with_FirstOrDefault(bool async)
    {
        await base.Correlated_collections_with_FirstOrDefault(async);

        AssertSql(
            """
SELECT (
    SELECT TOP 1 `g`.`FullName`
    FROM `Gears` AS `g`
    WHERE `s`.`Id` = `g`.`SquadId`
    ORDER BY `g`.`Nickname`)
FROM `Squads` AS `s`
ORDER BY `s`.`Name`
""");
    }

    public override async Task Correlated_collections_on_left_join_with_predicate(bool async)
    {
        await base.Correlated_collections_on_left_join_with_predicate(async);

        AssertSql(
            """
SELECT `s`.`Nickname`, `t`.`Id`, `s`.`SquadId`, `w`.`Name`, `w`.`Id`
FROM (`Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`FullName`, `g`.`HasSoulPatch`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname`)
LEFT JOIN `Weapons` AS `w` ON `s`.`FullName` = `w`.`OwnerFullName`
WHERE `s`.`HasSoulPatch` = FALSE
ORDER BY `t`.`Id`, `s`.`Nickname`, `s`.`SquadId`
""");
    }

    public override async Task Correlated_collections_on_left_join_with_null_value(bool async)
    {
        await base.Correlated_collections_on_left_join_with_null_value(async);

        AssertSql(
            """
SELECT `t`.`Id`, `s`.`Nickname`, `s`.`SquadId`, `w`.`Name`, `w`.`Id`
FROM (`Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`FullName`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname`)
LEFT JOIN `Weapons` AS `w` ON `s`.`FullName` = `w`.`OwnerFullName`
ORDER BY `t`.`Note`, `t`.`Id`, `s`.`Nickname`, `s`.`SquadId`
""");
    }

    public override async Task Correlated_collections_left_join_with_self_reference(bool async)
    {
        await base.Correlated_collections_left_join_with_self_reference(async);

        AssertSql(
    """
SELECT `t`.`Note`, `t`.`Id`, `s`.`Nickname`, `s`.`SquadId`, `s0`.`FullName`, `s0`.`Nickname`, `s0`.`SquadId`
FROM (`Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`
    FROM `Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
    WHERE `o`.`Nickname` IS NOT NULL
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname`)
LEFT JOIN (
    SELECT `g0`.`FullName`, `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`
    FROM `Gears` AS `g0`
) AS `s0` ON (`s`.`Nickname` = `s0`.`LeaderNickname` OR (`s`.`Nickname` IS NULL AND `s0`.`LeaderNickname` IS NULL)) AND `s`.`SquadId` = `s0`.`LeaderSquadId`
ORDER BY `t`.`Id`, `s`.`Nickname`, `s`.`SquadId`, `s0`.`Nickname`
""");
    }

    public override async Task Correlated_collections_deeply_nested_left_join(bool async)
    {
        await base.Correlated_collections_deeply_nested_left_join(async);

        AssertSql(
    """
SELECT `t`.`Id`, `s`.`Nickname`, `s`.`SquadId`, `s0`.`Id`, `s1`.`Nickname`, `s1`.`SquadId`, `s1`.`Id`, `s1`.`AmmunitionType`, `s1`.`IsAutomatic`, `s1`.`Name`, `s1`.`OwnerFullName`, `s1`.`SynergyWithId`
FROM ((`Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname`)
LEFT JOIN `Squads` AS `s0` ON `s`.`SquadId` = `s0`.`Id`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
    FROM `Gears` AS `g0`
    LEFT JOIN (
        SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
        FROM `Weapons` AS `w`
        WHERE `w`.`IsAutomatic` = TRUE
    ) AS `w0` ON `g0`.`FullName` = `w0`.`OwnerFullName`
    WHERE `g0`.`HasSoulPatch` = TRUE
) AS `s1` ON `s0`.`Id` = `s1`.`SquadId`
ORDER BY `t`.`Note`, `s`.`Nickname` DESC, `t`.`Id`, `s`.`SquadId`, `s0`.`Id`, `s1`.`Nickname`, `s1`.`SquadId`
""");
    }

    public override async Task Correlated_collections_from_left_join_with_additional_elements_projected_of_that_join(bool async)
    {
        await base.Correlated_collections_from_left_join_with_additional_elements_projected_of_that_join(async);

        AssertSql(
            """
SELECT `w`.`Id`, `s`.`Nickname`, `s`.`SquadId`, `s0`.`Id`, `s1`.`Nickname`, `s1`.`SquadId`, `s1`.`Id`, `s1`.`AmmunitionType`, `s1`.`IsAutomatic`, `s1`.`Name`, `s1`.`OwnerFullName`, `s1`.`SynergyWithId`, `s1`.`Rank`
FROM ((`Weapons` AS `w`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`FullName`
    FROM `Gears` AS `g`
) AS `s` ON `w`.`OwnerFullName` = `s`.`FullName`)
LEFT JOIN `Squads` AS `s0` ON `s`.`SquadId` = `s0`.`Id`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `w1`.`Id`, `w1`.`AmmunitionType`, `w1`.`IsAutomatic`, `w1`.`Name`, `w1`.`OwnerFullName`, `w1`.`SynergyWithId`, `g0`.`Rank`, `g0`.`FullName`
    FROM `Gears` AS `g0`
    LEFT JOIN (
        SELECT `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
        FROM `Weapons` AS `w0`
        WHERE `w0`.`IsAutomatic` = FALSE
    ) AS `w1` ON `g0`.`FullName` = `w1`.`OwnerFullName`
) AS `s1` ON `s0`.`Id` = `s1`.`SquadId`
ORDER BY `w`.`Name`, `w`.`Id`, `s`.`Nickname`, `s`.`SquadId`, `s0`.`Id`, `s1`.`FullName` DESC, `s1`.`Nickname`, `s1`.`SquadId`, `s1`.`Id`
""");
    }

    public override async Task Correlated_collections_complex_scenario1(bool async)
    {
        await base.Correlated_collections_complex_scenario1(async);

        AssertSql(
    """
SELECT `g`.`FullName`, `g`.`Nickname`, `g`.`SquadId`, `s2`.`Id`, `s2`.`Nickname`, `s2`.`SquadId`, `s2`.`Id0`, `s2`.`Nickname0`, `s2`.`HasSoulPatch`, `s2`.`SquadId0`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `w`.`Id`, `s`.`Nickname`, `s`.`SquadId`, `s0`.`Id` AS `Id0`, `s1`.`Nickname` AS `Nickname0`, `s1`.`HasSoulPatch`, `s1`.`SquadId` AS `SquadId0`, `w`.`OwnerFullName`
    FROM ((`Weapons` AS `w`
    LEFT JOIN (
        SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`FullName`
        FROM `Gears` AS `g0`
    ) AS `s` ON `w`.`OwnerFullName` = `s`.`FullName`)
    LEFT JOIN `Squads` AS `s0` ON `s`.`SquadId` = `s0`.`Id`)
    LEFT JOIN (
        SELECT `g1`.`Nickname`, `g1`.`HasSoulPatch`, `g1`.`SquadId`
        FROM `Gears` AS `g1`
    ) AS `s1` ON `s0`.`Id` = `s1`.`SquadId`
) AS `s2` ON `g`.`FullName` = `s2`.`OwnerFullName`
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `s2`.`Id`, `s2`.`Nickname`, `s2`.`SquadId`, `s2`.`Id0`, `s2`.`Nickname0`
""");
    }

    public override async Task Correlated_collections_complex_scenario2(bool async)
    {
        await base.Correlated_collections_complex_scenario2(async);

        AssertSql(
    """
SELECT `g`.`FullName`, `g`.`Nickname`, `g`.`SquadId`, `s3`.`FullName`, `s3`.`Nickname`, `s3`.`SquadId`, `s3`.`Id`, `s3`.`Nickname0`, `s3`.`SquadId0`, `s3`.`Id0`, `s3`.`Nickname00`, `s3`.`HasSoulPatch`, `s3`.`SquadId00`
FROM (`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`FullName`, `g0`.`Nickname`, `g0`.`SquadId`, `s2`.`Id`, `s2`.`Nickname` AS `Nickname0`, `s2`.`SquadId` AS `SquadId0`, `s2`.`Id0`, `s2`.`Nickname0` AS `Nickname00`, `s2`.`HasSoulPatch`, `s2`.`SquadId0` AS `SquadId00`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`
    FROM `Gears` AS `g0`
    LEFT JOIN (
        SELECT `w`.`Id`, `s`.`Nickname`, `s`.`SquadId`, `s0`.`Id` AS `Id0`, `s1`.`Nickname` AS `Nickname0`, `s1`.`HasSoulPatch`, `s1`.`SquadId` AS `SquadId0`, `w`.`OwnerFullName`
        FROM ((`Weapons` AS `w`
        LEFT JOIN (
            SELECT `g1`.`Nickname`, `g1`.`SquadId`, `g1`.`FullName`
            FROM `Gears` AS `g1`
        ) AS `s` ON `w`.`OwnerFullName` = `s`.`FullName`)
        LEFT JOIN `Squads` AS `s0` ON `s`.`SquadId` = `s0`.`Id`)
        LEFT JOIN (
            SELECT `g2`.`Nickname`, `g2`.`HasSoulPatch`, `g2`.`SquadId`
            FROM `Gears` AS `g2`
        ) AS `s1` ON `s0`.`Id` = `s1`.`SquadId`
    ) AS `s2` ON `g0`.`FullName` = `s2`.`OwnerFullName`
) AS `s3` ON `g`.`Nickname` = `s3`.`LeaderNickname` AND `g`.`SquadId` = `s3`.`LeaderSquadId`
WHERE `o`.`Nickname` IS NOT NULL
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `s3`.`Nickname`, `s3`.`SquadId`, `s3`.`Id`, `s3`.`Nickname0`, `s3`.`SquadId0`, `s3`.`Id0`, `s3`.`Nickname00`
""");
    }

    public override async Task Correlated_collections_with_funky_orderby_complex_scenario1(bool async)
    {
        await base.Correlated_collections_with_funky_orderby_complex_scenario1(async);

        AssertSql(
    """
SELECT `g`.`FullName`, `g`.`Nickname`, `g`.`SquadId`, `s2`.`Id`, `s2`.`Nickname`, `s2`.`SquadId`, `s2`.`Id0`, `s2`.`Nickname0`, `s2`.`HasSoulPatch`, `s2`.`SquadId0`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `w`.`Id`, `s`.`Nickname`, `s`.`SquadId`, `s0`.`Id` AS `Id0`, `s1`.`Nickname` AS `Nickname0`, `s1`.`HasSoulPatch`, `s1`.`SquadId` AS `SquadId0`, `w`.`OwnerFullName`
    FROM ((`Weapons` AS `w`
    LEFT JOIN (
        SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`FullName`
        FROM `Gears` AS `g0`
    ) AS `s` ON `w`.`OwnerFullName` = `s`.`FullName`)
    LEFT JOIN `Squads` AS `s0` ON `s`.`SquadId` = `s0`.`Id`)
    LEFT JOIN (
        SELECT `g1`.`Nickname`, `g1`.`HasSoulPatch`, `g1`.`SquadId`
        FROM `Gears` AS `g1`
    ) AS `s1` ON `s0`.`Id` = `s1`.`SquadId`
) AS `s2` ON `g`.`FullName` = `s2`.`OwnerFullName`
ORDER BY `g`.`FullName`, `g`.`Nickname` DESC, `g`.`SquadId`, `s2`.`Id`, `s2`.`Nickname`, `s2`.`SquadId`, `s2`.`Id0`, `s2`.`Nickname0`
""");
    }

    public override async Task Correlated_collections_with_funky_orderby_complex_scenario2(bool async)
    {
        await base.Correlated_collections_with_funky_orderby_complex_scenario2(async);

        AssertSql(
    """
SELECT `g`.`FullName`, `g`.`Nickname`, `g`.`SquadId`, `s3`.`FullName`, `s3`.`Nickname`, `s3`.`SquadId`, `s3`.`Id`, `s3`.`Nickname0`, `s3`.`SquadId0`, `s3`.`Id0`, `s3`.`Nickname00`, `s3`.`HasSoulPatch`, `s3`.`SquadId00`
FROM (`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`FullName`, `g0`.`Nickname`, `g0`.`SquadId`, `s2`.`Id`, `s2`.`Nickname` AS `Nickname0`, `s2`.`SquadId` AS `SquadId0`, `s2`.`Id0`, `s2`.`Nickname0` AS `Nickname00`, `s2`.`HasSoulPatch`, `s2`.`SquadId0` AS `SquadId00`, `g0`.`HasSoulPatch` AS `HasSoulPatch0`, `s2`.`IsAutomatic`, `s2`.`Name`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`
    FROM `Gears` AS `g0`
    LEFT JOIN (
        SELECT `w`.`Id`, `s`.`Nickname`, `s`.`SquadId`, `s0`.`Id` AS `Id0`, `s1`.`Nickname` AS `Nickname0`, `s1`.`HasSoulPatch`, `s1`.`SquadId` AS `SquadId0`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`
        FROM ((`Weapons` AS `w`
        LEFT JOIN (
            SELECT `g1`.`Nickname`, `g1`.`SquadId`, `g1`.`FullName`
            FROM `Gears` AS `g1`
        ) AS `s` ON `w`.`OwnerFullName` = `s`.`FullName`)
        LEFT JOIN `Squads` AS `s0` ON `s`.`SquadId` = `s0`.`Id`)
        LEFT JOIN (
            SELECT `g2`.`Nickname`, `g2`.`HasSoulPatch`, `g2`.`SquadId`
            FROM `Gears` AS `g2`
        ) AS `s1` ON `s0`.`Id` = `s1`.`SquadId`
    ) AS `s2` ON `g0`.`FullName` = `s2`.`OwnerFullName`
) AS `s3` ON `g`.`Nickname` = `s3`.`LeaderNickname` AND `g`.`SquadId` = `s3`.`LeaderSquadId`
WHERE `o`.`Nickname` IS NOT NULL
ORDER BY NOT (`g`.`HasSoulPatch`), `g`.`LeaderNickname`, `g`.`FullName`, `g`.`Nickname`, `g`.`SquadId`, `s3`.`FullName`, NOT (`s3`.`HasSoulPatch0`) DESC, `s3`.`Nickname`, `s3`.`SquadId`, NOT (`s3`.`IsAutomatic`), `s3`.`Name` DESC, `s3`.`Id`, `s3`.`Nickname0`, `s3`.`SquadId0`, `s3`.`Id0`, `s3`.`Nickname00`
""");
    }

    public override async Task Correlated_collection_with_top_level_FirstOrDefault(bool async)
    {
        await base.Correlated_collection_with_top_level_FirstOrDefault(async);

        AssertSql(
    """
SELECT `s`.`Nickname`, `s`.`SquadId`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM (
    SELECT TOP 1 `g`.`Nickname`, `g`.`SquadId`, `g`.`FullName`
    FROM `Gears` AS `g`
    ORDER BY `g`.`Nickname`
) AS `s`
LEFT JOIN `Weapons` AS `w` ON `s`.`FullName` = `w`.`OwnerFullName`
ORDER BY `s`.`Nickname`, `s`.`SquadId`
""");
    }

    public override async Task Correlated_collection_with_top_level_Count(bool async)
    {
        await base.Correlated_collection_with_top_level_Count(async);

        AssertSql(
"""
SELECT COUNT(*)
FROM `Gears` AS `g`
""");
    }

    public override async Task Correlated_collection_with_top_level_Last_with_orderby_on_outer(bool async)
    {
        await base.Correlated_collection_with_top_level_Last_with_orderby_on_outer(async);

        AssertSql(
    """
SELECT `s`.`Nickname`, `s`.`SquadId`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM (
    SELECT TOP 1 `g`.`Nickname`, `g`.`SquadId`, `g`.`FullName`
    FROM `Gears` AS `g`
    ORDER BY `g`.`FullName`
) AS `s`
LEFT JOIN `Weapons` AS `w` ON `s`.`FullName` = `w`.`OwnerFullName`
ORDER BY `s`.`FullName`, `s`.`Nickname`, `s`.`SquadId`
""");
    }

    public override async Task Correlated_collection_with_top_level_Last_with_order_by_on_inner(bool async)
    {
        await base.Correlated_collection_with_top_level_Last_with_order_by_on_inner(async);

        AssertSql(
    """
SELECT `s`.`Nickname`, `s`.`SquadId`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM (
    SELECT TOP 1 `g`.`Nickname`, `g`.`SquadId`, `g`.`FullName`
    FROM `Gears` AS `g`
    ORDER BY `g`.`FullName` DESC
) AS `s`
LEFT JOIN `Weapons` AS `w` ON `s`.`FullName` = `w`.`OwnerFullName`
ORDER BY `s`.`FullName` DESC, `s`.`Nickname`, `s`.`SquadId`, `w`.`Name`
""");
    }

    public override async Task Null_semantics_on_nullable_bool_from_inner_join_subquery_is_fully_applied(bool async)
    {
        await base.Null_semantics_on_nullable_bool_from_inner_join_subquery_is_fully_applied(async);

        AssertSql(
            """
SELECT `s`.`Id`, `s`.`CapitalName`, `s`.`Name`, `s`.`ServerAddress`, `s`.`CommanderName`, `s`.`Eradicated`, `s`.`Discriminator`
FROM `LocustLeaders` AS `l`
INNER JOIN (
    SELECT `f`.`Id`, `f`.`CapitalName`, `f`.`Name`, `f`.`ServerAddress`, `l0`.`CommanderName`, `l0`.`Eradicated`, IIF(`l0`.`Id` IS NOT NULL, 'LocustHorde', NULL) AS `Discriminator`
    FROM `Factions` AS `f`
    LEFT JOIN `LocustHordes` AS `l0` ON `f`.`Id` = `l0`.`Id`
    WHERE `l0`.`Id` IS NOT NULL AND `f`.`Name` = 'Swarm'
) AS `s` ON `l`.`Name` = `s`.`CommanderName`
WHERE `s`.`Eradicated` = FALSE OR `s`.`Eradicated` IS NULL
""");
    }

    public override async Task Null_semantics_on_nullable_bool_from_left_join_subquery_is_fully_applied(bool async)
    {
        await base.Null_semantics_on_nullable_bool_from_left_join_subquery_is_fully_applied(async);

        AssertSql(
            """
SELECT `s`.`Id`, `s`.`CapitalName`, `s`.`Name`, `s`.`ServerAddress`, `s`.`CommanderName`, `s`.`Eradicated`, `s`.`Discriminator`
FROM `LocustLeaders` AS `l`
LEFT JOIN (
    SELECT `f`.`Id`, `f`.`CapitalName`, `f`.`Name`, `f`.`ServerAddress`, `l0`.`CommanderName`, `l0`.`Eradicated`, IIF(`l0`.`Id` IS NOT NULL, 'LocustHorde', NULL) AS `Discriminator`
    FROM `Factions` AS `f`
    LEFT JOIN `LocustHordes` AS `l0` ON `f`.`Id` = `l0`.`Id`
    WHERE `l0`.`Id` IS NOT NULL AND `f`.`Name` = 'Swarm'
) AS `s` ON `l`.`Name` = `s`.`CommanderName`
WHERE `s`.`Eradicated` = FALSE OR `s`.`Eradicated` IS NULL
""");
    }

    public override async Task Include_on_derived_type_with_order_by_and_paging(bool async)
    {
        await base.Include_on_derived_type_with_order_by_and_paging(async);

        AssertSql(
    """
SELECT `s0`.`Name`, `s0`.`LocustHordeId`, `s0`.`ThreatLevel`, `s0`.`ThreatLevelByte`, `s0`.`ThreatLevelNullableByte`, `s0`.`DefeatedByNickname`, `s0`.`DefeatedBySquadId`, `s0`.`HighCommandId`, `s0`.`Discriminator`, `s0`.`Nickname`, `s0`.`SquadId`, `s0`.`AssignedCityName`, `s0`.`CityOfBirthName`, `s0`.`FullName`, `s0`.`HasSoulPatch`, `s0`.`LeaderNickname`, `s0`.`LeaderSquadId`, `s0`.`Rank`, `s0`.`Discriminator0` AS `Discriminator`, `s0`.`Id`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM (
    SELECT TOP 10 `l`.`Name`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`ThreatLevelByte`, `l`.`ThreatLevelNullableByte`, `l0`.`DefeatedByNickname`, `l0`.`DefeatedBySquadId`, `l0`.`HighCommandId`, IIF(`l0`.`Name` IS NOT NULL, 'LocustCommander', NULL) AS `Discriminator`, `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator` AS `Discriminator0`, `t`.`Id`, `t`.`Note`
    FROM ((`LocustLeaders` AS `l`
    LEFT JOIN `LocustCommanders` AS `l0` ON `l`.`Name` = `l0`.`Name`)
    LEFT JOIN (
        SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
        FROM `Gears` AS `g`
        LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
    ) AS `s` ON `l0`.`DefeatedByNickname` = `s`.`Nickname` AND `l0`.`DefeatedBySquadId` = `s`.`SquadId`)
    LEFT JOIN `Tags` AS `t` ON (`s`.`Nickname` = `t`.`GearNickName` OR (`s`.`Nickname` IS NULL AND `t`.`GearNickName` IS NULL)) AND (`s`.`SquadId` = `t`.`GearSquadId` OR (`s`.`SquadId` IS NULL AND `t`.`GearSquadId` IS NULL))
    ORDER BY `t`.`Note`
) AS `s0`
LEFT JOIN `Weapons` AS `w` ON `s0`.`FullName` = `w`.`OwnerFullName`
ORDER BY `s0`.`Note`, `s0`.`Name`, `s0`.`Nickname`, `s0`.`SquadId`, `s0`.`Id`
""");
    }

    public override async Task Select_required_navigation_on_derived_type(bool async)
    {
        await base.Select_required_navigation_on_derived_type(async);

        AssertSql(
            """
SELECT `l1`.`Name`
FROM (`LocustLeaders` AS `l`
LEFT JOIN `LocustCommanders` AS `l0` ON `l`.`Name` = `l0`.`Name`)
LEFT JOIN `LocustHighCommands` AS `l1` ON `l0`.`HighCommandId` = `l1`.`Id`
""");
    }

    public override async Task Select_required_navigation_on_the_same_type_with_cast(bool async)
    {
        await base.Select_required_navigation_on_the_same_type_with_cast(async);

        AssertSql(
"""
SELECT `c`.`Name`
FROM `Gears` AS `g`
INNER JOIN `Cities` AS `c` ON `g`.`CityOfBirthName` = `c`.`Name`
""");
    }

    public override async Task Where_required_navigation_on_derived_type(bool async)
    {
        await base.Where_required_navigation_on_derived_type(async);

        AssertSql(
            """
SELECT `l`.`Name`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`ThreatLevelByte`, `l`.`ThreatLevelNullableByte`, `l0`.`DefeatedByNickname`, `l0`.`DefeatedBySquadId`, `l0`.`HighCommandId`, IIF(`l0`.`Name` IS NOT NULL, 'LocustCommander', NULL) AS `Discriminator`
FROM (`LocustLeaders` AS `l`
LEFT JOIN `LocustCommanders` AS `l0` ON `l`.`Name` = `l0`.`Name`)
LEFT JOIN `LocustHighCommands` AS `l1` ON `l0`.`HighCommandId` = `l1`.`Id`
WHERE `l1`.`IsOperational` = TRUE
""");
    }

    public override async Task Outer_parameter_in_join_key(bool async)
    {
        await base.Outer_parameter_in_join_key(async);

        AssertSql(
"""
SELECT [g].[Nickname], [g].[SquadId], [t1].[Note], [t1].[Id], [t1].[Nickname], [t1].[SquadId]
FROM [Gears] AS [g]
LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
OUTER APPLY (
    SELECT [t].[Note], [t].[Id], [t0].[Nickname], [t0].[SquadId]
    FROM [Tags] AS [t]
    INNER JOIN (
        SELECT [g0].[Nickname], [g0].[SquadId], [g0].[FullName]
        FROM [Gears] AS [g0]
    ) AS [t0] ON [g].[FullName] = [t0].[FullName]
) AS [t1]
WHERE [o].[Nickname] IS NOT NULL
ORDER BY [g].[Nickname], [g].[SquadId], [t1].[Id], [t1].[Nickname]
""");
    }

    public override async Task Outer_parameter_in_join_key_inner_and_outer(bool async)
    {
        await base.Outer_parameter_in_join_key_inner_and_outer(async);

        AssertSql(
"""
SELECT [g].[Nickname], [g].[SquadId], [t1].[Note], [t1].[Id], [t1].[Nickname], [t1].[SquadId]
FROM [Gears] AS [g]
LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
OUTER APPLY (
    SELECT [t].[Note], [t].[Id], [t0].[Nickname], [t0].[SquadId]
    FROM [Tags] AS [t]
    INNER JOIN (
        SELECT [g0].[Nickname], [g0].[SquadId]
        FROM [Gears] AS [g0]
    ) AS [t0] ON [g].[FullName] = [g].[Nickname]
) AS [t1]
WHERE [o].[Nickname] IS NOT NULL
ORDER BY [g].[Nickname], [g].[SquadId], [t1].[Id], [t1].[Nickname]
""");
    }

    public override async Task Outer_parameter_in_group_join_with_DefaultIfEmpty(bool async)
    {
        await base.Outer_parameter_in_group_join_with_DefaultIfEmpty(async);

        AssertSql(
"""
SELECT [g].[Nickname], [g].[SquadId], [t1].[Note], [t1].[Id], [t1].[Nickname], [t1].[SquadId]
FROM [Gears] AS [g]
LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
OUTER APPLY (
    SELECT [t].[Note], [t].[Id], [t0].[Nickname], [t0].[SquadId]
    FROM [Tags] AS [t]
    LEFT JOIN (
        SELECT [g0].[Nickname], [g0].[SquadId], [g0].[FullName]
        FROM [Gears] AS [g0]
    ) AS [t0] ON [g].[FullName] = [t0].[FullName]
) AS [t1]
WHERE [o].[Nickname] IS NOT NULL
ORDER BY [g].[Nickname], [g].[SquadId], [t1].[Id], [t1].[Nickname]
""");
    }

    public override async Task Negated_bool_ternary_inside_anonymous_type_in_projection(bool async)
    {
        await base.Negated_bool_ternary_inside_anonymous_type_in_projection(async);

        AssertSql(
            """
SELECT IIF(`s`.`HasSoulPatch` = TRUE, TRUE, IIF(`s`.`HasSoulPatch` IS NULL, TRUE, `s`.`HasSoulPatch`)) BXOR TRUE AS `c`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`HasSoulPatch`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
""");
    }

    public override async Task Order_by_entity_qsre(bool async)
    {
        await base.Order_by_entity_qsre(async);

        AssertSql(
            """
SELECT `g`.`FullName`
FROM `Gears` AS `g`
LEFT JOIN `Cities` AS `c` ON `g`.`AssignedCityName` = `c`.`Name`
ORDER BY `c`.`Name`, `g`.`Nickname` DESC
""");
    }

    public override async Task Order_by_entity_qsre_with_inheritance(bool async)
    {
        await base.Order_by_entity_qsre_with_inheritance(async);

        AssertSql(
"""
SELECT `l`.`Name`
FROM (`LocustLeaders` AS `l`
LEFT JOIN `LocustCommanders` AS `l0` ON `l`.`Name` = `l0`.`Name`)
LEFT JOIN `LocustHighCommands` AS `l1` ON `l0`.`HighCommandId` = `l1`.`Id`
WHERE (`l0`.`Name` IS NOT NULL) AND (`l0`.`HighCommandId` IS NOT NULL AND `l1`.`Id` IS NOT NULL)
ORDER BY `l1`.`Id`, `l`.`Name`
""");
    }

    public override async Task Order_by_entity_qsre_composite_key(bool async)
    {
        await base.Order_by_entity_qsre_composite_key(async);

        AssertSql(
            """
SELECT `w`.`Name`
FROM `Weapons` AS `w`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`FullName`
    FROM `Gears` AS `g`
) AS `s` ON `w`.`OwnerFullName` = `s`.`FullName`
ORDER BY `s`.`Nickname`, `s`.`SquadId`, `w`.`Id`
""");
    }

    public override async Task Order_by_entity_qsre_with_other_orderbys(bool async)
    {
        await base.Order_by_entity_qsre_with_other_orderbys(async);

        AssertSql(
            """
SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM (`Weapons` AS `w`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`FullName`
    FROM `Gears` AS `g`
) AS `s` ON `w`.`OwnerFullName` = `s`.`FullName`)
LEFT JOIN `Weapons` AS `w0` ON `w`.`SynergyWithId` = `w0`.`Id`
ORDER BY NOT (`w`.`IsAutomatic`), `s`.`Nickname` DESC, `s`.`SquadId` DESC, `w0`.`Id`, `w`.`Name`
""");
    }

    public override async Task Join_on_entity_qsre_keys(bool async)
    {
        await base.Join_on_entity_qsre_keys(async);

        AssertSql(
"""
SELECT `w`.`Name` AS `Name1`, `w0`.`Name` AS `Name2`
FROM `Weapons` AS `w`
INNER JOIN `Weapons` AS `w0` ON `w`.`Id` = `w0`.`Id`
""");
    }

    public override async Task Join_on_entity_qsre_keys_composite_key(bool async)
    {
        await base.Join_on_entity_qsre_keys_composite_key(async);

        AssertSql(
    """
SELECT `g`.`FullName` AS `GearName1`, `s`.`FullName` AS `GearName2`
FROM `Gears` AS `g`
INNER JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`FullName`
    FROM `Gears` AS `g0`
) AS `s` ON `g`.`Nickname` = `s`.`Nickname` AND `g`.`SquadId` = `s`.`SquadId`
""");
    }

    public override async Task Join_on_entity_qsre_keys_inheritance(bool async)
    {
        await base.Join_on_entity_qsre_keys_inheritance(async);

        AssertSql(
    """
SELECT `g`.`FullName` AS `GearName`, `s`.`FullName` AS `OfficerName`
FROM `Gears` AS `g`
INNER JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`FullName`
    FROM `Gears` AS `g0`
    LEFT JOIN `Officers` AS `o` ON `g0`.`Nickname` = `o`.`Nickname` AND `g0`.`SquadId` = `o`.`SquadId`
    WHERE `o`.`Nickname` IS NOT NULL
) AS `s` ON `g`.`Nickname` = `s`.`Nickname` AND `g`.`SquadId` = `s`.`SquadId`
""");
    }

    public override async Task Join_on_entity_qsre_keys_outer_key_is_navigation(bool async)
    {
        await base.Join_on_entity_qsre_keys_outer_key_is_navigation(async);

        AssertSql(
            """
SELECT `w`.`Name` AS `Name1`, `w1`.`Name` AS `Name2`
FROM (`Weapons` AS `w`
LEFT JOIN `Weapons` AS `w0` ON `w`.`SynergyWithId` = `w0`.`Id`)
LEFT JOIN `Weapons` AS `w1` ON `w0`.`Id` = `w1`.`Id`
WHERE `w0`.`Id` IS NOT NULL AND `w1`.`Id` IS NOT NULL
""");
    }

    public override async Task Join_on_entity_qsre_keys_inner_key_is_navigation(bool async)
    {
        await base.Join_on_entity_qsre_keys_inner_key_is_navigation(async);

        AssertSql(
    """
SELECT `c`.`Name` AS `CityName`, `s`.`Nickname` AS `GearNickname`
FROM `Cities` AS `c`
INNER JOIN (
    SELECT `g`.`Nickname`, `c0`.`Name`
    FROM `Gears` AS `g`
    LEFT JOIN `Cities` AS `c0` ON `g`.`AssignedCityName` = `c0`.`Name`
) AS `s` ON `c`.`Name` = `s`.`Name`
""");
    }

    public override async Task Join_on_entity_qsre_keys_inner_key_is_navigation_composite_key(bool async)
    {
        await base.Join_on_entity_qsre_keys_inner_key_is_navigation_composite_key(async);

        AssertSql(
    """
SELECT `g`.`Nickname`, `s0`.`Note`
FROM `Gears` AS `g`
INNER JOIN (
    SELECT `t`.`Note`, `s`.`Nickname`, `s`.`SquadId`
    FROM `Tags` AS `t`
    LEFT JOIN (
        SELECT `g0`.`Nickname`, `g0`.`SquadId`
        FROM `Gears` AS `g0`
    ) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
    WHERE `t`.`Note` IN ('Cole''s Tag', 'Dom''s Tag')
) AS `s0` ON `g`.`Nickname` = `s0`.`Nickname` AND `g`.`SquadId` = `s0`.`SquadId`
""");
    }

    public override async Task Join_on_entity_qsre_keys_inner_key_is_nested_navigation(bool async)
    {
        await base.Join_on_entity_qsre_keys_inner_key_is_nested_navigation(async);

        AssertSql(
    """
SELECT `s`.`Name` AS `SquadName`, `s2`.`Name` AS `WeaponName`
FROM `Squads` AS `s`
INNER JOIN (
    SELECT `w`.`Name`, `s1`.`Id` AS `Id0`
    FROM (`Weapons` AS `w`
    LEFT JOIN (
        SELECT `g`.`SquadId`, `g`.`FullName`
        FROM `Gears` AS `g`
    ) AS `s0` ON `w`.`OwnerFullName` = `s0`.`FullName`)
    LEFT JOIN `Squads` AS `s1` ON `s0`.`SquadId` = `s1`.`Id`
    WHERE `w`.`IsAutomatic` = TRUE
) AS `s2` ON `s`.`Id` = `s2`.`Id0`
""");
    }

    public override async Task GroupJoin_on_entity_qsre_keys_inner_key_is_nested_navigation(bool async)
    {
        await base.GroupJoin_on_entity_qsre_keys_inner_key_is_nested_navigation(async);

        AssertSql(
    """
SELECT `s`.`Name` AS `SquadName`, `s2`.`Name` AS `WeaponName`
FROM `Squads` AS `s`
LEFT JOIN (
    SELECT `w`.`Name`, `s1`.`Id` AS `Id0`
    FROM (`Weapons` AS `w`
    LEFT JOIN (
        SELECT `g`.`SquadId`, `g`.`FullName`
        FROM `Gears` AS `g`
    ) AS `s0` ON `w`.`OwnerFullName` = `s0`.`FullName`)
    LEFT JOIN `Squads` AS `s1` ON `s0`.`SquadId` = `s1`.`Id`
) AS `s2` ON `s`.`Id` = `s2`.`Id0`
""");
    }

    public override async Task Streaming_correlated_collection_issue_11403(bool async)
    {
        await base.Streaming_correlated_collection_issue_11403(async);

        AssertSql(
            """
SELECT `s`.`Nickname`, `s`.`SquadId`, `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
FROM (
    SELECT TOP 1 `g`.`Nickname`, `g`.`SquadId`, `g`.`FullName`
    FROM `Gears` AS `g`
    ORDER BY `g`.`Nickname`
) AS `s`
LEFT JOIN (
    SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
    FROM `Weapons` AS `w`
    WHERE `w`.`IsAutomatic` = FALSE
) AS `w0` ON `s`.`FullName` = `w0`.`OwnerFullName`
ORDER BY `s`.`Nickname`, `s`.`SquadId`, `w0`.`Id`
""");
    }

    public override async Task Project_one_value_type_from_empty_collection(bool async)
    {
        await base.Project_one_value_type_from_empty_collection(async);

        AssertSql(
            """
SELECT `s`.`Name`, IIF((
        SELECT TOP 1 `g`.`SquadId`
        FROM `Gears` AS `g`
        WHERE `s`.`Id` = `g`.`SquadId` AND `g`.`HasSoulPatch` = TRUE) IS NULL, 0, (
        SELECT TOP 1 `g`.`SquadId`
        FROM `Gears` AS `g`
        WHERE `s`.`Id` = `g`.`SquadId` AND `g`.`HasSoulPatch` = TRUE)) AS `SquadId`
FROM `Squads` AS `s`
WHERE `s`.`Name` = 'Kilo'
""");
    }

    public override async Task Project_one_value_type_converted_to_nullable_from_empty_collection(bool async)
    {
        await base.Project_one_value_type_converted_to_nullable_from_empty_collection(async);

        AssertSql(
            """
SELECT `s`.`Name`, (
    SELECT TOP 1 `g`.`SquadId`
    FROM `Gears` AS `g`
    WHERE `s`.`Id` = `g`.`SquadId` AND `g`.`HasSoulPatch` = TRUE) AS `SquadId`
FROM `Squads` AS `s`
WHERE `s`.`Name` = 'Kilo'
""");
    }

    public override async Task Project_one_value_type_with_client_projection_from_empty_collection(bool async)
    {
        await base.Project_one_value_type_with_client_projection_from_empty_collection(async);

        AssertSql(
"""
SELECT [s].[Name], [t0].[SquadId], [t0].[LeaderSquadId], [t0].[c]
FROM [Squads] AS [s]
LEFT JOIN (
    SELECT [t].[SquadId], [t].[LeaderSquadId], [t].[c]
    FROM (
        SELECT [g].[SquadId], [g].[LeaderSquadId], 1 AS [c], ROW_NUMBER() OVER(PARTITION BY [g].[SquadId] ORDER BY [g].[Nickname], [g].[SquadId]) AS [row]
        FROM [Gears] AS [g]
        WHERE [g].[HasSoulPatch] = CAST(1 AS bit)
    ) AS [t]
    WHERE [t].[row] <= 1
) AS [t0] ON [s].[Id] = [t0].[SquadId]
WHERE [s].[Name] = N'Kilo'
""");
    }

    public override async Task Filter_on_subquery_projecting_one_value_type_from_empty_collection(bool async)
    {
        await base.Filter_on_subquery_projecting_one_value_type_from_empty_collection(async);

        AssertSql(
            """
SELECT `s`.`Name`
FROM `Squads` AS `s`
WHERE `s`.`Name` = 'Kilo' AND IIF((
        SELECT TOP 1 `g`.`SquadId`
        FROM `Gears` AS `g`
        WHERE `s`.`Id` = `g`.`SquadId` AND `g`.`HasSoulPatch` = TRUE) IS NULL, 0, (
        SELECT TOP 1 `g`.`SquadId`
        FROM `Gears` AS `g`
        WHERE `s`.`Id` = `g`.`SquadId` AND `g`.`HasSoulPatch` = TRUE)) <> 0
""");
    }

    public override async Task Select_subquery_projecting_single_constant_int(bool async)
    {
        await base.Select_subquery_projecting_single_constant_int(async);

        AssertSql(
            """
SELECT `s`.`Name`, IIF((
        SELECT TOP 1 42
        FROM `Gears` AS `g`
        WHERE `s`.`Id` = `g`.`SquadId` AND `g`.`HasSoulPatch` = TRUE) IS NULL, 0, (
        SELECT TOP 1 42
        FROM `Gears` AS `g`
        WHERE `s`.`Id` = `g`.`SquadId` AND `g`.`HasSoulPatch` = TRUE)) AS `Gear`
FROM `Squads` AS `s`
""");
    }

    public override async Task Select_subquery_projecting_single_constant_string(bool async)
    {
        await base.Select_subquery_projecting_single_constant_string(async);

        AssertSql(
            """
SELECT `s`.`Name`, (
    SELECT TOP 1 'Foo'
    FROM `Gears` AS `g`
    WHERE `s`.`Id` = `g`.`SquadId` AND `g`.`HasSoulPatch` = TRUE) AS `Gear`
FROM `Squads` AS `s`
""");
    }

    public override async Task Select_subquery_projecting_single_constant_bool(bool async)
    {
        await base.Select_subquery_projecting_single_constant_bool(async);

        AssertSql(
            """
SELECT `s`.`Name`, IIF((
        SELECT TOP 1 TRUE
        FROM `Gears` AS `g`
        WHERE `s`.`Id` = `g`.`SquadId` AND `g`.`HasSoulPatch` = TRUE) IS NULL, FALSE, (
        SELECT TOP 1 TRUE
        FROM `Gears` AS `g`
        WHERE `s`.`Id` = `g`.`SquadId` AND `g`.`HasSoulPatch` = TRUE)) AS `Gear`
FROM `Squads` AS `s`
""");
    }

    public override async Task Select_subquery_projecting_single_constant_inside_anonymous(bool async)
    {
        await base.Select_subquery_projecting_single_constant_inside_anonymous(async);

        AssertSql(
"""
SELECT [s].[Name], [t0].[One]
FROM [Squads] AS [s]
LEFT JOIN (
    SELECT [t].[One], [t].[SquadId]
    FROM (
        SELECT 1 AS [One], [g].[SquadId], ROW_NUMBER() OVER(PARTITION BY [g].[SquadId] ORDER BY [g].[Nickname], [g].[SquadId]) AS [row]
        FROM [Gears] AS [g]
        WHERE [g].[HasSoulPatch] = CAST(1 AS bit)
    ) AS [t]
    WHERE [t].[row] <= 1
) AS [t0] ON [s].[Id] = [t0].[SquadId]
""");
    }

    public override async Task Select_subquery_projecting_multiple_constants_inside_anonymous(bool async)
    {
        await base.Select_subquery_projecting_multiple_constants_inside_anonymous(async);

        AssertSql(
"""
SELECT `s`.`Id`, `s`.`Banner`, `s`.`Banner5`, `s`.`InternalNumber`, `s`.`Name`, `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`, `t`.`Discriminator`
FROM `Squads` AS `s`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
) AS `t` ON `s`.`Id` = `t`.`SquadId`
ORDER BY `s`.`Id`, `t`.`Nickname`
""");
    }

    public override async Task Include_with_order_by_constant(bool async)
    {
        await base.Include_with_order_by_constant(async);

        AssertSql(
    """
SELECT `s`.`Id`, `s`.`Banner`, `s`.`Banner5`, `s`.`InternalNumber`, `s`.`Name`, `s0`.`Nickname`, `s0`.`SquadId`, `s0`.`AssignedCityName`, `s0`.`CityOfBirthName`, `s0`.`FullName`, `s0`.`HasSoulPatch`, `s0`.`LeaderNickname`, `s0`.`LeaderSquadId`, `s0`.`Rank`, `s0`.`Discriminator`
FROM `Squads` AS `s`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
) AS `s0` ON `s`.`Id` = `s0`.`SquadId`
ORDER BY `s`.`Id`, `s0`.`Nickname`
""");
    }

    public override async Task Correlated_collection_order_by_constant(bool async)
    {
        await base.Correlated_collection_order_by_constant(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `w`.`Name`, `w`.`Id`
FROM `Gears` AS `g`
LEFT JOIN `Weapons` AS `w` ON `g`.`FullName` = `w`.`OwnerFullName`
ORDER BY `g`.`Nickname`, `g`.`SquadId`
""");
    }

    public override async Task Select_subquery_projecting_single_constant_null_of_non_mapped_type(bool async)
    {
        await base.Select_subquery_projecting_single_constant_null_of_non_mapped_type(async);

        AssertSql(
"""
SELECT [s].[Name], [t0].[c]
FROM [Squads] AS [s]
LEFT JOIN (
    SELECT [t].[c], [t].[SquadId]
    FROM (
        SELECT 1 AS [c], [g].[SquadId], ROW_NUMBER() OVER(PARTITION BY [g].[SquadId] ORDER BY [g].[Nickname], [g].[SquadId]) AS [row]
        FROM [Gears] AS [g]
        WHERE [g].[HasSoulPatch] = CAST(1 AS bit)
    ) AS [t]
    WHERE [t].[row] <= 1
) AS [t0] ON [s].[Id] = [t0].[SquadId]
""");
    }

    public override async Task Select_subquery_projecting_single_constant_of_non_mapped_type(bool async)
    {
        await base.Select_subquery_projecting_single_constant_of_non_mapped_type(async);

        AssertSql(
"""
SELECT [s].[Name], [t0].[c]
FROM [Squads] AS [s]
LEFT JOIN (
    SELECT [t].[c], [t].[SquadId]
    FROM (
        SELECT 1 AS [c], [g].[SquadId], ROW_NUMBER() OVER(PARTITION BY [g].[SquadId] ORDER BY [g].[Nickname], [g].[SquadId]) AS [row]
        FROM [Gears] AS [g]
        WHERE [g].[HasSoulPatch] = CAST(1 AS bit)
    ) AS [t]
    WHERE [t].[row] <= 1
) AS [t0] ON [s].[Id] = [t0].[SquadId]
""");
    }

    public override async Task Include_collection_OrderBy_aggregate(bool async)
    {
        await base.Include_collection_OrderBy_aggregate(async);

        AssertSql(
            """
SELECT `s0`.`Nickname`, `s0`.`SquadId`, `s0`.`AssignedCityName`, `s0`.`CityOfBirthName`, `s0`.`FullName`, `s0`.`HasSoulPatch`, `s0`.`LeaderNickname`, `s0`.`LeaderSquadId`, `s0`.`Rank`, `s0`.`Discriminator`, `s0`.`Nickname0`, `s0`.`SquadId0`, `s0`.`AssignedCityName0`, `s0`.`CityOfBirthName0`, `s0`.`FullName0`, `s0`.`HasSoulPatch0`, `s0`.`LeaderNickname0`, `s0`.`LeaderSquadId0`, `s0`.`Rank0`, `s0`.`Discriminator0`, `s0`.`c`
FROM (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `s`.`Nickname` AS `Nickname0`, `s`.`SquadId` AS `SquadId0`, `s`.`AssignedCityName` AS `AssignedCityName0`, `s`.`CityOfBirthName` AS `CityOfBirthName0`, `s`.`FullName` AS `FullName0`, `s`.`HasSoulPatch` AS `HasSoulPatch0`, `s`.`LeaderNickname` AS `LeaderNickname0`, `s`.`LeaderSquadId` AS `LeaderSquadId0`, `s`.`Rank` AS `Rank0`, `s`.`Discriminator` AS `Discriminator0`, (
        SELECT COUNT(*)
        FROM `Weapons` AS `w`
        WHERE `g`.`FullName` = `w`.`OwnerFullName`) AS `c`
    FROM (`Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
    LEFT JOIN (
        SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
        FROM `Gears` AS `g0`
        LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`
    ) AS `s` ON `g`.`Nickname` = `s`.`LeaderNickname` AND `g`.`SquadId` = `s`.`LeaderSquadId`
    WHERE `o`.`Nickname` IS NOT NULL
) AS `s0`
ORDER BY `s0`.`c`, `s0`.`Nickname`, `s0`.`SquadId`, `s0`.`Nickname0`
""");
    }

    public override async Task Include_collection_with_complex_OrderBy2(bool async)
    {
        await base.Include_collection_with_complex_OrderBy2(async);

        AssertSql(
"""
SELECT `s0`.`Nickname`, `s0`.`SquadId`, `s0`.`AssignedCityName`, `s0`.`CityOfBirthName`, `s0`.`FullName`, `s0`.`HasSoulPatch`, `s0`.`LeaderNickname`, `s0`.`LeaderSquadId`, `s0`.`Rank`, `s0`.`Discriminator`, `s0`.`Nickname0`, `s0`.`SquadId0`, `s0`.`AssignedCityName0`, `s0`.`CityOfBirthName0`, `s0`.`FullName0`, `s0`.`HasSoulPatch0`, `s0`.`LeaderNickname0`, `s0`.`LeaderSquadId0`, `s0`.`Rank0`, `s0`.`Discriminator0`, `s0`.`c`
FROM (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `s`.`Nickname` AS `Nickname0`, `s`.`SquadId` AS `SquadId0`, `s`.`AssignedCityName` AS `AssignedCityName0`, `s`.`CityOfBirthName` AS `CityOfBirthName0`, `s`.`FullName` AS `FullName0`, `s`.`HasSoulPatch` AS `HasSoulPatch0`, `s`.`LeaderNickname` AS `LeaderNickname0`, `s`.`LeaderSquadId` AS `LeaderSquadId0`, `s`.`Rank` AS `Rank0`, `s`.`Discriminator` AS `Discriminator0`, (
        SELECT TOP 1 `w`.`IsAutomatic`
        FROM `Weapons` AS `w`
        WHERE `g`.`FullName` = `w`.`OwnerFullName`
        ORDER BY `w`.`Id`) AS `c`
    FROM (`Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
    LEFT JOIN (
        SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
        FROM `Gears` AS `g0`
        LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`
    ) AS `s` ON `g`.`Nickname` = `s`.`LeaderNickname` AND `g`.`SquadId` = `s`.`LeaderSquadId`
    WHERE `o`.`Nickname` IS NOT NULL
) AS `s0`
ORDER BY NOT (`s0`.`c`), `s0`.`Nickname`, `s0`.`SquadId`, `s0`.`Nickname0`
""");
    }

    public override async Task Include_collection_with_complex_OrderBy3(bool async)
    {
        await base.Include_collection_with_complex_OrderBy3(async);

        AssertSql(
"""
SELECT `s0`.`Nickname`, `s0`.`SquadId`, `s0`.`AssignedCityName`, `s0`.`CityOfBirthName`, `s0`.`FullName`, `s0`.`HasSoulPatch`, `s0`.`LeaderNickname`, `s0`.`LeaderSquadId`, `s0`.`Rank`, `s0`.`Discriminator`, `s0`.`Nickname0`, `s0`.`SquadId0`, `s0`.`AssignedCityName0`, `s0`.`CityOfBirthName0`, `s0`.`FullName0`, `s0`.`HasSoulPatch0`, `s0`.`LeaderNickname0`, `s0`.`LeaderSquadId0`, `s0`.`Rank0`, `s0`.`Discriminator0`, `s0`.`c`
FROM (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `s`.`Nickname` AS `Nickname0`, `s`.`SquadId` AS `SquadId0`, `s`.`AssignedCityName` AS `AssignedCityName0`, `s`.`CityOfBirthName` AS `CityOfBirthName0`, `s`.`FullName` AS `FullName0`, `s`.`HasSoulPatch` AS `HasSoulPatch0`, `s`.`LeaderNickname` AS `LeaderNickname0`, `s`.`LeaderSquadId` AS `LeaderSquadId0`, `s`.`Rank` AS `Rank0`, `s`.`Discriminator` AS `Discriminator0`, IIF((
            SELECT TOP 1 `w`.`IsAutomatic`
            FROM `Weapons` AS `w`
            WHERE `g`.`FullName` = `w`.`OwnerFullName`
            ORDER BY `w`.`Id`) IS NULL, FALSE, (
            SELECT TOP 1 `w`.`IsAutomatic`
            FROM `Weapons` AS `w`
            WHERE `g`.`FullName` = `w`.`OwnerFullName`
            ORDER BY `w`.`Id`)) AS `c`
    FROM (`Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
    LEFT JOIN (
        SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
        FROM `Gears` AS `g0`
        LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`
    ) AS `s` ON `g`.`Nickname` = `s`.`LeaderNickname` AND `g`.`SquadId` = `s`.`LeaderSquadId`
    WHERE `o`.`Nickname` IS NOT NULL
) AS `s0`
ORDER BY NOT (`s0`.`c`), `s0`.`Nickname`, `s0`.`SquadId`, `s0`.`Nickname0`
""");
    }

    public override async Task Correlated_collection_with_complex_OrderBy(bool async)
    {
        await base.Correlated_collection_with_complex_OrderBy(async);

        AssertSql(
            """
SELECT `s0`.`Nickname`, `s0`.`SquadId`, `s0`.`Nickname0`, `s0`.`SquadId0`, `s0`.`AssignedCityName`, `s0`.`CityOfBirthName`, `s0`.`FullName`, `s0`.`HasSoulPatch`, `s0`.`LeaderNickname`, `s0`.`LeaderSquadId`, `s0`.`Rank`, `s0`.`Discriminator`, `s0`.`c`
FROM (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `s`.`Nickname` AS `Nickname0`, `s`.`SquadId` AS `SquadId0`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`, (
        SELECT COUNT(*)
        FROM `Weapons` AS `w`
        WHERE `g`.`FullName` = `w`.`OwnerFullName`) AS `c`
    FROM (`Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
    LEFT JOIN (
        SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
        FROM `Gears` AS `g0`
        LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`
        WHERE `g0`.`HasSoulPatch` = FALSE
    ) AS `s` ON `g`.`Nickname` = `s`.`LeaderNickname` AND `g`.`SquadId` = `s`.`LeaderSquadId`
    WHERE `o`.`Nickname` IS NOT NULL
) AS `s0`
ORDER BY `s0`.`c`, `s0`.`Nickname`, `s0`.`SquadId`, `s0`.`Nickname0`
""");
    }

    public override async Task Correlated_collection_with_very_complex_order_by(bool async)
    {
        await base.Correlated_collection_with_very_complex_order_by(async);

        AssertSql(
            """
SELECT `s0`.`Nickname`, `s0`.`SquadId`, `s0`.`Nickname0`, `s0`.`SquadId0`, `s0`.`AssignedCityName`, `s0`.`CityOfBirthName`, `s0`.`FullName`, `s0`.`HasSoulPatch`, `s0`.`LeaderNickname`, `s0`.`LeaderSquadId`, `s0`.`Rank`, `s0`.`Discriminator`, `s0`.`c`
FROM (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `s`.`Nickname` AS `Nickname0`, `s`.`SquadId` AS `SquadId0`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`, (
        SELECT COUNT(*)
        FROM `Weapons` AS `w`
        WHERE `g`.`FullName` = `w`.`OwnerFullName` AND `w`.`IsAutomatic` = IIF((
                SELECT TOP 1 `g0`.`HasSoulPatch`
                FROM `Gears` AS `g0`
                WHERE `g0`.`Nickname` = 'Marcus') IS NULL, FALSE, (
                SELECT TOP 1 `g0`.`HasSoulPatch`
                FROM `Gears` AS `g0`
                WHERE `g0`.`Nickname` = 'Marcus'))) AS `c`
    FROM (`Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
    LEFT JOIN (
        SELECT `g1`.`Nickname`, `g1`.`SquadId`, `g1`.`AssignedCityName`, `g1`.`CityOfBirthName`, `g1`.`FullName`, `g1`.`HasSoulPatch`, `g1`.`LeaderNickname`, `g1`.`LeaderSquadId`, `g1`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
        FROM `Gears` AS `g1`
        LEFT JOIN `Officers` AS `o0` ON `g1`.`Nickname` = `o0`.`Nickname` AND `g1`.`SquadId` = `o0`.`SquadId`
        WHERE `g1`.`HasSoulPatch` = FALSE
    ) AS `s` ON `g`.`Nickname` = `s`.`LeaderNickname` AND `g`.`SquadId` = `s`.`LeaderSquadId`
    WHERE `o`.`Nickname` IS NOT NULL
) AS `s0`
ORDER BY `s0`.`c`, `s0`.`Nickname`, `s0`.`SquadId`, `s0`.`Nickname0`
""");
    }

    public override async Task Cast_to_derived_type_after_OfType_works(bool async)
    {
        await base.Cast_to_derived_type_after_OfType_works(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE `o`.`Nickname` IS NOT NULL
""");
    }

    public override async Task Select_subquery_boolean(bool async)
    {
        await base.Select_subquery_boolean(async);

        AssertSql(
"""
SELECT IIF((
        SELECT TOP 1 `w`.`IsAutomatic`
        FROM `Weapons` AS `w`
        WHERE `g`.`FullName` = `w`.`OwnerFullName`
        ORDER BY `w`.`Id`) IS NULL, FALSE, (
        SELECT TOP 1 `w`.`IsAutomatic`
        FROM `Weapons` AS `w`
        WHERE `g`.`FullName` = `w`.`OwnerFullName`
        ORDER BY `w`.`Id`))
FROM `Gears` AS `g`
""");
    }

    public override async Task Select_subquery_boolean_with_pushdown(bool async)
    {
        await base.Select_subquery_boolean_with_pushdown(async);

        AssertSql(
"""
SELECT (
    SELECT TOP 1 `w`.`IsAutomatic`
    FROM `Weapons` AS `w`
    WHERE `g`.`FullName` = `w`.`OwnerFullName`
    ORDER BY `w`.`Id`)
FROM `Gears` AS `g`
""");
    }

    public override async Task Select_subquery_int_with_inside_cast_and_coalesce(bool async)
    {
        await base.Select_subquery_int_with_inside_cast_and_coalesce(async);

        AssertSql(
        """
SELECT IIF((
        SELECT TOP 1 `w`.`Id`
        FROM `Weapons` AS `w`
        WHERE `g`.`FullName` = `w`.`OwnerFullName`
        ORDER BY `w`.`Id`) IS NULL, 42, (
        SELECT TOP 1 `w`.`Id`
        FROM `Weapons` AS `w`
        WHERE `g`.`FullName` = `w`.`OwnerFullName`
        ORDER BY `w`.`Id`))
FROM `Gears` AS `g`
""");
    }

    public override async Task Select_subquery_int_with_outside_cast_and_coalesce(bool async)
    {
        await base.Select_subquery_int_with_outside_cast_and_coalesce(async);

        AssertSql(
            """
SELECT IIF((
        SELECT TOP 1 `w`.`Id`
        FROM `Weapons` AS `w`
        WHERE `g`.`FullName` = `w`.`OwnerFullName`
        ORDER BY `w`.`Id`) IS NULL, 0, (
        SELECT TOP 1 `w`.`Id`
        FROM `Weapons` AS `w`
        WHERE `g`.`FullName` = `w`.`OwnerFullName`
        ORDER BY `w`.`Id`))
FROM `Gears` AS `g`
""");
    }

    public override async Task Select_subquery_int_with_pushdown_and_coalesce(bool async)
    {
        await base.Select_subquery_int_with_pushdown_and_coalesce(async);

        AssertSql(
"""
SELECT IIF((
        SELECT TOP 1 `w`.`Id`
        FROM `Weapons` AS `w`
        WHERE `g`.`FullName` = `w`.`OwnerFullName`
        ORDER BY `w`.`Id`) IS NULL, 42, (
        SELECT TOP 1 `w`.`Id`
        FROM `Weapons` AS `w`
        WHERE `g`.`FullName` = `w`.`OwnerFullName`
        ORDER BY `w`.`Id`))
FROM `Gears` AS `g`
""");
    }

    public override async Task Select_subquery_int_with_pushdown_and_coalesce2(bool async)
    {
        await base.Select_subquery_int_with_pushdown_and_coalesce2(async);

        AssertSql(
"""
SELECT IIF((
        SELECT TOP 1 `w`.`Id`
        FROM `Weapons` AS `w`
        WHERE `g`.`FullName` = `w`.`OwnerFullName`
        ORDER BY `w`.`Id`) IS NULL, (
        SELECT TOP 1 `w0`.`Id`
        FROM `Weapons` AS `w0`
        WHERE `g`.`FullName` = `w0`.`OwnerFullName`
        ORDER BY `w0`.`Id`), (
        SELECT TOP 1 `w`.`Id`
        FROM `Weapons` AS `w`
        WHERE `g`.`FullName` = `w`.`OwnerFullName`
        ORDER BY `w`.`Id`))
FROM `Gears` AS `g`
""");
    }

    public override async Task Select_subquery_boolean_empty(bool async)
    {
        await base.Select_subquery_boolean_empty(async);

        AssertSql(
"""
SELECT IIF((
        SELECT TOP 1 `w`.`IsAutomatic`
        FROM `Weapons` AS `w`
        WHERE `g`.`FullName` = `w`.`OwnerFullName` AND `w`.`Name` = 'BFG'
        ORDER BY `w`.`Id`) IS NULL, FALSE, (
        SELECT TOP 1 `w`.`IsAutomatic`
        FROM `Weapons` AS `w`
        WHERE `g`.`FullName` = `w`.`OwnerFullName` AND `w`.`Name` = 'BFG'
        ORDER BY `w`.`Id`))
FROM `Gears` AS `g`
""");
    }

    public override async Task Select_subquery_boolean_empty_with_pushdown(bool async)
    {
        await base.Select_subquery_boolean_empty_with_pushdown(async);

        AssertSql(
            """
SELECT (
    SELECT TOP 1 `w`.`IsAutomatic`
    FROM `Weapons` AS `w`
    WHERE `g`.`FullName` = `w`.`OwnerFullName` AND `w`.`Name` = 'BFG'
    ORDER BY `w`.`Id`)
FROM `Gears` AS `g`
""");
    }

    public override async Task Select_subquery_distinct_singleordefault_boolean1(bool async)
    {
        await base.Select_subquery_distinct_singleordefault_boolean1(async);

        AssertSql(
"""
SELECT COALESCE((
    SELECT TOP(1) [t].[IsAutomatic]
    FROM (
        SELECT DISTINCT [w].[Id], [w].[AmmunitionType], [w].[IsAutomatic], [w].[Name], [w].[OwnerFullName], [w].[SynergyWithId]
        FROM [Weapons] AS [w]
        WHERE [g].[FullName] = [w].[OwnerFullName] AND ([w].[Name] LIKE N'%Lancer%')
    ) AS [t]), CAST(0 AS bit))
FROM [Gears] AS [g]
WHERE [g].[HasSoulPatch] = CAST(1 AS bit)
""");
    }

    public override async Task Select_subquery_distinct_singleordefault_boolean2(bool async)
    {
        await base.Select_subquery_distinct_singleordefault_boolean2(async);

        AssertSql(
"""
SELECT IIF((
        SELECT DISTINCT TOP 1 `w`.`IsAutomatic`
        FROM `Weapons` AS `w`
        WHERE `g`.`FullName` = `w`.`OwnerFullName` AND (`w`.`Name` LIKE '%Lancer%')) IS NULL, FALSE, (
        SELECT DISTINCT TOP 1 `w`.`IsAutomatic`
        FROM `Weapons` AS `w`
        WHERE `g`.`FullName` = `w`.`OwnerFullName` AND (`w`.`Name` LIKE '%Lancer%')))
FROM `Gears` AS `g`
WHERE `g`.`HasSoulPatch` = TRUE
""");
    }

    public override async Task Select_subquery_distinct_singleordefault_boolean_with_pushdown(bool async)
    {
        await base.Select_subquery_distinct_singleordefault_boolean_with_pushdown(async);

        AssertSql(
"""
SELECT (
    SELECT TOP(1) [t].[IsAutomatic]
    FROM (
        SELECT DISTINCT [w].[Id], [w].[AmmunitionType], [w].[IsAutomatic], [w].[Name], [w].[OwnerFullName], [w].[SynergyWithId]
        FROM [Weapons] AS [w]
        WHERE [g].[FullName] = [w].[OwnerFullName] AND ([w].[Name] LIKE N'%Lancer%')
    ) AS [t])
FROM [Gears] AS [g]
WHERE [g].[HasSoulPatch] = CAST(1 AS bit)
""");
    }

    public override async Task Select_subquery_distinct_singleordefault_boolean_empty1(bool async)
    {
        await base.Select_subquery_distinct_singleordefault_boolean_empty1(async);

        AssertSql(
"""
SELECT COALESCE((
    SELECT TOP(1) [t].[IsAutomatic]
    FROM (
        SELECT DISTINCT [w].[Id], [w].[AmmunitionType], [w].[IsAutomatic], [w].[Name], [w].[OwnerFullName], [w].[SynergyWithId]
        FROM [Weapons] AS [w]
        WHERE [g].[FullName] = [w].[OwnerFullName] AND [w].[Name] = N'BFG'
    ) AS [t]), CAST(0 AS bit))
FROM [Gears] AS [g]
WHERE [g].[HasSoulPatch] = CAST(1 AS bit)
""");
    }

    public override async Task Select_subquery_distinct_singleordefault_boolean_empty2(bool async)
    {
        await base.Select_subquery_distinct_singleordefault_boolean_empty2(async);

        AssertSql(
"""
SELECT IIF((
        SELECT DISTINCT TOP 1 `w`.`IsAutomatic`
        FROM `Weapons` AS `w`
        WHERE `g`.`FullName` = `w`.`OwnerFullName` AND `w`.`Name` = 'BFG') IS NULL, FALSE, (
        SELECT DISTINCT TOP 1 `w`.`IsAutomatic`
        FROM `Weapons` AS `w`
        WHERE `g`.`FullName` = `w`.`OwnerFullName` AND `w`.`Name` = 'BFG'))
FROM `Gears` AS `g`
WHERE `g`.`HasSoulPatch` = TRUE
""");
    }

    public override async Task Select_subquery_distinct_singleordefault_boolean_empty_with_pushdown(bool async)
    {
        await base.Select_subquery_distinct_singleordefault_boolean_empty_with_pushdown(async);

        AssertSql(
"""
SELECT (
    SELECT TOP(1) [t].[IsAutomatic]
    FROM (
        SELECT DISTINCT [w].[Id], [w].[AmmunitionType], [w].[IsAutomatic], [w].[Name], [w].[OwnerFullName], [w].[SynergyWithId]
        FROM [Weapons] AS [w]
        WHERE [g].[FullName] = [w].[OwnerFullName] AND [w].[Name] = N'BFG'
    ) AS [t])
FROM [Gears] AS [g]
WHERE [g].[HasSoulPatch] = CAST(1 AS bit)
""");
    }

    public override async Task Cast_subquery_to_base_type_using_typed_ToList(bool async)
    {
        await base.Cast_subquery_to_base_type_using_typed_ToList(async);

        AssertSql(
            """
SELECT `c`.`Name`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Nickname`, `s`.`Rank`, `s`.`SquadId`
FROM `Cities` AS `c`
LEFT JOIN (
    SELECT `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Nickname`, `g`.`Rank`, `g`.`SquadId`, `g`.`AssignedCityName`
    FROM `Gears` AS `g`
) AS `s` ON `c`.`Name` = `s`.`AssignedCityName`
WHERE `c`.`Name` = 'Ephyra'
ORDER BY `c`.`Name`, `s`.`Nickname`
""");
    }

    public override async Task Cast_ordered_subquery_to_base_type_using_typed_ToArray(bool async)
    {
        await base.Cast_ordered_subquery_to_base_type_using_typed_ToArray(async);

        AssertSql(
            """
SELECT `c`.`Name`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Nickname`, `s`.`Rank`, `s`.`SquadId`
FROM `Cities` AS `c`
LEFT JOIN (
    SELECT `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Nickname`, `g`.`Rank`, `g`.`SquadId`, `g`.`AssignedCityName`
    FROM `Gears` AS `g`
) AS `s` ON `c`.`Name` = `s`.`AssignedCityName`
WHERE `c`.`Name` = 'Ephyra'
ORDER BY `c`.`Name`, `s`.`Nickname` DESC
""");
    }

    public override async Task Correlated_collection_with_complex_order_by_funcletized_to_constant_bool(bool async)
    {
        await base.Correlated_collection_with_complex_order_by_funcletized_to_constant_bool(async);

        AssertSql(
            """
SELECT `g`.`Nickname`, `g`.`SquadId`, `w`.`Name`, `w`.`Id`
FROM `Gears` AS `g`
LEFT JOIN `Weapons` AS `w` ON `g`.`FullName` = `w`.`OwnerFullName`
ORDER BY `g`.`Nickname`, `g`.`SquadId`
""");
    }

    public override async Task Double_order_by_on_nullable_bool_coming_from_optional_navigation(bool async)
    {
        await base.Double_order_by_on_nullable_bool_coming_from_optional_navigation(async);

        AssertSql(
"""
SELECT `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
FROM `Weapons` AS `w`
LEFT JOIN `Weapons` AS `w0` ON `w`.`SynergyWithId` = `w0`.`Id`
ORDER BY NOT (`w0`.`IsAutomatic`), `w0`.`Id`
""");
    }

    public override async Task Double_order_by_on_Like(bool async)
    {
        await base.Double_order_by_on_Like(async);

        AssertSql(
"""
SELECT `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
FROM `Weapons` AS `w`
LEFT JOIN `Weapons` AS `w0` ON `w`.`SynergyWithId` = `w0`.`Id`
ORDER BY NOT (IIF((`w0`.`Name` LIKE '%Lancer') AND `w0`.`Name` IS NOT NULL, TRUE, FALSE))
""");
    }

    public override async Task Double_order_by_on_is_null(bool async)
    {
        await base.Double_order_by_on_is_null(async);

        AssertSql(
            """
SELECT `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
FROM `Weapons` AS `w`
LEFT JOIN `Weapons` AS `w0` ON `w`.`SynergyWithId` = `w0`.`Id`
ORDER BY NOT (IIF(`w0`.`Name` IS NULL, TRUE, FALSE))
""");
    }

    public override async Task Double_order_by_on_string_compare(bool async)
    {
        await base.Double_order_by_on_string_compare(async);

        AssertSql(
"""
SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
ORDER BY NOT (IIF(`w`.`Name` = 'Marcus'' Lancer' AND `w`.`Name` IS NOT NULL, TRUE, FALSE)), `w`.`Id`
""");
    }

    public override async Task Double_order_by_binary_expression(bool async)
    {
        await base.Double_order_by_binary_expression(async);

        AssertSql(
            """
SELECT `w0`.`Binary`
FROM (
    SELECT `w`.`Id` + 2 AS `Binary`
    FROM `Weapons` AS `w`
) AS `w0`
ORDER BY `w0`.`Binary`
""");
    }

    public override async Task String_compare_with_null_conditional_argument(bool async)
    {
        await base.String_compare_with_null_conditional_argument(async);

        AssertSql(
"""
SELECT `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
FROM `Weapons` AS `w`
LEFT JOIN `Weapons` AS `w0` ON `w`.`SynergyWithId` = `w0`.`Id`
ORDER BY NOT (IIF(`w0`.`Name` = 'Marcus'' Lancer' AND `w0`.`Name` IS NOT NULL, TRUE, FALSE))
""");
    }

    public override async Task String_compare_with_null_conditional_argument2(bool async)
    {
        await base.String_compare_with_null_conditional_argument2(async);

        AssertSql(
"""
SELECT `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
FROM `Weapons` AS `w`
LEFT JOIN `Weapons` AS `w0` ON `w`.`SynergyWithId` = `w0`.`Id`
ORDER BY NOT (IIF('Marcus'' Lancer' = `w0`.`Name` AND `w0`.`Name` IS NOT NULL, TRUE, FALSE))
""");
    }

    public override async Task String_concat_with_null_conditional_argument(bool async)
    {
        await base.String_concat_with_null_conditional_argument(async);

        AssertSql(
"""
SELECT `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
FROM `Weapons` AS `w`
LEFT JOIN `Weapons` AS `w0` ON `w`.`SynergyWithId` = `w0`.`Id`
ORDER BY IIF(`w0`.`Name` IS NULL, '', `w0`.`Name`) & (5 & '')
""");
    }

    public override async Task String_concat_with_null_conditional_argument2(bool async)
    {
        await base.String_concat_with_null_conditional_argument2(async);

        AssertSql(
"""
SELECT `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
FROM `Weapons` AS `w`
LEFT JOIN `Weapons` AS `w0` ON `w`.`SynergyWithId` = `w0`.`Id`
ORDER BY IIF(`w0`.`Name` IS NULL, '', `w0`.`Name`) & 'Marcus'' Lancer'
""");
    }

    public override async Task String_concat_on_various_types(bool async)
    {
        await base.String_concat_on_various_types(async);

        AssertSql(
"""
SELECT ('HasSoulPatch ' & (`g`.`HasSoulPatch` & '')) & ' HasSoulPatch' AS `HasSoulPatch`, ('Rank ' & (`g`.`Rank` & '')) & ' Rank' AS `Rank`, ('SquadId ' & (`g`.`SquadId` & '')) & ' SquadId' AS `SquadId`, ('Rating ' & IIF((`m`.`Rating` & '') IS NULL, '', (`m`.`Rating` & ''))) & ' Rating' AS `Rating`, ('Timeline ' & (`m`.`Timeline` & '')) & ' Timeline' AS `Timeline`
FROM `Gears` AS `g`,
`Missions` AS `m`
ORDER BY `g`.`Nickname`, `m`.`Id`
""");
    }

    public override async Task Time_of_day_datetimeoffset(bool async)
    {
        await base.Time_of_day_datetimeoffset(async);

        AssertSql(
            """
SELECT TIMEVALUE(`m`.`Timeline`)
FROM `Missions` AS `m`
""");
    }

    public override async Task GroupBy_Property_Include_Select_Average(bool async)
    {
        await base.GroupBy_Property_Include_Select_Average(async);

        AssertSql(
"""
SELECT AVG(CDBL(`g`.`SquadId`))
FROM `Gears` AS `g`
GROUP BY `g`.`Rank`
""");
    }

    public override async Task GroupBy_Property_Include_Select_Sum(bool async)
    {
        await base.GroupBy_Property_Include_Select_Sum(async);

        AssertSql(
"""
SELECT IIF(SUM(`g`.`SquadId`) IS NULL, 0, SUM(`g`.`SquadId`))
FROM `Gears` AS `g`
GROUP BY `g`.`Rank`
""");
    }

    public override async Task GroupBy_Property_Include_Select_Count(bool async)
    {
        await base.GroupBy_Property_Include_Select_Count(async);

        AssertSql(
"""
SELECT COUNT(*)
FROM `Gears` AS `g`
GROUP BY `g`.`Rank`
""");
    }

    public override async Task GroupBy_Property_Include_Select_LongCount(bool async)
    {
        await base.GroupBy_Property_Include_Select_LongCount(async);

        AssertSql(
"""
SELECT COUNT(*)
FROM `Gears` AS `g`
GROUP BY `g`.`Rank`
""");
    }

    public override async Task GroupBy_Property_Include_Select_Min(bool async)
    {
        await base.GroupBy_Property_Include_Select_Min(async);

        AssertSql(
"""
SELECT MIN(`g`.`SquadId`)
FROM `Gears` AS `g`
GROUP BY `g`.`Rank`
""");
    }

    public override async Task GroupBy_Property_Include_Aggregate_with_anonymous_selector(bool async)
    {
        await base.GroupBy_Property_Include_Aggregate_with_anonymous_selector(async);

        AssertSql(
"""
SELECT `g`.`Nickname` AS `Key`, COUNT(*) AS `c`
FROM `Gears` AS `g`
GROUP BY `g`.`Nickname`
ORDER BY `g`.`Nickname`
""");
    }

    public override async Task Group_by_with_include_with_entity_in_result_selector(bool async)
    {
        await base.Group_by_with_include_with_entity_in_result_selector(async);

        AssertSql(
"""
SELECT [t].[Rank], [t].[c], [t0].[Nickname], [t0].[SquadId], [t0].[AssignedCityName], [t0].[CityOfBirthName], [t0].[FullName], [t0].[HasSoulPatch], [t0].[LeaderNickname], [t0].[LeaderSquadId], [t0].[Rank], [t0].[Discriminator], [t0].[Name], [t0].[Location], [t0].[Nation]
FROM (
    SELECT [g].[Rank], COUNT(*) AS [c]
    FROM [Gears] AS [g]
    GROUP BY [g].[Rank]
) AS [t]
LEFT JOIN (
    SELECT [t1].[Nickname], [t1].[SquadId], [t1].[AssignedCityName], [t1].[CityOfBirthName], [t1].[FullName], [t1].[HasSoulPatch], [t1].[LeaderNickname], [t1].[LeaderSquadId], [t1].[Rank], [t1].[Discriminator], [t1].[Name], [t1].[Location], [t1].[Nation]
    FROM (
        SELECT [g0].[Nickname], [g0].[SquadId], [g0].[AssignedCityName], [g0].[CityOfBirthName], [g0].[FullName], [g0].[HasSoulPatch], [g0].[LeaderNickname], [g0].[LeaderSquadId], [g0].[Rank], CASE
            WHEN [o0].[Nickname] IS NOT NULL THEN N'Officer'
        END AS [Discriminator], [c].[Name], [c].[Location], [c].[Nation], ROW_NUMBER() OVER(PARTITION BY [g0].[Rank] ORDER BY [g0].[Nickname]) AS [row]
        FROM [Gears] AS [g0]
        LEFT JOIN [Officers] AS [o0] ON [g0].[Nickname] = [o0].[Nickname] AND [g0].[SquadId] = [o0].[SquadId]
        INNER JOIN [Cities] AS [c] ON [g0].[CityOfBirthName] = [c].[Name]
    ) AS [t1]
    WHERE [t1].[row] <= 1
) AS [t0] ON [t].[Rank] = [t0].[Rank]
ORDER BY [t].[Rank]
""");
    }

    public override async Task GroupBy_Property_Include_Select_Max(bool async)
    {
        await base.GroupBy_Property_Include_Select_Max(async);

        AssertSql(
"""
SELECT MAX(`g`.`SquadId`)
FROM `Gears` AS `g`
GROUP BY `g`.`Rank`
""");
    }

    public override async Task Include_with_group_by_and_FirstOrDefault_gets_properly_applied(bool async)
    {
        await base.Include_with_group_by_and_FirstOrDefault_gets_properly_applied(async);

        AssertSql(
"""
SELECT [t0].[Nickname], [t0].[SquadId], [t0].[AssignedCityName], [t0].[CityOfBirthName], [t0].[FullName], [t0].[HasSoulPatch], [t0].[LeaderNickname], [t0].[LeaderSquadId], [t0].[Rank], [t0].[Discriminator], [t0].[Name], [t0].[Location], [t0].[Nation]
FROM (
    SELECT [g].[Rank]
    FROM [Gears] AS [g]
    GROUP BY [g].[Rank]
) AS [t]
LEFT JOIN (
    SELECT [t1].[Nickname], [t1].[SquadId], [t1].[AssignedCityName], [t1].[CityOfBirthName], [t1].[FullName], [t1].[HasSoulPatch], [t1].[LeaderNickname], [t1].[LeaderSquadId], [t1].[Rank], [t1].[Discriminator], [t1].[Name], [t1].[Location], [t1].[Nation]
    FROM (
        SELECT [g0].[Nickname], [g0].[SquadId], [g0].[AssignedCityName], [g0].[CityOfBirthName], [g0].[FullName], [g0].[HasSoulPatch], [g0].[LeaderNickname], [g0].[LeaderSquadId], [g0].[Rank], CASE
            WHEN [o0].[Nickname] IS NOT NULL THEN N'Officer'
        END AS [Discriminator], [c].[Name], [c].[Location], [c].[Nation], ROW_NUMBER() OVER(PARTITION BY [g0].[Rank] ORDER BY [g0].[Nickname], [g0].[SquadId], [c].[Name]) AS [row]
        FROM [Gears] AS [g0]
        LEFT JOIN [Officers] AS [o0] ON [g0].[Nickname] = [o0].[Nickname] AND [g0].[SquadId] = [o0].[SquadId]
        INNER JOIN [Cities] AS [c] ON [g0].[CityOfBirthName] = [c].[Name]
        WHERE [g0].[HasSoulPatch] = CAST(1 AS bit)
    ) AS [t1]
    WHERE [t1].[row] <= 1
) AS [t0] ON [t].[Rank] = [t0].[Rank]
""");
    }

    public override async Task Include_collection_with_Cast_to_base(bool async)
    {
        await base.Include_collection_with_Cast_to_base(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM (`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN `Weapons` AS `w` ON `g`.`FullName` = `w`.`OwnerFullName`
WHERE `o`.`Nickname` IS NOT NULL
ORDER BY `g`.`Nickname`, `g`.`SquadId`
""");
    }

    public override async Task Include_with_client_method_and_member_access_still_applies_includes(bool async)
    {
        await base.Include_with_client_method_and_member_access_still_applies_includes(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`IssueDate`, `t`.`Note`
FROM (`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN `Tags` AS `t` ON `g`.`Nickname` = `t`.`GearNickName` AND `g`.`SquadId` = `t`.`GearSquadId`
""");
    }

    public override async Task Include_with_projection_of_unmapped_property_still_gets_applied(bool async)
    {
        await base.Include_with_projection_of_unmapped_property_still_gets_applied(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM (`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN `Weapons` AS `w` ON `g`.`FullName` = `w`.`OwnerFullName`
ORDER BY `g`.`Nickname`, `g`.`SquadId`
""");
    }

    public override async Task Multiple_includes_with_client_method_around_entity_and_also_projecting_included_collection()
    {
        await base.Multiple_includes_with_client_method_around_entity_and_also_projecting_included_collection();

        AssertSql(
    """
SELECT `s`.`Name`, `s`.`Id`, `s`.`Banner`, `s`.`Banner5`, `s`.`InternalNumber`, `s0`.`Nickname`, `s0`.`SquadId`, `s0`.`AssignedCityName`, `s0`.`CityOfBirthName`, `s0`.`FullName`, `s0`.`HasSoulPatch`, `s0`.`LeaderNickname`, `s0`.`LeaderSquadId`, `s0`.`Rank`, `s0`.`Discriminator`, `s0`.`Id`, `s0`.`AmmunitionType`, `s0`.`IsAutomatic`, `s0`.`Name`, `s0`.`OwnerFullName`, `s0`.`SynergyWithId`
FROM `Squads` AS `s`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
    FROM (`Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
    LEFT JOIN `Weapons` AS `w` ON `g`.`FullName` = `w`.`OwnerFullName`
) AS `s0` ON `s`.`Id` = `s0`.`SquadId`
WHERE `s`.`Name` = 'Delta'
ORDER BY `s`.`Id`, `s0`.`Nickname`, `s0`.`SquadId`
""");
    }

    public override async Task OrderBy_same_expression_containing_IsNull_correctly_deduplicates_the_ordering(bool async)
    {
        await base.OrderBy_same_expression_containing_IsNull_correctly_deduplicates_the_ordering(async);

        AssertSql(
            """
SELECT IIF(`g`.`LeaderNickname` IS NOT NULL, CBOOL(IIF(LEN(`g`.`Nickname`) IS NULL, NULL, CLNG(LEN(`g`.`Nickname`))) BXOR 5) BXOR TRUE, NULL)
FROM `Gears` AS `g`
ORDER BY NOT (IIF(IIF(`g`.`LeaderNickname` IS NOT NULL, CBOOL(IIF(LEN(`g`.`Nickname`) IS NULL, NULL, CLNG(LEN(`g`.`Nickname`))) BXOR 5) BXOR TRUE, NULL) IS NOT NULL, TRUE, FALSE))
""");
    }

    public override async Task GetValueOrDefault_in_projection(bool async)
    {
        await base.GetValueOrDefault_in_projection(async);

        AssertSql(
"""
SELECT IIF(`w`.`SynergyWithId` IS NULL, 0, `w`.`SynergyWithId`)
FROM `Weapons` AS `w`
""");
    }

    public override async Task GetValueOrDefault_in_filter(bool async)
    {
        await base.GetValueOrDefault_in_filter(async);

        AssertSql(
"""
SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE IIF(`w`.`SynergyWithId` IS NULL, 0, `w`.`SynergyWithId`) = 0
""");
    }

    public override async Task GetValueOrDefault_in_filter_non_nullable_column(bool async)
    {
        await base.GetValueOrDefault_in_filter_non_nullable_column(async);

        AssertSql(
            """
SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE `w`.`Id` = 0
""");
    }

    public override async Task GetValueOrDefault_in_order_by(bool async)
    {
        await base.GetValueOrDefault_in_order_by(async);

        AssertSql(
"""
SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
ORDER BY IIF(`w`.`SynergyWithId` IS NULL, 0, `w`.`SynergyWithId`), `w`.`Id`
""");
    }

    public override async Task GetValueOrDefault_with_argument(bool async)
    {
        await base.GetValueOrDefault_with_argument(async);

        AssertSql(
"""
SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE IIF(`w`.`SynergyWithId` IS NULL, `w`.`Id`, `w`.`SynergyWithId`) = 1
""");
    }

    public override async Task GetValueOrDefault_with_argument_complex(bool async)
    {
        await base.GetValueOrDefault_with_argument_complex(async);

        AssertSql(
            """
SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE IIF(`w`.`SynergyWithId` IS NULL, IIF(LEN(`w`.`Name`) IS NULL, NULL, CLNG(LEN(`w`.`Name`))) + 42, `w`.`SynergyWithId`) > 10
""");
    }

    public override async Task Filter_with_complex_predicate_containing_subquery(bool async)
    {
        await base.Filter_with_complex_predicate_containing_subquery(async);

        AssertSql(
            """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE `g`.`FullName` <> 'Dom' AND EXISTS (
    SELECT 1
    FROM `Weapons` AS `w`
    WHERE `g`.`FullName` = `w`.`OwnerFullName` AND `w`.`IsAutomatic` = TRUE)
""");
    }

    public override async Task Query_with_complex_let_containing_ordering_and_filter_projecting_firstOrDefault_element_of_let(
        bool async)
    {
        await base.Query_with_complex_let_containing_ordering_and_filter_projecting_firstOrDefault_element_of_let(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, (
    SELECT TOP 1 `w`.`Name`
    FROM `Weapons` AS `w`
    WHERE `g`.`FullName` = `w`.`OwnerFullName` AND `w`.`IsAutomatic` = TRUE
    ORDER BY `w`.`AmmunitionType` DESC) AS `WeaponName`
FROM `Gears` AS `g`
WHERE `g`.`Nickname` <> 'Dom'
""");
    }

    public override async Task
        Null_semantics_is_correctly_applied_for_function_comparisons_that_take_arguments_from_optional_navigation(bool async)
    {
        await base.Null_semantics_is_correctly_applied_for_function_comparisons_that_take_arguments_from_optional_navigation(async);

        AssertSql(
            """
SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`IssueDate`, `t`.`Note`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
WHERE IIF(`s`.`SquadId` IS NULL, NULL, MID(`t`.`Note`, 0 + 1, `s`.`SquadId`)) = `t`.`GearNickName` OR ((`t`.`Note` IS NULL OR `s`.`SquadId` IS NULL) AND `t`.`GearNickName` IS NULL)
""");
    }

    public override async Task
        Null_semantics_is_correctly_applied_for_function_comparisons_that_take_arguments_from_optional_navigation_complex(bool async)
    {
        await base.Null_semantics_is_correctly_applied_for_function_comparisons_that_take_arguments_from_optional_navigation_complex(
            async);

        AssertSql(
            """
SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`IssueDate`, `t`.`Note`
FROM (`Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`)
LEFT JOIN `Squads` AS `s0` ON `s`.`SquadId` = `s0`.`Id`
WHERE IIF(LEN(`s0`.`Name`) IS NULL, NULL, MID(`t`.`Note`, 0 + 1, IIF(LEN(`s0`.`Name`) IS NULL, NULL, CLNG(LEN(`s0`.`Name`))))) = `t`.`GearNickName` OR ((`t`.`Note` IS NULL OR `s0`.`Name` IS NULL) AND `t`.`GearNickName` IS NULL)
""");
    }

    public override async Task Filter_with_new_Guid(bool async)
    {
        await base.Filter_with_new_Guid(async);

        AssertSql(
            """
SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`IssueDate`, `t`.`Note`
FROM `Tags` AS `t`
WHERE `t`.`Id` = '{df36f493-463f-4123-83f9-6b135deeb7ba}'
""");
    }

    public override async Task Filter_with_new_Guid_closure(bool async)
    {
        await base.Filter_with_new_Guid_closure(async);

        AssertSql(
"""
@__p_0='df36f493-463f-4123-83f9-6b135deeb7bd'

SELECT [t].[Id], [t].[GearNickName], [t].[GearSquadId], [t].[Note]
FROM [Tags] AS [t]
WHERE [t].[Id] = @__p_0
""",
//
"""
@__p_0='b39a6fba-9026-4d69-828e-fd7068673e57'

SELECT [t].[Id], [t].[GearNickName], [t].[GearSquadId], [t].[Note]
FROM [Tags] AS [t]
WHERE [t].[Id] = @__p_0
""");
    }

    public override async Task OfTypeNav1(bool async)
    {
        await base.OfTypeNav1(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM ((`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN `Tags` AS `t` ON `g`.`Nickname` = `t`.`GearNickName` AND `g`.`SquadId` = `t`.`GearSquadId`)
LEFT JOIN `Tags` AS `t0` ON `g`.`Nickname` = `t0`.`GearNickName` AND `g`.`SquadId` = `t0`.`GearSquadId`
WHERE (`t`.`Note` <> 'Foo' OR `t`.`Note` IS NULL) AND `o`.`Nickname` IS NOT NULL AND (`t0`.`Note` <> 'Bar' OR `t0`.`Note` IS NULL)
""");
    }

    public override async Task OfTypeNav2(bool async)
    {
        await base.OfTypeNav2(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM ((`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN `Tags` AS `t` ON `g`.`Nickname` = `t`.`GearNickName` AND `g`.`SquadId` = `t`.`GearSquadId`)
LEFT JOIN `Cities` AS `c` ON `g`.`AssignedCityName` = `c`.`Name`
WHERE (`t`.`Note` <> 'Foo' OR `t`.`Note` IS NULL) AND `o`.`Nickname` IS NOT NULL AND (`c`.`Location` <> 'Bar' OR `c`.`Location` IS NULL)
""");
    }

    public override async Task OfTypeNav3(bool async)
    {
        await base.OfTypeNav3(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM (((`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN `Tags` AS `t` ON `g`.`Nickname` = `t`.`GearNickName` AND `g`.`SquadId` = `t`.`GearSquadId`)
INNER JOIN `Weapons` AS `w` ON `g`.`FullName` = `w`.`OwnerFullName`)
LEFT JOIN `Tags` AS `t0` ON `g`.`Nickname` = `t0`.`GearNickName` AND `g`.`SquadId` = `t0`.`GearSquadId`
WHERE (`t`.`Note` <> 'Foo' OR `t`.`Note` IS NULL) AND `o`.`Nickname` IS NOT NULL AND (`t0`.`Note` <> 'Bar' OR `t0`.`Note` IS NULL)
""");
    }

    public override async Task Nav_rewrite_Distinct_with_convert()
    {
        await base.Nav_rewrite_Distinct_with_convert();

        AssertSql();
    }

    public override async Task Nav_rewrite_Distinct_with_convert_anonymous()
    {
        await base.Nav_rewrite_Distinct_with_convert_anonymous();

        AssertSql();
    }

    public override async Task Nav_rewrite_with_convert1(bool async)
    {
        await base.Nav_rewrite_with_convert1(async);

        AssertSql(
            """
SELECT `s`.`Name`, `s`.`LocustHordeId`, `s`.`ThreatLevel`, `s`.`ThreatLevelByte`, `s`.`ThreatLevelNullableByte`, `s`.`DefeatedByNickname`, `s`.`DefeatedBySquadId`, `s`.`HighCommandId`
FROM ((`Factions` AS `f`
LEFT JOIN `LocustHordes` AS `l` ON `f`.`Id` = `l`.`Id`)
LEFT JOIN `Cities` AS `c` ON `f`.`CapitalName` = `c`.`Name`)
LEFT JOIN (
    SELECT `l0`.`Name`, `l0`.`LocustHordeId`, `l0`.`ThreatLevel`, `l0`.`ThreatLevelByte`, `l0`.`ThreatLevelNullableByte`, `l1`.`DefeatedByNickname`, `l1`.`DefeatedBySquadId`, `l1`.`HighCommandId`
    FROM `LocustLeaders` AS `l0`
    INNER JOIN `LocustCommanders` AS `l1` ON `l0`.`Name` = `l1`.`Name`
) AS `s` ON `l`.`CommanderName` = `s`.`Name`
WHERE `c`.`Name` <> 'Foo' OR `c`.`Name` IS NULL
""");
    }

    public override async Task Nav_rewrite_with_convert2(bool async)
    {
        await base.Nav_rewrite_with_convert2(async);

        AssertSql(
            """
SELECT `f`.`Id`, `f`.`CapitalName`, `f`.`Name`, `f`.`ServerAddress`, `l`.`CommanderName`, `l`.`Eradicated`, IIF(`l`.`Id` IS NOT NULL, 'LocustHorde', NULL) AS `Discriminator`
FROM ((`Factions` AS `f`
LEFT JOIN `LocustHordes` AS `l` ON `f`.`Id` = `l`.`Id`)
LEFT JOIN `Cities` AS `c` ON `f`.`CapitalName` = `c`.`Name`)
LEFT JOIN (
    SELECT `l0`.`Name`
    FROM `LocustLeaders` AS `l0`
    INNER JOIN `LocustCommanders` AS `l1` ON `l0`.`Name` = `l1`.`Name`
) AS `s` ON `l`.`CommanderName` = `s`.`Name`
WHERE (`c`.`Name` <> 'Foo' OR `c`.`Name` IS NULL) AND (`s`.`Name` <> 'Bar' OR `s`.`Name` IS NULL)
""");
    }

    public override async Task Nav_rewrite_with_convert3(bool async)
    {
        await base.Nav_rewrite_with_convert3(async);

        AssertSql(
            """
SELECT `f`.`Id`, `f`.`CapitalName`, `f`.`Name`, `f`.`ServerAddress`, `l`.`CommanderName`, `l`.`Eradicated`, IIF(`l`.`Id` IS NOT NULL, 'LocustHorde', NULL) AS `Discriminator`
FROM ((`Factions` AS `f`
LEFT JOIN `LocustHordes` AS `l` ON `f`.`Id` = `l`.`Id`)
LEFT JOIN `Cities` AS `c` ON `f`.`CapitalName` = `c`.`Name`)
LEFT JOIN (
    SELECT `l0`.`Name`
    FROM `LocustLeaders` AS `l0`
    INNER JOIN `LocustCommanders` AS `l1` ON `l0`.`Name` = `l1`.`Name`
) AS `s` ON `l`.`CommanderName` = `s`.`Name`
WHERE (`c`.`Name` <> 'Foo' OR `c`.`Name` IS NULL) AND (`s`.`Name` <> 'Bar' OR `s`.`Name` IS NULL)
""");
    }

    public override async Task Where_contains_on_navigation_with_composite_keys(bool async)
    {
        await base.Where_contains_on_navigation_with_composite_keys(async);

        AssertSql(
            """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE EXISTS (
    SELECT 1
    FROM `Cities` AS `c`
    WHERE EXISTS (
        SELECT 1
        FROM `Gears` AS `g0`
        WHERE `c`.`Name` = `g0`.`CityOfBirthName` AND `g0`.`Nickname` = `g`.`Nickname` AND `g0`.`SquadId` = `g`.`SquadId`))
""");
    }

    public override async Task Include_with_complex_order_by(bool async)
    {
        await base.Include_with_complex_order_by(async);

        AssertSql(
            """
SELECT `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`, `s`.`Id`, `s`.`AmmunitionType`, `s`.`IsAutomatic`, `s`.`Name`, `s`.`OwnerFullName`, `s`.`SynergyWithId`, `s`.`c`
FROM (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`, (
        SELECT TOP 1 `w`.`Name`
        FROM `Weapons` AS `w`
        WHERE `g`.`FullName` = `w`.`OwnerFullName` AND (`w`.`Name` LIKE '%Gnasher%')) AS `c`
    FROM (`Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
    LEFT JOIN `Weapons` AS `w0` ON `g`.`FullName` = `w0`.`OwnerFullName`
) AS `s`
ORDER BY `s`.`c`, `s`.`Nickname`, `s`.`SquadId`
""");
    }

    public override async Task Anonymous_projection_take_followed_by_projecting_single_element_from_collection_navigation(bool async)
    {
        await base.Anonymous_projection_take_followed_by_projecting_single_element_from_collection_navigation(async);

        AssertSql(
"""
@__p_0='25'

SELECT [t0].[Id], [t0].[AmmunitionType], [t0].[IsAutomatic], [t0].[Name], [t0].[OwnerFullName], [t0].[SynergyWithId]
FROM (
    SELECT TOP(@__p_0) [g].[FullName]
    FROM [Gears] AS [g]
) AS [t]
LEFT JOIN (
    SELECT [t1].[Id], [t1].[AmmunitionType], [t1].[IsAutomatic], [t1].[Name], [t1].[OwnerFullName], [t1].[SynergyWithId]
    FROM (
        SELECT [w].[Id], [w].[AmmunitionType], [w].[IsAutomatic], [w].[Name], [w].[OwnerFullName], [w].[SynergyWithId], ROW_NUMBER() OVER(PARTITION BY [w].[OwnerFullName] ORDER BY [w].[Id]) AS [row]
        FROM [Weapons] AS [w]
    ) AS [t1]
    WHERE [t1].[row] <= 1
) AS [t0] ON [t].[FullName] = [t0].[OwnerFullName]
""");
    }

    public override async Task Bool_projection_from_subquery_treated_appropriately_in_where(bool async)
    {
        await base.Bool_projection_from_subquery_treated_appropriately_in_where(async);

        AssertSql(
            """
SELECT `c`.`Name`, `c`.`Location`, `c`.`Nation`
FROM `Cities` AS `c`
WHERE (
    SELECT TOP 1 `g`.`HasSoulPatch`
    FROM `Gears` AS `g`
    ORDER BY `g`.`Nickname`, `g`.`SquadId`) = TRUE
""");
    }

    public override async Task DateTimeOffset_Contains_Less_than_Greater_than(bool async)
    {
        var dto = JetTestHelpers.GetExpectedValue(new DateTimeOffset(599898024001234567, new TimeSpan(1, 30, 0)));
        var start = dto.AddDays(-1);
        var end = dto.AddDays(1);
        var dates = new[] { dto };

        await AssertQuery(
            async,
            ss => ss.Set<Mission>().Where(
                m => start <= m.Timeline.Date && m.Timeline < end && dates.Contains(m.Timeline)));

        AssertSql(
    """
@__start_0='1902-01-01T08:30:00.0000000Z' (DbType = DateTime)
@__end_1='1902-01-03T08:30:00.0000000Z' (DbType = DateTime)

SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Date`, `m`.`Difficulty`, `m`.`Duration`, `m`.`Rating`, `m`.`Time`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE @__start_0 <= DATEVALUE(`m`.`Timeline`) AND `m`.`Timeline` < @__end_1 AND `m`.`Timeline` = CDATE('1902-01-02 08:30:00')
""");
    }

    public override Task DateTimeOffsetNow_minus_timespan(bool async)
        => AssertTranslationFailed(() => base.DateTimeOffsetNow_minus_timespan(async));

    public override async Task Navigation_inside_interpolated_string_expanded(bool async)
    {
        await base.Navigation_inside_interpolated_string_expanded(async);

        AssertSql(
"""
SELECT IIF(`w`.`SynergyWithId` IS NOT NULL, TRUE, FALSE), `w0`.`OwnerFullName`
FROM `Weapons` AS `w`
LEFT JOIN `Weapons` AS `w0` ON `w`.`SynergyWithId` = `w0`.`Id`
""");
    }

    public override async Task Left_join_projection_using_coalesce_tracking(bool async)
    {
        await base.Left_join_projection_using_coalesce_tracking(async);

        AssertSql(
    """
SELECT `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`, `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM (`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g0`
    LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`
) AS `s` ON `g`.`LeaderNickname` = `s`.`Nickname`
""");
    }

    public override async Task Left_join_projection_using_conditional_tracking(bool async)
    {
        await base.Left_join_projection_using_conditional_tracking(async);

        AssertSql(
    """
SELECT IIF(`s`.`Nickname` IS NULL OR `s`.`SquadId` IS NULL, TRUE, FALSE), `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`
FROM (`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g0`
    LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`
) AS `s` ON `g`.`LeaderNickname` = `s`.`Nickname`
""");
    }

    public override async Task Project_collection_navigation_nested_with_take_composite_key(bool async)
    {
        await base.Project_collection_navigation_nested_with_take_composite_key(async);

        AssertSql(
"""
SELECT [t].[Id], [t0].[Nickname], [t0].[SquadId], [t1].[Nickname], [t1].[SquadId], [t1].[AssignedCityName], [t1].[CityOfBirthName], [t1].[FullName], [t1].[HasSoulPatch], [t1].[LeaderNickname], [t1].[LeaderSquadId], [t1].[Rank], [t1].[Discriminator]
FROM [Tags] AS [t]
LEFT JOIN (
    SELECT [g].[Nickname], [g].[SquadId], CASE
        WHEN [o].[Nickname] IS NOT NULL THEN N'Officer'
    END AS [Discriminator]
    FROM [Gears] AS [g]
    LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
) AS [t0] ON [t].[GearNickName] = [t0].[Nickname] AND [t].[GearSquadId] = [t0].[SquadId]
LEFT JOIN (
    SELECT [t2].[Nickname], [t2].[SquadId], [t2].[AssignedCityName], [t2].[CityOfBirthName], [t2].[FullName], [t2].[HasSoulPatch], [t2].[LeaderNickname], [t2].[LeaderSquadId], [t2].[Rank], [t2].[Discriminator]
    FROM (
        SELECT [g0].[Nickname], [g0].[SquadId], [g0].[AssignedCityName], [g0].[CityOfBirthName], [g0].[FullName], [g0].[HasSoulPatch], [g0].[LeaderNickname], [g0].[LeaderSquadId], [g0].[Rank], CASE
            WHEN [o0].[Nickname] IS NOT NULL THEN N'Officer'
        END AS [Discriminator], ROW_NUMBER() OVER(PARTITION BY [g0].[LeaderNickname], [g0].[LeaderSquadId] ORDER BY [g0].[Nickname], [g0].[SquadId]) AS [row]
        FROM [Gears] AS [g0]
        LEFT JOIN [Officers] AS [o0] ON [g0].[Nickname] = [o0].[Nickname] AND [g0].[SquadId] = [o0].[SquadId]
    ) AS [t2]
    WHERE [t2].[row] <= 50
) AS [t1] ON ([t0].[Nickname] = [t1].[LeaderNickname] OR (([t0].[Nickname] IS NULL) AND ([t1].[LeaderNickname] IS NULL))) AND [t0].[SquadId] = [t1].[LeaderSquadId]
WHERE [t0].[Discriminator] = N'Officer'
ORDER BY [t].[Id], [t0].[Nickname], [t0].[SquadId], [t1].[Nickname]
""");
    }

    public override async Task Project_collection_navigation_nested_composite_key(bool async)
    {
        await base.Project_collection_navigation_nested_composite_key(async);

        AssertSql(
            """
SELECT `t`.`Id`, `s`.`Nickname`, `s`.`SquadId`, `s0`.`Nickname`, `s0`.`SquadId`, `s0`.`AssignedCityName`, `s0`.`CityOfBirthName`, `s0`.`FullName`, `s0`.`HasSoulPatch`, `s0`.`LeaderNickname`, `s0`.`LeaderSquadId`, `s0`.`Rank`, `s0`.`Discriminator`
FROM (`Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g0`
    LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`
) AS `s0` ON (`s`.`Nickname` = `s0`.`LeaderNickname` OR (`s`.`Nickname` IS NULL AND `s0`.`LeaderNickname` IS NULL)) AND `s`.`SquadId` = `s0`.`LeaderSquadId`
WHERE `s`.`Discriminator` = 'Officer'
ORDER BY `t`.`Id`, `s`.`Nickname`, `s`.`SquadId`, `s0`.`Nickname`
""");
    }

    public override async Task Null_checks_in_correlated_predicate_are_correctly_translated(bool async)
    {
        await base.Null_checks_in_correlated_predicate_are_correctly_translated(async);

        AssertSql(
"""
SELECT [t].[Id], [t0].[Nickname], [t0].[SquadId], [t0].[AssignedCityName], [t0].[CityOfBirthName], [t0].[FullName], [t0].[HasSoulPatch], [t0].[LeaderNickname], [t0].[LeaderSquadId], [t0].[Rank], [t0].[Discriminator]
FROM [Tags] AS [t]
LEFT JOIN (
    SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOfBirthName], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], CASE
        WHEN [o].[Nickname] IS NOT NULL THEN N'Officer'
    END AS [Discriminator]
    FROM [Gears] AS [g]
    LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
) AS [t0] ON [t].[GearNickName] = [t0].[Nickname] AND [t].[GearSquadId] = [t0].[SquadId] AND ([t].[Note] IS NOT NULL)
ORDER BY [t].[Id], [t0].[Nickname]
""");
    }

    public override async Task SelectMany_Where_DefaultIfEmpty_with_navigation_in_the_collection_selector(bool async)
    {
        await base.SelectMany_Where_DefaultIfEmpty_with_navigation_in_the_collection_selector(async);

        AssertSql(
            """
@__isAutomatic_0='True'

SELECT `g`.`Nickname`, `g`.`FullName`, IIF(`w0`.`Id` IS NOT NULL, TRUE, FALSE) AS `Collection`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `w`.`Id`, `w`.`OwnerFullName`
    FROM `Weapons` AS `w`
    WHERE `w`.`IsAutomatic` = @__isAutomatic_0
) AS `w0` ON `g`.`FullName` = `w0`.`OwnerFullName`
""");
    }

    public override async Task Join_with_inner_being_a_subquery_projecting_single_property(bool async)
    {
        await base.Join_with_inner_being_a_subquery_projecting_single_property(async);

        AssertSql(
    """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM (`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
INNER JOIN (
    SELECT `g0`.`Nickname`
    FROM `Gears` AS `g0`
) AS `s` ON `g`.`Nickname` = `s`.`Nickname`
""");
    }

    public override async Task Join_with_inner_being_a_subquery_projecting_anonymous_type_with_single_property(bool async)
    {
        await base.Join_with_inner_being_a_subquery_projecting_anonymous_type_with_single_property(async);

        AssertSql(
    """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM (`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
INNER JOIN (
    SELECT `g0`.`Nickname`
    FROM `Gears` AS `g0`
) AS `s` ON `g`.`Nickname` = `s`.`Nickname`
""");
    }

    public override async Task Navigation_based_on_complex_expression1(bool async)
    {
        await base.Navigation_based_on_complex_expression1(async);

        AssertSql(
            """
SELECT `f`.`Id`, `f`.`CapitalName`, `f`.`Name`, `f`.`ServerAddress`, `l`.`CommanderName`, `l`.`Eradicated`, IIF(`l`.`Id` IS NOT NULL, 'LocustHorde', NULL) AS `Discriminator`
FROM (`Factions` AS `f`
LEFT JOIN `LocustHordes` AS `l` ON `f`.`Id` = `l`.`Id`)
LEFT JOIN (
    SELECT `l0`.`Name`
    FROM `LocustLeaders` AS `l0`
    INNER JOIN `LocustCommanders` AS `l1` ON `l0`.`Name` = `l1`.`Name`
) AS `s` ON `l`.`CommanderName` = `s`.`Name`
WHERE IIF(`l`.`Id` IS NOT NULL AND `s`.`Name` IS NOT NULL, TRUE, FALSE) = TRUE
""");
    }

    public override async Task Navigation_based_on_complex_expression2(bool async)
    {
        await base.Navigation_based_on_complex_expression2(async);

        AssertSql(
            """
SELECT `f`.`Id`, `f`.`CapitalName`, `f`.`Name`, `f`.`ServerAddress`, `l`.`CommanderName`, `l`.`Eradicated`, IIF(`l`.`Id` IS NOT NULL, 'LocustHorde', NULL) AS `Discriminator`
FROM (`Factions` AS `f`
LEFT JOIN `LocustHordes` AS `l` ON `f`.`Id` = `l`.`Id`)
LEFT JOIN (
    SELECT `l0`.`Name`
    FROM `LocustLeaders` AS `l0`
    INNER JOIN `LocustCommanders` AS `l1` ON `l0`.`Name` = `l1`.`Name`
) AS `s` ON `l`.`CommanderName` = `s`.`Name`
WHERE `l`.`Id` IS NOT NULL AND `s`.`Name` IS NOT NULL
""");
    }

    public override async Task Navigation_based_on_complex_expression3(bool async)
    {
        await base.Navigation_based_on_complex_expression3(async);

        AssertSql(
            """
SELECT `s`.`Name`, `s`.`LocustHordeId`, `s`.`ThreatLevel`, `s`.`ThreatLevelByte`, `s`.`ThreatLevelNullableByte`, `s`.`DefeatedByNickname`, `s`.`DefeatedBySquadId`, `s`.`HighCommandId`
FROM (`Factions` AS `f`
LEFT JOIN `LocustHordes` AS `l` ON `f`.`Id` = `l`.`Id`)
LEFT JOIN (
    SELECT `l0`.`Name`, `l0`.`LocustHordeId`, `l0`.`ThreatLevel`, `l0`.`ThreatLevelByte`, `l0`.`ThreatLevelNullableByte`, `l1`.`DefeatedByNickname`, `l1`.`DefeatedBySquadId`, `l1`.`HighCommandId`
    FROM `LocustLeaders` AS `l0`
    INNER JOIN `LocustCommanders` AS `l1` ON `l0`.`Name` = `l1`.`Name`
) AS `s` ON `l`.`CommanderName` = `s`.`Name`
WHERE `l`.`Id` IS NOT NULL
""");
    }

    public override async Task Navigation_based_on_complex_expression4(bool async)
    {
        await base.Navigation_based_on_complex_expression4(async);

        AssertSql(
"""
SELECT CASE
    WHEN [l].[Id] IS NOT NULL THEN CAST(1 AS bit)
    ELSE CAST(0 AS bit)
END, [t0].[Name], [t0].[LocustHordeId], [t0].[ThreatLevel], [t0].[ThreatLevelByte], [t0].[ThreatLevelNullableByte], [t0].[DefeatedByNickname], [t0].[DefeatedBySquadId], [t0].[HighCommandId], [t].[Name], [t].[LocustHordeId], [t].[ThreatLevel], [t].[ThreatLevelByte], [t].[ThreatLevelNullableByte], [t].[DefeatedByNickname], [t].[DefeatedBySquadId], [t].[HighCommandId], [t].[Discriminator]
FROM [Factions] AS [f]
LEFT JOIN [LocustHordes] AS [l] ON [f].[Id] = [l].[Id]
CROSS JOIN (
    SELECT [l0].[Name], [l0].[LocustHordeId], [l0].[ThreatLevel], [l0].[ThreatLevelByte], [l0].[ThreatLevelNullableByte], [l1].[DefeatedByNickname], [l1].[DefeatedBySquadId], [l1].[HighCommandId], CASE
        WHEN [l1].[Name] IS NOT NULL THEN N'LocustCommander'
    END AS [Discriminator]
    FROM [LocustLeaders] AS [l0]
    LEFT JOIN [LocustCommanders] AS [l1] ON [l0].[Name] = [l1].[Name]
    WHERE [l1].[Name] IS NOT NULL
) AS [t]
LEFT JOIN (
    SELECT [l2].[Name], [l2].[LocustHordeId], [l2].[ThreatLevel], [l2].[ThreatLevelByte], [l2].[ThreatLevelNullableByte], [l3].[DefeatedByNickname], [l3].[DefeatedBySquadId], [l3].[HighCommandId]
    FROM [LocustLeaders] AS [l2]
    INNER JOIN [LocustCommanders] AS [l3] ON [l2].[Name] = [l3].[Name]
) AS [t0] ON [l].[CommanderName] = [t0].[Name]
""");
    }

    public override async Task Navigation_based_on_complex_expression5(bool async)
    {
        await base.Navigation_based_on_complex_expression5(async);

        AssertSql(
"""
SELECT [t0].[Name], [t0].[LocustHordeId], [t0].[ThreatLevel], [t0].[ThreatLevelByte], [t0].[ThreatLevelNullableByte], [t0].[DefeatedByNickname], [t0].[DefeatedBySquadId], [t0].[HighCommandId], [t].[Name], [t].[LocustHordeId], [t].[ThreatLevel], [t].[ThreatLevelByte], [t].[ThreatLevelNullableByte], [t].[DefeatedByNickname], [t].[DefeatedBySquadId], [t].[HighCommandId], [t].[Discriminator]
FROM [Factions] AS [f]
LEFT JOIN [LocustHordes] AS [l] ON [f].[Id] = [l].[Id]
CROSS JOIN (
    SELECT [l0].[Name], [l0].[LocustHordeId], [l0].[ThreatLevel], [l0].[ThreatLevelByte], [l0].[ThreatLevelNullableByte], [l1].[DefeatedByNickname], [l1].[DefeatedBySquadId], [l1].[HighCommandId], CASE
        WHEN [l1].[Name] IS NOT NULL THEN N'LocustCommander'
    END AS [Discriminator]
    FROM [LocustLeaders] AS [l0]
    LEFT JOIN [LocustCommanders] AS [l1] ON [l0].[Name] = [l1].[Name]
    WHERE [l1].[Name] IS NOT NULL
) AS [t]
LEFT JOIN (
    SELECT [l2].[Name], [l2].[LocustHordeId], [l2].[ThreatLevel], [l2].[ThreatLevelByte], [l2].[ThreatLevelNullableByte], [l3].[DefeatedByNickname], [l3].[DefeatedBySquadId], [l3].[HighCommandId]
    FROM [LocustLeaders] AS [l2]
    INNER JOIN [LocustCommanders] AS [l3] ON [l2].[Name] = [l3].[Name]
) AS [t0] ON [l].[CommanderName] = [t0].[Name]
WHERE [l].[Id] IS NOT NULL
""");
    }

    public override async Task Navigation_based_on_complex_expression6(bool async)
    {
        await base.Navigation_based_on_complex_expression6(async);

        AssertSql(
        """
SELECT IIF(`m`.`Timeline` > NOW(), TRUE, FALSE)
FROM `Missions` AS `m`
""");
    }

    public override async Task Select_as_operator(bool async)
    {
        await base.Select_as_operator(async);

        AssertSql(
"""
SELECT `l`.`Name`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`ThreatLevelByte`, `l`.`ThreatLevelNullableByte`, `l0`.`DefeatedByNickname`, `l0`.`DefeatedBySquadId`, `l0`.`HighCommandId`, IIF(`l0`.`Name` IS NOT NULL, 'LocustCommander', NULL) AS `Discriminator`
FROM `LocustLeaders` AS `l`
LEFT JOIN `LocustCommanders` AS `l0` ON `l`.`Name` = `l0`.`Name`
""");
    }

    public override async Task Select_datetimeoffset_comparison_in_projection(bool async)
    {
        await base.Select_datetimeoffset_comparison_in_projection(async);

        AssertSql(
"""
SELECT IIF(`m`.`Timeline` > NOW(), TRUE, FALSE)
FROM `Missions` AS `m`
""");
    }

    public override async Task OfType_in_subquery_works(bool async)
    {
        await base.OfType_in_subquery_works(async);

        AssertSql(
            """
SELECT `s`.`Name`, `s`.`Location`, `s`.`Nation`
FROM (`Gears` AS `g`
INNER JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
INNER JOIN (
    SELECT `c`.`Name`, `c`.`Location`, `c`.`Nation`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`
    FROM (`Gears` AS `g0`
    LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`)
    LEFT JOIN `Cities` AS `c` ON `g0`.`AssignedCityName` = `c`.`Name`
    WHERE `o0`.`Nickname` IS NOT NULL
) AS `s` ON `g`.`Nickname` = `s`.`LeaderNickname` AND `g`.`SquadId` = `s`.`LeaderSquadId`
""");
    }

    public override async Task Nullable_bool_comparison_is_translated_to_server(bool async)
    {
        await base.Nullable_bool_comparison_is_translated_to_server(async);

        AssertSql(
"""
SELECT IIF(`l`.`Eradicated` = TRUE AND `l`.`Eradicated` IS NOT NULL, TRUE, FALSE) AS `IsEradicated`
FROM `Factions` AS `f`
INNER JOIN `LocustHordes` AS `l` ON `f`.`Id` = `l`.`Id`
""");
    }

    public override async Task Accessing_reference_navigation_collection_composition_generates_single_query(bool async)
    {
        await base.Accessing_reference_navigation_collection_composition_generates_single_query(async);

        AssertSql(
            """
SELECT `g`.`Nickname`, `g`.`SquadId`, `s`.`Id`, `s`.`IsAutomatic`, `s`.`Name`, `s`.`Id0`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `w`.`Id`, `w`.`IsAutomatic`, `w0`.`Name`, `w0`.`Id` AS `Id0`, `w`.`OwnerFullName`
    FROM `Weapons` AS `w`
    LEFT JOIN `Weapons` AS `w0` ON `w`.`SynergyWithId` = `w0`.`Id`
) AS `s` ON `g`.`FullName` = `s`.`OwnerFullName`
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `s`.`Id`
""");
    }

    public override async Task Reference_include_chain_loads_correctly_when_middle_is_null(bool async)
    {
        await base.Reference_include_chain_loads_correctly_when_middle_is_null(async);

        AssertSql(
            """
SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`IssueDate`, `t`.`Note`, `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`, `s0`.`Id`, `s0`.`Banner`, `s0`.`Banner5`, `s0`.`InternalNumber`, `s0`.`Name`
FROM (`Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`)
LEFT JOIN `Squads` AS `s0` ON `s`.`SquadId` = `s0`.`Id`
ORDER BY `t`.`Note`
""");
    }

    public override async Task Accessing_property_of_optional_navigation_in_child_projection_works(bool async)
    {
        await base.Accessing_property_of_optional_navigation_in_child_projection_works(async);

        AssertSql(
            """
SELECT IIF(`s`.`Nickname` IS NOT NULL AND `s`.`SquadId` IS NOT NULL, TRUE, FALSE), `t`.`Id`, `s`.`Nickname`, `s`.`SquadId`, `s1`.`Nickname`, `s1`.`Id`, `s1`.`SquadId`
FROM (`Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`FullName`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`)
LEFT JOIN (
    SELECT `s0`.`Nickname`, `w`.`Id`, `s0`.`SquadId`, `w`.`OwnerFullName`
    FROM `Weapons` AS `w`
    LEFT JOIN (
        SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`FullName`
        FROM `Gears` AS `g0`
    ) AS `s0` ON `w`.`OwnerFullName` = `s0`.`FullName`
) AS `s1` ON `s`.`FullName` = `s1`.`OwnerFullName`
ORDER BY `t`.`Note`, `t`.`Id`, `s`.`Nickname`, `s`.`SquadId`, `s1`.`Id`, `s1`.`Nickname`
""");
    }

    public override async Task Collection_navigation_ofType_filter_works(bool async)
    {
        await base.Collection_navigation_ofType_filter_works(async);

        AssertSql(
"""
SELECT `c`.`Name`, `c`.`Location`, `c`.`Nation`
FROM `Cities` AS `c`
WHERE EXISTS (
    SELECT 1
    FROM `Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
    WHERE `c`.`Name` = `g`.`CityOfBirthName` AND `o`.`Nickname` IS NOT NULL AND `g`.`Nickname` = 'Marcus')
""");
    }

    public override async Task Query_reusing_parameter_doesnt_declare_duplicate_parameter(bool async)
    {
        await base.Query_reusing_parameter_doesnt_declare_duplicate_parameter(async);

        AssertSql(
            """
@__prm_Inner_Nickname_0='Marcus' (Size = 255)

SELECT `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`
FROM (
    SELECT DISTINCT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
    WHERE `g`.`Nickname` <> @__prm_Inner_Nickname_0
) AS `s`
ORDER BY `s`.`FullName`
""");
    }

    public override async Task Query_reusing_parameter_with_inner_query_doesnt_declare_duplicate_parameter(bool async)
    {
        await base.Query_reusing_parameter_with_inner_query_doesnt_declare_duplicate_parameter(async);

        AssertSql(
            """
@__squadId_0='1'
@__squadId_0='1'

SELECT `u`.`Nickname`, `u`.`SquadId`, `u`.`AssignedCityName`, `u`.`CityOfBirthName`, `u`.`FullName`, `u`.`HasSoulPatch`, `u`.`LeaderNickname`, `u`.`LeaderSquadId`, `u`.`Rank`, `u`.`Discriminator`
FROM (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM (`Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
    INNER JOIN `Squads` AS `s` ON `g`.`SquadId` = `s`.`Id`
    WHERE `s`.`Id` IN (
        SELECT `s0`.`Id`
        FROM `Squads` AS `s0`
        WHERE `s0`.`Id` = @__squadId_0
    )
    UNION ALL
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM (`Gears` AS `g0`
    LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`)
    INNER JOIN `Squads` AS `s1` ON `g0`.`SquadId` = `s1`.`Id`
    WHERE `s1`.`Id` IN (
        SELECT `s2`.`Id`
        FROM `Squads` AS `s2`
        WHERE `s2`.`Id` = @__squadId_0
    )
) AS `u`
ORDER BY `u`.`FullName`
""");
    }

    public override async Task Query_reusing_parameter_with_inner_query_expression_doesnt_declare_duplicate_parameter(bool async)
    {
        await base.Query_reusing_parameter_with_inner_query_expression_doesnt_declare_duplicate_parameter(async);

        AssertSql(
            """
@__gearId_0='1'
@__gearId_0='1'

SELECT `s`.`Id`, `s`.`Banner`, `s`.`Banner5`, `s`.`InternalNumber`, `s`.`Name`
FROM `Squads` AS `s`
WHERE EXISTS (
    SELECT 1
    FROM `Gears` AS `g`
    WHERE `s`.`Id` = `g`.`SquadId` AND `g`.`SquadId` = @__gearId_0 AND `g`.`SquadId` = @__gearId_0)
""");
    }

    public override async Task Query_reusing_parameter_doesnt_declare_duplicate_parameter_complex(bool async)
    {
        await base.Query_reusing_parameter_doesnt_declare_duplicate_parameter_complex(async);

        AssertSql(
            """
@__entity_equality_prm_Inner_Squad_0_Id='1' (Nullable = true)
@__entity_equality_prm_Inner_Squad_0_Id='1' (Nullable = true)

SELECT `s1`.`Nickname`, `s1`.`SquadId`, `s1`.`AssignedCityName`, `s1`.`CityOfBirthName`, `s1`.`FullName`, `s1`.`HasSoulPatch`, `s1`.`LeaderNickname`, `s1`.`LeaderSquadId`, `s1`.`Rank`, `s1`.`Discriminator`
FROM (
    SELECT DISTINCT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM (`Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
    INNER JOIN `Squads` AS `s` ON `g`.`SquadId` = `s`.`Id`
    WHERE `s`.`Id` = @__entity_equality_prm_Inner_Squad_0_Id
) AS `s1`
INNER JOIN `Squads` AS `s0` ON `s1`.`SquadId` = `s0`.`Id`
WHERE `s0`.`Id` = @__entity_equality_prm_Inner_Squad_0_Id
ORDER BY `s1`.`FullName`
""");
    }

    public override async Task Complex_GroupBy_after_set_operator(bool async)
    {
        await base.Complex_GroupBy_after_set_operator(async);

        AssertSql(
    """
SELECT `u`.`Name`, `u`.`Count`, IIF(SUM(`u`.`Count`) IS NULL, 0, SUM(`u`.`Count`)) AS `Sum`
FROM (
    SELECT `c`.`Name`, (
        SELECT COUNT(*)
        FROM `Weapons` AS `w`
        WHERE `g`.`FullName` = `w`.`OwnerFullName`) AS `Count`
    FROM `Gears` AS `g`
    LEFT JOIN `Cities` AS `c` ON `g`.`AssignedCityName` = `c`.`Name`
    UNION ALL
    SELECT `c0`.`Name`, (
        SELECT COUNT(*)
        FROM `Weapons` AS `w0`
        WHERE `g0`.`FullName` = `w0`.`OwnerFullName`) AS `Count`
    FROM `Gears` AS `g0`
    INNER JOIN `Cities` AS `c0` ON `g0`.`CityOfBirthName` = `c0`.`Name`
) AS `u`
GROUP BY `u`.`Name`, `u`.`Count`
""");
    }

    public override async Task Complex_GroupBy_after_set_operator_using_result_selector(bool async)
    {
        await base.Complex_GroupBy_after_set_operator_using_result_selector(async);

        AssertSql(
    """
SELECT `u`.`Name`, `u`.`Count`, IIF(SUM(`u`.`Count`) IS NULL, 0, SUM(`u`.`Count`)) AS `Sum`
FROM (
    SELECT `c`.`Name`, (
        SELECT COUNT(*)
        FROM `Weapons` AS `w`
        WHERE `g`.`FullName` = `w`.`OwnerFullName`) AS `Count`
    FROM `Gears` AS `g`
    LEFT JOIN `Cities` AS `c` ON `g`.`AssignedCityName` = `c`.`Name`
    UNION ALL
    SELECT `c0`.`Name`, (
        SELECT COUNT(*)
        FROM `Weapons` AS `w0`
        WHERE `g0`.`FullName` = `w0`.`OwnerFullName`) AS `Count`
    FROM `Gears` AS `g0`
    INNER JOIN `Cities` AS `c0` ON `g0`.`CityOfBirthName` = `c0`.`Name`
) AS `u`
GROUP BY `u`.`Name`, `u`.`Count`
""");
    }

    public override async Task Left_join_with_GroupBy_with_composite_group_key(bool async)
    {
        await base.Left_join_with_GroupBy_with_composite_group_key(async);

        AssertSql(
"""
SELECT `g`.`CityOfBirthName`, `g`.`HasSoulPatch`
FROM (`Gears` AS `g`
INNER JOIN `Squads` AS `s` ON `g`.`SquadId` = `s`.`Id`)
LEFT JOIN `Tags` AS `t` ON `g`.`Nickname` = `t`.`GearNickName`
GROUP BY `g`.`CityOfBirthName`, `g`.`HasSoulPatch`
""");
    }

    public override async Task GroupBy_with_boolean_grouping_key(bool async)
    {
        await base.GroupBy_with_boolean_grouping_key(async);

        AssertSql(
    """
SELECT `s`.`CityOfBirthName`, `s`.`HasSoulPatch`, `s`.`IsMarcus`, COUNT(*) AS `Count`
FROM (
    SELECT `g`.`CityOfBirthName`, `g`.`HasSoulPatch`, IIF(`g`.`Nickname` = 'Marcus', TRUE, FALSE) AS `IsMarcus`
    FROM `Gears` AS `g`
) AS `s`
GROUP BY `s`.`CityOfBirthName`, `s`.`HasSoulPatch`, `s`.`IsMarcus`
""");
    }

    public override async Task GroupBy_with_boolean_groupin_key_thru_navigation_access(bool async)
    {
        await base.GroupBy_with_boolean_groupin_key_thru_navigation_access(async);

        AssertSql(
    """
SELECT `s`.`HasSoulPatch`, LCASE(`s0`.`Name`) AS `Name`
FROM (`Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`HasSoulPatch`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`)
LEFT JOIN `Squads` AS `s0` ON `s`.`SquadId` = `s0`.`Id`
GROUP BY `s`.`HasSoulPatch`, `s0`.`Name`
""");
    }

    public override async Task Group_by_over_projection_with_multiple_properties_accessed_thru_navigation(bool async)
    {
        await base.Group_by_over_projection_with_multiple_properties_accessed_thru_navigation(async);

        AssertSql(
"""
SELECT `c`.`Name`
FROM `Gears` AS `g`
INNER JOIN `Cities` AS `c` ON `g`.`CityOfBirthName` = `c`.`Name`
GROUP BY `c`.`Name`
""");
    }

    public override async Task Group_by_on_StartsWith_with_null_parameter_as_argument(bool async)
    {
        await base.Group_by_on_StartsWith_with_null_parameter_as_argument(async);

        AssertSql(
    """
SELECT `s`.`Key`
FROM (
    SELECT FALSE AS `Key`
    FROM `Gears` AS `g`
) AS `s`
GROUP BY `s`.`Key`
""");
    }

    public override async Task Group_by_with_having_StartsWith_with_null_parameter_as_argument(bool async)
    {
        await base.Group_by_with_having_StartsWith_with_null_parameter_as_argument(async);

        AssertSql(
"""
SELECT `g`.`FullName`
FROM `Gears` AS `g`
GROUP BY `g`.`FullName`
HAVING 0 = 1
""");
    }

    public override async Task Select_StartsWith_with_null_parameter_as_argument(bool async)
    {
        await base.Select_StartsWith_with_null_parameter_as_argument(async);

        AssertSql(
"""
SELECT FALSE
FROM `Gears` AS `g`
""");
    }

    public override async Task Select_null_parameter_is_not_null(bool async)
    {
        await base.Select_null_parameter_is_not_null(async);

        AssertSql(
            """
@__p_0='False'

SELECT CBOOL(@__p_0)
FROM `Gears` AS `g`
""");
    }

    public override async Task Where_null_parameter_is_not_null(bool async)
    {
        await base.Where_null_parameter_is_not_null(async);

        AssertSql(
            """
@__p_0='False'

SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE @__p_0 = TRUE
""");
    }

    public override async Task OrderBy_StartsWith_with_null_parameter_as_argument(bool async)
    {
        await base.OrderBy_StartsWith_with_null_parameter_as_argument(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
ORDER BY `g`.`Nickname`
""");
    }

    public override async Task OrderBy_Contains_empty_list(bool async)
    {
        await base.OrderBy_Contains_empty_list(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
""");
    }

    public override async Task Where_with_enum_flags_parameter(bool async)
    {
        await base.Where_with_enum_flags_parameter(async);

        AssertSql(
"""
@__rank_0='1' (Nullable = true)
@__rank_0='1' (Nullable = true)

SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE (`g`.`Rank` BAND @__rank_0) = @__rank_0
""",
//
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
""",
//
"""
@__rank_0='2' (Nullable = true)
@__rank_0='2' (Nullable = true)

SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE (`g`.`Rank` BOR @__rank_0) <> @__rank_0
""",
//
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE 0 = 1
""");
    }

    public override async Task FirstOrDefault_navigation_access_entity_equality_in_where_predicate_apply_peneding_selector(bool async)
    {
        await base.FirstOrDefault_navigation_access_entity_equality_in_where_predicate_apply_peneding_selector(async);

        AssertSql(
    """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM (`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN `Cities` AS `c` ON `g`.`AssignedCityName` = `c`.`Name`
WHERE `c`.`Name` = (
    SELECT TOP 1 `c0`.`Name`
    FROM `Gears` AS `g0`
    INNER JOIN `Cities` AS `c0` ON `g0`.`CityOfBirthName` = `c0`.`Name`
    ORDER BY `g0`.`Nickname`) OR (`c`.`Name` IS NULL AND (
    SELECT TOP 1 `c0`.`Name`
    FROM `Gears` AS `g0`
    INNER JOIN `Cities` AS `c0` ON `g0`.`CityOfBirthName` = `c0`.`Name`
    ORDER BY `g0`.`Nickname`) IS NULL)
""");
    }

    public override async Task Bitwise_operation_with_non_null_parameter_optimizes_null_checks(bool async)
    {
        await base.Bitwise_operation_with_non_null_parameter_optimizes_null_checks(async);

        AssertSql(
            """
@__ranks_0='134'

SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE (`g`.`Rank` BAND @__ranks_0) <> 0
""",
            //
            """
@__ranks_0='134'
@__ranks_0='134'

SELECT CBOOL((`g`.`Rank` BOR @__ranks_0) BXOR @__ranks_0) BXOR TRUE
FROM `Gears` AS `g`
""",
            //
            """
@__ranks_0='134'
@__ranks_0='134'
@__ranks_0='134'

SELECT CBOOL((`g`.`Rank` BOR (`g`.`Rank` BOR (@__ranks_0 BOR (`g`.`Rank` BOR @__ranks_0)))) BXOR @__ranks_0) BXOR TRUE
FROM `Gears` AS `g`
""");
    }

    public override async Task Bitwise_operation_with_null_arguments(bool async)
    {
        await base.Bitwise_operation_with_null_arguments(async);

        AssertSql(
"""
SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE `w`.`AmmunitionType` IS NULL
""",
//
"""
SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE `w`.`AmmunitionType` IS NULL
""",
//
"""
SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE `w`.`AmmunitionType` IS NULL
""",
//
"""
SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
""",
//
"""
@__prm_0='2' (Nullable = true)

SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE (`w`.`AmmunitionType` BAND @__prm_0) <> 0 OR `w`.`AmmunitionType` IS NULL
""",
//
"""
@__prm_0='1' (Nullable = true)
@__prm_0='1' (Nullable = true)

SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE (`w`.`AmmunitionType` BAND @__prm_0) = @__prm_0
""");
    }

    public override async Task Logical_operation_with_non_null_parameter_optimizes_null_checks(bool async)
    {
        await base.Logical_operation_with_non_null_parameter_optimizes_null_checks(async);

        AssertSql(
"""
@__prm_0='True'

SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE `g`.`HasSoulPatch` <> @__prm_0
""",
//
"""
@__prm_0='False'

SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE `g`.`HasSoulPatch` <> @__prm_0
""");
    }

    public override async Task Cast_OfType_works_correctly(bool async)
    {
        await base.Cast_OfType_works_correctly(async);

        AssertSql(
"""
SELECT `g`.`FullName`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE `o`.`Nickname` IS NOT NULL
""");
    }

    public override async Task Join_inner_source_custom_projection_followed_by_filter(bool async)
    {
        await base.Join_inner_source_custom_projection_followed_by_filter(async);

        AssertSql(
            """
SELECT IIF(`s`.`Name` = 'Locust', TRUE, NULL) AS `IsEradicated`, `s`.`CommanderName`, `s`.`Name`
FROM `LocustLeaders` AS `l`
INNER JOIN (
    SELECT `f`.`Name`, `l0`.`CommanderName`
    FROM `Factions` AS `f`
    LEFT JOIN `LocustHordes` AS `l0` ON `f`.`Id` = `l0`.`Id`
    WHERE `l0`.`Id` IS NOT NULL
) AS `s` ON `l`.`Name` = `s`.`CommanderName`
WHERE IIF(`s`.`Name` = 'Locust', TRUE, NULL) = FALSE OR IIF(`s`.`Name` = 'Locust', TRUE, NULL) IS NULL
""");
    }

    public override async Task Byte_array_contains_literal(bool async)
    {
        await base.Byte_array_contains_literal(async);

        AssertSql(
            """
SELECT `s`.`Id`, `s`.`Banner`, `s`.`Banner5`, `s`.`InternalNumber`, `s`.`Name`
FROM `Squads` AS `s`
WHERE INSTR(1, STRCONV(`s`.`Banner`, 64), 0x01, 0) > 0
""");
    }

    public override async Task Byte_array_filter_by_length_literal(bool async)
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => base.Byte_array_filter_by_length_literal(async));
    }

    [ConditionalTheory]
    [MemberData(nameof(IsAsyncData))]
    public async Task Byte_array_filter_by_length_literal2(bool async)
    {
        await AssertQuery(
            async,
            ss => ss.Set<Squad>().Where(w => EF.Functions.ByteArrayLength(w.Banner) == 2),
            ss => ss.Set<Squad>().Where(w => w.Banner != null && w.Banner.Length == 2));

        AssertSql(
            """
SELECT `s`.`Id`, `s`.`Banner`, `s`.`Banner5`, `s`.`InternalNumber`, `s`.`Name`
FROM `Squads` AS `s`
WHERE IIF(ASCB(RIGHTB(`s`.`Banner`, 1)) = 0, LENB(`s`.`Banner`) - 1, LENB(`s`.`Banner`)) = 2
""");
    }

    public override async Task Byte_array_filter_by_length_parameter(bool async)
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => base.Byte_array_filter_by_length_parameter(async));
    }

    [ConditionalTheory]
    [MemberData(nameof(IsAsyncData))]
    public async Task Byte_array_filter_by_length_parameter2(bool async)
    {
        var someByteArr = new[] { (byte)42, (byte)24 };
        await AssertQuery(
            async,
            ss => ss.Set<Squad>().Where(w => EF.Functions.ByteArrayLength(w.Banner) == someByteArr.Length),
            ss => ss.Set<Squad>().Where(w => w.Banner != null && w.Banner.Length == someByteArr.Length));

        AssertSql(
            """
@__p_1='2'

SELECT `s`.`Id`, `s`.`Banner`, `s`.`Banner5`, `s`.`InternalNumber`, `s`.`Name`
FROM `Squads` AS `s`
WHERE IIF(ASCB(RIGHTB(`s`.`Banner`, 1)) = 0, LENB(`s`.`Banner`) - 1, LENB(`s`.`Banner`)) = @__p_1
""");
    }

    public override void Byte_array_filter_by_length_parameter_compiled()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => base.Byte_array_filter_by_length_parameter_compiled());
    }

    [ConditionalFact]
    public virtual void Byte_array_filter_by_length_parameter_compiled2()
    {
        var query = EF.CompileQuery(
            (GearsOfWarContext context, byte[] byteArrayParam)
                => context.Squads.Where(w => EF.Functions.ByteArrayLength(w.Banner) == EF.Functions.ByteArrayLength(byteArrayParam)).Count());

        using var context = CreateContext();
        var byteQueryParam = new[] { (byte)42, (byte)128 };

        Assert.Equal(2, query(context, byteQueryParam));

        AssertSql(
            """
@__byteArrayParam='0x2A80' (Size = 510)
@__byteArrayParam='0x2A80' (Size = 510)
@__byteArrayParam='0x2A80' (Size = 510)
@__byteArrayParam='0x2A80' (Size = 510)
@__byteArrayParam='0x2A80' (Size = 510)
@__byteArrayParam='0x2A80' (Size = 510)

SELECT COUNT(*)
FROM `Squads` AS `s`
WHERE IIF(ASCB(RIGHTB(`s`.`Banner`, 1)) = 0, LENB(`s`.`Banner`) - 1, LENB(`s`.`Banner`)) = IIF(IIF(ASCB(RIGHTB(@__byteArrayParam, 1)) = 0, LENB(@__byteArrayParam) - 1, LENB(@__byteArrayParam)) IS NULL, NULL, CLNG(IIF(ASCB(RIGHTB(@__byteArrayParam, 1)) = 0, LENB(@__byteArrayParam) - 1, LENB(@__byteArrayParam))))
""");
    }

    public override async Task Byte_array_contains_parameter(bool async)
    {
        await base.Byte_array_contains_parameter(async);

        AssertSql(
            """
@__someByte_0='1' (Size = 1)

SELECT `s`.`Id`, `s`.`Banner`, `s`.`Banner5`, `s`.`InternalNumber`, `s`.`Name`
FROM `Squads` AS `s`
WHERE INSTR(1, STRCONV(`s`.`Banner`, 64), CHR(@__someByte_0), 0) > 0
""");
    }

    public override async Task Byte_array_filter_by_length_literal_does_not_cast_on_varbinary_n(bool async)
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => base.Byte_array_filter_by_length_literal_does_not_cast_on_varbinary_n(async));
    }

    [ConditionalTheory]
    [MemberData(nameof(IsAsyncData))]
    public async Task Byte_array_filter_by_length_literal_does_not_cast_on_varbinary_n2(bool async)
    {
        await AssertQuery(
            async,
            ss => ss.Set<Squad>().Where(w => EF.Functions.ByteArrayLength(w.Banner5) == 5),
            ss => ss.Set<Squad>().Where(w => w.Banner5 != null && w.Banner5.Length == 5));

        AssertSql(
            """
SELECT `s`.`Id`, `s`.`Banner`, `s`.`Banner5`, `s`.`InternalNumber`, `s`.`Name`
FROM `Squads` AS `s`
WHERE IIF(ASCB(RIGHTB(`s`.`Banner5`, 1)) = 0, LENB(`s`.`Banner5`) - 1, LENB(`s`.`Banner5`)) = 5
""");
    }

    public override async Task Conditional_expression_with_test_being_simplified_to_constant_simple(bool isAsync)
    {
        await base.Conditional_expression_with_test_being_simplified_to_constant_simple(isAsync);

        AssertSql(
            """
@__prm_0='True'

SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE IIF(`g`.`HasSoulPatch` = @__prm_0, TRUE, FALSE) = TRUE
""");
    }

    public override async Task Conditional_expression_with_test_being_simplified_to_constant_complex(bool isAsync)
    {
        await base.Conditional_expression_with_test_being_simplified_to_constant_complex(isAsync);

        AssertSql(
"""
@__prm_0='True'
@__prm2_1='Marcus' Lancer' (Size = 255)

SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE IIF(`g`.`HasSoulPatch` = @__prm_0 AND (
        SELECT TOP 1 `w`.`Name`
        FROM `Weapons` AS `w`
        WHERE `w`.`Id` = `g`.`SquadId`) = @__prm2_1, TRUE, FALSE) = TRUE
""");
    }

    public override async Task OrderBy_bool_coming_from_optional_navigation(bool async)
    {
        await base.OrderBy_bool_coming_from_optional_navigation(async);

        AssertSql(
"""
SELECT `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
FROM `Weapons` AS `w`
LEFT JOIN `Weapons` AS `w0` ON `w`.`SynergyWithId` = `w0`.`Id`
ORDER BY NOT (`w0`.`IsAutomatic`)
""");
    }

    public override async Task DateTimeOffset_Date_returns_datetime(bool async)
    {
        var dateTimeOffset = new DateTimeOffset(102, 3, 1, 8, 0, 0, new TimeSpan(-5, 0, 0));

        await AssertQuery(
            async,
            ss => ss.Set<Mission>().Where(m => m.Timeline.Date >= dateTimeOffset.Date));

        AssertSql(
    """
@__dateTimeOffset_Date_0='0102-03-01T00:00:00.0000000' (DbType = DateTime)

SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Date`, `m`.`Difficulty`, `m`.`Duration`, `m`.`Rating`, `m`.`Time`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE DATEVALUE(`m`.`Timeline`) >= CDATE(@__dateTimeOffset_Date_0)
""");
    }

    public override async Task Conditional_with_conditions_evaluating_to_false_gets_optimized(bool async)
    {
        await base.Conditional_with_conditions_evaluating_to_false_gets_optimized(async);

        AssertSql(
"""
SELECT `g`.`FullName`
FROM `Gears` AS `g`
""");
    }

    public override async Task Conditional_with_conditions_evaluating_to_true_gets_optimized(bool async)
    {
        await base.Conditional_with_conditions_evaluating_to_true_gets_optimized(async);

        AssertSql(
"""
SELECT `g`.`CityOfBirthName`
FROM `Gears` AS `g`
""");
    }

    public override async Task Projecting_required_string_column_compared_to_null_parameter(bool async)
    {
        await base.Projecting_required_string_column_compared_to_null_parameter(async);

        AssertSql(
            """
SELECT FALSE
FROM `Gears` AS `g`
""");
    }

    public override async Task Byte_array_filter_by_SequenceEqual(bool isAsync)
    {
        await base.Byte_array_filter_by_SequenceEqual(isAsync);

        AssertSql(
"""
@__byteArrayParam_0='0x0405060708' (Size = 5)

SELECT `s`.`Id`, `s`.`Banner`, `s`.`Banner5`, `s`.`InternalNumber`, `s`.`Name`
FROM `Squads` AS `s`
WHERE `s`.`Banner5` = @__byteArrayParam_0
""");
    }

    public override async Task Group_by_nullable_property_HasValue_and_project_the_grouping_key(bool async)
    {
        await base.Group_by_nullable_property_HasValue_and_project_the_grouping_key(async);

        AssertSql(
            """
SELECT `w0`.`Key`
FROM (
    SELECT IIF(`w`.`SynergyWithId` IS NOT NULL, TRUE, FALSE) AS `Key`
    FROM `Weapons` AS `w`
) AS `w0`
GROUP BY `w0`.`Key`
""");
    }

    public override async Task Group_by_nullable_property_and_project_the_grouping_key_HasValue(bool async)
    {
        await base.Group_by_nullable_property_and_project_the_grouping_key_HasValue(async);

        AssertSql(
"""
SELECT IIF(`w`.`SynergyWithId` IS NOT NULL, TRUE, FALSE)
FROM `Weapons` AS `w`
GROUP BY `w`.`SynergyWithId`
""");
    }

    public override async Task Checked_context_with_cast_does_not_fail(bool isAsync)
    {
        await base.Checked_context_with_cast_does_not_fail(isAsync);

        AssertSql(
            """
SELECT `l`.`Name`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`ThreatLevelByte`, `l`.`ThreatLevelNullableByte`, `l0`.`DefeatedByNickname`, `l0`.`DefeatedBySquadId`, `l0`.`HighCommandId`, IIF(`l0`.`Name` IS NOT NULL, 'LocustCommander', NULL) AS `Discriminator`
FROM `LocustLeaders` AS `l`
LEFT JOIN `LocustCommanders` AS `l0` ON `l`.`Name` = `l0`.`Name`
WHERE CBYTE(`l`.`ThreatLevel`) >= CBYTE(5)
""");
    }

    public override async Task Checked_context_with_addition_does_not_fail(bool isAsync)
    {
        await base.Checked_context_with_addition_does_not_fail(isAsync);

        AssertSql(
            """
SELECT `l`.`Name`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`ThreatLevelByte`, `l`.`ThreatLevelNullableByte`, `l0`.`DefeatedByNickname`, `l0`.`DefeatedBySquadId`, `l0`.`HighCommandId`, IIF(`l0`.`Name` IS NOT NULL, 'LocustCommander', NULL) AS `Discriminator`
FROM `LocustLeaders` AS `l`
LEFT JOIN `LocustCommanders` AS `l0` ON `l`.`Name` = `l0`.`Name`
WHERE CLNG(`l`.`ThreatLevel`) <= (5 + CLNG(`l`.`ThreatLevel`))
""");
    }

    public override async Task TimeSpan_Hours(bool async)
    {
        await base.TimeSpan_Hours(async);

        AssertSql(
            """
SELECT DATEPART('h', `m`.`Duration`)
FROM `Missions` AS `m`
""");
    }

    public override async Task TimeSpan_Minutes(bool async)
    {
        await base.TimeSpan_Minutes(async);

        AssertSql(
"""
SELECT DATEPART('n', `m`.`Duration`)
FROM `Missions` AS `m`
""");
    }

    public override async Task TimeSpan_Seconds(bool async)
    {
        await base.TimeSpan_Seconds(async);

        AssertSql(
"""
SELECT DATEPART('s', `m`.`Duration`)
FROM `Missions` AS `m`
""");
    }

    public override async Task TimeSpan_Milliseconds(bool async)
    {
        await base.TimeSpan_Milliseconds(async);

        AssertSql(
"""
SELECT DATEPART(millisecond, [m].[Duration])
FROM [Missions] AS [m]
""");
    }

    public override async Task Where_TimeSpan_Hours(bool async)
    {
        await base.Where_TimeSpan_Hours(async);

        AssertSql(
    """
SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Date`, `m`.`Difficulty`, `m`.`Duration`, `m`.`Rating`, `m`.`Time`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE DATEPART('h', `m`.`Duration`) = 1
""");
    }

    public override async Task Where_TimeSpan_Minutes(bool async)
    {
        await base.Where_TimeSpan_Minutes(async);

        AssertSql(
    """
SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Date`, `m`.`Difficulty`, `m`.`Duration`, `m`.`Rating`, `m`.`Time`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE DATEPART('n', `m`.`Duration`) = 2
""");
    }

    public override async Task Where_TimeSpan_Seconds(bool async)
    {
        await base.Where_TimeSpan_Seconds(async);

        AssertSql(
    """
SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Date`, `m`.`Difficulty`, `m`.`Duration`, `m`.`Rating`, `m`.`Time`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE DATEPART('s', `m`.`Duration`) = 3
""");
    }

    public override async Task Where_TimeSpan_Milliseconds(bool async)
    {
        await base.Where_TimeSpan_Milliseconds(async);

        AssertSql(
"""
SELECT [m].[Id], [m].[CodeName], [m].[Duration], [m].[Rating], [m].[Timeline]
FROM [Missions] AS [m]
WHERE DATEPART(millisecond, [m].[Duration]) = 1
""");
    }

    public override async Task Contains_on_collection_of_byte_subquery(bool async)
    {
        await base.Contains_on_collection_of_byte_subquery(async);

        AssertSql(
    """
SELECT `l`.`Name`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`ThreatLevelByte`, `l`.`ThreatLevelNullableByte`, `l0`.`DefeatedByNickname`, `l0`.`DefeatedBySquadId`, `l0`.`HighCommandId`, IIF(`l0`.`Name` IS NOT NULL, 'LocustCommander', NULL) AS `Discriminator`
FROM `LocustLeaders` AS `l`
LEFT JOIN `LocustCommanders` AS `l0` ON `l`.`Name` = `l0`.`Name`
WHERE `l`.`ThreatLevelByte` IN (
    SELECT `l1`.`ThreatLevelByte`
    FROM `LocustLeaders` AS `l1`
)
""");
    }

    public override async Task Contains_on_collection_of_nullable_byte_subquery(bool async)
    {
        await base.Contains_on_collection_of_nullable_byte_subquery(async);

        AssertSql(
    """
SELECT `l`.`Name`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`ThreatLevelByte`, `l`.`ThreatLevelNullableByte`, `l0`.`DefeatedByNickname`, `l0`.`DefeatedBySquadId`, `l0`.`HighCommandId`, IIF(`l0`.`Name` IS NOT NULL, 'LocustCommander', NULL) AS `Discriminator`
FROM `LocustLeaders` AS `l`
LEFT JOIN `LocustCommanders` AS `l0` ON `l`.`Name` = `l0`.`Name`
WHERE EXISTS (
    SELECT 1
    FROM `LocustLeaders` AS `l1`
    WHERE `l1`.`ThreatLevelNullableByte` = `l`.`ThreatLevelNullableByte` OR (`l1`.`ThreatLevelNullableByte` IS NULL AND `l`.`ThreatLevelNullableByte` IS NULL))
""");
    }

    public override async Task Contains_on_collection_of_nullable_byte_subquery_null_constant(bool async)
    {
        await base.Contains_on_collection_of_nullable_byte_subquery_null_constant(async);

        AssertSql(
            """
SELECT `l`.`Name`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`ThreatLevelByte`, `l`.`ThreatLevelNullableByte`, `l0`.`DefeatedByNickname`, `l0`.`DefeatedBySquadId`, `l0`.`HighCommandId`, IIF(`l0`.`Name` IS NOT NULL, 'LocustCommander', NULL) AS `Discriminator`
FROM `LocustLeaders` AS `l`
LEFT JOIN `LocustCommanders` AS `l0` ON `l`.`Name` = `l0`.`Name`
WHERE EXISTS (
    SELECT 1
    FROM `LocustLeaders` AS `l1`
    WHERE `l1`.`ThreatLevelNullableByte` IS NULL)
""");
    }

    public override async Task Contains_on_collection_of_nullable_byte_subquery_null_parameter(bool async)
    {
        await base.Contains_on_collection_of_nullable_byte_subquery_null_parameter(async);

        AssertSql(
    """
SELECT `l`.`Name`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`ThreatLevelByte`, `l`.`ThreatLevelNullableByte`, `l0`.`DefeatedByNickname`, `l0`.`DefeatedBySquadId`, `l0`.`HighCommandId`, IIF(`l0`.`Name` IS NOT NULL, 'LocustCommander', NULL) AS `Discriminator`
FROM `LocustLeaders` AS `l`
LEFT JOIN `LocustCommanders` AS `l0` ON `l`.`Name` = `l0`.`Name`
WHERE EXISTS (
    SELECT 1
    FROM `LocustLeaders` AS `l1`
    WHERE `l1`.`ThreatLevelNullableByte` IS NULL)
""");
    }

    public override async Task Contains_on_byte_array_property_using_byte_column(bool async)
    {
        await base.Contains_on_byte_array_property_using_byte_column(async);

        AssertSql(
            """
SELECT `s`.`Id`, `s`.`Banner`, `s`.`Banner5`, `s`.`InternalNumber`, `s`.`Name`, `s0`.`Name`, `s0`.`LocustHordeId`, `s0`.`ThreatLevel`, `s0`.`ThreatLevelByte`, `s0`.`ThreatLevelNullableByte`, `s0`.`DefeatedByNickname`, `s0`.`DefeatedBySquadId`, `s0`.`HighCommandId`, `s0`.`Discriminator`
FROM `Squads` AS `s`,
(
    SELECT `l`.`Name`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`ThreatLevelByte`, `l`.`ThreatLevelNullableByte`, `l0`.`DefeatedByNickname`, `l0`.`DefeatedBySquadId`, `l0`.`HighCommandId`, IIF(`l0`.`Name` IS NOT NULL, 'LocustCommander', NULL) AS `Discriminator`
    FROM `LocustLeaders` AS `l`
    LEFT JOIN `LocustCommanders` AS `l0` ON `l`.`Name` = `l0`.`Name`
) AS `s0`
WHERE INSTR(1, STRCONV(`s`.`Banner`, 64), CHR(`s0`.`ThreatLevelByte`), 0) > 0
""");
    }

    public override async Task Subquery_projecting_non_nullable_scalar_contains_non_nullable_value_doesnt_need_null_expansion(
        bool async)
    {
        await base.Subquery_projecting_non_nullable_scalar_contains_non_nullable_value_doesnt_need_null_expansion(async);

        AssertSql(
"""
SELECT [t].[Nickname], [t].[SquadId], [t].[AssignedCityName], [t].[CityOfBirthName], [t].[FullName], [t].[HasSoulPatch], [t].[LeaderNickname], [t].[LeaderSquadId], [t].[Rank], [t].[Discriminator]
FROM [LocustLeaders] AS [l]
CROSS APPLY (
    SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOfBirthName], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], CASE
        WHEN [o].[Nickname] IS NOT NULL THEN N'Officer'
    END AS [Discriminator]
    FROM [Gears] AS [g]
    LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
    WHERE EXISTS (
        SELECT 1
        FROM [LocustLeaders] AS [l1]
        LEFT JOIN [LocustCommanders] AS [l2] ON [l1].[Name] = [l2].[Name]
        WHERE [l1].[ThreatLevelByte] = [l].[ThreatLevelByte])
) AS [t]
""");
    }

    public override async Task Subquery_projecting_non_nullable_scalar_contains_non_nullable_value_doesnt_need_null_expansion_negated(
        bool async)
    {
        await base.Subquery_projecting_non_nullable_scalar_contains_non_nullable_value_doesnt_need_null_expansion_negated(async);

        AssertSql(
"""
SELECT [t].[Nickname], [t].[SquadId], [t].[AssignedCityName], [t].[CityOfBirthName], [t].[FullName], [t].[HasSoulPatch], [t].[LeaderNickname], [t].[LeaderSquadId], [t].[Rank], [t].[Discriminator]
FROM [LocustLeaders] AS [l]
CROSS APPLY (
    SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOfBirthName], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], CASE
        WHEN [o].[Nickname] IS NOT NULL THEN N'Officer'
    END AS [Discriminator]
    FROM [Gears] AS [g]
    LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
    WHERE NOT (EXISTS (
        SELECT 1
        FROM [LocustLeaders] AS [l1]
        LEFT JOIN [LocustCommanders] AS [l2] ON [l1].[Name] = [l2].[Name]
        WHERE [l1].[ThreatLevelByte] = [l].[ThreatLevelByte]))
) AS [t]
""");
    }

    public override async Task Subquery_projecting_nullable_scalar_contains_nullable_value_needs_null_expansion(bool async)
    {
        await base.Subquery_projecting_nullable_scalar_contains_nullable_value_needs_null_expansion(async);

        AssertSql(
"""
SELECT [t].[Nickname], [t].[SquadId], [t].[AssignedCityName], [t].[CityOfBirthName], [t].[FullName], [t].[HasSoulPatch], [t].[LeaderNickname], [t].[LeaderSquadId], [t].[Rank], [t].[Discriminator]
FROM [LocustLeaders] AS [l]
CROSS APPLY (
    SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOfBirthName], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], CASE
        WHEN [o].[Nickname] IS NOT NULL THEN N'Officer'
    END AS [Discriminator]
    FROM [Gears] AS [g]
    LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
    WHERE EXISTS (
        SELECT 1
        FROM [LocustLeaders] AS [l1]
        LEFT JOIN [LocustCommanders] AS [l2] ON [l1].[Name] = [l2].[Name]
        WHERE [l1].[ThreatLevelNullableByte] = [l].[ThreatLevelNullableByte] OR (([l1].[ThreatLevelNullableByte] IS NULL) AND ([l].[ThreatLevelNullableByte] IS NULL)))
) AS [t]
""");
    }

    public override async Task Subquery_projecting_nullable_scalar_contains_nullable_value_needs_null_expansion_negated(bool async)
    {
        await base.Subquery_projecting_nullable_scalar_contains_nullable_value_needs_null_expansion_negated(async);

        AssertSql(
"""
SELECT [t].[Nickname], [t].[SquadId], [t].[AssignedCityName], [t].[CityOfBirthName], [t].[FullName], [t].[HasSoulPatch], [t].[LeaderNickname], [t].[LeaderSquadId], [t].[Rank], [t].[Discriminator]
FROM [LocustLeaders] AS [l]
CROSS APPLY (
    SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOfBirthName], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], CASE
        WHEN [o].[Nickname] IS NOT NULL THEN N'Officer'
    END AS [Discriminator]
    FROM [Gears] AS [g]
    LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
    WHERE NOT (EXISTS (
        SELECT 1
        FROM [LocustLeaders] AS [l1]
        LEFT JOIN [LocustCommanders] AS [l2] ON [l1].[Name] = [l2].[Name]
        WHERE [l1].[ThreatLevelNullableByte] = [l].[ThreatLevelNullableByte] OR (([l1].[ThreatLevelNullableByte] IS NULL) AND ([l].[ThreatLevelNullableByte] IS NULL))))
) AS [t]
""");
    }

    public override async Task Enum_closure_typed_as_underlying_type_generates_correct_parameter_type(bool async)
    {
        await base.Enum_closure_typed_as_underlying_type_generates_correct_parameter_type(async);

        AssertSql(
            """
@__prm_0='1' (Nullable = true)

SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE @__prm_0 = `w`.`AmmunitionType`
""");
    }

    public override async Task Enum_flags_closure_typed_as_underlying_type_generates_correct_parameter_type(bool async)
    {
        await base.Enum_flags_closure_typed_as_underlying_type_generates_correct_parameter_type(async);

        AssertSql(
"""
@__prm_0='133'

SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE (@__prm_0 BAND `g`.`Rank`) = `g`.`Rank`
""");
    }

    public override async Task Enum_flags_closure_typed_as_different_type_generates_correct_parameter_type(bool async)
    {
        await base.Enum_flags_closure_typed_as_different_type_generates_correct_parameter_type(async);

        AssertSql(
            """
@__prm_0='5'

SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE (@__prm_0 BAND CLNG(`g`.`Rank`)) = CLNG(`g`.`Rank`)
""");
    }

    public override async Task Constant_enum_with_same_underlying_value_as_previously_parameterized_int(bool async)
    {
        await base.Constant_enum_with_same_underlying_value_as_previously_parameterized_int(async);

        AssertSql(
"""
SELECT TOP 1 `g`.`Rank` BAND 1
FROM `Gears` AS `g`
ORDER BY `g`.`Nickname`
""");
    }

    public override async Task Enum_array_contains(bool async)
    {
        await base.Enum_array_contains(async);

        AssertSql(
"""
SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
LEFT JOIN `Weapons` AS `w0` ON `w`.`SynergyWithId` = `w0`.`Id`
WHERE `w0`.`Id` IS NOT NULL AND (`w0`.`AmmunitionType` IS NULL OR `w0`.`AmmunitionType` = 1)
""");
    }

    /*[ConditionalTheory]
    [MemberData(nameof(IsAsyncData))]
    public async Task DataLength_function_for_string_parameter(bool async)
    {
        await AssertQueryScalar(
            async,
            ss => ss.Set<Mission>().Select(m => EF.Functions.DataLength(m.CodeName)),
            ss => ss.Set<Mission>().Select(m => (int?)(m.CodeName.Length * 2)));

        AssertSql(
"""
SELECT CAST(DATALENGTH([m].[CodeName]) AS int)
FROM [Missions] AS [m]
""");
    }*/

    public override async Task CompareTo_used_with_non_unicode_string_column_and_constant(bool async)
    {
        await base.CompareTo_used_with_non_unicode_string_column_and_constant(async);

        AssertSql(
            """
SELECT `c`.`Name`, `c`.`Location`, `c`.`Nation`
FROM `Cities` AS `c`
WHERE `c`.`Location` = 'Unknown'
""");
    }

    public override async Task Coalesce_used_with_non_unicode_string_column_and_constant(bool async)
    {
        await base.Coalesce_used_with_non_unicode_string_column_and_constant(async);

        AssertSql(
"""
SELECT IIF(`c`.`Location` IS NULL, 'Unknown', `c`.`Location`)
FROM `Cities` AS `c`
""");
    }

    public override async Task Groupby_anonymous_type_with_navigations_followed_up_by_anonymous_projection_and_orderby(bool async)
    {
        await base.Groupby_anonymous_type_with_navigations_followed_up_by_anonymous_projection_and_orderby(async);

        AssertSql(
            """
SELECT `c`.`Name`, `c`.`Location`, COUNT(*) AS `Count`
FROM (`Weapons` AS `w`
LEFT JOIN (
    SELECT `g`.`CityOfBirthName`, `g`.`FullName`
    FROM `Gears` AS `g`
) AS `s` ON `w`.`OwnerFullName` = `s`.`FullName`)
LEFT JOIN `Cities` AS `c` ON `s`.`CityOfBirthName` = `c`.`Name`
GROUP BY `c`.`Name`, `c`.`Location`
ORDER BY `c`.`Location`
""");
    }

    public override async Task SelectMany_predicate_with_non_equality_comparison_converted_to_inner_join(bool async)
    {
        await base.SelectMany_predicate_with_non_equality_comparison_converted_to_inner_join(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM (`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
INNER JOIN `Weapons` AS `w` ON `g`.`FullName` <> `w`.`OwnerFullName` OR `w`.`OwnerFullName` IS NULL
ORDER BY `g`.`Nickname`, `w`.`Id`
""");
    }

    public override async Task SelectMany_predicate_with_non_equality_comparison_DefaultIfEmpty_converted_to_left_join(bool async)
    {
        await base.SelectMany_predicate_with_non_equality_comparison_DefaultIfEmpty_converted_to_left_join(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM (`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN `Weapons` AS `w` ON `g`.`FullName` <> `w`.`OwnerFullName` OR `w`.`OwnerFullName` IS NULL
ORDER BY `g`.`Nickname`, `w`.`Id`
""");
    }

    public override async Task SelectMany_predicate_after_navigation_with_non_equality_comparison_DefaultIfEmpty_converted_to_left_join(
        bool async)
    {
        await base.SelectMany_predicate_after_navigation_with_non_equality_comparison_DefaultIfEmpty_converted_to_left_join(async);

        AssertSql(
    """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `s`.`Id`, `s`.`AmmunitionType`, `s`.`IsAutomatic`, `s`.`Name`, `s`.`OwnerFullName`, `s`.`SynergyWithId`
FROM (`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN (
    SELECT `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
    FROM `Weapons` AS `w`
    LEFT JOIN `Weapons` AS `w0` ON `w`.`SynergyWithId` = `w0`.`Id`
) AS `s` ON `g`.`FullName` <> `s`.`OwnerFullName` OR `s`.`OwnerFullName` IS NULL
ORDER BY `g`.`Nickname`, `s`.`Id`
""");
    }

    public override async Task SelectMany_without_result_selector_and_non_equality_comparison_converted_to_join(bool async)
    {
        await base.SelectMany_without_result_selector_and_non_equality_comparison_converted_to_join(async);

        AssertSql(
"""
SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Gears` AS `g`
LEFT JOIN `Weapons` AS `w` ON `g`.`FullName` <> `w`.`OwnerFullName` OR `w`.`OwnerFullName` IS NULL
""");
    }

    public override async Task Filtered_collection_projection_with_order_comparison_predicate_converted_to_join(bool async)
    {
        await base.Filtered_collection_projection_with_order_comparison_predicate_converted_to_join(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Gears` AS `g`
LEFT JOIN `Weapons` AS `w` ON `g`.`FullName` = `w`.`OwnerFullName` AND `g`.`SquadId` < `w`.`Id`
ORDER BY `g`.`Nickname`, `g`.`SquadId`
""");
    }

    public override async Task Filtered_collection_projection_with_order_comparison_predicate_converted_to_join2(bool async)
    {
        await base.Filtered_collection_projection_with_order_comparison_predicate_converted_to_join2(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Gears` AS `g`
LEFT JOIN `Weapons` AS `w` ON `g`.`FullName` = `w`.`OwnerFullName` AND `g`.`SquadId` <= `w`.`Id`
ORDER BY `g`.`Nickname`, `g`.`SquadId`
""");
    }

    public override async Task Filtered_collection_projection_with_order_comparison_predicate_converted_to_join3(bool async)
    {
        await base.Filtered_collection_projection_with_order_comparison_predicate_converted_to_join3(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Gears` AS `g`
LEFT JOIN `Weapons` AS `w` ON `g`.`FullName` = `w`.`OwnerFullName` AND `g`.`SquadId` >= `w`.`Id`
ORDER BY `g`.`Nickname`, `g`.`SquadId`
""");
    }

    public override async Task SelectMany_predicate_with_non_equality_comparison_with_Take_doesnt_convert_to_join(bool async)
    {
        await base.SelectMany_predicate_with_non_equality_comparison_with_Take_doesnt_convert_to_join(async);

        AssertSql(
"""
SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOfBirthName], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], CASE
    WHEN [o].[Nickname] IS NOT NULL THEN N'Officer'
END AS [Discriminator], [t].[Id], [t].[AmmunitionType], [t].[IsAutomatic], [t].[Name], [t].[OwnerFullName], [t].[SynergyWithId]
FROM [Gears] AS [g]
LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
CROSS APPLY (
    SELECT TOP(3) [w].[Id], [w].[AmmunitionType], [w].[IsAutomatic], [w].[Name], [w].[OwnerFullName], [w].[SynergyWithId]
    FROM [Weapons] AS [w]
    WHERE [w].[OwnerFullName] <> [g].[FullName] OR ([w].[OwnerFullName] IS NULL)
    ORDER BY [w].[Id]
) AS [t]
ORDER BY [g].[Nickname], [t].[Id]
""");
    }

    public override async Task FirstOrDefault_over_int_compared_to_zero(bool async)
    {
        await base.FirstOrDefault_over_int_compared_to_zero(async);

        AssertSql(
            """
SELECT `s`.`Name`
FROM `Squads` AS `s`
WHERE `s`.`Name` = 'Delta' AND IIF((
        SELECT TOP 1 `g`.`SquadId`
        FROM `Gears` AS `g`
        WHERE `s`.`Id` = `g`.`SquadId` AND `g`.`HasSoulPatch` = TRUE
        ORDER BY `g`.`FullName`) IS NULL, 0, (
        SELECT TOP 1 `g`.`SquadId`
        FROM `Gears` AS `g`
        WHERE `s`.`Id` = `g`.`SquadId` AND `g`.`HasSoulPatch` = TRUE
        ORDER BY `g`.`FullName`)) <> 0
""");
    }

    public override async Task Correlated_collection_with_inner_collection_references_element_two_levels_up(bool async)
    {
        await base.Correlated_collection_with_inner_collection_references_element_two_levels_up(async);

        AssertSql(
"""
SELECT [g].[FullName], [g].[Nickname], [g].[SquadId], [t].[ReportName], [t].[OfficerName], [t].[Nickname], [t].[SquadId]
FROM [Gears] AS [g]
LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
OUTER APPLY (
    SELECT [g0].[FullName] AS [ReportName], [g].[FullName] AS [OfficerName], [g0].[Nickname], [g0].[SquadId]
    FROM [Gears] AS [g0]
    WHERE [g].[Nickname] = [g0].[LeaderNickname] AND [g].[SquadId] = [g0].[LeaderSquadId]
) AS [t]
WHERE [o].[Nickname] IS NOT NULL
ORDER BY [g].[Nickname], [g].[SquadId], [t].[Nickname]
""");
    }

    public override async Task Accessing_derived_property_using_hard_and_soft_cast(bool async)
    {
        await base.Accessing_derived_property_using_hard_and_soft_cast(async);

        AssertSql(
"""
SELECT `l`.`Name`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`ThreatLevelByte`, `l`.`ThreatLevelNullableByte`, `l0`.`DefeatedByNickname`, `l0`.`DefeatedBySquadId`, `l0`.`HighCommandId`, IIF(`l0`.`Name` IS NOT NULL, 'LocustCommander', NULL) AS `Discriminator`
FROM `LocustLeaders` AS `l`
LEFT JOIN `LocustCommanders` AS `l0` ON `l`.`Name` = `l0`.`Name`
WHERE `l0`.`Name` IS NOT NULL AND (`l0`.`HighCommandId` <> 0 OR `l0`.`HighCommandId` IS NULL)
""",
//
"""
SELECT `l`.`Name`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`ThreatLevelByte`, `l`.`ThreatLevelNullableByte`, `l0`.`DefeatedByNickname`, `l0`.`DefeatedBySquadId`, `l0`.`HighCommandId`, IIF(`l0`.`Name` IS NOT NULL, 'LocustCommander', NULL) AS `Discriminator`
FROM `LocustLeaders` AS `l`
LEFT JOIN `LocustCommanders` AS `l0` ON `l`.`Name` = `l0`.`Name`
WHERE `l0`.`Name` IS NOT NULL AND (`l0`.`HighCommandId` <> 0 OR `l0`.`HighCommandId` IS NULL)
""");
    }

    public override async Task Cast_to_derived_followed_by_include_and_FirstOrDefault(bool async)
    {
        await base.Cast_to_derived_followed_by_include_and_FirstOrDefault(async);
AssertSql(
    """
SELECT TOP 1 `l`.`Name`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`ThreatLevelByte`, `l`.`ThreatLevelNullableByte`, `l0`.`DefeatedByNickname`, `l0`.`DefeatedBySquadId`, `l0`.`HighCommandId`, IIF(`l0`.`Name` IS NOT NULL, 'LocustCommander', NULL) AS `Discriminator`, `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`
FROM (`LocustLeaders` AS `l`
LEFT JOIN `LocustCommanders` AS `l0` ON `l`.`Name` = `l0`.`Name`)
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
) AS `s` ON `l0`.`DefeatedByNickname` = `s`.`Nickname` AND `l0`.`DefeatedBySquadId` = `s`.`SquadId`
WHERE `l`.`Name` LIKE '%Queen%'
""");
    }

    public override async Task Correlated_collection_take(bool async)
    {
        await base.Correlated_collection_take(async);

        AssertSql(
"""
SELECT [g].[Nickname], [g].[SquadId], [c].[Name], [t0].[Id], [t0].[AmmunitionType], [t0].[IsAutomatic], [t0].[Name], [t0].[OwnerFullName], [t0].[SynergyWithId], [c].[Location], [c].[Nation]
FROM [Gears] AS [g]
INNER JOIN [Cities] AS [c] ON [g].[CityOfBirthName] = [c].[Name]
LEFT JOIN (
    SELECT [t].[Id], [t].[AmmunitionType], [t].[IsAutomatic], [t].[Name], [t].[OwnerFullName], [t].[SynergyWithId]
    FROM (
        SELECT [w].[Id], [w].[AmmunitionType], [w].[IsAutomatic], [w].[Name], [w].[OwnerFullName], [w].[SynergyWithId], ROW_NUMBER() OVER(PARTITION BY [w].[OwnerFullName] ORDER BY [w].[Id]) AS [row]
        FROM [Weapons] AS [w]
    ) AS [t]
    WHERE [t].[row] <= 10
) AS [t0] ON [g].[FullName] = [t0].[OwnerFullName]
ORDER BY [g].[Nickname], [g].[SquadId], [c].[Name]
""");
    }

    public override async Task First_on_byte_array(bool async)
    {
        await base.First_on_byte_array(async);

        AssertSql(
            """
SELECT `s`.`Id`, `s`.`Banner`, `s`.`Banner5`, `s`.`InternalNumber`, `s`.`Name`
FROM `Squads` AS `s`
WHERE ASCB(MIDB(`s`.`Banner`, 1, 1)) = 2
""");
    }

    public override async Task Array_access_on_byte_array(bool async)
    {
        await base.Array_access_on_byte_array(async);

        AssertSql(
            """
SELECT `s`.`Id`, `s`.`Banner`, `s`.`Banner5`, `s`.`InternalNumber`, `s`.`Name`
FROM `Squads` AS `s`
WHERE ASCB(MIDB(`s`.`Banner5`, 2 + 1, 1)) = 6
""");
    }

    public override async Task Project_shadow_properties(bool async)
    {
        await base.Project_shadow_properties(async);

        AssertSql(
        """
SELECT `g`.`Nickname`, `g`.`AssignedCityName`
FROM `Gears` AS `g`
""");
    }

    public override async Task Composite_key_entity_equal(bool async)
    {
        await base.Composite_key_entity_equal(async);

        AssertSql(
"""
SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOfBirthName], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], CASE
    WHEN [o].[Nickname] IS NOT NULL THEN N'Officer'
END AS [Discriminator], [t].[Nickname], [t].[SquadId], [t].[AssignedCityName], [t].[CityOfBirthName], [t].[FullName], [t].[HasSoulPatch], [t].[LeaderNickname], [t].[LeaderSquadId], [t].[Rank], [t].[Discriminator]
FROM [Gears] AS [g]
LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
CROSS JOIN (
    SELECT [g0].[Nickname], [g0].[SquadId], [g0].[AssignedCityName], [g0].[CityOfBirthName], [g0].[FullName], [g0].[HasSoulPatch], [g0].[LeaderNickname], [g0].[LeaderSquadId], [g0].[Rank], CASE
        WHEN [o0].[Nickname] IS NOT NULL THEN N'Officer'
    END AS [Discriminator]
    FROM [Gears] AS [g0]
    LEFT JOIN [Officers] AS [o0] ON [g0].[Nickname] = [o0].[Nickname] AND [g0].[SquadId] = [o0].[SquadId]
) AS [t]
WHERE [g].[Nickname] = [t].[Nickname] AND [g].[SquadId] = [t].[SquadId]
""");
    }

    public override async Task Composite_key_entity_not_equal(bool async)
    {
        await base.Composite_key_entity_not_equal(async);

        AssertSql(
"""
SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOfBirthName], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], CASE
    WHEN [o].[Nickname] IS NOT NULL THEN N'Officer'
END AS [Discriminator], [t].[Nickname], [t].[SquadId], [t].[AssignedCityName], [t].[CityOfBirthName], [t].[FullName], [t].[HasSoulPatch], [t].[LeaderNickname], [t].[LeaderSquadId], [t].[Rank], [t].[Discriminator]
FROM [Gears] AS [g]
LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
CROSS JOIN (
    SELECT [g0].[Nickname], [g0].[SquadId], [g0].[AssignedCityName], [g0].[CityOfBirthName], [g0].[FullName], [g0].[HasSoulPatch], [g0].[LeaderNickname], [g0].[LeaderSquadId], [g0].[Rank], CASE
        WHEN [o0].[Nickname] IS NOT NULL THEN N'Officer'
    END AS [Discriminator]
    FROM [Gears] AS [g0]
    LEFT JOIN [Officers] AS [o0] ON [g0].[Nickname] = [o0].[Nickname] AND [g0].[SquadId] = [o0].[SquadId]
) AS [t]
WHERE [g].[Nickname] <> [t].[Nickname] OR [g].[SquadId] <> [t].[SquadId]
""");
    }

    public override async Task Composite_key_entity_equal_null(bool async)
    {
        await base.Composite_key_entity_equal_null(async);

        AssertSql(
    """
SELECT `l`.`Name`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`ThreatLevelByte`, `l`.`ThreatLevelNullableByte`, `l0`.`DefeatedByNickname`, `l0`.`DefeatedBySquadId`, `l0`.`HighCommandId`, IIF(`l0`.`Name` IS NOT NULL, 'LocustCommander', NULL) AS `Discriminator`
FROM (`LocustLeaders` AS `l`
LEFT JOIN `LocustCommanders` AS `l0` ON `l`.`Name` = `l0`.`Name`)
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`
    FROM `Gears` AS `g`
) AS `s` ON `l0`.`DefeatedByNickname` = `s`.`Nickname` AND `l0`.`DefeatedBySquadId` = `s`.`SquadId`
WHERE `l0`.`Name` IS NOT NULL AND (`s`.`Nickname` IS NULL OR `s`.`SquadId` IS NULL)
""");
    }

    public override async Task Composite_key_entity_not_equal_null(bool async)
    {
        await base.Composite_key_entity_not_equal_null(async);

        AssertSql(
    """
SELECT `l`.`Name`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`ThreatLevelByte`, `l`.`ThreatLevelNullableByte`, `l0`.`DefeatedByNickname`, `l0`.`DefeatedBySquadId`, `l0`.`HighCommandId`, IIF(`l0`.`Name` IS NOT NULL, 'LocustCommander', NULL) AS `Discriminator`
FROM (`LocustLeaders` AS `l`
LEFT JOIN `LocustCommanders` AS `l0` ON `l`.`Name` = `l0`.`Name`)
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`
    FROM `Gears` AS `g`
) AS `s` ON `l0`.`DefeatedByNickname` = `s`.`Nickname` AND `l0`.`DefeatedBySquadId` = `s`.`SquadId`
WHERE `l0`.`Name` IS NOT NULL AND `s`.`Nickname` IS NOT NULL AND `s`.`SquadId` IS NOT NULL
""");
    }

    public override async Task Projecting_property_converted_to_nullable_with_comparison(bool async)
    {
        await base.Projecting_property_converted_to_nullable_with_comparison(async);

        AssertSql(
            """
SELECT `t`.`Note`, IIF(`t`.`GearNickName` IS NOT NULL, TRUE, FALSE), `s`.`Nickname`, `s`.`SquadId`, `s`.`HasSoulPatch`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`HasSoulPatch`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
WHERE IIF(`t`.`GearNickName` IS NOT NULL, `s`.`SquadId`, NULL) = 1
""");
    }

    public override async Task Projecting_property_converted_to_nullable_with_addition(bool async)
    {
        await base.Projecting_property_converted_to_nullable_with_addition(async);

        AssertSql(
            """
SELECT `t`.`Note`, IIF(`t`.`GearNickName` IS NOT NULL, TRUE, FALSE), `s`.`Nickname`, `s`.`SquadId`, `s`.`HasSoulPatch`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`HasSoulPatch`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
WHERE (IIF(`t`.`GearNickName` IS NOT NULL, `s`.`SquadId`, NULL) + 1) = 2
""");
    }

    public override async Task Projecting_property_converted_to_nullable_with_addition_and_final_projection(bool async)
    {
        await base.Projecting_property_converted_to_nullable_with_addition_and_final_projection(async);

        AssertSql(
            """
SELECT `t`.`Note`, IIF(`t`.`GearNickName` IS NOT NULL, `s`.`SquadId`, NULL) + 1 AS `Value`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
WHERE IIF(`t`.`GearNickName` IS NOT NULL, `s`.`Nickname`, NULL) IS NOT NULL
""");
    }

    public override async Task Projecting_property_converted_to_nullable_with_conditional(bool async)
    {
        await base.Projecting_property_converted_to_nullable_with_conditional(async);

        AssertSql(
            """
SELECT IIF(`t`.`Note` <> 'K.I.A.' OR `t`.`Note` IS NULL, IIF(`t`.`GearNickName` IS NOT NULL, `s`.`SquadId`, NULL), -1)
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
""");
    }

    public override async Task Projecting_property_converted_to_nullable_with_function_call(bool async)
    {
        await base.Projecting_property_converted_to_nullable_with_function_call(async);

        AssertSql(
            """
SELECT MID(IIF(`t`.`GearNickName` IS NOT NULL, `s`.`Nickname`, NULL), 0 + 1, 3)
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
""");
    }

    public override async Task Projecting_property_converted_to_nullable_with_function_call2(bool async)
    {
        await base.Projecting_property_converted_to_nullable_with_function_call2(async);

        AssertSql(
            """
SELECT `t`.`Note`, MID(`t`.`Note`, 0 + 1, IIF(`t`.`GearNickName` IS NOT NULL, `s`.`SquadId`, NULL)) AS `Function`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
WHERE IIF(`t`.`GearNickName` IS NOT NULL, `s`.`Nickname`, NULL) IS NOT NULL
""");
    }

    public override async Task Projecting_property_converted_to_nullable_into_element_init(bool async)
    {
        await base.Projecting_property_converted_to_nullable_into_element_init(async);

        AssertSql(
            """
SELECT IIF(`t`.`GearNickName` IS NOT NULL, IIF(LEN(`s`.`Nickname`) IS NULL, NULL, CLNG(LEN(`s`.`Nickname`))), NULL), IIF(`t`.`GearNickName` IS NOT NULL, `s`.`SquadId`, NULL), IIF(`t`.`GearNickName` IS NOT NULL, `s`.`SquadId`, NULL) + 1
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
WHERE IIF(`t`.`GearNickName` IS NOT NULL, `s`.`Nickname`, NULL) IS NOT NULL
ORDER BY `t`.`Note`
""");
    }

    public override async Task Projecting_property_converted_to_nullable_into_member_assignment(bool async)
    {
        await base.Projecting_property_converted_to_nullable_into_member_assignment(async);

        AssertSql(
            """
SELECT IIF(`t`.`GearNickName` IS NOT NULL, `s`.`SquadId`, NULL) AS `Id`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
WHERE IIF(`t`.`GearNickName` IS NOT NULL, `s`.`Nickname`, NULL) IS NOT NULL
ORDER BY `t`.`Note`
""");
    }

    public override async Task Projecting_property_converted_to_nullable_into_new_array(bool async)
    {
        await base.Projecting_property_converted_to_nullable_into_new_array(async);

        AssertSql(
            """
SELECT IIF(`t`.`GearNickName` IS NOT NULL, IIF(LEN(`s`.`Nickname`) IS NULL, NULL, CLNG(LEN(`s`.`Nickname`))), NULL), IIF(`t`.`GearNickName` IS NOT NULL, `s`.`SquadId`, NULL), IIF(`t`.`GearNickName` IS NOT NULL, `s`.`SquadId`, NULL) + 1
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
WHERE IIF(`t`.`GearNickName` IS NOT NULL, `s`.`Nickname`, NULL) IS NOT NULL
ORDER BY `t`.`Note`
""");
    }

    public override async Task Projecting_property_converted_to_nullable_into_unary(bool async)
    {
        await base.Projecting_property_converted_to_nullable_into_unary(async);

        AssertSql(
            """
SELECT `t`.`Note`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`HasSoulPatch`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
WHERE IIF(`t`.`GearNickName` IS NOT NULL, `s`.`Nickname`, NULL) IS NOT NULL AND IIF(`t`.`GearNickName` IS NOT NULL, `s`.`HasSoulPatch`, NULL) = FALSE
ORDER BY `t`.`Note`
""");
    }

    public override async Task Projecting_property_converted_to_nullable_into_member_access(bool async)
    {
        await base.Projecting_property_converted_to_nullable_into_member_access(async);

        AssertSql(
"""
SELECT `g`.`Nickname`
FROM `Gears` AS `g`
LEFT JOIN `Tags` AS `t` ON `g`.`Nickname` = `t`.`GearNickName` AND `g`.`SquadId` = `t`.`GearSquadId`
WHERE DATEPART('m', `t`.`IssueDate`) <> 5
ORDER BY `g`.`Nickname`
""");
    }

    public override async Task Projecting_property_converted_to_nullable_and_use_it_in_order_by(bool async)
    {
        await base.Projecting_property_converted_to_nullable_and_use_it_in_order_by(async);

        AssertSql(
            """
SELECT `t`.`Note`, IIF(`t`.`GearNickName` IS NOT NULL, TRUE, FALSE), `s`.`Nickname`, `s`.`SquadId`, `s`.`HasSoulPatch`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`HasSoulPatch`
    FROM `Gears` AS `g`
) AS `s` ON `t`.`GearNickName` = `s`.`Nickname` AND `t`.`GearSquadId` = `s`.`SquadId`
WHERE IIF(`t`.`GearNickName` IS NOT NULL, `s`.`Nickname`, NULL) IS NOT NULL
ORDER BY IIF(`t`.`GearNickName` IS NOT NULL, `s`.`SquadId`, NULL), `t`.`Note`
""");
    }

    public override async Task Where_DateOnly_Year(bool async)
    {
        await base.Where_DateOnly_Year(async);

        AssertSql(
    """
SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Date`, `m`.`Difficulty`, `m`.`Duration`, `m`.`Rating`, `m`.`Time`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE DATEPART('yyyy', `m`.`Date`) = 1990
""");
    }

    public override async Task Where_DateOnly_Month(bool async)
    {
        await base.Where_DateOnly_Month(async);

        AssertSql(
    """
SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Date`, `m`.`Difficulty`, `m`.`Duration`, `m`.`Rating`, `m`.`Time`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE DATEPART('m', `m`.`Date`) = 11
""");
    }

    public override async Task Where_DateOnly_Day(bool async)
    {
        await base.Where_DateOnly_Day(async);

        AssertSql(
    """
SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Date`, `m`.`Difficulty`, `m`.`Duration`, `m`.`Rating`, `m`.`Time`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE DATEPART('d', `m`.`Date`) = 10
""");
    }

    public override async Task Where_DateOnly_DayOfYear(bool async)
    {
        await base.Where_DateOnly_DayOfYear(async);

        AssertSql(
    """
SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Date`, `m`.`Difficulty`, `m`.`Duration`, `m`.`Rating`, `m`.`Time`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE DATEPART('y', `m`.`Date`) = 314
""");
    }

    public override async Task Where_DateOnly_DayOfWeek(bool async)
    {
        await AssertTranslationFailed(() => base.Where_DateOnly_DayOfWeek(async));

        AssertSql();
    }

    public override async Task Where_DateOnly_AddYears(bool async)
    {
        await base.Where_DateOnly_AddYears(async);

        AssertSql(
    """
SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Date`, `m`.`Difficulty`, `m`.`Duration`, `m`.`Rating`, `m`.`Time`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE DATEADD('yyyy', CLNG(3), `m`.`Date`) = #1993-11-10#
""");
    }

    public override async Task Where_DateOnly_AddMonths(bool async)
    {
        await base.Where_DateOnly_AddMonths(async);

        AssertSql(
    """
SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Date`, `m`.`Difficulty`, `m`.`Duration`, `m`.`Rating`, `m`.`Time`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE DATEADD('m', CLNG(3), `m`.`Date`) = #1991-02-10#
""");
    }

    public override async Task Where_DateOnly_AddDays(bool async)
    {
        await base.Where_DateOnly_AddDays(async);

        AssertSql(
    """
SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Date`, `m`.`Difficulty`, `m`.`Duration`, `m`.`Rating`, `m`.`Time`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE DATEADD('d', CLNG(3), `m`.`Date`) = #1990-11-13#
""");
    }

    public override async Task Where_TimeOnly_Hour(bool async)
    {
        await base.Where_TimeOnly_Hour(async);

        AssertSql(
    """
SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Date`, `m`.`Difficulty`, `m`.`Duration`, `m`.`Rating`, `m`.`Time`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE DATEPART('h', `m`.`Time`) = 10
""");
    }

    public override async Task Where_TimeOnly_Minute(bool async)
    {
        await base.Where_TimeOnly_Minute(async);

        AssertSql(
    """
SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Date`, `m`.`Difficulty`, `m`.`Duration`, `m`.`Rating`, `m`.`Time`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE DATEPART('n', `m`.`Time`) = 15
""");
    }

    public override async Task Where_TimeOnly_Second(bool async)
    {
        await base.Where_TimeOnly_Second(async);

        AssertSql(
    """
SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Date`, `m`.`Difficulty`, `m`.`Duration`, `m`.`Rating`, `m`.`Time`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE DATEPART('s', `m`.`Time`) = 50
""");
    }

    public override async Task Where_TimeOnly_Millisecond(bool async)
    {
        await base.Where_TimeOnly_Millisecond(async);

        AssertSql(
"""
SELECT [m].[Id], [m].[CodeName], [m].[Date], [m].[Duration], [m].[Rating], [m].[Time], [m].[Timeline]
FROM [Missions] AS [m]
WHERE DATEPART(millisecond, [m].[Time]) = 500
""");
    }

    public override async Task Where_TimeOnly_AddHours(bool async)
    {
        await base.Where_TimeOnly_AddHours(async);

        AssertSql(
            """
SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Date`, `m`.`Difficulty`, `m`.`Duration`, `m`.`Rating`, `m`.`Time`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE TIMEVALUE(DATEADD('h', CLNG(3.0), `m`.`Time`)) = TIMEVALUE('13:15:50')
""");
    }

    public override async Task Where_TimeOnly_AddMinutes(bool async)
    {
        await base.Where_TimeOnly_AddMinutes(async);

        AssertSql(
    """
SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Date`, `m`.`Difficulty`, `m`.`Duration`, `m`.`Rating`, `m`.`Time`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE TIMEVALUE(DATEADD('n', CLNG(3.0), `m`.`Time`)) = TIMEVALUE('10:18:50')
""");
    }

    public override async Task Where_TimeOnly_Add_TimeSpan(bool async)
    {
        await AssertTranslationFailed(() => base.Where_TimeOnly_Add_TimeSpan(async));

        AssertSql();
    }

    public override async Task Where_TimeOnly_IsBetween(bool async)
    {
        await base.Where_TimeOnly_IsBetween(async);

        AssertSql(
    """
SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Date`, `m`.`Difficulty`, `m`.`Duration`, `m`.`Rating`, `m`.`Time`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE (IIF(`m`.`Time` >= TIMEVALUE('10:00:00'), TRUE, FALSE) BAND IIF(`m`.`Time` < TIMEVALUE('11:00:00'), TRUE, FALSE)) = TRUE
""");
    }

    public override async Task Where_TimeOnly_subtract_TimeOnly(bool async)
    {
        await AssertTranslationFailed(() => base.Where_TimeOnly_subtract_TimeOnly(async));

        AssertSql();
    }

    public override async Task Where_TimeOnly_FromDateTime_compared_to_property(bool async)
    {
        await base.Where_TimeOnly_FromDateTime_compared_to_property(async);

        AssertSql(
            """
SELECT `t`.`Id` AS `TagId`, `m`.`Id` AS `MissionId`
FROM `Tags` AS `t`,
`Missions` AS `m`
WHERE TIMEVALUE(`t`.`IssueDate`) = `m`.`Time`
""");
    }

    public override async Task Where_TimeOnly_FromDateTime_compared_to_parameter(bool async)
    {
        await base.Where_TimeOnly_FromDateTime_compared_to_parameter(async);

        AssertSql(
"""
@__time_0='02:00' (DbType = Time)

SELECT [t].[Id], [t].[GearNickName], [t].[GearSquadId], [t].[IssueDate], [t].[Note]
FROM [Tags] AS [t]
LEFT JOIN (
    SELECT [g].[Nickname], [g].[SquadId]
    FROM [Gears] AS [g]
) AS [s] ON [t].[GearNickName] = [s].[Nickname] AND [t].[GearSquadId] = [s].[SquadId]
WHERE [s].[Nickname] IS NOT NULL AND [s].[SquadId] IS NOT NULL AND CAST(DATEADD(hour, CAST(CAST([s].[SquadId] AS float) AS int), [t].[IssueDate]) AS time) = @__time_0
""");
    }

    public override async Task Where_TimeOnly_FromDateTime_compared_to_constant(bool async)
    {
        await base.Where_TimeOnly_FromDateTime_compared_to_constant(async);

        AssertSql(
            """
SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`IssueDate`, `t`.`Note`
FROM `Tags` AS `t`
WHERE IIF(DATEADD('h', CDBL(IIF(LEN(`t`.`Note`) IS NULL, NULL, CLNG(LEN(`t`.`Note`)))), `t`.`IssueDate`) IS NULL, NULL, TIMEVALUE(DATEADD('h', CDBL(IIF(LEN(`t`.`Note`) IS NULL, NULL, CLNG(LEN(`t`.`Note`)))), `t`.`IssueDate`))) > TIMEVALUE('09:00:00')
""");
    }

    public override async Task Where_TimeOnly_FromTimeSpan_compared_to_property(bool async)
    {
        await base.Where_TimeOnly_FromTimeSpan_compared_to_property(async);

        AssertSql(
            """
SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Date`, `m`.`Difficulty`, `m`.`Duration`, `m`.`Rating`, `m`.`Time`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE TIMEVALUE(`m`.`Duration`) < `m`.`Time`
""");
    }

    public override async Task Where_TimeOnly_FromTimeSpan_compared_to_parameter(bool async)
    {
        await base.Where_TimeOnly_FromTimeSpan_compared_to_parameter(async);

        AssertSql(
            """
@__time_0='01:02:03'

SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Date`, `m`.`Difficulty`, `m`.`Duration`, `m`.`Rating`, `m`.`Time`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE TIMEVALUE(`m`.`Duration`) = TIMEVALUE(@__time_0)
""");
    }

    public override async Task Order_by_TimeOnly_FromTimeSpan(bool async)
    {
        await base.Order_by_TimeOnly_FromTimeSpan(async);

        AssertSql(
"""
SELECT [m].[Id], [m].[CodeName], [m].[Date], [m].[Difficulty], [m].[Duration], [m].[Rating], [m].[Time], [m].[Timeline]
FROM [Missions] AS [m]
ORDER BY CAST([m].[Duration] AS time)
""");
    }

    public override async Task Where_DateOnly_FromDateTime_compared_to_property(bool async)
    {
        await base.Where_DateOnly_FromDateTime_compared_to_property(async);

        AssertSql(
            """
SELECT `t`.`Id` AS `TagId`, `m`.`Id` AS `MissionId`
FROM `Tags` AS `t`,
`Missions` AS `m`
WHERE `t`.`IssueDate` > `m`.`Date`
""");
    }

    public override async Task Where_DateOnly_FromDateTime_compared_to_constant_and_parameter(bool async)
    {
        await base.Where_DateOnly_FromDateTime_compared_to_constant_and_parameter(async);

        AssertSql(
"""
@__prm_0='10/11/0002' (DbType = Date)

SELECT [t].[Id], [t].[GearNickName], [t].[GearSquadId], [t].[IssueDate], [t].[Note]
FROM [Tags] AS [t]
WHERE CAST([t].[IssueDate] AS date) IN (@__prm_0, '0015-03-07')
""");
    }

    public override async Task Project_navigation_defined_on_base_from_entity_with_inheritance_using_soft_cast(bool async)
    {
        await base.Project_navigation_defined_on_base_from_entity_with_inheritance_using_soft_cast(async);

        AssertSql(
"""
SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOfBirthName], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], CASE
    WHEN [o].[Nickname] IS NOT NULL THEN N'Officer'
END AS [Discriminator], [t].[Id], [t].[GearNickName], [t].[GearSquadId], [t].[IssueDate], [t].[Note], CASE
    WHEN [t].[Id] IS NULL THEN CAST(1 AS bit)
    ELSE CAST(0 AS bit)
END AS [IsNull], [c].[Name], [c].[Location], [c].[Nation], CASE
    WHEN [c].[Name] IS NULL THEN CAST(1 AS bit)
    ELSE CAST(0 AS bit)
END AS [IsNull], [s].[Id], [s].[Banner], [s].[Banner5], [s].[InternalNumber], [s].[Name], CASE
    WHEN [s].[Id] IS NULL THEN CAST(1 AS bit)
    ELSE CAST(0 AS bit)
END AS [IsNull]
FROM [Gears] AS [g]
LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
LEFT JOIN [Tags] AS [t] ON [g].[Nickname] = [t].[GearNickName] AND [g].[SquadId] = [t].[GearSquadId]
LEFT JOIN [Cities] AS [c] ON [g].[CityOfBirthName] = [c].[Name]
LEFT JOIN [Squads] AS [s] ON [g].[SquadId] = [s].[Id]
""");
    }

    public override async Task Project_navigation_defined_on_derived_from_entity_with_inheritance_using_soft_cast(bool async)
    {
        await base.Project_navigation_defined_on_derived_from_entity_with_inheritance_using_soft_cast(async);

        AssertSql(
        """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE `g`.`HasSoulPatch` = TRUE AND `g`.`HasSoulPatch` IN (FALSE, TRUE)
""");
    }

    public override async Task Join_entity_with_itself_grouped_by_key_followed_by_include_skip_take(bool async)
    {
        await base.Join_entity_with_itself_grouped_by_key_followed_by_include_skip_take(async);

        AssertSql(
            """
SELECT `s1`.`Nickname`, `s1`.`SquadId`, `s1`.`AssignedCityName`, `s1`.`CityOfBirthName`, `s1`.`FullName`, `s1`.`HasSoulPatch`, `s1`.`LeaderNickname`, `s1`.`LeaderSquadId`, `s1`.`Rank`, `s1`.`Discriminator`, `s1`.`HasSoulPatch0`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM (
    SELECT TOP 10 `s0`.`Nickname`, `s0`.`SquadId`, `s0`.`AssignedCityName`, `s0`.`CityOfBirthName`, `s0`.`FullName`, `s0`.`HasSoulPatch`, `s0`.`LeaderNickname`, `s0`.`LeaderSquadId`, `s0`.`Rank`, `s0`.`Discriminator`, `s0`.`HasSoulPatch0`
    FROM (
        SELECT TOP 10 `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `s`.`HasSoulPatch` AS `HasSoulPatch0`
        FROM (`Gears` AS `g`
        LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
        LEFT JOIN (
            SELECT MIN(IIF(LEN(`g0`.`Nickname`) IS NULL, NULL, CLNG(LEN(`g0`.`Nickname`)))) AS `c`, `g0`.`HasSoulPatch`
            FROM `Gears` AS `g0`
            WHERE `g0`.`Nickname` <> 'Dom'
            GROUP BY `g0`.`HasSoulPatch`
        ) AS `s` ON IIF(LEN(`g`.`Nickname`) IS NULL, NULL, CLNG(LEN(`g`.`Nickname`))) = `s`.`c`
        WHERE `s`.`c` IS NOT NULL
        ORDER BY `g`.`Nickname`
    ) AS `s0`
    ORDER BY `s0`.`Nickname` DESC
) AS `s1`
LEFT JOIN `Weapons` AS `w` ON `s1`.`FullName` = `w`.`OwnerFullName`
ORDER BY `s1`.`Nickname`, `s1`.`SquadId`, NOT (`s1`.`HasSoulPatch0`)
""");
    }

    public override async Task Where_bool_column_and_Contains(bool async)
    {
        await base.Where_bool_column_and_Contains(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE `g`.`HasSoulPatch` = TRUE AND `g`.`HasSoulPatch` IN (FALSE, TRUE)
""");
    }

    public override async Task Where_bool_column_or_Contains(bool async)
    {
        await base.Where_bool_column_or_Contains(async);

        AssertSql(
        """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE `g`.`HasSoulPatch` = TRUE AND `g`.`HasSoulPatch` IN (FALSE, TRUE)
""");
    }

    public override async Task Parameter_used_multiple_times_take_appropriate_inferred_type_mapping(bool async)
    {
        await base.Parameter_used_multiple_times_take_appropriate_inferred_type_mapping(async);

        AssertSql(
            """
@__place_0='Ephyra's location' (Size = 255), @__place_0_1='Ephyra's location' (Size = 100)
@__place_0_1='Ephyra's location' (Size = 100)

SELECT `c`.`Name`, `c`.`Location`, `c`.`Nation`
FROM `Cities` AS `c`
WHERE `c`.`Nation` = @__place_0 OR `c`.`Location` = @__place_0_1 OR `c`.`Location` = @__place_0_1
""");
    }

    public override async Task Enum_matching_take_value_gets_different_type_mapping(bool async)
    {
        await base.Enum_matching_take_value_gets_different_type_mapping(async);

        AssertSql(
            """
@__value_1='1'

SELECT TOP 1 `g`.`Rank` BAND @__value_1
FROM `Gears` AS `g`
ORDER BY `g`.`Nickname`
""");
    }

    public override async Task SelectMany_Where_DefaultIfEmpty_with_navigation_in_the_collection_selector_order_comparison(bool async)
    {
        await base.SelectMany_Where_DefaultIfEmpty_with_navigation_in_the_collection_selector_order_comparison(async);

        AssertSql(
    """
@__prm_0='1'

SELECT `g`.`Nickname`, `g`.`FullName`, IIF(`w0`.`Id` IS NOT NULL, TRUE, FALSE) AS `Collection`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `w`.`Id`, `w`.`OwnerFullName`
    FROM `Weapons` AS `w`
    WHERE `w`.`Id` > @__prm_0
) AS `w0` ON `g`.`FullName` = `w0`.`OwnerFullName`
""");
    }

    public override async Task Project_entity_and_collection_element(bool async)
    {
        await base.Project_entity_and_collection_element(async);

        AssertSql(
"""
SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOfBirthName], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], CASE
    WHEN [o].[Nickname] IS NOT NULL THEN N'Officer'
END AS [Discriminator], [s].[Id], [s].[Banner], [s].[Banner5], [s].[InternalNumber], [s].[Name], [w].[Id], [w].[AmmunitionType], [w].[IsAutomatic], [w].[Name], [w].[OwnerFullName], [w].[SynergyWithId], [t0].[Id], [t0].[AmmunitionType], [t0].[IsAutomatic], [t0].[Name], [t0].[OwnerFullName], [t0].[SynergyWithId]
FROM [Gears] AS [g]
LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
INNER JOIN [Squads] AS [s] ON [g].[SquadId] = [s].[Id]
LEFT JOIN [Weapons] AS [w] ON [g].[FullName] = [w].[OwnerFullName]
LEFT JOIN (
    SELECT [t].[Id], [t].[AmmunitionType], [t].[IsAutomatic], [t].[Name], [t].[OwnerFullName], [t].[SynergyWithId]
    FROM (
        SELECT [w0].[Id], [w0].[AmmunitionType], [w0].[IsAutomatic], [w0].[Name], [w0].[OwnerFullName], [w0].[SynergyWithId], ROW_NUMBER() OVER(PARTITION BY [w0].[OwnerFullName] ORDER BY [w0].[Id]) AS [row]
        FROM [Weapons] AS [w0]
    ) AS [t]
    WHERE [t].[row] <= 1
) AS [t0] ON [g].[FullName] = [t0].[OwnerFullName]
ORDER BY [g].[Nickname], [g].[SquadId], [s].[Id]
""");
    }

    public override async Task DateTimeOffset_DateAdd_AddYears(bool async)
    {
        await base.DateTimeOffset_DateAdd_AddYears(async);

        AssertSql(
            """
SELECT DATEADD('yyyy', 1, `m`.`Timeline`)
FROM `Missions` AS `m`
""");
    }

    public override async Task Correlated_collection_via_SelectMany_with_Distinct_missing_indentifying_columns_in_projection(bool async)
    {
        await base.Correlated_collection_via_SelectMany_with_Distinct_missing_indentifying_columns_in_projection(async);

        AssertSql(
"""
@__isAutomatic_0='True'

SELECT `g`.`Nickname`, `g`.`FullName`, IIF(`t`.`Id` IS NOT NULL, TRUE, FALSE) AS `Collection`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `w`.`Id`, `w`.`OwnerFullName`
    FROM `Weapons` AS `w`
    WHERE `w`.`IsAutomatic` <> @__isAutomatic_0
) AS `t` ON `g`.`FullName` = `t`.`OwnerFullName`
""");
    }

    public override async Task Basic_query_gears(bool async)
    {
        await base.Basic_query_gears(async);

        AssertSql(
            """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
""");
    }

    public override async Task Contains_on_readonly_enumerable(bool async)
    {
        await base.Contains_on_readonly_enumerable(async);

        AssertSql(
        """
SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE `w`.`AmmunitionType` = 1
""");
    }

    public override async Task SelectMany_Where_DefaultIfEmpty_with_navigation_in_the_collection_selector_not_equal(bool async)
    {
        await base.SelectMany_Where_DefaultIfEmpty_with_navigation_in_the_collection_selector_not_equal(async);

        AssertSql(
    """
@__isAutomatic_0='True'

SELECT `g`.`Nickname`, `g`.`FullName`, IIF(`w0`.`Id` IS NOT NULL, TRUE, FALSE) AS `Collection`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `w`.`Id`, `w`.`OwnerFullName`
    FROM `Weapons` AS `w`
    WHERE `w`.`IsAutomatic` <> @__isAutomatic_0
) AS `w0` ON `g`.`FullName` = `w0`.`OwnerFullName`
""");
    }

    public override async Task Trying_to_access_unmapped_property_in_projection(bool async)
    {
        await base.Trying_to_access_unmapped_property_in_projection(async);

        AssertSql(
        """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
""");
    }

    public override async Task Cast_to_derived_type_causes_client_eval(bool async)
    {
        await base.Cast_to_derived_type_causes_client_eval(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
""");
    }

    public override async Task Comparison_with_value_converted_subclass(bool async)
    {
        await base.Comparison_with_value_converted_subclass(async);

        AssertSql(
        """
SELECT `f`.`Id`, `f`.`CapitalName`, `f`.`Name`, `f`.`ServerAddress`, `l`.`CommanderName`, `l`.`Eradicated`, IIF(`l`.`Id` IS NOT NULL, 'LocustHorde', NULL) AS `Discriminator`
FROM `Factions` AS `f`
LEFT JOIN `LocustHordes` AS `l` ON `f`.`Id` = `l`.`Id`
WHERE `f`.`ServerAddress` = '127.0.0.1'
""");
    }

    public override async Task FirstOrDefault_on_empty_collection_of_DateTime_in_subquery(bool async)
    {
        //await base.FirstOrDefault_on_empty_collection_of_DateTime_in_subquery(async);
        await AssertQuery(
            async,
            ss => from g in ss.Set<Gear>()
                let invalidTagIssueDate = (from t in ss.Set<CogTag>()
                    where t.GearNickName == g.FullName
                    orderby t.Id
                    select t.IssueDate).FirstOrDefault()
                where g.Tag.IssueDate > invalidTagIssueDate
                select new { g.Nickname, invalidTagIssueDate },
            ss => from g in ss.Set<Gear>()
                let invalidTagIssueDate = (from t in ss.Set<CogTag>()
                    where t.GearNickName == g.FullName
                    orderby t.Id
                    select t.IssueDate).FirstOrDefault(new DateTime(100, 1, 1))
                where g.Tag.IssueDate > invalidTagIssueDate
                select new { g.Nickname, invalidTagIssueDate });

        AssertSql(
            """
SELECT `g`.`Nickname`, IIF((
        SELECT TOP 1 `t1`.`IssueDate`
        FROM `Tags` AS `t1`
        WHERE `t1`.`GearNickName` = `g`.`FullName`
        ORDER BY `t1`.`Id`) IS NULL, #0100-01-01#, (
        SELECT TOP 1 `t1`.`IssueDate`
        FROM `Tags` AS `t1`
        WHERE `t1`.`GearNickName` = `g`.`FullName`
        ORDER BY `t1`.`Id`)) AS `invalidTagIssueDate`
FROM `Gears` AS `g`
LEFT JOIN `Tags` AS `t` ON `g`.`Nickname` = `t`.`GearNickName` AND `g`.`SquadId` = `t`.`GearSquadId`
WHERE `t`.`IssueDate` > IIF((
        SELECT TOP 1 `t0`.`IssueDate`
        FROM `Tags` AS `t0`
        WHERE `t0`.`GearNickName` = `g`.`FullName`
        ORDER BY `t0`.`Id`) IS NULL, #0100-01-01#, (
        SELECT TOP 1 `t0`.`IssueDate`
        FROM `Tags` AS `t0`
        WHERE `t0`.`GearNickName` = `g`.`FullName`
        ORDER BY `t0`.`Id`))
""");
    }

    public override async Task
        Correlated_collection_with_groupby_not_projecting_identifier_column_with_group_aggregate_in_final_projection_multiple_grouping_keys(
            bool async)
    {
        await base
            .Correlated_collection_with_groupby_not_projecting_identifier_column_with_group_aggregate_in_final_projection_multiple_grouping_keys(
                async);

        AssertSql(
"""
SELECT [g].[Nickname], [g].[SquadId], [t].[IsAutomatic], [t].[Name], [t].[Count]
FROM [Gears] AS [g]
OUTER APPLY (
    SELECT [w].[IsAutomatic], [w].[Name], COUNT(*) AS [Count]
    FROM [Weapons] AS [w]
    WHERE [g].[FullName] = [w].[OwnerFullName]
    GROUP BY [w].[IsAutomatic], [w].[Name]
) AS [t]
ORDER BY [g].[Nickname], [g].[SquadId], [t].[IsAutomatic]
""");
    }

    public override async Task
        Correlated_collection_with_groupby_with_complex_grouping_key_not_projecting_identifier_column_with_group_aggregate_in_final_projection(
            bool async)
    {
        await base
            .Correlated_collection_with_groupby_with_complex_grouping_key_not_projecting_identifier_column_with_group_aggregate_in_final_projection(
                async);

        AssertSql(
        """
SELECT IIF(SUM(`m`.`Rating`) IS NULL, 0.0, SUM(`m`.`Rating`))
FROM `Missions` AS `m`
WHERE `m`.`CodeName` = 'Operation Foobar'
""");
    }

    public override async Task Sum_with_no_data_nullable_double(bool async)
    {
        await base.Sum_with_no_data_nullable_double(async);

        AssertSql(
"""
SELECT IIF(SUM(`m`.`Rating`) IS NULL, 0.0, SUM(`m`.`Rating`))
FROM `Missions` AS `m`
WHERE `m`.`CodeName` = 'Operation Foobar'
""");
    }

    public override async Task ToString_guid_property_projection(bool async)
    {
        await AssertQuery(
            async,
            ss => ss.Set<CogTag>().Select(
                ct => new { A = ct.GearNickName, B = ct.Id.ToString("B") }),
            elementSorter: e => e.B,
            elementAsserter: (e, a) =>
            {
                Assert.Equal(e.A, a.A);
                Assert.Equal(e.B.ToLower(), a.B.ToLower());
            });

        AssertSql(
            """
SELECT `t`.`GearNickName`, `t`.`Id`
FROM `Tags` AS `t`
""");
    }

    public override async Task Correlated_collection_with_distinct_not_projecting_identifier_column(bool async)
    {
        await base.Correlated_collection_with_distinct_not_projecting_identifier_column(async);

        AssertSql(
"""
SELECT [g].[Nickname], [g].[SquadId], [t].[Name], [t].[IsAutomatic]
FROM [Gears] AS [g]
OUTER APPLY (
    SELECT DISTINCT [w].[Name], [w].[IsAutomatic]
    FROM [Weapons] AS [w]
    WHERE [g].[FullName] = [w].[OwnerFullName]
) AS [t]
ORDER BY [g].[Nickname], [g].[SquadId], [t].[Name]
""");
    }

    public override async Task Include_after_Select_throws(bool async)
    {
        await base.Include_after_Select_throws(async);

        AssertSql(
"""
SELECT `f`.`Id`, `f`.`CapitalName`, `f`.`Name`, `f`.`ServerAddress`, `l`.`CommanderName`, `l`.`Eradicated`, IIF(`l`.`Id` IS NOT NULL, 'LocustHorde', NULL) AS `Discriminator`, `c`.`Name`, `c`.`Location`, `c`.`Nation`
FROM (`Factions` AS `f`
LEFT JOIN `LocustHordes` AS `l` ON `f`.`Id` = `l`.`Id`)
LEFT JOIN `Cities` AS `c` ON `f`.`CapitalName` = `c`.`Name`
""");
    }

    public override async Task Cast_to_derived_followed_by_multiple_includes(bool async)
    {
        await base.Cast_to_derived_followed_by_multiple_includes(async);
AssertSql(
    """
SELECT `l`.`Name`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`ThreatLevelByte`, `l`.`ThreatLevelNullableByte`, `l0`.`DefeatedByNickname`, `l0`.`DefeatedBySquadId`, `l0`.`HighCommandId`, IIF(`l0`.`Name` IS NOT NULL, 'LocustCommander', NULL) AS `Discriminator`, `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM ((`LocustLeaders` AS `l`
LEFT JOIN `LocustCommanders` AS `l0` ON `l`.`Name` = `l0`.`Name`)
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
) AS `s` ON `l0`.`DefeatedByNickname` = `s`.`Nickname` AND `l0`.`DefeatedBySquadId` = `s`.`SquadId`)
LEFT JOIN `Weapons` AS `w` ON `s`.`FullName` = `w`.`OwnerFullName`
WHERE `l`.`Name` LIKE '%Queen%'
ORDER BY `l`.`Name`, `s`.`Nickname`, `s`.`SquadId`
""");
    }

    public override async Task Correlated_collection_with_distinct_projecting_identifier_column(bool async)
    {
        await base.Correlated_collection_with_distinct_projecting_identifier_column(async);

        AssertSql(
"""
SELECT [g].[Nickname], [g].[SquadId], [t].[Id], [t].[Name]
FROM [Gears] AS [g]
OUTER APPLY (
    SELECT DISTINCT [w].[Id], [w].[Name]
    FROM [Weapons] AS [w]
    WHERE [g].[FullName] = [w].[OwnerFullName]
) AS [t]
ORDER BY [g].[Nickname], [g].[SquadId]
""");
    }

    public override async Task Where_equals_method_on_nullable_with_object_overload(bool async)
    {
        await base.Where_equals_method_on_nullable_with_object_overload(async);

        AssertSql(
            """
SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Date`, `m`.`Difficulty`, `m`.`Duration`, `m`.`Rating`, `m`.`Time`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE `m`.`Rating` IS NULL
""");
    }

    public override async Task
        Correlated_collection_with_groupby_not_projecting_identifier_column_but_only_grouping_key_in_final_projection(bool async)
    {
        await base.Correlated_collection_with_groupby_not_projecting_identifier_column_but_only_grouping_key_in_final_projection(async);

        AssertSql(
"""
SELECT `f`.`Id`, `f`.`CapitalName`, `f`.`Name`, `f`.`ServerAddress`, `l`.`CommanderName`, `l`.`Eradicated`, IIF(`l`.`Id` IS NOT NULL, 'LocustHorde', NULL) AS `Discriminator`
FROM `Factions` AS `f`
LEFT JOIN `LocustHordes` AS `l` ON `f`.`Id` = `l`.`Id`
WHERE `l`.`Id` IS NOT NULL
""");
    }

    public override async Task Project_derivied_entity_with_convert_to_parent(bool async)
    {
        await base.Project_derivied_entity_with_convert_to_parent(async);
        AssertSql(
"""
SELECT `f`.`Id`, `f`.`CapitalName`, `f`.`Name`, `f`.`ServerAddress`, `l`.`CommanderName`, `l`.`Eradicated`, IIF(`l`.`Id` IS NOT NULL, 'LocustHorde', NULL) AS `Discriminator`
FROM `Factions` AS `f`
LEFT JOIN `LocustHordes` AS `l` ON `f`.`Id` = `l`.`Id`
WHERE `l`.`Id` IS NOT NULL
""");
    }

    public override async Task Include_after_SelectMany_throws(bool async)
    {
        await base.Include_after_SelectMany_throws(async);

        AssertSql(
            """
SELECT `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`, `s0`.`Id`, `s0`.`Banner`, `s0`.`Banner5`, `s0`.`InternalNumber`, `s0`.`Name`
FROM ((`Factions` AS `f`
LEFT JOIN `Cities` AS `c` ON `f`.`CapitalName` = `c`.`Name`)
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
) AS `s` ON `c`.`Name` = `s`.`CityOfBirthName`)
LEFT JOIN `Squads` AS `s0` ON `s`.`SquadId` = `s0`.`Id`
WHERE `c`.`Name` IS NOT NULL AND `s`.`CityOfBirthName` IS NOT NULL AND `s`.`SquadId` IS NOT NULL AND `s0`.`Id` IS NOT NULL
""");
    }

    public override async Task Correlated_collection_with_distinct_projecting_identifier_column_composite_key(bool async)
    {
        await base.Correlated_collection_with_distinct_projecting_identifier_column_composite_key(async);

        AssertSql(
            """
SELECT `s`.`Id`, `s0`.`Nickname`, `s0`.`SquadId`, `s0`.`HasSoulPatch`
FROM `Squads` AS `s`
LEFT JOIN (
    SELECT DISTINCT `g`.`Nickname`, `g`.`SquadId`, `g`.`HasSoulPatch`
    FROM `Gears` AS `g`
) AS `s0` ON `s`.`Id` = `s0`.`SquadId`
ORDER BY `s`.`Id`, `s0`.`Nickname`
""");
    }

    public override async Task Include_on_entity_that_is_not_present_in_final_projection_but_uses_TypeIs_instead(bool async)
    {
        await base.Include_on_entity_that_is_not_present_in_final_projection_but_uses_TypeIs_instead(async);

        AssertSql(
        """
SELECT `g`.`Nickname`, IIF(`o`.`Nickname` IS NOT NULL, TRUE, FALSE) AS `IsOfficer`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
""");
    }

    public override async Task GroupBy_Select_sum(bool async)
    {
        await base.GroupBy_Select_sum(async);

        AssertSql(
            """
SELECT IIF(SUM(`m`.`Rating`) IS NULL, 0.0, SUM(`m`.`Rating`))
FROM `Missions` AS `m`
GROUP BY `m`.`CodeName`
""");
    }

    public override async Task ToString_boolean_property_nullable(bool async)
    {
        await base.ToString_boolean_property_nullable(async);

        AssertSql(
            """
SELECT IIF(`l`.`Eradicated` = FALSE, 'False', IIF(`l`.`Eradicated` = TRUE, 'True', ''))
FROM `Factions` AS `f`
INNER JOIN `LocustHordes` AS `l` ON `f`.`Id` = `l`.`Id`
""");
    }

    public override async Task ToString_boolean_computed_nullable(bool async)
    {
        await base.ToString_boolean_computed_nullable(async);

        AssertSql(
            """
SELECT CASE CASE
    WHEN NOT ([l].[Eradicated] = CAST(1 AS bit) OR ([l].[CommanderName] = N'Unknown' AND [l].[CommanderName] IS NOT NULL)) THEN CAST(0 AS bit)
    WHEN [l].[Eradicated] = CAST(1 AS bit) OR ([l].[CommanderName] = N'Unknown' AND [l].[CommanderName] IS NOT NULL) THEN CAST(1 AS bit)
END
    WHEN CAST(0 AS bit) THEN N'False'
    WHEN CAST(1 AS bit) THEN N'True'
    ELSE N''
END
FROM [LocustHordes] AS [l]
""");
    }

    public override async Task Correlated_collection_after_distinct_3_levels(bool async)
    {
        await base.Correlated_collection_after_distinct_3_levels(async);

        AssertSql(
"""
SELECT [t].[Id], [t].[Name], [t1].[Nickname], [t1].[FullName], [t1].[HasSoulPatch], [t1].[Id], [t1].[Name], [t1].[Nickname0], [t1].[FullName0], [t1].[HasSoulPatch0], [t1].[Id0]
FROM (
    SELECT DISTINCT [s].[Id], [s].[Name]
    FROM [Squads] AS [s]
) AS [t]
OUTER APPLY (
    SELECT [t0].[Nickname], [t0].[FullName], [t0].[HasSoulPatch], [t2].[Id], [t2].[Name], [t2].[Nickname] AS [Nickname0], [t2].[FullName] AS [FullName0], [t2].[HasSoulPatch] AS [HasSoulPatch0], [t2].[Id0]
    FROM (
        SELECT DISTINCT [g].[Nickname], [g].[FullName], [g].[HasSoulPatch]
        FROM [Gears] AS [g]
        WHERE [g].[SquadId] = [t].[Id]
    ) AS [t0]
    OUTER APPLY (
        SELECT [t].[Id], [t].[Name], [t0].[Nickname], [t0].[FullName], [t0].[HasSoulPatch], [w].[Id] AS [Id0]
        FROM [Weapons] AS [w]
        WHERE [w].[OwnerFullName] = [t0].[FullName]
    ) AS [t2]
) AS [t1]
ORDER BY [t].[Id], [t1].[Nickname], [t1].[FullName], [t1].[HasSoulPatch]
""");
    }

    public override async Task ToString_string_property_projection(bool async)
    {
        await base.ToString_string_property_projection(async);

        AssertSql(
            """
SELECT `w`.`Name`
FROM `Weapons` AS `w`
""");
    }

    public override async Task ToString_boolean_property_non_nullable(bool async)
    {
        await base.ToString_boolean_property_non_nullable(async);

        AssertSql(
            """
SELECT IIF(`w`.`IsAutomatic` = FALSE, 'False', 'True')
FROM `Weapons` AS `w`
""");
    }

    public override async Task Include_on_derived_entity_with_cast(bool async)
    {
        await base.Include_on_derived_entity_with_cast(async);

        AssertSql(
            """
SELECT `f`.`Id`, `f`.`CapitalName`, `f`.`Name`, `f`.`ServerAddress`, `l`.`CommanderName`, `l`.`Eradicated`, IIF(`l`.`Id` IS NOT NULL, 'LocustHorde', NULL) AS `Discriminator`, `c`.`Name`, `c`.`Location`, `c`.`Nation`
FROM (`Factions` AS `f`
LEFT JOIN `LocustHordes` AS `l` ON `f`.`Id` = `l`.`Id`)
LEFT JOIN `Cities` AS `c` ON `f`.`CapitalName` = `c`.`Name`
WHERE `l`.`Id` IS NOT NULL
ORDER BY `f`.`Id`
""");
    }

    public override async Task String_concat_nullable_expressions_are_coalesced(bool async)
    {
        await base.String_concat_nullable_expressions_are_coalesced(async);
        AssertSql(
"""
SELECT ((`g`.`FullName` & '') & IIF(`g`.`LeaderNickname` IS NULL, '', `g`.`LeaderNickname`)) & ''
FROM `Gears` AS `g`
""");
    }

    public override async Task Correlated_collection_with_distinct_projecting_identifier_column_and_correlation_key(bool async)
    {
        await base.Correlated_collection_with_distinct_projecting_identifier_column_and_correlation_key(async);

        AssertSql(
            """
SELECT `g`.`Nickname`, `g`.`SquadId`, `w0`.`Id`, `w0`.`Name`, `w0`.`OwnerFullName`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT DISTINCT `w`.`Id`, `w`.`Name`, `w`.`OwnerFullName`
    FROM `Weapons` AS `w`
) AS `w0` ON `g`.`FullName` = `w0`.`OwnerFullName`
ORDER BY `g`.`Nickname`, `g`.`SquadId`
""");
    }

    public override async Task Correlated_collection_with_groupby_not_projecting_identifier_column_with_group_aggregate_in_final_projection(
        bool async)
    {
        await base.Correlated_collection_with_groupby_not_projecting_identifier_column_with_group_aggregate_in_final_projection(async);

        AssertSql(
"""
SELECT [g].[Nickname], [g].[SquadId], [t].[Key], [t].[Count]
FROM [Gears] AS [g]
OUTER APPLY (
    SELECT [w].[IsAutomatic] AS [Key], COUNT(*) AS [Count]
    FROM [Weapons] AS [w]
    WHERE [g].[FullName] = [w].[OwnerFullName]
    GROUP BY [w].[IsAutomatic]
) AS [t]
ORDER BY [g].[Nickname], [g].[SquadId]
""");
    }

    public override async Task Project_discriminator_columns(bool async)
    {
        await base.Project_discriminator_columns(async);

        AssertSql();
    }

    public override async Task Correlated_collection_with_distinct_not_projecting_identifier_column_also_projecting_complex_expressions(
        bool async)
    {
        await base.Correlated_collection_with_distinct_not_projecting_identifier_column_also_projecting_complex_expressions(async);

        AssertSql();
    }

    public override async Task Client_eval_followed_by_aggregate_operation(bool async)
    {
        await base.Client_eval_followed_by_aggregate_operation(async);

        AssertSql();
    }

    public override async Task Client_member_and_unsupported_string_Equals_in_the_same_query(bool async)
    {
        await base.Client_member_and_unsupported_string_Equals_in_the_same_query(async);

        AssertSql();
    }

    public override async Task Client_side_equality_with_parameter_works_with_optional_navigations(bool async)
    {
        await base.Client_side_equality_with_parameter_works_with_optional_navigations(async);

        AssertSql();
    }

    public override async Task Correlated_collection_order_by_constant_null_of_non_mapped_type(bool async)
    {
        await base.Correlated_collection_order_by_constant_null_of_non_mapped_type(async);

        AssertSql();
    }

    public override async Task GetValueOrDefault_on_DateTimeOffset(bool async)
    {
        await base.GetValueOrDefault_on_DateTimeOffset(async);

        AssertSql();
    }

    public override async Task Where_coalesce_with_anonymous_types(bool async)
    {
        await base.Where_coalesce_with_anonymous_types(async);

        AssertSql();
    }

    public override async Task Projecting_correlated_collection_followed_by_Distinct(bool async)
    {
        await base.Projecting_correlated_collection_followed_by_Distinct(async);

        AssertSql();
    }

    public override async Task Projecting_some_properties_as_well_as_correlated_collection_followed_by_Distinct(bool async)
    {
        await base.Projecting_some_properties_as_well_as_correlated_collection_followed_by_Distinct(async);

        AssertSql();
    }

    public override async Task Projecting_entity_as_well_as_correlated_collection_followed_by_Distinct(bool async)
    {
        await base.Projecting_entity_as_well_as_correlated_collection_followed_by_Distinct(async);

        AssertSql();
    }

    public override async Task Projecting_entity_as_well_as_complex_correlated_collection_followed_by_Distinct(bool async)
    {
        await base.Projecting_entity_as_well_as_complex_correlated_collection_followed_by_Distinct(async);

        AssertSql();
    }

    public override async Task Projecting_entity_as_well_as_correlated_collection_of_scalars_followed_by_Distinct(bool async)
    {
        await base.Projecting_entity_as_well_as_correlated_collection_of_scalars_followed_by_Distinct(async);

        AssertSql();
    }

    public override async Task Correlated_collection_with_distinct_3_levels(bool async)
    {
        await base.Correlated_collection_with_distinct_3_levels(async);

        AssertSql();
    }

    public override async Task Correlated_collection_after_distinct_3_levels_without_original_identifiers(bool async)
    {
        await base.Correlated_collection_after_distinct_3_levels_without_original_identifiers(async);

        AssertSql();
    }

    public override async Task Checked_context_throws_on_client_evaluation(bool async)
    {
        await base.Checked_context_throws_on_client_evaluation(async);

        AssertSql();
    }

    public override async Task Trying_to_access_unmapped_property_throws_informative_error(bool async)
    {
        await base.Trying_to_access_unmapped_property_throws_informative_error(async);

        AssertSql();
    }

    public override async Task Trying_to_access_unmapped_property_inside_aggregate(bool async)
    {
        await base.Trying_to_access_unmapped_property_inside_aggregate(async);

        AssertSql();
    }

    public override async Task Trying_to_access_unmapped_property_inside_subquery(bool async)
    {
        await base.Trying_to_access_unmapped_property_inside_subquery(async);

        AssertSql();
    }

    public override async Task Trying_to_access_unmapped_property_inside_join_key_selector(bool async)
    {
        await base.Trying_to_access_unmapped_property_inside_join_key_selector(async);

        AssertSql();
    }

    public override async Task Client_projection_with_nested_unmapped_property_bubbles_up_translation_failure_info(bool async)
    {
        await base.Client_projection_with_nested_unmapped_property_bubbles_up_translation_failure_info(async);

        AssertSql();
    }

    public override async Task Include_after_select_with_cast_throws(bool async)
    {
        await base.Include_after_select_with_cast_throws(async);

        AssertSql();
    }

    public override async Task Include_after_select_with_entity_projection_throws(bool async)
    {
        await base.Include_after_select_with_entity_projection_throws(async);

        AssertSql();
    }

    public override async Task Include_after_select_anonymous_projection_throws(bool async)
    {
        await base.Include_after_select_anonymous_projection_throws(async);

        AssertSql();
    }

    public override async Task Group_by_with_aggregate_max_on_entity_type(bool async)
    {
        await base.Group_by_with_aggregate_max_on_entity_type(async);

        AssertSql();
    }

    public override async Task Include_collection_and_invalid_navigation_using_string_throws(bool async)
    {
        await base.Include_collection_and_invalid_navigation_using_string_throws(async);

        AssertSql();
    }

    public override async Task Include_with_concat(bool async)
    {
        await base.Include_with_concat(async);

        AssertSql();
    }

    public override async Task Join_with_complex_key_selector(bool async)
    {
        await base.Join_with_complex_key_selector(async);

        AssertSql(
"""
SELECT `s`.`Id`, `t0`.`Id` AS `TagId`
FROM `Squads` AS `s`,
(
    SELECT `t`.`Id`
    FROM `Tags` AS `t`
    WHERE `t`.`Note` = 'Marcus'' Tag'
) AS `t0`
""");
    }

    public override async Task Streaming_correlated_collection_issue_11403_returning_ordered_enumerable_throws(bool async)
    {
        await base.Streaming_correlated_collection_issue_11403_returning_ordered_enumerable_throws(async);

        AssertSql();
    }

    public override async Task Select_correlated_filtered_collection_returning_queryable_throws(bool async)
    {
        await base.Select_correlated_filtered_collection_returning_queryable_throws(async);

        AssertSql();
    }

    public override async Task Client_method_on_collection_navigation_in_predicate(bool async)
    {
        await base.Client_method_on_collection_navigation_in_predicate(async);

        AssertSql();
    }

    public override async Task Client_method_on_collection_navigation_in_predicate_accessed_by_ef_property(bool async)
    {
        await base.Client_method_on_collection_navigation_in_predicate_accessed_by_ef_property(async);

        AssertSql();
    }

    public override async Task Client_method_on_collection_navigation_in_order_by(bool async)
    {
        await base.Client_method_on_collection_navigation_in_order_by(async);

        AssertSql();
    }

    public override async Task Client_method_on_collection_navigation_in_additional_from_clause(bool async)
    {
        await base.Client_method_on_collection_navigation_in_additional_from_clause(async);

        AssertSql();
    }

    public override async Task Include_multiple_one_to_one_and_one_to_many_self_reference(bool async)
    {
        await base.Include_multiple_one_to_one_and_one_to_many_self_reference(async);

        AssertSql();
    }

    public override async Task Include_multiple_one_to_one_and_one_to_one_and_one_to_many(bool async)
    {
        await base.Include_multiple_one_to_one_and_one_to_one_and_one_to_many(async);

        AssertSql();
    }

    public override async Task Include_multiple_include_then_include(bool async)
    {
        await base.Include_multiple_include_then_include(async);

        AssertSql();
    }

    public override async Task Select_Where_Navigation_Client(bool async)
    {
        await base.Select_Where_Navigation_Client(async);

        AssertSql();
    }

    public override async Task Where_subquery_equality_to_null_with_composite_key(bool async)
    {
        await base.Where_subquery_equality_to_null_with_composite_key(async);

        AssertSql(
            """
SELECT `s`.`Id`, `s`.`Banner`, `s`.`Banner5`, `s`.`InternalNumber`, `s`.`Name`
FROM `Squads` AS `s`
WHERE NOT EXISTS (
    SELECT 1
    FROM `Gears` AS `g`
    WHERE `s`.`Id` = `g`.`SquadId`)
""");
    }

    public override async Task Where_subquery_equality_to_null_with_composite_key_should_match_nulls(bool async)
    {
        await base.Where_subquery_equality_to_null_with_composite_key_should_match_nulls(async);

        AssertSql(
    """
SELECT `s`.`Id`, `s`.`Banner`, `s`.`Banner5`, `s`.`InternalNumber`, `s`.`Name`
FROM `Squads` AS `s`
WHERE NOT EXISTS (
    SELECT 1
    FROM `Gears` AS `g`
    WHERE `s`.`Id` = `g`.`SquadId` AND `g`.`FullName` = 'Anthony Carmine')
""");
    }

    public override async Task Where_subquery_equality_to_null_without_composite_key(bool async)
    {
        await base.Where_subquery_equality_to_null_without_composite_key(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE NOT EXISTS (
    SELECT 1
    FROM `Weapons` AS `w`
    WHERE `g`.`FullName` = `w`.`OwnerFullName`)
""");
    }

    public override async Task Where_subquery_equality_to_null_without_composite_key_should_match_null(bool async)
    {
        await base.Where_subquery_equality_to_null_without_composite_key_should_match_null(async);

        AssertSql(
            """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE NOT EXISTS (
    SELECT 1
    FROM `Weapons` AS `w`
    WHERE `g`.`FullName` = `w`.`OwnerFullName` AND `w`.`Name` = 'Hammer of Dawn')
""");
    }

    public override async Task Include_reference_on_derived_type_using_EF_Property(bool async)
    {
        await base.Include_reference_on_derived_type_using_EF_Property(async);

        AssertSql(
            """
SELECT `l`.`Name`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`ThreatLevelByte`, `l`.`ThreatLevelNullableByte`, `l0`.`DefeatedByNickname`, `l0`.`DefeatedBySquadId`, `l0`.`HighCommandId`, IIF(`l0`.`Name` IS NOT NULL, 'LocustCommander', NULL) AS `Discriminator`, `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`
FROM (`LocustLeaders` AS `l`
LEFT JOIN `LocustCommanders` AS `l0` ON `l`.`Name` = `l0`.`Name`)
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g`
    LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
) AS `s` ON `l0`.`DefeatedByNickname` = `s`.`Nickname` AND `l0`.`DefeatedBySquadId` = `s`.`SquadId`
""");
    }

    public override async Task Include_collection_on_derived_type_using_EF_Property(bool async)
    {
        await base.Include_collection_on_derived_type_using_EF_Property(async);

        AssertSql(
            """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`
FROM (`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g0`
    LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`
) AS `s` ON `g`.`Nickname` = `s`.`LeaderNickname` AND `g`.`SquadId` = `s`.`LeaderSquadId`
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `s`.`Nickname`
""");
    }

    public override async Task EF_Property_based_Include_navigation_on_derived_type(bool async)
    {
        await base.EF_Property_based_Include_navigation_on_derived_type(async);

        AssertSql(
            """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`
FROM (`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g0`
    LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`
) AS `s` ON `g`.`Nickname` = `s`.`LeaderNickname` AND `g`.`SquadId` = `s`.`LeaderSquadId`
WHERE `o`.`Nickname` IS NOT NULL
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `s`.`Nickname`
""");
    }

    public override async Task ElementAt_basic_with_OrderBy(bool async)
    {
        await base.ElementAt_basic_with_OrderBy(async);

        AssertSql(
            """
SELECT `s0`.`Nickname`, `s0`.`SquadId`, `s0`.`AssignedCityName`, `s0`.`CityOfBirthName`, `s0`.`FullName`, `s0`.`HasSoulPatch`, `s0`.`LeaderNickname`, `s0`.`LeaderSquadId`, `s0`.`Rank`, `s0`.`Discriminator`
FROM (
    SELECT TOP 1 `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`
    FROM (
        SELECT TOP 1 `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
        FROM `Gears` AS `g`
        LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
        ORDER BY `g`.`FullName`
    ) AS `s`
    ORDER BY `s`.`FullName` DESC
) AS `s0`
ORDER BY `s0`.`FullName`
""");
    }

    public override async Task ElementAtOrDefault_basic_with_OrderBy(bool async)
    {
        await base.ElementAtOrDefault_basic_with_OrderBy(async);

        AssertSql(
            """
SELECT `s0`.`Nickname`, `s0`.`SquadId`, `s0`.`AssignedCityName`, `s0`.`CityOfBirthName`, `s0`.`FullName`, `s0`.`HasSoulPatch`, `s0`.`LeaderNickname`, `s0`.`LeaderSquadId`, `s0`.`Rank`, `s0`.`Discriminator`
FROM (
    SELECT TOP 1 `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`
    FROM (
        SELECT TOP 2 `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
        FROM `Gears` AS `g`
        LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
        ORDER BY `g`.`FullName`
    ) AS `s`
    ORDER BY `s`.`FullName` DESC
) AS `s0`
ORDER BY `s0`.`FullName`
""");
    }

    public override async Task ElementAtOrDefault_basic_with_OrderBy_parameter(bool async)
    {
        await base.ElementAtOrDefault_basic_with_OrderBy_parameter(async);

        AssertSql(
            """
SELECT `s0`.`Nickname`, `s0`.`SquadId`, `s0`.`AssignedCityName`, `s0`.`CityOfBirthName`, `s0`.`FullName`, `s0`.`HasSoulPatch`, `s0`.`LeaderNickname`, `s0`.`LeaderSquadId`, `s0`.`Rank`, `s0`.`Discriminator`
FROM (
    SELECT TOP 1 `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`
    FROM (
        SELECT TOP 3 `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
        FROM `Gears` AS `g`
        LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
        ORDER BY `g`.`FullName`
    ) AS `s`
    ORDER BY `s`.`FullName` DESC
) AS `s0`
ORDER BY `s0`.`FullName`
""");
    }

    public override async Task Where_subquery_with_ElementAtOrDefault_equality_to_null_with_composite_key(bool async)
    {
        await base.Where_subquery_with_ElementAtOrDefault_equality_to_null_with_composite_key(async);

        AssertSql(
"""
SELECT [s].[Id], [s].[Banner], [s].[Banner5], [s].[InternalNumber], [s].[Name]
FROM [Squads] AS [s]
WHERE NOT EXISTS (
    SELECT 1
    FROM [Gears] AS [g]
    LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
    WHERE [s].[Id] = [g].[SquadId]
    ORDER BY [g].[Nickname]
    OFFSET 2 ROWS)
""");
    }

    public override async Task Where_subquery_with_ElementAt_using_column_as_index(bool async)
    {
        await base.Where_subquery_with_ElementAt_using_column_as_index(async);

        AssertSql(
"""
SELECT [s].[Id], [s].[Banner], [s].[Banner5], [s].[InternalNumber], [s].[Name]
FROM [Squads] AS [s]
WHERE (
    SELECT [g].[Nickname]
    FROM [Gears] AS [g]
    LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
    WHERE [s].[Id] = [g].[SquadId]
    ORDER BY [g].[Nickname]
    OFFSET [s].[Id] ROWS FETCH NEXT 1 ROWS ONLY) = N'Cole Train'
""");
    }

    public override async Task Using_indexer_on_byte_array_and_string_in_projection(bool async)
    {
        await base.Using_indexer_on_byte_array_and_string_in_projection(async);

        AssertSql(
            """
SELECT `s`.`Id`, ASCB(MIDB(`s`.`Banner`, 0 + 1, 1)), `s`.`Name`
FROM `Squads` AS `s`
""");
    }

    public override async Task DateTimeOffset_to_unix_time_milliseconds(bool async)
    {
        await base.DateTimeOffset_to_unix_time_milliseconds(async);

        AssertSql(
"""
@__unixEpochMilliseconds_0='0'

SELECT [g].[Nickname], [g].[SquadId], [g].[AssignedCityName], [g].[CityOfBirthName], [g].[FullName], [g].[HasSoulPatch], [g].[LeaderNickname], [g].[LeaderSquadId], [g].[Rank], CASE
    WHEN [o].[Nickname] IS NOT NULL THEN N'Officer'
END AS [Discriminator], [s].[Id], [s].[Banner], [s].[Banner5], [s].[InternalNumber], [s].[Name], [s1].[SquadId], [s1].[MissionId]
FROM [Gears] AS [g]
LEFT JOIN [Officers] AS [o] ON [g].[Nickname] = [o].[Nickname] AND [g].[SquadId] = [o].[SquadId]
INNER JOIN [Squads] AS [s] ON [g].[SquadId] = [s].[Id]
LEFT JOIN [SquadMissions] AS [s1] ON [s].[Id] = [s1].[SquadId]
WHERE NOT EXISTS (
    SELECT 1
    FROM [SquadMissions] AS [s0]
    INNER JOIN [Missions] AS [m] ON [s0].[MissionId] = [m].[Id]
    WHERE [s].[Id] = [s0].[SquadId] AND @__unixEpochMilliseconds_0 = DATEDIFF_BIG(millisecond, '1970-01-01T00:00:00.0000000+00:00', [m].[Timeline]))
ORDER BY [g].[Nickname], [g].[SquadId], [s].[Id], [s1].[SquadId]
""");
    }

    public override async Task DateTimeOffset_to_unix_time_seconds(bool async)
    {
        await base.DateTimeOffset_to_unix_time_seconds(async);

        AssertSql(
            $"""
@__unixEpochSeconds_0='0' (DbType = Decimal)

SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `s`.`Id`, `s`.`Banner`, `s`.`Banner5`, `s`.`InternalNumber`, `s`.`Name`, `s1`.`SquadId`, `s1`.`MissionId`
FROM ((`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
INNER JOIN `Squads` AS `s` ON `g`.`SquadId` = `s`.`Id`)
LEFT JOIN `SquadMissions` AS `s1` ON `s`.`Id` = `s1`.`SquadId`
WHERE NOT EXISTS (
    SELECT `s0`.`SquadId`
    FROM `SquadMissions` AS `s0`
    INNER JOIN `Missions` AS `m` ON `s0`.`MissionId` = `m`.`Id`
    WHERE `s`.`Id` = `s0`.`SquadId` AND {AssertSqlHelper.Parameter("@__unixEpochSeconds_0")} = DATEDIFF('s', CDATE('1970-01-01 00:00:00'), `m`.`Timeline`))
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `s`.`Id`, `s1`.`SquadId`
""");
    }

    public override async Task Set_operator_with_navigation_in_projection_groupby_aggregate(bool async)
    {
        await base.Set_operator_with_navigation_in_projection_groupby_aggregate(async);

        AssertSql(
            """
SELECT `s`.`Name`, (
    SELECT IIF(SUM(IIF(LEN(`c`.`Location`) IS NULL, NULL, CLNG(LEN(`c`.`Location`)))) IS NULL, 0, SUM(IIF(LEN(`c`.`Location`) IS NULL, NULL, CLNG(LEN(`c`.`Location`)))))
    FROM (`Gears` AS `g2`
    INNER JOIN `Squads` AS `s0` ON `g2`.`SquadId` = `s0`.`Id`)
    INNER JOIN `Cities` AS `c` ON `g2`.`CityOfBirthName` = `c`.`Name`
    WHERE 'Marcus' IN (
        SELECT `u0`.`Nickname`
        FROM (
            SELECT `g3`.`Nickname`
            FROM `Gears` AS `g3`
            UNION ALL
            SELECT `g4`.`Nickname`
            FROM `Gears` AS `g4`
        ) AS `u0`
    ) AND (`s`.`Name` = `s0`.`Name` OR (`s`.`Name` IS NULL AND `s0`.`Name` IS NULL))) AS `SumOfLengths`
FROM `Gears` AS `g`
INNER JOIN `Squads` AS `s` ON `g`.`SquadId` = `s`.`Id`
WHERE 'Marcus' IN (
    SELECT `u`.`Nickname`
    FROM (
        SELECT `g0`.`Nickname`
        FROM `Gears` AS `g0`
        UNION ALL
        SELECT `g1`.`Nickname`
        FROM `Gears` AS `g1`
    ) AS `u`
)
GROUP BY `s`.`Name`
""");
    }

    public override async Task Nav_expansion_inside_Contains_argument(bool async)
    {
        await base.Nav_expansion_inside_Contains_argument(async);

        AssertSql(
"""
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE IIF(EXISTS (
        SELECT 1
        FROM `Weapons` AS `w`
        WHERE `g`.`FullName` = `w`.`OwnerFullName`), 1, 0) IN (1, -1)
""");
    }

    public override async Task Nav_expansion_with_member_pushdown_inside_Contains_argument(bool async)
    {
        await base.Nav_expansion_with_member_pushdown_inside_Contains_argument(async);

        AssertSql(
 """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE (
    SELECT TOP 1 `w`.`Name`
    FROM `Weapons` AS `w`
    WHERE `g`.`FullName` = `w`.`OwnerFullName`
    ORDER BY `w`.`Id`) IN ('Marcus'' Lancer', 'Dom''s Gnasher')
""");
    }

    public override async Task Subquery_inside_Take_argument(bool async)
    {
        await base.Subquery_inside_Take_argument(async);

        AssertSql(
            """
@__numbers_0='[0,1,2]' (Size = 4000)

SELECT [g].[Nickname], [g].[SquadId], [t0].[Id], [t0].[AmmunitionType], [t0].[IsAutomatic], [t0].[Name], [t0].[OwnerFullName], [t0].[SynergyWithId]
FROM [Gears] AS [g]
LEFT JOIN (
    SELECT [t].[Id], [t].[AmmunitionType], [t].[IsAutomatic], [t].[Name], [t].[OwnerFullName], [t].[SynergyWithId]
    FROM (
        SELECT [w].[Id], [w].[AmmunitionType], [w].[IsAutomatic], [w].[Name], [w].[OwnerFullName], [w].[SynergyWithId], ROW_NUMBER() OVER(PARTITION BY [w].[OwnerFullName] ORDER BY [w].[Id]) AS [row]
        FROM [Weapons] AS [w]
    ) AS [t]
    WHERE [t].[row] <= COALESCE((
        SELECT [n].[value]
        FROM OPENJSON(@__numbers_0) WITH ([value] int '$') AS [n]
        ORDER BY [n].[value]
        OFFSET 1 ROWS FETCH NEXT 1 ROWS ONLY), 0)
) AS [t0] ON [g].[FullName] = [t0].[OwnerFullName]
ORDER BY [g].[Nickname], [g].[SquadId], [t0].[OwnerFullName], [t0].[Id]
""");
    }

    public override async Task Nav_expansion_inside_Skip_correlated_to_source(bool async)
    {
        await base.Nav_expansion_inside_Skip_correlated_to_source(async);

        AssertSql();
    }

    public override async Task Nav_expansion_inside_Take_correlated_to_source(bool async)
    {
        await base.Nav_expansion_inside_Take_correlated_to_source(async);

        AssertSql();
    }

    public override async Task Nav_expansion_with_member_pushdown_inside_Take_correlated_to_source(bool async)
    {
        await base.Nav_expansion_with_member_pushdown_inside_Take_correlated_to_source(async);

        AssertSql();
    }

    public override async Task Nav_expansion_inside_ElementAt_correlated_to_source(bool async)
    {
        await base.Nav_expansion_inside_ElementAt_correlated_to_source(async);

        AssertSql();
    }

    public override async Task Include_one_to_many_on_composite_key_then_orderby_key_properties(bool async)
    {
        await base.Include_one_to_many_on_composite_key_then_orderby_key_properties(async);

        AssertSql(
            """
SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM (`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN `Weapons` AS `w` ON `g`.`FullName` = `w`.`OwnerFullName`
ORDER BY `g`.`SquadId`, `g`.`Nickname`
""");
    }

    public override async Task Find_underlying_property_after_GroupJoin_DefaultIfEmpty(bool async)
    {
        await base.Find_underlying_property_after_GroupJoin_DefaultIfEmpty(async);

        AssertSql(
            """
SELECT `g`.`FullName`, IIF(`s`.`ThreatLevel` IS NULL, NULL, CLNG(`s`.`ThreatLevel`)) AS `ThreatLevel`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `l`.`ThreatLevel`, `l0`.`DefeatedByNickname`
    FROM `LocustLeaders` AS `l`
    LEFT JOIN `LocustCommanders` AS `l0` ON `l`.`Name` = `l0`.`Name`
    WHERE `l0`.`Name` IS NOT NULL
) AS `s` ON `g`.`Nickname` = `s`.`DefeatedByNickname`
""");
    }

    public override async Task Join_include_coalesce_simple(bool async)
    {
        await base.Join_include_coalesce_simple(async);

        AssertSql(
            """
SELECT `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`, `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`, IIF(`g`.`Nickname` = 'Marcus', TRUE, FALSE)
FROM ((`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g0`
    LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`
) AS `s` ON `g`.`LeaderNickname` = `s`.`Nickname`)
LEFT JOIN `Weapons` AS `w` ON `g`.`FullName` = `w`.`OwnerFullName`
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `s`.`Nickname`, `s`.`SquadId`
""",
            //
            """
SELECT `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`, `g`.`Nickname`, `g`.`SquadId`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM ((`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g0`
    LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`
) AS `s` ON `g`.`LeaderNickname` = `s`.`Nickname`)
LEFT JOIN `Weapons` AS `w` ON `s`.`FullName` = `w`.`OwnerFullName`
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `s`.`Nickname`, `s`.`SquadId`
""",
            //
            """
SELECT `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`, `g`.`Nickname`, `g`.`SquadId`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
FROM (((`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g0`
    LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`
) AS `s` ON `g`.`LeaderNickname` = `s`.`Nickname`)
LEFT JOIN `Weapons` AS `w` ON `s`.`FullName` = `w`.`OwnerFullName`)
LEFT JOIN `Weapons` AS `w0` ON `g`.`FullName` = `w0`.`OwnerFullName`
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `s`.`Nickname`, `s`.`SquadId`, `w`.`Id`
""");
    }

    public override async Task Join_include_coalesce_nested(bool async)
    {
        await base.Join_include_coalesce_nested(async);

        AssertSql(
            """
SELECT `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`, `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`, IIF(`g`.`Nickname` = 'Marcus', TRUE, FALSE)
FROM ((`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g0`
    LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`
) AS `s` ON `g`.`LeaderNickname` = `s`.`Nickname`)
LEFT JOIN `Weapons` AS `w` ON `g`.`FullName` = `w`.`OwnerFullName`
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `s`.`Nickname`, `s`.`SquadId`
""",
            //
            """
SELECT `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`, `g`.`Nickname`, `g`.`SquadId`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`, `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `w1`.`Id`, `w1`.`AmmunitionType`, `w1`.`IsAutomatic`, `w1`.`Name`, `w1`.`OwnerFullName`, `w1`.`SynergyWithId`
FROM ((((`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g0`
    LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`
) AS `s` ON `g`.`LeaderNickname` = `s`.`Nickname`)
LEFT JOIN `Weapons` AS `w` ON `s`.`FullName` = `w`.`OwnerFullName`)
LEFT JOIN `Weapons` AS `w0` ON `s`.`FullName` = `w0`.`OwnerFullName`)
LEFT JOIN `Weapons` AS `w1` ON `s`.`FullName` = `w1`.`OwnerFullName`
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `s`.`Nickname`, `s`.`SquadId`, `w`.`Id`, `w0`.`Id`
""");
    }

    public override async Task Join_include_conditional(bool async)
    {
        await base.Join_include_conditional(async);

        AssertSql(
            """
SELECT IIF(`s`.`Nickname` IS NOT NULL AND `s`.`SquadId` IS NOT NULL, TRUE, FALSE), `s`.`Nickname`, `s`.`SquadId`, `s`.`AssignedCityName`, `s`.`CityOfBirthName`, `s`.`FullName`, `s`.`HasSoulPatch`, `s`.`LeaderNickname`, `s`.`LeaderSquadId`, `s`.`Rank`, `s`.`Discriminator`, `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`, IIF(`g`.`Nickname` = 'Marcus', TRUE, FALSE)
FROM ((`Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, IIF(`o0`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
    FROM `Gears` AS `g0`
    LEFT JOIN `Officers` AS `o0` ON `g0`.`Nickname` = `o0`.`Nickname` AND `g0`.`SquadId` = `o0`.`SquadId`
) AS `s` ON `g`.`LeaderNickname` = `s`.`Nickname`)
LEFT JOIN `Weapons` AS `w` ON `g`.`FullName` = `w`.`OwnerFullName`
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `s`.`Nickname`, `s`.`SquadId`
""");
    }

    public override async Task Derived_reference_is_skipped_when_base_type(bool async)
    {
        await base.Derived_reference_is_skipped_when_base_type(async);

        AssertSql(
            """
SELECT `l`.`Name`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`ThreatLevelByte`, `l`.`ThreatLevelNullableByte`, `l0`.`DefeatedByNickname`, `l0`.`DefeatedBySquadId`, `l0`.`HighCommandId`, IIF(`l0`.`Name` IS NOT NULL, 'LocustCommander', NULL) AS `Discriminator`, `l1`.`Id`, `l1`.`IsOperational`, `l1`.`Name`
FROM (`LocustLeaders` AS `l`
LEFT JOIN `LocustCommanders` AS `l0` ON `l`.`Name` = `l0`.`Name`)
LEFT JOIN `LocustHighCommands` AS `l1` ON `l0`.`HighCommandId` = `l1`.`Id`
""");
    }

    public override async Task Nested_contains_with_enum(bool async)
    {
        await base.Nested_contains_with_enum(async);

        AssertSql(
            """
@__key_1='5f221fb9-66f4-442a-92c9-d97ed5989cc7'
@__key_1='5f221fb9-66f4-442a-92c9-d97ed5989cc7'

SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, IIF(`o`.`Nickname` IS NOT NULL, 'Officer', NULL) AS `Discriminator`
FROM `Gears` AS `g`
LEFT JOIN `Officers` AS `o` ON `g`.`Nickname` = `o`.`Nickname` AND `g`.`SquadId` = `o`.`SquadId`
WHERE IIF(`g`.`Rank` = 1, @__key_1, @__key_1) IN ('{0a47bcb7-a1cb-4345-8944-c58f82d6aac7}', '{5f221fb9-66f4-442a-92c9-d97ed5989cc7}')
""",
            //
            """
@__key_1='5f221fb9-66f4-442a-92c9-d97ed5989cc7'
@__key_1='5f221fb9-66f4-442a-92c9-d97ed5989cc7'

SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE IIF(`w`.`AmmunitionType` = 1, @__key_1, @__key_1) IN ('{0a47bcb7-a1cb-4345-8944-c58f82d6aac7}', '{5f221fb9-66f4-442a-92c9-d97ed5989cc7}')
""");
    }

    private void AssertSql(params string[] expected)
        => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);
}
