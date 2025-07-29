// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.EntityFrameworkCore.Query.Relationships.Projection;
using Microsoft.EntityFrameworkCore.TestUtilities;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query.Relationships.Projection;

public class OwnedReferenceNoTrackingProjectionJetTest
    : OwnedReferenceNoTrackingProjectionRelationalTestBase<OwnedRelationshipsJetFixture>
{
    public OwnedReferenceNoTrackingProjectionJetTest(OwnedRelationshipsJetFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    public override async Task Select_root(bool async)
    {
        await base.Select_root(async);
    }

    public override async Task Select_trunk_optional(bool async)
    {
        await base.Select_trunk_optional(async);

        AssertSql(
            """
SELECT `r`.`Id`, `r`.`OptionalReferenceTrunk_Name`, `s`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `s`.`Id1`, `s`.`Name`, `s`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `s`.`RelationshipsBranchEntityId1`, `s`.`Id10`, `s`.`Name0`, `s`.`OptionalReferenceLeaf_Name`, `s`.`RequiredReferenceLeaf_Name`, `r`.`OptionalReferenceTrunk_OptionalReferenceBranch_Name`, `r2`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r2`.`Id1`, `r2`.`Name`, `r`.`OptionalReferenceTrunk_OptionalReferenceBranch_OptionalReferenc~`, `r`.`OptionalReferenceTrunk_OptionalReferenceBranch_RequiredReferenc~`, `r`.`OptionalReferenceTrunk_RequiredReferenceBranch_Name`, `r3`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r3`.`Id1`, `r3`.`Name`, `r`.`OptionalReferenceTrunk_RequiredReferenceBranch_OptionalReferenc~`, `r`.`OptionalReferenceTrunk_RequiredReferenceBranch_RequiredReferenc~`
FROM ((`RootEntities` AS `r`
LEFT JOIN (
    SELECT `r0`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `r0`.`Id1`, `r0`.`Name`, `r1`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r1`.`RelationshipsBranchEntityId1`, `r1`.`Id1` AS `Id10`, `r1`.`Name` AS `Name0`, `r0`.`OptionalReferenceLeaf_Name`, `r0`.`RequiredReferenceLeaf_Name`
    FROM `Root_OptionalReferenceTrunk_CollectionBranch` AS `r0`
    LEFT JOIN `Root_OptionalReferenceTrunk_CollectionBranch_CollectionLeaf` AS `r1` ON `r0`.`RelationshipsTrunkEntityRelationshipsRootEntityId` = `r1`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~` AND `r0`.`Id1` = `r1`.`RelationshipsBranchEntityId1`
) AS `s` ON IIF(`r`.`OptionalReferenceTrunk_Name` IS NOT NULL, `r`.`Id`, NULL) = `s`.`RelationshipsTrunkEntityRelationshipsRootEntityId`)
LEFT JOIN `Root_OptionalReferenceTrunk_OptionalReferenceBranch_CollB7BC1840` AS `r2` ON IIF(`r`.`OptionalReferenceTrunk_OptionalReferenceBranch_Name` IS NOT NULL, `r`.`Id`, NULL) = `r2`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`)
LEFT JOIN `Root_OptionalReferenceTrunk_RequiredReferenceBranch_CollB7BC1840` AS `r3` ON IIF(`r`.`OptionalReferenceTrunk_RequiredReferenceBranch_Name` IS NOT NULL, `r`.`Id`, NULL) = `r3`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`
ORDER BY `r`.`Id`, `s`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `s`.`Id1`, `s`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `s`.`RelationshipsBranchEntityId1`, `s`.`Id10`, `r2`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r2`.`Id1`, `r3`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`
""");
    }

    public override async Task Select_trunk_required(bool async)
    {
        await base.Select_trunk_required(async);

        AssertSql(
            """
SELECT `r`.`Id`, `r`.`RequiredReferenceTrunk_Name`, `s`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `s`.`Id1`, `s`.`Name`, `s`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `s`.`RelationshipsBranchEntityId1`, `s`.`Id10`, `s`.`Name0`, `s`.`OptionalReferenceLeaf_Name`, `s`.`RequiredReferenceLeaf_Name`, `r`.`RequiredReferenceTrunk_OptionalReferenceBranch_Name`, `r2`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r2`.`Id1`, `r2`.`Name`, `r`.`RequiredReferenceTrunk_OptionalReferenceBranch_OptionalReferenc~`, `r`.`RequiredReferenceTrunk_OptionalReferenceBranch_RequiredReferenc~`, `r`.`RequiredReferenceTrunk_RequiredReferenceBranch_Name`, `r3`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r3`.`Id1`, `r3`.`Name`, `r`.`RequiredReferenceTrunk_RequiredReferenceBranch_OptionalReferenc~`, `r`.`RequiredReferenceTrunk_RequiredReferenceBranch_RequiredReferenc~`
FROM ((`RootEntities` AS `r`
LEFT JOIN (
    SELECT `r0`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `r0`.`Id1`, `r0`.`Name`, `r1`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r1`.`RelationshipsBranchEntityId1`, `r1`.`Id1` AS `Id10`, `r1`.`Name` AS `Name0`, `r0`.`OptionalReferenceLeaf_Name`, `r0`.`RequiredReferenceLeaf_Name`
    FROM `Root_RequiredReferenceTrunk_CollectionBranch` AS `r0`
    LEFT JOIN `Root_RequiredReferenceTrunk_CollectionBranch_CollectionLeaf` AS `r1` ON `r0`.`RelationshipsTrunkEntityRelationshipsRootEntityId` = `r1`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~` AND `r0`.`Id1` = `r1`.`RelationshipsBranchEntityId1`
) AS `s` ON `r`.`Id` = `s`.`RelationshipsTrunkEntityRelationshipsRootEntityId`)
LEFT JOIN `Root_RequiredReferenceTrunk_OptionalReferenceBranch_CollB7BC1840` AS `r2` ON IIF(`r`.`RequiredReferenceTrunk_OptionalReferenceBranch_Name` IS NOT NULL, `r`.`Id`, NULL) = `r2`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`)
LEFT JOIN `Root_RequiredReferenceTrunk_RequiredReferenceBranch_CollB7BC1840` AS `r3` ON `r`.`Id` = `r3`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`
ORDER BY `r`.`Id`, `s`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `s`.`Id1`, `s`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `s`.`RelationshipsBranchEntityId1`, `s`.`Id10`, `r2`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r2`.`Id1`, `r3`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`
""");
    }

    public override async Task Select_branch_required_required(bool async)
    {
        await base.Select_branch_required_required(async);

        AssertSql(
            """
SELECT `r`.`Id`, `r`.`RequiredReferenceTrunk_RequiredReferenceBranch_Name`, `r0`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r0`.`Id1`, `r0`.`Name`, `r`.`RequiredReferenceTrunk_RequiredReferenceBranch_OptionalReferenc~`, `r`.`RequiredReferenceTrunk_RequiredReferenceBranch_RequiredReferenc~`
FROM `RootEntities` AS `r`
LEFT JOIN `Root_RequiredReferenceTrunk_RequiredReferenceBranch_CollB7BC1840` AS `r0` ON `r`.`Id` = `r0`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`
ORDER BY `r`.`Id`, `r0`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`
""");
    }

    public override async Task Select_branch_required_optional(bool async)
    {
        await base.Select_branch_required_optional(async);

        AssertSql(
            """
SELECT `r`.`Id`, `r`.`RequiredReferenceTrunk_OptionalReferenceBranch_Name`, `r0`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r0`.`Id1`, `r0`.`Name`, `r`.`RequiredReferenceTrunk_OptionalReferenceBranch_OptionalReferenc~`, `r`.`RequiredReferenceTrunk_OptionalReferenceBranch_RequiredReferenc~`
FROM `RootEntities` AS `r`
LEFT JOIN `Root_RequiredReferenceTrunk_OptionalReferenceBranch_CollB7BC1840` AS `r0` ON IIF(`r`.`RequiredReferenceTrunk_OptionalReferenceBranch_Name` IS NOT NULL, `r`.`Id`, NULL) = `r0`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`
ORDER BY `r`.`Id`, `r0`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`
""");
    }

    public override async Task Select_branch_optional_required(bool async)
    {
        await base.Select_branch_optional_required(async);

        AssertSql(
            """
SELECT `r`.`Id`, `r`.`RequiredReferenceTrunk_RequiredReferenceBranch_Name`, `r0`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r0`.`Id1`, `r0`.`Name`, `r`.`RequiredReferenceTrunk_RequiredReferenceBranch_OptionalReferenc~`, `r`.`RequiredReferenceTrunk_RequiredReferenceBranch_RequiredReferenc~`
FROM `RootEntities` AS `r`
LEFT JOIN `Root_RequiredReferenceTrunk_RequiredReferenceBranch_CollB7BC1840` AS `r0` ON `r`.`Id` = `r0`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`
ORDER BY `r`.`Id`, `r0`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`
""");
    }

    public override async Task Select_branch_optional_optional(bool async)
    {
        await base.Select_branch_optional_optional(async);

        AssertSql(
            """
SELECT `r`.`Id`, `r`.`RequiredReferenceTrunk_OptionalReferenceBranch_Name`, `r0`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r0`.`Id1`, `r0`.`Name`, `r`.`RequiredReferenceTrunk_OptionalReferenceBranch_OptionalReferenc~`, `r`.`RequiredReferenceTrunk_OptionalReferenceBranch_RequiredReferenc~`
FROM `RootEntities` AS `r`
LEFT JOIN `Root_RequiredReferenceTrunk_OptionalReferenceBranch_CollB7BC1840` AS `r0` ON IIF(`r`.`RequiredReferenceTrunk_OptionalReferenceBranch_Name` IS NOT NULL, `r`.`Id`, NULL) = `r0`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`
ORDER BY `r`.`Id`, `r0`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`
""");
    }

    public override async Task Select_root_duplicated(bool async)
    {
        await base.Select_root_duplicated(async);
    }

    public override async Task Select_trunk_and_branch_duplicated(bool async)
    {
        await base.Select_trunk_and_branch_duplicated(async);

        AssertSql(
            """
SELECT `r`.`Id`, `r`.`OptionalReferenceTrunk_Name`, `s`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `s`.`Id1`, `s`.`Name`, `s`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `s`.`RelationshipsBranchEntityId1`, `s`.`Id10`, `s`.`Name0`, `s`.`OptionalReferenceLeaf_Name`, `s`.`RequiredReferenceLeaf_Name`, `r`.`OptionalReferenceTrunk_OptionalReferenceBranch_Name`, `r2`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r2`.`Id1`, `r2`.`Name`, `r`.`OptionalReferenceTrunk_OptionalReferenceBranch_OptionalReferenc~`, `r`.`OptionalReferenceTrunk_OptionalReferenceBranch_RequiredReferenc~`, `r`.`OptionalReferenceTrunk_RequiredReferenceBranch_Name`, `r3`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r3`.`Id1`, `r3`.`Name`, `r`.`OptionalReferenceTrunk_RequiredReferenceBranch_OptionalReferenc~`, `r`.`OptionalReferenceTrunk_RequiredReferenceBranch_RequiredReferenc~`, `r4`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r4`.`Id1`, `r4`.`Name`, `s0`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `s0`.`Id1`, `s0`.`Name`, `s0`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `s0`.`RelationshipsBranchEntityId1`, `s0`.`Id10`, `s0`.`Name0`, `s0`.`OptionalReferenceLeaf_Name`, `s0`.`RequiredReferenceLeaf_Name`, `r7`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r7`.`Id1`, `r7`.`Name`, `r8`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r8`.`Id1`, `r8`.`Name`, `r9`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r9`.`Id1`, `r9`.`Name`
FROM (((((((`RootEntities` AS `r`
LEFT JOIN (
    SELECT `r0`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `r0`.`Id1`, `r0`.`Name`, `r1`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r1`.`RelationshipsBranchEntityId1`, `r1`.`Id1` AS `Id10`, `r1`.`Name` AS `Name0`, `r0`.`OptionalReferenceLeaf_Name`, `r0`.`RequiredReferenceLeaf_Name`
    FROM `Root_OptionalReferenceTrunk_CollectionBranch` AS `r0`
    LEFT JOIN `Root_OptionalReferenceTrunk_CollectionBranch_CollectionLeaf` AS `r1` ON `r0`.`RelationshipsTrunkEntityRelationshipsRootEntityId` = `r1`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~` AND `r0`.`Id1` = `r1`.`RelationshipsBranchEntityId1`
) AS `s` ON IIF(`r`.`OptionalReferenceTrunk_Name` IS NOT NULL, `r`.`Id`, NULL) = `s`.`RelationshipsTrunkEntityRelationshipsRootEntityId`)
LEFT JOIN `Root_OptionalReferenceTrunk_OptionalReferenceBranch_CollB7BC1840` AS `r2` ON IIF(`r`.`OptionalReferenceTrunk_OptionalReferenceBranch_Name` IS NOT NULL, `r`.`Id`, NULL) = `r2`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`)
LEFT JOIN `Root_OptionalReferenceTrunk_RequiredReferenceBranch_CollB7BC1840` AS `r3` ON IIF(`r`.`OptionalReferenceTrunk_RequiredReferenceBranch_Name` IS NOT NULL, `r`.`Id`, NULL) = `r3`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`)
LEFT JOIN `Root_OptionalReferenceTrunk_RequiredReferenceBranch_CollB7BC1840` AS `r4` ON IIF(`r`.`OptionalReferenceTrunk_RequiredReferenceBranch_Name` IS NOT NULL, `r`.`Id`, NULL) = `r4`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`)
LEFT JOIN (
    SELECT `r5`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `r5`.`Id1`, `r5`.`Name`, `r6`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r6`.`RelationshipsBranchEntityId1`, `r6`.`Id1` AS `Id10`, `r6`.`Name` AS `Name0`, `r5`.`OptionalReferenceLeaf_Name`, `r5`.`RequiredReferenceLeaf_Name`
    FROM `Root_OptionalReferenceTrunk_CollectionBranch` AS `r5`
    LEFT JOIN `Root_OptionalReferenceTrunk_CollectionBranch_CollectionLeaf` AS `r6` ON `r5`.`RelationshipsTrunkEntityRelationshipsRootEntityId` = `r6`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~` AND `r5`.`Id1` = `r6`.`RelationshipsBranchEntityId1`
) AS `s0` ON IIF(`r`.`OptionalReferenceTrunk_Name` IS NOT NULL, `r`.`Id`, NULL) = `s0`.`RelationshipsTrunkEntityRelationshipsRootEntityId`)
LEFT JOIN `Root_OptionalReferenceTrunk_OptionalReferenceBranch_CollB7BC1840` AS `r7` ON IIF(`r`.`OptionalReferenceTrunk_OptionalReferenceBranch_Name` IS NOT NULL, `r`.`Id`, NULL) = `r7`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`)
LEFT JOIN `Root_OptionalReferenceTrunk_RequiredReferenceBranch_CollB7BC1840` AS `r8` ON IIF(`r`.`OptionalReferenceTrunk_RequiredReferenceBranch_Name` IS NOT NULL, `r`.`Id`, NULL) = `r8`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`)
LEFT JOIN `Root_OptionalReferenceTrunk_RequiredReferenceBranch_CollB7BC1840` AS `r9` ON IIF(`r`.`OptionalReferenceTrunk_RequiredReferenceBranch_Name` IS NOT NULL, `r`.`Id`, NULL) = `r9`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`
ORDER BY `r`.`Id`, `s`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `s`.`Id1`, `s`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `s`.`RelationshipsBranchEntityId1`, `s`.`Id10`, `r2`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r2`.`Id1`, `r3`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r3`.`Id1`, `r4`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r4`.`Id1`, `s0`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `s0`.`Id1`, `s0`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `s0`.`RelationshipsBranchEntityId1`, `s0`.`Id10`, `r7`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r7`.`Id1`, `r8`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r8`.`Id1`, `r9`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`
""");
    }

    public override async Task Select_trunk_and_trunk_duplicated(bool async)
    {
        await base.Select_trunk_and_trunk_duplicated(async);

        AssertSql(
            """
SELECT `r`.`Id`, `r`.`RequiredReferenceTrunk_Name`, `s`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `s`.`Id1`, `s`.`Name`, `s`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `s`.`RelationshipsBranchEntityId1`, `s`.`Id10`, `s`.`Name0`, `s`.`OptionalReferenceLeaf_Name`, `s`.`RequiredReferenceLeaf_Name`, `r`.`RequiredReferenceTrunk_OptionalReferenceBranch_Name`, `r2`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r2`.`Id1`, `r2`.`Name`, `r`.`RequiredReferenceTrunk_OptionalReferenceBranch_OptionalReferenc~`, `r`.`RequiredReferenceTrunk_OptionalReferenceBranch_RequiredReferenc~`, `r`.`RequiredReferenceTrunk_RequiredReferenceBranch_Name`, `r3`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r3`.`Id1`, `r3`.`Name`, `r`.`RequiredReferenceTrunk_RequiredReferenceBranch_OptionalReferenc~`, `r`.`RequiredReferenceTrunk_RequiredReferenceBranch_RequiredReferenc~`, `s0`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `s0`.`Id1`, `s0`.`Name`, `s0`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `s0`.`RelationshipsBranchEntityId1`, `s0`.`Id10`, `s0`.`Name0`, `s0`.`OptionalReferenceLeaf_Name`, `s0`.`RequiredReferenceLeaf_Name`, `r6`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r6`.`Id1`, `r6`.`Name`, `r7`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r7`.`Id1`, `r7`.`Name`
FROM (((((`RootEntities` AS `r`
LEFT JOIN (
    SELECT `r0`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `r0`.`Id1`, `r0`.`Name`, `r1`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r1`.`RelationshipsBranchEntityId1`, `r1`.`Id1` AS `Id10`, `r1`.`Name` AS `Name0`, `r0`.`OptionalReferenceLeaf_Name`, `r0`.`RequiredReferenceLeaf_Name`
    FROM `Root_RequiredReferenceTrunk_CollectionBranch` AS `r0`
    LEFT JOIN `Root_RequiredReferenceTrunk_CollectionBranch_CollectionLeaf` AS `r1` ON `r0`.`RelationshipsTrunkEntityRelationshipsRootEntityId` = `r1`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~` AND `r0`.`Id1` = `r1`.`RelationshipsBranchEntityId1`
) AS `s` ON `r`.`Id` = `s`.`RelationshipsTrunkEntityRelationshipsRootEntityId`)
LEFT JOIN `Root_RequiredReferenceTrunk_OptionalReferenceBranch_CollB7BC1840` AS `r2` ON IIF(`r`.`RequiredReferenceTrunk_OptionalReferenceBranch_Name` IS NOT NULL, `r`.`Id`, NULL) = `r2`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`)
LEFT JOIN `Root_RequiredReferenceTrunk_RequiredReferenceBranch_CollB7BC1840` AS `r3` ON `r`.`Id` = `r3`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`)
LEFT JOIN (
    SELECT `r4`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `r4`.`Id1`, `r4`.`Name`, `r5`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r5`.`RelationshipsBranchEntityId1`, `r5`.`Id1` AS `Id10`, `r5`.`Name` AS `Name0`, `r4`.`OptionalReferenceLeaf_Name`, `r4`.`RequiredReferenceLeaf_Name`
    FROM `Root_RequiredReferenceTrunk_CollectionBranch` AS `r4`
    LEFT JOIN `Root_RequiredReferenceTrunk_CollectionBranch_CollectionLeaf` AS `r5` ON `r4`.`RelationshipsTrunkEntityRelationshipsRootEntityId` = `r5`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~` AND `r4`.`Id1` = `r5`.`RelationshipsBranchEntityId1`
) AS `s0` ON `r`.`Id` = `s0`.`RelationshipsTrunkEntityRelationshipsRootEntityId`)
LEFT JOIN `Root_RequiredReferenceTrunk_OptionalReferenceBranch_CollB7BC1840` AS `r6` ON IIF(`r`.`RequiredReferenceTrunk_OptionalReferenceBranch_Name` IS NOT NULL, `r`.`Id`, NULL) = `r6`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`)
LEFT JOIN `Root_RequiredReferenceTrunk_RequiredReferenceBranch_CollB7BC1840` AS `r7` ON `r`.`Id` = `r7`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`
ORDER BY `r`.`Id`, `s`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `s`.`Id1`, `s`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `s`.`RelationshipsBranchEntityId1`, `s`.`Id10`, `r2`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r2`.`Id1`, `r3`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r3`.`Id1`, `s0`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `s0`.`Id1`, `s0`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `s0`.`RelationshipsBranchEntityId1`, `s0`.`Id10`, `r6`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r6`.`Id1`, `r7`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`
""");
    }

    public override async Task Select_leaf_trunk_root(bool async)
    {
        await base.Select_leaf_trunk_root(async);
    }

    public override async Task Select_subquery_root_set_required_trunk_FirstOrDefault_branch(bool async)
    {
        await base.Select_subquery_root_set_required_trunk_FirstOrDefault_branch(async);

        AssertSql(
            """
SELECT [r2].[Id], [r2].[RequiredReferenceTrunk_RequiredReferenceBranch_Name], [r].[Id], [r1].[RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsRootEntityId], [r1].[Id1], [r1].[Name], [r2].[RequiredReferenceTrunk_RequiredReferenceBranch_OptionalReferenceLeaf_Name], [r2].[RequiredReferenceTrunk_RequiredReferenceBranch_RequiredReferenceLeaf_Name]
FROM [RootEntities] AS [r]
OUTER APPLY (
    SELECT TOP(1) [r0].[Id], [r0].[RequiredReferenceTrunk_RequiredReferenceBranch_Name], [r0].[RequiredReferenceTrunk_RequiredReferenceBranch_OptionalReferenceLeaf_Name], [r0].[RequiredReferenceTrunk_RequiredReferenceBranch_RequiredReferenceLeaf_Name]
    FROM [RootEntities] AS [r0]
    ORDER BY [r0].[Id]
) AS [r2]
LEFT JOIN [Root_RequiredReferenceTrunk_RequiredReferenceBranch_CollectionLeaf] AS [r1] ON [r2].[Id] = [r1].[RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsRootEntityId]
ORDER BY [r].[Id], [r2].[Id], [r1].[RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsRootEntityId]
""");
    }

    public override async Task Select_subquery_root_set_optional_trunk_FirstOrDefault_branch(bool async)
    {
        await base.Select_subquery_root_set_optional_trunk_FirstOrDefault_branch(async);

        AssertSql(
            """
SELECT [r2].[Id], [r2].[OptionalReferenceTrunk_OptionalReferenceBranch_Name], [r].[Id], [r1].[RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsRootEntityId], [r1].[Id1], [r1].[Name], [r2].[OptionalReferenceTrunk_OptionalReferenceBranch_OptionalReferenceLeaf_Name], [r2].[OptionalReferenceTrunk_OptionalReferenceBranch_RequiredReferenceLeaf_Name]
FROM [RootEntities] AS [r]
OUTER APPLY (
    SELECT TOP(1) [r0].[Id], [r0].[OptionalReferenceTrunk_OptionalReferenceBranch_Name], [r0].[OptionalReferenceTrunk_OptionalReferenceBranch_OptionalReferenceLeaf_Name], [r0].[OptionalReferenceTrunk_OptionalReferenceBranch_RequiredReferenceLeaf_Name]
    FROM [RootEntities] AS [r0]
    ORDER BY [r0].[Id]
) AS [r2]
LEFT JOIN [Root_OptionalReferenceTrunk_OptionalReferenceBranch_CollectionLeaf] AS [r1] ON CASE
    WHEN [r2].[OptionalReferenceTrunk_OptionalReferenceBranch_Name] IS NOT NULL THEN [r2].[Id]
END = [r1].[RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsRootEntityId]
ORDER BY [r].[Id], [r2].[Id], [r1].[RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsRootEntityId]
""");
    }

    [ConditionalFact]
    public virtual void Check_all_tests_overridden()
        => TestHelpers.AssertAllMethodsOverridden(GetType());

    private void AssertSql(params string[] expected)
        => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);
}
