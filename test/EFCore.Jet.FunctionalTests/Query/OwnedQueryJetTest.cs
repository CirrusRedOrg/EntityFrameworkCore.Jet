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
            fixture.TestSqlLoggerFactory.Clear();
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

            AssertSql(
                """
SELECT `o`.`Id`, `o`.`Discriminator`, `o`.`Name`, `s`.`ClientId`, `s`.`Id`, `s`.`OrderDate`, `s`.`OrderClientId`, `s`.`OrderId`, `s`.`Id0`, `s`.`Detail`, `o`.`PersonAddress_AddressLine`, `o`.`PersonAddress_PlaceType`, `o`.`PersonAddress_ZipCode`, `o`.`PersonAddress_Country_Name`, `o`.`PersonAddress_Country_PlanetId`, `o`.`BranchAddress_BranchName`, `o`.`BranchAddress_PlaceType`, `o`.`BranchAddress_Country_Name`, `o`.`BranchAddress_Country_PlanetId`, `o`.`LeafBAddress_LeafBType`, `o`.`LeafBAddress_PlaceType`, `o`.`LeafBAddress_Country_Name`, `o`.`LeafBAddress_Country_PlanetId`, `o`.`LeafAAddress_LeafType`, `o`.`LeafAAddress_PlaceType`, `o`.`LeafAAddress_Country_Name`, `o`.`LeafAAddress_Country_PlanetId`
FROM `OwnedPerson` AS `o`
LEFT JOIN (
    SELECT `o0`.`ClientId`, `o0`.`Id`, `o0`.`OrderDate`, `o1`.`OrderClientId`, `o1`.`OrderId`, `o1`.`Id` AS `Id0`, `o1`.`Detail`
    FROM `Order` AS `o0`
    LEFT JOIN `OrderDetail` AS `o1` ON `o0`.`ClientId` = `o1`.`OrderClientId` AND `o0`.`Id` = `o1`.`OrderId`
) AS `s` ON `o`.`Id` = `s`.`ClientId`
ORDER BY `o`.`Id`, `s`.`ClientId`, `s`.`Id`, `s`.`OrderClientId`, `s`.`OrderId`
""");
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
                """
SELECT `o`.`Id`, `o`.`Discriminator`, `o`.`Name`, `s`.`ClientId`, `s`.`Id`, `s`.`OrderDate`, `s`.`OrderClientId`, `s`.`OrderId`, `s`.`Id0`, `s`.`Detail`, `o`.`PersonAddress_AddressLine`, `o`.`PersonAddress_PlaceType`, `o`.`PersonAddress_ZipCode`, `o`.`PersonAddress_Country_Name`, `o`.`PersonAddress_Country_PlanetId`, `o`.`BranchAddress_BranchName`, `o`.`BranchAddress_PlaceType`, `o`.`BranchAddress_Country_Name`, `o`.`BranchAddress_Country_PlanetId`, `o`.`LeafAAddress_LeafType`, `o`.`LeafAAddress_PlaceType`, `o`.`LeafAAddress_Country_Name`, `o`.`LeafAAddress_Country_PlanetId`
FROM `OwnedPerson` AS `o`
LEFT JOIN (
    SELECT `o0`.`ClientId`, `o0`.`Id`, `o0`.`OrderDate`, `o1`.`OrderClientId`, `o1`.`OrderId`, `o1`.`Id` AS `Id0`, `o1`.`Detail`
    FROM `Order` AS `o0`
    LEFT JOIN `OrderDetail` AS `o1` ON `o0`.`ClientId` = `o1`.`OrderClientId` AND `o0`.`Id` = `o1`.`OrderId`
) AS `s` ON `o`.`Id` = `s`.`ClientId`
WHERE `o`.`Discriminator` IN ('Branch', 'LeafA')
ORDER BY `o`.`Id`, `s`.`ClientId`, `s`.`Id`, `s`.`OrderClientId`, `s`.`OrderId`
""");
        }

        public override async Task Query_for_branch_type_loads_all_owned_navs_tracking(bool isAsync)
        {
            await base.Query_for_branch_type_loads_all_owned_navs_tracking(isAsync);

            AssertSql(
                """
SELECT `o`.`Id`, `o`.`Discriminator`, `o`.`Name`, `s`.`ClientId`, `s`.`Id`, `s`.`OrderDate`, `s`.`OrderClientId`, `s`.`OrderId`, `s`.`Id0`, `s`.`Detail`, `o`.`PersonAddress_AddressLine`, `o`.`PersonAddress_PlaceType`, `o`.`PersonAddress_ZipCode`, `o`.`PersonAddress_Country_Name`, `o`.`PersonAddress_Country_PlanetId`, `o`.`BranchAddress_BranchName`, `o`.`BranchAddress_PlaceType`, `o`.`BranchAddress_Country_Name`, `o`.`BranchAddress_Country_PlanetId`, `o`.`LeafAAddress_LeafType`, `o`.`LeafAAddress_PlaceType`, `o`.`LeafAAddress_Country_Name`, `o`.`LeafAAddress_Country_PlanetId`
FROM `OwnedPerson` AS `o`
LEFT JOIN (
    SELECT `o0`.`ClientId`, `o0`.`Id`, `o0`.`OrderDate`, `o1`.`OrderClientId`, `o1`.`OrderId`, `o1`.`Id` AS `Id0`, `o1`.`Detail`
    FROM `Order` AS `o0`
    LEFT JOIN `OrderDetail` AS `o1` ON `o0`.`ClientId` = `o1`.`OrderClientId` AND `o0`.`Id` = `o1`.`OrderId`
) AS `s` ON `o`.`Id` = `s`.`ClientId`
WHERE `o`.`Discriminator` IN ('Branch', 'LeafA')
ORDER BY `o`.`Id`, `s`.`ClientId`, `s`.`Id`, `s`.`OrderClientId`, `s`.`OrderId`
""");
        }

        public override async Task Query_for_leaf_type_loads_all_owned_navs(bool isAsync)
        {
            await base.Query_for_leaf_type_loads_all_owned_navs(isAsync);

            AssertSql(
                """
SELECT `o`.`Id`, `o`.`Discriminator`, `o`.`Name`, `s`.`ClientId`, `s`.`Id`, `s`.`OrderDate`, `s`.`OrderClientId`, `s`.`OrderId`, `s`.`Id0`, `s`.`Detail`, `o`.`PersonAddress_AddressLine`, `o`.`PersonAddress_PlaceType`, `o`.`PersonAddress_ZipCode`, `o`.`PersonAddress_Country_Name`, `o`.`PersonAddress_Country_PlanetId`, `o`.`BranchAddress_BranchName`, `o`.`BranchAddress_PlaceType`, `o`.`BranchAddress_Country_Name`, `o`.`BranchAddress_Country_PlanetId`, `o`.`LeafAAddress_LeafType`, `o`.`LeafAAddress_PlaceType`, `o`.`LeafAAddress_Country_Name`, `o`.`LeafAAddress_Country_PlanetId`
FROM `OwnedPerson` AS `o`
LEFT JOIN (
    SELECT `o0`.`ClientId`, `o0`.`Id`, `o0`.`OrderDate`, `o1`.`OrderClientId`, `o1`.`OrderId`, `o1`.`Id` AS `Id0`, `o1`.`Detail`
    FROM `Order` AS `o0`
    LEFT JOIN `OrderDetail` AS `o1` ON `o0`.`ClientId` = `o1`.`OrderClientId` AND `o0`.`Id` = `o1`.`OrderId`
) AS `s` ON `o`.`Id` = `s`.`ClientId`
WHERE `o`.`Discriminator` = 'LeafA'
ORDER BY `o`.`Id`, `s`.`ClientId`, `s`.`Id`, `s`.`OrderClientId`, `s`.`OrderId`
""");
        }

        public override async Task Query_when_subquery(bool isAsync)
        {
            await base.Query_when_subquery(isAsync);

            AssertSql(
                """
SELECT `o3`.`Id`, `o3`.`Discriminator`, `o3`.`Name`, `s`.`ClientId`, `s`.`Id`, `s`.`OrderDate`, `s`.`OrderClientId`, `s`.`OrderId`, `s`.`Id0`, `s`.`Detail`, `o3`.`PersonAddress_AddressLine`, `o3`.`PersonAddress_PlaceType`, `o3`.`PersonAddress_ZipCode`, `o3`.`PersonAddress_Country_Name`, `o3`.`PersonAddress_Country_PlanetId`, `o3`.`BranchAddress_BranchName`, `o3`.`BranchAddress_PlaceType`, `o3`.`BranchAddress_Country_Name`, `o3`.`BranchAddress_Country_PlanetId`, `o3`.`LeafBAddress_LeafBType`, `o3`.`LeafBAddress_PlaceType`, `o3`.`LeafBAddress_Country_Name`, `o3`.`LeafBAddress_Country_PlanetId`, `o3`.`LeafAAddress_LeafType`, `o3`.`LeafAAddress_PlaceType`, `o3`.`LeafAAddress_Country_Name`, `o3`.`LeafAAddress_Country_PlanetId`
FROM (
    SELECT TOP 5 `o0`.`Id`, `o0`.`Discriminator`, `o0`.`Name`, `o0`.`PersonAddress_AddressLine`, `o0`.`PersonAddress_PlaceType`, `o0`.`PersonAddress_ZipCode`, `o0`.`PersonAddress_Country_Name`, `o0`.`PersonAddress_Country_PlanetId`, `o0`.`BranchAddress_BranchName`, `o0`.`BranchAddress_PlaceType`, `o0`.`BranchAddress_Country_Name`, `o0`.`BranchAddress_Country_PlanetId`, `o0`.`LeafBAddress_LeafBType`, `o0`.`LeafBAddress_PlaceType`, `o0`.`LeafBAddress_Country_Name`, `o0`.`LeafBAddress_Country_PlanetId`, `o0`.`LeafAAddress_LeafType`, `o0`.`LeafAAddress_PlaceType`, `o0`.`LeafAAddress_Country_Name`, `o0`.`LeafAAddress_Country_PlanetId`
    FROM (
        SELECT DISTINCT `o`.`Id`, `o`.`Discriminator`, `o`.`Name`, `o`.`PersonAddress_AddressLine`, `o`.`PersonAddress_PlaceType`, `o`.`PersonAddress_ZipCode`, `o`.`PersonAddress_Country_Name`, `o`.`PersonAddress_Country_PlanetId`, `o`.`BranchAddress_BranchName`, `o`.`BranchAddress_PlaceType`, `o`.`BranchAddress_Country_Name`, `o`.`BranchAddress_Country_PlanetId`, `o`.`LeafBAddress_LeafBType`, `o`.`LeafBAddress_PlaceType`, `o`.`LeafBAddress_Country_Name`, `o`.`LeafBAddress_Country_PlanetId`, `o`.`LeafAAddress_LeafType`, `o`.`LeafAAddress_PlaceType`, `o`.`LeafAAddress_Country_Name`, `o`.`LeafAAddress_Country_PlanetId`
        FROM `OwnedPerson` AS `o`
    ) AS `o0`
    ORDER BY `o0`.`Id`
) AS `o3`
LEFT JOIN (
    SELECT `o1`.`ClientId`, `o1`.`Id`, `o1`.`OrderDate`, `o2`.`OrderClientId`, `o2`.`OrderId`, `o2`.`Id` AS `Id0`, `o2`.`Detail`
    FROM `Order` AS `o1`
    LEFT JOIN `OrderDetail` AS `o2` ON `o1`.`ClientId` = `o2`.`OrderClientId` AND `o1`.`Id` = `o2`.`OrderId`
) AS `s` ON `o3`.`Id` = `s`.`ClientId`
ORDER BY `o3`.`Id`, `s`.`ClientId`, `s`.`Id`, `s`.`OrderClientId`, `s`.`OrderId`
""");
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
                """
SELECT `o`.`Id`, `o`.`Discriminator`, `o`.`Name`, `s`.`ClientId`, `s`.`Id`, `s`.`OrderDate`, `s`.`OrderClientId`, `s`.`OrderId`, `s`.`Id0`, `s`.`Detail`, `o`.`PersonAddress_AddressLine`, `o`.`PersonAddress_PlaceType`, `o`.`PersonAddress_ZipCode`, `o`.`PersonAddress_Country_Name`, `o`.`PersonAddress_Country_PlanetId`, `o`.`BranchAddress_BranchName`, `o`.`BranchAddress_PlaceType`, `o`.`BranchAddress_Country_Name`, `o`.`BranchAddress_Country_PlanetId`, `o`.`LeafBAddress_LeafBType`, `o`.`LeafBAddress_PlaceType`, `o`.`LeafBAddress_Country_Name`, `o`.`LeafBAddress_Country_PlanetId`, `o`.`LeafAAddress_LeafType`, `o`.`LeafAAddress_PlaceType`, `o`.`LeafAAddress_Country_Name`, `o`.`LeafAAddress_Country_PlanetId`
FROM `OwnedPerson` AS `o`
LEFT JOIN (
    SELECT `o0`.`ClientId`, `o0`.`Id`, `o0`.`OrderDate`, `o1`.`OrderClientId`, `o1`.`OrderId`, `o1`.`Id` AS `Id0`, `o1`.`Detail`
    FROM `Order` AS `o0`
    LEFT JOIN `OrderDetail` AS `o1` ON `o0`.`ClientId` = `o1`.`OrderClientId` AND `o0`.`Id` = `o1`.`OrderId`
) AS `s` ON `o`.`Id` = `s`.`ClientId`
WHERE `o`.`PersonAddress_Country_Name` = 'USA'
ORDER BY `o`.`Id`, `s`.`ClientId`, `s`.`Id`, `s`.`OrderClientId`, `s`.`OrderId`
""");
        }

        public override async Task Navigation_rewrite_on_owned_collection(bool isAsync)
        {
            await base.Navigation_rewrite_on_owned_collection(isAsync);

            AssertSql(
                """
SELECT `o`.`Id`, `s`.`ClientId`, `s`.`Id`, `s`.`OrderDate`, `s`.`OrderClientId`, `s`.`OrderId`, `s`.`Id0`, `s`.`Detail`
FROM `OwnedPerson` AS `o`
LEFT JOIN (
    SELECT `o1`.`ClientId`, `o1`.`Id`, `o1`.`OrderDate`, `o2`.`OrderClientId`, `o2`.`OrderId`, `o2`.`Id` AS `Id0`, `o2`.`Detail`
    FROM `Order` AS `o1`
    LEFT JOIN `OrderDetail` AS `o2` ON `o1`.`ClientId` = `o2`.`OrderClientId` AND `o1`.`Id` = `o2`.`OrderId`
) AS `s` ON `o`.`Id` = `s`.`ClientId`
WHERE (
    SELECT COUNT(*)
    FROM `Order` AS `o0`
    WHERE `o`.`Id` = `o0`.`ClientId`) > 0
ORDER BY `o`.`Id`, `s`.`ClientId`, `s`.`Id`, `s`.`OrderClientId`, `s`.`OrderId`
""");
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
    SELECT TOP 1 `o1`.`PersonAddress_Country_Name`
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
LEFT JOIN `OrderDetail` AS `o1` ON `o0`.`ClientId` = `o1`.`OrderClientId` AND `o0`.`Id` = `o1`.`OrderId`
ORDER BY `o`.`Id`, `o0`.`ClientId`, `o0`.`Id`, `o1`.`OrderClientId`, `o1`.`OrderId`");
        }

        public override async Task Navigation_rewrite_on_owned_reference_followed_by_regular_entity(bool isAsync)
        {
            await base.Navigation_rewrite_on_owned_reference_followed_by_regular_entity(isAsync);

            AssertSql(
                """
SELECT `p`.`Id`, `p`.`Name`, `p`.`StarId`
FROM `OwnedPerson` AS `o`
LEFT JOIN `Planet` AS `p` ON `o`.`PersonAddress_Country_PlanetId` = `p`.`Id`
""");
        }

        public override async Task Filter_owned_entity_chained_with_regular_entity_followed_by_projecting_owned_collection(bool isAsync)
        {
            await base.Filter_owned_entity_chained_with_regular_entity_followed_by_projecting_owned_collection(isAsync);

            AssertSql(
                """
SELECT `o`.`Id`, `p`.`Id`, `s`.`ClientId`, `s`.`Id`, `s`.`OrderDate`, `s`.`OrderClientId`, `s`.`OrderId`, `s`.`Id0`, `s`.`Detail`
FROM (`OwnedPerson` AS `o`
LEFT JOIN `Planet` AS `p` ON `o`.`PersonAddress_Country_PlanetId` = `p`.`Id`)
LEFT JOIN (
    SELECT `o0`.`ClientId`, `o0`.`Id`, `o0`.`OrderDate`, `o1`.`OrderClientId`, `o1`.`OrderId`, `o1`.`Id` AS `Id0`, `o1`.`Detail`
    FROM `Order` AS `o0`
    LEFT JOIN `OrderDetail` AS `o1` ON `o0`.`ClientId` = `o1`.`OrderClientId` AND `o0`.`Id` = `o1`.`OrderId`
) AS `s` ON `o`.`Id` = `s`.`ClientId`
WHERE `p`.`Id` <> 42 OR `p`.`Id` IS NULL
ORDER BY `o`.`Id`, `p`.`Id`, `s`.`ClientId`, `s`.`Id`, `s`.`OrderClientId`, `s`.`OrderId`
""");
        }

        public override async Task Project_multiple_owned_navigations(bool isAsync)
        {
            await base.Project_multiple_owned_navigations(isAsync);

            AssertSql(
                """
SELECT `o`.`Id`, `p`.`Id`, `s`.`ClientId`, `s`.`Id`, `s`.`OrderDate`, `s`.`OrderClientId`, `s`.`OrderId`, `s`.`Id0`, `s`.`Detail`, `o`.`PersonAddress_AddressLine`, `o`.`PersonAddress_PlaceType`, `o`.`PersonAddress_ZipCode`, `o`.`PersonAddress_Country_Name`, `o`.`PersonAddress_Country_PlanetId`, `p`.`Name`, `p`.`StarId`
FROM (`OwnedPerson` AS `o`
LEFT JOIN `Planet` AS `p` ON `o`.`PersonAddress_Country_PlanetId` = `p`.`Id`)
LEFT JOIN (
    SELECT `o0`.`ClientId`, `o0`.`Id`, `o0`.`OrderDate`, `o1`.`OrderClientId`, `o1`.`OrderId`, `o1`.`Id` AS `Id0`, `o1`.`Detail`
    FROM `Order` AS `o0`
    LEFT JOIN `OrderDetail` AS `o1` ON `o0`.`ClientId` = `o1`.`OrderClientId` AND `o0`.`Id` = `o1`.`OrderId`
) AS `s` ON `o`.`Id` = `s`.`ClientId`
ORDER BY `o`.`Id`, `p`.`Id`, `s`.`ClientId`, `s`.`Id`, `s`.`OrderClientId`, `s`.`OrderId`
""");
        }

        public override async Task Project_multiple_owned_navigations_with_expansion_on_owned_collections(bool isAsync)
        {
            await base.Project_multiple_owned_navigations_with_expansion_on_owned_collections(isAsync);

            AssertSql(
"""
SELECT (
    SELECT COUNT(*)
    FROM ((`Order` AS `o0`
    LEFT JOIN `OwnedPerson` AS `o1` ON `o0`.`ClientId` = `o1`.`Id`)
    LEFT JOIN `Planet` AS `p0` ON `o1`.`PersonAddress_Country_PlanetId` = `p0`.`Id`)
    LEFT JOIN `Star` AS `s` ON `p0`.`StarId` = `s`.`Id`
    WHERE `o`.`Id` = `o0`.`ClientId` AND (`s`.`Id` <> 42 OR `s`.`Id` IS NULL)) AS `Count`, `p`.`Id`, `p`.`Name`, `p`.`StarId`
FROM `OwnedPerson` AS `o`
LEFT JOIN `Planet` AS `p` ON `o`.`PersonAddress_Country_PlanetId` = `p`.`Id`
ORDER BY `o`.`Id`
""");
        }

        public override async Task Navigation_rewrite_on_owned_reference_followed_by_regular_entity_filter(bool isAsync)
        {
            await base.Navigation_rewrite_on_owned_reference_followed_by_regular_entity_filter(isAsync);

            AssertSql(
                """
SELECT `o`.`Id`, `o`.`Discriminator`, `o`.`Name`, `p`.`Id`, `s`.`ClientId`, `s`.`Id`, `s`.`OrderDate`, `s`.`OrderClientId`, `s`.`OrderId`, `s`.`Id0`, `s`.`Detail`, `o`.`PersonAddress_AddressLine`, `o`.`PersonAddress_PlaceType`, `o`.`PersonAddress_ZipCode`, `o`.`PersonAddress_Country_Name`, `o`.`PersonAddress_Country_PlanetId`, `o`.`BranchAddress_BranchName`, `o`.`BranchAddress_PlaceType`, `o`.`BranchAddress_Country_Name`, `o`.`BranchAddress_Country_PlanetId`, `o`.`LeafBAddress_LeafBType`, `o`.`LeafBAddress_PlaceType`, `o`.`LeafBAddress_Country_Name`, `o`.`LeafBAddress_Country_PlanetId`, `o`.`LeafAAddress_LeafType`, `o`.`LeafAAddress_PlaceType`, `o`.`LeafAAddress_Country_Name`, `o`.`LeafAAddress_Country_PlanetId`
FROM (`OwnedPerson` AS `o`
LEFT JOIN `Planet` AS `p` ON `o`.`PersonAddress_Country_PlanetId` = `p`.`Id`)
LEFT JOIN (
    SELECT `o0`.`ClientId`, `o0`.`Id`, `o0`.`OrderDate`, `o1`.`OrderClientId`, `o1`.`OrderId`, `o1`.`Id` AS `Id0`, `o1`.`Detail`
    FROM `Order` AS `o0`
    LEFT JOIN `OrderDetail` AS `o1` ON `o0`.`ClientId` = `o1`.`OrderClientId` AND `o0`.`Id` = `o1`.`OrderId`
) AS `s` ON `o`.`Id` = `s`.`ClientId`
WHERE `p`.`Id` <> 7 OR `p`.`Id` IS NULL
ORDER BY `o`.`Id`, `p`.`Id`, `s`.`ClientId`, `s`.`Id`, `s`.`OrderClientId`, `s`.`OrderId`
""");
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
                """
SELECT `m`.`Id`, `m`.`Diameter`, `m`.`PlanetId`
FROM (`OwnedPerson` AS `o`
LEFT JOIN `Planet` AS `p` ON `o`.`PersonAddress_Country_PlanetId` = `p`.`Id`)
LEFT JOIN `Moon` AS `m` ON `p`.`Id` = `m`.`PlanetId`
WHERE `p`.`Id` IS NOT NULL AND `m`.`PlanetId` IS NOT NULL
""");
        }

        public override async Task SelectMany_on_owned_reference_with_entity_in_between_ending_in_owned_collection(bool isAsync)
        {
            await base.SelectMany_on_owned_reference_with_entity_in_between_ending_in_owned_collection(isAsync);

            AssertSql(
                """
SELECT `e`.`Id`, `e`.`Name`, `e`.`StarId`
FROM ((`OwnedPerson` AS `o`
LEFT JOIN `Planet` AS `p` ON `o`.`PersonAddress_Country_PlanetId` = `p`.`Id`)
LEFT JOIN `Star` AS `s` ON `p`.`StarId` = `s`.`Id`)
LEFT JOIN `Element` AS `e` ON `s`.`Id` = `e`.`StarId`
WHERE `s`.`Id` IS NOT NULL AND `e`.`StarId` IS NOT NULL
""");
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
                """
SELECT `o`.`Id`, `o`.`Discriminator`, `o`.`Name`, `s`.`ClientId`, `s`.`Id`, `s`.`OrderDate`, `s`.`OrderClientId`, `s`.`OrderId`, `s`.`Id0`, `s`.`Detail`, `o`.`PersonAddress_AddressLine`, `o`.`PersonAddress_PlaceType`, `o`.`PersonAddress_ZipCode`, `o`.`PersonAddress_Country_Name`, `o`.`PersonAddress_Country_PlanetId`, `o`.`BranchAddress_BranchName`, `o`.`BranchAddress_PlaceType`, `o`.`BranchAddress_Country_Name`, `o`.`BranchAddress_Country_PlanetId`, `o`.`LeafAAddress_LeafType`, `o`.`LeafAAddress_PlaceType`, `o`.`LeafAAddress_Country_Name`, `o`.`LeafAAddress_Country_PlanetId`
FROM `OwnedPerson` AS `o`
LEFT JOIN (
    SELECT `o0`.`ClientId`, `o0`.`Id`, `o0`.`OrderDate`, `o1`.`OrderClientId`, `o1`.`OrderId`, `o1`.`Id` AS `Id0`, `o1`.`Detail`
    FROM `Order` AS `o0`
    LEFT JOIN `OrderDetail` AS `o1` ON `o0`.`ClientId` = `o1`.`OrderClientId` AND `o0`.`Id` = `o1`.`OrderId`
) AS `s` ON `o`.`Id` = `s`.`ClientId`
WHERE `o`.`Discriminator` = 'LeafA'
ORDER BY `o`.`Id`, `s`.`ClientId`, `s`.`Id`, `s`.`OrderClientId`, `s`.`OrderId`
""");
        }

        public override async Task Preserve_includes_when_applying_skip_take_after_anonymous_type_select(bool isAsync)
        {
            await base.Preserve_includes_when_applying_skip_take_after_anonymous_type_select(isAsync);

            AssertSql(
                """
SELECT `o4`.`Id`, `o4`.`Discriminator`, `o4`.`Name`, `s`.`ClientId`, `s`.`Id`, `s`.`OrderDate`, `s`.`OrderClientId`, `s`.`OrderId`, `s`.`Id0`, `s`.`Detail`, `o4`.`PersonAddress_AddressLine`, `o4`.`PersonAddress_PlaceType`, `o4`.`PersonAddress_ZipCode`, `o4`.`PersonAddress_Country_Name`, `o4`.`PersonAddress_Country_PlanetId`, `o4`.`BranchAddress_BranchName`, `o4`.`BranchAddress_PlaceType`, `o4`.`BranchAddress_Country_Name`, `o4`.`BranchAddress_Country_PlanetId`, `o4`.`LeafBAddress_LeafBType`, `o4`.`LeafBAddress_PlaceType`, `o4`.`LeafBAddress_Country_Name`, `o4`.`LeafBAddress_Country_PlanetId`, `o4`.`LeafAAddress_LeafType`, `o4`.`LeafAAddress_PlaceType`, `o4`.`LeafAAddress_Country_Name`, `o4`.`LeafAAddress_Country_PlanetId`, `o4`.`c`
FROM (
    SELECT TOP 100 `o3`.`Id`, `o3`.`Discriminator`, `o3`.`Name`, `o3`.`PersonAddress_AddressLine`, `o3`.`PersonAddress_PlaceType`, `o3`.`PersonAddress_ZipCode`, `o3`.`PersonAddress_Country_Name`, `o3`.`PersonAddress_Country_PlanetId`, `o3`.`BranchAddress_BranchName`, `o3`.`BranchAddress_PlaceType`, `o3`.`BranchAddress_Country_Name`, `o3`.`BranchAddress_Country_PlanetId`, `o3`.`LeafBAddress_LeafBType`, `o3`.`LeafBAddress_PlaceType`, `o3`.`LeafBAddress_Country_Name`, `o3`.`LeafBAddress_Country_PlanetId`, `o3`.`LeafAAddress_LeafType`, `o3`.`LeafAAddress_PlaceType`, `o3`.`LeafAAddress_Country_Name`, `o3`.`LeafAAddress_Country_PlanetId`, `o3`.`c`
    FROM (
        SELECT TOP 100 `o`.`Id`, `o`.`Discriminator`, `o`.`Name`, `o`.`PersonAddress_AddressLine`, `o`.`PersonAddress_PlaceType`, `o`.`PersonAddress_ZipCode`, `o`.`PersonAddress_Country_Name`, `o`.`PersonAddress_Country_PlanetId`, `o`.`BranchAddress_BranchName`, `o`.`BranchAddress_PlaceType`, `o`.`BranchAddress_Country_Name`, `o`.`BranchAddress_Country_PlanetId`, `o`.`LeafBAddress_LeafBType`, `o`.`LeafBAddress_PlaceType`, `o`.`LeafBAddress_Country_Name`, `o`.`LeafBAddress_Country_PlanetId`, `o`.`LeafAAddress_LeafType`, `o`.`LeafAAddress_PlaceType`, `o`.`LeafAAddress_Country_Name`, `o`.`LeafAAddress_Country_PlanetId`, (
            SELECT COUNT(*)
            FROM `OwnedPerson` AS `o2`) AS `c`
        FROM `OwnedPerson` AS `o`
        ORDER BY `o`.`Id`
    ) AS `o3`
    ORDER BY `o3`.`Id` DESC
) AS `o4`
LEFT JOIN (
    SELECT `o0`.`ClientId`, `o0`.`Id`, `o0`.`OrderDate`, `o1`.`OrderClientId`, `o1`.`OrderId`, `o1`.`Id` AS `Id0`, `o1`.`Detail`
    FROM `Order` AS `o0`
    LEFT JOIN `OrderDetail` AS `o1` ON `o0`.`ClientId` = `o1`.`OrderClientId` AND `o0`.`Id` = `o1`.`OrderId`
) AS `s` ON `o4`.`Id` = `s`.`ClientId`
ORDER BY `o4`.`Id`, `s`.`ClientId`, `s`.`Id`, `s`.`OrderClientId`, `s`.`OrderId`
""");
        }

        public override async Task Unmapped_property_projection_loads_owned_navigations(bool isAsync)
        {
            await base.Unmapped_property_projection_loads_owned_navigations(isAsync);

            AssertSql(
                """
SELECT `o`.`Id`, `o`.`Discriminator`, `o`.`Name`, `s`.`ClientId`, `s`.`Id`, `s`.`OrderDate`, `s`.`OrderClientId`, `s`.`OrderId`, `s`.`Id0`, `s`.`Detail`, `o`.`PersonAddress_AddressLine`, `o`.`PersonAddress_PlaceType`, `o`.`PersonAddress_ZipCode`, `o`.`PersonAddress_Country_Name`, `o`.`PersonAddress_Country_PlanetId`, `o`.`BranchAddress_BranchName`, `o`.`BranchAddress_PlaceType`, `o`.`BranchAddress_Country_Name`, `o`.`BranchAddress_Country_PlanetId`, `o`.`LeafBAddress_LeafBType`, `o`.`LeafBAddress_PlaceType`, `o`.`LeafBAddress_Country_Name`, `o`.`LeafBAddress_Country_PlanetId`, `o`.`LeafAAddress_LeafType`, `o`.`LeafAAddress_PlaceType`, `o`.`LeafAAddress_Country_Name`, `o`.`LeafAAddress_Country_PlanetId`
FROM `OwnedPerson` AS `o`
LEFT JOIN (
    SELECT `o0`.`ClientId`, `o0`.`Id`, `o0`.`OrderDate`, `o1`.`OrderClientId`, `o1`.`OrderId`, `o1`.`Id` AS `Id0`, `o1`.`Detail`
    FROM `Order` AS `o0`
    LEFT JOIN `OrderDetail` AS `o1` ON `o0`.`ClientId` = `o1`.`OrderClientId` AND `o0`.`Id` = `o1`.`OrderId`
) AS `s` ON `o`.`Id` = `s`.`ClientId`
WHERE `o`.`Id` = 1
ORDER BY `o`.`Id`, `s`.`ClientId`, `s`.`Id`, `s`.`OrderClientId`, `s`.`OrderId`
""");
        }

        private void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);

        public class OwnedQueryJetFixture : RelationalOwnedQueryFixture
        {
            protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;
        }
    }
}
