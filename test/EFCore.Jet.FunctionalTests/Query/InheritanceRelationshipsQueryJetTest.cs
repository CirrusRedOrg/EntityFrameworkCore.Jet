// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Query;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query
{
    public class InheritanceRelationshipsQueryJetTest
        : InheritanceRelationshipsQueryTestBase<InheritanceRelationshipsQueryJetFixture>
    {
        public InheritanceRelationshipsQueryJetTest(
            InheritanceRelationshipsQueryJetFixture fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            fixture.TestSqlLoggerFactory.Clear();
        }
        
        public override async Task Include_reference_with_inheritance_reverse(bool async)
        {
            await base.Include_reference_with_inheritance_reverse(async);

            AssertSql(
                $@"SELECT `b`.`Id`, `b`.`BaseParentId`, `b`.`Discriminator`, `b`.`Name`, `t`.`Id`, `t`.`Discriminator`, `t`.`Name`, `t`.`BaseId`
FROM `BaseReferencesOnBase` AS `b`
LEFT JOIN (
    SELECT `b0`.`Id`, `b0`.`Discriminator`, `b0`.`Name`, `b0`.`BaseId`
    FROM `BaseEntities` AS `b0`
    WHERE `b0`.`Discriminator` IN ('BaseInheritanceRelationshipEntity', 'DerivedInheritanceRelationshipEntity')
) AS `t` ON `b`.`BaseParentId` = `t`.`Id`
WHERE `b`.`Discriminator` IN ('BaseReferenceOnBase', 'DerivedReferenceOnBase')");
        }

        public override async Task Include_self_reference_with_inheritance(bool async)
        {
            await base.Include_self_reference_with_inheritance(async);

            AssertSql(
                $@"SELECT `b`.`Id`, `b`.`Discriminator`, `b`.`Name`, `b`.`BaseId`, `t`.`Id`, `t`.`Discriminator`, `t`.`Name`, `t`.`BaseId`
FROM `BaseEntities` AS `b`
LEFT JOIN (
    SELECT `b0`.`Id`, `b0`.`Discriminator`, `b0`.`Name`, `b0`.`BaseId`
    FROM `BaseEntities` AS `b0`
    WHERE `b0`.`Discriminator` = 'DerivedInheritanceRelationshipEntity'
) AS `t` ON `b`.`Id` = `t`.`BaseId`
WHERE `b`.`Discriminator` IN ('BaseInheritanceRelationshipEntity', 'DerivedInheritanceRelationshipEntity')");
        }

        public override async Task Include_self_reference_with_inheritance_reverse(bool async)
        {
            await base.Include_self_reference_with_inheritance_reverse(async);

            AssertSql(
                $@"SELECT `b`.`Id`, `b`.`Discriminator`, `b`.`Name`, `b`.`BaseId`, `t`.`Id`, `t`.`Discriminator`, `t`.`Name`, `t`.`BaseId`
FROM `BaseEntities` AS `b`
LEFT JOIN (
    SELECT `b0`.`Id`, `b0`.`Discriminator`, `b0`.`Name`, `b0`.`BaseId`
    FROM `BaseEntities` AS `b0`
    WHERE `b0`.`Discriminator` IN ('BaseInheritanceRelationshipEntity', 'DerivedInheritanceRelationshipEntity')
) AS `t` ON `b`.`BaseId` = `t`.`Id`
WHERE `b`.`Discriminator` = 'DerivedInheritanceRelationshipEntity'");
        }
        
        public override async Task Include_reference_with_inheritance_with_filter_reverse(bool async)
        {
            await base.Include_reference_with_inheritance_with_filter_reverse(async);

            AssertSql(
                $@"SELECT `b`.`Id`, `b`.`BaseParentId`, `b`.`Discriminator`, `b`.`Name`, `t`.`Id`, `t`.`Discriminator`, `t`.`Name`, `t`.`BaseId`
FROM `BaseReferencesOnBase` AS `b`
LEFT JOIN (
    SELECT `b0`.`Id`, `b0`.`Discriminator`, `b0`.`Name`, `b0`.`BaseId`
    FROM `BaseEntities` AS `b0`
    WHERE `b0`.`Discriminator` IN ('BaseInheritanceRelationshipEntity', 'DerivedInheritanceRelationshipEntity')
) AS `t` ON `b`.`BaseParentId` = `t`.`Id`
WHERE `b`.`Discriminator` IN ('BaseReferenceOnBase', 'DerivedReferenceOnBase') AND ((`b`.`Name` <> 'Bar') OR `b`.`Name` IS NULL)");
        }

        public override async Task Include_reference_without_inheritance(bool async)
        {
            await base.Include_reference_without_inheritance(async);

            AssertSql(
                $@"SELECT `b`.`Id`, `b`.`Discriminator`, `b`.`Name`, `b`.`BaseId`, `r`.`Id`, `r`.`Name`, `r`.`ParentId`
FROM `BaseEntities` AS `b`
LEFT JOIN `ReferencesOnBase` AS `r` ON `b`.`Id` = `r`.`ParentId`
WHERE `b`.`Discriminator` IN ('BaseInheritanceRelationshipEntity', 'DerivedInheritanceRelationshipEntity')");
        }

        public override async Task Include_reference_without_inheritance_reverse(bool async)
        {
            await base.Include_reference_without_inheritance_reverse(async);

            AssertSql(
                $@"SELECT `r`.`Id`, `r`.`Name`, `r`.`ParentId`, `t`.`Id`, `t`.`Discriminator`, `t`.`Name`, `t`.`BaseId`
FROM `ReferencesOnBase` AS `r`
LEFT JOIN (
    SELECT `b`.`Id`, `b`.`Discriminator`, `b`.`Name`, `b`.`BaseId`
    FROM `BaseEntities` AS `b`
    WHERE `b`.`Discriminator` IN ('BaseInheritanceRelationshipEntity', 'DerivedInheritanceRelationshipEntity')
) AS `t` ON `r`.`ParentId` = `t`.`Id`");
        }

        public override async Task Include_reference_without_inheritance_with_filter(bool async)
        {
            await base.Include_reference_without_inheritance_with_filter(async);

            AssertSql(
                $@"SELECT `b`.`Id`, `b`.`Discriminator`, `b`.`Name`, `b`.`BaseId`, `r`.`Id`, `r`.`Name`, `r`.`ParentId`
FROM `BaseEntities` AS `b`
LEFT JOIN `ReferencesOnBase` AS `r` ON `b`.`Id` = `r`.`ParentId`
WHERE `b`.`Discriminator` IN ('BaseInheritanceRelationshipEntity', 'DerivedInheritanceRelationshipEntity') AND ((`b`.`Name` <> 'Bar') OR `b`.`Name` IS NULL)");
        }

        public override async Task Include_reference_without_inheritance_with_filter_reverse(bool async)
        {
            await base.Include_reference_without_inheritance_with_filter_reverse(async);

            AssertSql(
                $@"SELECT `r`.`Id`, `r`.`Name`, `r`.`ParentId`, `t`.`Id`, `t`.`Discriminator`, `t`.`Name`, `t`.`BaseId`
FROM `ReferencesOnBase` AS `r`
LEFT JOIN (
    SELECT `b`.`Id`, `b`.`Discriminator`, `b`.`Name`, `b`.`BaseId`
    FROM `BaseEntities` AS `b`
    WHERE `b`.`Discriminator` IN ('BaseInheritanceRelationshipEntity', 'DerivedInheritanceRelationshipEntity')
) AS `t` ON `r`.`ParentId` = `t`.`Id`
WHERE (`r`.`Name` <> 'Bar') OR `r`.`Name` IS NULL");
        }
        
        public override async Task Include_collection_with_inheritance_reverse(bool async)
        {
            await base.Include_collection_with_inheritance_reverse(async);

            AssertSql(
                $@"SELECT `b`.`Id`, `b`.`BaseParentId`, `b`.`Discriminator`, `b`.`Name`, `b`.`DerivedProperty`, `t`.`Id`, `t`.`Discriminator`, `t`.`Name`, `t`.`BaseId`
FROM `BaseCollectionsOnBase` AS `b`
LEFT JOIN (
    SELECT `b0`.`Id`, `b0`.`Discriminator`, `b0`.`Name`, `b0`.`BaseId`
    FROM `BaseEntities` AS `b0`
    WHERE `b0`.`Discriminator` IN ('BaseInheritanceRelationshipEntity', 'DerivedInheritanceRelationshipEntity')
) AS `t` ON `b`.`BaseParentId` = `t`.`Id`
WHERE `b`.`Discriminator` IN ('BaseCollectionOnBase', 'DerivedCollectionOnBase')");
        }
        
        public override async Task Include_collection_with_inheritance_with_filter_reverse(bool async)
        {
            await base.Include_collection_with_inheritance_with_filter_reverse(async);

            AssertSql(
                $@"SELECT `b`.`Id`, `b`.`BaseParentId`, `b`.`Discriminator`, `b`.`Name`, `b`.`DerivedProperty`, `t`.`Id`, `t`.`Discriminator`, `t`.`Name`, `t`.`BaseId`
FROM `BaseCollectionsOnBase` AS `b`
LEFT JOIN (
    SELECT `b0`.`Id`, `b0`.`Discriminator`, `b0`.`Name`, `b0`.`BaseId`
    FROM `BaseEntities` AS `b0`
    WHERE `b0`.`Discriminator` IN ('BaseInheritanceRelationshipEntity', 'DerivedInheritanceRelationshipEntity')
) AS `t` ON `b`.`BaseParentId` = `t`.`Id`
WHERE `b`.`Discriminator` IN ('BaseCollectionOnBase', 'DerivedCollectionOnBase') AND ((`b`.`Name` <> 'Bar') OR `b`.`Name` IS NULL)");
        }

        public override async Task Include_collection_without_inheritance(bool async)
        {
            await base.Include_collection_without_inheritance(async);

            AssertSql(
                $@"SELECT `b`.`Id`, `b`.`Discriminator`, `b`.`Name`, `b`.`BaseId`, `c`.`Id`, `c`.`Name`, `c`.`ParentId`
FROM `BaseEntities` AS `b`
LEFT JOIN `CollectionsOnBase` AS `c` ON `b`.`Id` = `c`.`ParentId`
WHERE `b`.`Discriminator` IN ('BaseInheritanceRelationshipEntity', 'DerivedInheritanceRelationshipEntity')
ORDER BY `b`.`Id`, `c`.`Id`");
        }

        public override async Task Include_collection_without_inheritance_reverse(bool async)
        {
            await base.Include_collection_without_inheritance_reverse(async);

            AssertSql(
                $@"SELECT `c`.`Id`, `c`.`Name`, `c`.`ParentId`, `t`.`Id`, `t`.`Discriminator`, `t`.`Name`, `t`.`BaseId`
FROM `CollectionsOnBase` AS `c`
LEFT JOIN (
    SELECT `b`.`Id`, `b`.`Discriminator`, `b`.`Name`, `b`.`BaseId`
    FROM `BaseEntities` AS `b`
    WHERE `b`.`Discriminator` IN ('BaseInheritanceRelationshipEntity', 'DerivedInheritanceRelationshipEntity')
) AS `t` ON `c`.`ParentId` = `t`.`Id`");
        }

        public override async Task Include_collection_without_inheritance_with_filter(bool async)
        {
            await base.Include_collection_without_inheritance_with_filter(async);

            AssertSql(
                $@"SELECT `b`.`Id`, `b`.`Discriminator`, `b`.`Name`, `b`.`BaseId`, `c`.`Id`, `c`.`Name`, `c`.`ParentId`
FROM `BaseEntities` AS `b`
LEFT JOIN `CollectionsOnBase` AS `c` ON `b`.`Id` = `c`.`ParentId`
WHERE `b`.`Discriminator` IN ('BaseInheritanceRelationshipEntity', 'DerivedInheritanceRelationshipEntity') AND ((`b`.`Name` <> 'Bar') OR `b`.`Name` IS NULL)
ORDER BY `b`.`Id`, `c`.`Id`");
        }

        public override async Task Include_collection_without_inheritance_with_filter_reverse(bool async)
        {
            await base.Include_collection_without_inheritance_with_filter_reverse(async);

            AssertSql(
                $@"SELECT `c`.`Id`, `c`.`Name`, `c`.`ParentId`, `t`.`Id`, `t`.`Discriminator`, `t`.`Name`, `t`.`BaseId`
FROM `CollectionsOnBase` AS `c`
LEFT JOIN (
    SELECT `b`.`Id`, `b`.`Discriminator`, `b`.`Name`, `b`.`BaseId`
    FROM `BaseEntities` AS `b`
    WHERE `b`.`Discriminator` IN ('BaseInheritanceRelationshipEntity', 'DerivedInheritanceRelationshipEntity')
) AS `t` ON `c`.`ParentId` = `t`.`Id`
WHERE (`c`.`Name` <> 'Bar') OR `c`.`Name` IS NULL");
        }

        public override async Task Include_reference_with_inheritance_on_derived1(bool async)
        {
            await base.Include_reference_with_inheritance_on_derived1(async);

            AssertSql(
                $@"SELECT `b`.`Id`, `b`.`Discriminator`, `b`.`Name`, `b`.`BaseId`, `t`.`Id`, `t`.`BaseParentId`, `t`.`Discriminator`, `t`.`Name`
FROM `BaseEntities` AS `b`
LEFT JOIN (
    SELECT `b0`.`Id`, `b0`.`BaseParentId`, `b0`.`Discriminator`, `b0`.`Name`
    FROM `BaseReferencesOnBase` AS `b0`
    WHERE `b0`.`Discriminator` IN ('BaseReferenceOnBase', 'DerivedReferenceOnBase')
) AS `t` ON `b`.`Id` = `t`.`BaseParentId`
WHERE `b`.`Discriminator` = 'DerivedInheritanceRelationshipEntity'");
        }

        public override async Task Include_reference_with_inheritance_on_derived2(bool async)
        {
            await base.Include_reference_with_inheritance_on_derived2(async);

            AssertSql(
                $@"SELECT `b`.`Id`, `b`.`Discriminator`, `b`.`Name`, `b`.`BaseId`, `t`.`Id`, `t`.`BaseParentId`, `t`.`Discriminator`, `t`.`Name`, `t`.`DerivedInheritanceRelationshipEntityId`
FROM `BaseEntities` AS `b`
LEFT JOIN (
    SELECT `b0`.`Id`, `b0`.`BaseParentId`, `b0`.`Discriminator`, `b0`.`Name`, `b0`.`DerivedInheritanceRelationshipEntityId`
    FROM `BaseReferencesOnDerived` AS `b0`
    WHERE `b0`.`Discriminator` IN ('BaseReferenceOnDerived', 'DerivedReferenceOnDerived')
) AS `t` ON `b`.`Id` = `t`.`BaseParentId`
WHERE `b`.`Discriminator` = 'DerivedInheritanceRelationshipEntity'");
        }

        public override async Task Include_reference_with_inheritance_on_derived4(bool async)
        {
            await base.Include_reference_with_inheritance_on_derived4(async);

            AssertSql(
                $@"SELECT `b`.`Id`, `b`.`Discriminator`, `b`.`Name`, `b`.`BaseId`, `t`.`Id`, `t`.`BaseParentId`, `t`.`Discriminator`, `t`.`Name`, `t`.`DerivedInheritanceRelationshipEntityId`
FROM `BaseEntities` AS `b`
LEFT JOIN (
    SELECT `b0`.`Id`, `b0`.`BaseParentId`, `b0`.`Discriminator`, `b0`.`Name`, `b0`.`DerivedInheritanceRelationshipEntityId`
    FROM `BaseReferencesOnDerived` AS `b0`
    WHERE `b0`.`Discriminator` = 'DerivedReferenceOnDerived'
) AS `t` ON `b`.`Id` = `t`.`DerivedInheritanceRelationshipEntityId`
WHERE `b`.`Discriminator` = 'DerivedInheritanceRelationshipEntity'");
        }

        public override async Task Include_reference_with_inheritance_on_derived_reverse(bool async)
        {
            await base.Include_reference_with_inheritance_on_derived_reverse(async);

            AssertSql(
                $@"SELECT `b`.`Id`, `b`.`BaseParentId`, `b`.`Discriminator`, `b`.`Name`, `b`.`DerivedInheritanceRelationshipEntityId`, `t`.`Id`, `t`.`Discriminator`, `t`.`Name`, `t`.`BaseId`
FROM `BaseReferencesOnDerived` AS `b`
LEFT JOIN (
    SELECT `b0`.`Id`, `b0`.`Discriminator`, `b0`.`Name`, `b0`.`BaseId`
    FROM `BaseEntities` AS `b0`
    WHERE `b0`.`Discriminator` = 'DerivedInheritanceRelationshipEntity'
) AS `t` ON `b`.`BaseParentId` = `t`.`Id`
WHERE `b`.`Discriminator` IN ('BaseReferenceOnDerived', 'DerivedReferenceOnDerived')");
        }

        public override async Task Include_reference_with_inheritance_on_derived_with_filter1(bool async)
        {
            await base.Include_reference_with_inheritance_on_derived_with_filter1(async);

            AssertSql(
                $@"SELECT `b`.`Id`, `b`.`Discriminator`, `b`.`Name`, `b`.`BaseId`, `t`.`Id`, `t`.`BaseParentId`, `t`.`Discriminator`, `t`.`Name`
FROM `BaseEntities` AS `b`
LEFT JOIN (
    SELECT `b0`.`Id`, `b0`.`BaseParentId`, `b0`.`Discriminator`, `b0`.`Name`
    FROM `BaseReferencesOnBase` AS `b0`
    WHERE `b0`.`Discriminator` IN ('BaseReferenceOnBase', 'DerivedReferenceOnBase')
) AS `t` ON `b`.`Id` = `t`.`BaseParentId`
WHERE (`b`.`Discriminator` = 'DerivedInheritanceRelationshipEntity') AND ((`b`.`Name` <> 'Bar') OR `b`.`Name` IS NULL)");
        }

        public override async Task Include_reference_with_inheritance_on_derived_with_filter2(bool async)
        {
            await base.Include_reference_with_inheritance_on_derived_with_filter2(async);

            AssertSql(
                $@"SELECT `b`.`Id`, `b`.`Discriminator`, `b`.`Name`, `b`.`BaseId`, `t`.`Id`, `t`.`BaseParentId`, `t`.`Discriminator`, `t`.`Name`, `t`.`DerivedInheritanceRelationshipEntityId`
FROM `BaseEntities` AS `b`
LEFT JOIN (
    SELECT `b0`.`Id`, `b0`.`BaseParentId`, `b0`.`Discriminator`, `b0`.`Name`, `b0`.`DerivedInheritanceRelationshipEntityId`
    FROM `BaseReferencesOnDerived` AS `b0`
    WHERE `b0`.`Discriminator` IN ('BaseReferenceOnDerived', 'DerivedReferenceOnDerived')
) AS `t` ON `b`.`Id` = `t`.`BaseParentId`
WHERE (`b`.`Discriminator` = 'DerivedInheritanceRelationshipEntity') AND ((`b`.`Name` <> 'Bar') OR `b`.`Name` IS NULL)");
        }

        public override async Task Include_reference_with_inheritance_on_derived_with_filter4(bool async)
        {
            await base.Include_reference_with_inheritance_on_derived_with_filter4(async);

            AssertSql(
                $@"SELECT `b`.`Id`, `b`.`Discriminator`, `b`.`Name`, `b`.`BaseId`, `t`.`Id`, `t`.`BaseParentId`, `t`.`Discriminator`, `t`.`Name`, `t`.`DerivedInheritanceRelationshipEntityId`
FROM `BaseEntities` AS `b`
LEFT JOIN (
    SELECT `b0`.`Id`, `b0`.`BaseParentId`, `b0`.`Discriminator`, `b0`.`Name`, `b0`.`DerivedInheritanceRelationshipEntityId`
    FROM `BaseReferencesOnDerived` AS `b0`
    WHERE `b0`.`Discriminator` = 'DerivedReferenceOnDerived'
) AS `t` ON `b`.`Id` = `t`.`DerivedInheritanceRelationshipEntityId`
WHERE (`b`.`Discriminator` = 'DerivedInheritanceRelationshipEntity') AND ((`b`.`Name` <> 'Bar') OR `b`.`Name` IS NULL)");
        }

        public override async Task Include_reference_with_inheritance_on_derived_with_filter_reverse(bool async)
        {
            await base.Include_reference_with_inheritance_on_derived_with_filter_reverse(async);

            AssertSql(
                $@"SELECT `b`.`Id`, `b`.`BaseParentId`, `b`.`Discriminator`, `b`.`Name`, `b`.`DerivedInheritanceRelationshipEntityId`, `t`.`Id`, `t`.`Discriminator`, `t`.`Name`, `t`.`BaseId`
FROM `BaseReferencesOnDerived` AS `b`
LEFT JOIN (
    SELECT `b0`.`Id`, `b0`.`Discriminator`, `b0`.`Name`, `b0`.`BaseId`
    FROM `BaseEntities` AS `b0`
    WHERE `b0`.`Discriminator` = 'DerivedInheritanceRelationshipEntity'
) AS `t` ON `b`.`BaseParentId` = `t`.`Id`
WHERE `b`.`Discriminator` IN ('BaseReferenceOnDerived', 'DerivedReferenceOnDerived') AND ((`b`.`Name` <> 'Bar') OR `b`.`Name` IS NULL)");
        }

        public override async Task Include_reference_without_inheritance_on_derived1(bool async)
        {
            await base.Include_reference_without_inheritance_on_derived1(async);

            AssertSql(
                $@"SELECT `b`.`Id`, `b`.`Discriminator`, `b`.`Name`, `b`.`BaseId`, `r`.`Id`, `r`.`Name`, `r`.`ParentId`
FROM `BaseEntities` AS `b`
LEFT JOIN `ReferencesOnBase` AS `r` ON `b`.`Id` = `r`.`ParentId`
WHERE `b`.`Discriminator` = 'DerivedInheritanceRelationshipEntity'");
        }

        public override async Task Include_reference_without_inheritance_on_derived2(bool async)
        {
            await base.Include_reference_without_inheritance_on_derived2(async);

            AssertSql(
                $@"SELECT `b`.`Id`, `b`.`Discriminator`, `b`.`Name`, `b`.`BaseId`, `r`.`Id`, `r`.`Name`, `r`.`ParentId`
FROM `BaseEntities` AS `b`
LEFT JOIN `ReferencesOnDerived` AS `r` ON `b`.`Id` = `r`.`ParentId`
WHERE `b`.`Discriminator` = 'DerivedInheritanceRelationshipEntity'");
        }

        public override async Task Include_reference_without_inheritance_on_derived_reverse(bool async)
        {
            await base.Include_reference_without_inheritance_on_derived_reverse(async);

            AssertSql(
                $@"SELECT `r`.`Id`, `r`.`Name`, `r`.`ParentId`, `t`.`Id`, `t`.`Discriminator`, `t`.`Name`, `t`.`BaseId`
FROM `ReferencesOnDerived` AS `r`
LEFT JOIN (
    SELECT `b`.`Id`, `b`.`Discriminator`, `b`.`Name`, `b`.`BaseId`
    FROM `BaseEntities` AS `b`
    WHERE `b`.`Discriminator` = 'DerivedInheritanceRelationshipEntity'
) AS `t` ON `r`.`ParentId` = `t`.`Id`");
        }

        public override async Task Include_collection_with_inheritance_on_derived1(bool async)
        {
            await base.Include_collection_with_inheritance_on_derived1(async);

            AssertSql(
                $@"SELECT `b`.`Id`, `b`.`Discriminator`, `b`.`Name`, `b`.`BaseId`, `t`.`Id`, `t`.`BaseParentId`, `t`.`Discriminator`, `t`.`Name`, `t`.`DerivedProperty`
FROM `BaseEntities` AS `b`
LEFT JOIN (
    SELECT `b0`.`Id`, `b0`.`BaseParentId`, `b0`.`Discriminator`, `b0`.`Name`, `b0`.`DerivedProperty`
    FROM `BaseCollectionsOnBase` AS `b0`
    WHERE `b0`.`Discriminator` IN ('BaseCollectionOnBase', 'DerivedCollectionOnBase')
) AS `t` ON `b`.`Id` = `t`.`BaseParentId`
WHERE `b`.`Discriminator` = 'DerivedInheritanceRelationshipEntity'
ORDER BY `b`.`Id`, `t`.`Id`");
        }

        public override async Task Include_collection_with_inheritance_on_derived2(bool async)
        {
            await base.Include_collection_with_inheritance_on_derived2(async);

            AssertSql(
                $@"SELECT `b`.`Id`, `b`.`Discriminator`, `b`.`Name`, `b`.`BaseId`, `t`.`Id`, `t`.`Discriminator`, `t`.`Name`, `t`.`ParentId`, `t`.`DerivedInheritanceRelationshipEntityId`
FROM `BaseEntities` AS `b`
LEFT JOIN (
    SELECT `b0`.`Id`, `b0`.`Discriminator`, `b0`.`Name`, `b0`.`ParentId`, `b0`.`DerivedInheritanceRelationshipEntityId`
    FROM `BaseCollectionsOnDerived` AS `b0`
    WHERE `b0`.`Discriminator` IN ('BaseCollectionOnDerived', 'DerivedCollectionOnDerived')
) AS `t` ON `b`.`Id` = `t`.`ParentId`
WHERE `b`.`Discriminator` = 'DerivedInheritanceRelationshipEntity'
ORDER BY `b`.`Id`, `t`.`Id`");
        }

        public override async Task Include_collection_with_inheritance_on_derived3(bool async)
        {
            await base.Include_collection_with_inheritance_on_derived3(async);

            AssertSql(
                $@"SELECT `b`.`Id`, `b`.`Discriminator`, `b`.`Name`, `b`.`BaseId`, `t`.`Id`, `t`.`Discriminator`, `t`.`Name`, `t`.`ParentId`, `t`.`DerivedInheritanceRelationshipEntityId`
FROM `BaseEntities` AS `b`
LEFT JOIN (
    SELECT `b0`.`Id`, `b0`.`Discriminator`, `b0`.`Name`, `b0`.`ParentId`, `b0`.`DerivedInheritanceRelationshipEntityId`
    FROM `BaseCollectionsOnDerived` AS `b0`
    WHERE `b0`.`Discriminator` = 'DerivedCollectionOnDerived'
) AS `t` ON `b`.`Id` = `t`.`DerivedInheritanceRelationshipEntityId`
WHERE `b`.`Discriminator` = 'DerivedInheritanceRelationshipEntity'
ORDER BY `b`.`Id`, `t`.`Id`");
        }

        public override async Task Include_collection_with_inheritance_on_derived_reverse(bool async)
        {
            await base.Include_collection_with_inheritance_on_derived_reverse(async);

            AssertSql(
                $@"SELECT `b`.`Id`, `b`.`Discriminator`, `b`.`Name`, `b`.`ParentId`, `b`.`DerivedInheritanceRelationshipEntityId`, `t`.`Id`, `t`.`Discriminator`, `t`.`Name`, `t`.`BaseId`
FROM `BaseCollectionsOnDerived` AS `b`
LEFT JOIN (
    SELECT `b0`.`Id`, `b0`.`Discriminator`, `b0`.`Name`, `b0`.`BaseId`
    FROM `BaseEntities` AS `b0`
    WHERE `b0`.`Discriminator` = 'DerivedInheritanceRelationshipEntity'
) AS `t` ON `b`.`ParentId` = `t`.`Id`
WHERE `b`.`Discriminator` IN ('BaseCollectionOnDerived', 'DerivedCollectionOnDerived')");
        }
        
        public override async Task Nested_include_with_inheritance_reference_reference_reverse(bool async)
        {
            await base.Nested_include_with_inheritance_reference_reference_reverse(async);

            AssertSql(
                $@"SELECT `n`.`Id`, `n`.`Discriminator`, `n`.`Name`, `n`.`ParentCollectionId`, `n`.`ParentReferenceId`, `t`.`Id`, `t`.`BaseParentId`, `t`.`Discriminator`, `t`.`Name`, `t0`.`Id`, `t0`.`Discriminator`, `t0`.`Name`, `t0`.`BaseId`
FROM `NestedReferences` AS `n`
LEFT JOIN (
    SELECT `b`.`Id`, `b`.`BaseParentId`, `b`.`Discriminator`, `b`.`Name`
    FROM `BaseReferencesOnBase` AS `b`
    WHERE `b`.`Discriminator` IN ('BaseReferenceOnBase', 'DerivedReferenceOnBase')
) AS `t` ON `n`.`ParentReferenceId` = `t`.`Id`
LEFT JOIN (
    SELECT `b0`.`Id`, `b0`.`Discriminator`, `b0`.`Name`, `b0`.`BaseId`
    FROM `BaseEntities` AS `b0`
    WHERE `b0`.`Discriminator` IN ('BaseInheritanceRelationshipEntity', 'DerivedInheritanceRelationshipEntity')
) AS `t0` ON `t`.`BaseParentId` = `t0`.`Id`
WHERE `n`.`Discriminator` IN ('NestedReferenceBase', 'NestedReferenceDerived')");
        }
        
        public override async Task Nested_include_with_inheritance_reference_collection_reverse(bool async)
        {
            await base.Nested_include_with_inheritance_reference_collection_reverse(async);

            AssertSql(
                $@"SELECT `n`.`Id`, `n`.`Discriminator`, `n`.`Name`, `n`.`ParentCollectionId`, `n`.`ParentReferenceId`, `t`.`Id`, `t`.`BaseParentId`, `t`.`Discriminator`, `t`.`Name`, `t0`.`Id`, `t0`.`Discriminator`, `t0`.`Name`, `t0`.`BaseId`
FROM `NestedCollections` AS `n`
LEFT JOIN (
    SELECT `b`.`Id`, `b`.`BaseParentId`, `b`.`Discriminator`, `b`.`Name`
    FROM `BaseReferencesOnBase` AS `b`
    WHERE `b`.`Discriminator` IN ('BaseReferenceOnBase', 'DerivedReferenceOnBase')
) AS `t` ON `n`.`ParentReferenceId` = `t`.`Id`
LEFT JOIN (
    SELECT `b0`.`Id`, `b0`.`Discriminator`, `b0`.`Name`, `b0`.`BaseId`
    FROM `BaseEntities` AS `b0`
    WHERE `b0`.`Discriminator` IN ('BaseInheritanceRelationshipEntity', 'DerivedInheritanceRelationshipEntity')
) AS `t0` ON `t`.`BaseParentId` = `t0`.`Id`
WHERE `n`.`Discriminator` IN ('NestedCollectionBase', 'NestedCollectionDerived')");
        }
        
        public override async Task Nested_include_with_inheritance_collection_reference_reverse(bool async)
        {
            await base.Nested_include_with_inheritance_collection_reference_reverse(async);

            AssertSql(
                $@"SELECT `n`.`Id`, `n`.`Discriminator`, `n`.`Name`, `n`.`ParentCollectionId`, `n`.`ParentReferenceId`, `t`.`Id`, `t`.`BaseParentId`, `t`.`Discriminator`, `t`.`Name`, `t`.`DerivedProperty`, `t0`.`Id`, `t0`.`Discriminator`, `t0`.`Name`, `t0`.`BaseId`
FROM `NestedReferences` AS `n`
LEFT JOIN (
    SELECT `b`.`Id`, `b`.`BaseParentId`, `b`.`Discriminator`, `b`.`Name`, `b`.`DerivedProperty`
    FROM `BaseCollectionsOnBase` AS `b`
    WHERE `b`.`Discriminator` IN ('BaseCollectionOnBase', 'DerivedCollectionOnBase')
) AS `t` ON `n`.`ParentCollectionId` = `t`.`Id`
LEFT JOIN (
    SELECT `b0`.`Id`, `b0`.`Discriminator`, `b0`.`Name`, `b0`.`BaseId`
    FROM `BaseEntities` AS `b0`
    WHERE `b0`.`Discriminator` IN ('BaseInheritanceRelationshipEntity', 'DerivedInheritanceRelationshipEntity')
) AS `t0` ON `t`.`BaseParentId` = `t0`.`Id`
WHERE `n`.`Discriminator` IN ('NestedReferenceBase', 'NestedReferenceDerived')");
        }
        
        public override async Task Nested_include_with_inheritance_collection_collection_reverse(bool async)
        {
            await base.Nested_include_with_inheritance_collection_collection_reverse(async);

            AssertSql(
                $@"SELECT `n`.`Id`, `n`.`Discriminator`, `n`.`Name`, `n`.`ParentCollectionId`, `n`.`ParentReferenceId`, `t`.`Id`, `t`.`BaseParentId`, `t`.`Discriminator`, `t`.`Name`, `t`.`DerivedProperty`, `t0`.`Id`, `t0`.`Discriminator`, `t0`.`Name`, `t0`.`BaseId`
FROM `NestedCollections` AS `n`
LEFT JOIN (
    SELECT `b`.`Id`, `b`.`BaseParentId`, `b`.`Discriminator`, `b`.`Name`, `b`.`DerivedProperty`
    FROM `BaseCollectionsOnBase` AS `b`
    WHERE `b`.`Discriminator` IN ('BaseCollectionOnBase', 'DerivedCollectionOnBase')
) AS `t` ON `n`.`ParentCollectionId` = `t`.`Id`
LEFT JOIN (
    SELECT `b0`.`Id`, `b0`.`Discriminator`, `b0`.`Name`, `b0`.`BaseId`
    FROM `BaseEntities` AS `b0`
    WHERE `b0`.`Discriminator` IN ('BaseInheritanceRelationshipEntity', 'DerivedInheritanceRelationshipEntity')
) AS `t0` ON `t`.`BaseParentId` = `t0`.`Id`
WHERE `n`.`Discriminator` IN ('NestedCollectionBase', 'NestedCollectionDerived')");
        }

        public override async Task Nested_include_collection_reference_on_non_entity_base(bool async)
        {
            await base.Nested_include_collection_reference_on_non_entity_base(async);

            AssertSql(
                $@"SELECT `r`.`Id`, `r`.`Name`, `t`.`Id`, `t`.`Name`, `t`.`ReferenceId`, `t`.`ReferencedEntityId`, `t`.`Id0`, `t`.`Name0`
FROM `ReferencedEntities` AS `r`
LEFT JOIN (
    SELECT `p`.`Id`, `p`.`Name`, `p`.`ReferenceId`, `p`.`ReferencedEntityId`, `r0`.`Id` AS `Id0`, `r0`.`Name` AS `Name0`
    FROM `PrincipalEntities` AS `p`
    LEFT JOIN `ReferencedEntities` AS `r0` ON `p`.`ReferenceId` = `r0`.`Id`
) AS `t` ON `r`.`Id` = `t`.`ReferencedEntityId`
ORDER BY `r`.`Id`, `t`.`Id`");
        }

        private void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);
    }
}
