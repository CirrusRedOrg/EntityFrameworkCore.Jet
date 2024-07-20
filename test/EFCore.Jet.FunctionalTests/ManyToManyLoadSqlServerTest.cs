// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestModels.ManyToManyModel;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests;
#nullable disable
public class ManyToManyLoadJetTest : ManyToManyLoadTestBase<ManyToManyLoadJetTest.ManyToManyLoadJetFixture>
{
    public ManyToManyLoadJetTest(ManyToManyLoadJetFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    public override async Task Load_collection(EntityState state, QueryTrackingBehavior queryTrackingBehavior, bool async)
    {
        await base.Load_collection(state, queryTrackingBehavior, async);

        AssertSql(
            """
@__p_0='3'
@__p_0='3'

SELECT `s`.`Id`, `s`.`CollectionInverseId`, `s`.`ExtraId`, `s`.`Name`, `s`.`ReferenceInverseId`, `e`.`Id`, `s`.`OneId`, `s`.`TwoId`, `s0`.`Id`, `s0`.`Name`, `s0`.`OneId`, `s0`.`TwoId`
FROM (`EntityOnes` AS `e`
INNER JOIN (
    SELECT `e0`.`Id`, `e0`.`CollectionInverseId`, `e0`.`ExtraId`, `e0`.`Name`, `e0`.`ReferenceInverseId`, `j`.`OneId`, `j`.`TwoId`
    FROM `JoinOneToTwo` AS `j`
    INNER JOIN `EntityTwos` AS `e0` ON `j`.`TwoId` = `e0`.`Id`
) AS `s` ON `e`.`Id` = `s`.`OneId`)
LEFT JOIN (
    SELECT `e1`.`Id`, `e1`.`Name`, `j0`.`OneId`, `j0`.`TwoId`
    FROM `JoinOneToTwo` AS `j0`
    INNER JOIN `EntityOnes` AS `e1` ON `j0`.`OneId` = `e1`.`Id`
    WHERE `e1`.`Id` = @__p_0
) AS `s0` ON `s`.`Id` = `s0`.`TwoId`
WHERE `e`.`Id` = @__p_0
ORDER BY `e`.`Id`, `s`.`OneId`, `s`.`TwoId`, `s`.`Id`, `s0`.`OneId`, `s0`.`TwoId`
""");
    }

    public override async Task Load_collection_using_Query_with_Include_for_inverse(bool async)
    {
        await base.Load_collection_using_Query_with_Include_for_inverse(async);

        AssertSql(
            """
@__p_0='3'
@__p_0='3'

SELECT `s`.`Id`, `s`.`CollectionInverseId`, `s`.`ExtraId`, `s`.`Name`, `s`.`ReferenceInverseId`, `e`.`Id`, `s`.`OneSkipSharedId`, `s`.`TwoSkipSharedId`, `s0`.`OneSkipSharedId`, `s0`.`TwoSkipSharedId`, `s0`.`Id`, `s0`.`Name`
FROM (`EntityOnes` AS `e`
INNER JOIN (
    SELECT `e1`.`Id`, `e1`.`CollectionInverseId`, `e1`.`ExtraId`, `e1`.`Name`, `e1`.`ReferenceInverseId`, `e0`.`OneSkipSharedId`, `e0`.`TwoSkipSharedId`
    FROM `EntityOneEntityTwo` AS `e0`
    INNER JOIN `EntityTwos` AS `e1` ON `e0`.`TwoSkipSharedId` = `e1`.`Id`
) AS `s` ON `e`.`Id` = `s`.`OneSkipSharedId`)
LEFT JOIN (
    SELECT `e2`.`OneSkipSharedId`, `e2`.`TwoSkipSharedId`, `e3`.`Id`, `e3`.`Name`
    FROM `EntityOneEntityTwo` AS `e2`
    INNER JOIN `EntityOnes` AS `e3` ON `e2`.`OneSkipSharedId` = `e3`.`Id`
    WHERE `e3`.`Id` = @__p_0
) AS `s0` ON `s`.`Id` = `s0`.`TwoSkipSharedId`
WHERE `e`.`Id` = @__p_0
ORDER BY `e`.`Id`, `s`.`OneSkipSharedId`, `s`.`TwoSkipSharedId`, `s`.`Id`, `s0`.`OneSkipSharedId`, `s0`.`TwoSkipSharedId`
""");
    }

    public override async Task Load_collection_using_Query_with_Include_for_same_collection(bool async)
    {
        await base.Load_collection_using_Query_with_Include_for_same_collection(async);

        AssertSql(
            """
@__p_0='3'
@__p_0='3'

SELECT `s`.`Id`, `s`.`CollectionInverseId`, `s`.`ExtraId`, `s`.`Name`, `s`.`ReferenceInverseId`, `e`.`Id`, `s`.`OneSkipSharedId`, `s`.`TwoSkipSharedId`, `s1`.`OneSkipSharedId`, `s1`.`TwoSkipSharedId`, `s1`.`Id`, `s1`.`Name`, `s1`.`OneSkipSharedId0`, `s1`.`TwoSkipSharedId0`, `s1`.`Id0`, `s1`.`CollectionInverseId`, `s1`.`ExtraId`, `s1`.`Name0`, `s1`.`ReferenceInverseId`
FROM (`EntityOnes` AS `e`
INNER JOIN (
    SELECT `e1`.`Id`, `e1`.`CollectionInverseId`, `e1`.`ExtraId`, `e1`.`Name`, `e1`.`ReferenceInverseId`, `e0`.`OneSkipSharedId`, `e0`.`TwoSkipSharedId`
    FROM `EntityOneEntityTwo` AS `e0`
    INNER JOIN `EntityTwos` AS `e1` ON `e0`.`TwoSkipSharedId` = `e1`.`Id`
) AS `s` ON `e`.`Id` = `s`.`OneSkipSharedId`)
LEFT JOIN (
    SELECT `e2`.`OneSkipSharedId`, `e2`.`TwoSkipSharedId`, `e3`.`Id`, `e3`.`Name`, `s0`.`OneSkipSharedId` AS `OneSkipSharedId0`, `s0`.`TwoSkipSharedId` AS `TwoSkipSharedId0`, `s0`.`Id` AS `Id0`, `s0`.`CollectionInverseId`, `s0`.`ExtraId`, `s0`.`Name` AS `Name0`, `s0`.`ReferenceInverseId`
    FROM (`EntityOneEntityTwo` AS `e2`
    INNER JOIN `EntityOnes` AS `e3` ON `e2`.`OneSkipSharedId` = `e3`.`Id`)
    LEFT JOIN (
        SELECT `e4`.`OneSkipSharedId`, `e4`.`TwoSkipSharedId`, `e5`.`Id`, `e5`.`CollectionInverseId`, `e5`.`ExtraId`, `e5`.`Name`, `e5`.`ReferenceInverseId`
        FROM `EntityOneEntityTwo` AS `e4`
        INNER JOIN `EntityTwos` AS `e5` ON `e4`.`TwoSkipSharedId` = `e5`.`Id`
    ) AS `s0` ON `e3`.`Id` = `s0`.`OneSkipSharedId`
    WHERE `e3`.`Id` = @__p_0
) AS `s1` ON `s`.`Id` = `s1`.`TwoSkipSharedId`
WHERE `e`.`Id` = @__p_0
ORDER BY `e`.`Id`, `s`.`OneSkipSharedId`, `s`.`TwoSkipSharedId`, `s`.`Id`, `s1`.`OneSkipSharedId`, `s1`.`TwoSkipSharedId`, `s1`.`Id`, `s1`.`OneSkipSharedId0`, `s1`.`TwoSkipSharedId0`
""");
    }

    public override async Task Load_collection_using_Query_with_Include(bool async)
    {
        await base.Load_collection_using_Query_with_Include(async);

        AssertSql(
            """
@__p_0='3'
@__p_0='3'

SELECT `s`.`Id`, `s`.`CollectionInverseId`, `s`.`ExtraId`, `s`.`Name`, `s`.`ReferenceInverseId`, `e`.`Id`, `s`.`OneSkipSharedId`, `s`.`TwoSkipSharedId`, `s0`.`OneSkipSharedId`, `s0`.`TwoSkipSharedId`, `s0`.`Id`, `s0`.`Name`, `s1`.`ThreeId`, `s1`.`TwoId`, `s1`.`Id`, `s1`.`CollectionInverseId`, `s1`.`Name`, `s1`.`ReferenceInverseId`
FROM ((`EntityOnes` AS `e`
INNER JOIN (
    SELECT `e1`.`Id`, `e1`.`CollectionInverseId`, `e1`.`ExtraId`, `e1`.`Name`, `e1`.`ReferenceInverseId`, `e0`.`OneSkipSharedId`, `e0`.`TwoSkipSharedId`
    FROM `EntityOneEntityTwo` AS `e0`
    INNER JOIN `EntityTwos` AS `e1` ON `e0`.`TwoSkipSharedId` = `e1`.`Id`
) AS `s` ON `e`.`Id` = `s`.`OneSkipSharedId`)
LEFT JOIN (
    SELECT `e2`.`OneSkipSharedId`, `e2`.`TwoSkipSharedId`, `e3`.`Id`, `e3`.`Name`
    FROM `EntityOneEntityTwo` AS `e2`
    INNER JOIN `EntityOnes` AS `e3` ON `e2`.`OneSkipSharedId` = `e3`.`Id`
    WHERE `e3`.`Id` = @__p_0
) AS `s0` ON `s`.`Id` = `s0`.`TwoSkipSharedId`)
LEFT JOIN (
    SELECT `j`.`ThreeId`, `j`.`TwoId`, `e4`.`Id`, `e4`.`CollectionInverseId`, `e4`.`Name`, `e4`.`ReferenceInverseId`
    FROM `JoinTwoToThree` AS `j`
    INNER JOIN `EntityThrees` AS `e4` ON `j`.`ThreeId` = `e4`.`Id`
) AS `s1` ON `s`.`Id` = `s1`.`TwoId`
WHERE `e`.`Id` = @__p_0
ORDER BY `e`.`Id`, `s`.`OneSkipSharedId`, `s`.`TwoSkipSharedId`, `s`.`Id`, `s0`.`OneSkipSharedId`, `s0`.`TwoSkipSharedId`, `s0`.`Id`, `s1`.`ThreeId`, `s1`.`TwoId`
""");
    }

    public override async Task Load_collection_using_Query_with_filtered_Include(bool async)
    {
        await base.Load_collection_using_Query_with_filtered_Include(async);

        AssertSql(
            """
@__p_0='3'
@__p_0='3'

SELECT `s`.`Id`, `s`.`CollectionInverseId`, `s`.`ExtraId`, `s`.`Name`, `s`.`ReferenceInverseId`, `e`.`Id`, `s`.`OneSkipSharedId`, `s`.`TwoSkipSharedId`, `s0`.`OneSkipSharedId`, `s0`.`TwoSkipSharedId`, `s0`.`Id`, `s0`.`Name`, `s1`.`ThreeId`, `s1`.`TwoId`, `s1`.`Id`, `s1`.`CollectionInverseId`, `s1`.`Name`, `s1`.`ReferenceInverseId`
FROM ((`EntityOnes` AS `e`
INNER JOIN (
    SELECT `e1`.`Id`, `e1`.`CollectionInverseId`, `e1`.`ExtraId`, `e1`.`Name`, `e1`.`ReferenceInverseId`, `e0`.`OneSkipSharedId`, `e0`.`TwoSkipSharedId`
    FROM `EntityOneEntityTwo` AS `e0`
    INNER JOIN `EntityTwos` AS `e1` ON `e0`.`TwoSkipSharedId` = `e1`.`Id`
) AS `s` ON `e`.`Id` = `s`.`OneSkipSharedId`)
LEFT JOIN (
    SELECT `e2`.`OneSkipSharedId`, `e2`.`TwoSkipSharedId`, `e3`.`Id`, `e3`.`Name`
    FROM `EntityOneEntityTwo` AS `e2`
    INNER JOIN `EntityOnes` AS `e3` ON `e2`.`OneSkipSharedId` = `e3`.`Id`
    WHERE `e3`.`Id` = @__p_0
) AS `s0` ON `s`.`Id` = `s0`.`TwoSkipSharedId`)
LEFT JOIN (
    SELECT `j`.`ThreeId`, `j`.`TwoId`, `e4`.`Id`, `e4`.`CollectionInverseId`, `e4`.`Name`, `e4`.`ReferenceInverseId`
    FROM `JoinTwoToThree` AS `j`
    INNER JOIN `EntityThrees` AS `e4` ON `j`.`ThreeId` = `e4`.`Id`
    WHERE `e4`.`Id` IN (13, 11)
) AS `s1` ON `s`.`Id` = `s1`.`TwoId`
WHERE `e`.`Id` = @__p_0
ORDER BY `e`.`Id`, `s`.`OneSkipSharedId`, `s`.`TwoSkipSharedId`, `s`.`Id`, `s0`.`OneSkipSharedId`, `s0`.`TwoSkipSharedId`, `s0`.`Id`, `s1`.`ThreeId`, `s1`.`TwoId`
""");
    }

    public override async Task Load_collection_using_Query_with_filtered_Include_and_projection(bool async)
    {
        await base.Load_collection_using_Query_with_filtered_Include_and_projection(async);

        AssertSql(
"""
@__p_0='3'

SELECT [t].[Id], [t].[Name], (
    SELECT COUNT(*)
    FROM [EntityOneEntityTwo] AS [e2]
    INNER JOIN [EntityOnes] AS [e3] ON [e2].[OneSkipSharedId] = [e3].[Id]
    WHERE [t].[Id] = [e2].[TwoSkipSharedId]) AS [Count1], (
    SELECT COUNT(*)
    FROM [JoinTwoToThree] AS [j]
    INNER JOIN [EntityThrees] AS [e4] ON [j].[ThreeId] = [e4].[Id]
    WHERE [t].[Id] = [j].[TwoId]) AS [Count3]
FROM [EntityOnes] AS [e]
INNER JOIN (
    SELECT [e1].[Id], [e1].[Name], [e0].[OneSkipSharedId]
    FROM [EntityOneEntityTwo] AS [e0]
    INNER JOIN [EntityTwos] AS [e1] ON [e0].[TwoSkipSharedId] = [e1].[Id]
) AS [t] ON [e].[Id] = [t].[OneSkipSharedId]
WHERE [e].[Id] = @__p_0
ORDER BY [t].[Id]
""");
    }

    public override async Task Load_collection_using_Query_with_join(bool async)
    {
        await base.Load_collection_using_Query_with_join(async);

        AssertSql(
            """
@__p_0='3'
@__p_0='3'

SELECT `s`.`Id`, `s`.`CollectionInverseId`, `s`.`ExtraId`, `s`.`Name`, `s`.`ReferenceInverseId`, `e`.`Id`, `s`.`OneSkipSharedId`, `s`.`TwoSkipSharedId`, `s1`.`Id`, `s1`.`OneSkipSharedId`, `s1`.`TwoSkipSharedId`, `s1`.`Id0`, `s2`.`OneSkipSharedId`, `s2`.`TwoSkipSharedId`, `s2`.`Id`, `s2`.`Name`, `s1`.`CollectionInverseId`, `s1`.`ExtraId`, `s1`.`Name0`, `s1`.`ReferenceInverseId`
FROM ((`EntityOnes` AS `e`
INNER JOIN (
    SELECT `e1`.`Id`, `e1`.`CollectionInverseId`, `e1`.`ExtraId`, `e1`.`Name`, `e1`.`ReferenceInverseId`, `e0`.`OneSkipSharedId`, `e0`.`TwoSkipSharedId`
    FROM `EntityOneEntityTwo` AS `e0`
    INNER JOIN `EntityTwos` AS `e1` ON `e0`.`TwoSkipSharedId` = `e1`.`Id`
) AS `s` ON `e`.`Id` = `s`.`OneSkipSharedId`)
LEFT JOIN (
    SELECT `e2`.`Id`, `s0`.`Id` AS `Id0`, `s0`.`CollectionInverseId`, `s0`.`ExtraId`, `s0`.`Name` AS `Name0`, `s0`.`ReferenceInverseId`, `s0`.`OneSkipSharedId`, `s0`.`TwoSkipSharedId`
    FROM `EntityOnes` AS `e2`
    INNER JOIN (
        SELECT `e4`.`Id`, `e4`.`CollectionInverseId`, `e4`.`ExtraId`, `e4`.`Name`, `e4`.`ReferenceInverseId`, `e3`.`OneSkipSharedId`, `e3`.`TwoSkipSharedId`
        FROM `EntityOneEntityTwo` AS `e3`
        INNER JOIN `EntityTwos` AS `e4` ON `e3`.`TwoSkipSharedId` = `e4`.`Id`
    ) AS `s0` ON `e2`.`Id` = `s0`.`OneSkipSharedId`
) AS `s1` ON `s`.`Id` = `s1`.`Id0`)
LEFT JOIN (
    SELECT `e5`.`OneSkipSharedId`, `e5`.`TwoSkipSharedId`, `e6`.`Id`, `e6`.`Name`
    FROM `EntityOneEntityTwo` AS `e5`
    INNER JOIN `EntityOnes` AS `e6` ON `e5`.`OneSkipSharedId` = `e6`.`Id`
    WHERE `e6`.`Id` = @__p_0
) AS `s2` ON `s`.`Id` = `s2`.`TwoSkipSharedId`
WHERE (`e`.`Id` = @__p_0) AND (`s`.`Id` IS NOT NULL AND `s1`.`Id0` IS NOT NULL)
ORDER BY `e`.`Id`, `s`.`OneSkipSharedId`, `s`.`TwoSkipSharedId`, `s`.`Id`, `s1`.`Id`, `s1`.`OneSkipSharedId`, `s1`.`TwoSkipSharedId`, `s1`.`Id0`, `s2`.`OneSkipSharedId`, `s2`.`TwoSkipSharedId`
""");
    }

    protected override void ClearLog()
        => Fixture.TestSqlLoggerFactory.Clear();

    protected override void RecordLog()
        => Sql = Fixture.TestSqlLoggerFactory.Sql;

    private const string FileNewLine = @"
";

    private void AssertSql(params string[] expected)
        => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);

    private string Sql { get; set; }

    public class ManyToManyLoadJetFixture : ManyToManyLoadFixtureBase, ITestSqlLoggerFactory
    {
        public TestSqlLoggerFactory TestSqlLoggerFactory
            => (TestSqlLoggerFactory)ListLoggerFactory;

        protected override ITestStoreFactory TestStoreFactory
            => JetTestStoreFactory.Instance;

        protected override void OnModelCreating(ModelBuilder modelBuilder, DbContext context)
        {
            base.OnModelCreating(modelBuilder, context);

            modelBuilder
                .Entity<JoinOneSelfPayload>()
                .Property(e => e.Payload)
                .HasDefaultValueSql("DATE()");

            modelBuilder
                .SharedTypeEntity<Dictionary<string, object>>("JoinOneToThreePayloadFullShared")
                .IndexerProperty<string>("Payload")
                .HasDefaultValue("Generated");

            modelBuilder
                .Entity<JoinOneToThreePayloadFull>()
                .Property(e => e.Payload)
                .HasDefaultValue("Generated");

            modelBuilder
                .Entity<UnidirectionalJoinOneSelfPayload>()
                .Property(e => e.Payload)
                .HasDefaultValueSql("DATE()");

            modelBuilder
                .SharedTypeEntity<Dictionary<string, object>>("UnidirectionalJoinOneToThreePayloadFullShared")
                .IndexerProperty<string>("Payload")
                .HasDefaultValue("Generated");

            modelBuilder
                .Entity<UnidirectionalJoinOneToThreePayloadFull>()
                .Property(e => e.Payload)
                .HasDefaultValue("Generated");
        }
    }
}
