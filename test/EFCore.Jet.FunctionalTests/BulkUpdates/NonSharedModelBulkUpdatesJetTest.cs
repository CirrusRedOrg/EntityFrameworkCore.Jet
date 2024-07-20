// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore.BulkUpdates;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests.BulkUpdates;

public class NonSharedModelBulkUpdatesJetTest : NonSharedModelBulkUpdatesTestBase
{
    protected override ITestStoreFactory TestStoreFactory
        => JetTestStoreFactory.Instance;

    [ConditionalFact]
    public virtual void Check_all_tests_overridden()
        => TestHelpers.AssertAllMethodsOverridden(GetType());

    public override async Task Delete_aggregate_root_when_eager_loaded_owned_collection(bool async)
    {
        await base.Delete_aggregate_root_when_eager_loaded_owned_collection(async);

        AssertSql(
"""
DELETE FROM `Owner` AS `o`
""");
    }

    public override async Task Delete_with_owned_collection_and_non_natively_translatable_query(bool async)
    {
        await base.Delete_with_owned_collection_and_non_natively_translatable_query(async);

        AssertSql(
            """
@__p_0='1'

DELETE FROM [o]
FROM [Owner] AS [o]
WHERE [o].[Id] IN (
    SELECT [o0].[Id]
    FROM [Owner] AS [o0]
    ORDER BY [o0].[Title]
    OFFSET @__p_0 ROWS
)
""");
    }

    public override async Task Delete_aggregate_root_when_table_sharing_with_owned(bool async)
    {
        await base.Delete_aggregate_root_when_table_sharing_with_owned(async);

        AssertSql(
"""
DELETE FROM `Owner` AS `o`
""");
    }

    public override async Task Delete_aggregate_root_when_table_sharing_with_non_owned_throws(bool async)
    {
        await base.Delete_aggregate_root_when_table_sharing_with_non_owned_throws(async);

        AssertSql();
    }

    public override async Task Replace_ColumnExpression_in_column_setter(bool async)
    {
        await base.Replace_ColumnExpression_in_column_setter(async);

        AssertSql(
            """
UPDATE `Owner` AS `o`
INNER JOIN `OwnedCollection` AS `o0` ON `o`.`Id` = `o0`.`OwnerId`
SET `o0`.`Value` = 'SomeValue'
""");
    }

    public override async Task Update_non_owned_property_on_entity_with_owned(bool async)
    {
        await base.Update_non_owned_property_on_entity_with_owned(async);

        AssertSql(
"""
UPDATE `Owner` AS `o`
SET `o`.`Title` = 'SomeValue'
""");
    }

    public override async Task Update_non_owned_property_on_entity_with_owned2(bool async)
    {
        await base.Update_non_owned_property_on_entity_with_owned2(async);

        AssertSql(
"""
UPDATE `Owner` AS `o`
SET `o`.`Title` = IIF(`o`.`Title` IS NULL, '', `o`.`Title`) & '_Suffix'
""");
    }

    public override async Task Update_non_owned_property_on_entity_with_owned_in_join(bool async)
    {
        await base.Update_non_owned_property_on_entity_with_owned_in_join(async);

        AssertSql(
    """
UPDATE `Owner` AS `o`
INNER JOIN `Owner` AS `o0` ON `o`.`Id` = `o0`.`Id`
SET `o`.`Title` = 'NewValue'
""");
    }

    public override async Task Update_owned_and_non_owned_properties_with_table_sharing(bool async)
    {
        await base.Update_owned_and_non_owned_properties_with_table_sharing(async);

        AssertSql(
            """
UPDATE `Owner` AS `o`
SET `o`.`OwnedReference_Number` = IIF(LEN(`o`.`Title`) IS NULL, NULL, CLNG(LEN(`o`.`Title`))),
    `o`.`Title` = (`o`.`OwnedReference_Number` & '')
""");
    }

    public override async Task Update_main_table_in_entity_with_entity_splitting(bool async)
    {
        await base.Update_main_table_in_entity_with_entity_splitting(async);

        AssertSql(
            """
UPDATE `Blogs` AS `b`
SET `b`.`CreationTimestamp` = #2020-01-01#
""");
    }

    public override async Task Update_non_main_table_in_entity_with_entity_splitting(bool async)
    {
        await base.Update_non_main_table_in_entity_with_entity_splitting(async);

        AssertSql(
            """
UPDATE `Blogs` AS `b`
INNER JOIN `BlogsPart1` AS `b0` ON `b`.`Id` = `b0`.`Id`
SET `b0`.`Rating` = IIF(LEN(`b0`.`Title`) IS NULL, NULL, CLNG(LEN(`b0`.`Title`))),
    `b0`.`Title` = (`b0`.`Rating` & '')
""");
    }

    public override async Task Delete_entity_with_auto_include(bool async)
    {
        await base.Delete_entity_with_auto_include(async);

        AssertSql(
"""
DELETE `c`.*
FROM `Context30572_Principal` AS `c`
LEFT JOIN `Context30572_Dependent` AS `c0` ON `c`.`DependentId` = `c0`.`Id`
""");
    }

    public override async Task Delete_predicate_based_on_optional_navigation(bool async)
    {
        await base.Delete_predicate_based_on_optional_navigation(async);

        AssertSql(
"""
DELETE `p`.*
FROM `Posts` AS `p`
LEFT JOIN `Blogs` AS `b` ON `p`.`BlogId` = `b`.`Id`
WHERE `b`.`Title` LIKE 'Arthur%'
""");
    }

    public override async Task Update_with_alias_uniquification_in_setter_subquery(bool async)
    {
        await base.Update_with_alias_uniquification_in_setter_subquery(async);

        AssertSql(
            """
UPDATE [o]
SET [o].[Total] = (
    SELECT COALESCE(SUM([o0].[Amount]), 0)
    FROM [OrderProduct] AS [o0]
    WHERE [o].[Id] = [o0].[OrderId])
FROM [Orders] AS [o]
WHERE [o].[Id] = 1
""");
    }

    private void AssertSql(params string[] expected)
        => TestSqlLoggerFactory.AssertBaseline(expected);

    private void AssertExecuteUpdateSql(params string[] expected)
        => TestSqlLoggerFactory.AssertBaseline(expected, forUpdate: true);
}
