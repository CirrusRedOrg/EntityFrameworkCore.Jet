// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit.Abstractions;
using Microsoft.EntityFrameworkCore.TestModels.TransportationModel;

namespace EntityFrameworkCore.Jet.FunctionalTests
{
    public class TableSplittingJetTest : TableSplittingTestBase
    {
        public TableSplittingJetTest(ITestOutputHelper testOutputHelper)
            : base(testOutputHelper)
        {
        }

        protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;

        public override async Task Can_use_with_redundant_relationships()
        {
            await base.Can_use_with_redundant_relationships();

            // TODO: `Name` shouldn't be selected multiple times and no joins are needed
            AssertSql(
                $@"SELECT `v`.`Name`, `v`.`Discriminator`, `v`.`SeatingCapacity`, `t0`.`Name`, `t0`.`Operator_Discriminator`, `t0`.`Operator_Name`, `t0`.`LicenseType`, `t3`.`Name`, `t3`.`Type`, `t5`.`Name`, `t5`.`Description`, `t5`.`Engine_Discriminator`, `t9`.`Name`, `t9`.`Capacity`, `t9`.`FuelTank_Discriminator`, `t9`.`FuelType`, `t9`.`GrainGeometry`
FROM `Vehicles` AS `v`
LEFT JOIN (
    SELECT `v0`.`Name`, `v0`.`Operator_Discriminator`, `v0`.`Operator_Name`, `v0`.`LicenseType`, `t`.`Name` AS `Name0`
    FROM `Vehicles` AS `v0`
    INNER JOIN (
        SELECT `v1`.`Name`, `v1`.`Discriminator`, `v1`.`SeatingCapacity`
        FROM `Vehicles` AS `v1`
        WHERE `v1`.`Discriminator` IN ('Vehicle', 'PoweredVehicle')
    ) AS `t` ON `v0`.`Name` = `t`.`Name`
    WHERE `v0`.`Operator_Discriminator` IN ('Operator', 'LicensedOperator')
) AS `t0` ON `v`.`Name` = `t0`.`Name`
LEFT JOIN (
    SELECT `v2`.`Name`, `v2`.`Type`, `t2`.`Name` AS `Name0`, `t2`.`Name0` AS `Name00`
    FROM `Vehicles` AS `v2`
    INNER JOIN (
        SELECT `v3`.`Name`, `v3`.`Operator_Discriminator`, `v3`.`Operator_Name`, `v3`.`LicenseType`, `t1`.`Name` AS `Name0`
        FROM `Vehicles` AS `v3`
        INNER JOIN (
            SELECT `v4`.`Name`, `v4`.`Discriminator`, `v4`.`SeatingCapacity`
            FROM `Vehicles` AS `v4`
            WHERE `v4`.`Discriminator` IN ('Vehicle', 'PoweredVehicle')
        ) AS `t1` ON `v3`.`Name` = `t1`.`Name`
        WHERE `v3`.`Operator_Discriminator` IN ('Operator', 'LicensedOperator')
    ) AS `t2` ON `v2`.`Name` = `t2`.`Name`
    WHERE `v2`.`Type` IS NOT NULL
) AS `t3` ON `t0`.`Name` = `t3`.`Name`
LEFT JOIN (
    SELECT `v5`.`Name`, `v5`.`Description`, `v5`.`Engine_Discriminator`, `t4`.`Name` AS `Name0`
    FROM `Vehicles` AS `v5`
    INNER JOIN (
        SELECT `v6`.`Name`, `v6`.`Discriminator`, `v6`.`SeatingCapacity`
        FROM `Vehicles` AS `v6`
        WHERE `v6`.`Discriminator` = 'PoweredVehicle'
    ) AS `t4` ON `v5`.`Name` = `t4`.`Name`
    WHERE `v5`.`Engine_Discriminator` IN ('Engine', 'ContinuousCombustionEngine', 'IntermittentCombustionEngine', 'SolidRocket')
) AS `t5` ON `v`.`Name` = `t5`.`Name`
LEFT JOIN (
    SELECT `v7`.`Name`, `v7`.`Capacity`, `v7`.`FuelTank_Discriminator`, `v7`.`FuelType`, `v7`.`GrainGeometry`
    FROM `Vehicles` AS `v7`
    INNER JOIN (
        SELECT `v8`.`Name`, `v8`.`Discriminator`, `v8`.`SeatingCapacity`
        FROM `Vehicles` AS `v8`
        WHERE `v8`.`Discriminator` = 'PoweredVehicle'
    ) AS `t6` ON `v7`.`Name` = `t6`.`Name`
    WHERE `v7`.`FuelTank_Discriminator` IN ('FuelTank', 'SolidFuelTank')
    UNION
    SELECT `v9`.`Name`, `v9`.`Capacity`, `v9`.`FuelTank_Discriminator`, `v9`.`FuelType`, `v9`.`GrainGeometry`
    FROM `Vehicles` AS `v9`
    INNER JOIN (
        SELECT `v10`.`Name`, `v10`.`Description`, `v10`.`Engine_Discriminator`, `t7`.`Name` AS `Name0`
        FROM `Vehicles` AS `v10`
        INNER JOIN (
            SELECT `v11`.`Name`, `v11`.`Discriminator`, `v11`.`SeatingCapacity`
            FROM `Vehicles` AS `v11`
            WHERE `v11`.`Discriminator` = 'PoweredVehicle'
        ) AS `t7` ON `v10`.`Name` = `t7`.`Name`
        WHERE `v10`.`Engine_Discriminator` IN ('ContinuousCombustionEngine', 'IntermittentCombustionEngine', 'SolidRocket')
    ) AS `t8` ON `v9`.`Name` = `t8`.`Name`
) AS `t9` ON `t5`.`Name` = `t9`.`Name`
WHERE `v`.`Discriminator` IN ('Vehicle', 'PoweredVehicle')
ORDER BY `v`.`Name`");
        }

        public override async Task Can_query_shared()
        {
            await base.Can_query_shared();

            AssertSql(
                $@"SELECT `v`.`Name`, `v`.`Operator_Discriminator`, `v`.`Operator_Name`, `v`.`LicenseType`
FROM `Vehicles` AS `v`
INNER JOIN (
    SELECT `v0`.`Name`, `v0`.`Discriminator`, `v0`.`SeatingCapacity`
    FROM `Vehicles` AS `v0`
    WHERE `v0`.`Discriminator` IN ('Vehicle', 'PoweredVehicle')
) AS `t` ON `v`.`Name` = `t`.`Name`
WHERE `v`.`Operator_Discriminator` IN ('Operator', 'LicensedOperator')");
        }

        public override async Task Can_query_shared_nonhierarchy()
        {
            await base.Can_query_shared_nonhierarchy();

            AssertSql(
                $@"SELECT `t0`.`Name`, `t0`.`Operator_Name`
FROM (
    SELECT `v`.`Name`, `v`.`Operator_Name`
    FROM `Vehicles` AS `v`
    WHERE `v`.`Operator_Name` IS NOT NULL
    UNION
    SELECT `v0`.`Name`, `v0`.`Operator_Name`
    FROM `Vehicles` AS `v0`
    INNER JOIN (
        SELECT `v1`.`Name`, `v1`.`Type`
        FROM `Vehicles` AS `v1`
        WHERE `v1`.`Type` IS NOT NULL
    ) AS `t` ON `v0`.`Name` = `t`.`Name`
) AS `t0`
INNER JOIN (
    SELECT `v2`.`Name`, `v2`.`Discriminator`, `v2`.`SeatingCapacity`
    FROM `Vehicles` AS `v2`
    WHERE `v2`.`Discriminator` IN ('Vehicle', 'PoweredVehicle')
) AS `t1` ON `t0`.`Name` = `t1`.`Name`");
        }

        public override async Task Can_query_shared_nonhierarchy_with_nonshared_dependent()
        {
            await base.Can_query_shared_nonhierarchy_with_nonshared_dependent();

            AssertSql(
                $@"SELECT `t`.`Name`, `t`.`Operator_Name`
FROM (
    SELECT `v`.`Name`, `v`.`Operator_Name`
    FROM `Vehicles` AS `v`
    WHERE `v`.`Operator_Name` IS NOT NULL
    UNION
    SELECT `v0`.`Name`, `v0`.`Operator_Name`
    FROM `Vehicles` AS `v0`
    INNER JOIN `OperatorDetails` AS `o` ON `v0`.`Name` = `o`.`VehicleName`
) AS `t`
INNER JOIN (
    SELECT `v1`.`Name`, `v1`.`Discriminator`, `v1`.`SeatingCapacity`
    FROM `Vehicles` AS `v1`
    WHERE `v1`.`Discriminator` IN ('Vehicle', 'PoweredVehicle')
) AS `t0` ON `t`.`Name` = `t0`.`Name`");
        }

        public override async Task Can_query_shared_derived_hierarchy()
        {
            await base.Can_query_shared_derived_hierarchy();

            AssertSql(
                $@"SELECT `v`.`Name`, `v`.`Capacity`, `v`.`FuelTank_Discriminator`, `v`.`FuelType`, `v`.`GrainGeometry`
FROM `Vehicles` AS `v`
INNER JOIN (
    SELECT `v0`.`Name`, `v0`.`Discriminator`, `v0`.`SeatingCapacity`
    FROM `Vehicles` AS `v0`
    WHERE `v0`.`Discriminator` = 'PoweredVehicle'
) AS `t` ON `v`.`Name` = `t`.`Name`
WHERE `v`.`FuelTank_Discriminator` IN ('FuelTank', 'SolidFuelTank')
UNION
SELECT `v1`.`Name`, `v1`.`Capacity`, `v1`.`FuelTank_Discriminator`, `v1`.`FuelType`, `v1`.`GrainGeometry`
FROM `Vehicles` AS `v1`
INNER JOIN (
    SELECT `v2`.`Name`, `v2`.`Description`, `v2`.`Engine_Discriminator`, `t0`.`Name` AS `Name0`
    FROM `Vehicles` AS `v2`
    INNER JOIN (
        SELECT `v3`.`Name`, `v3`.`Discriminator`, `v3`.`SeatingCapacity`
        FROM `Vehicles` AS `v3`
        WHERE `v3`.`Discriminator` = 'PoweredVehicle'
    ) AS `t0` ON `v2`.`Name` = `t0`.`Name`
    WHERE `v2`.`Engine_Discriminator` IN ('ContinuousCombustionEngine', 'IntermittentCombustionEngine', 'SolidRocket')
) AS `t1` ON `v1`.`Name` = `t1`.`Name`");
        }

        public override async Task Can_query_shared_derived_nonhierarchy()
        {
            await base.Can_query_shared_derived_nonhierarchy();

            AssertSql(
                $@"SELECT `v`.`Name`, `v`.`Capacity`, `v`.`FuelType`
FROM `Vehicles` AS `v`
INNER JOIN (
    SELECT `v0`.`Name`, `v0`.`Discriminator`, `v0`.`SeatingCapacity`
    FROM `Vehicles` AS `v0`
    WHERE `v0`.`Discriminator` = 'PoweredVehicle'
) AS `t` ON `v`.`Name` = `t`.`Name`
WHERE `v`.`FuelType` IS NOT NULL OR `v`.`Capacity` IS NOT NULL
UNION
SELECT `v1`.`Name`, `v1`.`Capacity`, `v1`.`FuelType`
FROM `Vehicles` AS `v1`
INNER JOIN (
    SELECT `v2`.`Name`, `v2`.`Description`, `v2`.`Engine_Discriminator`, `t0`.`Name` AS `Name0`
    FROM `Vehicles` AS `v2`
    INNER JOIN (
        SELECT `v3`.`Name`, `v3`.`Discriminator`, `v3`.`SeatingCapacity`
        FROM `Vehicles` AS `v3`
        WHERE `v3`.`Discriminator` = 'PoweredVehicle'
    ) AS `t0` ON `v2`.`Name` = `t0`.`Name`
    WHERE `v2`.`Engine_Discriminator` IN ('ContinuousCombustionEngine', 'IntermittentCombustionEngine', 'SolidRocket')
) AS `t1` ON `v1`.`Name` = `t1`.`Name`");
        }

        public override async Task Can_query_shared_derived_nonhierarchy_all_required()
        {
            await base.Can_query_shared_derived_nonhierarchy_all_required();

            AssertSql(
                $@"SELECT `v`.`Name`, `v`.`Capacity`, `v`.`FuelType`
FROM `Vehicles` AS `v`
INNER JOIN (
    SELECT `v0`.`Name`, `v0`.`Discriminator`, `v0`.`SeatingCapacity`
    FROM `Vehicles` AS `v0`
    WHERE `v0`.`Discriminator` = 'PoweredVehicle'
) AS `t` ON `v`.`Name` = `t`.`Name`
WHERE `v`.`FuelType` IS NOT NULL AND `v`.`Capacity` IS NOT NULL
UNION
SELECT `v1`.`Name`, `v1`.`Capacity`, `v1`.`FuelType`
FROM `Vehicles` AS `v1`
INNER JOIN (
    SELECT `v2`.`Name`, `v2`.`Description`, `v2`.`Engine_Discriminator`, `t0`.`Name` AS `Name0`
    FROM `Vehicles` AS `v2`
    INNER JOIN (
        SELECT `v3`.`Name`, `v3`.`Discriminator`, `v3`.`SeatingCapacity`
        FROM `Vehicles` AS `v3`
        WHERE `v3`.`Discriminator` = 'PoweredVehicle'
    ) AS `t0` ON `v2`.`Name` = `t0`.`Name`
    WHERE `v2`.`Engine_Discriminator` IN ('ContinuousCombustionEngine', 'IntermittentCombustionEngine', 'SolidRocket')
) AS `t1` ON `v1`.`Name` = `t1`.`Name`");
        }

        public override async Task Can_change_dependent_instance_non_derived()
        {
            await base.Can_change_dependent_instance_non_derived();

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@p3='Trek Pro Fit Madone 6 Series' (Nullable = false) (Size = 450)")}

{AssertSqlHelper.Declaration("@p0='LicensedOperator' (Nullable = false) (Size = 4000)")}

{AssertSqlHelper.Declaration("@p1='repairman' (Size = 4000)")}

{AssertSqlHelper.Declaration("@p2='Repair' (Size = 4000)")}

SET NOCOUNT ON;
UPDATE `Vehicles` SET `Operator_Discriminator` = {AssertSqlHelper.Parameter("@p0")}, `Operator_Name` = {AssertSqlHelper.Parameter("@p1")}, `LicenseType` = {AssertSqlHelper.Parameter("@p2")}
WHERE `Name` = {AssertSqlHelper.Parameter("@p3")};
SELECT @@ROWCOUNT;");
        }

        public override async Task Can_change_principal_instance_non_derived()
        {
            await base.Can_change_principal_instance_non_derived();

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@p1='Trek Pro Fit Madone 6 Series' (Nullable = false) (Size = 450)")}

{AssertSqlHelper.Declaration("@p0='2'")}

SET NOCOUNT ON;
UPDATE `Vehicles` SET `SeatingCapacity` = {AssertSqlHelper.Parameter("@p0")}
WHERE `Name` = {AssertSqlHelper.Parameter("@p1")};
SELECT @@ROWCOUNT;");
        }

        public override async Task Optional_dependent_materialized_when_no_properties()
        {
            await base.Optional_dependent_materialized_when_no_properties();

            AssertSql(
                @"SELECT TOP 1 `v`.`Name`, `v`.`Discriminator`, `v`.`SeatingCapacity`, `v`.`AttachedVehicleName`, `t`.`Name`, `t`.`Operator_Discriminator`, `t`.`Operator_Name`, `t`.`LicenseType`, `t0`.`Name`, `t0`.`Active`, `t0`.`Type`
FROM `Vehicles` AS `v`
LEFT JOIN (
    SELECT `v0`.`Name`, `v0`.`Operator_Discriminator`, `v0`.`Operator_Name`, `v0`.`LicenseType`
    FROM `Vehicles` AS `v0`
    INNER JOIN `Vehicles` AS `v1` ON `v0`.`Name` = `v1`.`Name`
) AS `t` ON `v`.`Name` = `t`.`Name`
LEFT JOIN (
    SELECT `v2`.`Name`, `v2`.`Active`, `v2`.`Type`
    FROM `Vehicles` AS `v2`
    INNER JOIN (
        SELECT `v3`.`Name`
        FROM `Vehicles` AS `v3`
        INNER JOIN `Vehicles` AS `v4` ON `v3`.`Name` = `v4`.`Name`
    ) AS `t1` ON `v2`.`Name` = `t1`.`Name`
    WHERE `v2`.`Active` IS NOT NULL
) AS `t0` ON `t`.`Name` = CASE
    WHEN `t0`.`Active` IS NOT NULL THEN `t0`.`Name`
END
WHERE `v`.`Name` = 'AIM-9M Sidewinder'
ORDER BY `v`.`Name`");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Engine>().ToTable("Vehicles")
                .Property(e => e.Computed).HasComputedColumnSql("1", stored: true);
        }
    }
}
