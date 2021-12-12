// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore.Query;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query
{
    public class GearsOfWarQueryJetTest : GearsOfWarQueryTestBase<GearsOfWarQueryJetFixture>
    {
        private static readonly string _eol = Environment.NewLine;

        // ReSharper disable once UnusedParameter.Local
#pragma warning disable IDE0060 // Remove unused parameter
        public GearsOfWarQueryJetTest(GearsOfWarQueryJetFixture fixture, ITestOutputHelper testOutputHelper)
#pragma warning restore IDE0060 // Remove unused parameter
            : base(fixture)
        {
            Fixture.TestSqlLoggerFactory.Clear();
            //Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
        }

        public override async Task Entity_equality_empty(bool isAsync)
        {
            await base.Entity_equality_empty(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE False = True");
        }

        public override async Task Include_multiple_one_to_one_and_one_to_many(bool isAsync)
        {
            await base.Include_multiple_one_to_one_and_one_to_many(isAsync);

            AssertSql(
                $@"SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`Note`, `t0`.`Nickname`, `t0`.`SquadId`, `t0`.`AssignedCityName`, `t0`.`CityOfBirthName`, `t0`.`Discriminator`, `t0`.`FullName`, `t0`.`HasSoulPatch`, `t0`.`LeaderNickname`, `t0`.`LeaderSquadId`, `t0`.`Rank`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
LEFT JOIN `Weapons` AS `w` ON `t0`.`FullName` = `w`.`OwnerFullName`
ORDER BY `t`.`Id`, `w`.`Id`");
        }

        public override async Task Include_multiple_one_to_one_optional_and_one_to_one_required(bool isAsync)
        {
            await base.Include_multiple_one_to_one_optional_and_one_to_one_required(isAsync);

            AssertSql(
                $@"SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`Note`, `t0`.`Nickname`, `t0`.`SquadId`, `t0`.`AssignedCityName`, `t0`.`CityOfBirthName`, `t0`.`Discriminator`, `t0`.`FullName`, `t0`.`HasSoulPatch`, `t0`.`LeaderNickname`, `t0`.`LeaderSquadId`, `t0`.`Rank`, `s`.`Id`, `s`.`InternalNumber`, `s`.`Name`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
LEFT JOIN `Squads` AS `s` ON `t0`.`SquadId` = `s`.`Id`");
        }

        public override async Task Include_multiple_circular(bool isAsync)
        {
            await base.Include_multiple_circular(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, `c`.`Name`, `c`.`Location`, `c`.`Nation`, `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`Discriminator`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`
FROM `Gears` AS `g`
INNER JOIN `Cities` AS `c` ON `g`.`CityOfBirthName` = `c`.`Name`
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON `c`.`Name` = `t`.`AssignedCityName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `c`.`Name`, `t`.`Nickname`, `t`.`SquadId`");
        }

        public override async Task Include_multiple_circular_with_filter(bool isAsync)
        {
            await base.Include_multiple_circular_with_filter(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, `c`.`Name`, `c`.`Location`, `c`.`Nation`, `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`Discriminator`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`
FROM `Gears` AS `g`
INNER JOIN `Cities` AS `c` ON `g`.`CityOfBirthName` = `c`.`Name`
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON `c`.`Name` = `t`.`AssignedCityName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Nickname` = 'Marcus')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `c`.`Name`, `t`.`Nickname`, `t`.`SquadId`");
        }

        public override async Task Include_using_alternate_key(bool isAsync)
        {
            await base.Include_using_alternate_key(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Gears` AS `g`
LEFT JOIN `Weapons` AS `w` ON `g`.`FullName` = `w`.`OwnerFullName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Nickname` = 'Marcus')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `w`.`Id`");
        }

        public override async Task Include_navigation_on_derived_type(bool isAsync)
        {
            await base.Include_navigation_on_derived_type(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`Discriminator`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON (`g`.`Nickname` = `t`.`LeaderNickname`) AND (`g`.`SquadId` = `t`.`LeaderSquadId`)
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Discriminator` = 'Officer')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `t`.`Nickname`, `t`.`SquadId`");
        }

        public override async Task String_based_Include_navigation_on_derived_type(bool isAsync)
        {
            await base.String_based_Include_navigation_on_derived_type(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`Discriminator`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON (`g`.`Nickname` = `t`.`LeaderNickname`) AND (`g`.`SquadId` = `t`.`LeaderSquadId`)
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Discriminator` = 'Officer')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `t`.`Nickname`, `t`.`SquadId`");
        }

        public override async Task Select_Where_Navigation_Included(bool isAsync)
        {
            await base.Select_Where_Navigation_Included(isAsync);

            AssertSql(
                $@"SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`Note`, `t0`.`Nickname`, `t0`.`SquadId`, `t0`.`AssignedCityName`, `t0`.`CityOfBirthName`, `t0`.`Discriminator`, `t0`.`FullName`, `t0`.`HasSoulPatch`, `t0`.`LeaderNickname`, `t0`.`LeaderSquadId`, `t0`.`Rank`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
WHERE `t0`.`Nickname` = 'Marcus'");
        }

        public override async Task Include_with_join_reference1(bool isAsync)
        {
            await base.Include_with_join_reference1(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, `c`.`Name`, `c`.`Location`, `c`.`Nation`
FROM `Gears` AS `g`
INNER JOIN `Tags` AS `t` ON (`g`.`SquadId` = `t`.`GearSquadId`) AND (`g`.`Nickname` = `t`.`GearNickName`)
INNER JOIN `Cities` AS `c` ON `g`.`CityOfBirthName` = `c`.`Name`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')");
        }

        public override async Task Include_with_join_reference2(bool isAsync)
        {
            await base.Include_with_join_reference2(isAsync);

            AssertSql(
                $@"SELECT `t0`.`Nickname`, `t0`.`SquadId`, `t0`.`AssignedCityName`, `t0`.`CityOfBirthName`, `t0`.`Discriminator`, `t0`.`FullName`, `t0`.`HasSoulPatch`, `t0`.`LeaderNickname`, `t0`.`LeaderSquadId`, `t0`.`Rank`, `c`.`Name`, `c`.`Location`, `c`.`Nation`
FROM `Tags` AS `t`
INNER JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearSquadId` = `t0`.`SquadId`) AND (`t`.`GearNickName` = `t0`.`Nickname`)
INNER JOIN `Cities` AS `c` ON `t0`.`CityOfBirthName` = `c`.`Name`");
        }

        public override async Task Include_with_join_collection1(bool isAsync)
        {
            await base.Include_with_join_collection1(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, `t`.`Id`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Gears` AS `g`
INNER JOIN `Tags` AS `t` ON (`g`.`SquadId` = `t`.`GearSquadId`) AND (`g`.`Nickname` = `t`.`GearNickName`)
LEFT JOIN `Weapons` AS `w` ON `g`.`FullName` = `w`.`OwnerFullName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `t`.`Id`, `w`.`Id`");
        }

        public override async Task Include_with_join_collection2(bool isAsync)
        {
            await base.Include_with_join_collection2(isAsync);

            AssertSql(
                $@"SELECT `t0`.`Nickname`, `t0`.`SquadId`, `t0`.`AssignedCityName`, `t0`.`CityOfBirthName`, `t0`.`Discriminator`, `t0`.`FullName`, `t0`.`HasSoulPatch`, `t0`.`LeaderNickname`, `t0`.`LeaderSquadId`, `t0`.`Rank`, `t`.`Id`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Tags` AS `t`
INNER JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearSquadId` = `t0`.`SquadId`) AND (`t`.`GearNickName` = `t0`.`Nickname`)
LEFT JOIN `Weapons` AS `w` ON `t0`.`FullName` = `w`.`OwnerFullName`
ORDER BY `t`.`Id`, `t0`.`Nickname`, `t0`.`SquadId`, `w`.`Id`");
        }

        public override async Task Include_where_list_contains_navigation(bool isAsync)
        {
            await base.Include_where_list_contains_navigation(isAsync);

            AssertSql(
                $@"SELECT `t`.`Id`
FROM `Tags` AS `t`",
                //
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`Note`
FROM `Gears` AS `g`
LEFT JOIN `Tags` AS `t` ON (`g`.`Nickname` = `t`.`GearNickName`) AND (`g`.`SquadId` = `t`.`GearSquadId`)
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`t`.`Id` IS NOT NULL AND `t`.`Id` IN ('34c8d86e-a4ac-4be5-827f-584dda348a07', 'df36f493-463f-4123-83f9-6b135deeb7ba', 'a8ad98f9-e023-4e2a-9a70-c2728455bd34', '70534e05-782c-4052-8720-c2c54481ce5f', 'a7be028a-0cf2-448f-ab55-ce8bc5d8cf69', 'b39a6fba-9026-4d69-828e-fd7068673e57'))");
        }

        public override async Task Include_where_list_contains_navigation2(bool isAsync)
        {
            await base.Include_where_list_contains_navigation2(isAsync);

            AssertSql(
                $@"SELECT `t`.`Id`
FROM `Tags` AS `t`",
                //
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`Note`
FROM `Gears` AS `g`
INNER JOIN `Cities` AS `c` ON `g`.`CityOfBirthName` = `c`.`Name`
LEFT JOIN `Tags` AS `t` ON (`g`.`Nickname` = `t`.`GearNickName`) AND (`g`.`SquadId` = `t`.`GearSquadId`)
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`c`.`Location` IS NOT NULL AND `t`.`Id` IN ('34c8d86e-a4ac-4be5-827f-584dda348a07', 'df36f493-463f-4123-83f9-6b135deeb7ba', 'a8ad98f9-e023-4e2a-9a70-c2728455bd34', '70534e05-782c-4052-8720-c2c54481ce5f', 'a7be028a-0cf2-448f-ab55-ce8bc5d8cf69', 'b39a6fba-9026-4d69-828e-fd7068673e57'))");
        }

        public override async Task Navigation_accessed_twice_outside_and_inside_subquery(bool isAsync)
        {
            await base.Navigation_accessed_twice_outside_and_inside_subquery(isAsync);

            AssertSql(
                $@"SELECT `t`.`Id`
FROM `Tags` AS `t`",
                //
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
LEFT JOIN `Tags` AS `t` ON (`g`.`Nickname` = `t`.`GearNickName`) AND (`g`.`SquadId` = `t`.`GearSquadId`)
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`t`.`Id` IS NOT NULL AND `t`.`Id` IN ('34c8d86e-a4ac-4be5-827f-584dda348a07', 'df36f493-463f-4123-83f9-6b135deeb7ba', 'a8ad98f9-e023-4e2a-9a70-c2728455bd34', '70534e05-782c-4052-8720-c2c54481ce5f', 'a7be028a-0cf2-448f-ab55-ce8bc5d8cf69', 'b39a6fba-9026-4d69-828e-fd7068673e57'))");
        }

        public override async Task Include_with_join_multi_level(bool isAsync)
        {
            await base.Include_with_join_multi_level(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, `c`.`Name`, `c`.`Location`, `c`.`Nation`, `t`.`Id`, `t0`.`Nickname`, `t0`.`SquadId`, `t0`.`AssignedCityName`, `t0`.`CityOfBirthName`, `t0`.`Discriminator`, `t0`.`FullName`, `t0`.`HasSoulPatch`, `t0`.`LeaderNickname`, `t0`.`LeaderSquadId`, `t0`.`Rank`
FROM `Gears` AS `g`
INNER JOIN `Tags` AS `t` ON (`g`.`SquadId` = `t`.`GearSquadId`) AND (`g`.`Nickname` = `t`.`GearNickName`)
INNER JOIN `Cities` AS `c` ON `g`.`CityOfBirthName` = `c`.`Name`
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON `c`.`Name` = `t0`.`AssignedCityName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `t`.`Id`, `c`.`Name`, `t0`.`Nickname`, `t0`.`SquadId`");
        }

        public override async Task Include_with_join_and_inheritance1(bool isAsync)
        {
            await base.Include_with_join_and_inheritance1(isAsync);

            AssertSql(
                $@"SELECT `t0`.`Nickname`, `t0`.`SquadId`, `t0`.`AssignedCityName`, `t0`.`CityOfBirthName`, `t0`.`Discriminator`, `t0`.`FullName`, `t0`.`HasSoulPatch`, `t0`.`LeaderNickname`, `t0`.`LeaderSquadId`, `t0`.`Rank`, `c`.`Name`, `c`.`Location`, `c`.`Nation`
FROM `Tags` AS `t`
INNER JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Discriminator` = 'Officer')
) AS `t0` ON (`t`.`GearSquadId` = `t0`.`SquadId`) AND (`t`.`GearNickName` = `t0`.`Nickname`)
INNER JOIN `Cities` AS `c` ON `t0`.`CityOfBirthName` = `c`.`Name`");
        }

        public override async Task Include_with_join_and_inheritance_with_orderby_before_and_after_include(bool isAsync)
        {
            await base.Include_with_join_and_inheritance_with_orderby_before_and_after_include(isAsync);

            AssertSql(
                $@"SELECT `t0`.`Nickname`, `t0`.`SquadId`, `t0`.`AssignedCityName`, `t0`.`CityOfBirthName`, `t0`.`Discriminator`, `t0`.`FullName`, `t0`.`HasSoulPatch`, `t0`.`LeaderNickname`, `t0`.`LeaderSquadId`, `t0`.`Rank`, `t`.`Id`, `t1`.`Nickname`, `t1`.`SquadId`, `t1`.`AssignedCityName`, `t1`.`CityOfBirthName`, `t1`.`Discriminator`, `t1`.`FullName`, `t1`.`HasSoulPatch`, `t1`.`LeaderNickname`, `t1`.`LeaderSquadId`, `t1`.`Rank`
FROM `Tags` AS `t`
INNER JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Discriminator` = 'Officer')
) AS `t0` ON (`t`.`GearSquadId` = `t0`.`SquadId`) AND (`t`.`GearNickName` = `t0`.`Nickname`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t1` ON (`t0`.`Nickname` = `t1`.`LeaderNickname`) AND (`t0`.`SquadId` = `t1`.`LeaderSquadId`)
ORDER BY `t0`.`HasSoulPatch`, `t0`.`Nickname` DESC, `t`.`Id`, `t0`.`SquadId`, `t1`.`Nickname`, `t1`.`SquadId`");
        }

        public override async Task Include_with_join_and_inheritance2(bool isAsync)
        {
            await base.Include_with_join_and_inheritance2(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, `t`.`Id`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Gears` AS `g`
INNER JOIN `Tags` AS `t` ON (`g`.`SquadId` = `t`.`GearSquadId`) AND (`g`.`Nickname` = `t`.`GearNickName`)
LEFT JOIN `Weapons` AS `w` ON `g`.`FullName` = `w`.`OwnerFullName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Discriminator` = 'Officer')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `t`.`Id`, `w`.`Id`");
        }

        public override async Task Include_with_join_and_inheritance3(bool isAsync)
        {
            await base.Include_with_join_and_inheritance3(isAsync);

            AssertSql(
                $@"SELECT `t0`.`Nickname`, `t0`.`SquadId`, `t0`.`AssignedCityName`, `t0`.`CityOfBirthName`, `t0`.`Discriminator`, `t0`.`FullName`, `t0`.`HasSoulPatch`, `t0`.`LeaderNickname`, `t0`.`LeaderSquadId`, `t0`.`Rank`, `t`.`Id`, `t1`.`Nickname`, `t1`.`SquadId`, `t1`.`AssignedCityName`, `t1`.`CityOfBirthName`, `t1`.`Discriminator`, `t1`.`FullName`, `t1`.`HasSoulPatch`, `t1`.`LeaderNickname`, `t1`.`LeaderSquadId`, `t1`.`Rank`
FROM `Tags` AS `t`
INNER JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Discriminator` = 'Officer')
) AS `t0` ON (`t`.`GearSquadId` = `t0`.`SquadId`) AND (`t`.`GearNickName` = `t0`.`Nickname`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t1` ON (`t0`.`Nickname` = `t1`.`LeaderNickname`) AND (`t0`.`SquadId` = `t1`.`LeaderSquadId`)
ORDER BY `t`.`Id`, `t0`.`Nickname`, `t0`.`SquadId`, `t1`.`Nickname`, `t1`.`SquadId`");
        }

        public override async Task Include_with_nested_navigation_in_order_by(bool isAsync)
        {
            await base.Include_with_nested_navigation_in_order_by(isAsync);

            AssertSql(
                $@"SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`, `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`Discriminator`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`
FROM `Weapons` AS `w`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON `w`.`OwnerFullName` = `t`.`FullName`
LEFT JOIN `Cities` AS `c` ON `t`.`CityOfBirthName` = `c`.`Name`
WHERE (`t`.`Nickname` <> 'Paduk') OR `t`.`Nickname` IS NULL
ORDER BY `c`.`Name`, `w`.`Id`");
        }

        public override async Task Where_enum(bool isAsync)
        {
            await base.Where_enum(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Rank` = 2)");
        }

        public override async Task Where_nullable_enum_with_constant(bool isAsync)
        {
            await base.Where_nullable_enum_with_constant(isAsync);

            AssertSql(
                $@"SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE `w`.`AmmunitionType` = 1");
        }

        public override async Task Where_nullable_enum_with_null_constant(bool isAsync)
        {
            await base.Where_nullable_enum_with_null_constant(isAsync);

            AssertSql(
                $@"SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE `w`.`AmmunitionType` IS NULL");
        }

        public override async Task Where_nullable_enum_with_non_nullable_parameter(bool isAsync)
        {
            await base.Where_nullable_enum_with_non_nullable_parameter(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__ammunitionType_0='1'")}

SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE `w`.`AmmunitionType` = {AssertSqlHelper.Parameter("@__ammunitionType_0")}");
        }

        public override async Task Where_nullable_enum_with_nullable_parameter(bool isAsync)
        {
            await base.Where_nullable_enum_with_nullable_parameter(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__ammunitionType_0='1' (Nullable = true)")}

SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE `w`.`AmmunitionType` = {AssertSqlHelper.Parameter("@__ammunitionType_0")}",
                //
                $@"SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE `w`.`AmmunitionType` IS NULL");
        }

        public override async Task Where_bitwise_and_enum(bool isAsync)
        {
            await base.Where_bitwise_and_enum(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((`g`.`Rank` BAND 1) > 0)",
                //
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((`g`.`Rank` BAND 1) = 1)");
        }

        public override async Task Where_bitwise_and_integral(bool isAsync)
        {
            await base.Where_bitwise_and_integral(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((`g`.`Rank` BAND 1) = 1)",
                //
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((CAST(`g`.`Rank` AS bigint) BAND 1) = 1)",
                //
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((CAST(`g`.`Rank` AS smallint) BAND 1) = 1)");
        }

        public override async Task Where_bitwise_and_nullable_enum_with_constant(bool isAsync)
        {
            await base.Where_bitwise_and_nullable_enum_with_constant(isAsync);

            AssertSql(
                $@"SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE (`w`.`AmmunitionType` BAND 1) > 0");
        }

        public override async Task Where_bitwise_and_nullable_enum_with_null_constant(bool isAsync)
        {
            await base.Where_bitwise_and_nullable_enum_with_null_constant(isAsync);

            AssertSql(
                $@"SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE (`w`.`AmmunitionType` BAND NULL) > 0");
        }

        public override async Task Where_bitwise_and_nullable_enum_with_non_nullable_parameter(bool isAsync)
        {
            await base.Where_bitwise_and_nullable_enum_with_non_nullable_parameter(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__ammunitionType_0='1'")}

SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE (`w`.`AmmunitionType` BAND {AssertSqlHelper.Parameter("@__ammunitionType_0")}) > 0");
        }

        public override async Task Where_bitwise_and_nullable_enum_with_nullable_parameter(bool isAsync)
        {
            await base.Where_bitwise_and_nullable_enum_with_nullable_parameter(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__ammunitionType_0='1' (Nullable = true)")}

SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE (`w`.`AmmunitionType` BAND {AssertSqlHelper.Parameter("@__ammunitionType_0")}) > 0",
                //
                $@"@__ammunitionType_0=NULL (DbType = Int32)

SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE (`w`.`AmmunitionType` BAND {AssertSqlHelper.Parameter("@__ammunitionType_0")}) > 0");
        }

        public override async Task Where_bitwise_or_enum(bool isAsync)
        {
            await base.Where_bitwise_or_enum(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((`g`.`Rank` BOR 1) > 0)");
        }

        public override async Task Bitwise_projects_values_in_select(bool isAsync)
        {
            await base.Bitwise_projects_values_in_select(isAsync);

            AssertSql(
                $@"SELECT TOP 1 IIF((`g`.`Rank` BAND 1) = 1, 1, 0) AS `BitwiseTrue`, IIF((`g`.`Rank` BAND 1) = 2, 1, 0) AS `BitwiseFalse`, `g`.`Rank` BAND 1 AS `BitwiseValue`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((`g`.`Rank` BAND 1) = 1)");
        }

        public override async Task Where_enum_has_flag(bool isAsync)
        {
            await base.Where_enum_has_flag(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((`g`.`Rank` BAND 1) = 1)",
                //
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((`g`.`Rank` BAND 9) = 9)",
                //
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((`g`.`Rank` BAND 1) = 1)",
                //
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((`g`.`Rank` BAND 1) = 1)",
                //
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((1 BAND `g`.`Rank`) = `g`.`Rank`)");
        }

        public override async Task Where_enum_has_flag_subquery(bool isAsync)
        {
            await base.Where_enum_has_flag_subquery(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (((`g`.`Rank` BAND (
    SELECT TOP 1 `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
    ORDER BY `g0`.`Nickname`, `g0`.`SquadId`)) = (
    SELECT TOP 1 `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
    ORDER BY `g0`.`Nickname`, `g0`.`SquadId`)) OR (`g`.`Rank` BAND (
    SELECT TOP 1 `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
    ORDER BY `g0`.`Nickname`, `g0`.`SquadId`) IS NULL AND (
    SELECT TOP 1 `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
    ORDER BY `g0`.`Nickname`, `g0`.`SquadId`) IS NULL))",
                //
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (((1 BAND (
    SELECT TOP 1 `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
    ORDER BY `g0`.`Nickname`, `g0`.`SquadId`)) = (
    SELECT TOP 1 `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
    ORDER BY `g0`.`Nickname`, `g0`.`SquadId`)) OR (1 BAND (
    SELECT TOP 1 `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
    ORDER BY `g0`.`Nickname`, `g0`.`SquadId`) IS NULL AND (
    SELECT TOP 1 `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
    ORDER BY `g0`.`Nickname`, `g0`.`SquadId`) IS NULL))");
        }

        public override async Task Where_enum_has_flag_subquery_with_pushdown(bool isAsync)
        {
            await base.Where_enum_has_flag_subquery_with_pushdown(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (((`g`.`Rank` BAND (
    SELECT TOP 1 `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
    ORDER BY `g0`.`Nickname`, `g0`.`SquadId`)) = (
    SELECT TOP 1 `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
    ORDER BY `g0`.`Nickname`, `g0`.`SquadId`)) OR (`g`.`Rank` BAND (
    SELECT TOP 1 `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
    ORDER BY `g0`.`Nickname`, `g0`.`SquadId`) IS NULL AND (
    SELECT TOP 1 `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
    ORDER BY `g0`.`Nickname`, `g0`.`SquadId`) IS NULL))",
                //
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (((1 BAND (
    SELECT TOP 1 `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
    ORDER BY `g0`.`Nickname`, `g0`.`SquadId`)) = (
    SELECT TOP 1 `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
    ORDER BY `g0`.`Nickname`, `g0`.`SquadId`)) OR (1 BAND (
    SELECT TOP 1 `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
    ORDER BY `g0`.`Nickname`, `g0`.`SquadId`) IS NULL AND (
    SELECT TOP 1 `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
    ORDER BY `g0`.`Nickname`, `g0`.`SquadId`) IS NULL))");
        }

        public override async Task Where_enum_has_flag_subquery_client_eval(bool isAsync)
        {
            await base.Where_enum_has_flag_subquery_client_eval(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (((`g`.`Rank` BAND (
    SELECT TOP 1 `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
    ORDER BY `g0`.`Nickname`, `g0`.`SquadId`)) = (
    SELECT TOP 1 `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
    ORDER BY `g0`.`Nickname`, `g0`.`SquadId`)) OR (`g`.`Rank` BAND (
    SELECT TOP 1 `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
    ORDER BY `g0`.`Nickname`, `g0`.`SquadId`) IS NULL AND (
    SELECT TOP 1 `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
    ORDER BY `g0`.`Nickname`, `g0`.`SquadId`) IS NULL))");
        }

        public override async Task Where_enum_has_flag_with_non_nullable_parameter(bool isAsync)
        {
            await base.Where_enum_has_flag_with_non_nullable_parameter(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__parameter_0='1'")}

SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((`g`.`Rank` BAND {AssertSqlHelper.Parameter("@__parameter_0")}) = {AssertSqlHelper.Parameter("@__parameter_0")})");
        }

        public override async Task Where_has_flag_with_nullable_parameter(bool isAsync)
        {
            await base.Where_has_flag_with_nullable_parameter(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__parameter_0='1' (Nullable = true)")}

SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((`g`.`Rank` BAND {AssertSqlHelper.Parameter("@__parameter_0")}) = {AssertSqlHelper.Parameter("@__parameter_0")})");
        }

        public override async Task Select_enum_has_flag(bool isAsync)
        {
            await base.Select_enum_has_flag(isAsync);

            AssertSql(
                $@"SELECT TOP 1 IIF((`g`.`Rank` BAND 1) = 1, 1, 0) AS `hasFlagTrue`, IIF((`g`.`Rank` BAND 2) = 2, 1, 0) AS `hasFlagFalse`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((`g`.`Rank` BAND 1) = 1)");
        }

        public override async Task Where_count_subquery_without_collision(bool isAsync)
        {
            await base.Where_count_subquery_without_collision(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((
    SELECT COUNT(*)
    FROM `Weapons` AS `w`
    WHERE `g`.`FullName` = `w`.`OwnerFullName`) = 2)");
        }

        public override async Task Where_any_subquery_without_collision(bool isAsync)
        {
            await base.Where_any_subquery_without_collision(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND EXISTS (
    SELECT 1
    FROM `Weapons` AS `w`
    WHERE `g`.`FullName` = `w`.`OwnerFullName`)");
        }

        public override async Task Select_inverted_boolean(bool isAsync)
        {
            await base.Select_inverted_boolean(isAsync);

            AssertSql(
                $@"SELECT `w`.`Id`, IIF(`w`.`IsAutomatic` <> True, 1, 0) AS `Manual`
FROM `Weapons` AS `w`
WHERE `w`.`IsAutomatic` = True");
        }

        public override async Task Select_comparison_with_null(bool isAsync)
        {
            await base.Select_comparison_with_null(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__ammunitionType_0='1' (Nullable = true)")}

SELECT `w`.`Id`, IIF((`w`.`AmmunitionType` = {AssertSqlHelper.Parameter("@__ammunitionType_0")}) AND `w`.`AmmunitionType` IS NOT NULL, 1, 0) AS `Cartridge`
FROM `Weapons` AS `w`
WHERE `w`.`AmmunitionType` = {AssertSqlHelper.Parameter("@__ammunitionType_0")}",
                //
                $@"SELECT `w`.`Id`, IIF(`w`.`AmmunitionType` IS NULL, 1, 0) AS `Cartridge`
FROM `Weapons` AS `w`
WHERE `w`.`AmmunitionType` IS NULL");
        }

        public override async Task Select_null_parameter(bool isAsync)
        {
            await base.Select_null_parameter(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__ammunitionType_0='1' (Nullable = true)")}

SELECT `w`.`Id`, {AssertSqlHelper.Parameter("@__ammunitionType_0")} AS `AmmoType`
FROM `Weapons` AS `w`",
                //
                $@"@__ammunitionType_0=NULL (DbType = Int32)

SELECT `w`.`Id`, {AssertSqlHelper.Parameter("@__ammunitionType_0")} AS `AmmoType`
FROM `Weapons` AS `w`",
                //
                $@"{AssertSqlHelper.Declaration("@__ammunitionType_0='2' (Nullable = true)")}

SELECT `w`.`Id`, {AssertSqlHelper.Parameter("@__ammunitionType_0")} AS `AmmoType`
FROM `Weapons` AS `w`",
                //
                $@"@__ammunitionType_0=NULL (DbType = Int32)

SELECT `w`.`Id`, {AssertSqlHelper.Parameter("@__ammunitionType_0")} AS `AmmoType`
FROM `Weapons` AS `w`");
        }

        public override async Task Select_ternary_operation_with_boolean(bool isAsync)
        {
            await base.Select_ternary_operation_with_boolean(isAsync);

            AssertSql(
                $@"SELECT `w`.`Id`, CASE
    WHEN `w`.`IsAutomatic` = True THEN 1
    ELSE 0
END AS `Num`
FROM `Weapons` AS `w`");
        }

        public override async Task Select_ternary_operation_with_inverted_boolean(bool isAsync)
        {
            await base.Select_ternary_operation_with_inverted_boolean(isAsync);

            AssertSql(
                $@"SELECT `w`.`Id`, CASE
    WHEN `w`.`IsAutomatic` <> True THEN 1
    ELSE 0
END AS `Num`
FROM `Weapons` AS `w`");
        }

        public override async Task Select_ternary_operation_with_has_value_not_null(bool isAsync)
        {
            await base.Select_ternary_operation_with_has_value_not_null(isAsync);

            AssertSql(
                $@"SELECT `w`.`Id`, CASE
    WHEN `w`.`AmmunitionType` IS NOT NULL AND (`w`.`AmmunitionType` = 1) THEN 'Yes'
    ELSE 'No'
END AS `IsCartridge`
FROM `Weapons` AS `w`
WHERE `w`.`AmmunitionType` IS NOT NULL AND (`w`.`AmmunitionType` = 1)");
        }

        public override async Task Select_ternary_operation_multiple_conditions(bool isAsync)
        {
            await base.Select_ternary_operation_multiple_conditions(isAsync);

            AssertSql(
                $@"SELECT `w`.`Id`, CASE
    WHEN (`w`.`AmmunitionType` = 2) AND (`w`.`SynergyWithId` = 1) THEN 'Yes'
    ELSE 'No'
END AS `IsCartridge`
FROM `Weapons` AS `w`");
        }

        public override async Task Select_ternary_operation_multiple_conditions_2(bool isAsync)
        {
            await base.Select_ternary_operation_multiple_conditions_2(isAsync);

            AssertSql(
                $@"SELECT `w`.`Id`, CASE
    WHEN (`w`.`IsAutomatic` <> True) AND (`w`.`SynergyWithId` = 1) THEN 'Yes'
    ELSE 'No'
END AS `IsCartridge`
FROM `Weapons` AS `w`");
        }

        public override async Task Select_multiple_conditions(bool isAsync)
        {
            await base.Select_multiple_conditions(isAsync);

            AssertSql(
                $@"SELECT `w`.`Id`, IIF((`w`.`IsAutomatic` <> True) AND ((`w`.`SynergyWithId` = 1) AND `w`.`SynergyWithId` IS NOT NULL), 1, 0) AS `IsCartridge`
FROM `Weapons` AS `w`");
        }

        public override async Task Select_nested_ternary_operations(bool isAsync)
        {
            await base.Select_nested_ternary_operations(isAsync);

            AssertSql(
                $@"SELECT `w`.`Id`, CASE
    WHEN `w`.`IsAutomatic` <> True THEN CASE
        WHEN `w`.`AmmunitionType` = 1 THEN 'ManualCartridge'
        ELSE 'Manual'
    END
    ELSE 'Auto'
END AS `IsManualCartridge`
FROM `Weapons` AS `w`");
        }

        public override async Task Null_propagation_optimization1(bool isAsync)
        {
            await base.Null_propagation_optimization1(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (IIF((`g`.`LeaderNickname` = 'Marcus') AND `g`.`LeaderNickname` IS NOT NULL, 1, 0) = True)");
        }

        public override async Task Null_propagation_optimization2(bool isAsync)
        {
            await base.Null_propagation_optimization2(isAsync);

            // issue #16050
            //            AssertSql(
            //                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
            //FROM `Gears` AS `g`
            //WHERE `g`.`Discriminator` IN ('Officer', 'Gear') AND `g`.`LeaderNickname` LIKE '%us'");
        }

        public override async Task Null_propagation_optimization3(bool isAsync)
        {
            await base.Null_propagation_optimization3(isAsync);

            // issue #16050
            //            AssertSql(
            //                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
            //FROM `Gears` AS `g`
            //WHERE `g`.`Discriminator` IN ('Officer', 'Gear') AND `g`.`LeaderNickname` LIKE '%us'");
        }

        public override async Task Null_propagation_optimization4(bool isAsync)
        {
            await base.Null_propagation_optimization4(isAsync);

            // issue #16050
            //            AssertSql(
            //                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
            //FROM `Gears` AS `g`
            //WHERE `g`.`Discriminator` IN ('Officer', 'Gear') AND (CAST(LEN(`g`.`LeaderNickname`) AS int) = 5)");
        }

        public override async Task Null_propagation_optimization5(bool isAsync)
        {
            await base.Null_propagation_optimization5(isAsync);

            // issue #16050
            //            AssertSql(
            //                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
            //FROM `Gears` AS `g`
            //WHERE `g`.`Discriminator` IN ('Officer', 'Gear') AND (CAST(LEN(`g`.`LeaderNickname`) AS int) = 5)");
        }

        public override async Task Null_propagation_optimization6(bool isAsync)
        {
            await base.Null_propagation_optimization6(isAsync);

            // issue #16050
            //            AssertSql(
            //                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
            //FROM `Gears` AS `g`
            //WHERE `g`.`Discriminator` IN ('Officer', 'Gear') AND (CAST(LEN(`g`.`LeaderNickname`) AS int) = 5)");
        }

        public override async Task Select_null_propagation_optimization7(bool isAsync)
        {
            await base.Select_null_propagation_optimization7(isAsync);

            // issue #16050
            //            AssertSql(
            //                $@"SELECT `g`.`LeaderNickname` + `g`.`LeaderNickname`
            //FROM `Gears` AS `g`
            //WHERE `g`.`Discriminator` IN ('Officer', 'Gear')");
        }

        public override async Task Select_null_propagation_optimization8(bool isAsync)
        {
            await base.Select_null_propagation_optimization8(isAsync);

            AssertSql(
                $@"SELECT `g`.`LeaderNickname` + `g`.`LeaderNickname`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Officer', 'Gear')");
        }

        public override async Task Select_null_propagation_optimization9(bool isAsync)
        {
            await base.Select_null_propagation_optimization9(isAsync);

            AssertSql(
                $@"SELECT CAST(LEN(`g`.`FullName`) AS int)
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')");
        }

        public override async Task Select_null_propagation_negative1(bool isAsync)
        {
            await base.Select_null_propagation_negative1(isAsync);

            AssertSql(
                $@"SELECT CASE
    WHEN `g`.`LeaderNickname` IS NOT NULL THEN IIF((CAST(LEN(`g`.`Nickname`) AS int) = 5) AND LEN(`g`.`Nickname`) IS NOT NULL, 1, 0)
    ELSE NULL
END
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')");
        }

        public override async Task Select_null_propagation_negative2(bool isAsync)
        {
            await base.Select_null_propagation_negative2(isAsync);

            AssertSql(
                $@"SELECT CASE
    WHEN `g`.`LeaderNickname` IS NOT NULL THEN `t`.`LeaderNickname`
    ELSE NULL
END
FROM `Gears` AS `g`,
(
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')");
        }

        public override async Task Select_null_propagation_negative3(bool isAsync)
        {
            await base.Select_null_propagation_negative3(isAsync);

            AssertSql(
                $@"SELECT `t`.`Nickname`, CASE
    WHEN `t`.`Nickname` IS NOT NULL THEN IIF(`t`.`LeaderNickname` IS NOT NULL, 1, 0)
    ELSE NULL
END AS `Condition`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON `g`.`HasSoulPatch` = True
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
ORDER BY `t`.`Nickname`");
        }

        public override async Task Select_null_propagation_negative4(bool isAsync)
        {
            await base.Select_null_propagation_negative4(isAsync);

            AssertSql(
                $@"SELECT IIF(`t`.`Nickname` IS NOT NULL, 1, 0), `t`.`Nickname`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON `g`.`HasSoulPatch` = True
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
ORDER BY `t`.`Nickname`");
        }

        public override async Task Select_null_propagation_negative5(bool isAsync)
        {
            await base.Select_null_propagation_negative5(isAsync);

            AssertSql(
                $@"SELECT IIF(`t`.`Nickname` IS NOT NULL, 1, 0), `t`.`Nickname`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON `g`.`HasSoulPatch` = True
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
ORDER BY `t`.`Nickname`");
        }

        public override async Task Select_null_propagation_negative6(bool isAsync)
        {
            await base.Select_null_propagation_negative6(isAsync);

            AssertSql(
                $@"SELECT CASE
    WHEN `g`.`LeaderNickname` IS NOT NULL THEN IIF(((CAST(LEN(`g`.`LeaderNickname`) AS int) <> CAST(LEN(`g`.`LeaderNickname`) AS int)) OR LEN(`g`.`LeaderNickname`) IS NULL) AND LEN(`g`.`LeaderNickname`) IS NOT NULL, 1, 0)
    ELSE NULL
END
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')");
        }

        public override async Task Select_null_propagation_negative7(bool isAsync)
        {
            await base.Select_null_propagation_negative7(isAsync);

            AssertSql(
                $@"SELECT CASE
    WHEN `g`.`LeaderNickname` IS NOT NULL THEN IIF(((`g`.`LeaderNickname` = `g`.`LeaderNickname`) AND `g`.`LeaderNickname` IS NOT NULL) OR `g`.`LeaderNickname` IS NULL, 1, 0)
    ELSE NULL
END
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')");
        }

        public override async Task Select_null_propagation_negative8(bool isAsync)
        {
            await base.Select_null_propagation_negative8(isAsync);

            AssertSql(
                $@"SELECT CASE
    WHEN `s`.`Id` IS NOT NULL THEN `c`.`Name`
    ELSE NULL
END
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
LEFT JOIN `Squads` AS `s` ON `t0`.`SquadId` = `s`.`Id`
LEFT JOIN `Cities` AS `c` ON `t0`.`AssignedCityName` = `c`.`Name`");
        }

        public override async Task Select_null_propagation_works_for_navigations_with_composite_keys(bool isAsync)
        {
            await base.Select_null_propagation_works_for_navigations_with_composite_keys(isAsync);

            AssertSql(
                $@"SELECT `t0`.`Nickname`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)");
        }

        public override async Task Select_null_propagation_works_for_multiple_navigations_with_composite_keys(bool isAsync)
        {
            await base.Select_null_propagation_works_for_multiple_navigations_with_composite_keys(isAsync);

            AssertSql(
                $@"SELECT CASE
    WHEN `c`.`Name` IS NOT NULL THEN `c`.`Name`
    ELSE NULL
END
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
LEFT JOIN `Tags` AS `t1` ON ((`t0`.`Nickname` = `t1`.`GearNickName`) OR (`t0`.`Nickname` IS NULL AND `t1`.`GearNickName` IS NULL)) AND ((`t0`.`SquadId` = `t1`.`GearSquadId`) OR (`t0`.`SquadId` IS NULL AND `t1`.`GearSquadId` IS NULL))
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t2` ON (`t1`.`GearNickName` = `t2`.`Nickname`) AND (`t1`.`GearSquadId` = `t2`.`SquadId`)
LEFT JOIN `Cities` AS `c` ON `t2`.`AssignedCityName` = `c`.`Name`");
        }

        public override async Task Select_conditional_with_anonymous_type_and_null_constant(bool isAsync)
        {
            await base.Select_conditional_with_anonymous_type_and_null_constant(isAsync);

            AssertSql(
                $@"SELECT IIF(`g`.`LeaderNickname` IS NOT NULL, 1, 0), `g`.`HasSoulPatch`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
ORDER BY `g`.`Nickname`");
        }

        public override async Task Select_conditional_with_anonymous_types(bool isAsync)
        {
            await base.Select_conditional_with_anonymous_types(isAsync);

            AssertSql(
                $@"SELECT IIF(`g`.`LeaderNickname` IS NOT NULL, 1, 0), `g`.`Nickname`, `g`.`FullName`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
ORDER BY `g`.`Nickname`");
        }

        public override async Task Select_coalesce_with_anonymous_types(bool isAsync)
        {
            await base.Select_coalesce_with_anonymous_types(isAsync);

            AssertSql(
                $@"SELECT `g`.`LeaderNickname`, `g`.`FullName`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
ORDER BY `g`.`Nickname`");
        }

        public override async Task Where_compare_anonymous_types(bool isAsync)
        {
            await base.Where_compare_anonymous_types(isAsync);

            AssertSql(
                $@"");
        }

        public override async Task Where_member_access_on_anonymous_type(bool isAsync)
        {
            await base.Where_member_access_on_anonymous_type(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`LeaderNickname` = 'Marcus')");
        }

        public override async Task Where_compare_anonymous_types_with_uncorrelated_members(bool isAsync)
        {
            await base.Where_compare_anonymous_types_with_uncorrelated_members(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`
FROM `Gears` AS `g`
WHERE False = True");
        }

        public override async Task Select_Where_Navigation_Scalar_Equals_Navigation_Scalar(bool isAsync)
        {
            await base.Select_Where_Navigation_Scalar_Equals_Navigation_Scalar(isAsync);

            AssertSql(
                $@"SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`Note`, `t0`.`Id`, `t0`.`GearNickName`, `t0`.`GearSquadId`, `t0`.`Note`
FROM `Tags` AS `t`,
`Tags` AS `t0`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t1` ON (`t`.`GearNickName` = `t1`.`Nickname`) AND (`t`.`GearSquadId` = `t1`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t2` ON (`t0`.`GearNickName` = `t2`.`Nickname`) AND (`t0`.`GearSquadId` = `t2`.`SquadId`)
WHERE (`t1`.`Nickname` = `t2`.`Nickname`) OR (`t1`.`Nickname` IS NULL AND `t2`.`Nickname` IS NULL)");
        }

        public override async Task Select_Singleton_Navigation_With_Member_Access(bool isAsync)
        {
            await base.Select_Singleton_Navigation_With_Member_Access(isAsync);

            AssertSql(
                $@"SELECT `t0`.`Nickname`, `t0`.`SquadId`, `t0`.`AssignedCityName`, `t0`.`CityOfBirthName`, `t0`.`Discriminator`, `t0`.`FullName`, `t0`.`HasSoulPatch`, `t0`.`LeaderNickname`, `t0`.`LeaderSquadId`, `t0`.`Rank`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
WHERE (`t0`.`Nickname` = 'Marcus') AND ((`t0`.`CityOfBirthName` <> 'Ephyra') OR `t0`.`CityOfBirthName` IS NULL)");
        }

        public override async Task Select_Where_Navigation(bool isAsync)
        {
            await base.Select_Where_Navigation(isAsync);

            AssertSql(
                $@"SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`Note`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
WHERE `t0`.`Nickname` = 'Marcus'");
        }

        public override async Task Select_Where_Navigation_Equals_Navigation(bool isAsync)
        {
            await base.Select_Where_Navigation_Equals_Navigation(isAsync);

            AssertSql(
                $@"SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`Note`, `t0`.`Id`, `t0`.`GearNickName`, `t0`.`GearSquadId`, `t0`.`Note`
FROM `Tags` AS `t`,
`Tags` AS `t0`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t1` ON (`t`.`GearNickName` = `t1`.`Nickname`) AND (`t`.`GearSquadId` = `t1`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t2` ON (`t0`.`GearNickName` = `t2`.`Nickname`) AND (`t0`.`GearSquadId` = `t2`.`SquadId`)
WHERE ((`t1`.`Nickname` = `t2`.`Nickname`) OR (`t1`.`Nickname` IS NULL AND `t2`.`Nickname` IS NULL)) AND ((`t1`.`SquadId` = `t2`.`SquadId`) OR (`t1`.`SquadId` IS NULL AND `t2`.`SquadId` IS NULL))");
        }

        public override async Task Select_Where_Navigation_Null(bool isAsync)
        {
            await base.Select_Where_Navigation_Null(isAsync);

            AssertSql(
                $@"SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`Note`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
WHERE `t0`.`Nickname` IS NULL");
        }

        public override async Task Select_Where_Navigation_Null_Reverse(bool isAsync)
        {
            await base.Select_Where_Navigation_Null_Reverse(isAsync);

            AssertSql(
                $@"SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`Note`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
WHERE `t0`.`Nickname` IS NULL");
        }

        public override async Task Select_Where_Navigation_Scalar_Equals_Navigation_Scalar_Projected(bool isAsync)
        {
            await base.Select_Where_Navigation_Scalar_Equals_Navigation_Scalar_Projected(isAsync);

            AssertSql(
                $@"SELECT `t`.`Id` AS `Id1`, `t0`.`Id` AS `Id2`
FROM `Tags` AS `t`,
`Tags` AS `t0`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t1` ON (`t`.`GearNickName` = `t1`.`Nickname`) AND (`t`.`GearSquadId` = `t1`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t2` ON (`t0`.`GearNickName` = `t2`.`Nickname`) AND (`t0`.`GearSquadId` = `t2`.`SquadId`)
WHERE (`t1`.`Nickname` = `t2`.`Nickname`) OR (`t1`.`Nickname` IS NULL AND `t2`.`Nickname` IS NULL)");
        }

        public override async Task Optional_Navigation_Null_Coalesce_To_Clr_Type(bool isAsync)
        {
            await base.Optional_Navigation_Null_Coalesce_To_Clr_Type(isAsync);

            AssertSql(
                $@"SELECT TOP 1 IIf(`w0`.`IsAutomatic` IS NULL, False, `w0`.`IsAutomatic`) AS `IsAutomatic`
FROM `Weapons` AS `w`
LEFT JOIN `Weapons` AS `w0` ON `w`.`SynergyWithId` = `w0`.`Id`
ORDER BY `w`.`Id`");
        }

        public override async Task Where_subquery_boolean(bool isAsync)
        {
            await base.Where_subquery_boolean(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((
    SELECT TOP 1 `w`.`IsAutomatic`
    FROM `Weapons` AS `w`
    WHERE `g`.`FullName` = `w`.`OwnerFullName`
    ORDER BY `w`.`Id`) = True)");
        }

        public override async Task Where_subquery_boolean_with_pushdown(bool isAsync)
        {
            await base.Where_subquery_boolean_with_pushdown(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((
    SELECT TOP 1 `w`.`IsAutomatic`
    FROM `Weapons` AS `w`
    WHERE `g`.`FullName` = `w`.`OwnerFullName`
    ORDER BY `w`.`Id`) = True)");
        }

        public override async Task Where_subquery_distinct_firstordefault_boolean(bool isAsync)
        {
            await base.Where_subquery_distinct_firstordefault_boolean(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((`g`.`HasSoulPatch` = True) AND ((
    SELECT TOP 1 `t`.`IsAutomatic`
    FROM (
        SELECT DISTINCT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
        FROM `Weapons` AS `w`
        WHERE `g`.`FullName` = `w`.`OwnerFullName`
    ) AS `t`
    ORDER BY `t`.`Id`) = True))");
        }

        public override async Task Where_subquery_distinct_firstordefault_boolean_with_pushdown(bool isAsync)
        {
            await base.Where_subquery_distinct_firstordefault_boolean_with_pushdown(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((`g`.`HasSoulPatch` = True) AND ((
    SELECT TOP 1 `t`.`IsAutomatic`
    FROM (
        SELECT DISTINCT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
        FROM `Weapons` AS `w`
        WHERE `g`.`FullName` = `w`.`OwnerFullName`
    ) AS `t`
    ORDER BY `t`.`Id`) = True))");
        }

        public override async Task Where_subquery_distinct_first_boolean(bool isAsync)
        {
            await base.Where_subquery_distinct_first_boolean(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((`g`.`HasSoulPatch` = True) AND ((
    SELECT TOP 1 `t`.`IsAutomatic`
    FROM (
        SELECT DISTINCT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
        FROM `Weapons` AS `w`
        WHERE `g`.`FullName` = `w`.`OwnerFullName`
    ) AS `t`
    ORDER BY `t`.`Id`) = True))
ORDER BY `g`.`Nickname`");
        }

        public override async Task Where_subquery_distinct_singleordefault_boolean1(bool isAsync)
        {
            await base.Where_subquery_distinct_singleordefault_boolean1(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((`g`.`HasSoulPatch` = True) AND ((
    SELECT TOP 1 `t`.`IsAutomatic`
    FROM (
        SELECT DISTINCT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
        FROM `Weapons` AS `w`
        WHERE (`g`.`FullName` = `w`.`OwnerFullName`) AND (CHARINDEX('Lancer', `w`.`Name`) > 0)
    ) AS `t`) = True))
ORDER BY `g`.`Nickname`");
        }

        public override async Task Where_subquery_distinct_singleordefault_boolean2(bool isAsync)
        {
            await base.Where_subquery_distinct_singleordefault_boolean2(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((`g`.`HasSoulPatch` = True) AND ((
    SELECT DISTINCT TOP 1 `w`.`IsAutomatic`
    FROM `Weapons` AS `w`
    WHERE (`g`.`FullName` = `w`.`OwnerFullName`) AND (CHARINDEX('Lancer', `w`.`Name`) > 0)) = True))
ORDER BY `g`.`Nickname`");
        }

        public override async Task Where_subquery_distinct_singleordefault_boolean_with_pushdown(bool isAsync)
        {
            await base.Where_subquery_distinct_singleordefault_boolean_with_pushdown(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((`g`.`HasSoulPatch` = True) AND ((
    SELECT TOP 1 `t`.`IsAutomatic`
    FROM (
        SELECT DISTINCT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
        FROM `Weapons` AS `w`
        WHERE (`g`.`FullName` = `w`.`OwnerFullName`) AND (CHARINDEX('Lancer', `w`.`Name`) > 0)
    ) AS `t`) = True))
ORDER BY `g`.`Nickname`");
        }

        public override async Task Where_subquery_distinct_lastordefault_boolean(bool isAsync)
        {
            await base.Where_subquery_distinct_lastordefault_boolean(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((
    SELECT TOP 1 `t`.`IsAutomatic`
    FROM (
        SELECT DISTINCT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
        FROM `Weapons` AS `w`
        WHERE `g`.`FullName` = `w`.`OwnerFullName`
    ) AS `t`
    ORDER BY `t`.`Id` DESC) <> True)
ORDER BY `g`.`Nickname`");
        }

        public override async Task Where_subquery_distinct_last_boolean(bool isAsync)
        {
            await base.Where_subquery_distinct_last_boolean(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((`g`.`HasSoulPatch` <> True) AND ((
    SELECT TOP 1 `t`.`IsAutomatic`
    FROM (
        SELECT DISTINCT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
        FROM `Weapons` AS `w`
        WHERE `g`.`FullName` = `w`.`OwnerFullName`
    ) AS `t`
    ORDER BY `t`.`Id` DESC) = True))
ORDER BY `g`.`Nickname`");
        }

        public override async Task Where_subquery_distinct_orderby_firstordefault_boolean(bool isAsync)
        {
            await base.Where_subquery_distinct_orderby_firstordefault_boolean(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((`g`.`HasSoulPatch` = True) AND ((
    SELECT TOP 1 `t`.`IsAutomatic`
    FROM (
        SELECT DISTINCT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
        FROM `Weapons` AS `w`
        WHERE `g`.`FullName` = `w`.`OwnerFullName`
    ) AS `t`
    ORDER BY `t`.`Id`) = True))");
        }

        public override async Task Where_subquery_distinct_orderby_firstordefault_boolean_with_pushdown(bool isAsync)
        {
            await base.Where_subquery_distinct_orderby_firstordefault_boolean_with_pushdown(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((`g`.`HasSoulPatch` = True) AND ((
    SELECT TOP 1 `t`.`IsAutomatic`
    FROM (
        SELECT DISTINCT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
        FROM `Weapons` AS `w`
        WHERE `g`.`FullName` = `w`.`OwnerFullName`
    ) AS `t`
    ORDER BY `t`.`Id`) = True))");
        }

        public override async Task Where_subquery_union_firstordefault_boolean(bool isAsync)
        {
            await base.Where_subquery_union_firstordefault_boolean(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Officer', 'Gear') AND (`g`.`HasSoulPatch` = 1)",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_FullName6='Damon Baird' (Size = 450)")}

SELECT `w6`.`Id`, `w6`.`AmmunitionType`, `w6`.`IsAutomatic`, `w6`.`Name`, `w6`.`OwnerFullName`, `w6`.`SynergyWithId`
FROM `Weapons` AS `w6`
WHERE {AssertSqlHelper.Parameter("@_outer_FullName6")} = `w6`.`OwnerFullName`",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_FullName5='Damon Baird' (Size = 450)")}

SELECT `w5`.`Id`, `w5`.`AmmunitionType`, `w5`.`IsAutomatic`, `w5`.`Name`, `w5`.`OwnerFullName`, `w5`.`SynergyWithId`
FROM `Weapons` AS `w5`
WHERE {AssertSqlHelper.Parameter("@_outer_FullName5")} = `w5`.`OwnerFullName`",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_FullName6='Marcus Fenix' (Size = 450)")}

SELECT `w6`.`Id`, `w6`.`AmmunitionType`, `w6`.`IsAutomatic`, `w6`.`Name`, `w6`.`OwnerFullName`, `w6`.`SynergyWithId`
FROM `Weapons` AS `w6`
WHERE {AssertSqlHelper.Parameter("@_outer_FullName6")} = `w6`.`OwnerFullName`",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_FullName5='Marcus Fenix' (Size = 450)")}

SELECT `w5`.`Id`, `w5`.`AmmunitionType`, `w5`.`IsAutomatic`, `w5`.`Name`, `w5`.`OwnerFullName`, `w5`.`SynergyWithId`
FROM `Weapons` AS `w5`
WHERE {AssertSqlHelper.Parameter("@_outer_FullName5")} = `w5`.`OwnerFullName`");
        }

        public override async Task Concat_with_count(bool isAsync)
        {
            await base.Concat_with_count(isAsync);

            AssertSql(
                $@"SELECT COUNT(*)
FROM (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
    UNION ALL
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t`");
        }

        public override async Task Concat_scalars_with_count(bool isAsync)
        {
            await base.Concat_scalars_with_count(isAsync);

            AssertSql(
                $@"SELECT COUNT(*)
FROM (
    SELECT `g`.`Nickname`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
    UNION ALL
    SELECT `g0`.`FullName` AS `Nickname`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t`");
        }

        public override async Task Concat_anonymous_with_count(bool isAsync)
        {
            await base.Concat_anonymous_with_count(isAsync);

            AssertSql(
                $@"SELECT COUNT(*)
FROM (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, `g`.`Nickname` AS `Name`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
    UNION ALL
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, `g0`.`FullName` AS `Name`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t`");
        }

        public override async Task Concat_with_scalar_projection(bool isAsync)
        {
            await base.Concat_with_scalar_projection(isAsync);

            AssertSql(
                $@"SELECT `t`.`Nickname`
FROM (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
    UNION ALL
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t`");
        }
        
        public override async Task Select_subquery_distinct_firstordefault(bool isAsync)
        {
            await base.Select_subquery_distinct_firstordefault(isAsync);

            AssertSql(
                $@"SELECT (
    SELECT TOP 1 `t`.`Name`
    FROM (
        SELECT DISTINCT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
        FROM `Weapons` AS `w`
        WHERE `g`.`FullName` = `w`.`OwnerFullName`
    ) AS `t`
    ORDER BY `t`.`Id`)
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`HasSoulPatch` = True)");
        }

        public override async Task Singleton_Navigation_With_Member_Access(bool isAsync)
        {
            await base.Singleton_Navigation_With_Member_Access(isAsync);

            AssertSql(
                $@"SELECT `t0`.`CityOfBirthName` AS `B`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
WHERE (`t0`.`Nickname` = 'Marcus') AND ((`t0`.`CityOfBirthName` <> 'Ephyra') OR `t0`.`CityOfBirthName` IS NULL)");
        }

        public override async Task GroupJoin_Composite_Key(bool isAsync)
        {
            await base.GroupJoin_Composite_Key(isAsync);

            AssertSql(
                $@"SELECT `t0`.`Nickname`, `t0`.`SquadId`, `t0`.`AssignedCityName`, `t0`.`CityOfBirthName`, `t0`.`Discriminator`, `t0`.`FullName`, `t0`.`HasSoulPatch`, `t0`.`LeaderNickname`, `t0`.`LeaderSquadId`, `t0`.`Rank`
FROM `Tags` AS `t`
INNER JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)");
        }

        public override async Task Join_navigation_translated_to_subquery_composite_key(bool isAsync)
        {
            await base.Join_navigation_translated_to_subquery_composite_key(isAsync);

            AssertSql(
                $@"SELECT `g`.`FullName`, `t1`.`Note`
FROM `Gears` AS `g`
INNER JOIN (
    SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`Note`, `t0`.`Nickname`, `t0`.`SquadId`, `t0`.`AssignedCityName`, `t0`.`CityOfBirthName`, `t0`.`Discriminator`, `t0`.`FullName`, `t0`.`HasSoulPatch`, `t0`.`LeaderNickname`, `t0`.`LeaderSquadId`, `t0`.`Rank`
    FROM `Tags` AS `t`
    LEFT JOIN (
        SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
        FROM `Gears` AS `g0`
        WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
    ) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
) AS `t1` ON `g`.`FullName` = `t1`.`FullName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')");
        }

        public override async Task Join_with_order_by_on_inner_sequence_navigation_translated_to_subquery_composite_key(bool isAsync)
        {
            await base.Join_with_order_by_on_inner_sequence_navigation_translated_to_subquery_composite_key(isAsync);

            AssertSql(
                $@"SELECT `g`.`FullName`, `t1`.`Note`
FROM `Gears` AS `g`
INNER JOIN (
    SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`Note`, `t0`.`Nickname`, `t0`.`SquadId`, `t0`.`AssignedCityName`, `t0`.`CityOfBirthName`, `t0`.`Discriminator`, `t0`.`FullName`, `t0`.`HasSoulPatch`, `t0`.`LeaderNickname`, `t0`.`LeaderSquadId`, `t0`.`Rank`
    FROM `Tags` AS `t`
    LEFT JOIN (
        SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
        FROM `Gears` AS `g0`
        WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
    ) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
) AS `t1` ON `g`.`FullName` = `t1`.`FullName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')");
        }

        public override async Task Join_with_order_by_without_skip_or_take(bool isAsync)
        {
            await base.Join_with_order_by_without_skip_or_take(isAsync);

            AssertSql(
                $@"SELECT `t`.`Name`, `g`.`FullName`
FROM `Gears` AS `g`
INNER JOIN (
    SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
    FROM `Weapons` AS `w`
) AS `t` ON `g`.`FullName` = `t`.`OwnerFullName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')");
        }

        public override async Task Join_with_order_by_without_skip_or_take_nested(bool isAsync)
        {
            await base.Join_with_order_by_without_skip_or_take_nested(isAsync);

            AssertSql(
                $@"SELECT `t0`.`Name`, `t`.`FullName`
FROM `Squads` AS `s`
INNER JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON `s`.`Id` = `t`.`SquadId`
INNER JOIN (
    SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
    FROM `Weapons` AS `w`
) AS `t0` ON `t`.`FullName` = `t0`.`OwnerFullName`");
        }

        public override async Task Collection_with_inheritance_and_join_include_joined(bool isAsync)
        {
            await base.Collection_with_inheritance_and_join_include_joined(isAsync);

            AssertSql(
                $@"SELECT `t0`.`Nickname`, `t0`.`SquadId`, `t0`.`AssignedCityName`, `t0`.`CityOfBirthName`, `t0`.`Discriminator`, `t0`.`FullName`, `t0`.`HasSoulPatch`, `t0`.`LeaderNickname`, `t0`.`LeaderSquadId`, `t0`.`Rank`, `t1`.`Id`, `t1`.`GearNickName`, `t1`.`GearSquadId`, `t1`.`Note`
FROM `Tags` AS `t`
INNER JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Discriminator` = 'Officer')
) AS `t0` ON (`t`.`GearSquadId` = `t0`.`SquadId`) AND (`t`.`GearNickName` = `t0`.`Nickname`)
LEFT JOIN `Tags` AS `t1` ON (`t0`.`Nickname` = `t1`.`GearNickName`) AND (`t0`.`SquadId` = `t1`.`GearSquadId`)");
        }

        public override async Task Collection_with_inheritance_and_join_include_source(bool isAsync)
        {
            await base.Collection_with_inheritance_and_join_include_source(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, `t0`.`Id`, `t0`.`GearNickName`, `t0`.`GearSquadId`, `t0`.`Note`
FROM `Gears` AS `g`
INNER JOIN `Tags` AS `t` ON (`g`.`SquadId` = `t`.`GearSquadId`) AND (`g`.`Nickname` = `t`.`GearNickName`)
LEFT JOIN `Tags` AS `t0` ON (`g`.`Nickname` = `t0`.`GearNickName`) AND (`g`.`SquadId` = `t0`.`GearSquadId`)
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Discriminator` = 'Officer')");
        }

        public override async Task Non_unicode_string_literal_is_used_for_non_unicode_column(bool isAsync)
        {
            await base.Non_unicode_string_literal_is_used_for_non_unicode_column(isAsync);

            AssertSql(
                $@"SELECT `c`.`Name`, `c`.`Location`, `c`.`Nation`
FROM `Cities` AS `c`
WHERE `c`.`Location` = 'Unknown'");
        }

        public override async Task Non_unicode_string_literal_is_used_for_non_unicode_column_right(bool isAsync)
        {
            await base.Non_unicode_string_literal_is_used_for_non_unicode_column_right(isAsync);

            AssertSql(
                $@"SELECT `c`.`Name`, `c`.`Location`, `c`.`Nation`
FROM `Cities` AS `c`
WHERE 'Unknown' = `c`.`Location`");
        }

        public override async Task Non_unicode_parameter_is_used_for_non_unicode_column(bool isAsync)
        {
            await base.Non_unicode_parameter_is_used_for_non_unicode_column(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__value_0='Unknown' (Size = 100) (DbType = AnsiString)")}

SELECT `c`.`Name`, `c`.`Location`, `c`.`Nation`
FROM `Cities` AS `c`
WHERE `c`.`Location` = {AssertSqlHelper.Parameter("@__value_0")}");
        }

        public override async Task Non_unicode_string_literals_in_contains_is_used_for_non_unicode_column(bool isAsync)
        {
            await base.Non_unicode_string_literals_in_contains_is_used_for_non_unicode_column(isAsync);

            AssertSql(
                $@"SELECT `c`.`Name`, `c`.`Location`, `c`.`Nation`
FROM `Cities` AS `c`
WHERE `c`.`Location` IN ('Unknown', 'Jacinto''s location', 'Ephyra''s location')");
        }

        public override async Task Non_unicode_string_literals_is_used_for_non_unicode_column_with_subquery(bool isAsync)
        {
            await base.Non_unicode_string_literals_is_used_for_non_unicode_column_with_subquery(isAsync);

            AssertSql(
                $@"SELECT `c`.`Name`, `c`.`Location`, `c`.`Nation`
FROM `Cities` AS `c`
WHERE (`c`.`Location` = 'Unknown') AND ((
    SELECT COUNT(*)
    FROM `Gears` AS `g`
    WHERE (`g`.`Discriminator` IN ('Gear', 'Officer') AND (`c`.`Name` = `g`.`CityOfBirthName`)) AND (`g`.`Nickname` = 'Paduk')) = 1)");
        }

        public override async Task Non_unicode_string_literals_is_used_for_non_unicode_column_in_subquery(bool isAsync)
        {
            await base.Non_unicode_string_literals_is_used_for_non_unicode_column_in_subquery(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
INNER JOIN `Cities` AS `c` ON `g`.`CityOfBirthName` = `c`.`Name`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((`g`.`Nickname` = 'Marcus') AND (`c`.`Location` = 'Jacinto''s location'))");
        }

        public override async Task Non_unicode_string_literals_is_used_for_non_unicode_column_with_contains(bool isAsync)
        {
            await base.Non_unicode_string_literals_is_used_for_non_unicode_column_with_contains(isAsync);

            AssertSql(
                $@"SELECT `c`.`Name`, `c`.`Location`, `c`.`Nation`
FROM `Cities` AS `c`
WHERE CHARINDEX('Jacinto', `c`.`Location`) > 0");
        }

        public override async Task Non_unicode_string_literals_is_used_for_non_unicode_column_with_concat(bool isAsync)
        {
            await base.Non_unicode_string_literals_is_used_for_non_unicode_column_with_concat(isAsync);

            AssertSql(
                $@"SELECT `c`.`Name`, `c`.`Location`, `c`.`Nation`
FROM `Cities` AS `c`
WHERE CHARINDEX('Add', `c`.`Location` + 'Added') > 0");
        }

        public override void Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_coalesce_result1()
        {
            base.Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_coalesce_result1();

            // Issue#16897
            AssertSql(
                $@"SELECT `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`Discriminator`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`, `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON `g`.`LeaderNickname` = `t`.`Nickname`
LEFT JOIN `Weapons` AS `w` ON `g`.`FullName` = `w`.`OwnerFullName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `w`.`Id`");
        }

        public override void Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_coalesce_result2()
        {
            base.Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_coalesce_result2();

            // Issue#16897
            AssertSql(
                $@"SELECT `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`Discriminator`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`, `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON `g`.`LeaderNickname` = `t`.`Nickname`
LEFT JOIN `Weapons` AS `w` ON `t`.`FullName` = `w`.`OwnerFullName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `w`.`Id`");
        }

        public override async Task Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_coalesce_result3(bool isAsync)
        {
            await base.Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_coalesce_result3(isAsync);

            // Issue#16897
            AssertSql(
                $@"SELECT `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`Discriminator`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`, `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`, `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON `g`.`LeaderNickname` = `t`.`Nickname`
LEFT JOIN `Weapons` AS `w` ON `t`.`FullName` = `w`.`OwnerFullName`
LEFT JOIN `Weapons` AS `w0` ON `g`.`FullName` = `w0`.`OwnerFullName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `w`.`Id`, `w0`.`Id`");
        }

        public override async Task Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_coalesce_result4(bool isAsync)
        {
            await base.Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_coalesce_result4(isAsync);

            // Issue#16897
            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`Discriminator`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `g2`.*
    FROM `Gears` AS `g2`
    WHERE `g2`.`Discriminator` IN ('Officer', 'Gear')
) AS `t` ON `g`.`LeaderNickname` = `t`.`Nickname`
WHERE `g`.`Discriminator` IN ('Officer', 'Gear')
ORDER BY `g`.`FullName`, `t`.`FullName`",
                //
                $@"SELECT [g.Weapons].`Id`, [g.Weapons].`AmmunitionType`, [g.Weapons].`IsAutomatic`, [g.Weapons].`Name`, [g.Weapons].`OwnerFullName`, [g.Weapons].`SynergyWithId`
FROM `Weapons` AS [g.Weapons]
INNER JOIN (
    SELECT DISTINCT `g0`.`FullName`
    FROM `Gears` AS `g0`
    LEFT JOIN (
        SELECT `g20`.*
        FROM `Gears` AS `g20`
        WHERE `g20`.`Discriminator` IN ('Officer', 'Gear')
    ) AS `t0` ON `g0`.`LeaderNickname` = `t0`.`Nickname`
    WHERE `g0`.`Discriminator` IN ('Officer', 'Gear')
) AS `t1` ON [g.Weapons].`OwnerFullName` = `t1`.`FullName`
ORDER BY `t1`.`FullName`",
                //
                $@"SELECT [g2.Weapons].`Id`, [g2.Weapons].`AmmunitionType`, [g2.Weapons].`IsAutomatic`, [g2.Weapons].`Name`, [g2.Weapons].`OwnerFullName`, [g2.Weapons].`SynergyWithId`
FROM `Weapons` AS [g2.Weapons]
INNER JOIN (
    SELECT DISTINCT `t2`.`FullName`, `g1`.`FullName` AS `FullName0`
    FROM `Gears` AS `g1`
    LEFT JOIN (
        SELECT `g21`.*
        FROM `Gears` AS `g21`
        WHERE `g21`.`Discriminator` IN ('Officer', 'Gear')
    ) AS `t2` ON `g1`.`LeaderNickname` = `t2`.`Nickname`
    WHERE `g1`.`Discriminator` IN ('Officer', 'Gear')
) AS `t3` ON [g2.Weapons].`OwnerFullName` = `t3`.`FullName`
ORDER BY `t3`.`FullName0`, `t3`.`FullName`");
        }

        public override async Task Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_inheritance_and_coalesce_result(bool isAsync)
        {
            await base.Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_inheritance_and_coalesce_result(isAsync);

            // Issue#16897
            AssertSql(
                $@"SELECT `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`Discriminator`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`, `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`, `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer') AND (`g0`.`Discriminator` = 'Officer')
) AS `t` ON `g`.`LeaderNickname` = `t`.`Nickname`
LEFT JOIN `Weapons` AS `w` ON `t`.`FullName` = `w`.`OwnerFullName`
LEFT JOIN `Weapons` AS `w0` ON `g`.`FullName` = `w0`.`OwnerFullName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `w`.`Id`, `w0`.`Id`");
        }

        public override async Task Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_conditional_result(bool isAsync)
        {
            await base.Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_conditional_result(isAsync);

            // Issue#16897
            AssertSql(
                $@"SELECT IIF(`t`.`Nickname` IS NOT NULL, 1, 0), `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`Discriminator`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`, `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`, `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON `g`.`LeaderNickname` = `t`.`Nickname`
LEFT JOIN `Weapons` AS `w` ON `t`.`FullName` = `w`.`OwnerFullName`
LEFT JOIN `Weapons` AS `w0` ON `g`.`FullName` = `w0`.`OwnerFullName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `w`.`Id`, `w0`.`Id`");
        }

        public override async Task Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_complex_projection_result(bool isAsync)
        {
            await base.Include_on_GroupJoin_SelectMany_DefaultIfEmpty_with_complex_projection_result(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`Discriminator`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `g2`.*
    FROM `Gears` AS `g2`
    WHERE `g2`.`Discriminator` IN ('Officer', 'Gear')
) AS `t` ON `g`.`LeaderNickname` = `t`.`Nickname`
WHERE `g`.`Discriminator` IN ('Officer', 'Gear')
ORDER BY `g`.`FullName`, `t`.`FullName`",
                //
                $@"SELECT [g.Weapons].`Id`, [g.Weapons].`AmmunitionType`, [g.Weapons].`IsAutomatic`, [g.Weapons].`Name`, [g.Weapons].`OwnerFullName`, [g.Weapons].`SynergyWithId`
FROM `Weapons` AS [g.Weapons]
INNER JOIN (
    SELECT DISTINCT `g0`.`FullName`
    FROM `Gears` AS `g0`
    LEFT JOIN (
        SELECT `g20`.*
        FROM `Gears` AS `g20`
        WHERE `g20`.`Discriminator` IN ('Officer', 'Gear')
    ) AS `t0` ON `g0`.`LeaderNickname` = `t0`.`Nickname`
    WHERE `g0`.`Discriminator` IN ('Officer', 'Gear') AND (`g0`.`Nickname` IS NOT NULL AND `t0`.`Nickname` IS NULL)
) AS `t1` ON [g.Weapons].`OwnerFullName` = `t1`.`FullName`
ORDER BY `t1`.`FullName`",
                //
                $@"SELECT [g2.Weapons].`Id`, [g2.Weapons].`AmmunitionType`, [g2.Weapons].`IsAutomatic`, [g2.Weapons].`Name`, [g2.Weapons].`OwnerFullName`, [g2.Weapons].`SynergyWithId`
FROM `Weapons` AS [g2.Weapons]
INNER JOIN (
    SELECT DISTINCT `t2`.`FullName`, `g1`.`FullName` AS `FullName0`
    FROM `Gears` AS `g1`
    LEFT JOIN (
        SELECT `g21`.*
        FROM `Gears` AS `g21`
        WHERE `g21`.`Discriminator` IN ('Officer', 'Gear')
    ) AS `t2` ON `g1`.`LeaderNickname` = `t2`.`Nickname`
    WHERE `g1`.`Discriminator` IN ('Officer', 'Gear') AND `t2`.`Nickname` IS NOT NULL
) AS `t3` ON [g2.Weapons].`OwnerFullName` = `t3`.`FullName`
ORDER BY `t3`.`FullName0`, `t3`.`FullName`");
        }

        public override async Task Coalesce_operator_in_predicate(bool isAsync)
        {
            await base.Coalesce_operator_in_predicate(isAsync);

            AssertSql(
                $@"SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE IIf(`w`.`IsAutomatic` IS NULL, False, `w`.`IsAutomatic`) = True");
        }

        public override async Task Coalesce_operator_in_predicate_with_other_conditions(bool isAsync)
        {
            await base.Coalesce_operator_in_predicate_with_other_conditions(isAsync);

            AssertSql(
                $@"SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE (`w`.`AmmunitionType` = 1) AND (IIf(`w`.`IsAutomatic` IS NULL, False, `w`.`IsAutomatic`) = True)");
        }

        public override async Task Coalesce_operator_in_projection_with_other_conditions(bool isAsync)
        {
            await base.Coalesce_operator_in_projection_with_other_conditions(isAsync);

            AssertSql(
                $@"SELECT IIF(((`w`.`AmmunitionType` = 1) AND `w`.`AmmunitionType` IS NOT NULL) AND (IIf(`w`.`IsAutomatic` IS NULL, False, `w`.`IsAutomatic`) = True), 1, 0)
FROM `Weapons` AS `w`");
        }

        public override async Task Optional_navigation_type_compensation_works_with_predicate(bool isAsync)
        {
            await base.Optional_navigation_type_compensation_works_with_predicate(isAsync);

            AssertSql(
                $@"SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`Note`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
WHERE ((`t`.`Note` <> 'K.I.A.') OR `t`.`Note` IS NULL) AND (`t0`.`HasSoulPatch` = True)");
        }

        public override async Task Optional_navigation_type_compensation_works_with_predicate2(bool isAsync)
        {
            await base.Optional_navigation_type_compensation_works_with_predicate2(isAsync);

            AssertSql(
                $@"SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`Note`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
WHERE `t0`.`HasSoulPatch` = True");
        }

        public override async Task Optional_navigation_type_compensation_works_with_predicate_negated(bool isAsync)
        {
            await base.Optional_navigation_type_compensation_works_with_predicate_negated(isAsync);

            AssertSql(
                $@"SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`Note`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
WHERE `t0`.`HasSoulPatch` <> True");
        }

        public override async Task Optional_navigation_type_compensation_works_with_predicate_negated_complex1(bool isAsync)
        {
            await base.Optional_navigation_type_compensation_works_with_predicate_negated_complex1(isAsync);

            AssertSql(
                $@"SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`Note`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
WHERE CASE
    WHEN `t0`.`HasSoulPatch` = True THEN True
    ELSE `t0`.`HasSoulPatch`
END <> True");
        }

        public override async Task Optional_navigation_type_compensation_works_with_predicate_negated_complex2(bool isAsync)
        {
            await base.Optional_navigation_type_compensation_works_with_predicate_negated_complex2(isAsync);

            AssertSql(
                $@"SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`Note`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
WHERE CASE
    WHEN `t0`.`HasSoulPatch` <> True THEN False
    ELSE `t0`.`HasSoulPatch`
END <> True");
        }

        public override async Task Optional_navigation_type_compensation_works_with_conditional_expression(bool isAsync)
        {
            await base.Optional_navigation_type_compensation_works_with_conditional_expression(isAsync);

            AssertSql(
                $@"SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`Note`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
WHERE IIF(`t0`.`HasSoulPatch` = True, 1, 0) = True");
        }

        public override async Task Optional_navigation_type_compensation_works_with_binary_expression(bool isAsync)
        {
            await base.Optional_navigation_type_compensation_works_with_binary_expression(isAsync);

            AssertSql(
                $@"SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`Note`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
WHERE (`t0`.`HasSoulPatch` = True) OR (CHARINDEX('Cole', `t`.`Note`) > 0)");
        }

        public override async Task Optional_navigation_type_compensation_works_with_binary_and_expression(bool isAsync)
        {
            await base.Optional_navigation_type_compensation_works_with_binary_and_expression(isAsync);

            AssertSql(
                $@"SELECT IIF((`t0`.`HasSoulPatch` = True) AND (CHARINDEX('Cole', `t`.`Note`) > 0), 1, 0)
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)");
        }

        public override async Task Optional_navigation_type_compensation_works_with_projection(bool isAsync)
        {
            await base.Optional_navigation_type_compensation_works_with_projection(isAsync);

            AssertSql(
                $@"SELECT `t0`.`SquadId`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
WHERE (`t`.`Note` <> 'K.I.A.') OR `t`.`Note` IS NULL");
        }

        public override async Task Optional_navigation_type_compensation_works_with_projection_into_anonymous_type(bool isAsync)
        {
            await base.Optional_navigation_type_compensation_works_with_projection_into_anonymous_type(isAsync);

            AssertSql(
                $@"SELECT `t0`.`SquadId`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
WHERE (`t`.`Note` <> 'K.I.A.') OR `t`.`Note` IS NULL");
        }

        public override async Task Optional_navigation_type_compensation_works_with_DTOs(bool isAsync)
        {
            await base.Optional_navigation_type_compensation_works_with_DTOs(isAsync);

            AssertSql(
                $@"SELECT `t0`.`SquadId` AS `Id`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
WHERE (`t`.`Note` <> 'K.I.A.') OR `t`.`Note` IS NULL");
        }

        public override async Task Optional_navigation_type_compensation_works_with_list_initializers(bool isAsync)
        {
            await base.Optional_navigation_type_compensation_works_with_list_initializers(isAsync);

            AssertSql(
                $@"SELECT `t0`.`SquadId`, `t0`.`SquadId` + 1
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
WHERE (`t`.`Note` <> 'K.I.A.') OR `t`.`Note` IS NULL
ORDER BY `t`.`Note`");
        }

        public override async Task Optional_navigation_type_compensation_works_with_array_initializers(bool isAsync)
        {
            await base.Optional_navigation_type_compensation_works_with_array_initializers(isAsync);

            AssertSql(
                $@"SELECT `t0`.`SquadId`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
WHERE (`t`.`Note` <> 'K.I.A.') OR `t`.`Note` IS NULL");
        }

        public override async Task Optional_navigation_type_compensation_works_with_orderby(bool isAsync)
        {
            await base.Optional_navigation_type_compensation_works_with_orderby(isAsync);

            AssertSql(
                $@"SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`Note`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
WHERE (`t`.`Note` <> 'K.I.A.') OR `t`.`Note` IS NULL
ORDER BY `t0`.`SquadId`");
        }
        
        public override async Task Optional_navigation_type_compensation_works_with_all(bool isAsync)
        {
            await base.Optional_navigation_type_compensation_works_with_all(isAsync);

            AssertSql(
                $@"SELECT CASE
    WHEN NOT EXISTS (
        SELECT 1
        FROM `Tags` AS `t`
        LEFT JOIN (
            SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
            FROM `Gears` AS `g`
            WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
        ) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
        WHERE ((`t`.`Note` <> 'K.I.A.') OR `t`.`Note` IS NULL) AND (`t0`.`HasSoulPatch` <> True)) THEN True
    ELSE False
END");
        }

        public override async Task Optional_navigation_type_compensation_works_with_negated_predicate(bool isAsync)
        {
            await base.Optional_navigation_type_compensation_works_with_negated_predicate(isAsync);

            AssertSql(
                $@"SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`Note`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
WHERE ((`t`.`Note` <> 'K.I.A.') OR `t`.`Note` IS NULL) AND (`t0`.`HasSoulPatch` <> True)");
        }

        public override async Task Optional_navigation_type_compensation_works_with_contains(bool isAsync)
        {
            await base.Optional_navigation_type_compensation_works_with_contains(isAsync);

            AssertSql(
                $@"SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`Note`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
WHERE ((`t`.`Note` <> 'K.I.A.') OR `t`.`Note` IS NULL) AND `t0`.`SquadId` IN (
    SELECT `g0`.`SquadId`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
)");
        }

        public override async Task Optional_navigation_type_compensation_works_with_skip(bool isAsync)
        {
            await base.Optional_navigation_type_compensation_works_with_skip(isAsync);

            AssertSql(
                $@"SELECT `t0`.`SquadId`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT [t.Gear].*
    FROM `Gears` AS [t.Gear]
    WHERE [t.Gear].`Discriminator` IN ('Officer', 'Gear')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
WHERE (`t`.`Note` <> 'K.I.A.') OR `t`.`Note` IS NULL
ORDER BY `t`.`Note`",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_SquadId='1'")}

SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Officer', 'Gear')
ORDER BY `g`.`Nickname`
SKIP {AssertSqlHelper.Parameter("@_outer_SquadId")}",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_SquadId='1'")}

SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Officer', 'Gear')
ORDER BY `g`.`Nickname`
SKIP {AssertSqlHelper.Parameter("@_outer_SquadId")}",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_SquadId='1'")}

SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Officer', 'Gear')
ORDER BY `g`.`Nickname`
SKIP {AssertSqlHelper.Parameter("@_outer_SquadId")}",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_SquadId='1'")}

SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Officer', 'Gear')
ORDER BY `g`.`Nickname`
SKIP {AssertSqlHelper.Parameter("@_outer_SquadId")}",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_SquadId='2'")}

SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Officer', 'Gear')
ORDER BY `g`.`Nickname`
SKIP {AssertSqlHelper.Parameter("@_outer_SquadId")}");
        }

        public override async Task Optional_navigation_type_compensation_works_with_take(bool isAsync)
        {
            await base.Optional_navigation_type_compensation_works_with_take(isAsync);

            AssertSql(
                $@"SELECT `t0`.`SquadId`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT [t.Gear].*
    FROM `Gears` AS [t.Gear]
    WHERE [t.Gear].`Discriminator` IN ('Officer', 'Gear')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
WHERE (`t`.`Note` <> 'K.I.A.') OR `t`.`Note` IS NULL
ORDER BY `t`.`Note`",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_SquadId='1'")}

SELECT TOP {AssertSqlHelper.Parameter("@_outer_SquadId")} `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Officer', 'Gear')
ORDER BY `g`.`Nickname`",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_SquadId='1'")}

SELECT TOP {AssertSqlHelper.Parameter("@_outer_SquadId")} `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Officer', 'Gear')
ORDER BY `g`.`Nickname`",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_SquadId='1'")}

SELECT TOP {AssertSqlHelper.Parameter("@_outer_SquadId")} `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Officer', 'Gear')
ORDER BY `g`.`Nickname`",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_SquadId='1'")}

SELECT TOP {AssertSqlHelper.Parameter("@_outer_SquadId")} `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Officer', 'Gear')
ORDER BY `g`.`Nickname`",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_SquadId='2'")}

SELECT TOP {AssertSqlHelper.Parameter("@_outer_SquadId")} `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Officer', 'Gear')
ORDER BY `g`.`Nickname`");
        }

        public override async Task Select_correlated_filtered_collection(bool isAsync)
        {
            await base.Select_correlated_filtered_collection(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `c`.`Name`, `t`.`Id`, `t`.`AmmunitionType`, `t`.`IsAutomatic`, `t`.`Name`, `t`.`OwnerFullName`, `t`.`SynergyWithId`
FROM `Gears` AS `g`
INNER JOIN `Cities` AS `c` ON `g`.`CityOfBirthName` = `c`.`Name`
LEFT JOIN (
    SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
    FROM `Weapons` AS `w`
    WHERE (`w`.`Name` <> 'Lancer') OR `w`.`Name` IS NULL
) AS `t` ON `g`.`FullName` = `t`.`OwnerFullName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((`c`.`Name` = 'Ephyra') OR (`c`.`Name` = 'Hanover'))
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `c`.`Name`, `t`.`Id`");
        }

        public override async Task Select_correlated_filtered_collection_with_composite_key(bool isAsync)
        {
            await base.Select_correlated_filtered_collection_with_composite_key(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`Discriminator`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer') AND (`g0`.`Nickname` <> 'Dom')
) AS `t` ON (`g`.`Nickname` = `t`.`LeaderNickname`) AND (`g`.`SquadId` = `t`.`LeaderSquadId`)
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Discriminator` = 'Officer')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `t`.`Nickname`, `t`.`SquadId`");
        }

        public override async Task Select_correlated_filtered_collection_works_with_caching(bool isAsync)
        {
            await base.Select_correlated_filtered_collection_works_with_caching(isAsync);

            AssertSql(
                $@"SELECT `t`.`GearNickName`
FROM `Tags` AS `t`
ORDER BY `t`.`Note`",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_GearNickName='Baird' (Size = 450)")}

SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Officer', 'Gear') AND (`g`.`Nickname` = {AssertSqlHelper.Parameter("@_outer_GearNickName")})",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_GearNickName='Cole Train' (Size = 450)")}

SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Officer', 'Gear') AND (`g`.`Nickname` = {AssertSqlHelper.Parameter("@_outer_GearNickName")})",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_GearNickName='Dom' (Size = 450)")}

SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Officer', 'Gear') AND (`g`.`Nickname` = {AssertSqlHelper.Parameter("@_outer_GearNickName")})",
                //
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Officer', 'Gear') AND `g`.`Nickname` IS NULL",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_GearNickName='Marcus' (Size = 450)")}

SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Officer', 'Gear') AND (`g`.`Nickname` = {AssertSqlHelper.Parameter("@_outer_GearNickName")})",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_GearNickName='Paduk' (Size = 450)")}

SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Officer', 'Gear') AND (`g`.`Nickname` = {AssertSqlHelper.Parameter("@_outer_GearNickName")})");
        }

        public override async Task Join_predicate_value_equals_condition(bool isAsync)
        {
            await base.Join_predicate_value_equals_condition(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
INNER JOIN `Weapons` AS `w` ON `w`.`SynergyWithId` IS NOT NULL
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')");
        }

        public override async Task Join_predicate_value(bool isAsync)
        {
            await base.Join_predicate_value(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
INNER JOIN `Weapons` AS `w` ON `g`.`HasSoulPatch` = True
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')");
        }

        public override async Task Join_predicate_condition_equals_condition(bool isAsync)
        {
            await base.Join_predicate_condition_equals_condition(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
INNER JOIN `Weapons` AS `w` ON `w`.`SynergyWithId` IS NOT NULL
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')");
        }

        public override async Task Left_join_predicate_value_equals_condition(bool isAsync)
        {
            await base.Left_join_predicate_value_equals_condition(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
LEFT JOIN `Weapons` AS `w` ON `w`.`SynergyWithId` IS NOT NULL
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')");
        }

        public override async Task Left_join_predicate_value(bool isAsync)
        {
            await base.Left_join_predicate_value(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
LEFT JOIN `Weapons` AS `w` ON `g`.`HasSoulPatch` = True
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')");
        }

        public override async Task Left_join_predicate_condition_equals_condition(bool isAsync)
        {
            await base.Left_join_predicate_condition_equals_condition(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
LEFT JOIN `Weapons` AS `w` ON `w`.`SynergyWithId` IS NOT NULL
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')");
        }

        public override async Task Where_datetimeoffset_now(bool isAsync)
        {
            await base.Where_datetimeoffset_now(isAsync);

            AssertSql(
                $@"SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Rating`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE (`m`.`Timeline` <> SYSDATETIMEOFFSET()) OR SYSDATETIMEOFFSET() IS NULL");
        }

        public override async Task Where_datetimeoffset_utcnow(bool isAsync)
        {
            await base.Where_datetimeoffset_utcnow(isAsync);

            AssertSql(
                $@"SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Rating`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE (`m`.`Timeline` <> CAST(SYSUTCDATETIME() AS datetimeoffset)) OR SYSUTCDATETIME() IS NULL");
        }

        public override async Task Where_datetimeoffset_date_component(bool isAsync)
        {
            await base.Where_datetimeoffset_date_component(isAsync);

            // issue #16057
            //            AssertSql(
            //                $@"SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Rating`, `m`.`Timeline`
            //FROM `Missions` AS `m`
            //WHERE CONVERT(date, `m`.`Timeline`) > '0001-01-01T00:00:00.0000000-08:00'");
        }

        public override async Task Where_datetimeoffset_year_component(bool isAsync)
        {
            await base.Where_datetimeoffset_year_component(isAsync);

            AssertSql(
                $@"SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Rating`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE DATEPART('yyyy', `m`.`Timeline`) = 2");
        }

        public override async Task Where_datetimeoffset_month_component(bool isAsync)
        {
            await base.Where_datetimeoffset_month_component(isAsync);

            AssertSql(
                $@"SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Rating`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE DATEPART('m', `m`.`Timeline`) = 1");
        }

        public override async Task Where_datetimeoffset_dayofyear_component(bool isAsync)
        {
            await base.Where_datetimeoffset_dayofyear_component(isAsync);

            AssertSql(
                $@"SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Rating`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE DATEPART(dayofyear, `m`.`Timeline`) = 2");
        }

        public override async Task Where_datetimeoffset_day_component(bool isAsync)
        {
            await base.Where_datetimeoffset_day_component(isAsync);

            AssertSql(
                $@"SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Rating`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE DATEPART('d', `m`.`Timeline`) = 2");
        }

        public override async Task Where_datetimeoffset_hour_component(bool isAsync)
        {
            await base.Where_datetimeoffset_hour_component(isAsync);

            AssertSql(
                $@"SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Rating`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE DATEPART('h', `m`.`Timeline`) = 10");
        }

        public override async Task Where_datetimeoffset_minute_component(bool isAsync)
        {
            await base.Where_datetimeoffset_minute_component(isAsync);

            AssertSql(
                $@"SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Rating`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE DATEPART('n', `m`.`Timeline`) = 0");
        }

        public override async Task Where_datetimeoffset_second_component(bool isAsync)
        {
            await base.Where_datetimeoffset_second_component(isAsync);

            AssertSql(
                $@"SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Rating`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE DATEPART('s', `m`.`Timeline`) = 0");
        }

        public override async Task Where_datetimeoffset_millisecond_component(bool isAsync)
        {
            await base.Where_datetimeoffset_millisecond_component(isAsync);

            AssertSql(
                $@"SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Rating`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE DATEPART(millisecond, `m`.`Timeline`) = 0");
        }

        public override async Task DateTimeOffset_DateAdd_AddMonths(bool isAsync)
        {
            await base.DateTimeOffset_DateAdd_AddMonths(isAsync);

            AssertSql(
                $@"SELECT DATEADD('m', 1, `m`.`Timeline`)
FROM `Missions` AS `m`");
        }

        public override async Task DateTimeOffset_DateAdd_AddDays(bool isAsync)
        {
            await base.DateTimeOffset_DateAdd_AddDays(isAsync);

            AssertSql(
                $@"SELECT DATEADD('d', CAST(1.0E0 AS int), `m`.`Timeline`)
FROM `Missions` AS `m`");
        }

        public override async Task DateTimeOffset_DateAdd_AddHours(bool isAsync)
        {
            await base.DateTimeOffset_DateAdd_AddHours(isAsync);

            AssertSql(
                $@"SELECT DATEADD('h', CAST(1.0E0 AS int), `m`.`Timeline`)
FROM `Missions` AS `m`");
        }

        public override async Task DateTimeOffset_DateAdd_AddMinutes(bool isAsync)
        {
            await base.DateTimeOffset_DateAdd_AddMinutes(isAsync);

            AssertSql(
                $@"SELECT DATEADD('n', CAST(1.0E0 AS int), `m`.`Timeline`)
FROM `Missions` AS `m`");
        }

        public override async Task DateTimeOffset_DateAdd_AddSeconds(bool isAsync)
        {
            await base.DateTimeOffset_DateAdd_AddSeconds(isAsync);

            AssertSql(
                $@"SELECT DATEADD('s', CAST(1.0E0 AS int), `m`.`Timeline`)
FROM `Missions` AS `m`");
        }

        public override async Task DateTimeOffset_DateAdd_AddMilliseconds(bool isAsync)
        {
            await base.DateTimeOffset_DateAdd_AddMilliseconds(isAsync);

            AssertSql(
                $@"SELECT DATEADD(millisecond, CAST(300.0E0 AS int), `m`.`Timeline`)
FROM `Missions` AS `m`");
        }

        public override async Task Where_datetimeoffset_milliseconds_parameter_and_constant(bool isAsync)
        {
            await base.Where_datetimeoffset_milliseconds_parameter_and_constant(isAsync);

            AssertSql(
                $@"SELECT COUNT(*)
FROM `Missions` AS `m`
WHERE `m`.`Timeline` = '1902-01-02T10:00:00.1234567+01:30'");
        }

        public override async Task Orderby_added_for_client_side_GroupJoin_composite_dependent_to_principal_LOJ_when_incomplete_key_is_used(
            bool isAsync)
        {
            await base.Orderby_added_for_client_side_GroupJoin_composite_dependent_to_principal_LOJ_when_incomplete_key_is_used(isAsync);

            AssertSql(
                $@"SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`Note`, `t0`.`Nickname`, `t0`.`SquadId`, `t0`.`AssignedCityName`, `t0`.`CityOfBirthName`, `t0`.`Discriminator`, `t0`.`FullName`, `t0`.`HasSoulPatch`, `t0`.`LeaderNickname`, `t0`.`LeaderSquadId`, `t0`.`Rank`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.*
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Officer', 'Gear')
) AS `t0` ON `t`.`GearNickName` = `t0`.`Nickname`
ORDER BY `t`.`GearNickName`");
        }

        public override async Task Complex_predicate_with_AndAlso_and_nullable_bool_property(bool isAsync)
        {
            await base.Complex_predicate_with_AndAlso_and_nullable_bool_property(isAsync);

            AssertSql(
                $@"SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON `w`.`OwnerFullName` = `t`.`FullName`
WHERE (`w`.`Id` <> 50) AND (`t`.`HasSoulPatch` <> True)");
        }

        public override async Task Distinct_with_optional_navigation_is_translated_to_sql(bool isAsync)
        {
            await base.Distinct_with_optional_navigation_is_translated_to_sql(isAsync);

            AssertSql(
                $@"SELECT DISTINCT `g`.`HasSoulPatch`
FROM `Gears` AS `g`
LEFT JOIN `Tags` AS `t` ON (`g`.`Nickname` = `t`.`GearNickName`) AND (`g`.`SquadId` = `t`.`GearSquadId`)
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((`t`.`Note` <> 'Foo') OR `t`.`Note` IS NULL)");
        }

        public override async Task Sum_with_optional_navigation_is_translated_to_sql(bool isAsync)
        {
            await base.Sum_with_optional_navigation_is_translated_to_sql(isAsync);

            AssertSql(
                $@"SELECT SUM(`g`.`SquadId`)
FROM `Gears` AS `g`
LEFT JOIN `Tags` AS `t` ON (`g`.`Nickname` = `t`.`GearNickName`) AND (`g`.`SquadId` = `t`.`GearSquadId`)
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((`t`.`Note` <> 'Foo') OR `t`.`Note` IS NULL)");
        }

        public override async Task Count_with_optional_navigation_is_translated_to_sql(bool isAsync)
        {
            await base.Count_with_optional_navigation_is_translated_to_sql(isAsync);

            AssertSql(
                $@"SELECT COUNT(*)
FROM `Gears` AS `g`
LEFT JOIN `Tags` AS `t` ON (`g`.`Nickname` = `t`.`GearNickName`) AND (`g`.`SquadId` = `t`.`GearSquadId`)
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((`t`.`Note` <> 'Foo') OR `t`.`Note` IS NULL)");
        }

        public override async Task FirstOrDefault_with_manually_created_groupjoin_is_translated_to_sql(bool isAsync)
        {
            await base.FirstOrDefault_with_manually_created_groupjoin_is_translated_to_sql(isAsync);

            AssertSql(
                $@"SELECT TOP 1 `s`.`Id`, `s`.`InternalNumber`, `s`.`Name`
FROM `Squads` AS `s`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON `s`.`Id` = `t`.`SquadId`
WHERE `s`.`Name` = 'Kilo'");
        }

        public override async Task Any_with_optional_navigation_as_subquery_predicate_is_translated_to_sql(bool isAsync)
        {
            await base.Any_with_optional_navigation_as_subquery_predicate_is_translated_to_sql(isAsync);

            AssertSql(
                $@"SELECT `s`.`Name`
FROM `Squads` AS `s`
WHERE NOT (EXISTS (
    SELECT 1
    FROM `Gears` AS `g`
    LEFT JOIN `Tags` AS `t` ON (`g`.`Nickname` = `t`.`GearNickName`) AND (`g`.`SquadId` = `t`.`GearSquadId`)
    WHERE (`g`.`Discriminator` IN ('Gear', 'Officer') AND (`s`.`Id` = `g`.`SquadId`)) AND (`t`.`Note` = 'Dom''s Tag')))");
        }

        public override async Task All_with_optional_navigation_is_translated_to_sql(bool isAsync)
        {
            await base.All_with_optional_navigation_is_translated_to_sql(isAsync);

            AssertSql(
                $@"SELECT CASE
    WHEN NOT EXISTS (
        SELECT 1
        FROM `Gears` AS `g`
        LEFT JOIN `Tags` AS `t` ON (`g`.`Nickname` = `t`.`GearNickName`) AND (`g`.`SquadId` = `t`.`GearSquadId`)
        WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((`t`.`Note` = 'Foo') AND `t`.`Note` IS NOT NULL)) THEN True
    ELSE False
END");
        }
        
        public override async Task Contains_with_local_nullable_guid_list_closure(bool isAsync)
        {
            await base.Contains_with_local_nullable_guid_list_closure(isAsync);

            AssertSql(
                $@"SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`Note`
FROM `Tags` AS `t`
WHERE `t`.`Id` IN ('d2c26679-562b-44d1-ab96-23d1775e0926', '23cbcf9b-ce14-45cf-aafa-2c2667ebfdd3', 'ab1b82d7-88db-42bd-a132-7eef9aa68af4')");
        }

        public override async Task Unnecessary_include_doesnt_get_added_complex_when_projecting_EF_Property(bool isAsync)
        {
            await base.Unnecessary_include_doesnt_get_added_complex_when_projecting_EF_Property(isAsync);

            AssertSql(
                $@"SELECT `g`.`FullName`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`HasSoulPatch` = True)
ORDER BY `g`.`Rank`");
        }

        public override async Task Multiple_order_bys_are_properly_lifted_from_subquery_created_by_include(bool isAsync)
        {
            await base.Multiple_order_bys_are_properly_lifted_from_subquery_created_by_include(isAsync);

            AssertSql(
                $@"SELECT `g`.`FullName`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`HasSoulPatch` <> True)
ORDER BY `g`.`FullName`");
        }

        public override async Task Order_by_is_properly_lifted_from_subquery_with_same_order_by_in_the_outer_query(bool isAsync)
        {
            await base.Order_by_is_properly_lifted_from_subquery_with_same_order_by_in_the_outer_query(isAsync);

            AssertSql(
                $@"SELECT `g`.`FullName`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`HasSoulPatch` <> True)
ORDER BY `g`.`FullName`");
        }

        public override async Task Where_is_properly_lifted_from_subquery_created_by_include(bool isAsync)
        {
            await base.Where_is_properly_lifted_from_subquery_created_by_include(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`Note`
FROM `Gears` AS `g`
LEFT JOIN `Tags` AS `t` ON (`g`.`Nickname` = `t`.`GearNickName`) AND (`g`.`SquadId` = `t`.`GearSquadId`)
WHERE (`g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`FullName` <> 'Augustus Cole')) AND (`g`.`HasSoulPatch` <> True)
ORDER BY `g`.`FullName`");
        }

        public override async Task Subquery_is_lifted_from_main_from_clause_of_SelectMany(bool isAsync)
        {
            await base.Subquery_is_lifted_from_main_from_clause_of_SelectMany(isAsync);

            AssertSql(
                $@"SELECT `g`.`FullName` AS `Name1`, `t`.`FullName` AS `Name2`
FROM `Gears` AS `g`,
(
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((`g`.`HasSoulPatch` = True) AND (`t`.`HasSoulPatch` <> True))
ORDER BY `g`.`FullName`");
        }

        public override async Task Subquery_containing_SelectMany_projecting_main_from_clause_gets_lifted(bool isAsync)
        {
            await base.Subquery_containing_SelectMany_projecting_main_from_clause_gets_lifted(isAsync);

            AssertSql(
                $@"SELECT `g`.`FullName`
FROM `Gears` AS `g`,
`Tags` AS `t`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`HasSoulPatch` = True)
ORDER BY `g`.`FullName`");
        }

        public override async Task Subquery_containing_join_projecting_main_from_clause_gets_lifted(bool isAsync)
        {
            await base.Subquery_containing_join_projecting_main_from_clause_gets_lifted(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`
FROM `Gears` AS `g`
INNER JOIN `Tags` AS `t` ON `g`.`Nickname` = `t`.`GearNickName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
ORDER BY `g`.`Nickname`");
        }

        public override async Task Subquery_containing_left_join_projecting_main_from_clause_gets_lifted(bool isAsync)
        {
            await base.Subquery_containing_left_join_projecting_main_from_clause_gets_lifted(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`
FROM `Gears` AS `g`
LEFT JOIN `Tags` AS `t` ON `g`.`Nickname` = `t`.`GearNickName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
ORDER BY `g`.`Nickname`");
        }

        public override async Task Subquery_containing_join_gets_lifted_clashing_names(bool isAsync)
        {
            await base.Subquery_containing_join_gets_lifted_clashing_names(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`
FROM `Gears` AS `g`
INNER JOIN `Tags` AS `t` ON `g`.`Nickname` = `t`.`GearNickName`
INNER JOIN `Tags` AS `t0` ON `g`.`Nickname` = `t0`.`GearNickName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((`t`.`GearNickName` <> 'Cole Train') OR `t`.`GearNickName` IS NULL)
ORDER BY `g`.`Nickname`, `t0`.`Id`");
        }

        public override async Task Subquery_created_by_include_gets_lifted_nested(bool isAsync)
        {
            await base.Subquery_created_by_include_gets_lifted_nested(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, `c`.`Name`, `c`.`Location`, `c`.`Nation`
FROM `Gears` AS `g`
INNER JOIN `Cities` AS `c` ON `g`.`CityOfBirthName` = `c`.`Name`
WHERE (`g`.`Discriminator` IN ('Gear', 'Officer') AND EXISTS (
    SELECT 1
    FROM `Weapons` AS `w`
    WHERE `g`.`FullName` = `w`.`OwnerFullName`)) AND (`g`.`HasSoulPatch` <> True)
ORDER BY `g`.`Nickname`");
        }

        public override async Task Subquery_is_lifted_from_additional_from_clause(bool isAsync)
        {
            await base.Subquery_is_lifted_from_additional_from_clause(isAsync);

            AssertSql(
                $@"SELECT `g`.`FullName` AS `Name1`, `t`.`FullName` AS `Name2`
FROM `Gears` AS `g`,
(
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((`g`.`HasSoulPatch` = True) AND (`t`.`HasSoulPatch` <> True))
ORDER BY `g`.`FullName`");
        }

        public override async Task Subquery_with_result_operator_is_not_lifted(bool isAsync)
        {
            await base.Subquery_with_result_operator_is_not_lifted(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='2'")}

SELECT `t`.`FullName`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`HasSoulPatch` <> True)
    ORDER BY `g`.`FullName`
) AS `t`
ORDER BY `t`.`Rank`");
        }

        public override async Task Skip_with_orderby_followed_by_orderBy_is_pushed_down(bool isAsync)
        {
            await base.Skip_with_orderby_followed_by_orderBy_is_pushed_down(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='1'")}

SELECT `t`.`FullName`
FROM (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`HasSoulPatch` <> True)
    ORDER BY `g`.`FullName`
    SKIP {AssertSqlHelper.Parameter("@__p_0")}
) AS `t`
ORDER BY `t`.`Rank`");
        }

        public override async Task Take_without_orderby_followed_by_orderBy_is_pushed_down1(bool isAsync)
        {
            await base.Take_without_orderby_followed_by_orderBy_is_pushed_down1(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='999'")}

SELECT `t`.`FullName`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`HasSoulPatch` <> True)
) AS `t`
ORDER BY `t`.`Rank`");
        }

        public override async Task Take_without_orderby_followed_by_orderBy_is_pushed_down2(bool isAsync)
        {
            await base.Take_without_orderby_followed_by_orderBy_is_pushed_down2(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='999'")}

SELECT `t`.`FullName`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`HasSoulPatch` <> True)
) AS `t`
ORDER BY `t`.`Rank`");
        }

        public override async Task Take_without_orderby_followed_by_orderBy_is_pushed_down3(bool isAsync)
        {
            await base.Take_without_orderby_followed_by_orderBy_is_pushed_down3(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='999'")}

SELECT `t`.`FullName`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`HasSoulPatch` <> True)
) AS `t`
ORDER BY `t`.`FullName`, `t`.`Rank`");
        }

        public override async Task Select_length_of_string_property(bool isAsync)
        {
            await base.Select_length_of_string_property(isAsync);

            AssertSql(
                $@"SELECT `w`.`Name`, CAST(LEN(`w`.`Name`) AS int) AS `Length`
FROM `Weapons` AS `w`");
        }

        public override async Task Client_method_on_collection_navigation_in_outer_join_key(bool isAsync)
        {
            await base.Client_method_on_collection_navigation_in_outer_join_key(isAsync);

            AssertSql(
                $@"SELECT `g`.`FullName`, `g`.`Nickname`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Officer', 'Gear')",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_FullName1='Damon Baird' (Size = 450)")}

SELECT `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
FROM `Weapons` AS `w0`
WHERE {AssertSqlHelper.Parameter("@_outer_FullName1")} = `w0`.`OwnerFullName`",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_FullName1='Augustus Cole' (Size = 450)")}

SELECT `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
FROM `Weapons` AS `w0`
WHERE {AssertSqlHelper.Parameter("@_outer_FullName1")} = `w0`.`OwnerFullName`",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_FullName1='Dominic Santiago' (Size = 450)")}

SELECT `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
FROM `Weapons` AS `w0`
WHERE {AssertSqlHelper.Parameter("@_outer_FullName1")} = `w0`.`OwnerFullName`",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_FullName1='Marcus Fenix' (Size = 450)")}

SELECT `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
FROM `Weapons` AS `w0`
WHERE {AssertSqlHelper.Parameter("@_outer_FullName1")} = `w0`.`OwnerFullName`",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_FullName1='Garron Paduk' (Size = 450)")}

SELECT `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
FROM `Weapons` AS `w0`
WHERE {AssertSqlHelper.Parameter("@_outer_FullName1")} = `w0`.`OwnerFullName`",
                //
                $@"SELECT `o`.`FullName`, `o`.`Nickname` AS `o`
FROM `Gears` AS `o`
WHERE (`o`.`Discriminator` = 'Officer') AND (`o`.`HasSoulPatch` = 1)",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_FullName='Damon Baird' (Size = 450)")}

SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE {AssertSqlHelper.Parameter("@_outer_FullName")} = `w`.`OwnerFullName`",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_FullName='Marcus Fenix' (Size = 450)")}

SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE {AssertSqlHelper.Parameter("@_outer_FullName")} = `w`.`OwnerFullName`");
        }

        public override async Task Member_access_on_derived_entity_using_cast(bool isAsync)
        {
            await base.Member_access_on_derived_entity_using_cast(isAsync);

            AssertSql(
                $@"SELECT `f`.`Name`, `f`.`Eradicated`
FROM `Factions` AS `f`
WHERE (`f`.`Discriminator` = 'LocustHorde') AND (`f`.`Discriminator` = 'LocustHorde')
ORDER BY `f`.`Name`");
        }

        public override async Task Member_access_on_derived_materialized_entity_using_cast(bool isAsync)
        {
            await base.Member_access_on_derived_materialized_entity_using_cast(isAsync);

            AssertSql(
                $@"SELECT `f`.`Id`, `f`.`CapitalName`, `f`.`Discriminator`, `f`.`Name`, `f`.`CommanderName`, `f`.`Eradicated`
FROM `Factions` AS `f`
WHERE (`f`.`Discriminator` = 'LocustHorde') AND (`f`.`Discriminator` = 'LocustHorde')
ORDER BY `f`.`Name`");
        }

        public override async Task Member_access_on_derived_entity_using_cast_and_let(bool isAsync)
        {
            await base.Member_access_on_derived_entity_using_cast_and_let(isAsync);

            AssertSql(
                $@"SELECT `f`.`Name`, `f`.`Eradicated`
FROM `Factions` AS `f`
WHERE (`f`.`Discriminator` = 'LocustHorde') AND (`f`.`Discriminator` = 'LocustHorde')
ORDER BY `f`.`Name`");
        }

        public override async Task Property_access_on_derived_entity_using_cast(bool isAsync)
        {
            await base.Property_access_on_derived_entity_using_cast(isAsync);

            AssertSql(
                $@"SELECT `f`.`Name`, `f`.`Eradicated`
FROM `Factions` AS `f`
WHERE (`f`.`Discriminator` = 'LocustHorde') AND (`f`.`Discriminator` = 'LocustHorde')
ORDER BY `f`.`Name`");
        }

        public override async Task Navigation_access_on_derived_entity_using_cast(bool isAsync)
        {
            await base.Navigation_access_on_derived_entity_using_cast(isAsync);

            AssertSql(
                $@"SELECT `f`.`Name`, `t`.`ThreatLevel` AS `Threat`
FROM `Factions` AS `f`
LEFT JOIN (
    SELECT `l`.`Name`, `l`.`Discriminator`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`DefeatedByNickname`, `l`.`DefeatedBySquadId`, `l`.`HighCommandId`
    FROM `LocustLeaders` AS `l`
    WHERE `l`.`Discriminator` = 'LocustCommander'
) AS `t` ON `f`.`CommanderName` = `t`.`Name`
WHERE (`f`.`Discriminator` = 'LocustHorde') AND (`f`.`Discriminator` = 'LocustHorde')
ORDER BY `f`.`Name`");
        }

        public override async Task Navigation_access_on_derived_materialized_entity_using_cast(bool isAsync)
        {
            await base.Navigation_access_on_derived_materialized_entity_using_cast(isAsync);

            AssertSql(
                $@"SELECT `f`.`Id`, `f`.`CapitalName`, `f`.`Discriminator`, `f`.`Name`, `f`.`CommanderName`, `f`.`Eradicated`, `t`.`ThreatLevel` AS `Threat`
FROM `Factions` AS `f`
LEFT JOIN (
    SELECT `l`.`Name`, `l`.`Discriminator`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`DefeatedByNickname`, `l`.`DefeatedBySquadId`, `l`.`HighCommandId`
    FROM `LocustLeaders` AS `l`
    WHERE `l`.`Discriminator` = 'LocustCommander'
) AS `t` ON `f`.`CommanderName` = `t`.`Name`
WHERE (`f`.`Discriminator` = 'LocustHorde') AND (`f`.`Discriminator` = 'LocustHorde')
ORDER BY `f`.`Name`");
        }

        public override async Task Navigation_access_via_EFProperty_on_derived_entity_using_cast(bool isAsync)
        {
            await base.Navigation_access_via_EFProperty_on_derived_entity_using_cast(isAsync);

            AssertSql(
                $@"SELECT `f`.`Name`, `t`.`ThreatLevel` AS `Threat`
FROM `Factions` AS `f`
LEFT JOIN (
    SELECT `l`.`Name`, `l`.`Discriminator`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`DefeatedByNickname`, `l`.`DefeatedBySquadId`, `l`.`HighCommandId`
    FROM `LocustLeaders` AS `l`
    WHERE `l`.`Discriminator` = 'LocustCommander'
) AS `t` ON `f`.`CommanderName` = `t`.`Name`
WHERE (`f`.`Discriminator` = 'LocustHorde') AND (`f`.`Discriminator` = 'LocustHorde')
ORDER BY `f`.`Name`");
        }

        public override async Task Navigation_access_fk_on_derived_entity_using_cast(bool isAsync)
        {
            await base.Navigation_access_fk_on_derived_entity_using_cast(isAsync);

            AssertSql(
                $@"SELECT `f`.`Name`, `t`.`Name` AS `CommanderName`
FROM `Factions` AS `f`
LEFT JOIN (
    SELECT `l`.`Name`, `l`.`Discriminator`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`DefeatedByNickname`, `l`.`DefeatedBySquadId`, `l`.`HighCommandId`
    FROM `LocustLeaders` AS `l`
    WHERE `l`.`Discriminator` = 'LocustCommander'
) AS `t` ON `f`.`CommanderName` = `t`.`Name`
WHERE (`f`.`Discriminator` = 'LocustHorde') AND (`f`.`Discriminator` = 'LocustHorde')
ORDER BY `f`.`Name`");
        }

        public override async Task Collection_navigation_access_on_derived_entity_using_cast(bool isAsync)
        {
            await base.Collection_navigation_access_on_derived_entity_using_cast(isAsync);

            AssertSql(
                $@"SELECT `f`.`Name`, (
    SELECT COUNT(*)
    FROM `LocustLeaders` AS `l`
    WHERE `l`.`Discriminator` IN ('LocustLeader', 'LocustCommander') AND (`f`.`Id` = `l`.`LocustHordeId`)) AS `LeadersCount`
FROM `Factions` AS `f`
WHERE (`f`.`Discriminator` = 'LocustHorde') AND (`f`.`Discriminator` = 'LocustHorde')
ORDER BY `f`.`Name`");
        }

        public override async Task Collection_navigation_access_on_derived_entity_using_cast_in_SelectMany(bool isAsync)
        {
            await base.Collection_navigation_access_on_derived_entity_using_cast_in_SelectMany(isAsync);

            AssertSql(
                $@"SELECT `f`.`Name`, `t`.`Name` AS `LeaderName`
FROM `Factions` AS `f`
INNER JOIN (
    SELECT `l`.`Name`, `l`.`Discriminator`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`DefeatedByNickname`, `l`.`DefeatedBySquadId`, `l`.`HighCommandId`
    FROM `LocustLeaders` AS `l`
    WHERE `l`.`Discriminator` IN ('LocustLeader', 'LocustCommander')
) AS `t` ON `f`.`Id` = `t`.`LocustHordeId`
WHERE (`f`.`Discriminator` = 'LocustHorde') AND (`f`.`Discriminator` = 'LocustHorde')
ORDER BY `t`.`Name`");
        }

        public override async Task Include_on_derived_entity_using_OfType(bool isAsync)
        {
            await base.Include_on_derived_entity_using_OfType(isAsync);

            AssertSql(
                $@"SELECT `f`.`Id`, `f`.`CapitalName`, `f`.`Discriminator`, `f`.`Name`, `f`.`CommanderName`, `f`.`Eradicated`, `t`.`Name`, `t`.`Discriminator`, `t`.`LocustHordeId`, `t`.`ThreatLevel`, `t`.`DefeatedByNickname`, `t`.`DefeatedBySquadId`, `t`.`HighCommandId`, `t0`.`Name`, `t0`.`Discriminator`, `t0`.`LocustHordeId`, `t0`.`ThreatLevel`, `t0`.`DefeatedByNickname`, `t0`.`DefeatedBySquadId`, `t0`.`HighCommandId`
FROM `Factions` AS `f`
LEFT JOIN (
    SELECT `l`.`Name`, `l`.`Discriminator`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`DefeatedByNickname`, `l`.`DefeatedBySquadId`, `l`.`HighCommandId`
    FROM `LocustLeaders` AS `l`
    WHERE `l`.`Discriminator` = 'LocustCommander'
) AS `t` ON `f`.`CommanderName` = `t`.`Name`
LEFT JOIN (
    SELECT `l0`.`Name`, `l0`.`Discriminator`, `l0`.`LocustHordeId`, `l0`.`ThreatLevel`, `l0`.`DefeatedByNickname`, `l0`.`DefeatedBySquadId`, `l0`.`HighCommandId`
    FROM `LocustLeaders` AS `l0`
    WHERE `l0`.`Discriminator` IN ('LocustLeader', 'LocustCommander')
) AS `t0` ON `f`.`Id` = `t0`.`LocustHordeId`
WHERE (`f`.`Discriminator` = 'LocustHorde') AND (`f`.`Discriminator` = 'LocustHorde')
ORDER BY `f`.`Name`, `f`.`Id`, `t0`.`Name`");
        }

//        public override async Task Include_on_derived_entity_using_subquery_with_cast(bool isAsync)
//        {
//            await base.Include_on_derived_entity_using_subquery_with_cast(isAsync);

//            AssertSql(
//                $@"SELECT `f`.`Id`, `f`.`CapitalName`, `f`.`Discriminator`, `f`.`Name`, `f`.`CommanderName`, `f`.`Eradicated`, `t`.`Name`, `t`.`Discriminator`, `t`.`LocustHordeId`, `t`.`ThreatLevel`, `t`.`DefeatedByNickname`, `t`.`DefeatedBySquadId`, `t`.`HighCommandId`
//FROM `Factions` AS `f`
//LEFT JOIN (
//    SELECT [f.Commander].*
//    FROM `LocustLeaders` AS [f.Commander]
//    WHERE [f.Commander].`Discriminator` = 'LocustCommander'
//) AS `t` ON (`f`.`Discriminator` = 'LocustHorde') AND (`f`.`CommanderName` = `t`.`Name`)
//WHERE (`f`.`Discriminator` = 'LocustHorde') AND (`f`.`Discriminator` = 'LocustHorde')
//ORDER BY `f`.`Name`, `f`.`Id`",
//                //
//                $@"SELECT [f.Leaders].`Name`, [f.Leaders].`Discriminator`, [f.Leaders].`LocustHordeId`, [f.Leaders].`ThreatLevel`, [f.Leaders].`DefeatedByNickname`, [f.Leaders].`DefeatedBySquadId`, [f.Leaders].`HighCommandId`
//FROM `LocustLeaders` AS [f.Leaders]
//INNER JOIN (
//    SELECT DISTINCT `f0`.`Id`, `f0`.`Name`
//    FROM `Factions` AS `f0`
//    LEFT JOIN (
//        SELECT [f.Commander0].*
//        FROM `LocustLeaders` AS [f.Commander0]
//        WHERE [f.Commander0].`Discriminator` = 'LocustCommander'
//    ) AS `t0` ON (`f0`.`Discriminator` = 'LocustHorde') AND (`f0`.`CommanderName` = `t0`.`Name`)
//    WHERE (`f0`.`Discriminator` = 'LocustHorde') AND (`f0`.`Discriminator` = 'LocustHorde')
//) AS `t1` ON [f.Leaders].`LocustHordeId` = `t1`.`Id`
//WHERE [f.Leaders].`Discriminator` IN ('LocustCommander', 'LocustLeader')
//ORDER BY `t1`.`Name`, `t1`.`Id`");
//        }

//        public override async Task Include_on_derived_entity_using_subquery_with_cast_AsNoTracking(bool isAsync)
//        {
//            await base.Include_on_derived_entity_using_subquery_with_cast_AsNoTracking(isAsync);

//            AssertSql(
//                $@"SELECT `f`.`Id`, `f`.`CapitalName`, `f`.`Discriminator`, `f`.`Name`, `f`.`CommanderName`, `f`.`Eradicated`, `t`.`Name`, `t`.`Discriminator`, `t`.`LocustHordeId`, `t`.`ThreatLevel`, `t`.`DefeatedByNickname`, `t`.`DefeatedBySquadId`, `t`.`HighCommandId`
//FROM `Factions` AS `f`
//LEFT JOIN (
//    SELECT [f.Commander].*
//    FROM `LocustLeaders` AS [f.Commander]
//    WHERE [f.Commander].`Discriminator` = 'LocustCommander'
//) AS `t` ON (`f`.`Discriminator` = 'LocustHorde') AND (`f`.`CommanderName` = `t`.`Name`)
//WHERE (`f`.`Discriminator` = 'LocustHorde') AND (`f`.`Discriminator` = 'LocustHorde')
//ORDER BY `f`.`Name`, `f`.`Id`",
//                //
//                $@"SELECT [f.Leaders].`Name`, [f.Leaders].`Discriminator`, [f.Leaders].`LocustHordeId`, [f.Leaders].`ThreatLevel`, [f.Leaders].`DefeatedByNickname`, [f.Leaders].`DefeatedBySquadId`, [f.Leaders].`HighCommandId`
//FROM `LocustLeaders` AS [f.Leaders]
//INNER JOIN (
//    SELECT DISTINCT `f0`.`Id`, `f0`.`Name`
//    FROM `Factions` AS `f0`
//    LEFT JOIN (
//        SELECT [f.Commander0].*
//        FROM `LocustLeaders` AS [f.Commander0]
//        WHERE [f.Commander0].`Discriminator` = 'LocustCommander'
//    ) AS `t0` ON (`f0`.`Discriminator` = 'LocustHorde') AND (`f0`.`CommanderName` = `t0`.`Name`)
//    WHERE (`f0`.`Discriminator` = 'LocustHorde') AND (`f0`.`Discriminator` = 'LocustHorde')
//) AS `t1` ON [f.Leaders].`LocustHordeId` = `t1`.`Id`
//WHERE [f.Leaders].`Discriminator` IN ('LocustCommander', 'LocustLeader')
//ORDER BY `t1`.`Name`, `t1`.`Id`");
//        }

//        public override void Include_on_derived_entity_using_subquery_with_cast_cross_product_base_entity()
//        {
//            base.Include_on_derived_entity_using_subquery_with_cast_cross_product_base_entity();

//            AssertSql(
//                $@"SELECT `f2`.`Id`, `f2`.`CapitalName`, `f2`.`Discriminator`, `f2`.`Name`, `f2`.`CommanderName`, `f2`.`Eradicated`, `t`.`Name`, `t`.`Discriminator`, `t`.`LocustHordeId`, `t`.`ThreatLevel`, `t`.`DefeatedByNickname`, `t`.`DefeatedBySquadId`, `t`.`HighCommandId`, `ff`.`Id`, `ff`.`CapitalName`, `ff`.`Discriminator`, `ff`.`Name`, `ff`.`CommanderName`, `ff`.`Eradicated`, [ff.Capital].`Name`, [ff.Capital].`Location`, [ff.Capital].`Nation`
//FROM `Factions` AS `f2`
//LEFT JOIN (
//    SELECT [f2.Commander].*
//    FROM `LocustLeaders` AS [f2.Commander]
//    WHERE [f2.Commander].`Discriminator` = 'LocustCommander'
//) AS `t` ON (`f2`.`Discriminator` = 'LocustHorde') AND (`f2`.`CommanderName` = `t`.`Name`)
//CROSS JOIN `Factions` AS `ff`
//LEFT JOIN `Cities` AS [ff.Capital] ON `ff`.`CapitalName` = [ff.Capital].`Name`
//WHERE (`f2`.`Discriminator` = 'LocustHorde') AND (`f2`.`Discriminator` = 'LocustHorde')
//ORDER BY `f2`.`Name`, `ff`.`Name`, `f2`.`Id`",
//                //
//                $@"SELECT [f2.Leaders].`Name`, [f2.Leaders].`Discriminator`, [f2.Leaders].`LocustHordeId`, [f2.Leaders].`ThreatLevel`, [f2.Leaders].`DefeatedByNickname`, [f2.Leaders].`DefeatedBySquadId`, [f2.Leaders].`HighCommandId`
//FROM `LocustLeaders` AS [f2.Leaders]
//INNER JOIN (
//    SELECT DISTINCT `f20`.`Id`, `f20`.`Name`, `ff0`.`Name` AS `Name0`
//    FROM `Factions` AS `f20`
//    LEFT JOIN (
//        SELECT [f2.Commander0].*
//        FROM `LocustLeaders` AS [f2.Commander0]
//        WHERE [f2.Commander0].`Discriminator` = 'LocustCommander'
//    ) AS `t0` ON (`f20`.`Discriminator` = 'LocustHorde') AND (`f20`.`CommanderName` = `t0`.`Name`)
//    CROSS JOIN `Factions` AS `ff0`
//    LEFT JOIN `Cities` AS [ff.Capital0] ON `ff0`.`CapitalName` = [ff.Capital0].`Name`
//    WHERE (`f20`.`Discriminator` = 'LocustHorde') AND (`f20`.`Discriminator` = 'LocustHorde')
//) AS `t1` ON [f2.Leaders].`LocustHordeId` = `t1`.`Id`
//WHERE [f2.Leaders].`Discriminator` IN ('LocustCommander', 'LocustLeader')
//ORDER BY `t1`.`Name`, `t1`.`Name0`, `t1`.`Id`");
//        }

        public override async Task Distinct_on_subquery_doesnt_get_lifted(bool isAsync)
        {
            await base.Distinct_on_subquery_doesnt_get_lifted(isAsync);

            AssertSql(
                $@"SELECT `t`.`HasSoulPatch`
FROM (
    SELECT DISTINCT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t`");
        }

        public override async Task Cast_result_operator_on_subquery_is_properly_lifted_to_a_convert(bool isAsync)
        {
            await base.Cast_result_operator_on_subquery_is_properly_lifted_to_a_convert(isAsync);

            AssertSql(
                $@"SELECT `f`.`Eradicated`
FROM `Factions` AS `f`
WHERE `f`.`Discriminator` = 'LocustHorde'");
        }

        public override async Task Comparing_two_collection_navigations_composite_key(bool isAsync)
        {
            await base.Comparing_two_collection_navigations_composite_key(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname` AS `Nickname1`, `t`.`Nickname` AS `Nickname2`
FROM `Gears` AS `g`,
(
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((`g`.`Nickname` = `t`.`Nickname`) AND (`g`.`SquadId` = `t`.`SquadId`))
ORDER BY `g`.`Nickname`");
        }

        public override async Task Comparing_two_collection_navigations_inheritance(bool isAsync)
        {
            await base.Comparing_two_collection_navigations_inheritance(isAsync);

            AssertSql(
                $@"SELECT `f`.`Name`, `t`.`Nickname`
FROM `Factions` AS `f`,
(
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Discriminator` = 'Officer')
) AS `t`
LEFT JOIN (
    SELECT `l`.`Name`, `l`.`Discriminator`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`DefeatedByNickname`, `l`.`DefeatedBySquadId`, `l`.`HighCommandId`
    FROM `LocustLeaders` AS `l`
    WHERE `l`.`Discriminator` = 'LocustCommander'
) AS `t0` ON `f`.`CommanderName` = `t0`.`Name`
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t1` ON (`t0`.`DefeatedByNickname` = `t1`.`Nickname`) AND (`t0`.`DefeatedBySquadId` = `t1`.`SquadId`)
WHERE ((`f`.`Discriminator` = 'LocustHorde') AND ((`f`.`Discriminator` = 'LocustHorde') AND (`t`.`HasSoulPatch` = True))) AND ((`t1`.`Nickname` = `t`.`Nickname`) AND (`t1`.`SquadId` = `t`.`SquadId`))");
        }

        public override async Task Comparing_entities_using_Equals_inheritance(bool isAsync)
        {
            await base.Comparing_entities_using_Equals_inheritance(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname` AS `Nickname1`, `t`.`Nickname` AS `Nickname2`
FROM `Gears` AS `g`,
(
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer') AND (`g0`.`Discriminator` = 'Officer')
) AS `t`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((`g`.`Nickname` = `t`.`Nickname`) AND (`g`.`SquadId` = `t`.`SquadId`))
ORDER BY `g`.`Nickname`, `t`.`Nickname`");
        }

        public override async Task Contains_on_nullable_array_produces_correct_sql(bool isAsync)
        {
            await base.Contains_on_nullable_array_produces_correct_sql(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
LEFT JOIN `Cities` AS `c` ON `g`.`AssignedCityName` = `c`.`Name`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((`g`.`SquadId` < 2) AND (`c`.`Name` IN ('Ephyra') OR `c`.`Name` IS NULL))");
        }

        public override async Task Optional_navigation_with_collection_composite_key(bool isAsync)
        {
            await base.Optional_navigation_with_collection_composite_key(isAsync);

            AssertSql(
                $@"SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`Note`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
WHERE (`t0`.`Discriminator` = 'Officer') AND ((
    SELECT COUNT(*)
    FROM `Gears` AS `g0`
    WHERE (`g0`.`Discriminator` IN ('Gear', 'Officer') AND (`t0`.`Nickname` IS NOT NULL AND ((`t0`.`Nickname` = `g0`.`LeaderNickname`) AND (`t0`.`SquadId` = `g0`.`LeaderSquadId`)))) AND (`g0`.`Nickname` = 'Dom')) > 0)");
        }

        public override async Task Select_null_conditional_with_inheritance(bool isAsync)
        {
            await base.Select_null_conditional_with_inheritance(isAsync);

            AssertSql(
                $@"SELECT CASE
    WHEN `f`.`CommanderName` IS NOT NULL THEN `f`.`CommanderName`
    ELSE NULL
END
FROM `Factions` AS `f`
WHERE (`f`.`Discriminator` = 'LocustHorde') AND (`f`.`Discriminator` = 'LocustHorde')");
        }

        public override async Task Select_null_conditional_with_inheritance_negative(bool isAsync)
        {
            await base.Select_null_conditional_with_inheritance_negative(isAsync);

            AssertSql(
                $@"SELECT CASE
    WHEN `f`.`CommanderName` IS NOT NULL THEN `f`.`Eradicated`
    ELSE NULL
END
FROM `Factions` AS `f`
WHERE (`f`.`Discriminator` = 'LocustHorde') AND (`f`.`Discriminator` = 'LocustHorde')");
        }

        public override async Task Project_collection_navigation_with_inheritance1(bool isAsync)
        {
            await base.Project_collection_navigation_with_inheritance1(isAsync);

            AssertSql(
                $@"SELECT `f`.`Id`, `t1`.`Name`, `t1`.`Discriminator`, `t1`.`LocustHordeId`, `t1`.`ThreatLevel`, `t1`.`DefeatedByNickname`, `t1`.`DefeatedBySquadId`, `t1`.`HighCommandId`
FROM `Factions` AS `f`
LEFT JOIN (
    SELECT `l`.`Name`, `l`.`Discriminator`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`DefeatedByNickname`, `l`.`DefeatedBySquadId`, `l`.`HighCommandId`
    FROM `LocustLeaders` AS `l`
    WHERE `l`.`Discriminator` = 'LocustCommander'
) AS `t` ON `f`.`CommanderName` = `t`.`Name`
LEFT JOIN (
    SELECT `f0`.`Id`, `f0`.`CapitalName`, `f0`.`Discriminator`, `f0`.`Name`, `f0`.`CommanderName`, `f0`.`Eradicated`
    FROM `Factions` AS `f0`
    WHERE `f0`.`Discriminator` = 'LocustHorde'
) AS `t0` ON `t`.`Name` = `t0`.`CommanderName`
LEFT JOIN (
    SELECT `l0`.`Name`, `l0`.`Discriminator`, `l0`.`LocustHordeId`, `l0`.`ThreatLevel`, `l0`.`DefeatedByNickname`, `l0`.`DefeatedBySquadId`, `l0`.`HighCommandId`
    FROM `LocustLeaders` AS `l0`
    WHERE `l0`.`Discriminator` IN ('LocustLeader', 'LocustCommander')
) AS `t1` ON `t0`.`Id` = `t1`.`LocustHordeId`
WHERE (`f`.`Discriminator` = 'LocustHorde') AND (`f`.`Discriminator` = 'LocustHorde')
ORDER BY `f`.`Id`, `t1`.`Name`");
        }

        public override async Task Project_collection_navigation_with_inheritance2(bool isAsync)
        {
            await base.Project_collection_navigation_with_inheritance2(isAsync);

            AssertSql(
                $@"SELECT `f`.`Id`, `t1`.`Nickname`, `t1`.`SquadId`, `t1`.`AssignedCityName`, `t1`.`CityOfBirthName`, `t1`.`Discriminator`, `t1`.`FullName`, `t1`.`HasSoulPatch`, `t1`.`LeaderNickname`, `t1`.`LeaderSquadId`, `t1`.`Rank`
FROM `Factions` AS `f`
LEFT JOIN (
    SELECT `l`.`Name`, `l`.`Discriminator`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`DefeatedByNickname`, `l`.`DefeatedBySquadId`, `l`.`HighCommandId`
    FROM `LocustLeaders` AS `l`
    WHERE `l`.`Discriminator` = 'LocustCommander'
) AS `t` ON `f`.`CommanderName` = `t`.`Name`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`DefeatedByNickname` = `t0`.`Nickname`) AND (`t`.`DefeatedBySquadId` = `t0`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t1` ON ((`t0`.`Nickname` = `t1`.`LeaderNickname`) OR (`t0`.`Nickname` IS NULL AND `t1`.`LeaderNickname` IS NULL)) AND (`t0`.`SquadId` = `t1`.`LeaderSquadId`)
WHERE (`f`.`Discriminator` = 'LocustHorde') AND (`f`.`Discriminator` = 'LocustHorde')
ORDER BY `f`.`Id`, `t1`.`Nickname`, `t1`.`SquadId`");
        }

        public override async Task Project_collection_navigation_with_inheritance3(bool isAsync)
        {
            await base.Project_collection_navigation_with_inheritance3(isAsync);

            AssertSql(
                $@"SELECT `f`.`Id`, `t1`.`Nickname`, `t1`.`SquadId`, `t1`.`AssignedCityName`, `t1`.`CityOfBirthName`, `t1`.`Discriminator`, `t1`.`FullName`, `t1`.`HasSoulPatch`, `t1`.`LeaderNickname`, `t1`.`LeaderSquadId`, `t1`.`Rank`
FROM `Factions` AS `f`
LEFT JOIN (
    SELECT `l`.`Name`, `l`.`Discriminator`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`DefeatedByNickname`, `l`.`DefeatedBySquadId`, `l`.`HighCommandId`
    FROM `LocustLeaders` AS `l`
    WHERE `l`.`Discriminator` = 'LocustCommander'
) AS `t` ON `f`.`CommanderName` = `t`.`Name`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`DefeatedByNickname` = `t0`.`Nickname`) AND (`t`.`DefeatedBySquadId` = `t0`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t1` ON ((`t0`.`Nickname` = `t1`.`LeaderNickname`) OR (`t0`.`Nickname` IS NULL AND `t1`.`LeaderNickname` IS NULL)) AND (`t0`.`SquadId` = `t1`.`LeaderSquadId`)
WHERE (`f`.`Discriminator` = 'LocustHorde') AND (`f`.`Discriminator` = 'LocustHorde')
ORDER BY `f`.`Id`, `t1`.`Nickname`, `t1`.`SquadId`");
        }

        public override async Task Include_reference_on_derived_type_using_string(bool isAsync)
        {
            await base.Include_reference_on_derived_type_using_string(isAsync);

            AssertSql(
                $@"SELECT `l`.`Name`, `l`.`Discriminator`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`DefeatedByNickname`, `l`.`DefeatedBySquadId`, `l`.`HighCommandId`, `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`Discriminator`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`
FROM `LocustLeaders` AS `l`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON (`l`.`DefeatedByNickname` = `t`.`Nickname`) AND (`l`.`DefeatedBySquadId` = `t`.`SquadId`)
WHERE `l`.`Discriminator` IN ('LocustLeader', 'LocustCommander')");
        }

        public override async Task Include_reference_on_derived_type_using_string_nested1(bool isAsync)
        {
            await base.Include_reference_on_derived_type_using_string_nested1(isAsync);

            AssertSql(
                $@"SELECT `l`.`Name`, `l`.`Discriminator`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`DefeatedByNickname`, `l`.`DefeatedBySquadId`, `l`.`HighCommandId`, `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`Discriminator`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`, `s`.`Id`, `s`.`InternalNumber`, `s`.`Name`
FROM `LocustLeaders` AS `l`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON (`l`.`DefeatedByNickname` = `t`.`Nickname`) AND (`l`.`DefeatedBySquadId` = `t`.`SquadId`)
LEFT JOIN `Squads` AS `s` ON `t`.`SquadId` = `s`.`Id`
WHERE `l`.`Discriminator` IN ('LocustLeader', 'LocustCommander')");
        }

        public override async Task Include_reference_on_derived_type_using_string_nested2(bool isAsync)
        {
            await base.Include_reference_on_derived_type_using_string_nested2(isAsync);

            AssertSql(
                $@"SELECT `l`.`Name`, `l`.`Discriminator`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`DefeatedByNickname`, `l`.`DefeatedBySquadId`, `l`.`HighCommandId`, `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`Discriminator`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`, `t0`.`Nickname`, `t0`.`SquadId`, `t0`.`AssignedCityName`, `t0`.`CityOfBirthName`, `t0`.`Discriminator`, `t0`.`FullName`, `t0`.`HasSoulPatch`, `t0`.`LeaderNickname`, `t0`.`LeaderSquadId`, `t0`.`Rank`, `t0`.`Name`, `t0`.`Location`, `t0`.`Nation`
FROM `LocustLeaders` AS `l`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON (`l`.`DefeatedByNickname` = `t`.`Nickname`) AND (`l`.`DefeatedBySquadId` = `t`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, `c`.`Name`, `c`.`Location`, `c`.`Nation`
    FROM `Gears` AS `g0`
    INNER JOIN `Cities` AS `c` ON `g0`.`CityOfBirthName` = `c`.`Name`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON ((`t`.`Nickname` = `t0`.`LeaderNickname`) OR (`t`.`Nickname` IS NULL AND `t0`.`LeaderNickname` IS NULL)) AND (`t`.`SquadId` = `t0`.`LeaderSquadId`)
WHERE `l`.`Discriminator` IN ('LocustLeader', 'LocustCommander')
ORDER BY `l`.`Name`, `t0`.`Nickname`, `t0`.`SquadId`, `t0`.`Name`");
        }

        public override async Task Include_reference_on_derived_type_using_lambda(bool isAsync)
        {
            await base.Include_reference_on_derived_type_using_lambda(isAsync);

            AssertSql(
                $@"SELECT `l`.`Name`, `l`.`Discriminator`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`DefeatedByNickname`, `l`.`DefeatedBySquadId`, `l`.`HighCommandId`, `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`Discriminator`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`
FROM `LocustLeaders` AS `l`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON (`l`.`DefeatedByNickname` = `t`.`Nickname`) AND (`l`.`DefeatedBySquadId` = `t`.`SquadId`)
WHERE `l`.`Discriminator` IN ('LocustLeader', 'LocustCommander')");
        }

        public override async Task Include_reference_on_derived_type_using_lambda_with_soft_cast(bool isAsync)
        {
            await base.Include_reference_on_derived_type_using_lambda_with_soft_cast(isAsync);

            AssertSql(
                $@"SELECT `l`.`Name`, `l`.`Discriminator`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`DefeatedByNickname`, `l`.`DefeatedBySquadId`, `l`.`HighCommandId`, `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`Discriminator`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`
FROM `LocustLeaders` AS `l`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON (`l`.`DefeatedByNickname` = `t`.`Nickname`) AND (`l`.`DefeatedBySquadId` = `t`.`SquadId`)
WHERE `l`.`Discriminator` IN ('LocustLeader', 'LocustCommander')");
        }

        public override async Task Include_reference_on_derived_type_using_lambda_with_tracking(bool isAsync)
        {
            await base.Include_reference_on_derived_type_using_lambda_with_tracking(isAsync);

            AssertSql(
                $@"SELECT `l`.`Name`, `l`.`Discriminator`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`DefeatedByNickname`, `l`.`DefeatedBySquadId`, `l`.`HighCommandId`, `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`Discriminator`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`
FROM `LocustLeaders` AS `l`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON (`l`.`DefeatedByNickname` = `t`.`Nickname`) AND (`l`.`DefeatedBySquadId` = `t`.`SquadId`)
WHERE `l`.`Discriminator` IN ('LocustLeader', 'LocustCommander')");
        }

        public override async Task Include_collection_on_derived_type_using_string(bool isAsync)
        {
            await base.Include_collection_on_derived_type_using_string(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`Discriminator`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON (`g`.`Nickname` = `t`.`LeaderNickname`) AND (`g`.`SquadId` = `t`.`LeaderSquadId`)
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `t`.`Nickname`, `t`.`SquadId`");
        }

        public override async Task Include_collection_on_derived_type_using_lambda(bool isAsync)
        {
            await base.Include_collection_on_derived_type_using_lambda(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`Discriminator`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON (`g`.`Nickname` = `t`.`LeaderNickname`) AND (`g`.`SquadId` = `t`.`LeaderSquadId`)
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `t`.`Nickname`, `t`.`SquadId`");
        }

        public override async Task Include_collection_on_derived_type_using_lambda_with_soft_cast(bool isAsync)
        {
            await base.Include_collection_on_derived_type_using_lambda_with_soft_cast(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`Discriminator`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON (`g`.`Nickname` = `t`.`LeaderNickname`) AND (`g`.`SquadId` = `t`.`LeaderSquadId`)
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `t`.`Nickname`, `t`.`SquadId`");
        }

        public override async Task Include_base_navigation_on_derived_entity(bool isAsync)
        {
            await base.Include_base_navigation_on_derived_entity(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`Note`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Gears` AS `g`
LEFT JOIN `Tags` AS `t` ON (`g`.`Nickname` = `t`.`GearNickName`) AND (`g`.`SquadId` = `t`.`GearSquadId`)
LEFT JOIN `Weapons` AS `w` ON `g`.`FullName` = `w`.`OwnerFullName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `w`.`Id`");
        }

        public override async Task ThenInclude_collection_on_derived_after_base_reference(bool isAsync)
        {
            await base.ThenInclude_collection_on_derived_after_base_reference(isAsync);

            AssertSql(
                $@"SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`Note`, `t0`.`Nickname`, `t0`.`SquadId`, `t0`.`AssignedCityName`, `t0`.`CityOfBirthName`, `t0`.`Discriminator`, `t0`.`FullName`, `t0`.`HasSoulPatch`, `t0`.`LeaderNickname`, `t0`.`LeaderSquadId`, `t0`.`Rank`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
LEFT JOIN `Weapons` AS `w` ON `t0`.`FullName` = `w`.`OwnerFullName`
ORDER BY `t`.`Id`, `w`.`Id`");
        }

        public override async Task ThenInclude_collection_on_derived_after_derived_reference(bool isAsync)
        {
            await base.ThenInclude_collection_on_derived_after_derived_reference(isAsync);

            AssertSql(
                $@"SELECT `f`.`Id`, `f`.`CapitalName`, `f`.`Discriminator`, `f`.`Name`, `f`.`CommanderName`, `f`.`Eradicated`, `t`.`Name`, `t`.`Discriminator`, `t`.`LocustHordeId`, `t`.`ThreatLevel`, `t`.`DefeatedByNickname`, `t`.`DefeatedBySquadId`, `t`.`HighCommandId`, `t0`.`Nickname`, `t0`.`SquadId`, `t0`.`AssignedCityName`, `t0`.`CityOfBirthName`, `t0`.`Discriminator`, `t0`.`FullName`, `t0`.`HasSoulPatch`, `t0`.`LeaderNickname`, `t0`.`LeaderSquadId`, `t0`.`Rank`, `t1`.`Nickname`, `t1`.`SquadId`, `t1`.`AssignedCityName`, `t1`.`CityOfBirthName`, `t1`.`Discriminator`, `t1`.`FullName`, `t1`.`HasSoulPatch`, `t1`.`LeaderNickname`, `t1`.`LeaderSquadId`, `t1`.`Rank`
FROM `Factions` AS `f`
LEFT JOIN (
    SELECT `l`.`Name`, `l`.`Discriminator`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`DefeatedByNickname`, `l`.`DefeatedBySquadId`, `l`.`HighCommandId`
    FROM `LocustLeaders` AS `l`
    WHERE `l`.`Discriminator` = 'LocustCommander'
) AS `t` ON `f`.`CommanderName` = `t`.`Name`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`DefeatedByNickname` = `t0`.`Nickname`) AND (`t`.`DefeatedBySquadId` = `t0`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t1` ON ((`t0`.`Nickname` = `t1`.`LeaderNickname`) OR (`t0`.`Nickname` IS NULL AND `t1`.`LeaderNickname` IS NULL)) AND (`t0`.`SquadId` = `t1`.`LeaderSquadId`)
WHERE `f`.`Discriminator` = 'LocustHorde'
ORDER BY `f`.`Id`, `t1`.`Nickname`, `t1`.`SquadId`");
        }

        public override async Task ThenInclude_collection_on_derived_after_derived_collection(bool isAsync)
        {
            await base.ThenInclude_collection_on_derived_after_derived_collection(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, `t0`.`Nickname`, `t0`.`SquadId`, `t0`.`AssignedCityName`, `t0`.`CityOfBirthName`, `t0`.`Discriminator`, `t0`.`FullName`, `t0`.`HasSoulPatch`, `t0`.`LeaderNickname`, `t0`.`LeaderSquadId`, `t0`.`Rank`, `t0`.`Nickname0`, `t0`.`SquadId0`, `t0`.`AssignedCityName0`, `t0`.`CityOfBirthName0`, `t0`.`Discriminator0`, `t0`.`FullName0`, `t0`.`HasSoulPatch0`, `t0`.`LeaderNickname0`, `t0`.`LeaderSquadId0`, `t0`.`Rank0`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, `t`.`Nickname` AS `Nickname0`, `t`.`SquadId` AS `SquadId0`, `t`.`AssignedCityName` AS `AssignedCityName0`, `t`.`CityOfBirthName` AS `CityOfBirthName0`, `t`.`Discriminator` AS `Discriminator0`, `t`.`FullName` AS `FullName0`, `t`.`HasSoulPatch` AS `HasSoulPatch0`, `t`.`LeaderNickname` AS `LeaderNickname0`, `t`.`LeaderSquadId` AS `LeaderSquadId0`, `t`.`Rank` AS `Rank0`
    FROM `Gears` AS `g0`
    LEFT JOIN (
        SELECT `g1`.`Nickname`, `g1`.`SquadId`, `g1`.`AssignedCityName`, `g1`.`CityOfBirthName`, `g1`.`Discriminator`, `g1`.`FullName`, `g1`.`HasSoulPatch`, `g1`.`LeaderNickname`, `g1`.`LeaderSquadId`, `g1`.`Rank`
        FROM `Gears` AS `g1`
        WHERE `g1`.`Discriminator` IN ('Gear', 'Officer')
    ) AS `t` ON (`g0`.`Nickname` = `t`.`LeaderNickname`) AND (`g0`.`SquadId` = `t`.`LeaderSquadId`)
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`g`.`Nickname` = `t0`.`LeaderNickname`) AND (`g`.`SquadId` = `t0`.`LeaderSquadId`)
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `t0`.`Nickname`, `t0`.`SquadId`, `t0`.`Nickname0`, `t0`.`SquadId0`");
        }

        public override async Task ThenInclude_reference_on_derived_after_derived_collection(bool isAsync)
        {
            await base.ThenInclude_reference_on_derived_after_derived_collection(isAsync);

            AssertSql(
                $@"SELECT `f`.`Id`, `f`.`CapitalName`, `f`.`Discriminator`, `f`.`Name`, `f`.`CommanderName`, `f`.`Eradicated`, `t0`.`Name`, `t0`.`Discriminator`, `t0`.`LocustHordeId`, `t0`.`ThreatLevel`, `t0`.`DefeatedByNickname`, `t0`.`DefeatedBySquadId`, `t0`.`HighCommandId`, `t0`.`Nickname`, `t0`.`SquadId`, `t0`.`AssignedCityName`, `t0`.`CityOfBirthName`, `t0`.`Discriminator0`, `t0`.`FullName`, `t0`.`HasSoulPatch`, `t0`.`LeaderNickname`, `t0`.`LeaderSquadId`, `t0`.`Rank`
FROM `Factions` AS `f`
LEFT JOIN (
    SELECT `l`.`Name`, `l`.`Discriminator`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`DefeatedByNickname`, `l`.`DefeatedBySquadId`, `l`.`HighCommandId`, `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`Discriminator` AS `Discriminator0`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`
    FROM `LocustLeaders` AS `l`
    LEFT JOIN (
        SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
        FROM `Gears` AS `g`
        WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
    ) AS `t` ON (`l`.`DefeatedByNickname` = `t`.`Nickname`) AND (`l`.`DefeatedBySquadId` = `t`.`SquadId`)
    WHERE `l`.`Discriminator` IN ('LocustLeader', 'LocustCommander')
) AS `t0` ON `f`.`Id` = `t0`.`LocustHordeId`
WHERE `f`.`Discriminator` = 'LocustHorde'
ORDER BY `f`.`Id`, `t0`.`Name`");
        }

        public override async Task Multiple_derived_included_on_one_method(bool isAsync)
        {
            await base.Multiple_derived_included_on_one_method(isAsync);

            AssertSql(
                $@"SELECT `f`.`Id`, `f`.`CapitalName`, `f`.`Discriminator`, `f`.`Name`, `f`.`CommanderName`, `f`.`Eradicated`, `t`.`Name`, `t`.`Discriminator`, `t`.`LocustHordeId`, `t`.`ThreatLevel`, `t`.`DefeatedByNickname`, `t`.`DefeatedBySquadId`, `t`.`HighCommandId`, `t0`.`Nickname`, `t0`.`SquadId`, `t0`.`AssignedCityName`, `t0`.`CityOfBirthName`, `t0`.`Discriminator`, `t0`.`FullName`, `t0`.`HasSoulPatch`, `t0`.`LeaderNickname`, `t0`.`LeaderSquadId`, `t0`.`Rank`, `t1`.`Nickname`, `t1`.`SquadId`, `t1`.`AssignedCityName`, `t1`.`CityOfBirthName`, `t1`.`Discriminator`, `t1`.`FullName`, `t1`.`HasSoulPatch`, `t1`.`LeaderNickname`, `t1`.`LeaderSquadId`, `t1`.`Rank`
FROM `Factions` AS `f`
LEFT JOIN (
    SELECT `l`.`Name`, `l`.`Discriminator`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`DefeatedByNickname`, `l`.`DefeatedBySquadId`, `l`.`HighCommandId`
    FROM `LocustLeaders` AS `l`
    WHERE `l`.`Discriminator` = 'LocustCommander'
) AS `t` ON `f`.`CommanderName` = `t`.`Name`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`DefeatedByNickname` = `t0`.`Nickname`) AND (`t`.`DefeatedBySquadId` = `t0`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t1` ON ((`t0`.`Nickname` = `t1`.`LeaderNickname`) OR (`t0`.`Nickname` IS NULL AND `t1`.`LeaderNickname` IS NULL)) AND (`t0`.`SquadId` = `t1`.`LeaderSquadId`)
WHERE `f`.`Discriminator` = 'LocustHorde'
ORDER BY `f`.`Id`, `t1`.`Nickname`, `t1`.`SquadId`");
        }

        public override async Task Include_on_derived_multi_level(bool isAsync)
        {
            await base.Include_on_derived_multi_level(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`Discriminator`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`, `t`.`Id`, `t`.`InternalNumber`, `t`.`Name`, `t`.`SquadId0`, `t`.`MissionId`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`, `s`.`Id`, `s`.`InternalNumber`, `s`.`Name`, `s0`.`SquadId` AS `SquadId0`, `s0`.`MissionId`
    FROM `Gears` AS `g0`
    INNER JOIN `Squads` AS `s` ON `g0`.`SquadId` = `s`.`Id`
    LEFT JOIN `SquadMissions` AS `s0` ON `s`.`Id` = `s0`.`SquadId`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON (`g`.`Nickname` = `t`.`LeaderNickname`) AND (`g`.`SquadId` = `t`.`LeaderSquadId`)
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `t`.`Nickname`, `t`.`SquadId`, `t`.`Id`, `t`.`SquadId0`, `t`.`MissionId`");
        }

        public override async Task Projecting_nullable_bool_in_conditional_works(bool isAsync)
        {
            await base.Projecting_nullable_bool_in_conditional_works(isAsync);

            AssertSql(
                $@"SELECT CASE
    WHEN `t0`.`Nickname` IS NOT NULL THEN `t0`.`HasSoulPatch`
    ELSE False
END AS `Prop`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)");
        }

        public override async Task Enum_ToString_is_client_eval(bool isAsync)
        {
            await base.Enum_ToString_is_client_eval(isAsync);

            AssertSql(
                $@"SELECT `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
ORDER BY `g`.`SquadId`, `g`.`Nickname`");
        }

        public override async Task Correlated_collections_naked_navigation_with_ToList(bool isAsync)
        {
            await base.Correlated_collections_naked_navigation_with_ToList(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Gears` AS `g`
LEFT JOIN `Weapons` AS `w` ON `g`.`FullName` = `w`.`OwnerFullName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Nickname` <> 'Marcus')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `w`.`Id`");
        }

        public override async Task Correlated_collections_naked_navigation_with_ToList_followed_by_projecting_count(bool isAsync)
        {
            await base.Correlated_collections_naked_navigation_with_ToList_followed_by_projecting_count(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Gears` AS `g`
LEFT JOIN `Weapons` AS `w` ON `g`.`FullName` = `w`.`OwnerFullName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Nickname` <> 'Marcus')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `w`.`Id`");
        }

        public override async Task Correlated_collections_naked_navigation_with_ToArray(bool isAsync)
        {
            await base.Correlated_collections_naked_navigation_with_ToArray(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Gears` AS `g`
LEFT JOIN `Weapons` AS `w` ON `g`.`FullName` = `w`.`OwnerFullName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Nickname` <> 'Marcus')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `w`.`Id`");
        }

        public override async Task Correlated_collections_basic_projection(bool isAsync)
        {
            await base.Correlated_collections_basic_projection(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `t`.`Id`, `t`.`AmmunitionType`, `t`.`IsAutomatic`, `t`.`Name`, `t`.`OwnerFullName`, `t`.`SynergyWithId`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
    FROM `Weapons` AS `w`
    WHERE (`w`.`IsAutomatic` = True) OR ((`w`.`Name` <> 'foo') OR `w`.`Name` IS NULL)
) AS `t` ON `g`.`FullName` = `t`.`OwnerFullName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Nickname` <> 'Marcus')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `t`.`Id`");
        }

        public override async Task Correlated_collections_basic_projection_explicit_to_list(bool isAsync)
        {
            await base.Correlated_collections_basic_projection_explicit_to_list(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `t`.`Id`, `t`.`AmmunitionType`, `t`.`IsAutomatic`, `t`.`Name`, `t`.`OwnerFullName`, `t`.`SynergyWithId`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
    FROM `Weapons` AS `w`
    WHERE (`w`.`IsAutomatic` = True) OR ((`w`.`Name` <> 'foo') OR `w`.`Name` IS NULL)
) AS `t` ON `g`.`FullName` = `t`.`OwnerFullName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Nickname` <> 'Marcus')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `t`.`Id`");
        }

        public override async Task Correlated_collections_basic_projection_explicit_to_array(bool isAsync)
        {
            await base.Correlated_collections_basic_projection_explicit_to_array(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `t`.`Id`, `t`.`AmmunitionType`, `t`.`IsAutomatic`, `t`.`Name`, `t`.`OwnerFullName`, `t`.`SynergyWithId`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
    FROM `Weapons` AS `w`
    WHERE (`w`.`IsAutomatic` = True) OR ((`w`.`Name` <> 'foo') OR `w`.`Name` IS NULL)
) AS `t` ON `g`.`FullName` = `t`.`OwnerFullName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Nickname` <> 'Marcus')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `t`.`Id`");
        }

        public override async Task Correlated_collections_basic_projection_ordered(bool isAsync)
        {
            await base.Correlated_collections_basic_projection_ordered(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `t`.`Id`, `t`.`AmmunitionType`, `t`.`IsAutomatic`, `t`.`Name`, `t`.`OwnerFullName`, `t`.`SynergyWithId`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
    FROM `Weapons` AS `w`
    WHERE (`w`.`IsAutomatic` = True) OR ((`w`.`Name` <> 'foo') OR `w`.`Name` IS NULL)
) AS `t` ON `g`.`FullName` = `t`.`OwnerFullName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Nickname` <> 'Marcus')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `t`.`Name` DESC, `t`.`Id`");
        }

        public override async Task Correlated_collections_basic_projection_composite_key(bool isAsync)
        {
            await base.Correlated_collections_basic_projection_composite_key(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `t`.`Nickname`, `t`.`FullName`, `t`.`SquadId`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`FullName`, `g0`.`SquadId`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer') AND (`g0`.`HasSoulPatch` <> True)
) AS `t` ON (`g`.`Nickname` = `t`.`LeaderNickname`) AND (`g`.`SquadId` = `t`.`LeaderSquadId`)
WHERE (`g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Discriminator` = 'Officer')) AND (`g`.`Nickname` <> 'Foo')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `t`.`Nickname`, `t`.`SquadId`");
        }

        public override async Task Correlated_collections_basic_projecting_single_property(bool isAsync)
        {
            await base.Correlated_collections_basic_projecting_single_property(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `t`.`Name`, `t`.`Id`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `w`.`Name`, `w`.`Id`, `w`.`OwnerFullName`
    FROM `Weapons` AS `w`
    WHERE (`w`.`IsAutomatic` = True) OR ((`w`.`Name` <> 'foo') OR `w`.`Name` IS NULL)
) AS `t` ON `g`.`FullName` = `t`.`OwnerFullName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Nickname` <> 'Marcus')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `t`.`Id`");
        }

        public override async Task Correlated_collections_basic_projecting_constant(bool isAsync)
        {
            await base.Correlated_collections_basic_projecting_constant(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `t`.`c`, `t`.`Id`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT 'BFG' AS `c`, `w`.`Id`, `w`.`OwnerFullName`
    FROM `Weapons` AS `w`
    WHERE (`w`.`IsAutomatic` = True) OR ((`w`.`Name` <> 'foo') OR `w`.`Name` IS NULL)
) AS `t` ON `g`.`FullName` = `t`.`OwnerFullName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Nickname` <> 'Marcus')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `t`.`Id`");
        }

        public override async Task Correlated_collections_basic_projecting_constant_bool(bool isAsync)
        {
            await base.Correlated_collections_basic_projecting_constant_bool(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `t`.`c`, `t`.`Id`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT True AS `c`, `w`.`Id`, `w`.`OwnerFullName`
    FROM `Weapons` AS `w`
    WHERE (`w`.`IsAutomatic` = True) OR ((`w`.`Name` <> 'foo') OR `w`.`Name` IS NULL)
) AS `t` ON `g`.`FullName` = `t`.`OwnerFullName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Nickname` <> 'Marcus')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `t`.`Id`");
        }

        public override async Task Correlated_collections_projection_of_collection_thru_navigation(bool isAsync)
        {
            await base.Correlated_collections_projection_of_collection_thru_navigation(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `s`.`Id`, `t`.`SquadId`, `t`.`MissionId`
FROM `Gears` AS `g`
INNER JOIN `Squads` AS `s` ON `g`.`SquadId` = `s`.`Id`
LEFT JOIN (
    SELECT `s0`.`SquadId`, `s0`.`MissionId`
    FROM `SquadMissions` AS `s0`
    WHERE `s0`.`MissionId` <> 17
) AS `t` ON `s`.`Id` = `t`.`SquadId`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Nickname` <> 'Marcus')
ORDER BY `g`.`FullName`, `g`.`Nickname`, `g`.`SquadId`, `s`.`Id`, `t`.`SquadId`, `t`.`MissionId`");
        }

        public override async Task Correlated_collections_project_anonymous_collection_result(bool isAsync)
        {
            await base.Correlated_collections_project_anonymous_collection_result(isAsync);

            AssertSql(
                $@"SELECT `s`.`Name`, `s`.`Id`, `t`.`FullName`, `t`.`Rank`, `t`.`Nickname`, `t`.`SquadId`
FROM `Squads` AS `s`
LEFT JOIN (
    SELECT `g`.`FullName`, `g`.`Rank`, `g`.`Nickname`, `g`.`SquadId`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON `s`.`Id` = `t`.`SquadId`
WHERE `s`.`Id` < 20
ORDER BY `s`.`Id`, `t`.`Nickname`, `t`.`SquadId`");
        }

        public override async Task Correlated_collections_nested(bool isAsync)
        {
            await base.Correlated_collections_nested(isAsync);

            AssertSql(
                $@"SELECT `s`.`Id`, `t0`.`SquadId`, `t0`.`MissionId`, `t0`.`Id`, `t0`.`SquadId0`, `t0`.`MissionId0`
FROM `Squads` AS `s`
LEFT JOIN (
    SELECT `s0`.`SquadId`, `s0`.`MissionId`, `m`.`Id`, `t`.`SquadId` AS `SquadId0`, `t`.`MissionId` AS `MissionId0`
    FROM `SquadMissions` AS `s0`
    INNER JOIN `Missions` AS `m` ON `s0`.`MissionId` = `m`.`Id`
    LEFT JOIN (
        SELECT `s1`.`SquadId`, `s1`.`MissionId`
        FROM `SquadMissions` AS `s1`
        WHERE `s1`.`SquadId` < 7
    ) AS `t` ON `m`.`Id` = `t`.`MissionId`
    WHERE `s0`.`MissionId` < 42
) AS `t0` ON `s`.`Id` = `t0`.`SquadId`
ORDER BY `s`.`Id`, `t0`.`SquadId`, `t0`.`MissionId`, `t0`.`Id`, `t0`.`SquadId0`, `t0`.`MissionId0`");
        }

        public override async Task Correlated_collections_nested_mixed_streaming_with_buffer1(bool isAsync)
        {
            await base.Correlated_collections_nested_mixed_streaming_with_buffer1(isAsync);

            AssertSql(
                $@"SELECT `s`.`Id`, `t0`.`SquadId`, `t0`.`MissionId`, `t0`.`Id`, `t0`.`SquadId0`, `t0`.`MissionId0`
FROM `Squads` AS `s`
LEFT JOIN (
    SELECT `s0`.`SquadId`, `s0`.`MissionId`, `m`.`Id`, `t`.`SquadId` AS `SquadId0`, `t`.`MissionId` AS `MissionId0`
    FROM `SquadMissions` AS `s0`
    INNER JOIN `Missions` AS `m` ON `s0`.`MissionId` = `m`.`Id`
    LEFT JOIN (
        SELECT `s1`.`SquadId`, `s1`.`MissionId`
        FROM `SquadMissions` AS `s1`
        WHERE `s1`.`SquadId` < 2
    ) AS `t` ON `m`.`Id` = `t`.`MissionId`
    WHERE `s0`.`MissionId` < 3
) AS `t0` ON `s`.`Id` = `t0`.`SquadId`
ORDER BY `s`.`Id`, `t0`.`SquadId`, `t0`.`MissionId`, `t0`.`Id`, `t0`.`SquadId0`, `t0`.`MissionId0`");
        }

        public override async Task Correlated_collections_nested_mixed_streaming_with_buffer2(bool isAsync)
        {
            await base.Correlated_collections_nested_mixed_streaming_with_buffer2(isAsync);

            AssertSql(
                $@"SELECT `s`.`Id`, `t0`.`SquadId`, `t0`.`MissionId`, `t0`.`Id`, `t0`.`SquadId0`, `t0`.`MissionId0`
FROM `Squads` AS `s`
LEFT JOIN (
    SELECT `s0`.`SquadId`, `s0`.`MissionId`, `m`.`Id`, `t`.`SquadId` AS `SquadId0`, `t`.`MissionId` AS `MissionId0`
    FROM `SquadMissions` AS `s0`
    INNER JOIN `Missions` AS `m` ON `s0`.`MissionId` = `m`.`Id`
    LEFT JOIN (
        SELECT `s1`.`SquadId`, `s1`.`MissionId`
        FROM `SquadMissions` AS `s1`
        WHERE `s1`.`SquadId` < 7
    ) AS `t` ON `m`.`Id` = `t`.`MissionId`
    WHERE `s0`.`MissionId` < 42
) AS `t0` ON `s`.`Id` = `t0`.`SquadId`
ORDER BY `s`.`Id`, `t0`.`SquadId`, `t0`.`MissionId`, `t0`.`Id`, `t0`.`SquadId0`, `t0`.`MissionId0`");
        }

        public override async Task Correlated_collections_nested_with_custom_ordering(bool isAsync)
        {
            await base.Correlated_collections_nested_with_custom_ordering(isAsync);

            AssertSql(
                $@"SELECT `g`.`FullName`, `g`.`Nickname`, `g`.`SquadId`, `t0`.`FullName`, `t0`.`Nickname`, `t0`.`SquadId`, `t0`.`Id`, `t0`.`AmmunitionType`, `t0`.`IsAutomatic`, `t0`.`Name`, `t0`.`OwnerFullName`, `t0`.`SynergyWithId`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `g0`.`FullName`, `g0`.`Nickname`, `g0`.`SquadId`, `t`.`Id`, `t`.`AmmunitionType`, `t`.`IsAutomatic`, `t`.`Name`, `t`.`OwnerFullName`, `t`.`SynergyWithId`, `g0`.`Rank`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`
    FROM `Gears` AS `g0`
    LEFT JOIN (
        SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
        FROM `Weapons` AS `w`
        WHERE (`w`.`Name` <> 'Bar') OR `w`.`Name` IS NULL
    ) AS `t` ON `g0`.`FullName` = `t`.`OwnerFullName`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer') AND (`g0`.`FullName` <> 'Foo')
) AS `t0` ON (`g`.`Nickname` = `t0`.`LeaderNickname`) AND (`g`.`SquadId` = `t0`.`LeaderSquadId`)
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Discriminator` = 'Officer')
ORDER BY `g`.`HasSoulPatch` DESC, `g`.`Nickname`, `g`.`SquadId`, `t0`.`Rank`, `t0`.`Nickname`, `t0`.`SquadId`, `t0`.`IsAutomatic`, `t0`.`Id`");
        }

        public override async Task Correlated_collections_same_collection_projected_multiple_times(bool isAsync)
        {
            await base.Correlated_collections_same_collection_projected_multiple_times(isAsync);

            AssertSql(
                $@"SELECT `g`.`FullName`, `g`.`Nickname`, `g`.`SquadId`, `t`.`Id`, `t`.`AmmunitionType`, `t`.`IsAutomatic`, `t`.`Name`, `t`.`OwnerFullName`, `t`.`SynergyWithId`, `t0`.`Id`, `t0`.`AmmunitionType`, `t0`.`IsAutomatic`, `t0`.`Name`, `t0`.`OwnerFullName`, `t0`.`SynergyWithId`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
    FROM `Weapons` AS `w`
    WHERE `w`.`IsAutomatic` = True
) AS `t` ON `g`.`FullName` = `t`.`OwnerFullName`
LEFT JOIN (
    SELECT `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
    FROM `Weapons` AS `w0`
    WHERE `w0`.`IsAutomatic` = True
) AS `t0` ON `g`.`FullName` = `t0`.`OwnerFullName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `t`.`Id`, `t0`.`Id`");
        }

        public override async Task Correlated_collections_similar_collection_projected_multiple_times(bool isAsync)
        {
            await base.Correlated_collections_similar_collection_projected_multiple_times(isAsync);

            AssertSql(
                $@"SELECT `g`.`FullName`, `g`.`Nickname`, `g`.`SquadId`, `t`.`Id`, `t`.`AmmunitionType`, `t`.`IsAutomatic`, `t`.`Name`, `t`.`OwnerFullName`, `t`.`SynergyWithId`, `t0`.`Id`, `t0`.`AmmunitionType`, `t0`.`IsAutomatic`, `t0`.`Name`, `t0`.`OwnerFullName`, `t0`.`SynergyWithId`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
    FROM `Weapons` AS `w`
    WHERE `w`.`IsAutomatic` = True
) AS `t` ON `g`.`FullName` = `t`.`OwnerFullName`
LEFT JOIN (
    SELECT `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
    FROM `Weapons` AS `w0`
    WHERE `w0`.`IsAutomatic` <> True
) AS `t0` ON `g`.`FullName` = `t0`.`OwnerFullName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
ORDER BY `g`.`Rank`, `g`.`Nickname`, `g`.`SquadId`, `t`.`OwnerFullName`, `t`.`Id`, `t0`.`IsAutomatic`, `t0`.`Id`");
        }

        public override async Task Correlated_collections_different_collections_projected(bool isAsync)
        {
            await base.Correlated_collections_different_collections_projected(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `t`.`Name`, `t`.`IsAutomatic`, `t`.`Id`, `t0`.`Nickname`, `t0`.`Rank`, `t0`.`SquadId`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `w`.`Name`, `w`.`IsAutomatic`, `w`.`Id`, `w`.`OwnerFullName`
    FROM `Weapons` AS `w`
    WHERE `w`.`IsAutomatic` = True
) AS `t` ON `g`.`FullName` = `t`.`OwnerFullName`
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`Rank`, `g0`.`SquadId`, `g0`.`FullName`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`g`.`Nickname` = `t0`.`LeaderNickname`) AND (`g`.`SquadId` = `t0`.`LeaderSquadId`)
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Discriminator` = 'Officer')
ORDER BY `g`.`FullName`, `g`.`Nickname`, `g`.`SquadId`, `t`.`Id`, `t0`.`FullName`, `t0`.`Nickname`, `t0`.`SquadId`");
        }

        public override async Task Multiple_orderby_with_navigation_expansion_on_one_of_the_order_bys(bool isAsync)
        {
            await base.Multiple_orderby_with_navigation_expansion_on_one_of_the_order_bys(isAsync);

            AssertSql(
                $@"SELECT `g`.`FullName`
FROM `Gears` AS `g`
LEFT JOIN `Tags` AS `t` ON (`g`.`Nickname` = `t`.`GearNickName`) AND (`g`.`SquadId` = `t`.`GearSquadId`)
WHERE (`g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Discriminator` = 'Officer')) AND EXISTS (
    SELECT 1
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer') AND ((`g`.`Nickname` = `g0`.`LeaderNickname`) AND (`g`.`SquadId` = `g0`.`LeaderSquadId`)))
ORDER BY `g`.`HasSoulPatch` DESC, `t`.`Note`");
        }

        public override async Task Multiple_orderby_with_navigation_expansion_on_one_of_the_order_bys_inside_subquery(bool isAsync)
        {
            await base.Multiple_orderby_with_navigation_expansion_on_one_of_the_order_bys_inside_subquery(isAsync);

            AssertSql(
                $@"SELECT `g`.`FullName`, `g`.`Nickname`, `g`.`SquadId`, `t2`.`Id`, `t2`.`AmmunitionType`, `t2`.`IsAutomatic`, `t2`.`Name`, `t2`.`OwnerFullName`, `t2`.`SynergyWithId`
FROM `Gears` AS `g`
LEFT JOIN `Tags` AS `t` ON (`g`.`Nickname` = `t`.`GearNickName`) AND (`g`.`SquadId` = `t`.`GearSquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
LEFT JOIN (
    SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`, `t1`.`Nickname`
    FROM `Weapons` AS `w`
    LEFT JOIN (
        SELECT `g1`.`Nickname`, `g1`.`SquadId`, `g1`.`AssignedCityName`, `g1`.`CityOfBirthName`, `g1`.`Discriminator`, `g1`.`FullName`, `g1`.`HasSoulPatch`, `g1`.`LeaderNickname`, `g1`.`LeaderSquadId`, `g1`.`Rank`
        FROM `Gears` AS `g1`
        WHERE `g1`.`Discriminator` IN ('Gear', 'Officer')
    ) AS `t1` ON `w`.`OwnerFullName` = `t1`.`FullName`
) AS `t2` ON `t0`.`FullName` = `t2`.`OwnerFullName`
WHERE (`g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Discriminator` = 'Officer')) AND EXISTS (
    SELECT 1
    FROM `Gears` AS `g2`
    WHERE `g2`.`Discriminator` IN ('Gear', 'Officer') AND ((`g`.`Nickname` = `g2`.`LeaderNickname`) AND (`g`.`SquadId` = `g2`.`LeaderSquadId`)))
ORDER BY `g`.`HasSoulPatch` DESC, `t`.`Note`, `g`.`Nickname`, `g`.`SquadId`, `t2`.`IsAutomatic`, `t2`.`Nickname` DESC, `t2`.`Id`");
        }

        public override async Task Multiple_orderby_with_navigation_expansion_on_one_of_the_order_bys_inside_subquery_duplicated_orderings(
            bool isAsync)
        {
            await base.Multiple_orderby_with_navigation_expansion_on_one_of_the_order_bys_inside_subquery_duplicated_orderings(isAsync);

            AssertSql(
                $@"SELECT `g`.`FullName`, `g`.`Nickname`, `g`.`SquadId`, `t2`.`Id`, `t2`.`AmmunitionType`, `t2`.`IsAutomatic`, `t2`.`Name`, `t2`.`OwnerFullName`, `t2`.`SynergyWithId`
FROM `Gears` AS `g`
LEFT JOIN `Tags` AS `t` ON (`g`.`Nickname` = `t`.`GearNickName`) AND (`g`.`SquadId` = `t`.`GearSquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
LEFT JOIN (
    SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`, `t1`.`Nickname`
    FROM `Weapons` AS `w`
    LEFT JOIN (
        SELECT `g1`.`Nickname`, `g1`.`SquadId`, `g1`.`AssignedCityName`, `g1`.`CityOfBirthName`, `g1`.`Discriminator`, `g1`.`FullName`, `g1`.`HasSoulPatch`, `g1`.`LeaderNickname`, `g1`.`LeaderSquadId`, `g1`.`Rank`
        FROM `Gears` AS `g1`
        WHERE `g1`.`Discriminator` IN ('Gear', 'Officer')
    ) AS `t1` ON `w`.`OwnerFullName` = `t1`.`FullName`
) AS `t2` ON `t0`.`FullName` = `t2`.`OwnerFullName`
WHERE (`g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Discriminator` = 'Officer')) AND EXISTS (
    SELECT 1
    FROM `Gears` AS `g2`
    WHERE `g2`.`Discriminator` IN ('Gear', 'Officer') AND ((`g`.`Nickname` = `g2`.`LeaderNickname`) AND (`g`.`SquadId` = `g2`.`LeaderSquadId`)))
ORDER BY `g`.`HasSoulPatch` DESC, `t`.`Note`, `g`.`Nickname`, `g`.`SquadId`, `t2`.`IsAutomatic`, `t2`.`Nickname` DESC, `t2`.`Id`");
        }

        public override async Task Multiple_orderby_with_navigation_expansion_on_one_of_the_order_bys_inside_subquery_complex_orderings(
            bool isAsync)
        {
            await base.Multiple_orderby_with_navigation_expansion_on_one_of_the_order_bys_inside_subquery_complex_orderings(isAsync);

            AssertSql(
                $@"SELECT `g`.`FullName`, `g`.`Nickname`, `g`.`SquadId`, `t2`.`Id`, `t2`.`AmmunitionType`, `t2`.`IsAutomatic`, `t2`.`Name`, `t2`.`OwnerFullName`, `t2`.`SynergyWithId`
FROM `Gears` AS `g`
LEFT JOIN `Tags` AS `t` ON (`g`.`Nickname` = `t`.`GearNickName`) AND (`g`.`SquadId` = `t`.`GearSquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
LEFT JOIN (
    SELECT `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`, (
        SELECT COUNT(*)
        FROM `Weapons` AS `w`
        WHERE `t1`.`FullName` IS NOT NULL AND (`t1`.`FullName` = `w`.`OwnerFullName`)) AS `c`
    FROM `Weapons` AS `w0`
    LEFT JOIN (
        SELECT `g1`.`Nickname`, `g1`.`SquadId`, `g1`.`AssignedCityName`, `g1`.`CityOfBirthName`, `g1`.`Discriminator`, `g1`.`FullName`, `g1`.`HasSoulPatch`, `g1`.`LeaderNickname`, `g1`.`LeaderSquadId`, `g1`.`Rank`
        FROM `Gears` AS `g1`
        WHERE `g1`.`Discriminator` IN ('Gear', 'Officer')
    ) AS `t1` ON `w0`.`OwnerFullName` = `t1`.`FullName`
) AS `t2` ON `t0`.`FullName` = `t2`.`OwnerFullName`
WHERE (`g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Discriminator` = 'Officer')) AND EXISTS (
    SELECT 1
    FROM `Gears` AS `g2`
    WHERE `g2`.`Discriminator` IN ('Gear', 'Officer') AND ((`g`.`Nickname` = `g2`.`LeaderNickname`) AND (`g`.`SquadId` = `g2`.`LeaderSquadId`)))
ORDER BY `g`.`HasSoulPatch` DESC, `t`.`Note`, `g`.`Nickname`, `g`.`SquadId`, `t2`.`Id` DESC, `t2`.`c`");
        }

        public override async Task Correlated_collections_multiple_nested_complex_collections(bool isAsync)
        {
            await base.Correlated_collections_multiple_nested_complex_collections(isAsync);

            AssertSql(
                $@"SELECT `g`.`FullName`, `g`.`Nickname`, `g`.`SquadId`, `t4`.`FullName`, `t4`.`Nickname`, `t4`.`SquadId`, `t4`.`Id`, `t4`.`Name`, `t4`.`IsAutomatic`, `t4`.`Id0`, `t4`.`Nickname0`, `t4`.`HasSoulPatch`, `t4`.`SquadId0`, `t6`.`Id`, `t6`.`AmmunitionType`, `t6`.`IsAutomatic`, `t6`.`Name`, `t6`.`OwnerFullName`, `t6`.`SynergyWithId`
FROM `Gears` AS `g`
LEFT JOIN `Tags` AS `t` ON (`g`.`Nickname` = `t`.`GearNickName`) AND (`g`.`SquadId` = `t`.`GearSquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
LEFT JOIN (
    SELECT `g1`.`FullName`, `g1`.`Nickname`, `g1`.`SquadId`, `t3`.`Id`, `t3`.`Name`, `t3`.`IsAutomatic`, `t3`.`Id0`, `t3`.`Nickname` AS `Nickname0`, `t3`.`HasSoulPatch`, `t3`.`SquadId` AS `SquadId0`, `g1`.`Rank`, `t3`.`IsAutomatic0`, `g1`.`LeaderNickname`, `g1`.`LeaderSquadId`
    FROM `Gears` AS `g1`
    LEFT JOIN (
        SELECT `w`.`Id`, `w0`.`Name`, `w0`.`IsAutomatic`, `w0`.`Id` AS `Id0`, `t2`.`Nickname`, `t2`.`HasSoulPatch`, `t2`.`SquadId`, `w`.`IsAutomatic` AS `IsAutomatic0`, `w`.`OwnerFullName`
        FROM `Weapons` AS `w`
        LEFT JOIN (
            SELECT `g2`.`Nickname`, `g2`.`SquadId`, `g2`.`AssignedCityName`, `g2`.`CityOfBirthName`, `g2`.`Discriminator`, `g2`.`FullName`, `g2`.`HasSoulPatch`, `g2`.`LeaderNickname`, `g2`.`LeaderSquadId`, `g2`.`Rank`
            FROM `Gears` AS `g2`
            WHERE `g2`.`Discriminator` IN ('Gear', 'Officer')
        ) AS `t1` ON `w`.`OwnerFullName` = `t1`.`FullName`
        LEFT JOIN `Squads` AS `s` ON `t1`.`SquadId` = `s`.`Id`
        LEFT JOIN `Weapons` AS `w0` ON `t1`.`FullName` = `w0`.`OwnerFullName`
        LEFT JOIN (
            SELECT `g3`.`Nickname`, `g3`.`HasSoulPatch`, `g3`.`SquadId`
            FROM `Gears` AS `g3`
            WHERE `g3`.`Discriminator` IN ('Gear', 'Officer')
        ) AS `t2` ON `s`.`Id` = `t2`.`SquadId`
        WHERE (`w`.`Name` <> 'Bar') OR `w`.`Name` IS NULL
    ) AS `t3` ON `g1`.`FullName` = `t3`.`OwnerFullName`
    WHERE `g1`.`Discriminator` IN ('Gear', 'Officer') AND (`g1`.`FullName` <> 'Foo')
) AS `t4` ON (`g`.`Nickname` = `t4`.`LeaderNickname`) AND (`g`.`SquadId` = `t4`.`LeaderSquadId`)
LEFT JOIN (
    SELECT `w1`.`Id`, `w1`.`AmmunitionType`, `w1`.`IsAutomatic`, `w1`.`Name`, `w1`.`OwnerFullName`, `w1`.`SynergyWithId`, `t5`.`Nickname`
    FROM `Weapons` AS `w1`
    LEFT JOIN (
        SELECT `g4`.`Nickname`, `g4`.`SquadId`, `g4`.`AssignedCityName`, `g4`.`CityOfBirthName`, `g4`.`Discriminator`, `g4`.`FullName`, `g4`.`HasSoulPatch`, `g4`.`LeaderNickname`, `g4`.`LeaderSquadId`, `g4`.`Rank`
        FROM `Gears` AS `g4`
        WHERE `g4`.`Discriminator` IN ('Gear', 'Officer')
    ) AS `t5` ON `w1`.`OwnerFullName` = `t5`.`FullName`
) AS `t6` ON `t0`.`FullName` = `t6`.`OwnerFullName`
WHERE (`g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Discriminator` = 'Officer')) AND EXISTS (
    SELECT 1
    FROM `Gears` AS `g5`
    WHERE `g5`.`Discriminator` IN ('Gear', 'Officer') AND ((`g`.`Nickname` = `g5`.`LeaderNickname`) AND (`g`.`SquadId` = `g5`.`LeaderSquadId`)))
ORDER BY `g`.`HasSoulPatch` DESC, `t`.`Note`, `g`.`Nickname`, `g`.`SquadId`, `t4`.`Rank`, `t4`.`Nickname`, `t4`.`SquadId`, `t4`.`IsAutomatic0`, `t4`.`Id`, `t4`.`Id0`, `t4`.`Nickname0`, `t4`.`SquadId0`, `t6`.`IsAutomatic`, `t6`.`Nickname` DESC, `t6`.`Id`");
        }

        public override async Task Correlated_collections_inner_subquery_selector_references_outer_qsre(bool isAsync)
        {
            await base.Correlated_collections_inner_subquery_selector_references_outer_qsre(isAsync);

            AssertSql(
                $@"SELECT `g`.`FullName`, `g`.`Nickname`, `g`.`SquadId`, `t`.`FullName`, `t`.`FullName0`, `t`.`Nickname`, `t`.`SquadId`
FROM `Gears` AS `g`
OUTER APPLY (
    SELECT `g0`.`FullName`, `g`.`FullName` AS `FullName0`, `g0`.`Nickname`, `g0`.`SquadId`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer') AND ((`g`.`Nickname` = `g0`.`LeaderNickname`) AND (`g`.`SquadId` = `g0`.`LeaderSquadId`))
) AS `t`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Discriminator` = 'Officer')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `t`.`Nickname`, `t`.`SquadId`");
        }

        public override async Task Correlated_collections_inner_subquery_predicate_references_outer_qsre(bool isAsync)
        {
            await base.Correlated_collections_inner_subquery_predicate_references_outer_qsre(isAsync);

            AssertSql(
                $@"SELECT `g`.`FullName`, `g`.`Nickname`, `g`.`SquadId`, `t`.`FullName`, `t`.`Nickname`, `t`.`SquadId`
FROM `Gears` AS `g`
OUTER APPLY (
    SELECT `g0`.`FullName`, `g0`.`Nickname`, `g0`.`SquadId`
    FROM `Gears` AS `g0`
    WHERE (`g0`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`FullName` <> 'Foo')) AND ((`g`.`Nickname` = `g0`.`LeaderNickname`) AND (`g`.`SquadId` = `g0`.`LeaderSquadId`))
) AS `t`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Discriminator` = 'Officer')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `t`.`Nickname`, `t`.`SquadId`");
        }

        public override async Task Correlated_collections_nested_inner_subquery_references_outer_qsre_one_level_up(bool isAsync)
        {
            await base.Correlated_collections_nested_inner_subquery_references_outer_qsre_one_level_up(isAsync);

            AssertSql(
                $@"SELECT `g`.`FullName`, `g`.`Nickname`, `g`.`SquadId`, `t0`.`FullName`, `t0`.`Nickname`, `t0`.`SquadId`, `t0`.`Name`, `t0`.`Nickname0`, `t0`.`Id`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `g0`.`FullName`, `g0`.`Nickname`, `g0`.`SquadId`, `t`.`Name`, `t`.`Nickname` AS `Nickname0`, `t`.`Id`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`
    FROM `Gears` AS `g0`
    OUTER APPLY (
        SELECT `w`.`Name`, `g0`.`Nickname`, `w`.`Id`
        FROM `Weapons` AS `w`
        WHERE ((`w`.`Name` <> 'Bar') OR `w`.`Name` IS NULL) AND (`g0`.`FullName` = `w`.`OwnerFullName`)
    ) AS `t`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer') AND (`g0`.`FullName` <> 'Foo')
) AS `t0` ON (`g`.`Nickname` = `t0`.`LeaderNickname`) AND (`g`.`SquadId` = `t0`.`LeaderSquadId`)
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Discriminator` = 'Officer')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `t0`.`Nickname`, `t0`.`SquadId`, `t0`.`Id`");
        }

        public override async Task Correlated_collections_nested_inner_subquery_references_outer_qsre_two_levels_up(bool isAsync)
        {
            await base.Correlated_collections_nested_inner_subquery_references_outer_qsre_two_levels_up(isAsync);

            AssertSql(
                $@"SELECT `g`.`FullName`, `g`.`Nickname`, `g`.`SquadId`, `t0`.`FullName`, `t0`.`Nickname`, `t0`.`SquadId`, `t0`.`Name`, `t0`.`Nickname0`, `t0`.`Id`
FROM `Gears` AS `g`
OUTER APPLY (
    SELECT `g0`.`FullName`, `g0`.`Nickname`, `g0`.`SquadId`, `t`.`Name`, `t`.`Nickname` AS `Nickname0`, `t`.`Id`
    FROM `Gears` AS `g0`
    LEFT JOIN (
        SELECT `w`.`Name`, `g`.`Nickname`, `w`.`Id`, `w`.`OwnerFullName`
        FROM `Weapons` AS `w`
        WHERE (`w`.`Name` <> 'Bar') OR `w`.`Name` IS NULL
    ) AS `t` ON `g0`.`FullName` = `t`.`OwnerFullName`
    WHERE (`g0`.`Discriminator` IN ('Gear', 'Officer') AND (`g0`.`FullName` <> 'Foo')) AND ((`g`.`Nickname` = `g0`.`LeaderNickname`) AND (`g`.`SquadId` = `g0`.`LeaderSquadId`))
) AS `t0`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Discriminator` = 'Officer')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `t0`.`Nickname`, `t0`.`SquadId`, `t0`.`Id`");
        }

        public override async Task Correlated_collections_on_select_many(bool isAsync)
        {
            await base.Correlated_collections_on_select_many(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `s`.`Name`, `g`.`SquadId`, `s`.`Id`, `t`.`Id`, `t`.`AmmunitionType`, `t`.`IsAutomatic`, `t`.`Name`, `t`.`OwnerFullName`, `t`.`SynergyWithId`, `t0`.`Nickname`, `t0`.`SquadId`, `t0`.`AssignedCityName`, `t0`.`CityOfBirthName`, `t0`.`Discriminator`, `t0`.`FullName`, `t0`.`HasSoulPatch`, `t0`.`LeaderNickname`, `t0`.`LeaderSquadId`, `t0`.`Rank`
FROM `Gears` AS `g`,
`Squads` AS `s`
LEFT JOIN (
    SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
    FROM `Weapons` AS `w`
    WHERE (`w`.`IsAutomatic` = True) OR ((`w`.`Name` <> 'foo') OR `w`.`Name` IS NULL)
) AS `t` ON `g`.`FullName` = `t`.`OwnerFullName`
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer') AND (`g0`.`HasSoulPatch` <> True)
) AS `t0` ON `s`.`Id` = `t0`.`SquadId`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`HasSoulPatch` = True)
ORDER BY `g`.`Nickname`, `s`.`Id` DESC, `g`.`SquadId`, `t`.`Id`, `t0`.`Nickname`, `t0`.`SquadId`");
        }

        public override async Task Correlated_collections_with_Skip(bool isAsync)
        {
            await base.Correlated_collections_with_Skip(isAsync);

            AssertSql(
                $@"SELECT `s`.`Id`
FROM `Squads` AS `s`
ORDER BY `s`.`Name`",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_Id='1'")}

SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Officer', 'Gear') AND ({AssertSqlHelper.Parameter("@_outer_Id")} = `g`.`SquadId`)
ORDER BY `g`.`Nickname`
SKIP 1",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_Id='2'")}

SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Officer', 'Gear') AND ({AssertSqlHelper.Parameter("@_outer_Id")} = `g`.`SquadId`)
ORDER BY `g`.`Nickname`
SKIP 1");
        }

        public override async Task Correlated_collections_with_Take(bool isAsync)
        {
            await base.Correlated_collections_with_Take(isAsync);

            AssertSql(
                $@"SELECT `s`.`Id`
FROM `Squads` AS `s`
ORDER BY `s`.`Name`",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_Id='1'")}

SELECT TOP 2 `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Officer', 'Gear') AND ({AssertSqlHelper.Parameter("@_outer_Id")} = `g`.`SquadId`)
ORDER BY `g`.`Nickname`",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_Id='2'")}

SELECT TOP 2 `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Officer', 'Gear') AND ({AssertSqlHelper.Parameter("@_outer_Id")} = `g`.`SquadId`)
ORDER BY `g`.`Nickname`");
        }

        public override async Task Correlated_collections_with_Distinct(bool isAsync)
        {
            await base.Correlated_collections_with_Distinct(isAsync);

            AssertSql(
                $@"SELECT `s`.`Id`, `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`Discriminator`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`
FROM `Squads` AS `s`
LEFT JOIN (
    SELECT DISTINCT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON `s`.`Id` = `t`.`SquadId`
ORDER BY `s`.`Name`, `s`.`Id`, `t`.`Nickname`, `t`.`SquadId`");
        }

        public override async Task Correlated_collections_with_FirstOrDefault(bool isAsync)
        {
            await base.Correlated_collections_with_FirstOrDefault(isAsync);

            AssertSql(
                $@"SELECT (
    SELECT TOP 1 `g`.`FullName`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`s`.`Id` = `g`.`SquadId`)
    ORDER BY `g`.`Nickname`)
FROM `Squads` AS `s`
ORDER BY `s`.`Name`");
        }

        public override async Task Correlated_collections_on_left_join_with_predicate(bool isAsync)
        {
            await base.Correlated_collections_on_left_join_with_predicate(isAsync);

            AssertSql(
                $@"SELECT `t0`.`Nickname`, `t`.`Id`, `w`.`Name`, `w`.`Id`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON `t`.`GearNickName` = `t0`.`Nickname`
LEFT JOIN `Weapons` AS `w` ON `t0`.`FullName` = `w`.`OwnerFullName`
WHERE `t0`.`HasSoulPatch` <> True
ORDER BY `t`.`Id`, `w`.`Id`");
        }

        public override async Task Correlated_collections_on_left_join_with_null_value(bool isAsync)
        {
            await base.Correlated_collections_on_left_join_with_null_value(isAsync);

            AssertSql(
                $@"SELECT `t`.`Id`, `w`.`Name`, `w`.`Id`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON `t`.`GearNickName` = `t0`.`Nickname`
LEFT JOIN `Weapons` AS `w` ON `t0`.`FullName` = `w`.`OwnerFullName`
ORDER BY `t`.`Note`, `t`.`Id`, `w`.`Id`");
        }

        public override async Task Correlated_collections_left_join_with_self_reference(bool isAsync)
        {
            await base.Correlated_collections_left_join_with_self_reference(isAsync);

            AssertSql(
                $@"SELECT `t`.`Note`, `t`.`Id`, `t1`.`FullName`, `t1`.`Nickname`, `t1`.`SquadId`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Discriminator` = 'Officer')
) AS `t0` ON `t`.`GearNickName` = `t0`.`Nickname`
LEFT JOIN (
    SELECT `g0`.`FullName`, `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t1` ON ((`t0`.`Nickname` = `t1`.`LeaderNickname`) OR (`t0`.`Nickname` IS NULL AND `t1`.`LeaderNickname` IS NULL)) AND (`t0`.`SquadId` = `t1`.`LeaderSquadId`)
ORDER BY `t`.`Id`, `t1`.`Nickname`, `t1`.`SquadId`");
        }

        public override async Task Correlated_collections_deeply_nested_left_join(bool isAsync)
        {
            await base.Correlated_collections_deeply_nested_left_join(isAsync);

            AssertSql(
                $@"SELECT `t`.`Id`, `t2`.`Nickname`, `t2`.`SquadId`, `t2`.`Id`, `t2`.`AmmunitionType`, `t2`.`IsAutomatic`, `t2`.`Name`, `t2`.`OwnerFullName`, `t2`.`SynergyWithId`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON `t`.`GearNickName` = `t0`.`Nickname`
LEFT JOIN `Squads` AS `s` ON `t0`.`SquadId` = `s`.`Id`
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `t1`.`Id`, `t1`.`AmmunitionType`, `t1`.`IsAutomatic`, `t1`.`Name`, `t1`.`OwnerFullName`, `t1`.`SynergyWithId`
    FROM `Gears` AS `g0`
    LEFT JOIN (
        SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
        FROM `Weapons` AS `w`
        WHERE `w`.`IsAutomatic` = True
    ) AS `t1` ON `g0`.`FullName` = `t1`.`OwnerFullName`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer') AND (`g0`.`HasSoulPatch` = True)
) AS `t2` ON `s`.`Id` = `t2`.`SquadId`
ORDER BY `t`.`Note`, `t0`.`Nickname` DESC, `t`.`Id`, `t2`.`Nickname`, `t2`.`SquadId`, `t2`.`Id`");
        }

        public override async Task Correlated_collections_from_left_join_with_additional_elements_projected_of_that_join(bool isAsync)
        {
            await base.Correlated_collections_from_left_join_with_additional_elements_projected_of_that_join(isAsync);

            AssertSql(
                $@"SELECT `w`.`Id`, `t1`.`Rank`, `t1`.`Nickname`, `t1`.`SquadId`, `t1`.`Id`, `t1`.`AmmunitionType`, `t1`.`IsAutomatic`, `t1`.`Name`, `t1`.`OwnerFullName`, `t1`.`SynergyWithId`
FROM `Weapons` AS `w`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON `w`.`OwnerFullName` = `t`.`FullName`
LEFT JOIN `Squads` AS `s` ON `t`.`SquadId` = `s`.`Id`
LEFT JOIN (
    SELECT `g0`.`Rank`, `g0`.`Nickname`, `g0`.`SquadId`, `t0`.`Id`, `t0`.`AmmunitionType`, `t0`.`IsAutomatic`, `t0`.`Name`, `t0`.`OwnerFullName`, `t0`.`SynergyWithId`, `g0`.`FullName`
    FROM `Gears` AS `g0`
    LEFT JOIN (
        SELECT `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
        FROM `Weapons` AS `w0`
        WHERE `w0`.`IsAutomatic` <> True
    ) AS `t0` ON `g0`.`FullName` = `t0`.`OwnerFullName`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t1` ON `s`.`Id` = `t1`.`SquadId`
ORDER BY `w`.`Name`, `w`.`Id`, `t1`.`FullName` DESC, `t1`.`Nickname`, `t1`.`SquadId`, `t1`.`Id`");
        }

        public override async Task Correlated_collections_complex_scenario1(bool isAsync)
        {
            await base.Correlated_collections_complex_scenario1(isAsync);

            AssertSql(
                $@"SELECT `g`.`FullName`, `g`.`Nickname`, `g`.`SquadId`, `t1`.`Id`, `t1`.`Nickname`, `t1`.`HasSoulPatch`, `t1`.`SquadId`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `w`.`Id`, `t0`.`Nickname`, `t0`.`HasSoulPatch`, `t0`.`SquadId`, `w`.`OwnerFullName`
    FROM `Weapons` AS `w`
    LEFT JOIN (
        SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
        FROM `Gears` AS `g0`
        WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
    ) AS `t` ON `w`.`OwnerFullName` = `t`.`FullName`
    LEFT JOIN `Squads` AS `s` ON `t`.`SquadId` = `s`.`Id`
    LEFT JOIN (
        SELECT `g1`.`Nickname`, `g1`.`HasSoulPatch`, `g1`.`SquadId`
        FROM `Gears` AS `g1`
        WHERE `g1`.`Discriminator` IN ('Gear', 'Officer')
    ) AS `t0` ON `s`.`Id` = `t0`.`SquadId`
) AS `t1` ON `g`.`FullName` = `t1`.`OwnerFullName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `t1`.`Id`, `t1`.`Nickname`, `t1`.`SquadId`");
        }

        public override async Task Correlated_collections_complex_scenario2(bool isAsync)
        {
            await base.Correlated_collections_complex_scenario2(isAsync);

            AssertSql(
                $@"SELECT `g`.`FullName`, `g`.`Nickname`, `g`.`SquadId`, `t2`.`FullName`, `t2`.`Nickname`, `t2`.`SquadId`, `t2`.`Id`, `t2`.`Nickname0`, `t2`.`HasSoulPatch`, `t2`.`SquadId0`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `g0`.`FullName`, `g0`.`Nickname`, `g0`.`SquadId`, `t1`.`Id`, `t1`.`Nickname` AS `Nickname0`, `t1`.`HasSoulPatch`, `t1`.`SquadId` AS `SquadId0`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`
    FROM `Gears` AS `g0`
    LEFT JOIN (
        SELECT `w`.`Id`, `t0`.`Nickname`, `t0`.`HasSoulPatch`, `t0`.`SquadId`, `w`.`OwnerFullName`
        FROM `Weapons` AS `w`
        LEFT JOIN (
            SELECT `g1`.`Nickname`, `g1`.`SquadId`, `g1`.`AssignedCityName`, `g1`.`CityOfBirthName`, `g1`.`Discriminator`, `g1`.`FullName`, `g1`.`HasSoulPatch`, `g1`.`LeaderNickname`, `g1`.`LeaderSquadId`, `g1`.`Rank`
            FROM `Gears` AS `g1`
            WHERE `g1`.`Discriminator` IN ('Gear', 'Officer')
        ) AS `t` ON `w`.`OwnerFullName` = `t`.`FullName`
        LEFT JOIN `Squads` AS `s` ON `t`.`SquadId` = `s`.`Id`
        LEFT JOIN (
            SELECT `g2`.`Nickname`, `g2`.`HasSoulPatch`, `g2`.`SquadId`
            FROM `Gears` AS `g2`
            WHERE `g2`.`Discriminator` IN ('Gear', 'Officer')
        ) AS `t0` ON `s`.`Id` = `t0`.`SquadId`
    ) AS `t1` ON `g0`.`FullName` = `t1`.`OwnerFullName`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t2` ON (`g`.`Nickname` = `t2`.`LeaderNickname`) AND (`g`.`SquadId` = `t2`.`LeaderSquadId`)
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Discriminator` = 'Officer')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `t2`.`Nickname`, `t2`.`SquadId`, `t2`.`Id`, `t2`.`Nickname0`, `t2`.`SquadId0`");
        }

        public override async Task Correlated_collections_with_funky_orderby_complex_scenario1(bool isAsync)
        {
            await base.Correlated_collections_with_funky_orderby_complex_scenario1(isAsync);

            AssertSql(
                $@"SELECT `g`.`FullName`, `g`.`Nickname`, `g`.`SquadId`, `t1`.`Id`, `t1`.`Nickname`, `t1`.`HasSoulPatch`, `t1`.`SquadId`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `w`.`Id`, `t0`.`Nickname`, `t0`.`HasSoulPatch`, `t0`.`SquadId`, `w`.`OwnerFullName`
    FROM `Weapons` AS `w`
    LEFT JOIN (
        SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
        FROM `Gears` AS `g0`
        WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
    ) AS `t` ON `w`.`OwnerFullName` = `t`.`FullName`
    LEFT JOIN `Squads` AS `s` ON `t`.`SquadId` = `s`.`Id`
    LEFT JOIN (
        SELECT `g1`.`Nickname`, `g1`.`HasSoulPatch`, `g1`.`SquadId`
        FROM `Gears` AS `g1`
        WHERE `g1`.`Discriminator` IN ('Gear', 'Officer')
    ) AS `t0` ON `s`.`Id` = `t0`.`SquadId`
) AS `t1` ON `g`.`FullName` = `t1`.`OwnerFullName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
ORDER BY `g`.`FullName`, `g`.`Nickname` DESC, `g`.`SquadId`, `t1`.`Id`, `t1`.`Nickname`, `t1`.`SquadId`");
        }

        public override async Task Correlated_collections_with_funky_orderby_complex_scenario2(bool isAsync)
        {
            await base.Correlated_collections_with_funky_orderby_complex_scenario2(isAsync);

            AssertSql(
                $@"SELECT `g`.`FullName`, `g`.`Nickname`, `g`.`SquadId`, `t2`.`FullName`, `t2`.`Nickname`, `t2`.`SquadId`, `t2`.`Id`, `t2`.`Nickname0`, `t2`.`HasSoulPatch`, `t2`.`SquadId0`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `g0`.`FullName`, `g0`.`Nickname`, `g0`.`SquadId`, `t1`.`Id`, `t1`.`Nickname` AS `Nickname0`, `t1`.`HasSoulPatch`, `t1`.`SquadId` AS `SquadId0`, `g0`.`HasSoulPatch` AS `HasSoulPatch0`, `t1`.`IsAutomatic`, `t1`.`Name`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`
    FROM `Gears` AS `g0`
    LEFT JOIN (
        SELECT `w`.`Id`, `t0`.`Nickname`, `t0`.`HasSoulPatch`, `t0`.`SquadId`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`
        FROM `Weapons` AS `w`
        LEFT JOIN (
            SELECT `g1`.`Nickname`, `g1`.`SquadId`, `g1`.`AssignedCityName`, `g1`.`CityOfBirthName`, `g1`.`Discriminator`, `g1`.`FullName`, `g1`.`HasSoulPatch`, `g1`.`LeaderNickname`, `g1`.`LeaderSquadId`, `g1`.`Rank`
            FROM `Gears` AS `g1`
            WHERE `g1`.`Discriminator` IN ('Gear', 'Officer')
        ) AS `t` ON `w`.`OwnerFullName` = `t`.`FullName`
        LEFT JOIN `Squads` AS `s` ON `t`.`SquadId` = `s`.`Id`
        LEFT JOIN (
            SELECT `g2`.`Nickname`, `g2`.`HasSoulPatch`, `g2`.`SquadId`
            FROM `Gears` AS `g2`
            WHERE `g2`.`Discriminator` IN ('Gear', 'Officer')
        ) AS `t0` ON `s`.`Id` = `t0`.`SquadId`
    ) AS `t1` ON `g0`.`FullName` = `t1`.`OwnerFullName`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t2` ON (`g`.`Nickname` = `t2`.`LeaderNickname`) AND (`g`.`SquadId` = `t2`.`LeaderSquadId`)
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Discriminator` = 'Officer')
ORDER BY `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`FullName`, `g`.`Nickname`, `g`.`SquadId`, `t2`.`FullName`, `t2`.`HasSoulPatch0` DESC, `t2`.`Nickname`, `t2`.`SquadId`, `t2`.`IsAutomatic`, `t2`.`Name` DESC, `t2`.`Id`, `t2`.`Nickname0`, `t2`.`SquadId0`");
        }

        public override async Task Correlated_collection_with_top_level_FirstOrDefault(bool isAsync)
        {
            await base.Correlated_collection_with_top_level_FirstOrDefault(isAsync);

            AssertSql(
                $@"SELECT `t`.`Nickname`, `t`.`SquadId`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM (
    SELECT TOP 1 `g`.`Nickname`, `g`.`SquadId`, `g`.`FullName`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
    ORDER BY `g`.`Nickname`
) AS `t`
LEFT JOIN `Weapons` AS `w` ON `t`.`FullName` = `w`.`OwnerFullName`
ORDER BY `t`.`Nickname`, `t`.`SquadId`, `w`.`Id`");
        }

        public override async Task Correlated_collection_with_top_level_Count(bool isAsync)
        {
            await base.Correlated_collection_with_top_level_Count(isAsync);

            AssertSql(
                $@"SELECT COUNT(*)
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')");
        }

        public override async Task Correlated_collection_with_top_level_Last_with_orderby_on_outer(bool isAsync)
        {
            await base.Correlated_collection_with_top_level_Last_with_orderby_on_outer(isAsync);

            AssertSql(
                $@"SELECT `t`.`Nickname`, `t`.`SquadId`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM (
    SELECT TOP 1 `g`.`Nickname`, `g`.`SquadId`, `g`.`FullName`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
    ORDER BY `g`.`FullName`
) AS `t`
LEFT JOIN `Weapons` AS `w` ON `t`.`FullName` = `w`.`OwnerFullName`
ORDER BY `t`.`FullName`, `t`.`Nickname`, `t`.`SquadId`, `w`.`Id`");
        }

        public override async Task Correlated_collection_with_top_level_Last_with_order_by_on_inner(bool isAsync)
        {
            await base.Correlated_collection_with_top_level_Last_with_order_by_on_inner(isAsync);

            AssertSql(
                $@"SELECT `t`.`Nickname`, `t`.`SquadId`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM (
    SELECT TOP 1 `g`.`Nickname`, `g`.`SquadId`, `g`.`FullName`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
    ORDER BY `g`.`FullName` DESC
) AS `t`
LEFT JOIN `Weapons` AS `w` ON `t`.`FullName` = `w`.`OwnerFullName`
ORDER BY `t`.`FullName` DESC, `t`.`Nickname`, `t`.`SquadId`, `w`.`Name`, `w`.`Id`");
        }
        
        public override async Task Null_semantics_on_nullable_bool_from_inner_join_subquery_is_fully_applied(bool isAsync)
        {
            await base.Null_semantics_on_nullable_bool_from_inner_join_subquery_is_fully_applied(isAsync);

            AssertSql(
                $@"SELECT `t`.`Id`, `t`.`CapitalName`, `t`.`Discriminator`, `t`.`Name`, `t`.`CommanderName`, `t`.`Eradicated`
FROM `LocustLeaders` AS `l`
INNER JOIN (
    SELECT `f`.`Id`, `f`.`CapitalName`, `f`.`Discriminator`, `f`.`Name`, `f`.`CommanderName`, `f`.`Eradicated`
    FROM `Factions` AS `f`
    WHERE ((`f`.`Discriminator` = 'LocustHorde') AND (`f`.`Discriminator` = 'LocustHorde')) AND (`f`.`Name` = 'Swarm')
) AS `t` ON `l`.`Name` = `t`.`CommanderName`
WHERE `l`.`Discriminator` IN ('LocustLeader', 'LocustCommander') AND ((`t`.`Eradicated` <> True) OR `t`.`Eradicated` IS NULL)");
        }

        public override async Task Null_semantics_on_nullable_bool_from_left_join_subquery_is_fully_applied(bool isAsync)
        {
            await base.Null_semantics_on_nullable_bool_from_left_join_subquery_is_fully_applied(isAsync);

            AssertSql(
                $@"SELECT `t`.`Id`, `t`.`CapitalName`, `t`.`Discriminator`, `t`.`Name`, `t`.`CommanderName`, `t`.`Eradicated`
FROM `LocustLeaders` AS `l`
LEFT JOIN (
    SELECT `f`.`Id`, `f`.`CapitalName`, `f`.`Discriminator`, `f`.`Name`, `f`.`CommanderName`, `f`.`Eradicated`
    FROM `Factions` AS `f`
    WHERE ((`f`.`Discriminator` = 'LocustHorde') AND (`f`.`Discriminator` = 'LocustHorde')) AND (`f`.`Name` = 'Swarm')
) AS `t` ON `l`.`Name` = `t`.`CommanderName`
WHERE `l`.`Discriminator` IN ('LocustLeader', 'LocustCommander') AND ((`t`.`Eradicated` <> True) OR `t`.`Eradicated` IS NULL)");
        }

        public override async Task Include_on_derived_type_with_order_by_and_paging(bool isAsync)
        {
            await base.Include_on_derived_type_with_order_by_and_paging(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='10'")}

SELECT `t1`.`Name`, `t1`.`Discriminator`, `t1`.`LocustHordeId`, `t1`.`ThreatLevel`, `t1`.`DefeatedByNickname`, `t1`.`DefeatedBySquadId`, `t1`.`HighCommandId`, `t2`.`Nickname`, `t2`.`SquadId`, `t2`.`AssignedCityName`, `t2`.`CityOfBirthName`, `t2`.`Discriminator`, `t2`.`FullName`, `t2`.`HasSoulPatch`, `t2`.`LeaderNickname`, `t2`.`LeaderSquadId`, `t2`.`Rank`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `l`.`Name`, `l`.`Discriminator`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`DefeatedByNickname`, `l`.`DefeatedBySquadId`, `l`.`HighCommandId`, `t0`.`Note`
    FROM `LocustLeaders` AS `l`
    LEFT JOIN (
        SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
        FROM `Gears` AS `g`
        WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
    ) AS `t` ON (`l`.`DefeatedByNickname` = `t`.`Nickname`) AND (`l`.`DefeatedBySquadId` = `t`.`SquadId`)
    LEFT JOIN `Tags` AS `t0` ON ((`t`.`Nickname` = `t0`.`GearNickName`) OR (`t`.`Nickname` IS NULL AND `t0`.`GearNickName` IS NULL)) AND ((`t`.`SquadId` = `t0`.`GearSquadId`) OR (`t`.`SquadId` IS NULL AND `t0`.`GearSquadId` IS NULL))
    WHERE `l`.`Discriminator` IN ('LocustLeader', 'LocustCommander')
    ORDER BY `t0`.`Note`
) AS `t1`
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t2` ON (`t1`.`DefeatedByNickname` = `t2`.`Nickname`) AND (`t1`.`DefeatedBySquadId` = `t2`.`SquadId`)
LEFT JOIN `Weapons` AS `w` ON `t2`.`FullName` = `w`.`OwnerFullName`
ORDER BY `t1`.`Note`, `t1`.`Name`, `w`.`Id`");
        }

        public override async Task Select_required_navigation_on_derived_type(bool isAsync)
        {
            await base.Select_required_navigation_on_derived_type(isAsync);

            AssertSql(
                $@"SELECT `l0`.`Name`
FROM `LocustLeaders` AS `l`
LEFT JOIN `LocustHighCommands` AS `l0` ON `l`.`HighCommandId` = `l0`.`Id`
WHERE `l`.`Discriminator` IN ('LocustLeader', 'LocustCommander')");
        }

        public override async Task Select_required_navigation_on_the_same_type_with_cast(bool isAsync)
        {
            await base.Select_required_navigation_on_the_same_type_with_cast(isAsync);

            AssertSql(
                $@"SELECT `c`.`Name`
FROM `Gears` AS `g`
INNER JOIN `Cities` AS `c` ON `g`.`CityOfBirthName` = `c`.`Name`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')");
        }

        public override async Task Where_required_navigation_on_derived_type(bool isAsync)
        {
            await base.Where_required_navigation_on_derived_type(isAsync);

            AssertSql(
                $@"SELECT `l`.`Name`, `l`.`Discriminator`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`DefeatedByNickname`, `l`.`DefeatedBySquadId`, `l`.`HighCommandId`
FROM `LocustLeaders` AS `l`
LEFT JOIN `LocustHighCommands` AS `l0` ON `l`.`HighCommandId` = `l0`.`Id`
WHERE `l`.`Discriminator` IN ('LocustLeader', 'LocustCommander') AND (`l0`.`IsOperational` = True)");
        }

        public override async Task Outer_parameter_in_join_key(bool isAsync)
        {
            await base.Outer_parameter_in_join_key(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `t1`.`Note`, `t1`.`Id`, `t1`.`Nickname`, `t1`.`SquadId`
FROM `Gears` AS `g`
OUTER APPLY (
    SELECT `t`.`Note`, `t`.`Id`, `t0`.`Nickname`, `t0`.`SquadId`
    FROM `Tags` AS `t`
    INNER JOIN (
        SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
        FROM `Gears` AS `g0`
        WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
    ) AS `t0` ON `g`.`FullName` = `t0`.`FullName`
) AS `t1`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Discriminator` = 'Officer')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `t1`.`Id`, `t1`.`Nickname`, `t1`.`SquadId`");
        }

        public override async Task Outer_parameter_in_join_key_inner_and_outer(bool isAsync)
        {
            await base.Outer_parameter_in_join_key_inner_and_outer(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `t1`.`Note`, `t1`.`Id`, `t1`.`Nickname`, `t1`.`SquadId`
FROM `Gears` AS `g`
OUTER APPLY (
    SELECT `t`.`Note`, `t`.`Id`, `t0`.`Nickname`, `t0`.`SquadId`
    FROM `Tags` AS `t`
    INNER JOIN (
        SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
        FROM `Gears` AS `g0`
        WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
    ) AS `t0` ON `g`.`FullName` = `g`.`Nickname`
) AS `t1`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Discriminator` = 'Officer')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `t1`.`Id`, `t1`.`Nickname`, `t1`.`SquadId`");
        }
        
        public override async Task Outer_parameter_in_group_join_with_DefaultIfEmpty(bool isAsync)
        {
            await base.Outer_parameter_in_group_join_with_DefaultIfEmpty(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `t1`.`Note`, `t1`.`Id`
FROM `Gears` AS `g`
OUTER APPLY (
    SELECT `t`.`Note`, `t`.`Id`
    FROM `Tags` AS `t`
    LEFT JOIN (
        SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
        FROM `Gears` AS `g0`
        WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
    ) AS `t0` ON `g`.`FullName` = `t0`.`FullName`
) AS `t1`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Discriminator` = 'Officer')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `t1`.`Id`");
        }

        public override async Task Negated_bool_ternary_inside_anonymous_type_in_projection(bool isAsync)
        {
            await base.Negated_bool_ternary_inside_anonymous_type_in_projection(isAsync);

            AssertSql(
                $@"SELECT CASE
    WHEN CASE
        WHEN `t0`.`HasSoulPatch` = True THEN True
        ELSE IIf(`t0`.`HasSoulPatch` IS NULL, True, `t0`.`HasSoulPatch`)
    END <> True THEN True
    ELSE False
END AS `c`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)");
        }

        public override async Task Order_by_entity_qsre(bool isAsync)
        {
            await base.Order_by_entity_qsre(isAsync);

            AssertSql(
                $@"SELECT `g`.`FullName`
FROM `Gears` AS `g`
LEFT JOIN `Cities` AS `c` ON `g`.`AssignedCityName` = `c`.`Name`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
ORDER BY `c`.`Name`, `g`.`Nickname` DESC");
        }

        public override async Task Order_by_entity_qsre_with_inheritance(bool isAsync)
        {
            await base.Order_by_entity_qsre_with_inheritance(isAsync);

            AssertSql(
                $@"SELECT `l`.`Name`
FROM `LocustLeaders` AS `l`
INNER JOIN `LocustHighCommands` AS `l0` ON `l`.`HighCommandId` = `l0`.`Id`
WHERE `l`.`Discriminator` IN ('LocustLeader', 'LocustCommander') AND (`l`.`Discriminator` = 'LocustCommander')
ORDER BY `l0`.`Id`, `l`.`Name`");
        }

        public override async Task Order_by_entity_qsre_composite_key(bool isAsync)
        {
            await base.Order_by_entity_qsre_composite_key(isAsync);

            AssertSql(
                $@"SELECT `w`.`Name`
FROM `Weapons` AS `w`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON `w`.`OwnerFullName` = `t`.`FullName`
ORDER BY `t`.`Nickname`, `t`.`SquadId`, `w`.`Id`");
        }

        public override async Task Order_by_entity_qsre_with_other_orderbys(bool isAsync)
        {
            await base.Order_by_entity_qsre_with_other_orderbys(isAsync);

            AssertSql(
                $@"SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON `w`.`OwnerFullName` = `t`.`FullName`
LEFT JOIN `Weapons` AS `w0` ON `w`.`SynergyWithId` = `w0`.`Id`
ORDER BY `w`.`IsAutomatic`, `t`.`Nickname` DESC, `t`.`SquadId` DESC, `w0`.`Id`, `w`.`Name`");
        }

        public override async Task Join_on_entity_qsre_keys(bool isAsync)
        {
            await base.Join_on_entity_qsre_keys(isAsync);

            AssertSql(
                $@"SELECT `w`.`Name` AS `Name1`, `w0`.`Name` AS `Name2`
FROM `Weapons` AS `w`
INNER JOIN `Weapons` AS `w0` ON `w`.`Id` = `w0`.`Id`");
        }

        public override async Task Join_on_entity_qsre_keys_composite_key(bool isAsync)
        {
            await base.Join_on_entity_qsre_keys_composite_key(isAsync);

            AssertSql(
                $@"SELECT `g`.`FullName` AS `GearName1`, `t`.`FullName` AS `GearName2`
FROM `Gears` AS `g`
INNER JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON (`g`.`Nickname` = `t`.`Nickname`) AND (`g`.`SquadId` = `t`.`SquadId`)
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')");
        }

        public override async Task Join_on_entity_qsre_keys_inheritance(bool isAsync)
        {
            await base.Join_on_entity_qsre_keys_inheritance(isAsync);

            AssertSql(
                $@"SELECT `g`.`FullName` AS `GearName`, `t`.`FullName` AS `OfficerName`
FROM `Gears` AS `g`
INNER JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer') AND (`g0`.`Discriminator` = 'Officer')
) AS `t` ON (`g`.`Nickname` = `t`.`Nickname`) AND (`g`.`SquadId` = `t`.`SquadId`)
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')");
        }

        public override async Task Join_on_entity_qsre_keys_outer_key_is_navigation(bool isAsync)
        {
            await base.Join_on_entity_qsre_keys_outer_key_is_navigation(isAsync);

            AssertSql(
                $@"SELECT `w`.`Name` AS `Name1`, `w1`.`Name` AS `Name2`
FROM `Weapons` AS `w`
LEFT JOIN `Weapons` AS `w0` ON `w`.`SynergyWithId` = `w0`.`Id`
INNER JOIN `Weapons` AS `w1` ON `w0`.`Id` = `w1`.`Id`");
        }

        public override async Task Join_on_entity_qsre_keys_inner_key_is_navigation(bool isAsync)
        {
            await base.Join_on_entity_qsre_keys_inner_key_is_navigation(isAsync);

            AssertSql(
                $@"SELECT `c`.`Name` AS `CityName`, `t`.`Nickname` AS `GearNickname`
FROM `Cities` AS `c`
INNER JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, `c0`.`Name`, `c0`.`Location`, `c0`.`Nation`
    FROM `Gears` AS `g`
    LEFT JOIN `Cities` AS `c0` ON `g`.`AssignedCityName` = `c0`.`Name`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON `c`.`Name` = `t`.`Name`");
        }

        public override async Task Join_on_entity_qsre_keys_inner_key_is_navigation_composite_key(bool isAsync)
        {
            await base.Join_on_entity_qsre_keys_inner_key_is_navigation_composite_key(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `t1`.`Note`
FROM `Gears` AS `g`
INNER JOIN (
    SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`Note`, `t0`.`Nickname`, `t0`.`SquadId`, `t0`.`AssignedCityName`, `t0`.`CityOfBirthName`, `t0`.`Discriminator`, `t0`.`FullName`, `t0`.`HasSoulPatch`, `t0`.`LeaderNickname`, `t0`.`LeaderSquadId`, `t0`.`Rank`
    FROM `Tags` AS `t`
    LEFT JOIN (
        SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
        FROM `Gears` AS `g0`
        WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
    ) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
    WHERE (`t`.`Note` = 'Cole''s Tag') OR (`t`.`Note` = 'Dom''s Tag')
) AS `t1` ON (`g`.`Nickname` = `t1`.`Nickname`) AND (`g`.`SquadId` = `t1`.`SquadId`)
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')");
        }

        public override async Task Join_on_entity_qsre_keys_inner_key_is_nested_navigation(bool isAsync)
        {
            await base.Join_on_entity_qsre_keys_inner_key_is_nested_navigation(isAsync);

            AssertSql(
                $@"SELECT `s`.`Name` AS `SquadName`, `t0`.`Name` AS `WeaponName`
FROM `Squads` AS `s`
INNER JOIN (
    SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`, `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`Discriminator`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`, `s0`.`Id` AS `Id0`, `s0`.`InternalNumber`, `s0`.`Name` AS `Name0`
    FROM `Weapons` AS `w`
    LEFT JOIN (
        SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
        FROM `Gears` AS `g`
        WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
    ) AS `t` ON `w`.`OwnerFullName` = `t`.`FullName`
    LEFT JOIN `Squads` AS `s0` ON `t`.`SquadId` = `s0`.`Id`
    WHERE `w`.`IsAutomatic` = True
) AS `t0` ON `s`.`Id` = `t0`.`Id0`");
        }

        public override async Task GroupJoin_on_entity_qsre_keys_inner_key_is_nested_navigation(bool isAsync)
        {
            await base.GroupJoin_on_entity_qsre_keys_inner_key_is_nested_navigation(isAsync);

            AssertSql(
                $@"SELECT `s`.`Name` AS `SquadName`, `t0`.`Name` AS `WeaponName`
FROM `Squads` AS `s`
LEFT JOIN (
    SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`, `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`Discriminator`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`, `s0`.`Id` AS `Id0`, `s0`.`InternalNumber`, `s0`.`Name` AS `Name0`
    FROM `Weapons` AS `w`
    LEFT JOIN (
        SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
        FROM `Gears` AS `g`
        WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
    ) AS `t` ON `w`.`OwnerFullName` = `t`.`FullName`
    LEFT JOIN `Squads` AS `s0` ON `t`.`SquadId` = `s0`.`Id`
) AS `t0` ON `s`.`Id` = `t0`.`Id0`");
        }
        
        public override async Task Streaming_correlated_collection_issue_11403(bool isAsync)
        {
            await base.Streaming_correlated_collection_issue_11403(isAsync);

            AssertSql(
                $@"SELECT TOP 1 `g`.`FullName`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Officer', 'Gear')
ORDER BY `g`.`Nickname`",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_FullName='Damon Baird' (Size = 450)")}

SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE ({AssertSqlHelper.Parameter("@_outer_FullName")} = `w`.`OwnerFullName`) AND (`w`.`IsAutomatic` = False)
ORDER BY `w`.`Id`");
        }

        public override async Task Project_one_value_type_from_empty_collection(bool isAsync)
        {
            await base.Project_one_value_type_from_empty_collection(isAsync);

            AssertSql(
                $@"SELECT `s`.`Name`, (
    SELECT TOP 1 `g`.`SquadId`
    FROM `Gears` AS `g`
    WHERE (`g`.`Discriminator` IN ('Gear', 'Officer') AND (`s`.`Id` = `g`.`SquadId`)) AND (`g`.`HasSoulPatch` = True)) AS `SquadId`
FROM `Squads` AS `s`
WHERE `s`.`Name` = 'Kilo'");
        }

        public override async Task Project_one_value_type_converted_to_nullable_from_empty_collection(bool isAsync)
        {
            await base.Project_one_value_type_converted_to_nullable_from_empty_collection(isAsync);

            AssertSql(
                $@"SELECT `s`.`Name`, (
    SELECT TOP 1 `g`.`SquadId`
    FROM `Gears` AS `g`
    WHERE (`g`.`Discriminator` IN ('Gear', 'Officer') AND (`s`.`Id` = `g`.`SquadId`)) AND (`g`.`HasSoulPatch` = True)) AS `SquadId`
FROM `Squads` AS `s`
WHERE `s`.`Name` = 'Kilo'");
        }

        public override async Task Project_one_value_type_with_client_projection_from_empty_collection(bool isAsync)
        {
            await base.Project_one_value_type_with_client_projection_from_empty_collection(isAsync);

            AssertSql(
                $@"SELECT `s`.`Name`, `t0`.`SquadId`, `t0`.`LeaderSquadId`, `t0`.`c`
FROM `Squads` AS `s`
LEFT JOIN (
    SELECT `t`.`SquadId`, `t`.`LeaderSquadId`, `t`.`c`, `t`.`Nickname`
    FROM (
        SELECT `g`.`SquadId`, `g`.`LeaderSquadId`, 1 AS `c`, `g`.`Nickname`, ROW_NUMBER() OVER(PARTITION BY `g`.`SquadId` ORDER BY `g`.`Nickname`, `g`.`SquadId`) AS `row`
        FROM `Gears` AS `g`
        WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`HasSoulPatch` = True)
    ) AS `t`
    WHERE `t`.`row` <= 1
) AS `t0` ON `s`.`Id` = `t0`.`SquadId`
WHERE `s`.`Name` = 'Kilo'");
        }

        public override async Task Filter_on_subquery_projecting_one_value_type_from_empty_collection(bool isAsync)
        {
            await base.Filter_on_subquery_projecting_one_value_type_from_empty_collection(isAsync);

            AssertSql(
                $@"SELECT `s`.`Name`
FROM `Squads` AS `s`
WHERE (`s`.`Name` = 'Kilo') AND (COALESCE((
    SELECT TOP 1 `g`.`SquadId`
    FROM `Gears` AS `g`
    WHERE (`g`.`Discriminator` IN ('Officer', 'Gear') AND (`s`.`Id` = `g`.`SquadId`)) AND (`g`.`HasSoulPatch` = True)
), 0) <> 0)");
        }

        public override async Task Select_subquery_projecting_single_constant_int(bool isAsync)
        {
            await base.Select_subquery_projecting_single_constant_int(isAsync);

            AssertSql(
                $@"SELECT `s`.`Name`, (
    SELECT TOP 1 42
    FROM `Gears` AS `g`
    WHERE (`g`.`Discriminator` IN ('Gear', 'Officer') AND (`s`.`Id` = `g`.`SquadId`)) AND (`g`.`HasSoulPatch` = True)) AS `Gear`
FROM `Squads` AS `s`");
        }

        public override async Task Select_subquery_projecting_single_constant_string(bool isAsync)
        {
            await base.Select_subquery_projecting_single_constant_string(isAsync);

            AssertSql(
                $@"SELECT `s`.`Name`, (
    SELECT TOP 1 'Foo'
    FROM `Gears` AS `g`
    WHERE (`g`.`Discriminator` IN ('Gear', 'Officer') AND (`s`.`Id` = `g`.`SquadId`)) AND (`g`.`HasSoulPatch` = True)) AS `Gear`
FROM `Squads` AS `s`");
        }

        public override async Task Select_subquery_projecting_single_constant_bool(bool isAsync)
        {
            await base.Select_subquery_projecting_single_constant_bool(isAsync);

            AssertSql(
                $@"SELECT `s`.`Name`, (
    SELECT TOP 1 True
    FROM `Gears` AS `g`
    WHERE (`g`.`Discriminator` IN ('Gear', 'Officer') AND (`s`.`Id` = `g`.`SquadId`)) AND (`g`.`HasSoulPatch` = True)) AS `Gear`
FROM `Squads` AS `s`");
        }

        public override async Task Select_subquery_projecting_single_constant_inside_anonymous(bool isAsync)
        {
            await base.Select_subquery_projecting_single_constant_inside_anonymous(isAsync);

            AssertSql(
                $@"SELECT `s`.`Name`, `t0`.`c`
FROM `Squads` AS `s`
LEFT JOIN (
    SELECT `t`.`c`, `t`.`Nickname`, `t`.`SquadId`
    FROM (
        SELECT 1 AS `c`, `g`.`Nickname`, `g`.`SquadId`, ROW_NUMBER() OVER(PARTITION BY `g`.`SquadId` ORDER BY `g`.`Nickname`, `g`.`SquadId`) AS `row`
        FROM `Gears` AS `g`
        WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`HasSoulPatch` = True)
    ) AS `t`
    WHERE `t`.`row` <= 1
) AS `t0` ON `s`.`Id` = `t0`.`SquadId`");
        }

        public override async Task Select_subquery_projecting_multiple_constants_inside_anonymous(bool isAsync)
        {
            await base.Select_subquery_projecting_multiple_constants_inside_anonymous(isAsync);

            AssertSql(
                $@"SELECT `s`.`Name`, `t0`.`c`, `t0`.`c0`, `t0`.`c1`
FROM `Squads` AS `s`
LEFT JOIN (
    SELECT `t`.`c`, `t`.`c0`, `t`.`c1`, `t`.`Nickname`, `t`.`SquadId`
    FROM (
        SELECT True AS `c`, False AS `c0`, 1 AS `c1`, `g`.`Nickname`, `g`.`SquadId`, ROW_NUMBER() OVER(PARTITION BY `g`.`SquadId` ORDER BY `g`.`Nickname`, `g`.`SquadId`) AS `row`
        FROM `Gears` AS `g`
        WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`HasSoulPatch` = True)
    ) AS `t`
    WHERE `t`.`row` <= 1
) AS `t0` ON `s`.`Id` = `t0`.`SquadId`");
        }

        public override async Task Include_with_order_by_constant(bool isAsync)
        {
            await base.Include_with_order_by_constant(isAsync);

            AssertSql(
                $@"SELECT `s`.`Id`, `s`.`InternalNumber`, `s`.`Name`, `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`Discriminator`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`
FROM `Squads` AS `s`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON `s`.`Id` = `t`.`SquadId`
ORDER BY `s`.`Id`, `t`.`Nickname`, `t`.`SquadId`");
        }
        
        public override async Task Correlated_collection_order_by_constant(bool isAsync)
        {
            await base.Correlated_collection_order_by_constant(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `w`.`Name`, `w`.`Id`
FROM `Gears` AS `g`
LEFT JOIN `Weapons` AS `w` ON `g`.`FullName` = `w`.`OwnerFullName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `w`.`Id`");
        }

        public override async Task Select_subquery_projecting_single_constant_null_of_non_mapped_type(bool isAsync)
        {
            await base.Select_subquery_projecting_single_constant_null_of_non_mapped_type(isAsync);

            AssertSql(
                $@"SELECT `s`.`Name`, `t0`.`c`
FROM `Squads` AS `s`
LEFT JOIN (
    SELECT `t`.`c`, `t`.`Nickname`, `t`.`SquadId`
    FROM (
        SELECT 1 AS `c`, `g`.`Nickname`, `g`.`SquadId`, ROW_NUMBER() OVER(PARTITION BY `g`.`SquadId` ORDER BY `g`.`Nickname`, `g`.`SquadId`) AS `row`
        FROM `Gears` AS `g`
        WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`HasSoulPatch` = True)
    ) AS `t`
    WHERE `t`.`row` <= 1
) AS `t0` ON `s`.`Id` = `t0`.`SquadId`");
        }

        public override async Task Select_subquery_projecting_single_constant_of_non_mapped_type(bool isAsync)
        {
            await base.Select_subquery_projecting_single_constant_of_non_mapped_type(isAsync);

            AssertSql(
                $@"SELECT `s`.`Name`, `t0`.`c`
FROM `Squads` AS `s`
LEFT JOIN (
    SELECT `t`.`c`, `t`.`Nickname`, `t`.`SquadId`
    FROM (
        SELECT 1 AS `c`, `g`.`Nickname`, `g`.`SquadId`, ROW_NUMBER() OVER(PARTITION BY `g`.`SquadId` ORDER BY `g`.`Nickname`, `g`.`SquadId`) AS `row`
        FROM `Gears` AS `g`
        WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`HasSoulPatch` = True)
    ) AS `t`
    WHERE `t`.`row` <= 1
) AS `t0` ON `s`.`Id` = `t0`.`SquadId`");
        }
        
        public override async Task Include_collection_OrderBy_aggregate(bool isAsync)
        {
            await base.Include_collection_OrderBy_aggregate(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`Discriminator`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON (`g`.`Nickname` = `t`.`LeaderNickname`) AND (`g`.`SquadId` = `t`.`LeaderSquadId`)
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Discriminator` = 'Officer')
ORDER BY (
    SELECT COUNT(*)
    FROM `Weapons` AS `w`
    WHERE `g`.`FullName` = `w`.`OwnerFullName`), `g`.`Nickname`, `g`.`SquadId`, `t`.`Nickname`, `t`.`SquadId`");
        }

        public override async Task Include_collection_with_complex_OrderBy2(bool isAsync)
        {
            await base.Include_collection_with_complex_OrderBy2(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`Discriminator`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON (`g`.`Nickname` = `t`.`LeaderNickname`) AND (`g`.`SquadId` = `t`.`LeaderSquadId`)
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Discriminator` = 'Officer')
ORDER BY (
    SELECT TOP 1 `w`.`IsAutomatic`
    FROM `Weapons` AS `w`
    WHERE `g`.`FullName` = `w`.`OwnerFullName`
    ORDER BY `w`.`Id`), `g`.`Nickname`, `g`.`SquadId`, `t`.`Nickname`, `t`.`SquadId`");
        }

        public override async Task Include_collection_with_complex_OrderBy3(bool isAsync)
        {
            await base.Include_collection_with_complex_OrderBy3(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`Discriminator`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON (`g`.`Nickname` = `t`.`LeaderNickname`) AND (`g`.`SquadId` = `t`.`LeaderSquadId`)
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Discriminator` = 'Officer')
ORDER BY (
    SELECT TOP 1 `w`.`IsAutomatic`
    FROM `Weapons` AS `w`
    WHERE `g`.`FullName` = `w`.`OwnerFullName`
    ORDER BY `w`.`Id`), `g`.`Nickname`, `g`.`SquadId`, `t`.`Nickname`, `t`.`SquadId`");
        }

        public override async Task Correlated_collection_with_complex_OrderBy(bool isAsync)
        {
            await base.Correlated_collection_with_complex_OrderBy(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`Discriminator`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer') AND (`g0`.`HasSoulPatch` <> True)
) AS `t` ON (`g`.`Nickname` = `t`.`LeaderNickname`) AND (`g`.`SquadId` = `t`.`LeaderSquadId`)
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Discriminator` = 'Officer')
ORDER BY (
    SELECT COUNT(*)
    FROM `Weapons` AS `w`
    WHERE `g`.`FullName` = `w`.`OwnerFullName`), `g`.`Nickname`, `g`.`SquadId`, `t`.`Nickname`, `t`.`SquadId`");
        }

        public override async Task Correlated_collection_with_very_complex_order_by(bool isAsync)
        {
            await base.Correlated_collection_with_very_complex_order_by(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`Discriminator`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer') AND (`g0`.`HasSoulPatch` <> True)
) AS `t` ON (`g`.`Nickname` = `t`.`LeaderNickname`) AND (`g`.`SquadId` = `t`.`LeaderSquadId`)
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Discriminator` = 'Officer')
ORDER BY (
    SELECT COUNT(*)
    FROM `Weapons` AS `w`
    WHERE (`g`.`FullName` = `w`.`OwnerFullName`) AND (`w`.`IsAutomatic` = (
        SELECT TOP 1 `g1`.`HasSoulPatch`
        FROM `Gears` AS `g1`
        WHERE `g1`.`Discriminator` IN ('Gear', 'Officer') AND (`g1`.`Nickname` = 'Marcus')))), `g`.`Nickname`, `g`.`SquadId`, `t`.`Nickname`, `t`.`SquadId`");
        }

        public override async Task Cast_to_derived_type_after_OfType_works(bool isAsync)
        {
            await base.Cast_to_derived_type_after_OfType_works(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Discriminator` = 'Officer')");
        }

        public override async Task Select_subquery_boolean(bool isAsync)
        {
            await base.Select_subquery_boolean(isAsync);

            AssertSql(
                $@"SELECT (
    SELECT TOP 1 `w`.`IsAutomatic`
    FROM `Weapons` AS `w`
    WHERE `g`.`FullName` = `w`.`OwnerFullName`
    ORDER BY `w`.`Id`)
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')");
        }

        public override async Task Select_subquery_boolean_with_pushdown(bool isAsync)
        {
            await base.Select_subquery_boolean_with_pushdown(isAsync);

            AssertSql(
                $@"SELECT (
    SELECT TOP 1 `w`.`IsAutomatic`
    FROM `Weapons` AS `w`
    WHERE `g`.`FullName` = `w`.`OwnerFullName`
    ORDER BY `w`.`Id`)
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')");
        }

        public override async Task Select_subquery_int_with_inside_cast_and_coalesce(bool isAsync)
        {
            await base.Select_subquery_int_with_inside_cast_and_coalesce(isAsync);

            AssertSql(
                $@"SELECT COALESCE((
    SELECT TOP 1 `w`.`Id`
    FROM `Weapons` AS `w`
    WHERE `g`.`FullName` = `w`.`OwnerFullName`
    ORDER BY `w`.`Id`), 42)
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')");
        }

        public override async Task Select_subquery_int_with_outside_cast_and_coalesce(bool isAsync)
        {
            await base.Select_subquery_int_with_outside_cast_and_coalesce(isAsync);

            AssertSql(
                $@"SELECT COALESCE((
    SELECT TOP 1 `w`.`Id`
    FROM `Weapons` AS `w`
    WHERE `g`.`FullName` = `w`.`OwnerFullName`
    ORDER BY `w`.`Id`), 42)
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')");
        }

        public override async Task Select_subquery_int_with_pushdown_and_coalesce(bool isAsync)
        {
            await base.Select_subquery_int_with_pushdown_and_coalesce(isAsync);

            AssertSql(
                $@"SELECT COALESCE((
    SELECT TOP 1 `w`.`Id`
    FROM `Weapons` AS `w`
    WHERE `g`.`FullName` = `w`.`OwnerFullName`
    ORDER BY `w`.`Id`), 42)
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')");
        }

        public override async Task Select_subquery_int_with_pushdown_and_coalesce2(bool isAsync)
        {
            await base.Select_subquery_int_with_pushdown_and_coalesce2(isAsync);

            AssertSql(
                $@"SELECT COALESCE((
    SELECT TOP 1 `w`.`Id`
    FROM `Weapons` AS `w`
    WHERE `g`.`FullName` = `w`.`OwnerFullName`
    ORDER BY `w`.`Id`), (
    SELECT TOP 1 `w0`.`Id`
    FROM `Weapons` AS `w0`
    WHERE `g`.`FullName` = `w0`.`OwnerFullName`
    ORDER BY `w0`.`Id`))
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')");
        }

        public override async Task Select_subquery_boolean_empty(bool isAsync)
        {
            await base.Select_subquery_boolean_empty(isAsync);

            AssertSql(
                $@"SELECT (
    SELECT TOP 1 `w`.`IsAutomatic`
    FROM `Weapons` AS `w`
    WHERE (`g`.`FullName` = `w`.`OwnerFullName`) AND (`w`.`Name` = 'BFG')
    ORDER BY `w`.`Id`)
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')");
        }

        public override async Task Select_subquery_boolean_empty_with_pushdown(bool isAsync)
        {
            await base.Select_subquery_boolean_empty_with_pushdown(isAsync);

            AssertSql(
                $@"SELECT (
    SELECT TOP 1 `w`.`IsAutomatic`
    FROM `Weapons` AS `w`
    WHERE (`g`.`FullName` = `w`.`OwnerFullName`) AND (`w`.`Name` = 'BFG')
    ORDER BY `w`.`Id`)
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')");
        }
        
        public override async Task Select_subquery_distinct_singleordefault_boolean1(bool isAsync)
        {
            await base.Select_subquery_distinct_singleordefault_boolean1(isAsync);

            AssertSql(
                $@"SELECT (
    SELECT TOP 1 `t`.`IsAutomatic`
    FROM (
        SELECT DISTINCT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
        FROM `Weapons` AS `w`
        WHERE (`g`.`FullName` = `w`.`OwnerFullName`) AND (CHARINDEX('Lancer', `w`.`Name`) > 0)
    ) AS `t`)
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`HasSoulPatch` = True)");
        }

        public override async Task Select_subquery_distinct_singleordefault_boolean2(bool isAsync)
        {
            await base.Select_subquery_distinct_singleordefault_boolean2(isAsync);

            AssertSql(
                $@"SELECT (
    SELECT DISTINCT TOP 1 `w`.`IsAutomatic`
    FROM `Weapons` AS `w`
    WHERE (`g`.`FullName` = `w`.`OwnerFullName`) AND (CHARINDEX('Lancer', `w`.`Name`) > 0))
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`HasSoulPatch` = True)");
        }

        public override async Task Select_subquery_distinct_singleordefault_boolean_with_pushdown(bool isAsync)
        {
            await base.Select_subquery_distinct_singleordefault_boolean_with_pushdown(isAsync);

            AssertSql(
                $@"SELECT (
    SELECT TOP 1 `t`.`IsAutomatic`
    FROM (
        SELECT DISTINCT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
        FROM `Weapons` AS `w`
        WHERE (`g`.`FullName` = `w`.`OwnerFullName`) AND (CHARINDEX('Lancer', `w`.`Name`) > 0)
    ) AS `t`)
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`HasSoulPatch` = True)");
        }

        public override async Task Select_subquery_distinct_singleordefault_boolean_empty1(bool isAsync)
        {
            await base.Select_subquery_distinct_singleordefault_boolean_empty1(isAsync);

            AssertSql(
                $@"SELECT (
    SELECT TOP 1 `t`.`IsAutomatic`
    FROM (
        SELECT DISTINCT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
        FROM `Weapons` AS `w`
        WHERE (`g`.`FullName` = `w`.`OwnerFullName`) AND (`w`.`Name` = 'BFG')
    ) AS `t`)
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`HasSoulPatch` = True)");
        }

        public override async Task Select_subquery_distinct_singleordefault_boolean_empty2(bool isAsync)
        {
            await base.Select_subquery_distinct_singleordefault_boolean_empty2(isAsync);

            AssertSql(
                $@"SELECT (
    SELECT DISTINCT TOP 1 `w`.`IsAutomatic`
    FROM `Weapons` AS `w`
    WHERE (`g`.`FullName` = `w`.`OwnerFullName`) AND (`w`.`Name` = 'BFG'))
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`HasSoulPatch` = True)");
        }

        public override async Task Select_subquery_distinct_singleordefault_boolean_empty_with_pushdown(bool isAsync)
        {
            await base.Select_subquery_distinct_singleordefault_boolean_empty_with_pushdown(isAsync);

            AssertSql(
                $@"SELECT (
    SELECT TOP 1 `t`.`IsAutomatic`
    FROM (
        SELECT DISTINCT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
        FROM `Weapons` AS `w`
        WHERE (`g`.`FullName` = `w`.`OwnerFullName`) AND (`w`.`Name` = 'BFG')
    ) AS `t`)
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`HasSoulPatch` = True)");
        }

        public override async Task Cast_subquery_to_base_type_using_typed_ToList(bool isAsync)
        {
            await base.Cast_subquery_to_base_type_using_typed_ToList(isAsync);

            AssertSql(
                $@"SELECT `c`.`Name`, `t`.`CityOfBirthName`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Nickname`, `t`.`Rank`, `t`.`SquadId`
FROM `Cities` AS `c`
LEFT JOIN (
    SELECT `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Nickname`, `g`.`Rank`, `g`.`SquadId`, `g`.`AssignedCityName`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON `c`.`Name` = `t`.`AssignedCityName`
WHERE `c`.`Name` = 'Ephyra'
ORDER BY `c`.`Name`, `t`.`Nickname`, `t`.`SquadId`");
        }

        public override async Task Cast_ordered_subquery_to_base_type_using_typed_ToArray(bool isAsync)
        {
            await base.Cast_ordered_subquery_to_base_type_using_typed_ToArray(isAsync);

            AssertSql(
                $@"SELECT `c`.`Name`, `t`.`CityOfBirthName`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Nickname`, `t`.`Rank`, `t`.`SquadId`
FROM `Cities` AS `c`
LEFT JOIN (
    SELECT `g`.`CityOfBirthName`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Nickname`, `g`.`Rank`, `g`.`SquadId`, `g`.`AssignedCityName`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON `c`.`Name` = `t`.`AssignedCityName`
WHERE `c`.`Name` = 'Ephyra'
ORDER BY `c`.`Name`, `t`.`Nickname` DESC, `t`.`SquadId`");
        }

        public override async Task Correlated_collection_with_complex_order_by_funcletized_to_constant_bool(bool isAsync)
        {
            await base.Correlated_collection_with_complex_order_by_funcletized_to_constant_bool(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`FullName`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Officer', 'Gear')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `g`.`FullName`",
                //
                $@"SELECT `t`.`c`, `t`.`Nickname`, `t`.`SquadId`, `t`.`FullName`, [g.Weapons].`Name`, [g.Weapons].`OwnerFullName`
FROM `Weapons` AS [g.Weapons]
INNER JOIN (
    SELECT False AS `c`, `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`FullName`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Officer', 'Gear')
) AS `t` ON [g.Weapons].`OwnerFullName` = `t`.`FullName`
ORDER BY `t`.`c` DESC, `t`.`Nickname`, `t`.`SquadId`, `t`.`FullName`");
        }

        public override async Task Double_order_by_on_nullable_bool_coming_from_optional_navigation(bool isAsync)
        {
            await base.Double_order_by_on_nullable_bool_coming_from_optional_navigation(isAsync);

            AssertSql(
                $@"SELECT `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
FROM `Weapons` AS `w`
LEFT JOIN `Weapons` AS `w0` ON `w`.`SynergyWithId` = `w0`.`Id`
ORDER BY `w0`.`IsAutomatic`, `w0`.`Id`");
        }

        public override async Task Double_order_by_on_Like(bool isAsync)
        {
            await base.Double_order_by_on_Like(isAsync);

            AssertSql(
                $@"SELECT `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
FROM `Weapons` AS `w`
LEFT JOIN `Weapons` AS `w0` ON `w`.`SynergyWithId` = `w0`.`Id`
ORDER BY IIF(`w0`.`Name` LIKE '%Lancer', 1, 0)");
        }

        public override async Task Double_order_by_on_is_null(bool isAsync)
        {
            await base.Double_order_by_on_is_null(isAsync);

            AssertSql(
                $@"SELECT `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
FROM `Weapons` AS `w`
LEFT JOIN `Weapons` AS `w0` ON `w`.`SynergyWithId` = `w0`.`Id`
ORDER BY IIF(`w0`.`Name` IS NULL, 1, 0)");
        }

        public override async Task Double_order_by_on_string_compare(bool isAsync)
        {
            await base.Double_order_by_on_string_compare(isAsync);

            // issue #16092
            //            AssertSql(
            //                $@"SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
            //FROM `Weapons` AS `w`
            //ORDER BY CASE
            //    WHEN `w`.`Name` = 'Marcus'' Lancer'
            //    THEN True ELSE False
            //END, `w`.`Id`");
        }

        public override async Task Double_order_by_binary_expression(bool isAsync)
        {
            await base.Double_order_by_binary_expression(isAsync);

            AssertSql(
                $@"SELECT `w`.`Id` + 2 AS `Binary`
FROM `Weapons` AS `w`
ORDER BY `w`.`Id` + 2");
        }

        public override async Task String_compare_with_null_conditional_argument(bool isAsync)
        {
            await base.String_compare_with_null_conditional_argument(isAsync);

            // issue #16092
            //            AssertSql(
            //                $@"SELECT [w.SynergyWith].`Id`, [w.SynergyWith].`AmmunitionType`, [w.SynergyWith].`IsAutomatic`, [w.SynergyWith].`Name`, [w.SynergyWith].`OwnerFullName`, [w.SynergyWith].`SynergyWithId`
            //FROM `Weapons` AS `w`
            //LEFT JOIN `Weapons` AS [w.SynergyWith] ON `w`.`SynergyWithId` = [w.SynergyWith].`Id`
            //ORDER BY CASE
            //    WHEN [w.SynergyWith].`Name` = 'Marcus'' Lancer'
            //    THEN True ELSE False
            //END");
        }

        public override async Task String_compare_with_null_conditional_argument2(bool isAsync)
        {
            await base.String_compare_with_null_conditional_argument2(isAsync);

            // issue #16092
            //            AssertSql(
            //                $@"SELECT [w.SynergyWith].`Id`, [w.SynergyWith].`AmmunitionType`, [w.SynergyWith].`IsAutomatic`, [w.SynergyWith].`Name`, [w.SynergyWith].`OwnerFullName`, [w.SynergyWith].`SynergyWithId`
            //FROM `Weapons` AS `w`
            //LEFT JOIN `Weapons` AS [w.SynergyWith] ON `w`.`SynergyWithId` = [w.SynergyWith].`Id`
            //ORDER BY CASE
            //    WHEN 'Marcus'' Lancer' = [w.SynergyWith].`Name`
            //    THEN True ELSE False
            //END");
        }

        public override async Task String_concat_with_null_conditional_argument(bool isAsync)
        {
            await base.String_concat_with_null_conditional_argument(isAsync);

            AssertSql(
                $@"SELECT `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
FROM `Weapons` AS `w`
LEFT JOIN `Weapons` AS `w0` ON `w`.`SynergyWithId` = `w0`.`Id`
ORDER BY `w0`.`Name` + CAST(5 AS nvarchar(max))");
        }

        public override async Task String_concat_with_null_conditional_argument2(bool isAsync)
        {
            await base.String_concat_with_null_conditional_argument2(isAsync);

            AssertSql(
                $@"SELECT `w0`.`Id`, `w0`.`AmmunitionType`, `w0`.`IsAutomatic`, `w0`.`Name`, `w0`.`OwnerFullName`, `w0`.`SynergyWithId`
FROM `Weapons` AS `w`
LEFT JOIN `Weapons` AS `w0` ON `w`.`SynergyWithId` = `w0`.`Id`
ORDER BY `w0`.`Name` + 'Marcus'' Lancer'");
        }

        public override async Task String_concat_on_various_types(bool isAsync)
        {
            await base.String_concat_on_various_types(isAsync);

            AssertSql(
                $@"");
        }

        public override async Task Time_of_day_datetimeoffset(bool isAsync)
        {
            await base.Time_of_day_datetimeoffset(isAsync);

            AssertSql(
                $@"SELECT CAST(`m`.`Timeline` AS time)
FROM `Missions` AS `m`");
        }

        public override async Task GroupBy_Property_Include_Select_Average(bool isAsync)
        {
            await base.GroupBy_Property_Include_Select_Average(isAsync);

            AssertSql(
                $@"SELECT AVG(IIf(`g`.`SquadId` IS NULL, NULL, CDBL(`g`.`SquadId`)))
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
GROUP BY `g`.`Rank`");
        }

        public override async Task GroupBy_Property_Include_Select_Sum(bool isAsync)
        {
            await base.GroupBy_Property_Include_Select_Sum(isAsync);

            AssertSql(
                $@"SELECT SUM(`g`.`SquadId`)
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
GROUP BY `g`.`Rank`");
        }

        public override async Task GroupBy_Property_Include_Select_Count(bool isAsync)
        {
            await base.GroupBy_Property_Include_Select_Count(isAsync);

            AssertSql(
                $@"SELECT COUNT(*)
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
GROUP BY `g`.`Rank`");
        }

        public override async Task GroupBy_Property_Include_Select_LongCount(bool isAsync)
        {
            await base.GroupBy_Property_Include_Select_LongCount(isAsync);

            AssertSql(
                $@"SELECT COUNT_BIG(*)
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
GROUP BY `g`.`Rank`");
        }

        public override async Task GroupBy_Property_Include_Select_Min(bool isAsync)
        {
            await base.GroupBy_Property_Include_Select_Min(isAsync);

            AssertSql(
                $@"SELECT MIN(`g`.`SquadId`)
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
GROUP BY `g`.`Rank`");
        }

        public override async Task GroupBy_Property_Include_Aggregate_with_anonymous_selector(bool isAsync)
        {
            await base.GroupBy_Property_Include_Aggregate_with_anonymous_selector(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname` AS `Key`, COUNT(*) AS `c`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
GROUP BY `g`.`Nickname`
ORDER BY `g`.`Nickname`");
        }

        public override async Task Group_by_entity_key_with_include_on_that_entity_with_key_in_result_selector(bool isAsync)
        {
            await base.Group_by_entity_key_with_include_on_that_entity_with_key_in_result_selector(isAsync);

            AssertSql(
                $@"");
        }

        public override async Task Group_by_entity_key_with_include_on_that_entity_with_key_in_result_selector_using_EF_Property(
            bool isAsync)
        {
            await base.Group_by_entity_key_with_include_on_that_entity_with_key_in_result_selector_using_EF_Property(isAsync);

            AssertSql(
                $@"");
        }

        public override async Task Group_by_with_include_with_entity_in_result_selector(bool isAsync)
        {
            await base.Group_by_with_include_with_entity_in_result_selector(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, [g.CityOfBirth].`Name`, [g.CityOfBirth].`Location`, [g.CityOfBirth].`Nation`
FROM `Gears` AS `g`
INNER JOIN `Cities` AS [g.CityOfBirth] ON `g`.`CityOfBirthName` = [g.CityOfBirth].`Name`
WHERE `g`.`Discriminator` IN ('Officer', 'Gear')
ORDER BY `g`.`Rank`");
        }

        public override async Task GroupBy_Property_Include_Select_Max(bool isAsync)
        {
            await base.GroupBy_Property_Include_Select_Max(isAsync);

            AssertSql(
                $@"SELECT MAX(`g`.`SquadId`)
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
GROUP BY `g`.`Rank`");
        }

        public override async Task Include_with_group_by_and_FirstOrDefault_gets_properly_applied(bool isAsync)
        {
            await base.Include_with_group_by_and_FirstOrDefault_gets_properly_applied(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, [g.CityOfBirth].`Name`, [g.CityOfBirth].`Location`, [g.CityOfBirth].`Nation`
FROM `Gears` AS `g`
INNER JOIN `Cities` AS [g.CityOfBirth] ON `g`.`CityOfBirthName` = [g.CityOfBirth].`Name`
WHERE `g`.`Discriminator` IN ('Officer', 'Gear')
ORDER BY `g`.`Rank`");
        }

        public override async Task Include_collection_with_Cast_to_base(bool isAsync)
        {
            await base.Include_collection_with_Cast_to_base(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Gears` AS `g`
LEFT JOIN `Weapons` AS `w` ON `g`.`FullName` = `w`.`OwnerFullName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Discriminator` = 'Officer')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `w`.`Id`");
        }

        public override async Task Include_with_client_method_and_member_access_still_applies_includes(bool isAsync)
        {
            await base.Include_with_client_method_and_member_access_still_applies_includes(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`Note`
FROM `Gears` AS `g`
LEFT JOIN `Tags` AS `t` ON (`g`.`Nickname` = `t`.`GearNickName`) AND (`g`.`SquadId` = `t`.`GearSquadId`)
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')");
        }

        public override async Task Include_with_projection_of_unmapped_property_still_gets_applied(bool isAsync)
        {
            await base.Include_with_projection_of_unmapped_property_still_gets_applied(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Gears` AS `g`
LEFT JOIN `Weapons` AS `w` ON `g`.`FullName` = `w`.`OwnerFullName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `w`.`Id`");
        }

        public override async Task Multiple_includes_with_client_method_around_entity_and_also_projecting_included_collection()
        {
            await base.Multiple_includes_with_client_method_around_entity_and_also_projecting_included_collection();

            AssertSql(
                $@"SELECT `s`.`Name`, `s`.`Id`, `s`.`InternalNumber`, `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`Discriminator`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`, `t`.`Id`, `t`.`AmmunitionType`, `t`.`IsAutomatic`, `t`.`Name`, `t`.`OwnerFullName`, `t`.`SynergyWithId`
FROM `Squads` AS `s`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
    FROM `Gears` AS `g`
    LEFT JOIN `Weapons` AS `w` ON `g`.`FullName` = `w`.`OwnerFullName`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON `s`.`Id` = `t`.`SquadId`
WHERE `s`.`Name` = 'Delta'
ORDER BY `s`.`Id`, `t`.`Nickname`, `t`.`SquadId`, `t`.`Id`");
        }

        public override async Task OrderBy_same_expression_containing_IsNull_correctly_deduplicates_the_ordering(bool isAsync)
        {
            await base.OrderBy_same_expression_containing_IsNull_correctly_deduplicates_the_ordering(isAsync);

            AssertSql(
                $@"SELECT CASE
    WHEN `g`.`LeaderNickname` IS NOT NULL THEN IIF((CAST(LEN(`g`.`Nickname`) AS int) = 5) AND LEN(`g`.`Nickname`) IS NOT NULL, 1, 0)
    ELSE NULL
END
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
ORDER BY CASE
    WHEN CASE
        WHEN `g`.`LeaderNickname` IS NOT NULL THEN IIF((CAST(LEN(`g`.`Nickname`) AS int) = 5) AND LEN(`g`.`Nickname`) IS NOT NULL, 1, 0)
        ELSE NULL
    END IS NOT NULL THEN True
    ELSE False
END");

        }

        public override async Task GetValueOrDefault_in_projection(bool isAsync)
        {
            await base.GetValueOrDefault_in_projection(isAsync);

            AssertSql(
                $@"SELECT IIf(`w`.`SynergyWithId` IS NULL, 0, `w`.`SynergyWithId`)
FROM `Weapons` AS `w`");
        }

        public override async Task GetValueOrDefault_in_filter(bool isAsync)
        {
            await base.GetValueOrDefault_in_filter(isAsync);

            AssertSql(
                $@"SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE IIf(`w`.`SynergyWithId` IS NULL, 0, `w`.`SynergyWithId`) = 0");
        }

        public override async Task GetValueOrDefault_in_filter_non_nullable_column(bool isAsync)
        {
            await base.GetValueOrDefault_in_filter_non_nullable_column(isAsync);

            AssertSql(
                $@"SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE IIf(`w`.`Id` IS NULL, 0, `w`.`Id`) = 0");
        }

        public override async Task GetValueOrDefault_in_order_by(bool isAsync)
        {
            await base.GetValueOrDefault_in_order_by(isAsync);

            AssertSql(
                $@"SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
ORDER BY IIf(`w`.`SynergyWithId` IS NULL, 0, `w`.`SynergyWithId`), `w`.`Id`");
        }

        public override async Task GetValueOrDefault_with_argument(bool isAsync)
        {
            await base.GetValueOrDefault_with_argument(isAsync);

            AssertSql(
                $@"SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE IIf(`w`.`SynergyWithId` IS NULL, 0, `w`.`SynergyWithId`) = 1");
        }

        public override async Task GetValueOrDefault_with_argument_complex(bool isAsync)
        {
            await base.GetValueOrDefault_with_argument_complex(isAsync);

            AssertSql(
                $@"SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Weapons` AS `w`
WHERE IIf(`w`.`SynergyWithId` IS NULL, 0, `w`.`SynergyWithId`) AS int) + 42) > 10");
        }

        public override async Task Filter_with_complex_predicate_containing_subquery(bool isAsync)
        {
            await base.Filter_with_complex_predicate_containing_subquery(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((`g`.`FullName` <> 'Dom') AND (
    SELECT TOP 1 `w`.`Id`
    FROM `Weapons` AS `w`
    WHERE (`g`.`FullName` = `w`.`OwnerFullName`) AND (`w`.`IsAutomatic` = True)
    ORDER BY `w`.`Id`) IS NOT NULL)");
        }

        public override async Task Query_with_complex_let_containing_ordering_and_filter_projecting_firstOrDefault_element_of_let(
            bool isAsync)
        {
            await base.Query_with_complex_let_containing_ordering_and_filter_projecting_firstOrDefault_element_of_let(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, (
    SELECT TOP 1 `w`.`Name`
    FROM `Weapons` AS `w`
    WHERE (`g`.`FullName` = `w`.`OwnerFullName`) AND (`w`.`IsAutomatic` = True)
    ORDER BY `w`.`AmmunitionType` DESC) AS `WeaponName`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Nickname` <> 'Dom')");
        }

        public override async Task
            Null_semantics_is_correctly_applied_for_function_comparisons_that_take_arguments_from_optional_navigation(bool isAsync)
        {
            await base.Null_semantics_is_correctly_applied_for_function_comparisons_that_take_arguments_from_optional_navigation(isAsync);

            AssertSql(
                $@"");
        }

        public override async Task
            Null_semantics_is_correctly_applied_for_function_comparisons_that_take_arguments_from_optional_navigation_complex(bool isAsync)
        {
            await base.Null_semantics_is_correctly_applied_for_function_comparisons_that_take_arguments_from_optional_navigation_complex(
                isAsync);

            AssertSql(
                $@"SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`Note`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
LEFT JOIN `Squads` AS `s` ON `t0`.`SquadId` = `s`.`Id`
WHERE (SUBSTRING(`t`.`Note`, 0 + 1, CAST(LEN(`s`.`Name`) AS int)) = `t`.`GearNickName`) OR (SUBSTRING(`t`.`Note`, 0 + 1, CAST(LEN(`s`.`Name`) AS int)) IS NULL AND `t`.`GearNickName` IS NULL)");
        }

        public override async Task Filter_with_new_Guid(bool isAsync)
        {
            await base.Filter_with_new_Guid(isAsync);

            AssertSql(
                $@"SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`Note`
FROM `Tags` AS `t`
WHERE `t`.`Id` = 'df36f493-463f-4123-83f9-6b135deeb7ba'");
        }

        public override async Task Filter_with_new_Guid_closure(bool isAsync)
        {
            await base.Filter_with_new_Guid_closure(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='df36f493-463f-4123-83f9-6b135deeb7bd'")}

SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`Note`
FROM `Tags` AS `t`
WHERE `t`.`Id` = {AssertSqlHelper.Parameter("@__p_0")}",
                //
                $@"{AssertSqlHelper.Declaration("@__p_0='b39a6fba-9026-4d69-828e-fd7068673e57'")}

SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`Note`
FROM `Tags` AS `t`
WHERE `t`.`Id` = {AssertSqlHelper.Parameter("@__p_0")}");
        }

        public override async Task OfTypeNav1(bool isAsync)
        {
            await base.OfTypeNav1(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
LEFT JOIN `Tags` AS `t` ON (`g`.`Nickname` = `t`.`GearNickName`) AND (`g`.`SquadId` = `t`.`GearSquadId`)
LEFT JOIN `Tags` AS `t0` ON (`g`.`Nickname` = `t0`.`GearNickName`) AND (`g`.`SquadId` = `t0`.`GearSquadId`)
WHERE ((`g`.`Discriminator` IN ('Gear', 'Officer') AND ((`t`.`Note` <> 'Foo') OR `t`.`Note` IS NULL)) AND (`g`.`Discriminator` = 'Officer')) AND ((`t0`.`Note` <> 'Bar') OR `t0`.`Note` IS NULL)");
        }

        public override async Task OfTypeNav2(bool isAsync)
        {
            await base.OfTypeNav2(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
LEFT JOIN `Tags` AS `t` ON (`g`.`Nickname` = `t`.`GearNickName`) AND (`g`.`SquadId` = `t`.`GearSquadId`)
LEFT JOIN `Cities` AS `c` ON `g`.`AssignedCityName` = `c`.`Name`
WHERE ((`g`.`Discriminator` IN ('Gear', 'Officer') AND ((`t`.`Note` <> 'Foo') OR `t`.`Note` IS NULL)) AND (`g`.`Discriminator` = 'Officer')) AND ((`c`.`Location` <> 'Bar') OR `c`.`Location` IS NULL)");
        }

        public override async Task OfTypeNav3(bool isAsync)
        {
            await base.OfTypeNav3(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
LEFT JOIN `Tags` AS `t` ON (`g`.`Nickname` = `t`.`GearNickName`) AND (`g`.`SquadId` = `t`.`GearSquadId`)
INNER JOIN `Weapons` AS `w` ON `g`.`FullName` = `w`.`OwnerFullName`
LEFT JOIN `Tags` AS `t0` ON (`g`.`Nickname` = `t0`.`GearNickName`) AND (`g`.`SquadId` = `t0`.`GearSquadId`)
WHERE ((`g`.`Discriminator` IN ('Gear', 'Officer') AND ((`t`.`Note` <> 'Foo') OR `t`.`Note` IS NULL)) AND (`g`.`Discriminator` = 'Officer')) AND ((`t0`.`Note` <> 'Bar') OR `t0`.`Note` IS NULL)");
        }

        public override void Nav_rewrite_Distinct_with_convert()
        {
            base.Nav_rewrite_Distinct_with_convert();

            AssertSql(
                $@"");
        }

        public override void Nav_rewrite_Distinct_with_convert_anonymous()
        {
            base.Nav_rewrite_Distinct_with_convert_anonymous();

            AssertSql(
                $@"");
        }

        public override async Task Nav_rewrite_with_convert1(bool isAsync)
        {
            await base.Nav_rewrite_with_convert1(isAsync);

            AssertSql(
                $@"SELECT `t`.`Name`, `t`.`Discriminator`, `t`.`LocustHordeId`, `t`.`ThreatLevel`, `t`.`DefeatedByNickname`, `t`.`DefeatedBySquadId`, `t`.`HighCommandId`
FROM `Factions` AS `f`
LEFT JOIN `Cities` AS `c` ON `f`.`CapitalName` = `c`.`Name`
LEFT JOIN (
    SELECT `l`.`Name`, `l`.`Discriminator`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`DefeatedByNickname`, `l`.`DefeatedBySquadId`, `l`.`HighCommandId`
    FROM `LocustLeaders` AS `l`
    WHERE `l`.`Discriminator` = 'LocustCommander'
) AS `t` ON `f`.`CommanderName` = `t`.`Name`
WHERE (`f`.`Discriminator` = 'LocustHorde') AND ((`c`.`Name` <> 'Foo') OR `c`.`Name` IS NULL)");
        }

        public override async Task Nav_rewrite_with_convert2(bool isAsync)
        {
            await base.Nav_rewrite_with_convert2(isAsync);

            AssertSql(
                $@"SELECT `f`.`Id`, `f`.`CapitalName`, `f`.`Discriminator`, `f`.`Name`, `f`.`CommanderName`, `f`.`Eradicated`
FROM `Factions` AS `f`
LEFT JOIN `Cities` AS `c` ON `f`.`CapitalName` = `c`.`Name`
LEFT JOIN (
    SELECT `l`.`Name`, `l`.`Discriminator`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`DefeatedByNickname`, `l`.`DefeatedBySquadId`, `l`.`HighCommandId`
    FROM `LocustLeaders` AS `l`
    WHERE `l`.`Discriminator` = 'LocustCommander'
) AS `t` ON `f`.`CommanderName` = `t`.`Name`
WHERE ((`f`.`Discriminator` = 'LocustHorde') AND ((`c`.`Name` <> 'Foo') OR `c`.`Name` IS NULL)) AND ((`t`.`Name` <> 'Bar') OR `t`.`Name` IS NULL)");
        }

        public override async Task Nav_rewrite_with_convert3(bool isAsync)
        {
            await base.Nav_rewrite_with_convert3(isAsync);

            AssertSql(
                $@"SELECT `f`.`Id`, `f`.`CapitalName`, `f`.`Discriminator`, `f`.`Name`, `f`.`CommanderName`, `f`.`Eradicated`
FROM `Factions` AS `f`
LEFT JOIN `Cities` AS `c` ON `f`.`CapitalName` = `c`.`Name`
LEFT JOIN (
    SELECT `l`.`Name`, `l`.`Discriminator`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`DefeatedByNickname`, `l`.`DefeatedBySquadId`, `l`.`HighCommandId`
    FROM `LocustLeaders` AS `l`
    WHERE `l`.`Discriminator` = 'LocustCommander'
) AS `t` ON `f`.`CommanderName` = `t`.`Name`
WHERE ((`f`.`Discriminator` = 'LocustHorde') AND ((`c`.`Name` <> 'Foo') OR `c`.`Name` IS NULL)) AND ((`t`.`Name` <> 'Bar') OR `t`.`Name` IS NULL)");
        }

        public override async Task Where_contains_on_navigation_with_composite_keys(bool isAsync)
        {
            await base.Where_contains_on_navigation_with_composite_keys(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND EXISTS (
    SELECT 1
    FROM `Cities` AS `c`
    WHERE EXISTS (
        SELECT 1
        FROM `Gears` AS `g0`
        WHERE (`g0`.`Discriminator` IN ('Gear', 'Officer') AND (`c`.`Name` = `g0`.`CityOfBirthName`)) AND ((`g0`.`Nickname` = `g`.`Nickname`) AND (`g0`.`SquadId` = `g`.`SquadId`))))");
        }

        public override async Task Include_with_complex_order_by(bool isAsync)
        {
            await base.Include_with_complex_order_by(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
FROM `Gears` AS `g`
LEFT JOIN `Weapons` AS `w` ON `g`.`FullName` = `w`.`OwnerFullName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
ORDER BY (
    SELECT TOP 1 `w0`.`Name`
    FROM `Weapons` AS `w0`
    WHERE (`g`.`FullName` = `w0`.`OwnerFullName`) AND (CHARINDEX('Gnasher', `w0`.`Name`) > 0)), `g`.`Nickname`, `g`.`SquadId`, `w`.`Id`");
        }

        public override async Task Anonymous_projection_take_followed_by_projecting_single_element_from_collection_navigation(bool isAsync)
        {
            await base.Anonymous_projection_take_followed_by_projecting_single_element_from_collection_navigation(isAsync);

            AssertSql(
                $@"");
        }

        public override async Task Bool_projection_from_subquery_treated_appropriately_in_where(bool isAsync)
        {
            await base.Bool_projection_from_subquery_treated_appropriately_in_where(isAsync);

            AssertSql(
                $@"SELECT `c`.`Name`, `c`.`Location`, `c`.`Nation`
FROM `Cities` AS `c`
WHERE (
    SELECT TOP 1 `g`.`HasSoulPatch`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
    ORDER BY `g`.`Nickname`, `g`.`SquadId`) = True");
        }

        public override async Task DateTimeOffset_Contains_Less_than_Greater_than(bool isAsync)
        {
            await base.DateTimeOffset_Contains_Less_than_Greater_than(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__start_0='1902-01-01T10:00:00.1234567+01:30'")}

{AssertSqlHelper.Declaration("@__end_1='1902-01-03T10:00:00.1234567+01:30'")}

SELECT `m`.`Id`, `m`.`CodeName`, `m`.`Rating`, `m`.`Timeline`
FROM `Missions` AS `m`
WHERE (({AssertSqlHelper.Parameter("@__start_0")} <= CAST(CONVERT(date, `m`.`Timeline`) AS datetimeoffset)) AND (`m`.`Timeline` < {AssertSqlHelper.Parameter("@__end_1")})) AND `m`.`Timeline` IN ('1902-01-02T10:00:00.1234567+01:30')");
        }

        public override async Task Navigation_inside_interpolated_string_expanded(bool isAsync)
        {
            await base.Navigation_inside_interpolated_string_expanded(isAsync);

            AssertSql(
                $@"SELECT IIF(`w`.`SynergyWithId` IS NOT NULL, 1, 0), `w0`.`OwnerFullName`
FROM `Weapons` AS `w`
LEFT JOIN `Weapons` AS `w0` ON `w`.`SynergyWithId` = `w0`.`Id`");
        }

        public override async Task Left_join_projection_using_coalesce_tracking(bool isAsync)
        {
            await base.Left_join_projection_using_coalesce_tracking(isAsync);

            AssertSql(
                $@"SELECT `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`Discriminator`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`, `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON `g`.`LeaderNickname` = `t`.`Nickname`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')");
        }

        public override async Task Left_join_projection_using_conditional_tracking(bool isAsync)
        {
            await base.Left_join_projection_using_conditional_tracking(isAsync);

            AssertSql(
                $@"SELECT IIF(`t`.`Nickname` IS NULL, 1, 0), `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`, `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`Discriminator`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON `g`.`LeaderNickname` = `t`.`Nickname`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')");
        }

        public override async Task Project_collection_navigation_nested_with_take_composite_key(bool isAsync)
        {
            await base.Project_collection_navigation_nested_with_take_composite_key(isAsync);

            AssertSql(
                $@"SELECT `t`.`Id`, `t1`.`Nickname`, `t1`.`SquadId`, `t1`.`AssignedCityName`, `t1`.`CityOfBirthName`, `t1`.`Discriminator`, `t1`.`FullName`, `t1`.`HasSoulPatch`, `t1`.`LeaderNickname`, `t1`.`LeaderSquadId`, `t1`.`Rank`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
OUTER APPLY (
    SELECT TOP 50 `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer') AND (`t0`.`Nickname` IS NOT NULL AND ((`t0`.`Nickname` = `g0`.`LeaderNickname`) AND (`t0`.`SquadId` = `g0`.`LeaderSquadId`)))
) AS `t1`
WHERE `t0`.`Discriminator` = 'Officer'
ORDER BY `t`.`Id`, `t1`.`Nickname`, `t1`.`SquadId`");
        }

        public override async Task Project_collection_navigation_nested_composite_key(bool isAsync)
        {
            await base.Project_collection_navigation_nested_composite_key(isAsync);

            AssertSql(
                $@"SELECT `t`.`Id`, `t1`.`Nickname`, `t1`.`SquadId`, `t1`.`AssignedCityName`, `t1`.`CityOfBirthName`, `t1`.`Discriminator`, `t1`.`FullName`, `t1`.`HasSoulPatch`, `t1`.`LeaderNickname`, `t1`.`LeaderSquadId`, `t1`.`Rank`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
LEFT JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t1` ON ((`t0`.`Nickname` = `t1`.`LeaderNickname`) OR (`t0`.`Nickname` IS NULL AND `t1`.`LeaderNickname` IS NULL)) AND (`t0`.`SquadId` = `t1`.`LeaderSquadId`)
WHERE `t0`.`Discriminator` = 'Officer'
ORDER BY `t`.`Id`, `t1`.`Nickname`, `t1`.`SquadId`");
        }

        public override async Task Null_checks_in_correlated_predicate_are_correctly_translated(bool isAsync)
        {
            await base.Null_checks_in_correlated_predicate_are_correctly_translated(isAsync);

            AssertSql(
                $@"SELECT `t`.`Id`, `t0`.`Nickname`, `t0`.`SquadId`, `t0`.`AssignedCityName`, `t0`.`CityOfBirthName`, `t0`.`Discriminator`, `t0`.`FullName`, `t0`.`HasSoulPatch`, `t0`.`LeaderNickname`, `t0`.`LeaderSquadId`, `t0`.`Rank`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (((`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)) AND `t`.`Note` IS NOT NULL) AND `t`.`Note` IS NOT NULL
ORDER BY `t`.`Id`, `t0`.`Nickname`, `t0`.`SquadId`");
        }

        public override async Task SelectMany_Where_DefaultIfEmpty_with_navigation_in_the_collection_selector(bool isAsync)
        {
            await base.SelectMany_Where_DefaultIfEmpty_with_navigation_in_the_collection_selector(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__isAutomatic_0='True'")}

SELECT `g`.`Nickname`, `g`.`FullName`, IIF(`t`.`Id` IS NOT NULL, 1, 0) AS `Collection`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `w`.`Id`, `w`.`AmmunitionType`, `w`.`IsAutomatic`, `w`.`Name`, `w`.`OwnerFullName`, `w`.`SynergyWithId`
    FROM `Weapons` AS `w`
    WHERE `w`.`IsAutomatic` = {AssertSqlHelper.Parameter("@__isAutomatic_0")}
) AS `t` ON `g`.`FullName` = `t`.`OwnerFullName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')");
        }

        public override async Task Join_with_inner_being_a_subquery_projecting_single_property(bool isAsync)
        {
            await base.Join_with_inner_being_a_subquery_projecting_single_property(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
INNER JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON `g`.`Nickname` = `t`.`Nickname`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')");
        }

        public override async Task Join_with_inner_being_a_subquery_projecting_anonymous_type_with_single_property(bool isAsync)
        {
            await base.Join_with_inner_being_a_subquery_projecting_anonymous_type_with_single_property(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
INNER JOIN (
    SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
    FROM `Gears` AS `g0`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t` ON `g`.`Nickname` = `t`.`Nickname`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')");
        }

        public override async Task Navigation_based_on_complex_expression1(bool isAsync)
        {
            await base.Navigation_based_on_complex_expression1(isAsync);

            AssertSql(
                $@"");
        }

        public override async Task Navigation_based_on_complex_expression2(bool isAsync)
        {
            await base.Navigation_based_on_complex_expression2(isAsync);

            AssertSql(
                $@"SELECT `f`.`Id`, `f`.`CapitalName`, `f`.`Discriminator`, `f`.`Name`, `f`.`CommanderName`, `f`.`Eradicated`
FROM `Factions` AS `f`
LEFT JOIN (
    SELECT `l`.`Name`, `l`.`Discriminator`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`DefeatedByNickname`, `l`.`DefeatedBySquadId`, `l`.`HighCommandId`
    FROM `LocustLeaders` AS `l`
    WHERE `l`.`Discriminator` = 'LocustCommander'
) AS `t` ON `f`.`CommanderName` = `t`.`Name`
WHERE ((`f`.`Discriminator` = 'LocustHorde') AND (`f`.`Discriminator` = 'LocustHorde')) AND `t`.`Name` IS NOT NULL");
        }

        public override async Task Navigation_based_on_complex_expression3(bool isAsync)
        {
            await base.Navigation_based_on_complex_expression3(isAsync);

            AssertSql(
                $@"SELECT `t`.`Name`, `t`.`Discriminator`, `t`.`LocustHordeId`, `t`.`ThreatLevel`, `t`.`DefeatedByNickname`, `t`.`DefeatedBySquadId`, `t`.`HighCommandId`
FROM `Factions` AS `f`
LEFT JOIN (
    SELECT `l`.`Name`, `l`.`Discriminator`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`DefeatedByNickname`, `l`.`DefeatedBySquadId`, `l`.`HighCommandId`
    FROM `LocustLeaders` AS `l`
    WHERE `l`.`Discriminator` = 'LocustCommander'
) AS `t` ON `f`.`CommanderName` = `t`.`Name`
WHERE (`f`.`Discriminator` = 'LocustHorde') AND (`f`.`Discriminator` = 'LocustHorde')");
        }

        public override async Task Navigation_based_on_complex_expression4(bool isAsync)
        {
            await base.Navigation_based_on_complex_expression4(isAsync);

            AssertSql(
                $@"");
        }

        public override async Task Navigation_based_on_complex_expression5(bool isAsync)
        {
            await base.Navigation_based_on_complex_expression5(isAsync);

            AssertSql(
                $@"");
        }

        public override async Task Navigation_based_on_complex_expression6(bool isAsync)
        {
            await base.Navigation_based_on_complex_expression6(isAsync);

            AssertSql(
                $@"");
        }

        public override async Task Select_as_operator(bool isAsync)
        {
            await base.Select_as_operator(isAsync);

            AssertSql(
                $@"SELECT `l`.`Name`, `l`.`Discriminator`, `l`.`LocustHordeId`, `l`.`ThreatLevel`, `l`.`DefeatedByNickname`, `l`.`DefeatedBySquadId`, `l`.`HighCommandId`
FROM `LocustLeaders` AS `l`
WHERE `l`.`Discriminator` IN ('LocustLeader', 'LocustCommander')");
        }

        public override async Task Select_datetimeoffset_comparison_in_projection(bool isAsync)
        {
            await base.Select_datetimeoffset_comparison_in_projection(isAsync);

            AssertSql(
                $@"SELECT IIF(`m`.`Timeline` > SYSDATETIMEOFFSET(), 1, 0)
FROM `Missions` AS `m`");
        }

        public override async Task OfType_in_subquery_works(bool isAsync)
        {
            await base.OfType_in_subquery_works(isAsync);

            AssertSql(
                $@"SELECT `t`.`Name`, `t`.`Location`, `t`.`Nation`
FROM `Gears` AS `g`
INNER JOIN (
    SELECT `c`.`Name`, `c`.`Location`, `c`.`Nation`, `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`
    FROM `Gears` AS `g0`
    LEFT JOIN `Cities` AS `c` ON `g0`.`AssignedCityName` = `c`.`Name`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer') AND (`g0`.`Discriminator` = 'Officer')
) AS `t` ON (`g`.`Nickname` = `t`.`LeaderNickname`) AND (`g`.`SquadId` = `t`.`LeaderSquadId`)
WHERE `g`.`Discriminator` = 'Officer'");
        }

        public override async Task Nullable_bool_comparison_is_translated_to_server(bool isAsync)
        {
            await base.Nullable_bool_comparison_is_translated_to_server(isAsync);

            AssertSql(
                $@"SELECT IIF((`f`.`Eradicated` = True) AND `f`.`Eradicated` IS NOT NULL, 1, 0) AS `IsEradicated`
FROM `Factions` AS `f`
WHERE `f`.`Discriminator` = 'LocustHorde'");
        }

        public override async Task Acessing_reference_navigation_collection_composition_generates_single_query(bool isAsync)
        {
            await base.Acessing_reference_navigation_collection_composition_generates_single_query(isAsync);

            AssertSql(
                $@"SELECT `g`.`Nickname`, `g`.`SquadId`, `t`.`Id`, `t`.`IsAutomatic`, `t`.`Name`
FROM `Gears` AS `g`
LEFT JOIN (
    SELECT `w`.`Id`, `w`.`IsAutomatic`, `w0`.`Name`, `w`.`OwnerFullName`
    FROM `Weapons` AS `w`
    LEFT JOIN `Weapons` AS `w0` ON `w`.`SynergyWithId` = `w0`.`Id`
) AS `t` ON `g`.`FullName` = `t`.`OwnerFullName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
ORDER BY `g`.`Nickname`, `g`.`SquadId`, `t`.`Id`");
        }

        public override async Task Reference_include_chain_loads_correctly_when_middle_is_null(bool isAsync)
        {
            await base.Reference_include_chain_loads_correctly_when_middle_is_null(isAsync);

            AssertSql(
                $@"SELECT `t`.`Id`, `t`.`GearNickName`, `t`.`GearSquadId`, `t`.`Note`, `t0`.`Nickname`, `t0`.`SquadId`, `t0`.`AssignedCityName`, `t0`.`CityOfBirthName`, `t0`.`Discriminator`, `t0`.`FullName`, `t0`.`HasSoulPatch`, `t0`.`LeaderNickname`, `t0`.`LeaderSquadId`, `t0`.`Rank`, `s`.`Id`, `s`.`InternalNumber`, `s`.`Name`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
LEFT JOIN `Squads` AS `s` ON `t0`.`SquadId` = `s`.`Id`
ORDER BY `t`.`Note`");
        }

        public override async Task Accessing_property_of_optional_navigation_in_child_projection_works(bool isAsync)
        {
            await base.Accessing_property_of_optional_navigation_in_child_projection_works(isAsync);

            AssertSql(
                $@"SELECT IIF(`t0`.`Nickname` IS NOT NULL, 1, 0), `t`.`Id`, `t2`.`Nickname`, `t2`.`Id`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
LEFT JOIN (
    SELECT `t1`.`Nickname`, `w`.`Id`, `w`.`OwnerFullName`
    FROM `Weapons` AS `w`
    LEFT JOIN (
        SELECT `g0`.`Nickname`, `g0`.`SquadId`, `g0`.`AssignedCityName`, `g0`.`CityOfBirthName`, `g0`.`Discriminator`, `g0`.`FullName`, `g0`.`HasSoulPatch`, `g0`.`LeaderNickname`, `g0`.`LeaderSquadId`, `g0`.`Rank`
        FROM `Gears` AS `g0`
        WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
    ) AS `t1` ON `w`.`OwnerFullName` = `t1`.`FullName`
) AS `t2` ON `t0`.`FullName` = `t2`.`OwnerFullName`
ORDER BY `t`.`Note`, `t`.`Id`, `t2`.`Id`");
        }

        public override async Task Collection_navigation_ofType_filter_works(bool isAsync)
        {
            await base.Collection_navigation_ofType_filter_works(isAsync);

            AssertSql(
                $@"SELECT `c`.`Name`, `c`.`Location`, `c`.`Nation`
FROM `Cities` AS `c`
WHERE EXISTS (
    SELECT 1
    FROM `Gears` AS `g`
    WHERE ((`g`.`Discriminator` IN ('Gear', 'Officer') AND (`c`.`Name` = `g`.`CityOfBirthName`)) AND (`g`.`Discriminator` = 'Officer')) AND (`g`.`Nickname` = 'Marcus'))");
        }

        public override async Task Query_reusing_parameter_doesnt_declare_duplicate_parameter(bool isAsync)
        {
            await base.Query_reusing_parameter_doesnt_declare_duplicate_parameter(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__prm_Inner_Nickname_0='Marcus' (Size = 450)")}

SELECT `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`Discriminator`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`
FROM (
    SELECT DISTINCT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE (`g`.`Discriminator` IN ('Gear', 'Officer') AND (`g`.`Nickname` <> {AssertSqlHelper.Parameter("@__prm_Inner_Nickname_0")})) AND (`g`.`Nickname` <> {AssertSqlHelper.Parameter("@__prm_Inner_Nickname_0")})
) AS `t`
ORDER BY `t`.`FullName`");
        }

        public override async Task Query_reusing_parameter_doesnt_declare_duplicate_parameter_complex(bool isAsync)
        {
            await base.Query_reusing_parameter_doesnt_declare_duplicate_parameter_complex(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__entity_equality_prm_Inner_Squad_0_Id='1' (Nullable = true)")}

SELECT `t`.`Nickname`, `t`.`SquadId`, `t`.`AssignedCityName`, `t`.`CityOfBirthName`, `t`.`Discriminator`, `t`.`FullName`, `t`.`HasSoulPatch`, `t`.`LeaderNickname`, `t`.`LeaderSquadId`, `t`.`Rank`
FROM (
    SELECT DISTINCT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    INNER JOIN `Squads` AS `s` ON `g`.`SquadId` = `s`.`Id`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (`s`.`Id` = {AssertSqlHelper.Parameter("@__entity_equality_prm_Inner_Squad_0_Id")})
) AS `t`
INNER JOIN `Squads` AS `s0` ON `t`.`SquadId` = `s0`.`Id`
WHERE `s0`.`Id` = {AssertSqlHelper.Parameter("@__entity_equality_prm_Inner_Squad_0_Id")}
ORDER BY `t`.`FullName`");
        }

        public override async Task Complex_GroupBy_after_set_operator(bool isAsync)
        {
            await base.Complex_GroupBy_after_set_operator(isAsync);

            AssertSql(
                $@"SELECT `t`.`Name`, `t`.`Count`, SUM(`t`.`Count`) AS `Sum`
FROM (
    SELECT `c`.`Name`, (
        SELECT COUNT(*)
        FROM `Weapons` AS `w`
        WHERE `g`.`FullName` = `w`.`OwnerFullName`) AS `Count`
    FROM `Gears` AS `g`
    LEFT JOIN `Cities` AS `c` ON `g`.`AssignedCityName` = `c`.`Name`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
    UNION ALL
    SELECT `c0`.`Name`, (
        SELECT COUNT(*)
        FROM `Weapons` AS `w0`
        WHERE `g0`.`FullName` = `w0`.`OwnerFullName`) AS `Count`
    FROM `Gears` AS `g0`
    INNER JOIN `Cities` AS `c0` ON `g0`.`CityOfBirthName` = `c0`.`Name`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t`
GROUP BY `t`.`Name`, `t`.`Count`");
        }

        public override async Task Complex_GroupBy_after_set_operator_using_result_selector(bool isAsync)
        {
            await base.Complex_GroupBy_after_set_operator_using_result_selector(isAsync);

            AssertSql(
                $@"SELECT `t`.`Name`, `t`.`Count`, SUM(`t`.`Count`) AS `Sum`
FROM (
    SELECT `c`.`Name`, (
        SELECT COUNT(*)
        FROM `Weapons` AS `w`
        WHERE `g`.`FullName` = `w`.`OwnerFullName`) AS `Count`
    FROM `Gears` AS `g`
    LEFT JOIN `Cities` AS `c` ON `g`.`AssignedCityName` = `c`.`Name`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
    UNION ALL
    SELECT `c0`.`Name`, (
        SELECT COUNT(*)
        FROM `Weapons` AS `w0`
        WHERE `g0`.`FullName` = `w0`.`OwnerFullName`) AS `Count`
    FROM `Gears` AS `g0`
    INNER JOIN `Cities` AS `c0` ON `g0`.`CityOfBirthName` = `c0`.`Name`
    WHERE `g0`.`Discriminator` IN ('Gear', 'Officer')
) AS `t`
GROUP BY `t`.`Name`, `t`.`Count`");
        }

        public override async Task Left_join_with_GroupBy_with_composite_group_key(bool isAsync)
        {
            await base.Left_join_with_GroupBy_with_composite_group_key(isAsync);

            AssertSql(
                $@"SELECT `g`.`CityOfBirthName`, `g`.`HasSoulPatch`
FROM `Gears` AS `g`
INNER JOIN `Squads` AS `s` ON `g`.`SquadId` = `s`.`Id`
LEFT JOIN `Tags` AS `t` ON `g`.`Nickname` = `t`.`GearNickName`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
GROUP BY `g`.`CityOfBirthName`, `g`.`HasSoulPatch`");
        }

        public override async Task GroupBy_with_boolean_grouping_key(bool isAsync)
        {
            await base.GroupBy_with_boolean_grouping_key(isAsync);

            AssertSql(
                $@"SELECT `g`.`CityOfBirthName`, `g`.`HasSoulPatch`, IIF(`g`.`Nickname` = 'Marcus', 1, 0) AS `IsMarcus`, COUNT(*) AS `Count`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
GROUP BY `g`.`CityOfBirthName`, `g`.`HasSoulPatch`, IIF(`g`.`Nickname` = 'Marcus', 1, 0)");
        }

        public override async Task GroupBy_with_boolean_groupin_key_thru_navigation_access(bool isAsync)
        {
            await base.GroupBy_with_boolean_groupin_key_thru_navigation_access(isAsync);

            AssertSql(
                $@"SELECT `t0`.`HasSoulPatch`, LOWER(`s`.`Name`) AS `Name`
FROM `Tags` AS `t`
LEFT JOIN (
    SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
    FROM `Gears` AS `g`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
) AS `t0` ON (`t`.`GearNickName` = `t0`.`Nickname`) AND (`t`.`GearSquadId` = `t0`.`SquadId`)
LEFT JOIN `Squads` AS `s` ON `t0`.`SquadId` = `s`.`Id`
GROUP BY `t0`.`HasSoulPatch`, `s`.`Name`");
        }

        public override async Task Group_by_over_projection_with_multiple_properties_accessed_thru_navigation(bool isAsync)
        {
            await base.Group_by_over_projection_with_multiple_properties_accessed_thru_navigation(isAsync);

            AssertSql(
                $@"SELECT `c`.`Name`
FROM `Gears` AS `g`
INNER JOIN `Cities` AS `c` ON `g`.`CityOfBirthName` = `c`.`Name`
LEFT JOIN `Cities` AS `c0` ON `g`.`AssignedCityName` = `c0`.`Name`
INNER JOIN `Squads` AS `s` ON `g`.`SquadId` = `s`.`Id`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
GROUP BY `c`.`Name`");
        }

        public override async Task Group_by_on_StartsWith_with_null_parameter_as_argument(bool isAsync)
        {
            await base.Group_by_on_StartsWith_with_null_parameter_as_argument(isAsync);

            AssertSql(
                $@"");
        }

        public override async Task Group_by_with_having_StartsWith_with_null_parameter_as_argument(bool isAsync)
        {
            await base.Group_by_with_having_StartsWith_with_null_parameter_as_argument(isAsync);

            AssertSql(
                $@"SELECT `g`.`FullName`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
GROUP BY `g`.`FullName`
HAVING False = True");
        }

        public override async Task Select_StartsWith_with_null_parameter_as_argument(bool isAsync)
        {
            await base.Select_StartsWith_with_null_parameter_as_argument(isAsync);

            AssertSql(
                $@"");
        }

        public override async Task Select_null_parameter_is_not_null(bool isAsync)
        {
            await base.Select_null_parameter_is_not_null(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='False'")}

SELECT {AssertSqlHelper.Parameter("@__p_0")}
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer')");
        }

        public override async Task Where_null_parameter_is_not_null(bool isAsync)
        {
            await base.Where_null_parameter_is_not_null(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='False'")}

SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ({AssertSqlHelper.Parameter("@__p_0")} = True)");
        }

        public override async Task OrderBy_StartsWith_with_null_parameter_as_argument(bool isAsync)
        {
            await base.OrderBy_StartsWith_with_null_parameter_as_argument(isAsync);

            AssertSql(
                $@"");
        }

        public override async Task Where_with_enum_flags_parameter(bool isAsync)
        {
            await base.Where_with_enum_flags_parameter(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__rank_0='0' (Nullable = true)")}

SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND ((`g`.`Rank` BAND {AssertSqlHelper.Parameter("@__rank_0")}) = {AssertSqlHelper.Parameter("@__rank_0")})",
                //
                $@"@__rank_0=NULL (DbType = Int32)

SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND `g`.`Rank` BAND {AssertSqlHelper.Parameter("@__rank_0")} IS NULL",
                //
                $@"{AssertSqlHelper.Declaration("@__rank_0='1' (Nullable = true)")}

SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND (((`g`.`Rank` BOR {AssertSqlHelper.Parameter("@__rank_0")}) <> {AssertSqlHelper.Parameter("@__rank_0")}) OR `g`.`Rank` BOR {AssertSqlHelper.Parameter("@__rank_0")} IS NULL)",
                //
                $@"@__rank_0=NULL (DbType = Int32)

SELECT `g`.`Nickname`, `g`.`SquadId`, `g`.`AssignedCityName`, `g`.`CityOfBirthName`, `g`.`Discriminator`, `g`.`FullName`, `g`.`HasSoulPatch`, `g`.`LeaderNickname`, `g`.`LeaderSquadId`, `g`.`Rank`
FROM `Gears` AS `g`
WHERE `g`.`Discriminator` IN ('Gear', 'Officer') AND `g`.`Rank` BOR {AssertSqlHelper.Parameter("@__rank_0")} IS NOT NULL");
        }

        public override async Task FirstOrDefault_navigation_access_entity_equality_in_where_predicate_apply_peneding_selector(bool isAsync)
        {
            await base.FirstOrDefault_navigation_access_entity_equality_in_where_predicate_apply_peneding_selector(isAsync);

            AssertSql(
                $@"SELECT `f`.`Id`, `f`.`CapitalName`, `f`.`Discriminator`, `f`.`Name`, `f`.`CommanderName`, `f`.`Eradicated`
FROM `Factions` AS `f`
LEFT JOIN `Cities` AS `c` ON `f`.`CapitalName` = `c`.`Name`
WHERE (`f`.`Discriminator` = 'LocustHorde') AND ((`c`.`Name` = (
    SELECT TOP 1 `c0`.`Name`
    FROM `Gears` AS `g`
    INNER JOIN `Cities` AS `c0` ON `g`.`CityOfBirthName` = `c0`.`Name`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
    ORDER BY `g`.`Nickname`)) OR (`c`.`Name` IS NULL AND (
    SELECT TOP 1 `c0`.`Name`
    FROM `Gears` AS `g`
    INNER JOIN `Cities` AS `c0` ON `g`.`CityOfBirthName` = `c0`.`Name`
    WHERE `g`.`Discriminator` IN ('Gear', 'Officer')
    ORDER BY `g`.`Nickname`) IS NULL))");
        }

        private void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);
    }
}
