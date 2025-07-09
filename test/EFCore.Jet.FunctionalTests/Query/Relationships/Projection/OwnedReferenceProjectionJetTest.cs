// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query.Relationships.Projection;
using Microsoft.EntityFrameworkCore.TestUtilities;
using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query.Relationships.Projection;

public class OwnedReferenceProjectionJetTest
    : OwnedReferenceProjectionRelationalTestBase<OwnedRelationshipsJetFixture>
{
    public OwnedReferenceProjectionJetTest(OwnedRelationshipsJetFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture)
    {
        Fixture.TestSqlLoggerFactory.Clear();
        Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
    }

    public override async Task Select_root(bool async)
    {
        await base.Select_root(async);

        AssertSql(
            """
SELECT `r`.`Id`, `r`.`Name`, `r`.`OptionalReferenceTrunkId`, `r`.`RequiredReferenceTrunkId`, `s0`.`RelationshipsRootEntityId`, `s0`.`Id1`, `s0`.`Name`, `s0`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `s0`.`RelationshipsTrunkEntityId1`, `s0`.`Id10`, `s0`.`Name0`, `s0`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `s0`.`RelationshipsBranchEntityRelationshipsTrunkEntityId1`, `s0`.`RelationshipsBranchEntityId1`, `s0`.`Id100`, `s0`.`Name00`, `s0`.`OptionalReferenceLeaf_Name`, `s0`.`RequiredReferenceLeaf_Name`, `s0`.`OptionalReferenceBranch_Name`, `s0`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelatio5A630C8B`, `s0`.`RelationshipsBranchEntityRelationshipsTrunkEntityId10`, `s0`.`Id11`, `s0`.`Name1`, `s0`.`OptionalReferenceBranch_OptionalReferenceLeaf_Name`, `s0`.`OptionalReferenceBranch_RequiredReferenceLeaf_Name`, `s0`.`RequiredReferenceBranch_Name`, `s0`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelatio3D99058E`, `s0`.`RelationshipsBranchEntityRelationshipsTrunkEntityId11`, `s0`.`Id12`, `s0`.`Name2`, `s0`.`RequiredReferenceBranch_OptionalReferenceLeaf_Name`, `s0`.`RequiredReferenceBranch_RequiredReferenceLeaf_Name`, `r`.`OptionalReferenceTrunk_Name`, `s1`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `s1`.`Id1`, `s1`.`Name`, `s1`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `s1`.`RelationshipsBranchEntityId1`, `s1`.`Id10`, `s1`.`Name0`, `s1`.`OptionalReferenceLeaf_Name`, `s1`.`RequiredReferenceLeaf_Name`, `r`.`OptionalReferenceTrunk_OptionalReferenceBranch_Name`, `r7`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r7`.`Id1`, `r7`.`Name`, `r`.`OptionalReferenceTrunk_OptionalReferenceBranch_OptionalReferenc~`, `r`.`OptionalReferenceTrunk_OptionalReferenceBranch_RequiredReferenc~`, `r`.`OptionalReferenceTrunk_RequiredReferenceBranch_Name`, `r8`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r8`.`Id1`, `r8`.`Name`, `r`.`OptionalReferenceTrunk_RequiredReferenceBranch_OptionalReferenc~`, `r`.`OptionalReferenceTrunk_RequiredReferenceBranch_RequiredReferenc~`, `r`.`RequiredReferenceTrunk_Name`, `s2`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `s2`.`Id1`, `s2`.`Name`, `s2`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `s2`.`RelationshipsBranchEntityId1`, `s2`.`Id10`, `s2`.`Name0`, `s2`.`OptionalReferenceLeaf_Name`, `s2`.`RequiredReferenceLeaf_Name`, `r`.`RequiredReferenceTrunk_OptionalReferenceBranch_Name`, `r11`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r11`.`Id1`, `r11`.`Name`, `r`.`RequiredReferenceTrunk_OptionalReferenceBranch_OptionalReferenc~`, `r`.`RequiredReferenceTrunk_OptionalReferenceBranch_RequiredReferenc~`, `r`.`RequiredReferenceTrunk_RequiredReferenceBranch_Name`, `r12`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r12`.`Id1`, `r12`.`Name`, `r`.`RequiredReferenceTrunk_RequiredReferenceBranch_OptionalReferenc~`, `r`.`RequiredReferenceTrunk_RequiredReferenceBranch_RequiredReferenc~`
FROM ((((((`RootEntities` AS `r`
LEFT JOIN (
    SELECT `r0`.`RelationshipsRootEntityId`, `r0`.`Id1`, `r0`.`Name`, `s`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `s`.`RelationshipsTrunkEntityId1`, `s`.`Id1` AS `Id10`, `s`.`Name` AS `Name0`, `s`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `s`.`RelationshipsBranchEntityRelationshipsTrunkEntityId1`, `s`.`RelationshipsBranchEntityId1`, `s`.`Id10` AS `Id100`, `s`.`Name0` AS `Name00`, `s`.`OptionalReferenceLeaf_Name`, `s`.`RequiredReferenceLeaf_Name`, `r0`.`OptionalReferenceBranch_Name`, `r3`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~` AS `RelationshipsBranchEntityRelationshipsTrunkEntityRelatio5A630C8B`, `r3`.`RelationshipsBranchEntityRelationshipsTrunkEntityId1` AS `RelationshipsBranchEntityRelationshipsTrunkEntityId10`, `r3`.`Id1` AS `Id11`, `r3`.`Name` AS `Name1`, `r0`.`OptionalReferenceBranch_OptionalReferenceLeaf_Name`, `r0`.`OptionalReferenceBranch_RequiredReferenceLeaf_Name`, `r0`.`RequiredReferenceBranch_Name`, `r4`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~` AS `RelationshipsBranchEntityRelationshipsTrunkEntityRelatio3D99058E`, `r4`.`RelationshipsBranchEntityRelationshipsTrunkEntityId1` AS `RelationshipsBranchEntityRelationshipsTrunkEntityId11`, `r4`.`Id1` AS `Id12`, `r4`.`Name` AS `Name2`, `r0`.`RequiredReferenceBranch_OptionalReferenceLeaf_Name`, `r0`.`RequiredReferenceBranch_RequiredReferenceLeaf_Name`
    FROM ((`Root_CollectionTrunk` AS `r0`
    LEFT JOIN (
        SELECT `r1`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `r1`.`RelationshipsTrunkEntityId1`, `r1`.`Id1`, `r1`.`Name`, `r2`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r2`.`RelationshipsBranchEntityRelationshipsTrunkEntityId1`, `r2`.`RelationshipsBranchEntityId1`, `r2`.`Id1` AS `Id10`, `r2`.`Name` AS `Name0`, `r1`.`OptionalReferenceLeaf_Name`, `r1`.`RequiredReferenceLeaf_Name`
        FROM `Root_CollectionTrunk_CollectionBranch` AS `r1`
        LEFT JOIN `Root_CollectionTrunk_CollectionBranch_CollectionLeaf` AS `r2` ON `r1`.`RelationshipsTrunkEntityRelationshipsRootEntityId` = `r2`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~` AND `r1`.`RelationshipsTrunkEntityId1` = `r2`.`RelationshipsBranchEntityRelationshipsTrunkEntityId1` AND `r1`.`Id1` = `r2`.`RelationshipsBranchEntityId1`
    ) AS `s` ON `r0`.`RelationshipsRootEntityId` = `s`.`RelationshipsTrunkEntityRelationshipsRootEntityId` AND `r0`.`Id1` = `s`.`RelationshipsTrunkEntityId1`)
    LEFT JOIN `Root_CollectionTrunk_OptionalReferenceBranch_CollectionLeaf` AS `r3` ON IIF(`r0`.`OptionalReferenceBranch_Name` IS NOT NULL, `r0`.`RelationshipsRootEntityId`, NULL) = `r3`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~` AND IIF(`r0`.`OptionalReferenceBranch_Name` IS NOT NULL, `r0`.`Id1`, NULL) = `r3`.`RelationshipsBranchEntityRelationshipsTrunkEntityId1`)
    LEFT JOIN `Root_CollectionTrunk_RequiredReferenceBranch_CollectionLeaf` AS `r4` ON `r0`.`RelationshipsRootEntityId` = `r4`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~` AND `r0`.`Id1` = `r4`.`RelationshipsBranchEntityRelationshipsTrunkEntityId1`
) AS `s0` ON `r`.`Id` = `s0`.`RelationshipsRootEntityId`)
LEFT JOIN (
    SELECT `r5`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `r5`.`Id1`, `r5`.`Name`, `r6`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r6`.`RelationshipsBranchEntityId1`, `r6`.`Id1` AS `Id10`, `r6`.`Name` AS `Name0`, `r5`.`OptionalReferenceLeaf_Name`, `r5`.`RequiredReferenceLeaf_Name`
    FROM `Root_OptionalReferenceTrunk_CollectionBranch` AS `r5`
    LEFT JOIN `Root_OptionalReferenceTrunk_CollectionBranch_CollectionLeaf` AS `r6` ON `r5`.`RelationshipsTrunkEntityRelationshipsRootEntityId` = `r6`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~` AND `r5`.`Id1` = `r6`.`RelationshipsBranchEntityId1`
) AS `s1` ON IIF(`r`.`OptionalReferenceTrunk_Name` IS NOT NULL, `r`.`Id`, NULL) = `s1`.`RelationshipsTrunkEntityRelationshipsRootEntityId`)
LEFT JOIN `Root_OptionalReferenceTrunk_OptionalReferenceBranch_CollB7BC1840` AS `r7` ON IIF(`r`.`OptionalReferenceTrunk_OptionalReferenceBranch_Name` IS NOT NULL, `r`.`Id`, NULL) = `r7`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`)
LEFT JOIN `Root_OptionalReferenceTrunk_RequiredReferenceBranch_CollB7BC1840` AS `r8` ON IIF(`r`.`OptionalReferenceTrunk_RequiredReferenceBranch_Name` IS NOT NULL, `r`.`Id`, NULL) = `r8`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`)
LEFT JOIN (
    SELECT `r9`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `r9`.`Id1`, `r9`.`Name`, `r10`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r10`.`RelationshipsBranchEntityId1`, `r10`.`Id1` AS `Id10`, `r10`.`Name` AS `Name0`, `r9`.`OptionalReferenceLeaf_Name`, `r9`.`RequiredReferenceLeaf_Name`
    FROM `Root_RequiredReferenceTrunk_CollectionBranch` AS `r9`
    LEFT JOIN `Root_RequiredReferenceTrunk_CollectionBranch_CollectionLeaf` AS `r10` ON `r9`.`RelationshipsTrunkEntityRelationshipsRootEntityId` = `r10`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~` AND `r9`.`Id1` = `r10`.`RelationshipsBranchEntityId1`
) AS `s2` ON `r`.`Id` = `s2`.`RelationshipsTrunkEntityRelationshipsRootEntityId`)
LEFT JOIN `Root_RequiredReferenceTrunk_OptionalReferenceBranch_CollB7BC1840` AS `r11` ON IIF(`r`.`RequiredReferenceTrunk_OptionalReferenceBranch_Name` IS NOT NULL, `r`.`Id`, NULL) = `r11`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`)
LEFT JOIN `Root_RequiredReferenceTrunk_RequiredReferenceBranch_CollB7BC1840` AS `r12` ON `r`.`Id` = `r12`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`
ORDER BY `r`.`Id`, `s0`.`RelationshipsRootEntityId`, `s0`.`Id1`, `s0`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `s0`.`RelationshipsTrunkEntityId1`, `s0`.`Id10`, `s0`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `s0`.`RelationshipsBranchEntityRelationshipsTrunkEntityId1`, `s0`.`RelationshipsBranchEntityId1`, `s0`.`Id100`, `s0`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelatio5A630C8B`, `s0`.`RelationshipsBranchEntityRelationshipsTrunkEntityId10`, `s0`.`Id11`, `s0`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelatio3D99058E`, `s0`.`RelationshipsBranchEntityRelationshipsTrunkEntityId11`, `s0`.`Id12`, `s1`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `s1`.`Id1`, `s1`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `s1`.`RelationshipsBranchEntityId1`, `s1`.`Id10`, `r7`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r7`.`Id1`, `r8`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r8`.`Id1`, `s2`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `s2`.`Id1`, `s2`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `s2`.`RelationshipsBranchEntityId1`, `s2`.`Id10`, `r11`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r11`.`Id1`, `r12`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`
""");
    }

    public override async Task Select_root_duplicated(bool async)
    {
        await base.Select_root_duplicated(async);

        AssertSql(
            """
SELECT `r`.`Id`, `r`.`Name`, `r`.`OptionalReferenceTrunkId`, `r`.`RequiredReferenceTrunkId`, `s0`.`RelationshipsRootEntityId`, `s0`.`Id1`, `s0`.`Name`, `s0`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `s0`.`RelationshipsTrunkEntityId1`, `s0`.`Id10`, `s0`.`Name0`, `s0`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `s0`.`RelationshipsBranchEntityRelationshipsTrunkEntityId1`, `s0`.`RelationshipsBranchEntityId1`, `s0`.`Id100`, `s0`.`Name00`, `s0`.`OptionalReferenceLeaf_Name`, `s0`.`RequiredReferenceLeaf_Name`, `s0`.`OptionalReferenceBranch_Name`, `s0`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelatio5A630C8B`, `s0`.`RelationshipsBranchEntityRelationshipsTrunkEntityId10`, `s0`.`Id11`, `s0`.`Name1`, `s0`.`OptionalReferenceBranch_OptionalReferenceLeaf_Name`, `s0`.`OptionalReferenceBranch_RequiredReferenceLeaf_Name`, `s0`.`RequiredReferenceBranch_Name`, `s0`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelatio3D99058E`, `s0`.`RelationshipsBranchEntityRelationshipsTrunkEntityId11`, `s0`.`Id12`, `s0`.`Name2`, `s0`.`RequiredReferenceBranch_OptionalReferenceLeaf_Name`, `s0`.`RequiredReferenceBranch_RequiredReferenceLeaf_Name`, `r`.`OptionalReferenceTrunk_Name`, `s1`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `s1`.`Id1`, `s1`.`Name`, `s1`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `s1`.`RelationshipsBranchEntityId1`, `s1`.`Id10`, `s1`.`Name0`, `s1`.`OptionalReferenceLeaf_Name`, `s1`.`RequiredReferenceLeaf_Name`, `r`.`OptionalReferenceTrunk_OptionalReferenceBranch_Name`, `r7`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r7`.`Id1`, `r7`.`Name`, `r`.`OptionalReferenceTrunk_OptionalReferenceBranch_OptionalReferenc~`, `r`.`OptionalReferenceTrunk_OptionalReferenceBranch_RequiredReferenc~`, `r`.`OptionalReferenceTrunk_RequiredReferenceBranch_Name`, `r8`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r8`.`Id1`, `r8`.`Name`, `r`.`OptionalReferenceTrunk_RequiredReferenceBranch_OptionalReferenc~`, `r`.`OptionalReferenceTrunk_RequiredReferenceBranch_RequiredReferenc~`, `r`.`RequiredReferenceTrunk_Name`, `s2`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `s2`.`Id1`, `s2`.`Name`, `s2`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `s2`.`RelationshipsBranchEntityId1`, `s2`.`Id10`, `s2`.`Name0`, `s2`.`OptionalReferenceLeaf_Name`, `s2`.`RequiredReferenceLeaf_Name`, `r`.`RequiredReferenceTrunk_OptionalReferenceBranch_Name`, `r11`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r11`.`Id1`, `r11`.`Name`, `r`.`RequiredReferenceTrunk_OptionalReferenceBranch_OptionalReferenc~`, `r`.`RequiredReferenceTrunk_OptionalReferenceBranch_RequiredReferenc~`, `r`.`RequiredReferenceTrunk_RequiredReferenceBranch_Name`, `r12`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r12`.`Id1`, `r12`.`Name`, `r`.`RequiredReferenceTrunk_RequiredReferenceBranch_OptionalReferenc~`, `r`.`RequiredReferenceTrunk_RequiredReferenceBranch_RequiredReferenc~`, `s4`.`RelationshipsRootEntityId`, `s4`.`Id1`, `s4`.`Name`, `s4`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `s4`.`RelationshipsTrunkEntityId1`, `s4`.`Id10`, `s4`.`Name0`, `s4`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `s4`.`RelationshipsBranchEntityRelationshipsTrunkEntityId1`, `s4`.`RelationshipsBranchEntityId1`, `s4`.`Id100`, `s4`.`Name00`, `s4`.`OptionalReferenceLeaf_Name`, `s4`.`RequiredReferenceLeaf_Name`, `s4`.`OptionalReferenceBranch_Name`, `s4`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelatio5A630C8B`, `s4`.`RelationshipsBranchEntityRelationshipsTrunkEntityId10`, `s4`.`Id11`, `s4`.`Name1`, `s4`.`OptionalReferenceBranch_OptionalReferenceLeaf_Name`, `s4`.`OptionalReferenceBranch_RequiredReferenceLeaf_Name`, `s4`.`RequiredReferenceBranch_Name`, `s4`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelatio3D99058E`, `s4`.`RelationshipsBranchEntityRelationshipsTrunkEntityId11`, `s4`.`Id12`, `s4`.`Name2`, `s4`.`RequiredReferenceBranch_OptionalReferenceLeaf_Name`, `s4`.`RequiredReferenceBranch_RequiredReferenceLeaf_Name`, `s5`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `s5`.`Id1`, `s5`.`Name`, `s5`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `s5`.`RelationshipsBranchEntityId1`, `s5`.`Id10`, `s5`.`Name0`, `s5`.`OptionalReferenceLeaf_Name`, `s5`.`RequiredReferenceLeaf_Name`, `r20`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r20`.`Id1`, `r20`.`Name`, `r21`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r21`.`Id1`, `r21`.`Name`, `s6`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `s6`.`Id1`, `s6`.`Name`, `s6`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `s6`.`RelationshipsBranchEntityId1`, `s6`.`Id10`, `s6`.`Name0`, `s6`.`OptionalReferenceLeaf_Name`, `s6`.`RequiredReferenceLeaf_Name`, `r24`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r24`.`Id1`, `r24`.`Name`, `r25`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r25`.`Id1`, `r25`.`Name`
FROM (((((((((((((`RootEntities` AS `r`
LEFT JOIN (
    SELECT `r0`.`RelationshipsRootEntityId`, `r0`.`Id1`, `r0`.`Name`, `s`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `s`.`RelationshipsTrunkEntityId1`, `s`.`Id1` AS `Id10`, `s`.`Name` AS `Name0`, `s`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `s`.`RelationshipsBranchEntityRelationshipsTrunkEntityId1`, `s`.`RelationshipsBranchEntityId1`, `s`.`Id10` AS `Id100`, `s`.`Name0` AS `Name00`, `s`.`OptionalReferenceLeaf_Name`, `s`.`RequiredReferenceLeaf_Name`, `r0`.`OptionalReferenceBranch_Name`, `r3`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~` AS `RelationshipsBranchEntityRelationshipsTrunkEntityRelatio5A630C8B`, `r3`.`RelationshipsBranchEntityRelationshipsTrunkEntityId1` AS `RelationshipsBranchEntityRelationshipsTrunkEntityId10`, `r3`.`Id1` AS `Id11`, `r3`.`Name` AS `Name1`, `r0`.`OptionalReferenceBranch_OptionalReferenceLeaf_Name`, `r0`.`OptionalReferenceBranch_RequiredReferenceLeaf_Name`, `r0`.`RequiredReferenceBranch_Name`, `r4`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~` AS `RelationshipsBranchEntityRelationshipsTrunkEntityRelatio3D99058E`, `r4`.`RelationshipsBranchEntityRelationshipsTrunkEntityId1` AS `RelationshipsBranchEntityRelationshipsTrunkEntityId11`, `r4`.`Id1` AS `Id12`, `r4`.`Name` AS `Name2`, `r0`.`RequiredReferenceBranch_OptionalReferenceLeaf_Name`, `r0`.`RequiredReferenceBranch_RequiredReferenceLeaf_Name`
    FROM ((`Root_CollectionTrunk` AS `r0`
    LEFT JOIN (
        SELECT `r1`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `r1`.`RelationshipsTrunkEntityId1`, `r1`.`Id1`, `r1`.`Name`, `r2`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r2`.`RelationshipsBranchEntityRelationshipsTrunkEntityId1`, `r2`.`RelationshipsBranchEntityId1`, `r2`.`Id1` AS `Id10`, `r2`.`Name` AS `Name0`, `r1`.`OptionalReferenceLeaf_Name`, `r1`.`RequiredReferenceLeaf_Name`
        FROM `Root_CollectionTrunk_CollectionBranch` AS `r1`
        LEFT JOIN `Root_CollectionTrunk_CollectionBranch_CollectionLeaf` AS `r2` ON `r1`.`RelationshipsTrunkEntityRelationshipsRootEntityId` = `r2`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~` AND `r1`.`RelationshipsTrunkEntityId1` = `r2`.`RelationshipsBranchEntityRelationshipsTrunkEntityId1` AND `r1`.`Id1` = `r2`.`RelationshipsBranchEntityId1`
    ) AS `s` ON `r0`.`RelationshipsRootEntityId` = `s`.`RelationshipsTrunkEntityRelationshipsRootEntityId` AND `r0`.`Id1` = `s`.`RelationshipsTrunkEntityId1`)
    LEFT JOIN `Root_CollectionTrunk_OptionalReferenceBranch_CollectionLeaf` AS `r3` ON IIF(`r0`.`OptionalReferenceBranch_Name` IS NOT NULL, `r0`.`RelationshipsRootEntityId`, NULL) = `r3`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~` AND IIF(`r0`.`OptionalReferenceBranch_Name` IS NOT NULL, `r0`.`Id1`, NULL) = `r3`.`RelationshipsBranchEntityRelationshipsTrunkEntityId1`)
    LEFT JOIN `Root_CollectionTrunk_RequiredReferenceBranch_CollectionLeaf` AS `r4` ON `r0`.`RelationshipsRootEntityId` = `r4`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~` AND `r0`.`Id1` = `r4`.`RelationshipsBranchEntityRelationshipsTrunkEntityId1`
) AS `s0` ON `r`.`Id` = `s0`.`RelationshipsRootEntityId`)
LEFT JOIN (
    SELECT `r5`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `r5`.`Id1`, `r5`.`Name`, `r6`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r6`.`RelationshipsBranchEntityId1`, `r6`.`Id1` AS `Id10`, `r6`.`Name` AS `Name0`, `r5`.`OptionalReferenceLeaf_Name`, `r5`.`RequiredReferenceLeaf_Name`
    FROM `Root_OptionalReferenceTrunk_CollectionBranch` AS `r5`
    LEFT JOIN `Root_OptionalReferenceTrunk_CollectionBranch_CollectionLeaf` AS `r6` ON `r5`.`RelationshipsTrunkEntityRelationshipsRootEntityId` = `r6`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~` AND `r5`.`Id1` = `r6`.`RelationshipsBranchEntityId1`
) AS `s1` ON IIF(`r`.`OptionalReferenceTrunk_Name` IS NOT NULL, `r`.`Id`, NULL) = `s1`.`RelationshipsTrunkEntityRelationshipsRootEntityId`)
LEFT JOIN `Root_OptionalReferenceTrunk_OptionalReferenceBranch_CollB7BC1840` AS `r7` ON IIF(`r`.`OptionalReferenceTrunk_OptionalReferenceBranch_Name` IS NOT NULL, `r`.`Id`, NULL) = `r7`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`)
LEFT JOIN `Root_OptionalReferenceTrunk_RequiredReferenceBranch_CollB7BC1840` AS `r8` ON IIF(`r`.`OptionalReferenceTrunk_RequiredReferenceBranch_Name` IS NOT NULL, `r`.`Id`, NULL) = `r8`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`)
LEFT JOIN (
    SELECT `r9`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `r9`.`Id1`, `r9`.`Name`, `r10`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r10`.`RelationshipsBranchEntityId1`, `r10`.`Id1` AS `Id10`, `r10`.`Name` AS `Name0`, `r9`.`OptionalReferenceLeaf_Name`, `r9`.`RequiredReferenceLeaf_Name`
    FROM `Root_RequiredReferenceTrunk_CollectionBranch` AS `r9`
    LEFT JOIN `Root_RequiredReferenceTrunk_CollectionBranch_CollectionLeaf` AS `r10` ON `r9`.`RelationshipsTrunkEntityRelationshipsRootEntityId` = `r10`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~` AND `r9`.`Id1` = `r10`.`RelationshipsBranchEntityId1`
) AS `s2` ON `r`.`Id` = `s2`.`RelationshipsTrunkEntityRelationshipsRootEntityId`)
LEFT JOIN `Root_RequiredReferenceTrunk_OptionalReferenceBranch_CollB7BC1840` AS `r11` ON IIF(`r`.`RequiredReferenceTrunk_OptionalReferenceBranch_Name` IS NOT NULL, `r`.`Id`, NULL) = `r11`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`)
LEFT JOIN `Root_RequiredReferenceTrunk_RequiredReferenceBranch_CollB7BC1840` AS `r12` ON `r`.`Id` = `r12`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`)
LEFT JOIN (
    SELECT `r13`.`RelationshipsRootEntityId`, `r13`.`Id1`, `r13`.`Name`, `s3`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `s3`.`RelationshipsTrunkEntityId1`, `s3`.`Id1` AS `Id10`, `s3`.`Name` AS `Name0`, `s3`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `s3`.`RelationshipsBranchEntityRelationshipsTrunkEntityId1`, `s3`.`RelationshipsBranchEntityId1`, `s3`.`Id10` AS `Id100`, `s3`.`Name0` AS `Name00`, `s3`.`OptionalReferenceLeaf_Name`, `s3`.`RequiredReferenceLeaf_Name`, `r13`.`OptionalReferenceBranch_Name`, `r16`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~` AS `RelationshipsBranchEntityRelationshipsTrunkEntityRelatio5A630C8B`, `r16`.`RelationshipsBranchEntityRelationshipsTrunkEntityId1` AS `RelationshipsBranchEntityRelationshipsTrunkEntityId10`, `r16`.`Id1` AS `Id11`, `r16`.`Name` AS `Name1`, `r13`.`OptionalReferenceBranch_OptionalReferenceLeaf_Name`, `r13`.`OptionalReferenceBranch_RequiredReferenceLeaf_Name`, `r13`.`RequiredReferenceBranch_Name`, `r17`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~` AS `RelationshipsBranchEntityRelationshipsTrunkEntityRelatio3D99058E`, `r17`.`RelationshipsBranchEntityRelationshipsTrunkEntityId1` AS `RelationshipsBranchEntityRelationshipsTrunkEntityId11`, `r17`.`Id1` AS `Id12`, `r17`.`Name` AS `Name2`, `r13`.`RequiredReferenceBranch_OptionalReferenceLeaf_Name`, `r13`.`RequiredReferenceBranch_RequiredReferenceLeaf_Name`
    FROM ((`Root_CollectionTrunk` AS `r13`
    LEFT JOIN (
        SELECT `r14`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `r14`.`RelationshipsTrunkEntityId1`, `r14`.`Id1`, `r14`.`Name`, `r15`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r15`.`RelationshipsBranchEntityRelationshipsTrunkEntityId1`, `r15`.`RelationshipsBranchEntityId1`, `r15`.`Id1` AS `Id10`, `r15`.`Name` AS `Name0`, `r14`.`OptionalReferenceLeaf_Name`, `r14`.`RequiredReferenceLeaf_Name`
        FROM `Root_CollectionTrunk_CollectionBranch` AS `r14`
        LEFT JOIN `Root_CollectionTrunk_CollectionBranch_CollectionLeaf` AS `r15` ON `r14`.`RelationshipsTrunkEntityRelationshipsRootEntityId` = `r15`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~` AND `r14`.`RelationshipsTrunkEntityId1` = `r15`.`RelationshipsBranchEntityRelationshipsTrunkEntityId1` AND `r14`.`Id1` = `r15`.`RelationshipsBranchEntityId1`
    ) AS `s3` ON `r13`.`RelationshipsRootEntityId` = `s3`.`RelationshipsTrunkEntityRelationshipsRootEntityId` AND `r13`.`Id1` = `s3`.`RelationshipsTrunkEntityId1`)
    LEFT JOIN `Root_CollectionTrunk_OptionalReferenceBranch_CollectionLeaf` AS `r16` ON IIF(`r13`.`OptionalReferenceBranch_Name` IS NOT NULL, `r13`.`RelationshipsRootEntityId`, NULL) = `r16`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~` AND IIF(`r13`.`OptionalReferenceBranch_Name` IS NOT NULL, `r13`.`Id1`, NULL) = `r16`.`RelationshipsBranchEntityRelationshipsTrunkEntityId1`)
    LEFT JOIN `Root_CollectionTrunk_RequiredReferenceBranch_CollectionLeaf` AS `r17` ON `r13`.`RelationshipsRootEntityId` = `r17`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~` AND `r13`.`Id1` = `r17`.`RelationshipsBranchEntityRelationshipsTrunkEntityId1`
) AS `s4` ON `r`.`Id` = `s4`.`RelationshipsRootEntityId`)
LEFT JOIN (
    SELECT `r18`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `r18`.`Id1`, `r18`.`Name`, `r19`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r19`.`RelationshipsBranchEntityId1`, `r19`.`Id1` AS `Id10`, `r19`.`Name` AS `Name0`, `r18`.`OptionalReferenceLeaf_Name`, `r18`.`RequiredReferenceLeaf_Name`
    FROM `Root_OptionalReferenceTrunk_CollectionBranch` AS `r18`
    LEFT JOIN `Root_OptionalReferenceTrunk_CollectionBranch_CollectionLeaf` AS `r19` ON `r18`.`RelationshipsTrunkEntityRelationshipsRootEntityId` = `r19`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~` AND `r18`.`Id1` = `r19`.`RelationshipsBranchEntityId1`
) AS `s5` ON IIF(`r`.`OptionalReferenceTrunk_Name` IS NOT NULL, `r`.`Id`, NULL) = `s5`.`RelationshipsTrunkEntityRelationshipsRootEntityId`)
LEFT JOIN `Root_OptionalReferenceTrunk_OptionalReferenceBranch_CollB7BC1840` AS `r20` ON IIF(`r`.`OptionalReferenceTrunk_OptionalReferenceBranch_Name` IS NOT NULL, `r`.`Id`, NULL) = `r20`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`)
LEFT JOIN `Root_OptionalReferenceTrunk_RequiredReferenceBranch_CollB7BC1840` AS `r21` ON IIF(`r`.`OptionalReferenceTrunk_RequiredReferenceBranch_Name` IS NOT NULL, `r`.`Id`, NULL) = `r21`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`)
LEFT JOIN (
    SELECT `r22`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `r22`.`Id1`, `r22`.`Name`, `r23`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r23`.`RelationshipsBranchEntityId1`, `r23`.`Id1` AS `Id10`, `r23`.`Name` AS `Name0`, `r22`.`OptionalReferenceLeaf_Name`, `r22`.`RequiredReferenceLeaf_Name`
    FROM `Root_RequiredReferenceTrunk_CollectionBranch` AS `r22`
    LEFT JOIN `Root_RequiredReferenceTrunk_CollectionBranch_CollectionLeaf` AS `r23` ON `r22`.`RelationshipsTrunkEntityRelationshipsRootEntityId` = `r23`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~` AND `r22`.`Id1` = `r23`.`RelationshipsBranchEntityId1`
) AS `s6` ON `r`.`Id` = `s6`.`RelationshipsTrunkEntityRelationshipsRootEntityId`)
LEFT JOIN `Root_RequiredReferenceTrunk_OptionalReferenceBranch_CollB7BC1840` AS `r24` ON IIF(`r`.`RequiredReferenceTrunk_OptionalReferenceBranch_Name` IS NOT NULL, `r`.`Id`, NULL) = `r24`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`)
LEFT JOIN `Root_RequiredReferenceTrunk_RequiredReferenceBranch_CollB7BC1840` AS `r25` ON `r`.`Id` = `r25`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`
ORDER BY `r`.`Id`, `s0`.`RelationshipsRootEntityId`, `s0`.`Id1`, `s0`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `s0`.`RelationshipsTrunkEntityId1`, `s0`.`Id10`, `s0`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `s0`.`RelationshipsBranchEntityRelationshipsTrunkEntityId1`, `s0`.`RelationshipsBranchEntityId1`, `s0`.`Id100`, `s0`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelatio5A630C8B`, `s0`.`RelationshipsBranchEntityRelationshipsTrunkEntityId10`, `s0`.`Id11`, `s0`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelatio3D99058E`, `s0`.`RelationshipsBranchEntityRelationshipsTrunkEntityId11`, `s0`.`Id12`, `s1`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `s1`.`Id1`, `s1`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `s1`.`RelationshipsBranchEntityId1`, `s1`.`Id10`, `r7`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r7`.`Id1`, `r8`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r8`.`Id1`, `s2`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `s2`.`Id1`, `s2`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `s2`.`RelationshipsBranchEntityId1`, `s2`.`Id10`, `r11`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r11`.`Id1`, `r12`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r12`.`Id1`, `s4`.`RelationshipsRootEntityId`, `s4`.`Id1`, `s4`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `s4`.`RelationshipsTrunkEntityId1`, `s4`.`Id10`, `s4`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `s4`.`RelationshipsBranchEntityRelationshipsTrunkEntityId1`, `s4`.`RelationshipsBranchEntityId1`, `s4`.`Id100`, `s4`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelatio5A630C8B`, `s4`.`RelationshipsBranchEntityRelationshipsTrunkEntityId10`, `s4`.`Id11`, `s4`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelatio3D99058E`, `s4`.`RelationshipsBranchEntityRelationshipsTrunkEntityId11`, `s4`.`Id12`, `s5`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `s5`.`Id1`, `s5`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `s5`.`RelationshipsBranchEntityId1`, `s5`.`Id10`, `r20`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r20`.`Id1`, `r21`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r21`.`Id1`, `s6`.`RelationshipsTrunkEntityRelationshipsRootEntityId`, `s6`.`Id1`, `s6`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `s6`.`RelationshipsBranchEntityId1`, `s6`.`Id10`, `r24`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`, `r24`.`Id1`, `r25`.`RelationshipsBranchEntityRelationshipsTrunkEntityRelationshipsR~`
""");
    }

    public override Task Select_trunk_optional(bool async)
        => AssertCantTrackOwned(() => base.Select_trunk_optional(async));

    public override Task Select_trunk_required(bool async)
        => AssertCantTrackOwned(() => base.Select_trunk_required(async));

    public override Task Select_branch_required_required(bool async)
        => AssertCantTrackOwned(() => base.Select_branch_required_required(async));

    public override Task Select_branch_required_optional(bool async)
        => AssertCantTrackOwned(() => base.Select_branch_required_optional(async));

    public override Task Select_branch_optional_required(bool async)
        => AssertCantTrackOwned(() => base.Select_branch_optional_required(async));

    public override Task Select_branch_optional_optional(bool async)
        => AssertCantTrackOwned(() => base.Select_branch_optional_optional(async));

    public override Task Select_trunk_and_branch_duplicated(bool async)
        => AssertCantTrackOwned(() => base.Select_trunk_and_branch_duplicated(async));

    public override Task Select_trunk_and_trunk_duplicated(bool async)
        => AssertCantTrackOwned(() => base.Select_trunk_and_trunk_duplicated(async));

    public override Task Select_leaf_trunk_root(bool async)
        => AssertCantTrackOwned(() => base.Select_leaf_trunk_root(async));

    public override Task Select_subquery_root_set_required_trunk_FirstOrDefault_branch(bool async)
        => AssertCantTrackOwned(() => base.Select_subquery_root_set_required_trunk_FirstOrDefault_branch(async));

    public override Task Select_subquery_root_set_optional_trunk_FirstOrDefault_branch(bool async)
        => AssertCantTrackOwned(() => base.Select_subquery_root_set_optional_trunk_FirstOrDefault_branch(async));

    private async Task AssertCantTrackOwned(Func<Task> test)
    {
        var message = (await Assert.ThrowsAsync<InvalidOperationException>(test)).Message;

        Assert.Equal(CoreStrings.OwnedEntitiesCannotBeTrackedWithoutTheirOwner, message);
        AssertSql();
    }

    [ConditionalFact]
    public virtual void Check_all_tests_overridden()
        => TestHelpers.AssertAllMethodsOverridden(GetType());

    private void AssertSql(params string[] expected)
        => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);
}
