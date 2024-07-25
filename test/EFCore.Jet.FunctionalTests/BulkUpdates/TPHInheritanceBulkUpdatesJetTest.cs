// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.BulkUpdates;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests.BulkUpdates;

public class TPHInheritanceBulkUpdatesJetTest(
    TPHInheritanceBulkUpdatesJetFixture fixture,
    ITestOutputHelper testOutputHelper)
    : TPHInheritanceBulkUpdatesTestBase<TPHInheritanceBulkUpdatesJetFixture>(fixture, testOutputHelper)
{
    [ConditionalFact]
    public virtual void Check_all_tests_overridden()
        => TestHelpers.AssertAllMethodsOverridden(GetType());

    public override async Task Delete_where_hierarchy(bool async)
    {
        await base.Delete_where_hierarchy(async);

        AssertSql(
            """
DELETE FROM `Animals` AS `a`
WHERE `a`.`Name` = 'Great spotted kiwi'
""");
    }

    public override async Task Delete_where_hierarchy_derived(bool async)
    {
        await base.Delete_where_hierarchy_derived(async);

        AssertSql(
            """
DELETE FROM `Animals` AS `a`
WHERE `a`.`Discriminator` = 'Kiwi' AND `a`.`Name` = 'Great spotted kiwi'
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
    WHERE `c`.`Id` = `a`.`CountryId` AND `a`.`CountryId` > 0) > 0
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
    WHERE `c`.`Id` = `a`.`CountryId` AND `a`.`Discriminator` = 'Kiwi' AND `a`.`CountryId` > 0) > 0
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
DELETE FROM [a]
FROM [Animals] AS [a]
WHERE [a].[Id] IN (
    SELECT (
        SELECT TOP(1) [a1].[Id]
        FROM [Animals] AS [a1]
        WHERE [a0].[CountryId] = [a1].[CountryId])
    FROM [Animals] AS [a0]
    GROUP BY [a0].[CountryId]
    HAVING COUNT(*) < 3
)
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
WHERE `a`.`Id` IN (
    SELECT `a2`.`Id`
    FROM (
        SELECT TOP 3 `a1`.`Id`, `a1`.`Name`
        FROM (
            SELECT TOP 3 `a0`.`Id`, `a0`.`Name`
            FROM `Animals` AS `a0`
            WHERE `a0`.`Name` = 'Great spotted kiwi'
            ORDER BY `a0`.`Name`
        ) AS `a1`
        ORDER BY `a1`.`Name` DESC
    ) AS `a2`
    ORDER BY `a2`.`Name`
)
""");
    }

    public override async Task Update_base_type(bool async)
    {
        await base.Update_base_type(async);

        AssertExecuteUpdateSql(
            """
UPDATE `Animals` AS `a`
SET `a`.`Name` = 'Animal'
WHERE `a`.`Name` = 'Great spotted kiwi'
""");
    }

    public override async Task Update_base_type_with_OfType(bool async)
    {
        await base.Update_base_type_with_OfType(async);

        AssertExecuteUpdateSql(
            """
UPDATE `Animals` AS `a`
SET `a`.`Name` = 'NewBird'
WHERE `a`.`Discriminator` = 'Kiwi'
""");
    }

    public override async Task Update_where_hierarchy_subquery(bool async)
    {
        await base.Update_where_hierarchy_subquery(async);

        AssertExecuteUpdateSql();
    }

    public override async Task Update_base_property_on_derived_type(bool async)
    {
        await base.Update_base_property_on_derived_type(async);

        AssertExecuteUpdateSql(
            """
UPDATE `Animals` AS `a`
SET `a`.`Name` = 'SomeOtherKiwi'
WHERE `a`.`Discriminator` = 'Kiwi'
""");
    }

    public override async Task Update_derived_property_on_derived_type(bool async)
    {
        await base.Update_derived_property_on_derived_type(async);

        AssertExecuteUpdateSql(
            """
UPDATE `Animals` AS `a`
SET `a`.`FoundOn` = CBYTE(0)
WHERE `a`.`Discriminator` = 'Kiwi'
""");
    }

    public override async Task Update_where_using_hierarchy(bool async)
    {
        await base.Update_where_using_hierarchy(async);

        AssertExecuteUpdateSql(
            """
UPDATE `Countries` AS `c`
SET `c`.`Name` = 'Monovia'
WHERE (
    SELECT COUNT(*)
    FROM `Animals` AS `a`
    WHERE `c`.`Id` = `a`.`CountryId` AND `a`.`CountryId` > 0) > 0
""");
    }

    public override async Task Update_base_and_derived_types(bool async)
    {
        await base.Update_base_and_derived_types(async);

        AssertExecuteUpdateSql(
            """
UPDATE `Animals` AS `a`
SET `a`.`FoundOn` = CBYTE(0),
    `a`.`Name` = 'Kiwi'
WHERE `a`.`Discriminator` = 'Kiwi'
""");
    }

    public override async Task Update_where_using_hierarchy_derived(bool async)
    {
        await base.Update_where_using_hierarchy_derived(async);

        AssertExecuteUpdateSql(
            """
UPDATE `Countries` AS `c`
SET `c`.`Name` = 'Monovia'
WHERE (
    SELECT COUNT(*)
    FROM `Animals` AS `a`
    WHERE `c`.`Id` = `a`.`CountryId` AND `a`.`Discriminator` = 'Kiwi' AND `a`.`CountryId` > 0) > 0
""");
    }

    public override async Task Update_where_keyless_entity_mapped_to_sql_query(bool async)
    {
        await base.Update_where_keyless_entity_mapped_to_sql_query(async);

        AssertExecuteUpdateSql();
    }

    public override async Task Update_with_interface_in_property_expression(bool async)
    {
        await base.Update_with_interface_in_property_expression(async);

        AssertExecuteUpdateSql(
            """
UPDATE `Drinks` AS `d`
SET `d`.`SugarGrams` = 0
WHERE `d`.`Discriminator` = 1
""");
    }

    public override async Task Update_with_interface_in_EF_Property_in_property_expression(bool async)
    {
        await base.Update_with_interface_in_EF_Property_in_property_expression(async);

        AssertExecuteUpdateSql(
            """
UPDATE `Drinks` AS `d`
SET `d`.`SugarGrams` = 0
WHERE `d`.`Discriminator` = 1
""");
    }

    protected override void ClearLog()
        => Fixture.TestSqlLoggerFactory.Clear();

    private void AssertSql(params string[] expected)
        => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);

    private void AssertExecuteUpdateSql(params string[] expected)
        => Fixture.TestSqlLoggerFactory.AssertBaseline(expected, forUpdate: true);
}
