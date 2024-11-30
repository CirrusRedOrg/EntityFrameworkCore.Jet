// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Query;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query;

public class ManyToManyNoTrackingQueryJetTest
    : ManyToManyNoTrackingQueryRelationalTestBase<ManyToManyQueryJetFixture>
{
    public ManyToManyNoTrackingQueryJetTest(ManyToManyQueryJetFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    public override async Task Skip_navigation_all(bool async)
    {
        await base.Skip_navigation_all(async);

        AssertSql(
            """
SELECT `e`.`Id`, `e`.`Name`
FROM `EntityOnes` AS `e`
WHERE NOT EXISTS (
    SELECT `j`.`OneId`
    FROM `JoinOneToTwo` AS `j`
    INNER JOIN `EntityTwos` AS `e0` ON `j`.`TwoId` = `e0`.`Id`
    WHERE `e`.`Id` = `j`.`OneId` AND (`e0`.`Name` NOT LIKE '%B%' OR `e0`.`Name` IS NULL))
""");
    }

    public override async Task Skip_navigation_any_without_predicate(bool async)
    {
        await base.Skip_navigation_any_without_predicate(async);

        AssertSql(
            """
SELECT `e`.`Id`, `e`.`Name`
FROM `EntityOnes` AS `e`
WHERE EXISTS (
    SELECT 1
    FROM `JoinOneToThreePayloadFull` AS `j`
    INNER JOIN `EntityThrees` AS `e0` ON `j`.`ThreeId` = `e0`.`Id`
    WHERE `e`.`Id` = `j`.`OneId` AND (`e0`.`Name` LIKE '%B%'))
""");
    }

    public override async Task Skip_navigation_any_with_predicate(bool async)
    {
        await base.Skip_navigation_any_with_predicate(async);

        AssertSql(
            """
SELECT `e`.`Id`, `e`.`Name`
FROM `EntityOnes` AS `e`
WHERE EXISTS (
    SELECT 1
    FROM `EntityOneEntityTwo` AS `e0`
    INNER JOIN `EntityTwos` AS `e1` ON `e0`.`TwoSkipSharedId` = `e1`.`Id`
    WHERE `e`.`Id` = `e0`.`OneSkipSharedId` AND (`e1`.`Name` LIKE '%B%'))
""");
    }

    public override async Task Skip_navigation_contains(bool async)
    {
        await base.Skip_navigation_contains(async);

        AssertSql(
"""
SELECT `e`.`Id`, `e`.`Name`
FROM `EntityOnes` AS `e`
WHERE EXISTS (
    SELECT 1
    FROM `JoinOneToThreePayloadFullShared` AS `j`
    INNER JOIN `EntityThrees` AS `e0` ON `j`.`ThreeId` = `e0`.`Id`
    WHERE `e`.`Id` = `j`.`OneId` AND `e0`.`Id` = 1)
""");
    }

    public override async Task Skip_navigation_count_without_predicate(bool async)
    {
        await base.Skip_navigation_count_without_predicate(async);

        AssertSql(
"""
SELECT [e].[Id], [e].[Name]
FROM [EntityOnes] AS [e]
WHERE (
    SELECT COUNT(*)
    FROM [JoinOneSelfPayload] AS [j]
    INNER JOIN [EntityOnes] AS [e0] ON [j].[LeftId] = [e0].[Id]
    WHERE [e].[Id] = [j].[RightId]) > 0
""");
    }

    public override async Task Skip_navigation_count_with_predicate(bool async)
    {
        await base.Skip_navigation_count_with_predicate(async);

        AssertSql(
            """
SELECT `e2`.`Id`, `e2`.`Name`, `e2`.`c`
FROM (
    SELECT `e`.`Id`, `e`.`Name`, (
        SELECT COUNT(*)
        FROM `JoinOneToBranch` AS `j`
        INNER JOIN (
            SELECT `e0`.`Id`, `e0`.`Name`
            FROM `EntityRoots` AS `e0`
            WHERE `e0`.`Discriminator` IN ('EntityBranch', 'EntityLeaf')
        ) AS `e1` ON `j`.`EntityBranchId` = `e1`.`Id`
        WHERE `e`.`Id` = `j`.`EntityOneId` AND (`e1`.`Name` LIKE 'L%')) AS `c`
    FROM `EntityOnes` AS `e`
) AS `e2`
ORDER BY `e2`.`c`, `e2`.`Id`
""");
    }

    public override async Task Skip_navigation_long_count_without_predicate(bool async)
    {
        await base.Skip_navigation_long_count_without_predicate(async);

        AssertSql(
"""
SELECT [e].[Id], [e].[CollectionInverseId], [e].[ExtraId], [e].[Name], [e].[ReferenceInverseId]
FROM [EntityTwos] AS [e]
WHERE (
    SELECT COUNT_BIG(*)
    FROM [JoinTwoToThree] AS [j]
    INNER JOIN [EntityThrees] AS [e0] ON [j].[ThreeId] = [e0].[Id]
    WHERE [e].[Id] = [j].[TwoId]) > CAST(0 AS bigint)
""");
    }

    public override async Task Skip_navigation_long_count_with_predicate(bool async)
    {
        await base.Skip_navigation_long_count_with_predicate(async);

        AssertSql(
            """
SELECT `e2`.`Id`, `e2`.`CollectionInverseId`, `e2`.`ExtraId`, `e2`.`Name`, `e2`.`ReferenceInverseId`, `e2`.`c`
FROM (
    SELECT `e`.`Id`, `e`.`CollectionInverseId`, `e`.`ExtraId`, `e`.`Name`, `e`.`ReferenceInverseId`, (
        SELECT COUNT(*)
        FROM `EntityTwoEntityTwo` AS `e0`
        INNER JOIN `EntityTwos` AS `e1` ON `e0`.`SelfSkipSharedLeftId` = `e1`.`Id`
        WHERE `e`.`Id` = `e0`.`SelfSkipSharedRightId` AND (`e1`.`Name` LIKE 'L%')) AS `c`
    FROM `EntityTwos` AS `e`
) AS `e2`
ORDER BY `e2`.`c` DESC, `e2`.`Id`
""");
    }

    public override async Task Skip_navigation_select_many_average(bool async)
    {
        await base.Skip_navigation_select_many_average(async);

        AssertSql(
            """
SELECT AVG(CDBL(`s`.`Key1`))
FROM `EntityTwos` AS `e`
INNER JOIN (
    SELECT `e1`.`Key1`, `e0`.`TwoSkipSharedId`
    FROM `EntityCompositeKeyEntityTwo` AS `e0`
    INNER JOIN `EntityCompositeKeys` AS `e1` ON `e0`.`CompositeKeySkipSharedKey1` = `e1`.`Key1` AND `e0`.`CompositeKeySkipSharedKey2` = `e1`.`Key2` AND `e0`.`CompositeKeySkipSharedKey3` = `e1`.`Key3`
) AS `s` ON `e`.`Id` = `s`.`TwoSkipSharedId`
""");
    }

    public override async Task Skip_navigation_select_many_max(bool async)
    {
        await base.Skip_navigation_select_many_max(async);

        AssertSql(
            """
SELECT MAX(`s`.`Key1`)
FROM `EntityThrees` AS `e`
INNER JOIN (
    SELECT `e0`.`Key1`, `j`.`ThreeId`
    FROM `JoinThreeToCompositeKeyFull` AS `j`
    INNER JOIN `EntityCompositeKeys` AS `e0` ON `j`.`CompositeId1` = `e0`.`Key1` AND `j`.`CompositeId2` = `e0`.`Key2` AND `j`.`CompositeId3` = `e0`.`Key3`
) AS `s` ON `e`.`Id` = `s`.`ThreeId`
""");
    }

    public override async Task Skip_navigation_select_many_min(bool async)
    {
        await base.Skip_navigation_select_many_min(async);

        AssertSql(
            """
SELECT MIN(`s`.`Id`)
FROM `EntityThrees` AS `e`
INNER JOIN (
    SELECT `e1`.`Id`, `e0`.`ThreeSkipSharedId`
    FROM `EntityRootEntityThree` AS `e0`
    INNER JOIN `EntityRoots` AS `e1` ON `e0`.`RootSkipSharedId` = `e1`.`Id`
) AS `s` ON `e`.`Id` = `s`.`ThreeSkipSharedId`
""");
    }

    public override async Task Skip_navigation_select_many_sum(bool async)
    {
        await base.Skip_navigation_select_many_sum(async);

        AssertSql(
            """
SELECT IIF(SUM(`s`.`Key1`) IS NULL, 0, SUM(`s`.`Key1`))
FROM `EntityRoots` AS `e`
INNER JOIN (
    SELECT `e1`.`Key1`, `e0`.`RootSkipSharedId`
    FROM `EntityCompositeKeyEntityRoot` AS `e0`
    INNER JOIN `EntityCompositeKeys` AS `e1` ON `e0`.`CompositeKeySkipSharedKey1` = `e1`.`Key1` AND `e0`.`CompositeKeySkipSharedKey2` = `e1`.`Key2` AND `e0`.`CompositeKeySkipSharedKey3` = `e1`.`Key3`
) AS `s` ON `e`.`Id` = `s`.`RootSkipSharedId`
""");
    }

    public override async Task Skip_navigation_select_subquery_average(bool async)
    {
        await base.Skip_navigation_select_subquery_average(async);

        AssertSql(
            """
SELECT (
    SELECT AVG(CDBL(`e0`.`Key1`))
    FROM `JoinCompositeKeyToLeaf` AS `j`
    INNER JOIN `EntityCompositeKeys` AS `e0` ON `j`.`CompositeId1` = `e0`.`Key1` AND `j`.`CompositeId2` = `e0`.`Key2` AND `j`.`CompositeId3` = `e0`.`Key3`
    WHERE `e`.`Id` = `j`.`LeafId`)
FROM `EntityRoots` AS `e`
WHERE `e`.`Discriminator` = 'EntityLeaf'
""");
    }

    public override async Task Skip_navigation_select_subquery_max(bool async)
    {
        await base.Skip_navigation_select_subquery_max(async);

        AssertSql(
"""
SELECT (
    SELECT MAX(`e0`.`Id`)
    FROM `JoinOneToTwo` AS `j`
    INNER JOIN `EntityOnes` AS `e0` ON `j`.`OneId` = `e0`.`Id`
    WHERE `e`.`Id` = `j`.`TwoId`)
FROM `EntityTwos` AS `e`
""");
    }

    public override async Task Skip_navigation_select_subquery_min(bool async)
    {
        await base.Skip_navigation_select_subquery_min(async);

        AssertSql(
"""
SELECT (
    SELECT MIN(`e0`.`Id`)
    FROM `JoinOneToThreePayloadFull` AS `j`
    INNER JOIN `EntityOnes` AS `e0` ON `j`.`OneId` = `e0`.`Id`
    WHERE `e`.`Id` = `j`.`ThreeId`)
FROM `EntityThrees` AS `e`
""");
    }

    public override async Task Skip_navigation_select_subquery_sum(bool async)
    {
        await base.Skip_navigation_select_subquery_sum(async);

        AssertSql(
"""
SELECT (
    SELECT IIF(SUM(`e1`.`Id`) IS NULL, 0, SUM(`e1`.`Id`))
    FROM `EntityOneEntityTwo` AS `e0`
    INNER JOIN `EntityOnes` AS `e1` ON `e0`.`OneSkipSharedId` = `e1`.`Id`
    WHERE `e`.`Id` = `e0`.`TwoSkipSharedId`)
FROM `EntityTwos` AS `e`
""");
    }

    public override async Task Skip_navigation_order_by_first_or_default(bool async)
    {
        await base.Skip_navigation_order_by_first_or_default(async);

        AssertSql(
"""
SELECT [t0].[Id], [t0].[Name]
FROM [EntityThrees] AS [e]
LEFT JOIN (
    SELECT [t].[Id], [t].[Name], [t].[ThreeId]
    FROM (
        SELECT [e0].[Id], [e0].[Name], [j].[ThreeId], ROW_NUMBER() OVER(PARTITION BY [j].[ThreeId] ORDER BY [e0].[Id]) AS [row]
        FROM [JoinOneToThreePayloadFullShared] AS [j]
        INNER JOIN [EntityOnes] AS [e0] ON [j].[OneId] = [e0].[Id]
    ) AS [t]
    WHERE [t].[row] <= 1
) AS [t0] ON [e].[Id] = [t0].[ThreeId]
""");
    }

    public override async Task Skip_navigation_order_by_single_or_default(bool async)
    {
        await base.Skip_navigation_order_by_single_or_default(async);

        AssertSql(
"""
SELECT [t0].[Id], [t0].[Name]
FROM [EntityOnes] AS [e]
OUTER APPLY (
    SELECT TOP(1) [t].[Id], [t].[Name]
    FROM (
        SELECT TOP(1) [e0].[Id], [e0].[Name]
        FROM [JoinOneSelfPayload] AS [j]
        INNER JOIN [EntityOnes] AS [e0] ON [j].[RightId] = [e0].[Id]
        WHERE [e].[Id] = [j].[LeftId]
        ORDER BY [e0].[Id]
    ) AS [t]
    ORDER BY [t].[Id]
) AS [t0]
""");
    }

    public override async Task Skip_navigation_order_by_last_or_default(bool async)
    {
        await base.Skip_navigation_order_by_last_or_default(async);

        AssertSql(
"""
SELECT [t0].[Id], [t0].[Name]
FROM [EntityRoots] AS [e]
LEFT JOIN (
    SELECT [t].[Id], [t].[Name], [t].[EntityBranchId]
    FROM (
        SELECT [e0].[Id], [e0].[Name], [j].[EntityBranchId], ROW_NUMBER() OVER(PARTITION BY [j].[EntityBranchId] ORDER BY [e0].[Id] DESC) AS [row]
        FROM [JoinOneToBranch] AS [j]
        INNER JOIN [EntityOnes] AS [e0] ON [j].[EntityOneId] = [e0].[Id]
    ) AS [t]
    WHERE [t].[row] <= 1
) AS [t0] ON [e].[Id] = [t0].[EntityBranchId]
WHERE [e].[Discriminator] IN (N'EntityBranch', N'EntityLeaf')
""");
    }

    public override async Task Skip_navigation_order_by_reverse_first_or_default(bool async)
    {
        await base.Skip_navigation_order_by_reverse_first_or_default(async);

        AssertSql(
"""
SELECT [t0].[Id], [t0].[CollectionInverseId], [t0].[ExtraId], [t0].[Name], [t0].[ReferenceInverseId]
FROM [EntityThrees] AS [e]
LEFT JOIN (
    SELECT [t].[Id], [t].[CollectionInverseId], [t].[ExtraId], [t].[Name], [t].[ReferenceInverseId], [t].[ThreeId]
    FROM (
        SELECT [e0].[Id], [e0].[CollectionInverseId], [e0].[ExtraId], [e0].[Name], [e0].[ReferenceInverseId], [j].[ThreeId], ROW_NUMBER() OVER(PARTITION BY [j].[ThreeId] ORDER BY [e0].[Id] DESC) AS [row]
        FROM [JoinTwoToThree] AS [j]
        INNER JOIN [EntityTwos] AS [e0] ON [j].[TwoId] = [e0].[Id]
    ) AS [t]
    WHERE [t].[row] <= 1
) AS [t0] ON [e].[Id] = [t0].[ThreeId]
""");
    }

    public override async Task Skip_navigation_cast(bool async)
    {
        await base.Skip_navigation_cast(async);

        AssertSql(
            """
SELECT `e`.`Key1`, `e`.`Key2`, `e`.`Key3`, `s`.`Id`, `s`.`Discriminator`, `s`.`Name`, `s`.`Number`, `s`.`IsGreen`, `s`.`LeafId`, `s`.`CompositeId1`, `s`.`CompositeId2`, `s`.`CompositeId3`
FROM `EntityCompositeKeys` AS `e`
LEFT JOIN (
    SELECT `e1`.`Id`, `e1`.`Discriminator`, `e1`.`Name`, `e1`.`Number`, `e1`.`IsGreen`, `j`.`LeafId`, `j`.`CompositeId1`, `j`.`CompositeId2`, `j`.`CompositeId3`
    FROM `JoinCompositeKeyToLeaf` AS `j`
    INNER JOIN (
        SELECT `e0`.`Id`, `e0`.`Discriminator`, `e0`.`Name`, `e0`.`Number`, `e0`.`IsGreen`
        FROM `EntityRoots` AS `e0`
        WHERE `e0`.`Discriminator` = 'EntityLeaf'
    ) AS `e1` ON `j`.`LeafId` = `e1`.`Id`
) AS `s` ON `e`.`Key1` = `s`.`CompositeId1` AND `e`.`Key2` = `s`.`CompositeId2` AND `e`.`Key3` = `s`.`CompositeId3`
ORDER BY `e`.`Key1`, `e`.`Key2`, `e`.`Key3`, `s`.`LeafId`, `s`.`CompositeId1`, `s`.`CompositeId2`, `s`.`CompositeId3`
""");
    }

    public override async Task Skip_navigation_of_type(bool async)
    {
        await base.Skip_navigation_of_type(async);

        AssertSql(
            """
SELECT `e`.`Key1`, `e`.`Key2`, `e`.`Key3`, `s`.`Id`, `s`.`Discriminator`, `s`.`Name`, `s`.`Number`, `s`.`IsGreen`, `s`.`RootSkipSharedId`, `s`.`CompositeKeySkipSharedKey1`, `s`.`CompositeKeySkipSharedKey2`, `s`.`CompositeKeySkipSharedKey3`
FROM `EntityCompositeKeys` AS `e`
LEFT JOIN (
    SELECT `e1`.`Id`, `e1`.`Discriminator`, `e1`.`Name`, `e1`.`Number`, `e1`.`IsGreen`, `e0`.`RootSkipSharedId`, `e0`.`CompositeKeySkipSharedKey1`, `e0`.`CompositeKeySkipSharedKey2`, `e0`.`CompositeKeySkipSharedKey3`
    FROM `EntityCompositeKeyEntityRoot` AS `e0`
    INNER JOIN `EntityRoots` AS `e1` ON `e0`.`RootSkipSharedId` = `e1`.`Id`
    WHERE `e1`.`Discriminator` = 'EntityLeaf'
) AS `s` ON `e`.`Key1` = `s`.`CompositeKeySkipSharedKey1` AND `e`.`Key2` = `s`.`CompositeKeySkipSharedKey2` AND `e`.`Key3` = `s`.`CompositeKeySkipSharedKey3`
ORDER BY `e`.`Key1`, `e`.`Key2`, `e`.`Key3`, `s`.`RootSkipSharedId`, `s`.`CompositeKeySkipSharedKey1`, `s`.`CompositeKeySkipSharedKey2`, `s`.`CompositeKeySkipSharedKey3`
""");
    }

    public override async Task Join_with_skip_navigation(bool async)
    {
        await base.Join_with_skip_navigation(async);

        AssertSql(
"""
SELECT [e].[Id], [e].[CollectionInverseId], [e].[ExtraId], [e].[Name], [e].[ReferenceInverseId], [e0].[Id], [e0].[CollectionInverseId], [e0].[ExtraId], [e0].[Name], [e0].[ReferenceInverseId]
FROM [EntityTwos] AS [e]
INNER JOIN [EntityTwos] AS [e0] ON [e].[Id] = (
    SELECT TOP(1) [e2].[Id]
    FROM [EntityTwoEntityTwo] AS [e1]
    INNER JOIN [EntityTwos] AS [e2] ON [e1].[SelfSkipSharedRightId] = [e2].[Id]
    WHERE [e0].[Id] = [e1].[SelfSkipSharedLeftId]
    ORDER BY [e2].[Id])
""");
    }

    public override async Task Left_join_with_skip_navigation(bool async)
    {
        await base.Left_join_with_skip_navigation(async);

        AssertSql(
"""
SELECT [e].[Key1], [e].[Key2], [e].[Key3], [e].[Name], [e0].[Key1], [e0].[Key2], [e0].[Key3], [e0].[Name]
FROM [EntityCompositeKeys] AS [e]
LEFT JOIN [EntityCompositeKeys] AS [e0] ON (
    SELECT TOP(1) [e2].[Id]
    FROM [EntityCompositeKeyEntityTwo] AS [e1]
    INNER JOIN [EntityTwos] AS [e2] ON [e1].[TwoSkipSharedId] = [e2].[Id]
    WHERE [e].[Key1] = [e1].[CompositeKeySkipSharedKey1] AND [e].[Key2] = [e1].[CompositeKeySkipSharedKey2] AND [e].[Key3] = [e1].[CompositeKeySkipSharedKey3]
    ORDER BY [e2].[Id]) = (
    SELECT TOP(1) [e3].[Id]
    FROM [JoinThreeToCompositeKeyFull] AS [j]
    INNER JOIN [EntityThrees] AS [e3] ON [j].[ThreeId] = [e3].[Id]
    WHERE [e0].[Key1] = [j].[CompositeId1] AND [e0].[Key2] = [j].[CompositeId2] AND [e0].[Key3] = [j].[CompositeId3]
    ORDER BY [e3].[Id])
ORDER BY [e].[Key1], [e0].[Key1], [e].[Key2], [e0].[Key2]
""");
    }

    public override async Task Select_many_over_skip_navigation(bool async)
    {
        await base.Select_many_over_skip_navigation(async);

        AssertSql(
            """
SELECT `s`.`Id`, `s`.`CollectionInverseId`, `s`.`Name`, `s`.`ReferenceInverseId`
FROM `EntityRoots` AS `e`
INNER JOIN (
    SELECT `e1`.`Id`, `e1`.`CollectionInverseId`, `e1`.`Name`, `e1`.`ReferenceInverseId`, `e0`.`RootSkipSharedId`
    FROM `EntityRootEntityThree` AS `e0`
    INNER JOIN `EntityThrees` AS `e1` ON `e0`.`ThreeSkipSharedId` = `e1`.`Id`
) AS `s` ON `e`.`Id` = `s`.`RootSkipSharedId`
""");
    }

    public override async Task Select_many_over_skip_navigation_where(bool async)
    {
        await base.Select_many_over_skip_navigation_where(async);

        AssertSql(
            """
SELECT `s`.`Id`, `s`.`CollectionInverseId`, `s`.`ExtraId`, `s`.`Name`, `s`.`ReferenceInverseId`
FROM `EntityOnes` AS `e`
LEFT JOIN (
    SELECT `e0`.`Id`, `e0`.`CollectionInverseId`, `e0`.`ExtraId`, `e0`.`Name`, `e0`.`ReferenceInverseId`, `j`.`OneId`
    FROM `JoinOneToTwo` AS `j`
    INNER JOIN `EntityTwos` AS `e0` ON `j`.`TwoId` = `e0`.`Id`
) AS `s` ON `e`.`Id` = `s`.`OneId`
""");
    }

    public override async Task Select_many_over_skip_navigation_order_by_skip(bool async)
    {
        await base.Select_many_over_skip_navigation_order_by_skip(async);

        AssertSql(
"""
SELECT [t0].[Id], [t0].[CollectionInverseId], [t0].[Name], [t0].[ReferenceInverseId]
FROM [EntityOnes] AS [e]
INNER JOIN (
    SELECT [t].[Id], [t].[CollectionInverseId], [t].[Name], [t].[ReferenceInverseId], [t].[OneId]
    FROM (
        SELECT [e0].[Id], [e0].[CollectionInverseId], [e0].[Name], [e0].[ReferenceInverseId], [j].[OneId], ROW_NUMBER() OVER(PARTITION BY [j].[OneId] ORDER BY [e0].[Id]) AS [row]
        FROM [JoinOneToThreePayloadFull] AS [j]
        INNER JOIN [EntityThrees] AS [e0] ON [j].[ThreeId] = [e0].[Id]
    ) AS [t]
    WHERE 2 < [t].[row]
) AS [t0] ON [e].[Id] = [t0].[OneId]
""");
    }

    public override async Task Select_many_over_skip_navigation_order_by_take(bool async)
    {
        await base.Select_many_over_skip_navigation_order_by_take(async);

        AssertSql(
"""
SELECT [t0].[Id], [t0].[CollectionInverseId], [t0].[ExtraId], [t0].[Name], [t0].[ReferenceInverseId]
FROM [EntityOnes] AS [e]
INNER JOIN (
    SELECT [t].[Id], [t].[CollectionInverseId], [t].[ExtraId], [t].[Name], [t].[ReferenceInverseId], [t].[OneSkipSharedId]
    FROM (
        SELECT [e1].[Id], [e1].[CollectionInverseId], [e1].[ExtraId], [e1].[Name], [e1].[ReferenceInverseId], [e0].[OneSkipSharedId], ROW_NUMBER() OVER(PARTITION BY [e0].[OneSkipSharedId] ORDER BY [e1].[Id]) AS [row]
        FROM [EntityOneEntityTwo] AS [e0]
        INNER JOIN [EntityTwos] AS [e1] ON [e0].[TwoSkipSharedId] = [e1].[Id]
    ) AS [t]
    WHERE [t].[row] <= 2
) AS [t0] ON [e].[Id] = [t0].[OneSkipSharedId]
""");
    }

    public override async Task Select_many_over_skip_navigation_order_by_skip_take(bool async)
    {
        await base.Select_many_over_skip_navigation_order_by_skip_take(async);

        AssertSql(
"""
SELECT [t0].[Id], [t0].[CollectionInverseId], [t0].[Name], [t0].[ReferenceInverseId]
FROM [EntityOnes] AS [e]
INNER JOIN (
    SELECT [t].[Id], [t].[CollectionInverseId], [t].[Name], [t].[ReferenceInverseId], [t].[OneId]
    FROM (
        SELECT [e0].[Id], [e0].[CollectionInverseId], [e0].[Name], [e0].[ReferenceInverseId], [j].[OneId], ROW_NUMBER() OVER(PARTITION BY [j].[OneId] ORDER BY [e0].[Id]) AS [row]
        FROM [JoinOneToThreePayloadFullShared] AS [j]
        INNER JOIN [EntityThrees] AS [e0] ON [j].[ThreeId] = [e0].[Id]
    ) AS [t]
    WHERE 2 < [t].[row] AND [t].[row] <= 5
) AS [t0] ON [e].[Id] = [t0].[OneId]
""");
    }

    public override async Task Select_many_over_skip_navigation_of_type(bool async)
    {
        await base.Select_many_over_skip_navigation_of_type(async);

        AssertSql(
            """
SELECT `s`.`Id`, `s`.`Discriminator`, `s`.`Name`, `s`.`Number`, `s`.`IsGreen`
FROM `EntityThrees` AS `e`
INNER JOIN (
    SELECT `e1`.`Id`, `e1`.`Discriminator`, `e1`.`Name`, `e1`.`Number`, `e1`.`IsGreen`, `e0`.`ThreeSkipSharedId`
    FROM `EntityRootEntityThree` AS `e0`
    INNER JOIN `EntityRoots` AS `e1` ON `e0`.`RootSkipSharedId` = `e1`.`Id`
    WHERE `e1`.`Discriminator` IN ('EntityBranch', 'EntityLeaf')
) AS `s` ON `e`.`Id` = `s`.`ThreeSkipSharedId`
""");
    }

    public override async Task Select_many_over_skip_navigation_cast(bool async)
    {
        await base.Select_many_over_skip_navigation_cast(async);

        AssertSql(
            """
SELECT `s`.`Id`, `s`.`Discriminator`, `s`.`Name`, `s`.`Number`, `s`.`IsGreen`
FROM `EntityOnes` AS `e`
INNER JOIN (
    SELECT `e1`.`Id`, `e1`.`Discriminator`, `e1`.`Name`, `e1`.`Number`, `e1`.`IsGreen`, `j`.`EntityOneId`
    FROM `JoinOneToBranch` AS `j`
    INNER JOIN (
        SELECT `e0`.`Id`, `e0`.`Discriminator`, `e0`.`Name`, `e0`.`Number`, `e0`.`IsGreen`
        FROM `EntityRoots` AS `e0`
        WHERE `e0`.`Discriminator` IN ('EntityBranch', 'EntityLeaf')
    ) AS `e1` ON `j`.`EntityBranchId` = `e1`.`Id`
) AS `s` ON `e`.`Id` = `s`.`EntityOneId`
""");
    }

    public override async Task Select_skip_navigation(bool async)
    {
        await base.Select_skip_navigation(async);

        AssertSql(
            """
SELECT `e`.`Id`, `s`.`Id`, `s`.`Name`, `s`.`LeftId`, `s`.`RightId`
FROM `EntityOnes` AS `e`
LEFT JOIN (
    SELECT `e0`.`Id`, `e0`.`Name`, `j`.`LeftId`, `j`.`RightId`
    FROM `JoinOneSelfPayload` AS `j`
    INNER JOIN `EntityOnes` AS `e0` ON `j`.`LeftId` = `e0`.`Id`
) AS `s` ON `e`.`Id` = `s`.`RightId`
ORDER BY `e`.`Id`, `s`.`LeftId`, `s`.`RightId`
""");
    }

    public override async Task Select_skip_navigation_multiple(bool async)
    {
        await base.Select_skip_navigation_multiple(async);

        AssertSql(
            """
SELECT `e`.`Id`, `s`.`Id`, `s`.`CollectionInverseId`, `s`.`Name`, `s`.`ReferenceInverseId`, `s`.`ThreeId`, `s`.`TwoId`, `s0`.`Id`, `s0`.`CollectionInverseId`, `s0`.`ExtraId`, `s0`.`Name`, `s0`.`ReferenceInverseId`, `s0`.`SelfSkipSharedLeftId`, `s0`.`SelfSkipSharedRightId`, `s1`.`Key1`, `s1`.`Key2`, `s1`.`Key3`, `s1`.`Name`, `s1`.`TwoSkipSharedId`, `s1`.`CompositeKeySkipSharedKey1`, `s1`.`CompositeKeySkipSharedKey2`, `s1`.`CompositeKeySkipSharedKey3`
FROM ((`EntityTwos` AS `e`
LEFT JOIN (
    SELECT `e0`.`Id`, `e0`.`CollectionInverseId`, `e0`.`Name`, `e0`.`ReferenceInverseId`, `j`.`ThreeId`, `j`.`TwoId`
    FROM `JoinTwoToThree` AS `j`
    INNER JOIN `EntityThrees` AS `e0` ON `j`.`ThreeId` = `e0`.`Id`
) AS `s` ON `e`.`Id` = `s`.`TwoId`)
LEFT JOIN (
    SELECT `e2`.`Id`, `e2`.`CollectionInverseId`, `e2`.`ExtraId`, `e2`.`Name`, `e2`.`ReferenceInverseId`, `e1`.`SelfSkipSharedLeftId`, `e1`.`SelfSkipSharedRightId`
    FROM `EntityTwoEntityTwo` AS `e1`
    INNER JOIN `EntityTwos` AS `e2` ON `e1`.`SelfSkipSharedLeftId` = `e2`.`Id`
) AS `s0` ON `e`.`Id` = `s0`.`SelfSkipSharedRightId`)
LEFT JOIN (
    SELECT `e4`.`Key1`, `e4`.`Key2`, `e4`.`Key3`, `e4`.`Name`, `e3`.`TwoSkipSharedId`, `e3`.`CompositeKeySkipSharedKey1`, `e3`.`CompositeKeySkipSharedKey2`, `e3`.`CompositeKeySkipSharedKey3`
    FROM `EntityCompositeKeyEntityTwo` AS `e3`
    INNER JOIN `EntityCompositeKeys` AS `e4` ON `e3`.`CompositeKeySkipSharedKey1` = `e4`.`Key1` AND `e3`.`CompositeKeySkipSharedKey2` = `e4`.`Key2` AND `e3`.`CompositeKeySkipSharedKey3` = `e4`.`Key3`
) AS `s1` ON `e`.`Id` = `s1`.`TwoSkipSharedId`
ORDER BY `e`.`Id`, `s`.`ThreeId`, `s`.`TwoId`, `s`.`Id`, `s0`.`SelfSkipSharedLeftId`, `s0`.`SelfSkipSharedRightId`, `s0`.`Id`, `s1`.`TwoSkipSharedId`, `s1`.`CompositeKeySkipSharedKey1`, `s1`.`CompositeKeySkipSharedKey2`, `s1`.`CompositeKeySkipSharedKey3`, `s1`.`Key1`, `s1`.`Key2`
""");
    }

    public override async Task Select_skip_navigation_first_or_default(bool async)
    {
        await base.Select_skip_navigation_first_or_default(async);

        AssertSql(
"""
SELECT [t0].[Key1], [t0].[Key2], [t0].[Key3], [t0].[Name]
FROM [EntityThrees] AS [e]
LEFT JOIN (
    SELECT [t].[Key1], [t].[Key2], [t].[Key3], [t].[Name], [t].[ThreeId]
    FROM (
        SELECT [e0].[Key1], [e0].[Key2], [e0].[Key3], [e0].[Name], [j].[ThreeId], ROW_NUMBER() OVER(PARTITION BY [j].[ThreeId] ORDER BY [e0].[Key1], [e0].[Key2]) AS [row]
        FROM [JoinThreeToCompositeKeyFull] AS [j]
        INNER JOIN [EntityCompositeKeys] AS [e0] ON [j].[CompositeId1] = [e0].[Key1] AND [j].[CompositeId2] = [e0].[Key2] AND [j].[CompositeId3] = [e0].[Key3]
    ) AS [t]
    WHERE [t].[row] <= 1
) AS [t0] ON [e].[Id] = [t0].[ThreeId]
ORDER BY [e].[Id]
""");
    }

    public override async Task Include_skip_navigation(bool async)
    {
        await base.Include_skip_navigation(async);

        AssertSql(
            """
SELECT `e`.`Key1`, `e`.`Key2`, `e`.`Key3`, `e`.`Name`, `s`.`Id`, `s`.`Discriminator`, `s`.`Name`, `s`.`Number`, `s`.`Slumber`, `s`.`IsGreen`, `s`.`IsBrown`, `s`.`RootSkipSharedId`, `s`.`CompositeKeySkipSharedKey1`, `s`.`CompositeKeySkipSharedKey2`, `s`.`CompositeKeySkipSharedKey3`
FROM `EntityCompositeKeys` AS `e`
LEFT JOIN (
    SELECT `e1`.`Id`, `e1`.`Discriminator`, `e1`.`Name`, `e1`.`Number`, `e1`.`Slumber`, `e1`.`IsGreen`, `e1`.`IsBrown`, `e0`.`RootSkipSharedId`, `e0`.`CompositeKeySkipSharedKey1`, `e0`.`CompositeKeySkipSharedKey2`, `e0`.`CompositeKeySkipSharedKey3`
    FROM `EntityCompositeKeyEntityRoot` AS `e0`
    INNER JOIN `EntityRoots` AS `e1` ON `e0`.`RootSkipSharedId` = `e1`.`Id`
) AS `s` ON `e`.`Key1` = `s`.`CompositeKeySkipSharedKey1` AND `e`.`Key2` = `s`.`CompositeKeySkipSharedKey2` AND `e`.`Key3` = `s`.`CompositeKeySkipSharedKey3`
ORDER BY `e`.`Key1`, `e`.`Key2`, `e`.`Key3`, `s`.`RootSkipSharedId`, `s`.`CompositeKeySkipSharedKey1`, `s`.`CompositeKeySkipSharedKey2`, `s`.`CompositeKeySkipSharedKey3`
""");
    }

    public override async Task Include_skip_navigation_then_reference(bool async)
    {
        await base.Include_skip_navigation_then_reference(async);

        AssertSql(
            """
SELECT `e`.`Id`, `e`.`CollectionInverseId`, `e`.`ExtraId`, `e`.`Name`, `e`.`ReferenceInverseId`, `s`.`Id`, `s`.`Name`, `s`.`Id0`, `s`.`CollectionInverseId`, `s`.`ExtraId`, `s`.`Name0`, `s`.`ReferenceInverseId`, `s`.`OneId`, `s`.`TwoId`
FROM `EntityTwos` AS `e`
LEFT JOIN (
    SELECT `e0`.`Id`, `e0`.`Name`, `e1`.`Id` AS `Id0`, `e1`.`CollectionInverseId`, `e1`.`ExtraId`, `e1`.`Name` AS `Name0`, `e1`.`ReferenceInverseId`, `j`.`OneId`, `j`.`TwoId`
    FROM (`JoinOneToTwo` AS `j`
    INNER JOIN `EntityOnes` AS `e0` ON `j`.`OneId` = `e0`.`Id`)
    LEFT JOIN `EntityTwos` AS `e1` ON `e0`.`Id` = `e1`.`ReferenceInverseId`
) AS `s` ON `e`.`Id` = `s`.`TwoId`
ORDER BY `e`.`Id`, `s`.`OneId`, `s`.`TwoId`, `s`.`Id`
""");
    }

    public override async Task Include_skip_navigation_then_include_skip_navigation(bool async)
    {
        await base.Include_skip_navigation_then_include_skip_navigation(async);

        AssertSql(
            """
SELECT `e`.`Key1`, `e`.`Key2`, `e`.`Key3`, `e`.`Name`, `s0`.`Id`, `s0`.`Discriminator`, `s0`.`Name`, `s0`.`Number`, `s0`.`IsGreen`, `s0`.`LeafId`, `s0`.`CompositeId1`, `s0`.`CompositeId2`, `s0`.`CompositeId3`, `s0`.`Id0`, `s0`.`Name0`, `s0`.`EntityBranchId`, `s0`.`EntityOneId`
FROM `EntityCompositeKeys` AS `e`
LEFT JOIN (
    SELECT `e1`.`Id`, `e1`.`Discriminator`, `e1`.`Name`, `e1`.`Number`, `e1`.`IsGreen`, `j`.`LeafId`, `j`.`CompositeId1`, `j`.`CompositeId2`, `j`.`CompositeId3`, `s`.`Id` AS `Id0`, `s`.`Name` AS `Name0`, `s`.`EntityBranchId`, `s`.`EntityOneId`
    FROM (`JoinCompositeKeyToLeaf` AS `j`
    INNER JOIN (
        SELECT `e0`.`Id`, `e0`.`Discriminator`, `e0`.`Name`, `e0`.`Number`, `e0`.`IsGreen`
        FROM `EntityRoots` AS `e0`
        WHERE `e0`.`Discriminator` = 'EntityLeaf'
    ) AS `e1` ON `j`.`LeafId` = `e1`.`Id`)
    LEFT JOIN (
        SELECT `e2`.`Id`, `e2`.`Name`, `j0`.`EntityBranchId`, `j0`.`EntityOneId`
        FROM `JoinOneToBranch` AS `j0`
        INNER JOIN `EntityOnes` AS `e2` ON `j0`.`EntityOneId` = `e2`.`Id`
    ) AS `s` ON `e1`.`Id` = `s`.`EntityBranchId`
) AS `s0` ON `e`.`Key1` = `s0`.`CompositeId1` AND `e`.`Key2` = `s0`.`CompositeId2` AND `e`.`Key3` = `s0`.`CompositeId3`
ORDER BY `e`.`Key1`, `e`.`Key2`, `e`.`Key3`, `s0`.`LeafId`, `s0`.`CompositeId1`, `s0`.`CompositeId2`, `s0`.`CompositeId3`, `s0`.`Id`, `s0`.`EntityBranchId`, `s0`.`EntityOneId`
""");
    }

    public override async Task Include_skip_navigation_then_include_reference_and_skip_navigation(bool async)
    {
        await base.Include_skip_navigation_then_include_reference_and_skip_navigation(async);

        AssertSql(
            """
SELECT `e`.`Id`, `e`.`CollectionInverseId`, `e`.`Name`, `e`.`ReferenceInverseId`, `s0`.`Id`, `s0`.`Name`, `s0`.`Id0`, `s0`.`CollectionInverseId`, `s0`.`ExtraId`, `s0`.`Name0`, `s0`.`ReferenceInverseId`, `s0`.`OneId`, `s0`.`ThreeId`, `s0`.`Id1`, `s0`.`Name1`, `s0`.`LeftId`, `s0`.`RightId`
FROM `EntityThrees` AS `e`
LEFT JOIN (
    SELECT `e0`.`Id`, `e0`.`Name`, `e1`.`Id` AS `Id0`, `e1`.`CollectionInverseId`, `e1`.`ExtraId`, `e1`.`Name` AS `Name0`, `e1`.`ReferenceInverseId`, `j`.`OneId`, `j`.`ThreeId`, `s`.`Id` AS `Id1`, `s`.`Name` AS `Name1`, `s`.`LeftId`, `s`.`RightId`
    FROM ((`JoinOneToThreePayloadFull` AS `j`
    INNER JOIN `EntityOnes` AS `e0` ON `j`.`OneId` = `e0`.`Id`)
    LEFT JOIN `EntityTwos` AS `e1` ON `e0`.`Id` = `e1`.`ReferenceInverseId`)
    LEFT JOIN (
        SELECT `e2`.`Id`, `e2`.`Name`, `j0`.`LeftId`, `j0`.`RightId`
        FROM `JoinOneSelfPayload` AS `j0`
        INNER JOIN `EntityOnes` AS `e2` ON `j0`.`RightId` = `e2`.`Id`
    ) AS `s` ON `e0`.`Id` = `s`.`LeftId`
) AS `s0` ON `e`.`Id` = `s0`.`ThreeId`
ORDER BY `e`.`Id`, `s0`.`OneId`, `s0`.`ThreeId`, `s0`.`Id`, `s0`.`Id0`, `s0`.`LeftId`, `s0`.`RightId`
""");
    }

    public override async Task Include_skip_navigation_and_reference(bool async)
    {
        await base.Include_skip_navigation_and_reference(async);

        AssertSql(
            """
SELECT `e`.`Id`, `e`.`CollectionInverseId`, `e`.`ExtraId`, `e`.`Name`, `e`.`ReferenceInverseId`, `e0`.`Id`, `s`.`Id`, `s`.`Name`, `s`.`OneSkipSharedId`, `s`.`TwoSkipSharedId`, `e0`.`CollectionInverseId`, `e0`.`Name`, `e0`.`ReferenceInverseId`
FROM (`EntityTwos` AS `e`
LEFT JOIN `EntityThrees` AS `e0` ON `e`.`Id` = `e0`.`ReferenceInverseId`)
LEFT JOIN (
    SELECT `e2`.`Id`, `e2`.`Name`, `e1`.`OneSkipSharedId`, `e1`.`TwoSkipSharedId`
    FROM `EntityOneEntityTwo` AS `e1`
    INNER JOIN `EntityOnes` AS `e2` ON `e1`.`OneSkipSharedId` = `e2`.`Id`
) AS `s` ON `e`.`Id` = `s`.`TwoSkipSharedId`
ORDER BY `e`.`Id`, `e0`.`Id`, `s`.`OneSkipSharedId`, `s`.`TwoSkipSharedId`
""");
    }

    public override async Task Filtered_include_skip_navigation_where(bool async)
    {
        await base.Filtered_include_skip_navigation_where(async);

        AssertSql(
            """
SELECT `e`.`Id`, `e`.`CollectionInverseId`, `e`.`Name`, `e`.`ReferenceInverseId`, `s`.`Id`, `s`.`Name`, `s`.`OneId`, `s`.`ThreeId`
FROM `EntityThrees` AS `e`
LEFT JOIN (
    SELECT `e0`.`Id`, `e0`.`Name`, `j`.`OneId`, `j`.`ThreeId`
    FROM `JoinOneToThreePayloadFullShared` AS `j`
    INNER JOIN `EntityOnes` AS `e0` ON `j`.`OneId` = `e0`.`Id`
    WHERE `e0`.`Id` < 10
) AS `s` ON `e`.`Id` = `s`.`ThreeId`
ORDER BY `e`.`Id`, `s`.`OneId`, `s`.`ThreeId`
""");
    }

    public override async Task Filtered_include_skip_navigation_order_by(bool async)
    {
        await base.Filtered_include_skip_navigation_order_by(async);

        AssertSql(
            """
SELECT `e`.`Id`, `e`.`CollectionInverseId`, `e`.`Name`, `e`.`ReferenceInverseId`, `s`.`Id`, `s`.`CollectionInverseId`, `s`.`ExtraId`, `s`.`Name`, `s`.`ReferenceInverseId`, `s`.`ThreeId`, `s`.`TwoId`
FROM `EntityThrees` AS `e`
LEFT JOIN (
    SELECT `e0`.`Id`, `e0`.`CollectionInverseId`, `e0`.`ExtraId`, `e0`.`Name`, `e0`.`ReferenceInverseId`, `j`.`ThreeId`, `j`.`TwoId`
    FROM `JoinTwoToThree` AS `j`
    INNER JOIN `EntityTwos` AS `e0` ON `j`.`TwoId` = `e0`.`Id`
) AS `s` ON `e`.`Id` = `s`.`ThreeId`
ORDER BY `e`.`Id`, `s`.`Id`, `s`.`ThreeId`
""");
    }

    public override async Task Filtered_include_skip_navigation_order_by_skip(bool async)
    {
        await base.Filtered_include_skip_navigation_order_by_skip(async);

        AssertSql(
"""
SELECT [e].[Id], [e].[CollectionInverseId], [e].[ExtraId], [e].[Name], [e].[ReferenceInverseId], [t0].[Id], [t0].[CollectionInverseId], [t0].[ExtraId], [t0].[Name], [t0].[ReferenceInverseId], [t0].[SelfSkipSharedLeftId], [t0].[SelfSkipSharedRightId]
FROM [EntityTwos] AS [e]
LEFT JOIN (
    SELECT [t].[Id], [t].[CollectionInverseId], [t].[ExtraId], [t].[Name], [t].[ReferenceInverseId], [t].[SelfSkipSharedLeftId], [t].[SelfSkipSharedRightId]
    FROM (
        SELECT [e1].[Id], [e1].[CollectionInverseId], [e1].[ExtraId], [e1].[Name], [e1].[ReferenceInverseId], [e0].[SelfSkipSharedLeftId], [e0].[SelfSkipSharedRightId], ROW_NUMBER() OVER(PARTITION BY [e0].[SelfSkipSharedLeftId] ORDER BY [e1].[Id]) AS [row]
        FROM [EntityTwoEntityTwo] AS [e0]
        INNER JOIN [EntityTwos] AS [e1] ON [e0].[SelfSkipSharedRightId] = [e1].[Id]
    ) AS [t]
    WHERE 2 < [t].[row]
) AS [t0] ON [e].[Id] = [t0].[SelfSkipSharedLeftId]
ORDER BY [e].[Id], [t0].[SelfSkipSharedLeftId], [t0].[Id]
""");
    }

    public override async Task Filtered_include_skip_navigation_order_by_take(bool async)
    {
        await base.Filtered_include_skip_navigation_order_by_take(async);

        AssertSql(
"""
SELECT [e].[Key1], [e].[Key2], [e].[Key3], [e].[Name], [t0].[Id], [t0].[CollectionInverseId], [t0].[ExtraId], [t0].[Name], [t0].[ReferenceInverseId], [t0].[TwoSkipSharedId], [t0].[CompositeKeySkipSharedKey1], [t0].[CompositeKeySkipSharedKey2], [t0].[CompositeKeySkipSharedKey3]
FROM [EntityCompositeKeys] AS [e]
LEFT JOIN (
    SELECT [t].[Id], [t].[CollectionInverseId], [t].[ExtraId], [t].[Name], [t].[ReferenceInverseId], [t].[TwoSkipSharedId], [t].[CompositeKeySkipSharedKey1], [t].[CompositeKeySkipSharedKey2], [t].[CompositeKeySkipSharedKey3]
    FROM (
        SELECT [e1].[Id], [e1].[CollectionInverseId], [e1].[ExtraId], [e1].[Name], [e1].[ReferenceInverseId], [e0].[TwoSkipSharedId], [e0].[CompositeKeySkipSharedKey1], [e0].[CompositeKeySkipSharedKey2], [e0].[CompositeKeySkipSharedKey3], ROW_NUMBER() OVER(PARTITION BY [e0].[CompositeKeySkipSharedKey1], [e0].[CompositeKeySkipSharedKey2], [e0].[CompositeKeySkipSharedKey3] ORDER BY [e1].[Id]) AS [row]
        FROM [EntityCompositeKeyEntityTwo] AS [e0]
        INNER JOIN [EntityTwos] AS [e1] ON [e0].[TwoSkipSharedId] = [e1].[Id]
    ) AS [t]
    WHERE [t].[row] <= 2
) AS [t0] ON [e].[Key1] = [t0].[CompositeKeySkipSharedKey1] AND [e].[Key2] = [t0].[CompositeKeySkipSharedKey2] AND [e].[Key3] = [t0].[CompositeKeySkipSharedKey3]
ORDER BY [e].[Key1], [e].[Key2], [e].[Key3], [t0].[CompositeKeySkipSharedKey1], [t0].[CompositeKeySkipSharedKey2], [t0].[CompositeKeySkipSharedKey3], [t0].[Id]
""");
    }

    public override async Task Filtered_include_skip_navigation_order_by_skip_take(bool async)
    {
        await base.Filtered_include_skip_navigation_order_by_skip_take(async);

        AssertSql(
"""
SELECT [e].[Key1], [e].[Key2], [e].[Key3], [e].[Name], [t0].[Id], [t0].[CollectionInverseId], [t0].[Name], [t0].[ReferenceInverseId], [t0].[Id0]
FROM [EntityCompositeKeys] AS [e]
LEFT JOIN (
    SELECT [t].[Id], [t].[CollectionInverseId], [t].[Name], [t].[ReferenceInverseId], [t].[Id0], [t].[CompositeId1], [t].[CompositeId2], [t].[CompositeId3]
    FROM (
        SELECT [e0].[Id], [e0].[CollectionInverseId], [e0].[Name], [e0].[ReferenceInverseId], [j].[Id] AS [Id0], [j].[CompositeId1], [j].[CompositeId2], [j].[CompositeId3], ROW_NUMBER() OVER(PARTITION BY [j].[CompositeId1], [j].[CompositeId2], [j].[CompositeId3] ORDER BY [e0].[Id]) AS [row]
        FROM [JoinThreeToCompositeKeyFull] AS [j]
        INNER JOIN [EntityThrees] AS [e0] ON [j].[ThreeId] = [e0].[Id]
    ) AS [t]
    WHERE 1 < [t].[row] AND [t].[row] <= 3
) AS [t0] ON [e].[Key1] = [t0].[CompositeId1] AND [e].[Key2] = [t0].[CompositeId2] AND [e].[Key3] = [t0].[CompositeId3]
ORDER BY [e].[Key1], [e].[Key2], [e].[Key3], [t0].[CompositeId1], [t0].[CompositeId2], [t0].[CompositeId3], [t0].[Id]
""");
    }

    public override async Task Filtered_then_include_skip_navigation_where(bool async)
    {
        await base.Filtered_then_include_skip_navigation_where(async);

        AssertSql(
            """
SELECT `e`.`Id`, `e`.`Discriminator`, `e`.`Name`, `e`.`Number`, `e`.`Slumber`, `e`.`IsGreen`, `e`.`IsBrown`, `s0`.`Id`, `s0`.`CollectionInverseId`, `s0`.`Name`, `s0`.`ReferenceInverseId`, `s0`.`RootSkipSharedId`, `s0`.`ThreeSkipSharedId`, `s0`.`Id0`, `s0`.`Name0`, `s0`.`OneId`, `s0`.`ThreeId`
FROM `EntityRoots` AS `e`
LEFT JOIN (
    SELECT `e1`.`Id`, `e1`.`CollectionInverseId`, `e1`.`Name`, `e1`.`ReferenceInverseId`, `e0`.`RootSkipSharedId`, `e0`.`ThreeSkipSharedId`, `s`.`Id` AS `Id0`, `s`.`Name` AS `Name0`, `s`.`OneId`, `s`.`ThreeId`
    FROM (`EntityRootEntityThree` AS `e0`
    INNER JOIN `EntityThrees` AS `e1` ON `e0`.`ThreeSkipSharedId` = `e1`.`Id`)
    LEFT JOIN (
        SELECT `e2`.`Id`, `e2`.`Name`, `j`.`OneId`, `j`.`ThreeId`
        FROM `JoinOneToThreePayloadFullShared` AS `j`
        INNER JOIN `EntityOnes` AS `e2` ON `j`.`OneId` = `e2`.`Id`
        WHERE `e2`.`Id` < 10
    ) AS `s` ON `e1`.`Id` = `s`.`ThreeId`
) AS `s0` ON `e`.`Id` = `s0`.`RootSkipSharedId`
ORDER BY `e`.`Id`, `s0`.`RootSkipSharedId`, `s0`.`ThreeSkipSharedId`, `s0`.`Id`, `s0`.`OneId`, `s0`.`ThreeId`
""");
    }

    public override async Task Filtered_then_include_skip_navigation_order_by_skip_take(bool async)
    {
        await base.Filtered_then_include_skip_navigation_order_by_skip_take(async);

        AssertSql(
"""
SELECT [e].[Id], [e].[Discriminator], [e].[Name], [e].[Number], [e].[Slumber], [e].[IsGreen], [e].[IsBrown], [t1].[Key1], [t1].[Key2], [t1].[Key3], [t1].[Name], [t1].[RootSkipSharedId], [t1].[CompositeKeySkipSharedKey1], [t1].[CompositeKeySkipSharedKey2], [t1].[CompositeKeySkipSharedKey3], [t1].[Id], [t1].[CollectionInverseId], [t1].[Name0], [t1].[ReferenceInverseId], [t1].[Id0]
FROM [EntityRoots] AS [e]
LEFT JOIN (
    SELECT [e1].[Key1], [e1].[Key2], [e1].[Key3], [e1].[Name], [e0].[RootSkipSharedId], [e0].[CompositeKeySkipSharedKey1], [e0].[CompositeKeySkipSharedKey2], [e0].[CompositeKeySkipSharedKey3], [t0].[Id], [t0].[CollectionInverseId], [t0].[Name] AS [Name0], [t0].[ReferenceInverseId], [t0].[Id0], [t0].[CompositeId1], [t0].[CompositeId2], [t0].[CompositeId3]
    FROM [EntityCompositeKeyEntityRoot] AS [e0]
    INNER JOIN [EntityCompositeKeys] AS [e1] ON [e0].[CompositeKeySkipSharedKey1] = [e1].[Key1] AND [e0].[CompositeKeySkipSharedKey2] = [e1].[Key2] AND [e0].[CompositeKeySkipSharedKey3] = [e1].[Key3]
    LEFT JOIN (
        SELECT [t].[Id], [t].[CollectionInverseId], [t].[Name], [t].[ReferenceInverseId], [t].[Id0], [t].[CompositeId1], [t].[CompositeId2], [t].[CompositeId3]
        FROM (
            SELECT [e2].[Id], [e2].[CollectionInverseId], [e2].[Name], [e2].[ReferenceInverseId], [j].[Id] AS [Id0], [j].[CompositeId1], [j].[CompositeId2], [j].[CompositeId3], ROW_NUMBER() OVER(PARTITION BY [j].[CompositeId1], [j].[CompositeId2], [j].[CompositeId3] ORDER BY [e2].[Id]) AS [row]
            FROM [JoinThreeToCompositeKeyFull] AS [j]
            INNER JOIN [EntityThrees] AS [e2] ON [j].[ThreeId] = [e2].[Id]
        ) AS [t]
        WHERE 1 < [t].[row] AND [t].[row] <= 3
    ) AS [t0] ON [e1].[Key1] = [t0].[CompositeId1] AND [e1].[Key2] = [t0].[CompositeId2] AND [e1].[Key3] = [t0].[CompositeId3]
) AS [t1] ON [e].[Id] = [t1].[RootSkipSharedId]
ORDER BY [e].[Id], [t1].[RootSkipSharedId], [t1].[CompositeKeySkipSharedKey1], [t1].[CompositeKeySkipSharedKey2], [t1].[CompositeKeySkipSharedKey3], [t1].[Key1], [t1].[Key2], [t1].[Key3], [t1].[CompositeId1], [t1].[CompositeId2], [t1].[CompositeId3], [t1].[Id]
""");
    }

    public override async Task Filtered_include_skip_navigation_where_then_include_skip_navigation(bool async)
    {
        await base.Filtered_include_skip_navigation_where_then_include_skip_navigation(async);

        AssertSql(
            """
SELECT `e`.`Id`, `e`.`Discriminator`, `e`.`Name`, `e`.`Number`, `e`.`IsGreen`, `s0`.`Key1`, `s0`.`Key2`, `s0`.`Key3`, `s0`.`Name`, `s0`.`LeafId`, `s0`.`CompositeId1`, `s0`.`CompositeId2`, `s0`.`CompositeId3`, `s0`.`Id`, `s0`.`CollectionInverseId`, `s0`.`ExtraId`, `s0`.`Name0`, `s0`.`ReferenceInverseId`, `s0`.`TwoSkipSharedId`, `s0`.`CompositeKeySkipSharedKey1`, `s0`.`CompositeKeySkipSharedKey2`, `s0`.`CompositeKeySkipSharedKey3`
FROM `EntityRoots` AS `e`
LEFT JOIN (
    SELECT `e0`.`Key1`, `e0`.`Key2`, `e0`.`Key3`, `e0`.`Name`, `j`.`LeafId`, `j`.`CompositeId1`, `j`.`CompositeId2`, `j`.`CompositeId3`, `s`.`Id`, `s`.`CollectionInverseId`, `s`.`ExtraId`, `s`.`Name` AS `Name0`, `s`.`ReferenceInverseId`, `s`.`TwoSkipSharedId`, `s`.`CompositeKeySkipSharedKey1`, `s`.`CompositeKeySkipSharedKey2`, `s`.`CompositeKeySkipSharedKey3`
    FROM (`JoinCompositeKeyToLeaf` AS `j`
    INNER JOIN `EntityCompositeKeys` AS `e0` ON `j`.`CompositeId1` = `e0`.`Key1` AND `j`.`CompositeId2` = `e0`.`Key2` AND `j`.`CompositeId3` = `e0`.`Key3`)
    LEFT JOIN (
        SELECT `e2`.`Id`, `e2`.`CollectionInverseId`, `e2`.`ExtraId`, `e2`.`Name`, `e2`.`ReferenceInverseId`, `e1`.`TwoSkipSharedId`, `e1`.`CompositeKeySkipSharedKey1`, `e1`.`CompositeKeySkipSharedKey2`, `e1`.`CompositeKeySkipSharedKey3`
        FROM `EntityCompositeKeyEntityTwo` AS `e1`
        INNER JOIN `EntityTwos` AS `e2` ON `e1`.`TwoSkipSharedId` = `e2`.`Id`
    ) AS `s` ON `e0`.`Key1` = `s`.`CompositeKeySkipSharedKey1` AND `e0`.`Key2` = `s`.`CompositeKeySkipSharedKey2` AND `e0`.`Key3` = `s`.`CompositeKeySkipSharedKey3`
    WHERE `e0`.`Key1` < 5
) AS `s0` ON `e`.`Id` = `s0`.`LeafId`
WHERE `e`.`Discriminator` = 'EntityLeaf'
ORDER BY `e`.`Id`, `s0`.`LeafId`, `s0`.`CompositeId1`, `s0`.`CompositeId2`, `s0`.`CompositeId3`, `s0`.`Key1`, `s0`.`Key2`, `s0`.`Key3`, `s0`.`TwoSkipSharedId`, `s0`.`CompositeKeySkipSharedKey1`, `s0`.`CompositeKeySkipSharedKey2`, `s0`.`CompositeKeySkipSharedKey3`
""");
    }

    public override async Task Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(bool async)
    {
        await base.Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where(async);

        AssertSql(
"""
SELECT [e].[Id], [e].[Name], [t1].[Id], [t1].[CollectionInverseId], [t1].[ExtraId], [t1].[Name], [t1].[ReferenceInverseId], [t1].[OneId], [t1].[TwoId], [t1].[Id0], [t1].[CollectionInverseId0], [t1].[Name0], [t1].[ReferenceInverseId0], [t1].[ThreeId], [t1].[TwoId0]
FROM [EntityOnes] AS [e]
OUTER APPLY (
    SELECT [t].[Id], [t].[CollectionInverseId], [t].[ExtraId], [t].[Name], [t].[ReferenceInverseId], [t].[OneId], [t].[TwoId], [t0].[Id] AS [Id0], [t0].[CollectionInverseId] AS [CollectionInverseId0], [t0].[Name] AS [Name0], [t0].[ReferenceInverseId] AS [ReferenceInverseId0], [t0].[ThreeId], [t0].[TwoId] AS [TwoId0]
    FROM (
        SELECT [e0].[Id], [e0].[CollectionInverseId], [e0].[ExtraId], [e0].[Name], [e0].[ReferenceInverseId], [j].[OneId], [j].[TwoId]
        FROM [JoinOneToTwo] AS [j]
        INNER JOIN [EntityTwos] AS [e0] ON [j].[TwoId] = [e0].[Id]
        WHERE [e].[Id] = [j].[OneId]
        ORDER BY [e0].[Id]
        OFFSET 1 ROWS FETCH NEXT 2 ROWS ONLY
    ) AS [t]
    LEFT JOIN (
        SELECT [e1].[Id], [e1].[CollectionInverseId], [e1].[Name], [e1].[ReferenceInverseId], [j0].[ThreeId], [j0].[TwoId]
        FROM [JoinTwoToThree] AS [j0]
        INNER JOIN [EntityThrees] AS [e1] ON [j0].[ThreeId] = [e1].[Id]
        WHERE [e1].[Id] < 10
    ) AS [t0] ON [t].[Id] = [t0].[TwoId]
) AS [t1]
ORDER BY [e].[Id], [t1].[Id], [t1].[OneId], [t1].[TwoId], [t1].[ThreeId], [t1].[TwoId0]
""");
    }

    public override async Task Filtered_include_skip_navigation_where_then_include_skip_navigation_order_by_skip_take(bool async)
    {
        await base.Filtered_include_skip_navigation_where_then_include_skip_navigation_order_by_skip_take(async);

        AssertSql(
"""
SELECT [e].[Id], [e].[Name], [t1].[Id], [t1].[CollectionInverseId], [t1].[ExtraId], [t1].[Name], [t1].[ReferenceInverseId], [t1].[OneId], [t1].[TwoId], [t1].[Id0], [t1].[CollectionInverseId0], [t1].[Name0], [t1].[ReferenceInverseId0], [t1].[ThreeId], [t1].[TwoId0]
FROM [EntityOnes] AS [e]
LEFT JOIN (
    SELECT [e0].[Id], [e0].[CollectionInverseId], [e0].[ExtraId], [e0].[Name], [e0].[ReferenceInverseId], [j].[OneId], [j].[TwoId], [t0].[Id] AS [Id0], [t0].[CollectionInverseId] AS [CollectionInverseId0], [t0].[Name] AS [Name0], [t0].[ReferenceInverseId] AS [ReferenceInverseId0], [t0].[ThreeId], [t0].[TwoId] AS [TwoId0]
    FROM [JoinOneToTwo] AS [j]
    INNER JOIN [EntityTwos] AS [e0] ON [j].[TwoId] = [e0].[Id]
    LEFT JOIN (
        SELECT [t].[Id], [t].[CollectionInverseId], [t].[Name], [t].[ReferenceInverseId], [t].[ThreeId], [t].[TwoId]
        FROM (
            SELECT [e1].[Id], [e1].[CollectionInverseId], [e1].[Name], [e1].[ReferenceInverseId], [j0].[ThreeId], [j0].[TwoId], ROW_NUMBER() OVER(PARTITION BY [j0].[TwoId] ORDER BY [e1].[Id]) AS [row]
            FROM [JoinTwoToThree] AS [j0]
            INNER JOIN [EntityThrees] AS [e1] ON [j0].[ThreeId] = [e1].[Id]
        ) AS [t]
        WHERE 1 < [t].[row] AND [t].[row] <= 3
    ) AS [t0] ON [e0].[Id] = [t0].[TwoId]
    WHERE [e0].[Id] < 10
) AS [t1] ON [e].[Id] = [t1].[OneId]
ORDER BY [e].[Id], [t1].[OneId], [t1].[TwoId], [t1].[Id], [t1].[TwoId0], [t1].[Id0]
""");
    }

    public override async Task Filter_include_on_skip_navigation_combined(bool async)
    {
        await base.Filter_include_on_skip_navigation_combined(async);

        AssertSql(
            """
SELECT `e`.`Id`, `e`.`CollectionInverseId`, `e`.`ExtraId`, `e`.`Name`, `e`.`ReferenceInverseId`, `s`.`Id`, `s`.`Name`, `s`.`Id0`, `s`.`CollectionInverseId`, `s`.`ExtraId`, `s`.`Name0`, `s`.`ReferenceInverseId`, `s`.`OneId`, `s`.`TwoId`, `s`.`Id1`, `s`.`CollectionInverseId0`, `s`.`ExtraId0`, `s`.`Name1`, `s`.`ReferenceInverseId0`
FROM `EntityTwos` AS `e`
LEFT JOIN (
    SELECT `e0`.`Id`, `e0`.`Name`, `e1`.`Id` AS `Id0`, `e1`.`CollectionInverseId`, `e1`.`ExtraId`, `e1`.`Name` AS `Name0`, `e1`.`ReferenceInverseId`, `j`.`OneId`, `j`.`TwoId`, `e2`.`Id` AS `Id1`, `e2`.`CollectionInverseId` AS `CollectionInverseId0`, `e2`.`ExtraId` AS `ExtraId0`, `e2`.`Name` AS `Name1`, `e2`.`ReferenceInverseId` AS `ReferenceInverseId0`
    FROM ((`JoinOneToTwo` AS `j`
    INNER JOIN `EntityOnes` AS `e0` ON `j`.`OneId` = `e0`.`Id`)
    LEFT JOIN `EntityTwos` AS `e1` ON `e0`.`Id` = `e1`.`ReferenceInverseId`)
    LEFT JOIN `EntityTwos` AS `e2` ON `e0`.`Id` = `e2`.`CollectionInverseId`
    WHERE `e0`.`Id` < 10
) AS `s` ON `e`.`Id` = `s`.`TwoId`
ORDER BY `e`.`Id`, `s`.`OneId`, `s`.`TwoId`, `s`.`Id`, `s`.`Id0`
""");
    }

    public override async Task Filter_include_on_skip_navigation_combined_with_filtered_then_includes(bool async)
    {
        await base.Filter_include_on_skip_navigation_combined_with_filtered_then_includes(async);

        AssertSql(
"""
SELECT [e].[Id], [e].[CollectionInverseId], [e].[Name], [e].[ReferenceInverseId], [t3].[Id], [t3].[Name], [t3].[OneId], [t3].[ThreeId], [t3].[Id0], [t3].[CollectionInverseId], [t3].[ExtraId], [t3].[Name0], [t3].[ReferenceInverseId], [t3].[OneId0], [t3].[TwoId], [t3].[Id1], [t3].[Discriminator], [t3].[Name1], [t3].[Number], [t3].[IsGreen], [t3].[EntityBranchId], [t3].[EntityOneId]
FROM [EntityThrees] AS [e]
LEFT JOIN (
    SELECT [e0].[Id], [e0].[Name], [j].[OneId], [j].[ThreeId], [t0].[Id] AS [Id0], [t0].[CollectionInverseId], [t0].[ExtraId], [t0].[Name] AS [Name0], [t0].[ReferenceInverseId], [t0].[OneId] AS [OneId0], [t0].[TwoId], [t1].[Id] AS [Id1], [t1].[Discriminator], [t1].[Name] AS [Name1], [t1].[Number], [t1].[IsGreen], [t1].[EntityBranchId], [t1].[EntityOneId]
    FROM [JoinOneToThreePayloadFull] AS [j]
    INNER JOIN [EntityOnes] AS [e0] ON [j].[OneId] = [e0].[Id]
    LEFT JOIN (
        SELECT [t].[Id], [t].[CollectionInverseId], [t].[ExtraId], [t].[Name], [t].[ReferenceInverseId], [t].[OneId], [t].[TwoId]
        FROM (
            SELECT [e1].[Id], [e1].[CollectionInverseId], [e1].[ExtraId], [e1].[Name], [e1].[ReferenceInverseId], [j0].[OneId], [j0].[TwoId], ROW_NUMBER() OVER(PARTITION BY [j0].[OneId] ORDER BY [e1].[Id]) AS [row]
            FROM [JoinOneToTwo] AS [j0]
            INNER JOIN [EntityTwos] AS [e1] ON [j0].[TwoId] = [e1].[Id]
        ) AS [t]
        WHERE 1 < [t].[row] AND [t].[row] <= 3
    ) AS [t0] ON [e0].[Id] = [t0].[OneId]
    LEFT JOIN (
        SELECT [t2].[Id], [t2].[Discriminator], [t2].[Name], [t2].[Number], [t2].[IsGreen], [j1].[EntityBranchId], [j1].[EntityOneId]
        FROM [JoinOneToBranch] AS [j1]
        INNER JOIN (
            SELECT [e2].[Id], [e2].[Discriminator], [e2].[Name], [e2].[Number], [e2].[IsGreen]
            FROM [EntityRoots] AS [e2]
            WHERE [e2].[Discriminator] IN (N'EntityBranch', N'EntityLeaf')
        ) AS [t2] ON [j1].[EntityBranchId] = [t2].[Id]
        WHERE [t2].[Id] < 20
    ) AS [t1] ON [e0].[Id] = [t1].[EntityOneId]
    WHERE [e0].[Id] < 10
) AS [t3] ON [e].[Id] = [t3].[ThreeId]
ORDER BY [e].[Id], [t3].[OneId], [t3].[ThreeId], [t3].[Id], [t3].[OneId0], [t3].[Id0], [t3].[TwoId], [t3].[EntityBranchId], [t3].[EntityOneId]
""");
    }

    public override async Task Filtered_include_on_skip_navigation_then_filtered_include_on_navigation(bool async)
    {
        await base.Filtered_include_on_skip_navigation_then_filtered_include_on_navigation(async);

        AssertSql(
            """
SELECT `e`.`Id`, `e`.`CollectionInverseId`, `e`.`Name`, `e`.`ReferenceInverseId`, `s`.`Id`, `s`.`Name`, `s`.`OneId`, `s`.`ThreeId`, `s`.`Id0`, `s`.`CollectionInverseId`, `s`.`ExtraId`, `s`.`Name0`, `s`.`ReferenceInverseId`
FROM `EntityThrees` AS `e`
LEFT JOIN (
    SELECT `e0`.`Id`, `e0`.`Name`, `j`.`OneId`, `j`.`ThreeId`, `e2`.`Id` AS `Id0`, `e2`.`CollectionInverseId`, `e2`.`ExtraId`, `e2`.`Name` AS `Name0`, `e2`.`ReferenceInverseId`
    FROM (`JoinOneToThreePayloadFull` AS `j`
    INNER JOIN `EntityOnes` AS `e0` ON `j`.`OneId` = `e0`.`Id`)
    LEFT JOIN (
        SELECT `e1`.`Id`, `e1`.`CollectionInverseId`, `e1`.`ExtraId`, `e1`.`Name`, `e1`.`ReferenceInverseId`
        FROM `EntityTwos` AS `e1`
        WHERE `e1`.`Id` < 5
    ) AS `e2` ON `e0`.`Id` = `e2`.`CollectionInverseId`
    WHERE `e0`.`Id` > 15
) AS `s` ON `e`.`Id` = `s`.`ThreeId`
ORDER BY `e`.`Id`, `s`.`OneId`, `s`.`ThreeId`, `s`.`Id`
""");
    }

    public override async Task Filtered_include_on_navigation_then_filtered_include_on_skip_navigation(bool async)
    {
        await base.Filtered_include_on_navigation_then_filtered_include_on_skip_navigation(async);

        AssertSql(
            """
SELECT `e`.`Id`, `e`.`Name`, `s0`.`Id`, `s0`.`CollectionInverseId`, `s0`.`ExtraId`, `s0`.`Name`, `s0`.`ReferenceInverseId`, `s0`.`Id0`, `s0`.`CollectionInverseId0`, `s0`.`Name0`, `s0`.`ReferenceInverseId0`, `s0`.`ThreeId`, `s0`.`TwoId`
FROM `EntityOnes` AS `e`
LEFT JOIN (
    SELECT `e0`.`Id`, `e0`.`CollectionInverseId`, `e0`.`ExtraId`, `e0`.`Name`, `e0`.`ReferenceInverseId`, `s`.`Id` AS `Id0`, `s`.`CollectionInverseId` AS `CollectionInverseId0`, `s`.`Name` AS `Name0`, `s`.`ReferenceInverseId` AS `ReferenceInverseId0`, `s`.`ThreeId`, `s`.`TwoId`
    FROM `EntityTwos` AS `e0`
    LEFT JOIN (
        SELECT `e1`.`Id`, `e1`.`CollectionInverseId`, `e1`.`Name`, `e1`.`ReferenceInverseId`, `j`.`ThreeId`, `j`.`TwoId`
        FROM `JoinTwoToThree` AS `j`
        INNER JOIN `EntityThrees` AS `e1` ON `j`.`ThreeId` = `e1`.`Id`
        WHERE `e1`.`Id` < 5
    ) AS `s` ON `e0`.`Id` = `s`.`TwoId`
    WHERE `e0`.`Id` > 15
) AS `s0` ON `e`.`Id` = `s0`.`CollectionInverseId`
ORDER BY `e`.`Id`, `s0`.`Id`, `s0`.`ThreeId`, `s0`.`TwoId`
""");
    }

    public override async Task Includes_accessed_via_different_path_are_merged(bool async)
    {
        await base.Includes_accessed_via_different_path_are_merged(async);

        AssertSql(" ");
    }

    public override async Task Filtered_includes_accessed_via_different_path_are_merged(bool async)
    {
        await base.Filtered_includes_accessed_via_different_path_are_merged(async);

        AssertSql(" ");
    }

    public override async Task Include_skip_navigation_split(bool async)
    {
        await base.Include_skip_navigation_split(async);

        AssertSql(
            """
SELECT `e`.`Key1`, `e`.`Key2`, `e`.`Key3`, `e`.`Name`
FROM `EntityCompositeKeys` AS `e`
ORDER BY `e`.`Key1`, `e`.`Key2`, `e`.`Key3`
""",
            //
            """
SELECT `s`.`Id`, `s`.`Discriminator`, `s`.`Name`, `s`.`Number`, `s`.`Slumber`, `s`.`IsGreen`, `s`.`IsBrown`, `e`.`Key1`, `e`.`Key2`, `e`.`Key3`
FROM `EntityCompositeKeys` AS `e`
INNER JOIN (
    SELECT `e1`.`Id`, `e1`.`Discriminator`, `e1`.`Name`, `e1`.`Number`, `e1`.`Slumber`, `e1`.`IsGreen`, `e1`.`IsBrown`, `e0`.`CompositeKeySkipSharedKey1`, `e0`.`CompositeKeySkipSharedKey2`, `e0`.`CompositeKeySkipSharedKey3`
    FROM `EntityCompositeKeyEntityRoot` AS `e0`
    INNER JOIN `EntityRoots` AS `e1` ON `e0`.`RootSkipSharedId` = `e1`.`Id`
) AS `s` ON `e`.`Key1` = `s`.`CompositeKeySkipSharedKey1` AND `e`.`Key2` = `s`.`CompositeKeySkipSharedKey2` AND `e`.`Key3` = `s`.`CompositeKeySkipSharedKey3`
ORDER BY `e`.`Key1`, `e`.`Key2`, `e`.`Key3`
""");
    }

    public override async Task Include_skip_navigation_then_reference_split(bool async)
    {
        await base.Include_skip_navigation_then_reference_split(async);

        AssertSql(
            """
SELECT `e`.`Id`, `e`.`CollectionInverseId`, `e`.`ExtraId`, `e`.`Name`, `e`.`ReferenceInverseId`
FROM `EntityTwos` AS `e`
ORDER BY `e`.`Id`
""",
            //
            """
SELECT `s`.`Id`, `s`.`Name`, `s`.`Id0`, `s`.`CollectionInverseId`, `s`.`ExtraId`, `s`.`Name0`, `s`.`ReferenceInverseId`, `e`.`Id`
FROM `EntityTwos` AS `e`
INNER JOIN (
    SELECT `e0`.`Id`, `e0`.`Name`, `e1`.`Id` AS `Id0`, `e1`.`CollectionInverseId`, `e1`.`ExtraId`, `e1`.`Name` AS `Name0`, `e1`.`ReferenceInverseId`, `j`.`TwoId`
    FROM (`JoinOneToTwo` AS `j`
    INNER JOIN `EntityOnes` AS `e0` ON `j`.`OneId` = `e0`.`Id`)
    LEFT JOIN `EntityTwos` AS `e1` ON `e0`.`Id` = `e1`.`ReferenceInverseId`
) AS `s` ON `e`.`Id` = `s`.`TwoId`
ORDER BY `e`.`Id`
""");
    }

    public override async Task Include_skip_navigation_then_include_skip_navigation_split(bool async)
    {
        await base.Include_skip_navigation_then_include_skip_navigation_split(async);

        AssertSql(
            """
SELECT `e`.`Key1`, `e`.`Key2`, `e`.`Key3`, `e`.`Name`
FROM `EntityCompositeKeys` AS `e`
ORDER BY `e`.`Key1`, `e`.`Key2`, `e`.`Key3`
""",
            //
            """
SELECT `s`.`Id`, `s`.`Discriminator`, `s`.`Name`, `s`.`Number`, `s`.`IsGreen`, `e`.`Key1`, `e`.`Key2`, `e`.`Key3`, `s`.`LeafId`, `s`.`CompositeId1`, `s`.`CompositeId2`, `s`.`CompositeId3`
FROM `EntityCompositeKeys` AS `e`
INNER JOIN (
    SELECT `e1`.`Id`, `e1`.`Discriminator`, `e1`.`Name`, `e1`.`Number`, `e1`.`IsGreen`, `j`.`LeafId`, `j`.`CompositeId1`, `j`.`CompositeId2`, `j`.`CompositeId3`
    FROM `JoinCompositeKeyToLeaf` AS `j`
    INNER JOIN (
        SELECT `e0`.`Id`, `e0`.`Discriminator`, `e0`.`Name`, `e0`.`Number`, `e0`.`IsGreen`
        FROM `EntityRoots` AS `e0`
        WHERE `e0`.`Discriminator` = 'EntityLeaf'
    ) AS `e1` ON `j`.`LeafId` = `e1`.`Id`
) AS `s` ON `e`.`Key1` = `s`.`CompositeId1` AND `e`.`Key2` = `s`.`CompositeId2` AND `e`.`Key3` = `s`.`CompositeId3`
ORDER BY `e`.`Key1`, `e`.`Key2`, `e`.`Key3`, `s`.`LeafId`, `s`.`CompositeId1`, `s`.`CompositeId2`, `s`.`CompositeId3`, `s`.`Id`
""",
            //
            """
SELECT `s0`.`Id`, `s0`.`Name`, `e`.`Key1`, `e`.`Key2`, `e`.`Key3`, `s`.`LeafId`, `s`.`CompositeId1`, `s`.`CompositeId2`, `s`.`CompositeId3`, `s`.`Id`
FROM (`EntityCompositeKeys` AS `e`
INNER JOIN (
    SELECT `e1`.`Id`, `j`.`LeafId`, `j`.`CompositeId1`, `j`.`CompositeId2`, `j`.`CompositeId3`
    FROM `JoinCompositeKeyToLeaf` AS `j`
    INNER JOIN (
        SELECT `e0`.`Id`
        FROM `EntityRoots` AS `e0`
        WHERE `e0`.`Discriminator` = 'EntityLeaf'
    ) AS `e1` ON `j`.`LeafId` = `e1`.`Id`
) AS `s` ON `e`.`Key1` = `s`.`CompositeId1` AND `e`.`Key2` = `s`.`CompositeId2` AND `e`.`Key3` = `s`.`CompositeId3`)
LEFT JOIN (
    SELECT `e2`.`Id`, `e2`.`Name`, `j0`.`EntityBranchId`
    FROM `JoinOneToBranch` AS `j0`
    INNER JOIN `EntityOnes` AS `e2` ON `j0`.`EntityOneId` = `e2`.`Id`
) AS `s0` ON `s`.`Id` = `s0`.`EntityBranchId`
WHERE `s`.`Id` IS NOT NULL AND `s0`.`EntityBranchId` IS NOT NULL
ORDER BY `e`.`Key1`, `e`.`Key2`, `e`.`Key3`, `s`.`LeafId`, `s`.`CompositeId1`, `s`.`CompositeId2`, `s`.`CompositeId3`, `s`.`Id`
""");
    }

    public override async Task Include_skip_navigation_then_include_reference_and_skip_navigation_split(bool async)
    {
        await base.Include_skip_navigation_then_include_reference_and_skip_navigation_split(async);

        AssertSql(
            """
SELECT `e`.`Id`, `e`.`CollectionInverseId`, `e`.`Name`, `e`.`ReferenceInverseId`
FROM `EntityThrees` AS `e`
ORDER BY `e`.`Id`
""",
            //
            """
SELECT `s`.`Id`, `s`.`Name`, `s`.`Id0`, `s`.`CollectionInverseId`, `s`.`ExtraId`, `s`.`Name0`, `s`.`ReferenceInverseId`, `e`.`Id`, `s`.`OneId`, `s`.`ThreeId`
FROM `EntityThrees` AS `e`
INNER JOIN (
    SELECT `e0`.`Id`, `e0`.`Name`, `e1`.`Id` AS `Id0`, `e1`.`CollectionInverseId`, `e1`.`ExtraId`, `e1`.`Name` AS `Name0`, `e1`.`ReferenceInverseId`, `j`.`OneId`, `j`.`ThreeId`
    FROM (`JoinOneToThreePayloadFull` AS `j`
    INNER JOIN `EntityOnes` AS `e0` ON `j`.`OneId` = `e0`.`Id`)
    LEFT JOIN `EntityTwos` AS `e1` ON `e0`.`Id` = `e1`.`ReferenceInverseId`
) AS `s` ON `e`.`Id` = `s`.`ThreeId`
ORDER BY `e`.`Id`, `s`.`OneId`, `s`.`ThreeId`, `s`.`Id`, `s`.`Id0`
""",
            //
            """
SELECT `s0`.`Id`, `s0`.`Name`, `e`.`Id`, `s`.`OneId`, `s`.`ThreeId`, `s`.`Id`, `s`.`Id0`
FROM (`EntityThrees` AS `e`
INNER JOIN (
    SELECT `e0`.`Id`, `e1`.`Id` AS `Id0`, `j`.`OneId`, `j`.`ThreeId`
    FROM (`JoinOneToThreePayloadFull` AS `j`
    INNER JOIN `EntityOnes` AS `e0` ON `j`.`OneId` = `e0`.`Id`)
    LEFT JOIN `EntityTwos` AS `e1` ON `e0`.`Id` = `e1`.`ReferenceInverseId`
) AS `s` ON `e`.`Id` = `s`.`ThreeId`)
LEFT JOIN (
    SELECT `e2`.`Id`, `e2`.`Name`, `j0`.`LeftId`
    FROM `JoinOneSelfPayload` AS `j0`
    INNER JOIN `EntityOnes` AS `e2` ON `j0`.`RightId` = `e2`.`Id`
) AS `s0` ON `s`.`Id` = `s0`.`LeftId`
WHERE `s`.`Id` IS NOT NULL AND `s0`.`LeftId` IS NOT NULL
ORDER BY `e`.`Id`, `s`.`OneId`, `s`.`ThreeId`, `s`.`Id`, `s`.`Id0`
""");
    }

    public override async Task Include_skip_navigation_and_reference_split(bool async)
    {
        await base.Include_skip_navigation_and_reference_split(async);

        AssertSql(
            """
SELECT `e`.`Id`, `e`.`CollectionInverseId`, `e`.`ExtraId`, `e`.`Name`, `e`.`ReferenceInverseId`, `e0`.`Id`, `e0`.`CollectionInverseId`, `e0`.`Name`, `e0`.`ReferenceInverseId`
FROM `EntityTwos` AS `e`
LEFT JOIN `EntityThrees` AS `e0` ON `e`.`Id` = `e0`.`ReferenceInverseId`
ORDER BY `e`.`Id`, `e0`.`Id`
""",
            //
            """
SELECT `s`.`Id`, `s`.`Name`, `e`.`Id`, `e0`.`Id`
FROM (`EntityTwos` AS `e`
LEFT JOIN `EntityThrees` AS `e0` ON `e`.`Id` = `e0`.`ReferenceInverseId`)
INNER JOIN (
    SELECT `e2`.`Id`, `e2`.`Name`, `e1`.`TwoSkipSharedId`
    FROM `EntityOneEntityTwo` AS `e1`
    INNER JOIN `EntityOnes` AS `e2` ON `e1`.`OneSkipSharedId` = `e2`.`Id`
) AS `s` ON `e`.`Id` = `s`.`TwoSkipSharedId`
ORDER BY `e`.`Id`, `e0`.`Id`
""");
    }

    public override async Task Filtered_include_skip_navigation_where_split(bool async)
    {
        await base.Filtered_include_skip_navigation_where_split(async);

        AssertSql(
            """
SELECT `e`.`Id`, `e`.`CollectionInverseId`, `e`.`Name`, `e`.`ReferenceInverseId`
FROM `EntityThrees` AS `e`
ORDER BY `e`.`Id`
""",
            //
            """
SELECT `s`.`Id`, `s`.`Name`, `e`.`Id`
FROM `EntityThrees` AS `e`
INNER JOIN (
    SELECT `e0`.`Id`, `e0`.`Name`, `j`.`ThreeId`
    FROM `JoinOneToThreePayloadFullShared` AS `j`
    INNER JOIN `EntityOnes` AS `e0` ON `j`.`OneId` = `e0`.`Id`
    WHERE `e0`.`Id` < 10
) AS `s` ON `e`.`Id` = `s`.`ThreeId`
ORDER BY `e`.`Id`
""");
    }

    public override async Task Filtered_include_skip_navigation_order_by_split(bool async)
    {
        await base.Filtered_include_skip_navigation_order_by_split(async);

        AssertSql(
            """
SELECT `e`.`Id`, `e`.`CollectionInverseId`, `e`.`Name`, `e`.`ReferenceInverseId`
FROM `EntityThrees` AS `e`
ORDER BY `e`.`Id`
""",
            //
            """
SELECT `s`.`Id`, `s`.`CollectionInverseId`, `s`.`ExtraId`, `s`.`Name`, `s`.`ReferenceInverseId`, `e`.`Id`
FROM `EntityThrees` AS `e`
INNER JOIN (
    SELECT `e0`.`Id`, `e0`.`CollectionInverseId`, `e0`.`ExtraId`, `e0`.`Name`, `e0`.`ReferenceInverseId`, `j`.`ThreeId`
    FROM `JoinTwoToThree` AS `j`
    INNER JOIN `EntityTwos` AS `e0` ON `j`.`TwoId` = `e0`.`Id`
) AS `s` ON `e`.`Id` = `s`.`ThreeId`
ORDER BY `e`.`Id`, `s`.`Id`
""");
    }

    public override async Task Filtered_include_skip_navigation_order_by_skip_split(bool async)
    {
        await base.Filtered_include_skip_navigation_order_by_skip_split(async);

        AssertSql(
"""
SELECT [e].[Id], [e].[CollectionInverseId], [e].[ExtraId], [e].[Name], [e].[ReferenceInverseId]
FROM [EntityTwos] AS [e]
ORDER BY [e].[Id]
""",
//
"""
SELECT [t0].[Id], [t0].[CollectionInverseId], [t0].[ExtraId], [t0].[Name], [t0].[ReferenceInverseId], [e].[Id]
FROM [EntityTwos] AS [e]
INNER JOIN (
    SELECT [t].[Id], [t].[CollectionInverseId], [t].[ExtraId], [t].[Name], [t].[ReferenceInverseId], [t].[SelfSkipSharedLeftId]
    FROM (
        SELECT [e1].[Id], [e1].[CollectionInverseId], [e1].[ExtraId], [e1].[Name], [e1].[ReferenceInverseId], [e0].[SelfSkipSharedLeftId], ROW_NUMBER() OVER(PARTITION BY [e0].[SelfSkipSharedLeftId] ORDER BY [e1].[Id]) AS [row]
        FROM [EntityTwoEntityTwo] AS [e0]
        INNER JOIN [EntityTwos] AS [e1] ON [e0].[SelfSkipSharedRightId] = [e1].[Id]
    ) AS [t]
    WHERE 2 < [t].[row]
) AS [t0] ON [e].[Id] = [t0].[SelfSkipSharedLeftId]
ORDER BY [e].[Id], [t0].[SelfSkipSharedLeftId], [t0].[Id]
""");
    }

    public override async Task Filtered_include_skip_navigation_order_by_take_split(bool async)
    {
        await base.Filtered_include_skip_navigation_order_by_take_split(async);

        AssertSql(
"""
SELECT [e].[Key1], [e].[Key2], [e].[Key3], [e].[Name]
FROM [EntityCompositeKeys] AS [e]
ORDER BY [e].[Key1], [e].[Key2], [e].[Key3]
""",
//
"""
SELECT [t0].[Id], [t0].[CollectionInverseId], [t0].[ExtraId], [t0].[Name], [t0].[ReferenceInverseId], [e].[Key1], [e].[Key2], [e].[Key3]
FROM [EntityCompositeKeys] AS [e]
INNER JOIN (
    SELECT [t].[Id], [t].[CollectionInverseId], [t].[ExtraId], [t].[Name], [t].[ReferenceInverseId], [t].[CompositeKeySkipSharedKey1], [t].[CompositeKeySkipSharedKey2], [t].[CompositeKeySkipSharedKey3]
    FROM (
        SELECT [e1].[Id], [e1].[CollectionInverseId], [e1].[ExtraId], [e1].[Name], [e1].[ReferenceInverseId], [e0].[CompositeKeySkipSharedKey1], [e0].[CompositeKeySkipSharedKey2], [e0].[CompositeKeySkipSharedKey3], ROW_NUMBER() OVER(PARTITION BY [e0].[CompositeKeySkipSharedKey1], [e0].[CompositeKeySkipSharedKey2], [e0].[CompositeKeySkipSharedKey3] ORDER BY [e1].[Id]) AS [row]
        FROM [EntityCompositeKeyEntityTwo] AS [e0]
        INNER JOIN [EntityTwos] AS [e1] ON [e0].[TwoSkipSharedId] = [e1].[Id]
    ) AS [t]
    WHERE [t].[row] <= 2
) AS [t0] ON [e].[Key1] = [t0].[CompositeKeySkipSharedKey1] AND [e].[Key2] = [t0].[CompositeKeySkipSharedKey2] AND [e].[Key3] = [t0].[CompositeKeySkipSharedKey3]
ORDER BY [e].[Key1], [e].[Key2], [e].[Key3], [t0].[CompositeKeySkipSharedKey1], [t0].[CompositeKeySkipSharedKey2], [t0].[CompositeKeySkipSharedKey3], [t0].[Id]
""");
    }

    public override async Task Filtered_include_skip_navigation_order_by_skip_take_split(bool async)
    {
        await base.Filtered_include_skip_navigation_order_by_skip_take_split(async);

        AssertSql(
"""
SELECT [e].[Key1], [e].[Key2], [e].[Key3], [e].[Name]
FROM [EntityCompositeKeys] AS [e]
ORDER BY [e].[Key1], [e].[Key2], [e].[Key3]
""",
//
"""
SELECT [t0].[Id], [t0].[CollectionInverseId], [t0].[Name], [t0].[ReferenceInverseId], [e].[Key1], [e].[Key2], [e].[Key3]
FROM [EntityCompositeKeys] AS [e]
INNER JOIN (
    SELECT [t].[Id], [t].[CollectionInverseId], [t].[Name], [t].[ReferenceInverseId], [t].[CompositeId1], [t].[CompositeId2], [t].[CompositeId3]
    FROM (
        SELECT [e0].[Id], [e0].[CollectionInverseId], [e0].[Name], [e0].[ReferenceInverseId], [j].[CompositeId1], [j].[CompositeId2], [j].[CompositeId3], ROW_NUMBER() OVER(PARTITION BY [j].[CompositeId1], [j].[CompositeId2], [j].[CompositeId3] ORDER BY [e0].[Id]) AS [row]
        FROM [JoinThreeToCompositeKeyFull] AS [j]
        INNER JOIN [EntityThrees] AS [e0] ON [j].[ThreeId] = [e0].[Id]
    ) AS [t]
    WHERE 1 < [t].[row] AND [t].[row] <= 3
) AS [t0] ON [e].[Key1] = [t0].[CompositeId1] AND [e].[Key2] = [t0].[CompositeId2] AND [e].[Key3] = [t0].[CompositeId3]
ORDER BY [e].[Key1], [e].[Key2], [e].[Key3], [t0].[CompositeId1], [t0].[CompositeId2], [t0].[CompositeId3], [t0].[Id]
""");
    }

    public override async Task Filtered_then_include_skip_navigation_where_split(bool async)
    {
        await base.Filtered_then_include_skip_navigation_where_split(async);

        AssertSql(
            """
SELECT `e`.`Id`, `e`.`Discriminator`, `e`.`Name`, `e`.`Number`, `e`.`Slumber`, `e`.`IsGreen`, `e`.`IsBrown`
FROM `EntityRoots` AS `e`
ORDER BY `e`.`Id`
""",
            //
            """
SELECT `s`.`Id`, `s`.`CollectionInverseId`, `s`.`Name`, `s`.`ReferenceInverseId`, `e`.`Id`, `s`.`RootSkipSharedId`, `s`.`ThreeSkipSharedId`
FROM `EntityRoots` AS `e`
INNER JOIN (
    SELECT `e1`.`Id`, `e1`.`CollectionInverseId`, `e1`.`Name`, `e1`.`ReferenceInverseId`, `e0`.`RootSkipSharedId`, `e0`.`ThreeSkipSharedId`
    FROM `EntityRootEntityThree` AS `e0`
    INNER JOIN `EntityThrees` AS `e1` ON `e0`.`ThreeSkipSharedId` = `e1`.`Id`
) AS `s` ON `e`.`Id` = `s`.`RootSkipSharedId`
ORDER BY `e`.`Id`, `s`.`RootSkipSharedId`, `s`.`ThreeSkipSharedId`, `s`.`Id`
""",
            //
            """
SELECT `s0`.`Id`, `s0`.`Name`, `e`.`Id`, `s`.`RootSkipSharedId`, `s`.`ThreeSkipSharedId`, `s`.`Id`
FROM (`EntityRoots` AS `e`
INNER JOIN (
    SELECT `e1`.`Id`, `e0`.`RootSkipSharedId`, `e0`.`ThreeSkipSharedId`
    FROM `EntityRootEntityThree` AS `e0`
    INNER JOIN `EntityThrees` AS `e1` ON `e0`.`ThreeSkipSharedId` = `e1`.`Id`
) AS `s` ON `e`.`Id` = `s`.`RootSkipSharedId`)
LEFT JOIN (
    SELECT `e2`.`Id`, `e2`.`Name`, `j`.`ThreeId`
    FROM `JoinOneToThreePayloadFullShared` AS `j`
    INNER JOIN `EntityOnes` AS `e2` ON `j`.`OneId` = `e2`.`Id`
    WHERE `e2`.`Id` < 10
) AS `s0` ON `s`.`Id` = `s0`.`ThreeId`
WHERE `s`.`Id` IS NOT NULL AND `s0`.`ThreeId` IS NOT NULL
ORDER BY `e`.`Id`, `s`.`RootSkipSharedId`, `s`.`ThreeSkipSharedId`, `s`.`Id`
""");
    }

    public override async Task Filtered_then_include_skip_navigation_order_by_skip_take_split(bool async)
    {
        await base.Filtered_then_include_skip_navigation_order_by_skip_take_split(async);

        AssertSql(
"""
SELECT [e].[Id], [e].[Discriminator], [e].[Name], [e].[Number], [e].[Slumber], [e].[IsGreen], [e].[IsBrown]
FROM [EntityRoots] AS [e]
ORDER BY [e].[Id]
""",
//
"""
SELECT [t].[Key1], [t].[Key2], [t].[Key3], [t].[Name], [e].[Id], [t].[RootSkipSharedId], [t].[CompositeKeySkipSharedKey1], [t].[CompositeKeySkipSharedKey2], [t].[CompositeKeySkipSharedKey3]
FROM [EntityRoots] AS [e]
INNER JOIN (
    SELECT [e1].[Key1], [e1].[Key2], [e1].[Key3], [e1].[Name], [e0].[RootSkipSharedId], [e0].[CompositeKeySkipSharedKey1], [e0].[CompositeKeySkipSharedKey2], [e0].[CompositeKeySkipSharedKey3]
    FROM [EntityCompositeKeyEntityRoot] AS [e0]
    INNER JOIN [EntityCompositeKeys] AS [e1] ON [e0].[CompositeKeySkipSharedKey1] = [e1].[Key1] AND [e0].[CompositeKeySkipSharedKey2] = [e1].[Key2] AND [e0].[CompositeKeySkipSharedKey3] = [e1].[Key3]
) AS [t] ON [e].[Id] = [t].[RootSkipSharedId]
ORDER BY [e].[Id], [t].[RootSkipSharedId], [t].[CompositeKeySkipSharedKey1], [t].[CompositeKeySkipSharedKey2], [t].[CompositeKeySkipSharedKey3], [t].[Key1], [t].[Key2], [t].[Key3]
""",
//
"""
SELECT [t0].[Id], [t0].[CollectionInverseId], [t0].[Name], [t0].[ReferenceInverseId], [e].[Id], [t].[RootSkipSharedId], [t].[CompositeKeySkipSharedKey1], [t].[CompositeKeySkipSharedKey2], [t].[CompositeKeySkipSharedKey3], [t].[Key1], [t].[Key2], [t].[Key3]
FROM [EntityRoots] AS [e]
INNER JOIN (
    SELECT [e1].[Key1], [e1].[Key2], [e1].[Key3], [e0].[RootSkipSharedId], [e0].[CompositeKeySkipSharedKey1], [e0].[CompositeKeySkipSharedKey2], [e0].[CompositeKeySkipSharedKey3]
    FROM [EntityCompositeKeyEntityRoot] AS [e0]
    INNER JOIN [EntityCompositeKeys] AS [e1] ON [e0].[CompositeKeySkipSharedKey1] = [e1].[Key1] AND [e0].[CompositeKeySkipSharedKey2] = [e1].[Key2] AND [e0].[CompositeKeySkipSharedKey3] = [e1].[Key3]
) AS [t] ON [e].[Id] = [t].[RootSkipSharedId]
INNER JOIN (
    SELECT [t1].[Id], [t1].[CollectionInverseId], [t1].[Name], [t1].[ReferenceInverseId], [t1].[CompositeId1], [t1].[CompositeId2], [t1].[CompositeId3]
    FROM (
        SELECT [e2].[Id], [e2].[CollectionInverseId], [e2].[Name], [e2].[ReferenceInverseId], [j].[CompositeId1], [j].[CompositeId2], [j].[CompositeId3], ROW_NUMBER() OVER(PARTITION BY [j].[CompositeId1], [j].[CompositeId2], [j].[CompositeId3] ORDER BY [e2].[Id]) AS [row]
        FROM [JoinThreeToCompositeKeyFull] AS [j]
        INNER JOIN [EntityThrees] AS [e2] ON [j].[ThreeId] = [e2].[Id]
    ) AS [t1]
    WHERE 1 < [t1].[row] AND [t1].[row] <= 3
) AS [t0] ON [t].[Key1] = [t0].[CompositeId1] AND [t].[Key2] = [t0].[CompositeId2] AND [t].[Key3] = [t0].[CompositeId3]
ORDER BY [e].[Id], [t].[RootSkipSharedId], [t].[CompositeKeySkipSharedKey1], [t].[CompositeKeySkipSharedKey2], [t].[CompositeKeySkipSharedKey3], [t].[Key1], [t].[Key2], [t].[Key3], [t0].[CompositeId1], [t0].[CompositeId2], [t0].[CompositeId3], [t0].[Id]
""");
    }

    public override async Task Filtered_include_skip_navigation_where_then_include_skip_navigation_split(bool async)
    {
        await base.Filtered_include_skip_navigation_where_then_include_skip_navigation_split(async);

        AssertSql(
            """
SELECT `e`.`Id`, `e`.`Discriminator`, `e`.`Name`, `e`.`Number`, `e`.`IsGreen`
FROM `EntityRoots` AS `e`
WHERE `e`.`Discriminator` = 'EntityLeaf'
ORDER BY `e`.`Id`
""",
            //
            """
SELECT `s`.`Key1`, `s`.`Key2`, `s`.`Key3`, `s`.`Name`, `e`.`Id`, `s`.`LeafId`, `s`.`CompositeId1`, `s`.`CompositeId2`, `s`.`CompositeId3`
FROM `EntityRoots` AS `e`
INNER JOIN (
    SELECT `e0`.`Key1`, `e0`.`Key2`, `e0`.`Key3`, `e0`.`Name`, `j`.`LeafId`, `j`.`CompositeId1`, `j`.`CompositeId2`, `j`.`CompositeId3`
    FROM `JoinCompositeKeyToLeaf` AS `j`
    INNER JOIN `EntityCompositeKeys` AS `e0` ON `j`.`CompositeId1` = `e0`.`Key1` AND `j`.`CompositeId2` = `e0`.`Key2` AND `j`.`CompositeId3` = `e0`.`Key3`
    WHERE `e0`.`Key1` < 5
) AS `s` ON `e`.`Id` = `s`.`LeafId`
WHERE `e`.`Discriminator` = 'EntityLeaf'
ORDER BY `e`.`Id`, `s`.`LeafId`, `s`.`CompositeId1`, `s`.`CompositeId2`, `s`.`CompositeId3`, `s`.`Key1`, `s`.`Key2`, `s`.`Key3`
""",
            //
            """
SELECT `s0`.`Id`, `s0`.`CollectionInverseId`, `s0`.`ExtraId`, `s0`.`Name`, `s0`.`ReferenceInverseId`, `e`.`Id`, `s`.`LeafId`, `s`.`CompositeId1`, `s`.`CompositeId2`, `s`.`CompositeId3`, `s`.`Key1`, `s`.`Key2`, `s`.`Key3`
FROM (`EntityRoots` AS `e`
INNER JOIN (
    SELECT `e0`.`Key1`, `e0`.`Key2`, `e0`.`Key3`, `j`.`LeafId`, `j`.`CompositeId1`, `j`.`CompositeId2`, `j`.`CompositeId3`
    FROM `JoinCompositeKeyToLeaf` AS `j`
    INNER JOIN `EntityCompositeKeys` AS `e0` ON `j`.`CompositeId1` = `e0`.`Key1` AND `j`.`CompositeId2` = `e0`.`Key2` AND `j`.`CompositeId3` = `e0`.`Key3`
    WHERE `e0`.`Key1` < 5
) AS `s` ON `e`.`Id` = `s`.`LeafId`)
LEFT JOIN (
    SELECT `e2`.`Id`, `e2`.`CollectionInverseId`, `e2`.`ExtraId`, `e2`.`Name`, `e2`.`ReferenceInverseId`, `e1`.`CompositeKeySkipSharedKey1`, `e1`.`CompositeKeySkipSharedKey2`, `e1`.`CompositeKeySkipSharedKey3`
    FROM `EntityCompositeKeyEntityTwo` AS `e1`
    INNER JOIN `EntityTwos` AS `e2` ON `e1`.`TwoSkipSharedId` = `e2`.`Id`
) AS `s0` ON `s`.`Key1` = `s0`.`CompositeKeySkipSharedKey1` AND `s`.`Key2` = `s0`.`CompositeKeySkipSharedKey2` AND `s`.`Key3` = `s0`.`CompositeKeySkipSharedKey3`
WHERE (`e`.`Discriminator` = 'EntityLeaf') AND (`s`.`Key1` IS NOT NULL AND `s0`.`CompositeKeySkipSharedKey1` IS NOT NULL AND `s`.`Key2` IS NOT NULL AND `s0`.`CompositeKeySkipSharedKey2` IS NOT NULL AND `s`.`Key3` IS NOT NULL AND `s0`.`CompositeKeySkipSharedKey3` IS NOT NULL)
ORDER BY `e`.`Id`, `s`.`LeafId`, `s`.`CompositeId1`, `s`.`CompositeId2`, `s`.`CompositeId3`, `s`.`Key1`, `s`.`Key2`, `s`.`Key3`
""");
    }

    public override async Task Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where_split(bool async)
    {
        await base.Filtered_include_skip_navigation_order_by_skip_take_then_include_skip_navigation_where_split(async);

        AssertSql(
"""
SELECT [e].[Id], [e].[Name]
FROM [EntityOnes] AS [e]
ORDER BY [e].[Id]
""",
//
"""
SELECT [t0].[Id], [t0].[CollectionInverseId], [t0].[ExtraId], [t0].[Name], [t0].[ReferenceInverseId], [e].[Id], [t0].[OneId], [t0].[TwoId]
FROM [EntityOnes] AS [e]
INNER JOIN (
    SELECT [t].[Id], [t].[CollectionInverseId], [t].[ExtraId], [t].[Name], [t].[ReferenceInverseId], [t].[OneId], [t].[TwoId]
    FROM (
        SELECT [e0].[Id], [e0].[CollectionInverseId], [e0].[ExtraId], [e0].[Name], [e0].[ReferenceInverseId], [j].[OneId], [j].[TwoId], ROW_NUMBER() OVER(PARTITION BY [j].[OneId] ORDER BY [e0].[Id]) AS [row]
        FROM [JoinOneToTwo] AS [j]
        INNER JOIN [EntityTwos] AS [e0] ON [j].[TwoId] = [e0].[Id]
    ) AS [t]
    WHERE 1 < [t].[row] AND [t].[row] <= 3
) AS [t0] ON [e].[Id] = [t0].[OneId]
ORDER BY [e].[Id], [t0].[OneId], [t0].[Id], [t0].[TwoId]
""",
//
"""
SELECT [t1].[Id], [t1].[CollectionInverseId], [t1].[Name], [t1].[ReferenceInverseId], [e].[Id], [t0].[OneId], [t0].[TwoId], [t0].[Id]
FROM [EntityOnes] AS [e]
INNER JOIN (
    SELECT [t].[Id], [t].[OneId], [t].[TwoId]
    FROM (
        SELECT [e0].[Id], [j].[OneId], [j].[TwoId], ROW_NUMBER() OVER(PARTITION BY [j].[OneId] ORDER BY [e0].[Id]) AS [row]
        FROM [JoinOneToTwo] AS [j]
        INNER JOIN [EntityTwos] AS [e0] ON [j].[TwoId] = [e0].[Id]
    ) AS [t]
    WHERE 1 < [t].[row] AND [t].[row] <= 3
) AS [t0] ON [e].[Id] = [t0].[OneId]
INNER JOIN (
    SELECT [e1].[Id], [e1].[CollectionInverseId], [e1].[Name], [e1].[ReferenceInverseId], [j0].[TwoId]
    FROM [JoinTwoToThree] AS [j0]
    INNER JOIN [EntityThrees] AS [e1] ON [j0].[ThreeId] = [e1].[Id]
    WHERE [e1].[Id] < 10
) AS [t1] ON [t0].[Id] = [t1].[TwoId]
ORDER BY [e].[Id], [t0].[OneId], [t0].[Id], [t0].[TwoId]
""");
    }

    public override async Task Filtered_include_skip_navigation_where_then_include_skip_navigation_order_by_skip_take_split(bool async)
    {
        await base.Filtered_include_skip_navigation_where_then_include_skip_navigation_order_by_skip_take_split(async);

        AssertSql(
"""
SELECT [e].[Id], [e].[Name]
FROM [EntityOnes] AS [e]
ORDER BY [e].[Id]
""",
//
"""
SELECT [t].[Id], [t].[CollectionInverseId], [t].[ExtraId], [t].[Name], [t].[ReferenceInverseId], [e].[Id], [t].[OneId], [t].[TwoId]
FROM [EntityOnes] AS [e]
INNER JOIN (
    SELECT [e0].[Id], [e0].[CollectionInverseId], [e0].[ExtraId], [e0].[Name], [e0].[ReferenceInverseId], [j].[OneId], [j].[TwoId]
    FROM [JoinOneToTwo] AS [j]
    INNER JOIN [EntityTwos] AS [e0] ON [j].[TwoId] = [e0].[Id]
    WHERE [e0].[Id] < 10
) AS [t] ON [e].[Id] = [t].[OneId]
ORDER BY [e].[Id], [t].[OneId], [t].[TwoId], [t].[Id]
""",
//
"""
SELECT [t0].[Id], [t0].[CollectionInverseId], [t0].[Name], [t0].[ReferenceInverseId], [e].[Id], [t].[OneId], [t].[TwoId], [t].[Id]
FROM [EntityOnes] AS [e]
INNER JOIN (
    SELECT [e0].[Id], [j].[OneId], [j].[TwoId]
    FROM [JoinOneToTwo] AS [j]
    INNER JOIN [EntityTwos] AS [e0] ON [j].[TwoId] = [e0].[Id]
    WHERE [e0].[Id] < 10
) AS [t] ON [e].[Id] = [t].[OneId]
INNER JOIN (
    SELECT [t1].[Id], [t1].[CollectionInverseId], [t1].[Name], [t1].[ReferenceInverseId], [t1].[TwoId]
    FROM (
        SELECT [e1].[Id], [e1].[CollectionInverseId], [e1].[Name], [e1].[ReferenceInverseId], [j0].[TwoId], ROW_NUMBER() OVER(PARTITION BY [j0].[TwoId] ORDER BY [e1].[Id]) AS [row]
        FROM [JoinTwoToThree] AS [j0]
        INNER JOIN [EntityThrees] AS [e1] ON [j0].[ThreeId] = [e1].[Id]
    ) AS [t1]
    WHERE 1 < [t1].[row] AND [t1].[row] <= 3
) AS [t0] ON [t].[Id] = [t0].[TwoId]
ORDER BY [e].[Id], [t].[OneId], [t].[TwoId], [t].[Id], [t0].[TwoId], [t0].[Id]
""");
    }

    public override async Task Filter_include_on_skip_navigation_combined_split(bool async)
    {
        await base.Filter_include_on_skip_navigation_combined_split(async);

        AssertSql(
            """
SELECT `e`.`Id`, `e`.`CollectionInverseId`, `e`.`ExtraId`, `e`.`Name`, `e`.`ReferenceInverseId`
FROM `EntityTwos` AS `e`
ORDER BY `e`.`Id`
""",
            //
            """
SELECT `s`.`Id`, `s`.`Name`, `s`.`Id0`, `s`.`CollectionInverseId`, `s`.`ExtraId`, `s`.`Name0`, `s`.`ReferenceInverseId`, `e`.`Id`, `s`.`OneId`, `s`.`TwoId`
FROM `EntityTwos` AS `e`
INNER JOIN (
    SELECT `e0`.`Id`, `e0`.`Name`, `e1`.`Id` AS `Id0`, `e1`.`CollectionInverseId`, `e1`.`ExtraId`, `e1`.`Name` AS `Name0`, `e1`.`ReferenceInverseId`, `j`.`OneId`, `j`.`TwoId`
    FROM (`JoinOneToTwo` AS `j`
    INNER JOIN `EntityOnes` AS `e0` ON `j`.`OneId` = `e0`.`Id`)
    LEFT JOIN `EntityTwos` AS `e1` ON `e0`.`Id` = `e1`.`ReferenceInverseId`
    WHERE `e0`.`Id` < 10
) AS `s` ON `e`.`Id` = `s`.`TwoId`
ORDER BY `e`.`Id`, `s`.`OneId`, `s`.`TwoId`, `s`.`Id`, `s`.`Id0`
""",
            //
            """
SELECT `e2`.`Id`, `e2`.`CollectionInverseId`, `e2`.`ExtraId`, `e2`.`Name`, `e2`.`ReferenceInverseId`, `e`.`Id`, `s`.`OneId`, `s`.`TwoId`, `s`.`Id`, `s`.`Id0`
FROM (`EntityTwos` AS `e`
INNER JOIN (
    SELECT `e0`.`Id`, `e1`.`Id` AS `Id0`, `j`.`OneId`, `j`.`TwoId`
    FROM (`JoinOneToTwo` AS `j`
    INNER JOIN `EntityOnes` AS `e0` ON `j`.`OneId` = `e0`.`Id`)
    LEFT JOIN `EntityTwos` AS `e1` ON `e0`.`Id` = `e1`.`ReferenceInverseId`
    WHERE `e0`.`Id` < 10
) AS `s` ON `e`.`Id` = `s`.`TwoId`)
LEFT JOIN `EntityTwos` AS `e2` ON `s`.`Id` = `e2`.`CollectionInverseId`
WHERE `s`.`Id` IS NOT NULL AND `e2`.`CollectionInverseId` IS NOT NULL
ORDER BY `e`.`Id`, `s`.`OneId`, `s`.`TwoId`, `s`.`Id`, `s`.`Id0`
""");
    }

    public override async Task Filter_include_on_skip_navigation_combined_with_filtered_then_includes_split(bool async)
    {
        await base.Filter_include_on_skip_navigation_combined_with_filtered_then_includes_split(async);

        AssertSql(
"""
SELECT [e].[Id], [e].[CollectionInverseId], [e].[Name], [e].[ReferenceInverseId]
FROM [EntityThrees] AS [e]
ORDER BY [e].[Id]
""",
//
"""
SELECT [t].[Id], [t].[Name], [e].[Id], [t].[OneId], [t].[ThreeId]
FROM [EntityThrees] AS [e]
INNER JOIN (
    SELECT [e0].[Id], [e0].[Name], [j].[OneId], [j].[ThreeId]
    FROM [JoinOneToThreePayloadFull] AS [j]
    INNER JOIN [EntityOnes] AS [e0] ON [j].[OneId] = [e0].[Id]
    WHERE [e0].[Id] < 10
) AS [t] ON [e].[Id] = [t].[ThreeId]
ORDER BY [e].[Id], [t].[OneId], [t].[ThreeId], [t].[Id]
""",
//
"""
SELECT [t0].[Id], [t0].[CollectionInverseId], [t0].[ExtraId], [t0].[Name], [t0].[ReferenceInverseId], [e].[Id], [t].[OneId], [t].[ThreeId], [t].[Id]
FROM [EntityThrees] AS [e]
INNER JOIN (
    SELECT [e0].[Id], [j].[OneId], [j].[ThreeId]
    FROM [JoinOneToThreePayloadFull] AS [j]
    INNER JOIN [EntityOnes] AS [e0] ON [j].[OneId] = [e0].[Id]
    WHERE [e0].[Id] < 10
) AS [t] ON [e].[Id] = [t].[ThreeId]
INNER JOIN (
    SELECT [t1].[Id], [t1].[CollectionInverseId], [t1].[ExtraId], [t1].[Name], [t1].[ReferenceInverseId], [t1].[OneId]
    FROM (
        SELECT [e1].[Id], [e1].[CollectionInverseId], [e1].[ExtraId], [e1].[Name], [e1].[ReferenceInverseId], [j0].[OneId], ROW_NUMBER() OVER(PARTITION BY [j0].[OneId] ORDER BY [e1].[Id]) AS [row]
        FROM [JoinOneToTwo] AS [j0]
        INNER JOIN [EntityTwos] AS [e1] ON [j0].[TwoId] = [e1].[Id]
    ) AS [t1]
    WHERE 1 < [t1].[row] AND [t1].[row] <= 3
) AS [t0] ON [t].[Id] = [t0].[OneId]
ORDER BY [e].[Id], [t].[OneId], [t].[ThreeId], [t].[Id], [t0].[OneId], [t0].[Id]
""",
//
"""
SELECT [t0].[Id], [t0].[Discriminator], [t0].[Name], [t0].[Number], [t0].[IsGreen], [e].[Id], [t].[OneId], [t].[ThreeId], [t].[Id]
FROM [EntityThrees] AS [e]
INNER JOIN (
    SELECT [e0].[Id], [j].[OneId], [j].[ThreeId]
    FROM [JoinOneToThreePayloadFull] AS [j]
    INNER JOIN [EntityOnes] AS [e0] ON [j].[OneId] = [e0].[Id]
    WHERE [e0].[Id] < 10
) AS [t] ON [e].[Id] = [t].[ThreeId]
INNER JOIN (
    SELECT [t1].[Id], [t1].[Discriminator], [t1].[Name], [t1].[Number], [t1].[IsGreen], [j0].[EntityOneId]
    FROM [JoinOneToBranch] AS [j0]
    INNER JOIN (
        SELECT [e1].[Id], [e1].[Discriminator], [e1].[Name], [e1].[Number], [e1].[IsGreen]
        FROM [EntityRoots] AS [e1]
        WHERE [e1].[Discriminator] IN (N'EntityBranch', N'EntityLeaf')
    ) AS [t1] ON [j0].[EntityBranchId] = [t1].[Id]
    WHERE [t1].[Id] < 20
) AS [t0] ON [t].[Id] = [t0].[EntityOneId]
ORDER BY [e].[Id], [t].[OneId], [t].[ThreeId], [t].[Id]
""");
    }

    public override async Task Filtered_include_on_skip_navigation_then_filtered_include_on_navigation_split(bool async)
    {
        await base.Filtered_include_on_skip_navigation_then_filtered_include_on_navigation_split(async);

        AssertSql(
            """
SELECT `e`.`Id`, `e`.`CollectionInverseId`, `e`.`Name`, `e`.`ReferenceInverseId`
FROM `EntityThrees` AS `e`
ORDER BY `e`.`Id`
""",
            //
            """
SELECT `s`.`Id`, `s`.`Name`, `e`.`Id`, `s`.`OneId`, `s`.`ThreeId`
FROM `EntityThrees` AS `e`
INNER JOIN (
    SELECT `e0`.`Id`, `e0`.`Name`, `j`.`OneId`, `j`.`ThreeId`
    FROM `JoinOneToThreePayloadFull` AS `j`
    INNER JOIN `EntityOnes` AS `e0` ON `j`.`OneId` = `e0`.`Id`
    WHERE `e0`.`Id` > 15
) AS `s` ON `e`.`Id` = `s`.`ThreeId`
ORDER BY `e`.`Id`, `s`.`OneId`, `s`.`ThreeId`, `s`.`Id`
""",
            //
            """
SELECT `e2`.`Id`, `e2`.`CollectionInverseId`, `e2`.`ExtraId`, `e2`.`Name`, `e2`.`ReferenceInverseId`, `e`.`Id`, `s`.`OneId`, `s`.`ThreeId`, `s`.`Id`
FROM (`EntityThrees` AS `e`
INNER JOIN (
    SELECT `e0`.`Id`, `j`.`OneId`, `j`.`ThreeId`
    FROM `JoinOneToThreePayloadFull` AS `j`
    INNER JOIN `EntityOnes` AS `e0` ON `j`.`OneId` = `e0`.`Id`
    WHERE `e0`.`Id` > 15
) AS `s` ON `e`.`Id` = `s`.`ThreeId`)
LEFT JOIN (
    SELECT `e1`.`Id`, `e1`.`CollectionInverseId`, `e1`.`ExtraId`, `e1`.`Name`, `e1`.`ReferenceInverseId`
    FROM `EntityTwos` AS `e1`
    WHERE `e1`.`Id` < 5
) AS `e2` ON `s`.`Id` = `e2`.`CollectionInverseId`
WHERE `s`.`Id` IS NOT NULL AND `e2`.`CollectionInverseId` IS NOT NULL
ORDER BY `e`.`Id`, `s`.`OneId`, `s`.`ThreeId`, `s`.`Id`
""");
    }

    public override async Task Filtered_include_on_navigation_then_filtered_include_on_skip_navigation_split(bool async)
    {
        await base.Filtered_include_on_navigation_then_filtered_include_on_skip_navigation_split(async);

        AssertSql(
            """
SELECT `e`.`Id`, `e`.`Name`
FROM `EntityOnes` AS `e`
ORDER BY `e`.`Id`
""",
            //
            """
SELECT `e2`.`Id`, `e2`.`CollectionInverseId`, `e2`.`ExtraId`, `e2`.`Name`, `e2`.`ReferenceInverseId`, `e`.`Id`
FROM `EntityOnes` AS `e`
INNER JOIN (
    SELECT `e0`.`Id`, `e0`.`CollectionInverseId`, `e0`.`ExtraId`, `e0`.`Name`, `e0`.`ReferenceInverseId`
    FROM `EntityTwos` AS `e0`
    WHERE `e0`.`Id` > 15
) AS `e2` ON `e`.`Id` = `e2`.`CollectionInverseId`
ORDER BY `e`.`Id`, `e2`.`Id`
""",
            //
            """
SELECT `s`.`Id`, `s`.`CollectionInverseId`, `s`.`Name`, `s`.`ReferenceInverseId`, `e`.`Id`, `e2`.`Id`
FROM (`EntityOnes` AS `e`
INNER JOIN (
    SELECT `e0`.`Id`, `e0`.`CollectionInverseId`
    FROM `EntityTwos` AS `e0`
    WHERE `e0`.`Id` > 15
) AS `e2` ON `e`.`Id` = `e2`.`CollectionInverseId`)
LEFT JOIN (
    SELECT `e1`.`Id`, `e1`.`CollectionInverseId`, `e1`.`Name`, `e1`.`ReferenceInverseId`, `j`.`TwoId`
    FROM `JoinTwoToThree` AS `j`
    INNER JOIN `EntityThrees` AS `e1` ON `j`.`ThreeId` = `e1`.`Id`
    WHERE `e1`.`Id` < 5
) AS `s` ON `e2`.`Id` = `s`.`TwoId`
WHERE `e2`.`Id` IS NOT NULL AND `s`.`TwoId` IS NOT NULL
ORDER BY `e`.`Id`, `e2`.`Id`
""");
    }

    private void AssertSql(params string[] expected)
        => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);
}
