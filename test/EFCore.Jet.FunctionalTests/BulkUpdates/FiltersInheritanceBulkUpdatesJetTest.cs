// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.BulkUpdates;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests.BulkUpdates;

public class FiltersInheritanceBulkUpdatesJetTest : FiltersInheritanceBulkUpdatesTestBase<
    FiltersInheritanceBulkUpdatesJetFixture>
{
    public FiltersInheritanceBulkUpdatesJetTest(FiltersInheritanceBulkUpdatesJetFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        ClearLog();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    [ConditionalFact]
    public virtual void Check_all_tests_overridden()
        => TestHelpers.AssertAllMethodsOverridden(GetType());

    public override async Task Delete_where_hierarchy(bool async)
    {
        await base.Delete_where_hierarchy(async);

        AssertSql(
            """
DELETE FROM `Animals` AS `a`
WHERE `a`.`CountryId` = 1 AND `a`.`Name` = 'Great spotted kiwi'
""");
    }

    public override async Task Delete_where_hierarchy_derived(bool async)
    {
        await base.Delete_where_hierarchy_derived(async);

        AssertSql(
            """
DELETE FROM `Animals` AS `a`
WHERE `a`.`Discriminator` = 'Kiwi' AND `a`.`CountryId` = 1 AND `a`.`Name` = 'Great spotted kiwi'
""");
    }

    public override async Task Delete_where_using_hierarchy(bool async)
    {
        await base.Delete_where_using_hierarchy(async);

        AssertSql(
            """
DELETE FROM `Countries` AS `c`
WHERE (
    SELECT COUNT(*)
    FROM `Animals` AS `a`
    WHERE `a`.`CountryId` = 1 AND `c`.`Id` = `a`.`CountryId` AND `a`.`CountryId` > 0) > 0
""");
    }

    public override async Task Delete_where_using_hierarchy_derived(bool async)
    {
        await base.Delete_where_using_hierarchy_derived(async);

        AssertSql(
            """
DELETE FROM `Countries` AS `c`
WHERE (
    SELECT COUNT(*)
    FROM `Animals` AS `a`
    WHERE `a`.`CountryId` = 1 AND `c`.`Id` = `a`.`CountryId` AND `a`.`Discriminator` = 'Kiwi' AND `a`.`CountryId` > 0) > 0
""");
    }

    public override async Task Delete_GroupBy_Where_Select_First(bool async)
    {
        await base.Delete_GroupBy_Where_Select_First(async);

        AssertSql();
    }

    public override async Task Delete_GroupBy_Where_Select_First_2(bool async)
    {
        await base.Delete_GroupBy_Where_Select_First_2(async);

        AssertSql();
    }

    public override async Task Delete_GroupBy_Where_Select_First_3(bool async)
    {
        await base.Delete_GroupBy_Where_Select_First_3(async);

        AssertSql(
            """
DELETE FROM `Animals` AS `a`
WHERE `a`.`CountryId` = 1 AND EXISTS (
    SELECT 1
    FROM `Animals` AS `a0`
    WHERE `a0`.`CountryId` = 1
    GROUP BY `a0`.`CountryId`
    HAVING COUNT(*) < 3 AND (
        SELECT TOP 1 `a1`.`Id`
        FROM `Animals` AS `a1`
        WHERE `a1`.`CountryId` = 1 AND `a0`.`CountryId` = `a1`.`CountryId`) = `a`.`Id`)
""");
    }

    public override async Task Delete_where_keyless_entity_mapped_to_sql_query(bool async)
    {
        await base.Delete_where_keyless_entity_mapped_to_sql_query(async);

        AssertSql();
    }

    public override async Task Delete_where_hierarchy_subquery(bool async)
    {
        await base.Delete_where_hierarchy_subquery(async);

        AssertSql(
            """
DELETE FROM `Animals` AS `a`
WHERE EXISTS (
    SELECT 1
    FROM (
        SELECT TOP 3 `t`.`Id`, `t`.`CountryId`, `t`.`Discriminator`, `t`.`Name`, `t`.`Species`, `t`.`EagleId`, `t`.`IsFlightless`, `t`.`Group`, `t`.`FoundOn`
        FROM (
            SELECT TOP 3 `a0`.`Id`, `a0`.`CountryId`, `a0`.`Discriminator`, `a0`.`Name`, `a0`.`Species`, `a0`.`EagleId`, `a0`.`IsFlightless`, `a0`.`Group`, `a0`.`FoundOn`
            FROM `Animals` AS `a0`
            WHERE `a0`.`CountryId` = 1 AND `a0`.`Name` = 'Great spotted kiwi'
            ORDER BY `a0`.`Name`
        ) AS `t`
        ORDER BY `t`.`Name` DESC
    ) AS `t0`
    WHERE `t0`.`Id` = `a`.`Id`)
""");
    }

    public override async Task Update_where_hierarchy(bool async)
    {
        await base.Update_where_hierarchy(async);

        AssertExecuteUpdateSql(
            """
UPDATE `Animals` AS `a`
SET `Name` = 'Animal'
WHERE `a`.`CountryId` = 1 AND `a`.`Name` = 'Great spotted kiwi'
""");
    }

    public override async Task Update_where_hierarchy_subquery(bool async)
    {
        await base.Update_where_hierarchy_subquery(async);

        AssertExecuteUpdateSql();
    }

    public override async Task Update_where_hierarchy_derived(bool async)
    {
        await base.Update_where_hierarchy_derived(async);

        AssertExecuteUpdateSql(
            """
UPDATE `Animals` AS `a`
SET `Name` = 'Kiwi'
WHERE `a`.`Discriminator` = 'Kiwi' AND `a`.`CountryId` = 1 AND `a`.`Name` = 'Great spotted kiwi'
""");
    }

    public override async Task Update_where_using_hierarchy(bool async)
    {
        await base.Update_where_using_hierarchy(async);

        AssertExecuteUpdateSql(
            """
UPDATE `Countries` AS `c`
SET `Name` = 'Monovia'
WHERE (
    SELECT COUNT(*)
    FROM `Animals` AS `a`
    WHERE `a`.`CountryId` = 1 AND `c`.`Id` = `a`.`CountryId` AND `a`.`CountryId` > 0) > 0
""");
    }

    public override async Task Update_where_using_hierarchy_derived(bool async)
    {
        await base.Update_where_using_hierarchy_derived(async);

        AssertExecuteUpdateSql(
            """
UPDATE `Countries` AS `c`
SET `Name` = 'Monovia'
WHERE (
    SELECT COUNT(*)
    FROM `Animals` AS `a`
    WHERE `a`.`CountryId` = 1 AND `c`.`Id` = `a`.`CountryId` AND `a`.`Discriminator` = 'Kiwi' AND `a`.`CountryId` > 0) > 0
""");
    }

    public override async Task Update_where_keyless_entity_mapped_to_sql_query(bool async)
    {
        await base.Update_where_keyless_entity_mapped_to_sql_query(async);

        AssertExecuteUpdateSql();
    }

    protected override void ClearLog()
        => Fixture.TestSqlLoggerFactory.Clear();

    private void AssertSql(params string[] expected)
        => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);

    private void AssertExecuteUpdateSql(params string[] expected)
        => Fixture.TestSqlLoggerFactory.AssertBaseline(expected, forUpdate: true);
}
