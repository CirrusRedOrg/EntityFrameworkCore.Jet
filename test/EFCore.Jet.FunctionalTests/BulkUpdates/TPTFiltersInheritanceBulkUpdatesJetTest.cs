﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.BulkUpdates;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace EntityFrameworkCore.Jet.FunctionalTests.BulkUpdates;

public class TPTFiltersInheritanceBulkUpdatesJetTest : TPTFiltersInheritanceBulkUpdatesTestBase<
    TPTFiltersInheritanceBulkUpdatesJetFixture>
{
    public TPTFiltersInheritanceBulkUpdatesJetTest(TPTFiltersInheritanceBulkUpdatesJetFixture fixture, ITestOutputHelper testOutputHelper)
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

        AssertSql();
    }

    public override async Task Delete_where_hierarchy_derived(bool async)
    {
        await base.Delete_where_hierarchy_derived(async);

        AssertSql();
    }

    public override async Task Delete_where_using_hierarchy(bool async)
    {
        await base.Delete_where_using_hierarchy(async);

        AssertSql(
            """
DELETE FROM `Countries` AS `c`
WHERE (
    SELECT COUNT(*)
    FROM ((`Animals` AS `a`
    LEFT JOIN `Birds` AS `b` ON `a`.`Id` = `b`.`Id`)
    LEFT JOIN `Eagle` AS `e` ON `a`.`Id` = `e`.`Id`)
    LEFT JOIN `Kiwi` AS `k` ON `a`.`Id` = `k`.`Id`
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
    FROM ((`Animals` AS `a`
    LEFT JOIN `Birds` AS `b` ON `a`.`Id` = `b`.`Id`)
    LEFT JOIN `Eagle` AS `e` ON `a`.`Id` = `e`.`Id`)
    LEFT JOIN `Kiwi` AS `k` ON `a`.`Id` = `k`.`Id`
    WHERE `a`.`CountryId` = 1 AND `c`.`Id` = `a`.`CountryId` AND (`k`.`Id` IS NOT NULL) AND `a`.`CountryId` > 0) > 0
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

        AssertSql();
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

        AssertSql();
    }

    public override async Task Update_where_hierarchy(bool async)
    {
        await base.Update_where_hierarchy(async);

        AssertExecuteUpdateSql();
    }

    public override async Task Update_where_hierarchy_subquery(bool async)
    {
        await base.Update_where_hierarchy_subquery(async);

        AssertExecuteUpdateSql();
    }

    public override async Task Update_where_hierarchy_derived(bool async)
    {
        await base.Update_where_hierarchy_derived(async);

        AssertExecuteUpdateSql();
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
    FROM ((`Animals` AS `a`
    LEFT JOIN `Birds` AS `b` ON `a`.`Id` = `b`.`Id`)
    LEFT JOIN `Eagle` AS `e` ON `a`.`Id` = `e`.`Id`)
    LEFT JOIN `Kiwi` AS `k` ON `a`.`Id` = `k`.`Id`
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
    FROM ((`Animals` AS `a`
    LEFT JOIN `Birds` AS `b` ON `a`.`Id` = `b`.`Id`)
    LEFT JOIN `Eagle` AS `e` ON `a`.`Id` = `e`.`Id`)
    LEFT JOIN `Kiwi` AS `k` ON `a`.`Id` = `k`.`Id`
    WHERE `a`.`CountryId` = 1 AND `c`.`Id` = `a`.`CountryId` AND (`k`.`Id` IS NOT NULL) AND `a`.`CountryId` > 0) > 0
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