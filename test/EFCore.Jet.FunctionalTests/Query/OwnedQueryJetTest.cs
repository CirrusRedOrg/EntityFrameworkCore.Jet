// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query
{
    public class OwnedQueryJetTest : OwnedQueryRelationalTestBase<OwnedQueryJetTest.OwnedQueryJetFixture>
    {
        public OwnedQueryJetTest(OwnedQueryJetFixture fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
        }

        public override async Task Query_with_owned_entity_equality_operator(bool isAsync)
        {
            await base.Query_with_owned_entity_equality_operator(isAsync);

            AssertSql(
                @"SELECT `o`.`Id`, `o`.`Discriminator`, `o`.`Name`, `t`.`Id`, `t0`.`ClientId`, `t0`.`Id`, `t0`.`OrderDate`, `t0`.`OrderClientId`, `t0`.`OrderId`, `t0`.`Id0`, `t0`.`Detail`, `o`.`PersonAddress_AddressLine`, `o`.`PersonAddress_PlaceType`, `o`.`PersonAddress_ZipCode`, `o`.`PersonAddress_Country_Name`, `o`.`PersonAddress_Country_PlanetId`, `o`.`BranchAddress_BranchName`, `o`.`BranchAddress_PlaceType`, `o`.`BranchAddress_Country_Name`, `o`.`BranchAddress_Country_PlanetId`, `o`.`LeafAAddress_LeafType`, `o`.`LeafAAddress_PlaceType`, `o`.`LeafAAddress_Country_Name`, `o`.`LeafAAddress_Country_PlanetId`
FROM `OwnedPerson` AS `o`
CROSS JOIN (
    SELECT `o0`.`Id`
    FROM `OwnedPerson` AS `o0`
    WHERE `o0`.`Discriminator` = N'LeafB'
) AS `t`
LEFT JOIN (
    SELECT `o1`.`ClientId`, `o1`.`Id`, `o1`.`OrderDate`, `o2`.`OrderClientId`, `o2`.`OrderId`, `o2`.`Id` AS `Id0`, `o2`.`Detail`
    FROM `Order` AS `o1`
    LEFT JOIN `OrderDetail` AS `o2` ON (`o1`.`ClientId` = `o2`.`OrderClientId`) AND (`o1`.`Id` = `o2`.`OrderId`)
) AS `t0` ON `o`.`Id` = `t0`.`ClientId`
WHERE 0 = 1
ORDER BY `o`.`Id`, `t`.`Id`, `t0`.`ClientId`, `t0`.`Id`, `t0`.`OrderClientId`, `t0`.`OrderId`");
        }

        public override async Task Query_for_base_type_loads_all_owned_navs(bool isAsync)
        {
            await base.Query_for_base_type_loads_all_owned_navs(isAsync);

            // See issue #10067
            AssertSql(
                @"SELECT `o`.`Id`, `o`.`Discriminator`, `o`.`Name`, `t`.`ClientId`, `t`.`Id`, `t`.`OrderDate`, `t`.`OrderClientId`, `t`.`OrderId`, `t`.`Id0`, `t`.`Detail`, `o`.`PersonAddress_AddressLine`, `o`.`PersonAddress_PlaceType`, `o`.`PersonAddress_ZipCode`, `o`.`PersonAddress_Country_Name`, `o`.`PersonAddress_Country_PlanetId`, `o`.`BranchAddress_BranchName`, `o`.`BranchAddress_PlaceType`, `o`.`BranchAddress_Country_Name`, `o`.`BranchAddress_Country_PlanetId`, `o`.`LeafBAddress_LeafBType`, `o`.`LeafBAddress_PlaceType`, `o`.`LeafBAddress_Country_Name`, `o`.`LeafBAddress_Country_PlanetId`, `o`.`LeafAAddress_LeafType`, `o`.`LeafAAddress_PlaceType`, `o`.`LeafAAddress_Country_Name`, `o`.`LeafAAddress_Country_PlanetId`
FROM `OwnedPerson` AS `o`
LEFT JOIN (
    SELECT `o0`.`ClientId`, `o0`.`Id`, `o0`.`OrderDate`, `o1`.`OrderClientId`, `o1`.`OrderId`, `o1`.`Id` AS `Id0`, `o1`.`Detail`
    FROM `Order` AS `o0`
    LEFT JOIN `OrderDetail` AS `o1` ON (`o0`.`ClientId` = `o1`.`OrderClientId`) AND (`o0`.`Id` = `o1`.`OrderId`)
) AS `t` ON `o`.`Id` = `t`.`ClientId`
ORDER BY `o`.`Id`, `t`.`ClientId`, `t`.`Id`, `t`.`OrderClientId`, `t`.`OrderId`");
        }

        public override async Task No_ignored_include_warning_when_implicit_load(bool isAsync)
        {
            await base.No_ignored_include_warning_when_implicit_load(isAsync);

            AssertSql(
                @"SELECT COUNT(*)
FROM `OwnedPerson` AS `o`");
        }

        public override async Task Query_for_branch_type_loads_all_owned_navs(bool isAsync)
        {
            await base.Query_for_branch_type_loads_all_owned_navs(isAsync);

            AssertSql(
                @"SELECT `o`.`Id`, `o`.`Discriminator`, `o`.`Name`, `t`.`ClientId`, `t`.`Id`, `t`.`OrderDate`, `t`.`OrderClientId`, `t`.`OrderId`, `t`.`Id0`, `t`.`Detail`, `o`.`PersonAddress_AddressLine`, `o`.`PersonAddress_PlaceType`, `o`.`PersonAddress_ZipCode`, `o`.`PersonAddress_Country_Name`, `o`.`PersonAddress_Country_PlanetId`, `o`.`BranchAddress_BranchName`, `o`.`BranchAddress_PlaceType`, `o`.`BranchAddress_Country_Name`, `o`.`BranchAddress_Country_PlanetId`, `o`.`LeafAAddress_LeafType`, `o`.`LeafAAddress_PlaceType`, `o`.`LeafAAddress_Country_Name`, `o`.`LeafAAddress_Country_PlanetId`
FROM `OwnedPerson` AS `o`
LEFT JOIN (
    SELECT `o0`.`ClientId`, `o0`.`Id`, `o0`.`OrderDate`, `o1`.`OrderClientId`, `o1`.`OrderId`, `o1`.`Id` AS `Id0`, `o1`.`Detail`
    FROM `Order` AS `o0`
    LEFT JOIN `OrderDetail` AS `o1` ON (`o0`.`ClientId` = `o1`.`OrderClientId`) AND (`o0`.`Id` = `o1`.`OrderId`)
) AS `t` ON `o`.`Id` = `t`.`ClientId`
WHERE `o`.`Discriminator` IN ('Branch', 'LeafA')
ORDER BY `o`.`Id`, `t`.`ClientId`, `t`.`Id`, `t`.`OrderClientId`, `t`.`OrderId`");
        }

        public override async Task Query_for_branch_type_loads_all_owned_navs_tracking(bool isAsync)
        {
            await base.Query_for_branch_type_loads_all_owned_navs_tracking(isAsync);

            AssertSql(
                @"SELECT `o`.`Id`, `o`.`Discriminator`, `o`.`Name`, `t`.`ClientId`, `t`.`Id`, `t`.`OrderDate`, `t`.`OrderClientId`, `t`.`OrderId`, `t`.`Id0`, `t`.`Detail`, `o`.`PersonAddress_AddressLine`, `o`.`PersonAddress_PlaceType`, `o`.`PersonAddress_ZipCode`, `o`.`PersonAddress_Country_Name`, `o`.`PersonAddress_Country_PlanetId`, `o`.`BranchAddress_BranchName`, `o`.`BranchAddress_PlaceType`, `o`.`BranchAddress_Country_Name`, `o`.`BranchAddress_Country_PlanetId`, `o`.`LeafAAddress_LeafType`, `o`.`LeafAAddress_PlaceType`, `o`.`LeafAAddress_Country_Name`, `o`.`LeafAAddress_Country_PlanetId`
FROM `OwnedPerson` AS `o`
LEFT JOIN (
    SELECT `o0`.`ClientId`, `o0`.`Id`, `o0`.`OrderDate`, `o1`.`OrderClientId`, `o1`.`OrderId`, `o1`.`Id` AS `Id0`, `o1`.`Detail`
    FROM `Order` AS `o0`
    LEFT JOIN `OrderDetail` AS `o1` ON (`o0`.`ClientId` = `o1`.`OrderClientId`) AND (`o0`.`Id` = `o1`.`OrderId`)
) AS `t` ON `o`.`Id` = `t`.`ClientId`
WHERE `o`.`Discriminator` IN (N'Branch', N'LeafA')
ORDER BY `o`.`Id`, `t`.`ClientId`, `t`.`Id`, `t`.`OrderClientId`, `t`.`OrderId`");
        }

        public override async Task Query_for_leaf_type_loads_all_owned_navs(bool isAsync)
        {
            await base.Query_for_leaf_type_loads_all_owned_navs(isAsync);

            AssertSql(
                @"SELECT `o`.`Id`, `o`.`Discriminator`, `o`.`Name`, `t`.`ClientId`, `t`.`Id`, `t`.`OrderDate`, `t`.`OrderClientId`, `t`.`OrderId`, `t`.`Id0`, `t`.`Detail`, `o`.`PersonAddress_AddressLine`, `o`.`PersonAddress_PlaceType`, `o`.`PersonAddress_ZipCode`, `o`.`PersonAddress_Country_Name`, `o`.`PersonAddress_Country_PlanetId`, `o`.`BranchAddress_BranchName`, `o`.`BranchAddress_PlaceType`, `o`.`BranchAddress_Country_Name`, `o`.`BranchAddress_Country_PlanetId`, `o`.`LeafAAddress_LeafType`, `o`.`LeafAAddress_PlaceType`, `o`.`LeafAAddress_Country_Name`, `o`.`LeafAAddress_Country_PlanetId`
FROM `OwnedPerson` AS `o`
LEFT JOIN (
    SELECT `o0`.`ClientId`, `o0`.`Id`, `o0`.`OrderDate`, `o1`.`OrderClientId`, `o1`.`OrderId`, `o1`.`Id` AS `Id0`, `o1`.`Detail`
    FROM `Order` AS `o0`
    LEFT JOIN `OrderDetail` AS `o1` ON (`o0`.`ClientId` = `o1`.`OrderClientId`) AND (`o0`.`Id` = `o1`.`OrderId`)
) AS `t` ON `o`.`Id` = `t`.`ClientId`
WHERE `o`.`Discriminator` = N'LeafA'
ORDER BY `o`.`Id`, `t`.`ClientId`, `t`.`Id`, `t`.`OrderClientId`, `t`.`OrderId`");
        }
        
        public override async Task Query_when_subquery(bool isAsync)
        {
            await base.Query_when_subquery(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='5'")}

SELECT `t0`.`Id`, `t0`.`Discriminator`, `t0`.`Name`, `t1`.`ClientId`, `t1`.`Id`, `t1`.`OrderDate`, `t1`.`OrderClientId`, `t1`.`OrderId`, `t1`.`Id0`, `t1`.`Detail`, `t0`.`PersonAddress_AddressLine`, `t0`.`PersonAddress_PlaceType`, `t0`.`PersonAddress_ZipCode`, `t0`.`PersonAddress_Country_Name`, `t0`.`PersonAddress_Country_PlanetId`, `t0`.`BranchAddress_BranchName`, `t0`.`BranchAddress_PlaceType`, `t0`.`BranchAddress_Country_Name`, `t0`.`BranchAddress_Country_PlanetId`, `t0`.`LeafBAddress_LeafBType`, `t0`.`LeafBAddress_PlaceType`, `t0`.`LeafBAddress_Country_Name`, `t0`.`LeafBAddress_Country_PlanetId`, `t0`.`LeafAAddress_LeafType`, `t0`.`LeafAAddress_PlaceType`, `t0`.`LeafAAddress_Country_Name`, `t0`.`LeafAAddress_Country_PlanetId`
FROM (
    SELECT TOP(@__p_0) `t`.`Id`, `t`.`Discriminator`, `t`.`Name`, `t`.`PersonAddress_AddressLine`, `t`.`PersonAddress_PlaceType`, `t`.`PersonAddress_ZipCode`, `t`.`PersonAddress_Country_Name`, `t`.`PersonAddress_Country_PlanetId`, `t`.`BranchAddress_BranchName`, `t`.`BranchAddress_PlaceType`, `t`.`BranchAddress_Country_Name`, `t`.`BranchAddress_Country_PlanetId`, `t`.`LeafBAddress_LeafBType`, `t`.`LeafBAddress_PlaceType`, `t`.`LeafBAddress_Country_Name`, `t`.`LeafBAddress_Country_PlanetId`, `t`.`LeafAAddress_LeafType`, `t`.`LeafAAddress_PlaceType`, `t`.`LeafAAddress_Country_Name`, `t`.`LeafAAddress_Country_PlanetId`
    FROM (
        SELECT DISTINCT `o`.`Id`, `o`.`Discriminator`, `o`.`Name`, `o`.`PersonAddress_AddressLine`, `o`.`PersonAddress_PlaceType`, `o`.`PersonAddress_ZipCode`, `o`.`PersonAddress_Country_Name`, `o`.`PersonAddress_Country_PlanetId`, `o`.`BranchAddress_BranchName`, `o`.`BranchAddress_PlaceType`, `o`.`BranchAddress_Country_Name`, `o`.`BranchAddress_Country_PlanetId`, `o`.`LeafBAddress_LeafBType`, `o`.`LeafBAddress_PlaceType`, `o`.`LeafBAddress_Country_Name`, `o`.`LeafBAddress_Country_PlanetId`, `o`.`LeafAAddress_LeafType`, `o`.`LeafAAddress_PlaceType`, `o`.`LeafAAddress_Country_Name`, `o`.`LeafAAddress_Country_PlanetId`
        FROM `OwnedPerson` AS `o`
    ) AS `t`
    ORDER BY `t`.`Id`
) AS `t0`
LEFT JOIN (
    SELECT `o0`.`ClientId`, `o0`.`Id`, `o0`.`OrderDate`, `o1`.`OrderClientId`, `o1`.`OrderId`, `o1`.`Id` AS `Id0`, `o1`.`Detail`
    FROM `Order` AS `o0`
    LEFT JOIN `OrderDetail` AS `o1` ON (`o0`.`ClientId` = `o1`.`OrderClientId`) AND (`o0`.`Id` = `o1`.`OrderId`)
) AS `t1` ON `t0`.`Id` = `t1`.`ClientId`
ORDER BY `t0`.`Id`, `t1`.`ClientId`, `t1`.`Id`, `t1`.`OrderClientId`, `t1`.`OrderId`");
        }

        public override async Task Navigation_rewrite_on_owned_reference_projecting_scalar(bool isAsync)
        {
            await base.Navigation_rewrite_on_owned_reference_projecting_scalar(isAsync);

            AssertSql(
                @"SELECT `o`.`PersonAddress_Country_Name`
FROM `OwnedPerson` AS `o`
WHERE `o`.`PersonAddress_Country_Name` = 'USA'");
        }

        public override async Task Navigation_rewrite_on_owned_reference_projecting_entity(bool isAsync)
        {
            await base.Navigation_rewrite_on_owned_reference_projecting_entity(isAsync);

            AssertSql(
                @"SELECT `o`.`Id`, `o`.`Discriminator`, `o`.`Name`, `t`.`ClientId`, `t`.`Id`, `t`.`OrderDate`, `t`.`OrderClientId`, `t`.`OrderId`, `t`.`Id0`, `t`.`Detail`, `o`.`PersonAddress_AddressLine`, `o`.`PersonAddress_PlaceType`, `o`.`PersonAddress_ZipCode`, `o`.`PersonAddress_Country_Name`, `o`.`PersonAddress_Country_PlanetId`, `o`.`BranchAddress_BranchName`, `o`.`BranchAddress_PlaceType`, `o`.`BranchAddress_Country_Name`, `o`.`BranchAddress_Country_PlanetId`, `o`.`LeafBAddress_LeafBType`, `o`.`LeafBAddress_PlaceType`, `o`.`LeafBAddress_Country_Name`, `o`.`LeafBAddress_Country_PlanetId`, `o`.`LeafAAddress_LeafType`, `o`.`LeafAAddress_PlaceType`, `o`.`LeafAAddress_Country_Name`, `o`.`LeafAAddress_Country_PlanetId`
FROM `OwnedPerson` AS `o`
LEFT JOIN (
    SELECT `o0`.`ClientId`, `o0`.`Id`, `o0`.`OrderDate`, `o1`.`OrderClientId`, `o1`.`OrderId`, `o1`.`Id` AS `Id0`, `o1`.`Detail`
    FROM `Order` AS `o0`
    LEFT JOIN `OrderDetail` AS `o1` ON (`o0`.`ClientId` = `o1`.`OrderClientId`) AND (`o0`.`Id` = `o1`.`OrderId`)
) AS `t` ON `o`.`Id` = `t`.`ClientId`
WHERE `o`.`PersonAddress_Country_Name` = 'USA'
ORDER BY `o`.`Id`, `t`.`ClientId`, `t`.`Id`, `t`.`OrderClientId`, `t`.`OrderId`");
        }

        public override async Task Navigation_rewrite_on_owned_collection(bool isAsync)
        {
            await base.Navigation_rewrite_on_owned_collection(isAsync);

            AssertSql(
                @"SELECT `o`.`Id`, `t`.`ClientId`, `t`.`Id`, `t`.`OrderDate`, `t`.`OrderClientId`, `t`.`OrderId`, `t`.`Id0`, `t`.`Detail`
FROM `OwnedPerson` AS `o`
LEFT JOIN (
    SELECT `o1`.`ClientId`, `o1`.`Id`, `o1`.`OrderDate`, `o2`.`OrderClientId`, `o2`.`OrderId`, `o2`.`Id` AS `Id0`, `o2`.`Detail`
    FROM `Order` AS `o1`
    LEFT JOIN `OrderDetail` AS `o2` ON (`o1`.`ClientId` = `o2`.`OrderClientId`) AND (`o1`.`Id` = `o2`.`OrderId`)
) AS `t` ON `o`.`Id` = `t`.`ClientId`
WHERE (
    SELECT COUNT(*)
    FROM `Order` AS `o0`
    WHERE `o`.`Id` = `o0`.`ClientId`) > 0
ORDER BY `o`.`Id`, `t`.`ClientId`, `t`.`Id`, `t`.`OrderClientId`, `t`.`OrderId`");
        }

        public override async Task Navigation_rewrite_on_owned_collection_with_composition(bool isAsync)
        {
            await base.Navigation_rewrite_on_owned_collection_with_composition(isAsync);

            AssertSql(
                @"SELECT IIF((
        SELECT TOP 1 IIF(`o0`.`Id` <> 42, TRUE, FALSE)
        FROM `Order` AS `o0`
        WHERE `o`.`Id` = `o0`.`ClientId`
        ORDER BY `o0`.`Id`) IS NULL, FALSE, (
        SELECT TOP 1 IIF(`o0`.`Id` <> 42, TRUE, FALSE)
        FROM `Order` AS `o0`
        WHERE `o`.`Id` = `o0`.`ClientId`
        ORDER BY `o0`.`Id`))
FROM `OwnedPerson` AS `o`
ORDER BY `o`.`Id`");
        }

        public override async Task Navigation_rewrite_on_owned_collection_with_composition_complex(bool isAsync)
        {
            await base.Navigation_rewrite_on_owned_collection_with_composition_complex(isAsync);

            AssertSql(
                @"SELECT (
    SELECT TOP(1) `o1`.`PersonAddress_Country_Name`
    FROM `Order` AS `o0`
    LEFT JOIN `OwnedPerson` AS `o1` ON `o0`.`ClientId` = `o1`.`Id`
    WHERE `o`.`Id` = `o0`.`ClientId`
    ORDER BY `o0`.`Id`)
FROM `OwnedPerson` AS `o`");
        }

        public override async Task SelectMany_on_owned_collection(bool isAsync)
        {
            await base.SelectMany_on_owned_collection(isAsync);

            AssertSql(
                @"SELECT `o0`.`ClientId`, `o0`.`Id`, `o0`.`OrderDate`, `o`.`Id`, `o1`.`OrderClientId`, `o1`.`OrderId`, `o1`.`Id`, `o1`.`Detail`
FROM (`OwnedPerson` AS `o`
INNER JOIN `Order` AS `o0` ON `o`.`Id` = `o0`.`ClientId`)
LEFT JOIN `OrderDetail` AS `o1` ON (`o0`.`ClientId` = `o1`.`OrderClientId`) AND (`o0`.`Id` = `o1`.`OrderId`)
ORDER BY `o`.`Id`, `o0`.`ClientId`, `o0`.`Id`, `o1`.`OrderClientId`, `o1`.`OrderId`");
        }

        public override async Task Navigation_rewrite_on_owned_reference_followed_by_regular_entity(bool isAsync)
        {
            await base.Navigation_rewrite_on_owned_reference_followed_by_regular_entity(isAsync);

            AssertSql(
                @"SELECT `p`.`Id`, `p`.`StarId`
FROM `OwnedPerson` AS `o`
LEFT JOIN `Planet` AS `p` ON `o`.`PersonAddress_Country_PlanetId` = `p`.`Id`");
        }

        public override async Task Filter_owned_entity_chained_with_regular_entity_followed_by_projecting_owned_collection(bool isAsync)
        {
            await base.Filter_owned_entity_chained_with_regular_entity_followed_by_projecting_owned_collection(isAsync);

            AssertSql(
                @"SELECT `o`.`Id`, `p`.`Id`, `t`.`ClientId`, `t`.`Id`, `t`.`OrderDate`, `t`.`OrderClientId`, `t`.`OrderId`, `t`.`Id0`, `t`.`Detail`
FROM `OwnedPerson` AS `o`
LEFT JOIN `Planet` AS `p` ON `o`.`PersonAddress_Country_PlanetId` = `p`.`Id`
LEFT JOIN (
    SELECT `o0`.`ClientId`, `o0`.`Id`, `o0`.`OrderDate`, `o1`.`OrderClientId`, `o1`.`OrderId`, `o1`.`Id` AS `Id0`, `o1`.`Detail`
    FROM `Order` AS `o0`
    LEFT JOIN `OrderDetail` AS `o1` ON (`o0`.`ClientId` = `o1`.`OrderClientId`) AND (`o0`.`Id` = `o1`.`OrderId`)
) AS `t` ON `o`.`Id` = `t`.`ClientId`
WHERE (`p`.`Id` <> 42) OR (`p`.`Id` IS NULL)
ORDER BY `o`.`Id`, `p`.`Id`, `t`.`ClientId`, `t`.`Id`, `t`.`OrderClientId`, `t`.`OrderId`");
        }

        public override async Task Project_multiple_owned_navigations(bool isAsync)
        {
            await base.Project_multiple_owned_navigations(isAsync);

            AssertSql(
                @"SELECT `o`.`Id`, `p`.`Id`, `t`.`ClientId`, `t`.`Id`, `t`.`OrderDate`, `t`.`OrderClientId`, `t`.`OrderId`, `t`.`Id0`, `t`.`Detail`, `o`.`PersonAddress_AddressLine`, `o`.`PersonAddress_PlaceType`, `o`.`PersonAddress_ZipCode`, `o`.`PersonAddress_Country_Name`, `o`.`PersonAddress_Country_PlanetId`, `p`.`StarId`
FROM `OwnedPerson` AS `o`
LEFT JOIN `Planet` AS `p` ON `o`.`PersonAddress_Country_PlanetId` = `p`.`Id`
LEFT JOIN (
    SELECT `o0`.`ClientId`, `o0`.`Id`, `o0`.`OrderDate`, `o1`.`OrderClientId`, `o1`.`OrderId`, `o1`.`Id` AS `Id0`, `o1`.`Detail`
    FROM `Order` AS `o0`
    LEFT JOIN `OrderDetail` AS `o1` ON (`o0`.`ClientId` = `o1`.`OrderClientId`) AND (`o0`.`Id` = `o1`.`OrderId`)
) AS `t` ON `o`.`Id` = `t`.`ClientId`
ORDER BY `o`.`Id`, `p`.`Id`, `t`.`ClientId`, `t`.`Id`, `t`.`OrderClientId`, `t`.`OrderId`");
        }

        public override async Task Project_multiple_owned_navigations_with_expansion_on_owned_collections(bool isAsync)
        {
            await base.Project_multiple_owned_navigations_with_expansion_on_owned_collections(isAsync);

            AssertSql(
                @"SELECT (
    SELECT COUNT(*)
    FROM ((`Order` AS `o0`
    LEFT JOIN `OwnedPerson` AS `o1` ON `o0`.`ClientId` = `o1`.`Id`)
    LEFT JOIN `Planet` AS `p0` ON `o1`.`PersonAddress_Country_PlanetId` = `p0`.`Id`)
    LEFT JOIN `Star` AS `s` ON `p0`.`StarId` = `s`.`Id`
    WHERE (`o`.`Id` = `o0`.`ClientId`) AND ((`s`.`Id` <> 42) OR (`s`.`Id` IS NULL))) AS `Count`, `p`.`Id`, `p`.`StarId`
FROM `OwnedPerson` AS `o`
LEFT JOIN `Planet` AS `p` ON `o`.`PersonAddress_Country_PlanetId` = `p`.`Id`
ORDER BY `o`.`Id`");
        }

        public override async Task Navigation_rewrite_on_owned_reference_followed_by_regular_entity_filter(bool isAsync)
        {
            await base.Navigation_rewrite_on_owned_reference_followed_by_regular_entity_filter(isAsync);

            AssertSql(
                @"SELECT `o`.`Id`, `o`.`Discriminator`, `o`.`Name`, `p`.`Id`, `t`.`ClientId`, `t`.`Id`, `t`.`OrderDate`, `t`.`OrderClientId`, `t`.`OrderId`, `t`.`Id0`, `t`.`Detail`, `o`.`PersonAddress_AddressLine`, `o`.`PersonAddress_PlaceType`, `o`.`PersonAddress_ZipCode`, `o`.`PersonAddress_Country_Name`, `o`.`PersonAddress_Country_PlanetId`, `o`.`BranchAddress_BranchName`, `o`.`BranchAddress_PlaceType`, `o`.`BranchAddress_Country_Name`, `o`.`BranchAddress_Country_PlanetId`, `o`.`LeafBAddress_LeafBType`, `o`.`LeafBAddress_PlaceType`, `o`.`LeafBAddress_Country_Name`, `o`.`LeafBAddress_Country_PlanetId`, `o`.`LeafAAddress_LeafType`, `o`.`LeafAAddress_PlaceType`, `o`.`LeafAAddress_Country_Name`, `o`.`LeafAAddress_Country_PlanetId`
FROM `OwnedPerson` AS `o`
LEFT JOIN `Planet` AS `p` ON `o`.`PersonAddress_Country_PlanetId` = `p`.`Id`
LEFT JOIN (
    SELECT `o0`.`ClientId`, `o0`.`Id`, `o0`.`OrderDate`, `o1`.`OrderClientId`, `o1`.`OrderId`, `o1`.`Id` AS `Id0`, `o1`.`Detail`
    FROM `Order` AS `o0`
    LEFT JOIN `OrderDetail` AS `o1` ON (`o0`.`ClientId` = `o1`.`OrderClientId`) AND (`o0`.`Id` = `o1`.`OrderId`)
) AS `t` ON `o`.`Id` = `t`.`ClientId`
WHERE (`p`.`Id` <> 7) OR (`p`.`Id` IS NULL)
ORDER BY `o`.`Id`, `p`.`Id`, `t`.`ClientId`, `t`.`Id`, `t`.`OrderClientId`, `t`.`OrderId`");
        }

        public override async Task Navigation_rewrite_on_owned_reference_followed_by_regular_entity_and_property(bool isAsync)
        {
            await base.Navigation_rewrite_on_owned_reference_followed_by_regular_entity_and_property(isAsync);

            AssertSql(
                @"SELECT `p`.`Id`
FROM `OwnedPerson` AS `o`
LEFT JOIN `Planet` AS `p` ON `o`.`PersonAddress_Country_PlanetId` = `p`.`Id`");
        }

        public override async Task Navigation_rewrite_on_owned_reference_followed_by_regular_entity_and_collection(bool isAsync)
        {
            await base.Navigation_rewrite_on_owned_reference_followed_by_regular_entity_and_collection(isAsync);

            AssertSql(
                @"SELECT `o`.`Id`, `p`.`Id`, `m`.`Id`, `m`.`Diameter`, `m`.`PlanetId`
FROM (`OwnedPerson` AS `o`
LEFT JOIN `Planet` AS `p` ON `o`.`PersonAddress_Country_PlanetId` = `p`.`Id`)
LEFT JOIN `Moon` AS `m` ON `p`.`Id` = `m`.`PlanetId`
ORDER BY `o`.`Id`, `p`.`Id`");
        }

        public override async Task SelectMany_on_owned_reference_followed_by_regular_entity_and_collection(bool isAsync)
        {
            await base.SelectMany_on_owned_reference_followed_by_regular_entity_and_collection(isAsync);

            AssertSql(
                @"SELECT `m`.`Id`, `m`.`Diameter`, `m`.`PlanetId`
FROM `OwnedPerson` AS `o`
LEFT JOIN `Planet` AS `p` ON `o`.`PersonAddress_Country_PlanetId` = `p`.`Id`
INNER JOIN `Moon` AS `m` ON `p`.`Id` = `m`.`PlanetId`");
        }

        public override async Task SelectMany_on_owned_reference_with_entity_in_between_ending_in_owned_collection(bool isAsync)
        {
            await base.SelectMany_on_owned_reference_with_entity_in_between_ending_in_owned_collection(isAsync);

            AssertSql(
                @"SELECT `e`.`Id`, `e`.`Name`, `e`.`StarId`
FROM ((`OwnedPerson` AS `o`
LEFT JOIN `Planet` AS `p` ON `o`.`PersonAddress_Country_PlanetId` = `p`.`Id`)
LEFT JOIN `Star` AS `s` ON `p`.`StarId` = `s`.`Id`)
INNER JOIN `Element` AS `e` ON `s`.`Id` = `e`.`StarId`");
        }

        public override async Task Navigation_rewrite_on_owned_reference_followed_by_regular_entity_and_another_reference(bool isAsync)
        {
            await base.Navigation_rewrite_on_owned_reference_followed_by_regular_entity_and_another_reference(isAsync);

            AssertSql(
                @"SELECT `s`.`Id`, `s`.`Name`, `o`.`Id`, `p`.`Id`, `e`.`Id`, `e`.`Name`, `e`.`StarId`
FROM ((`OwnedPerson` AS `o`
LEFT JOIN `Planet` AS `p` ON `o`.`PersonAddress_Country_PlanetId` = `p`.`Id`)
LEFT JOIN `Star` AS `s` ON `p`.`StarId` = `s`.`Id`)
LEFT JOIN `Element` AS `e` ON `s`.`Id` = `e`.`StarId`
ORDER BY `o`.`Id`, `p`.`Id`, `s`.`Id`");
        }

        public override async Task Navigation_rewrite_on_owned_reference_followed_by_regular_entity_and_another_reference_and_scalar(
            bool isAsync)
        {
            await base.Navigation_rewrite_on_owned_reference_followed_by_regular_entity_and_another_reference_and_scalar(isAsync);

            AssertSql(
                @"SELECT `s`.`Name`
FROM (`OwnedPerson` AS `o`
LEFT JOIN `Planet` AS `p` ON `o`.`PersonAddress_Country_PlanetId` = `p`.`Id`)
LEFT JOIN `Star` AS `s` ON `p`.`StarId` = `s`.`Id`");
        }

        public override async Task
            Navigation_rewrite_on_owned_reference_followed_by_regular_entity_and_another_reference_in_predicate_and_projection(bool isAsync)
        {
            await base.Navigation_rewrite_on_owned_reference_followed_by_regular_entity_and_another_reference_in_predicate_and_projection(
                isAsync);

            AssertSql(
                @"SELECT `s`.`Id`, `s`.`Name`, `o`.`Id`, `p`.`Id`, `e`.`Id`, `e`.`Name`, `e`.`StarId`
FROM ((`OwnedPerson` AS `o`
LEFT JOIN `Planet` AS `p` ON `o`.`PersonAddress_Country_PlanetId` = `p`.`Id`)
LEFT JOIN `Star` AS `s` ON `p`.`StarId` = `s`.`Id`)
LEFT JOIN `Element` AS `e` ON `s`.`Id` = `e`.`StarId`
WHERE `s`.`Name` = 'Sol'
ORDER BY `o`.`Id`, `p`.`Id`, `s`.`Id`");
        }

        public override async Task Query_with_OfType_eagerly_loads_correct_owned_navigations(bool isAsync)
        {
            await base.Query_with_OfType_eagerly_loads_correct_owned_navigations(isAsync);

            AssertSql(
                @"SELECT `o`.`Id`, `o`.`Discriminator`, `o`.`Name`, `t`.`ClientId`, `t`.`Id`, `t`.`OrderDate`, `t`.`OrderClientId`, `t`.`OrderId`, `t`.`Id0`, `t`.`Detail`, `o`.`PersonAddress_AddressLine`, `o`.`PersonAddress_PlaceType`, `o`.`PersonAddress_ZipCode`, `o`.`PersonAddress_Country_Name`, `o`.`PersonAddress_Country_PlanetId`, `o`.`BranchAddress_BranchName`, `o`.`BranchAddress_PlaceType`, `o`.`BranchAddress_Country_Name`, `o`.`BranchAddress_Country_PlanetId`, `o`.`LeafAAddress_LeafType`, `o`.`LeafAAddress_PlaceType`, `o`.`LeafAAddress_Country_Name`, `o`.`LeafAAddress_Country_PlanetId`
FROM `OwnedPerson` AS `o`
LEFT JOIN (
    SELECT `o0`.`ClientId`, `o0`.`Id`, `o0`.`OrderDate`, `o1`.`OrderClientId`, `o1`.`OrderId`, `o1`.`Id` AS `Id0`, `o1`.`Detail`
    FROM `Order` AS `o0`
    LEFT JOIN `OrderDetail` AS `o1` ON (`o0`.`ClientId` = `o1`.`OrderClientId`) AND (`o0`.`Id` = `o1`.`OrderId`)
) AS `t` ON `o`.`Id` = `t`.`ClientId`
WHERE `o`.`Discriminator` = 'LeafA'
ORDER BY `o`.`Id`, `t`.`ClientId`, `t`.`Id`, `t`.`OrderClientId`, `t`.`OrderId`");
        }

        public override async Task Preserve_includes_when_applying_skip_take_after_anonymous_type_select(bool isAsync)
        {
            await base.Preserve_includes_when_applying_skip_take_after_anonymous_type_select(isAsync);

            AssertSql(
                $@"SELECT COUNT(*)
FROM `OwnedPerson` AS `o`
WHERE `o`.`Discriminator` IN ('OwnedPerson', 'Branch', 'LeafB', 'LeafA')",
                //
                $@"{AssertSqlHelper.Declaration("@__Count_0='4'")}

{AssertSqlHelper.Declaration("@__p_1='0'")}

{AssertSqlHelper.Declaration("@__p_2='100'")}

SELECT `t`.`Id`, `t`.`Discriminator`, `t3`.`Id`, `t6`.`Id`, `t6`.`PersonAddress_Country_Name`, `t6`.`PersonAddress_Country_PlanetId`, `t8`.`Id`, `t11`.`Id`, `t11`.`BranchAddress_Country_Name`, `t11`.`BranchAddress_Country_PlanetId`, `t13`.`Id`, `t16`.`Id`, `t16`.`LeafBAddress_Country_Name`, `t16`.`LeafBAddress_Country_PlanetId`, `t18`.`Id`, `t21`.`Id`, `t21`.`LeafAAddress_Country_Name`, `t21`.`LeafAAddress_Country_PlanetId`, `t`.`c`, `o22`.`ClientId`, `o22`.`Id`
FROM (
    SELECT `o`.`Id`, `o`.`Discriminator`, {AssertSqlHelper.Parameter("@__Count_0")} AS `c`
    FROM `OwnedPerson` AS `o`
    WHERE `o`.`Discriminator` IN ('OwnedPerson', 'Branch', 'LeafB', 'LeafA')
    ORDER BY `o`.`Id`
    SKIP {AssertSqlHelper.Parameter("@__p_1")} FETCH NEXT {AssertSqlHelper.Parameter("@__p_2")} ROWS ONLY
) AS `t`
LEFT JOIN (
    SELECT `o0`.`Id`, `t0`.`Id` AS `Id0`
    FROM `OwnedPerson` AS `o0`
    INNER JOIN (
        SELECT `o1`.`Id`, `o1`.`Discriminator`
        FROM `OwnedPerson` AS `o1`
        WHERE `o1`.`Discriminator` IN ('OwnedPerson', 'Branch', 'LeafB', 'LeafA')
    ) AS `t0` ON `o0`.`Id` = `t0`.`Id`
) AS `t1` ON `t`.`Id` = `t1`.`Id`
LEFT JOIN (
    SELECT `o2`.`Id`, `t2`.`Id` AS `Id0`
    FROM `OwnedPerson` AS `o2`
    INNER JOIN (
        SELECT `o3`.`Id`, `o3`.`Discriminator`
        FROM `OwnedPerson` AS `o3`
        WHERE `o3`.`Discriminator` IN ('OwnedPerson', 'Branch', 'LeafB', 'LeafA')
    ) AS `t2` ON `o2`.`Id` = `t2`.`Id`
) AS `t3` ON `t`.`Id` = `t3`.`Id`
LEFT JOIN (
    SELECT `o4`.`Id`, `o4`.`PersonAddress_Country_Name`, `o4`.`PersonAddress_Country_PlanetId`, `t5`.`Id` AS `Id0`, `t5`.`Id0` AS `Id00`
    FROM `OwnedPerson` AS `o4`
    INNER JOIN (
        SELECT `o5`.`Id`, `t4`.`Id` AS `Id0`
        FROM `OwnedPerson` AS `o5`
        INNER JOIN (
            SELECT `o6`.`Id`, `o6`.`Discriminator`
            FROM `OwnedPerson` AS `o6`
            WHERE `o6`.`Discriminator` IN ('OwnedPerson', 'Branch', 'LeafB', 'LeafA')
        ) AS `t4` ON `o5`.`Id` = `t4`.`Id`
    ) AS `t5` ON `o4`.`Id` = `t5`.`Id`
    WHERE `o4`.`PersonAddress_Country_PlanetId` IS NOT NULL
) AS `t6` ON `t3`.`Id` = `t6`.`Id`
LEFT JOIN (
    SELECT `o7`.`Id`, `t7`.`Id` AS `Id0`
    FROM `OwnedPerson` AS `o7`
    INNER JOIN (
        SELECT `o8`.`Id`, `o8`.`Discriminator`
        FROM `OwnedPerson` AS `o8`
        WHERE `o8`.`Discriminator` IN ('Branch', 'LeafA')
    ) AS `t7` ON `o7`.`Id` = `t7`.`Id`
) AS `t8` ON `t`.`Id` = `t8`.`Id`
LEFT JOIN (
    SELECT `o9`.`Id`, `o9`.`BranchAddress_Country_Name`, `o9`.`BranchAddress_Country_PlanetId`, `t10`.`Id` AS `Id0`, `t10`.`Id0` AS `Id00`
    FROM `OwnedPerson` AS `o9`
    INNER JOIN (
        SELECT `o10`.`Id`, `t9`.`Id` AS `Id0`
        FROM `OwnedPerson` AS `o10`
        INNER JOIN (
            SELECT `o11`.`Id`, `o11`.`Discriminator`
            FROM `OwnedPerson` AS `o11`
            WHERE `o11`.`Discriminator` IN ('Branch', 'LeafA')
        ) AS `t9` ON `o10`.`Id` = `t9`.`Id`
    ) AS `t10` ON `o9`.`Id` = `t10`.`Id`
    WHERE `o9`.`BranchAddress_Country_PlanetId` IS NOT NULL
) AS `t11` ON `t8`.`Id` = `t11`.`Id`
LEFT JOIN (
    SELECT `o12`.`Id`, `t12`.`Id` AS `Id0`
    FROM `OwnedPerson` AS `o12`
    INNER JOIN (
        SELECT `o13`.`Id`, `o13`.`Discriminator`
        FROM `OwnedPerson` AS `o13`
        WHERE `o13`.`Discriminator` = 'LeafB'
    ) AS `t12` ON `o12`.`Id` = `t12`.`Id`
) AS `t13` ON `t`.`Id` = `t13`.`Id`
LEFT JOIN (
    SELECT `o14`.`Id`, `o14`.`LeafBAddress_Country_Name`, `o14`.`LeafBAddress_Country_PlanetId`, `t15`.`Id` AS `Id0`, `t15`.`Id0` AS `Id00`
    FROM `OwnedPerson` AS `o14`
    INNER JOIN (
        SELECT `o15`.`Id`, `t14`.`Id` AS `Id0`
        FROM `OwnedPerson` AS `o15`
        INNER JOIN (
            SELECT `o16`.`Id`, `o16`.`Discriminator`
            FROM `OwnedPerson` AS `o16`
            WHERE `o16`.`Discriminator` = 'LeafB'
        ) AS `t14` ON `o15`.`Id` = `t14`.`Id`
    ) AS `t15` ON `o14`.`Id` = `t15`.`Id`
    WHERE `o14`.`LeafBAddress_Country_PlanetId` IS NOT NULL
) AS `t16` ON `t13`.`Id` = `t16`.`Id`
LEFT JOIN (
    SELECT `o17`.`Id`, `t17`.`Id` AS `Id0`
    FROM `OwnedPerson` AS `o17`
    INNER JOIN (
        SELECT `o18`.`Id`, `o18`.`Discriminator`
        FROM `OwnedPerson` AS `o18`
        WHERE `o18`.`Discriminator` = 'LeafA'
    ) AS `t17` ON `o17`.`Id` = `t17`.`Id`
) AS `t18` ON `t`.`Id` = `t18`.`Id`
LEFT JOIN (
    SELECT `o19`.`Id`, `o19`.`LeafAAddress_Country_Name`, `o19`.`LeafAAddress_Country_PlanetId`, `t20`.`Id` AS `Id0`, `t20`.`Id0` AS `Id00`
    FROM `OwnedPerson` AS `o19`
    INNER JOIN (
        SELECT `o20`.`Id`, `t19`.`Id` AS `Id0`
        FROM `OwnedPerson` AS `o20`
        INNER JOIN (
            SELECT `o21`.`Id`, `o21`.`Discriminator`
            FROM `OwnedPerson` AS `o21`
            WHERE `o21`.`Discriminator` = 'LeafA'
        ) AS `t19` ON `o20`.`Id` = `t19`.`Id`
    ) AS `t20` ON `o19`.`Id` = `t20`.`Id`
    WHERE `o19`.`LeafAAddress_Country_PlanetId` IS NOT NULL
) AS `t21` ON `t18`.`Id` = `t21`.`Id`
LEFT JOIN `Order` AS `o22` ON `t`.`Id` = `o22`.`ClientId`
ORDER BY `t`.`Id`, `o22`.`ClientId`, `o22`.`Id`");
        }

        public override async Task Unmapped_property_projection_loads_owned_navigations(bool isAsync)
        {
            await base.Unmapped_property_projection_loads_owned_navigations(isAsync);

            AssertSql(
                $@"SELECT `o`.`Id`, `o`.`Discriminator`, `t0`.`Id`, `t3`.`Id`, `t3`.`PersonAddress_Country_Name`, `t3`.`PersonAddress_Country_PlanetId`, `t5`.`Id`, `t8`.`Id`, `t8`.`BranchAddress_Country_Name`, `t8`.`BranchAddress_Country_PlanetId`, `t10`.`Id`, `t13`.`Id`, `t13`.`LeafBAddress_Country_Name`, `t13`.`LeafBAddress_Country_PlanetId`, `t15`.`Id`, `t18`.`Id`, `t18`.`LeafAAddress_Country_Name`, `t18`.`LeafAAddress_Country_PlanetId`, `o20`.`ClientId`, `o20`.`Id`
FROM `OwnedPerson` AS `o`
LEFT JOIN (
    SELECT `o0`.`Id`, `t`.`Id` AS `Id0`
    FROM `OwnedPerson` AS `o0`
    INNER JOIN (
        SELECT `o1`.`Id`, `o1`.`Discriminator`
        FROM `OwnedPerson` AS `o1`
        WHERE `o1`.`Discriminator` IN ('OwnedPerson', 'Branch', 'LeafB', 'LeafA')
    ) AS `t` ON `o0`.`Id` = `t`.`Id`
) AS `t0` ON `o`.`Id` = `t0`.`Id`
LEFT JOIN (
    SELECT `o2`.`Id`, `o2`.`PersonAddress_Country_Name`, `o2`.`PersonAddress_Country_PlanetId`, `t2`.`Id` AS `Id0`, `t2`.`Id0` AS `Id00`
    FROM `OwnedPerson` AS `o2`
    INNER JOIN (
        SELECT `o3`.`Id`, `t1`.`Id` AS `Id0`
        FROM `OwnedPerson` AS `o3`
        INNER JOIN (
            SELECT `o4`.`Id`, `o4`.`Discriminator`
            FROM `OwnedPerson` AS `o4`
            WHERE `o4`.`Discriminator` IN ('OwnedPerson', 'Branch', 'LeafB', 'LeafA')
        ) AS `t1` ON `o3`.`Id` = `t1`.`Id`
    ) AS `t2` ON `o2`.`Id` = `t2`.`Id`
    WHERE `o2`.`PersonAddress_Country_PlanetId` IS NOT NULL
) AS `t3` ON `t0`.`Id` = `t3`.`Id`
LEFT JOIN (
    SELECT `o5`.`Id`, `t4`.`Id` AS `Id0`
    FROM `OwnedPerson` AS `o5`
    INNER JOIN (
        SELECT `o6`.`Id`, `o6`.`Discriminator`
        FROM `OwnedPerson` AS `o6`
        WHERE `o6`.`Discriminator` IN ('Branch', 'LeafA')
    ) AS `t4` ON `o5`.`Id` = `t4`.`Id`
) AS `t5` ON `o`.`Id` = `t5`.`Id`
LEFT JOIN (
    SELECT `o7`.`Id`, `o7`.`BranchAddress_Country_Name`, `o7`.`BranchAddress_Country_PlanetId`, `t7`.`Id` AS `Id0`, `t7`.`Id0` AS `Id00`
    FROM `OwnedPerson` AS `o7`
    INNER JOIN (
        SELECT `o8`.`Id`, `t6`.`Id` AS `Id0`
        FROM `OwnedPerson` AS `o8`
        INNER JOIN (
            SELECT `o9`.`Id`, `o9`.`Discriminator`
            FROM `OwnedPerson` AS `o9`
            WHERE `o9`.`Discriminator` IN ('Branch', 'LeafA')
        ) AS `t6` ON `o8`.`Id` = `t6`.`Id`
    ) AS `t7` ON `o7`.`Id` = `t7`.`Id`
    WHERE `o7`.`BranchAddress_Country_PlanetId` IS NOT NULL
) AS `t8` ON `t5`.`Id` = `t8`.`Id`
LEFT JOIN (
    SELECT `o10`.`Id`, `t9`.`Id` AS `Id0`
    FROM `OwnedPerson` AS `o10`
    INNER JOIN (
        SELECT `o11`.`Id`, `o11`.`Discriminator`
        FROM `OwnedPerson` AS `o11`
        WHERE `o11`.`Discriminator` = 'LeafB'
    ) AS `t9` ON `o10`.`Id` = `t9`.`Id`
) AS `t10` ON `o`.`Id` = `t10`.`Id`
LEFT JOIN (
    SELECT `o12`.`Id`, `o12`.`LeafBAddress_Country_Name`, `o12`.`LeafBAddress_Country_PlanetId`, `t12`.`Id` AS `Id0`, `t12`.`Id0` AS `Id00`
    FROM `OwnedPerson` AS `o12`
    INNER JOIN (
        SELECT `o13`.`Id`, `t11`.`Id` AS `Id0`
        FROM `OwnedPerson` AS `o13`
        INNER JOIN (
            SELECT `o14`.`Id`, `o14`.`Discriminator`
            FROM `OwnedPerson` AS `o14`
            WHERE `o14`.`Discriminator` = 'LeafB'
        ) AS `t11` ON `o13`.`Id` = `t11`.`Id`
    ) AS `t12` ON `o12`.`Id` = `t12`.`Id`
    WHERE `o12`.`LeafBAddress_Country_PlanetId` IS NOT NULL
) AS `t13` ON `t10`.`Id` = `t13`.`Id`
LEFT JOIN (
    SELECT `o15`.`Id`, `t14`.`Id` AS `Id0`
    FROM `OwnedPerson` AS `o15`
    INNER JOIN (
        SELECT `o16`.`Id`, `o16`.`Discriminator`
        FROM `OwnedPerson` AS `o16`
        WHERE `o16`.`Discriminator` = 'LeafA'
    ) AS `t14` ON `o15`.`Id` = `t14`.`Id`
) AS `t15` ON `o`.`Id` = `t15`.`Id`
LEFT JOIN (
    SELECT `o17`.`Id`, `o17`.`LeafAAddress_Country_Name`, `o17`.`LeafAAddress_Country_PlanetId`, `t17`.`Id` AS `Id0`, `t17`.`Id0` AS `Id00`
    FROM `OwnedPerson` AS `o17`
    INNER JOIN (
        SELECT `o18`.`Id`, `t16`.`Id` AS `Id0`
        FROM `OwnedPerson` AS `o18`
        INNER JOIN (
            SELECT `o19`.`Id`, `o19`.`Discriminator`
            FROM `OwnedPerson` AS `o19`
            WHERE `o19`.`Discriminator` = 'LeafA'
        ) AS `t16` ON `o18`.`Id` = `t16`.`Id`
    ) AS `t17` ON `o17`.`Id` = `t17`.`Id`
    WHERE `o17`.`LeafAAddress_Country_PlanetId` IS NOT NULL
) AS `t18` ON `t15`.`Id` = `t18`.`Id`
LEFT JOIN `Order` AS `o20` ON `o`.`Id` = `o20`.`ClientId`
WHERE `o`.`Discriminator` IN ('OwnedPerson', 'Branch', 'LeafB', 'LeafA') AND (`o`.`Id` = 1)
ORDER BY `o`.`Id`, `o20`.`ClientId`, `o20`.`Id`");
        }

        private void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);

        public class OwnedQueryJetFixture : RelationalOwnedQueryFixture
        {
            protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;
        }
    }
}
