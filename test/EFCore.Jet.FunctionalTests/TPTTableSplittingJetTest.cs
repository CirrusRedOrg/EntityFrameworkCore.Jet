// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests;

public class TPTTableSplittingJetTest : TPTTableSplittingTestBase
{
    public TPTTableSplittingJetTest(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    protected override ITestStoreFactory TestStoreFactory
        => JetTestStoreFactory.Instance;

    public override async Task Can_use_with_redundant_relationships()
    {
        await base.Can_use_with_redundant_relationships();

        AssertSql(
"""
SELECT [v].[Name], [v].[SeatingCapacity], [c].[AttachedVehicleName], CASE
    WHEN [c].[Name] IS NOT NULL THEN N'CompositeVehicle'
    WHEN [p].[Name] IS NOT NULL THEN N'PoweredVehicle'
END AS [Discriminator], [t].[Name], [t].[Operator_Name], [t].[LicenseType], [t].[Discriminator], [t0].[Name], [t0].[Active], [t0].[Type], [t1].[Name], [t1].[Computed], [t1].[Description], [t1].[Discriminator], [t2].[VehicleName], [t2].[Capacity], [t2].[FuelType], [t2].[GrainGeometry], [t2].[Discriminator]
FROM [Vehicles] AS [v]
LEFT JOIN [PoweredVehicles] AS [p] ON [v].[Name] = [p].[Name]
LEFT JOIN [CompositeVehicles] AS [c] ON [v].[Name] = [c].[Name]
LEFT JOIN (
    SELECT [v0].[Name], [v0].[Operator_Name], [l].[LicenseType], CASE
        WHEN [l].[VehicleName] IS NOT NULL THEN N'LicensedOperator'
    END AS [Discriminator]
    FROM [Vehicles] AS [v0]
    LEFT JOIN [LicensedOperators] AS [l] ON [v0].[Name] = [l].[VehicleName]
) AS [t] ON [v].[Name] = [t].[Name]
LEFT JOIN (
    SELECT [v1].[Name], [v1].[Active], [v1].[Type]
    FROM [Vehicles] AS [v1]
    WHERE [v1].[Active] IS NOT NULL
) AS [t0] ON [t].[Name] = CASE
    WHEN [t0].[Active] IS NOT NULL THEN [t0].[Name]
END
LEFT JOIN (
    SELECT [p0].[Name], [p0].[Computed], [p0].[Description], CASE
        WHEN [s].[VehicleName] IS NOT NULL THEN N'SolidRocket'
        WHEN [i].[VehicleName] IS NOT NULL THEN N'IntermittentCombustionEngine'
        WHEN [c1].[VehicleName] IS NOT NULL THEN N'ContinuousCombustionEngine'
    END AS [Discriminator]
    FROM [PoweredVehicles] AS [p0]
    LEFT JOIN [ContinuousCombustionEngines] AS [c1] ON [p0].[Name] = [c1].[VehicleName]
    LEFT JOIN [IntermittentCombustionEngines] AS [i] ON [p0].[Name] = [i].[VehicleName]
    LEFT JOIN [SolidRockets] AS [s] ON [p0].[Name] = [s].[VehicleName]
    WHERE [p0].[Computed] IS NOT NULL
) AS [t1] ON [v].[Name] = CASE
    WHEN [t1].[Computed] IS NOT NULL THEN [t1].[Name]
END
LEFT JOIN (
    SELECT [c2].[VehicleName], [c2].[Capacity], [c2].[FuelType], [s0].[GrainGeometry], CASE
        WHEN [s0].[VehicleName] IS NOT NULL THEN N'SolidFuelTank'
    END AS [Discriminator]
    FROM [CombustionEngines] AS [c2]
    LEFT JOIN [SolidFuelTanks] AS [s0] ON [c2].[VehicleName] = [s0].[VehicleName]
    WHERE [c2].[Capacity] IS NOT NULL
) AS [t2] ON [t1].[Name] = CASE
    WHEN [t2].[Capacity] IS NOT NULL THEN [t2].[VehicleName]
END
ORDER BY [v].[Name]
""");
    }

    public override async Task Can_query_shared()
    {
        await base.Can_query_shared();

        AssertSql(
"""
SELECT [v].[Name], [v].[Operator_Name], [l].[LicenseType], CASE
    WHEN [l].[VehicleName] IS NOT NULL THEN N'LicensedOperator'
END AS [Discriminator]
FROM [Vehicles] AS [v]
LEFT JOIN [LicensedOperators] AS [l] ON [v].[Name] = [l].[VehicleName]
""");
    }

    public override async Task Can_query_shared_nonhierarchy()
    {
        await base.Can_query_shared_nonhierarchy();

        AssertSql(
"""
SELECT [v].[Name], [v].[Operator_Name]
FROM [Vehicles] AS [v]
""");
    }

    public override async Task Can_query_shared_nonhierarchy_with_nonshared_dependent()
    {
        await base.Can_query_shared_nonhierarchy_with_nonshared_dependent();

        AssertSql(
"""
SELECT [v].[Name], [v].[Operator_Name]
FROM [Vehicles] AS [v]
""");
    }

    public override async Task Can_query_shared_derived_hierarchy()
    {
        await base.Can_query_shared_derived_hierarchy();

        AssertSql(
"""
SELECT [c].[VehicleName], [c].[Capacity], [c].[FuelType], [s].[GrainGeometry], CASE
    WHEN [s].[VehicleName] IS NOT NULL THEN N'SolidFuelTank'
END AS [Discriminator]
FROM [CombustionEngines] AS [c]
LEFT JOIN [SolidFuelTanks] AS [s] ON [c].[VehicleName] = [s].[VehicleName]
WHERE [c].[Capacity] IS NOT NULL
""");
    }

    public override async Task Can_query_shared_derived_nonhierarchy()
    {
        await base.Can_query_shared_derived_nonhierarchy();

        AssertSql(
"""
SELECT [c].[VehicleName], [c].[Capacity], [c].[FuelType]
FROM [CombustionEngines] AS [c]
WHERE [c].[Capacity] IS NOT NULL
""");
    }

    public override async Task Can_query_shared_derived_nonhierarchy_all_required()
    {
        await base.Can_query_shared_derived_nonhierarchy_all_required();

        AssertSql(
"""
SELECT [c].[VehicleName], [c].[Capacity], [c].[FuelType]
FROM [CombustionEngines] AS [c]
WHERE ([c].[Capacity] IS NOT NULL) AND ([c].[FuelType] IS NOT NULL)
""");
    }

    public override async Task Can_change_dependent_instance_non_derived()
    {
        await base.Can_change_dependent_instance_non_derived();
        AssertSql(
"""
@p0='Trek Pro Fit Madone 6 Series' (Nullable = false) (Size = 255)
@p1='Repair' (Size = 255)

INSERT INTO `LicensedOperators` (`VehicleName`, `LicenseType`)
VALUES (@p0, @p1);
SELECT @@ROWCOUNT;
"""
,
"""
@p0='repairman' (Size = 255)
@p1='Trek Pro Fit Madone 6 Series' (Nullable = false) (Size = 255)

UPDATE `Vehicles` SET `Operator_Name` = @p0
WHERE `Name` = @p1;
SELECT @@ROWCOUNT;
"""
,
"""
SELECT TOP 2 `v`.`Name`, `v`.`SeatingCapacity`, `c`.`AttachedVehicleName`, IIF(`c`.`Name` IS NOT NULL, 'CompositeVehicle', IIF(`p`.`Name` IS NOT NULL, 'PoweredVehicle', NULL)) AS `Discriminator`, `t`.`Name`, `t`.`Operator_Name`, `t`.`LicenseType`, `t`.`Discriminator`
FROM ((`Vehicles` AS `v`
LEFT JOIN `PoweredVehicles` AS `p` ON `v`.`Name` = `p`.`Name`)
LEFT JOIN `CompositeVehicles` AS `c` ON `v`.`Name` = `c`.`Name`)
LEFT JOIN (
    SELECT `v0`.`Name`, `v0`.`Operator_Name`, `l`.`LicenseType`, IIF(`l`.`VehicleName` IS NOT NULL, 'LicensedOperator', NULL) AS `Discriminator`
    FROM `Vehicles` AS `v0`
    LEFT JOIN `LicensedOperators` AS `l` ON `v0`.`Name` = `l`.`VehicleName`
) AS `t` ON `v`.`Name` = `t`.`Name`
WHERE `v`.`Name` = 'Trek Pro Fit Madone 6 Series'
""");
    }

    public override async Task Can_change_principal_instance_non_derived()
    {
        await base.Can_change_principal_instance_non_derived();

        AssertSql(
"""
@p0='2'
@p1='Trek Pro Fit Madone 6 Series' (Nullable = false) (Size = 255)

UPDATE `Vehicles` SET `SeatingCapacity` = @p0
WHERE `Name` = @p1;
SELECT @@ROWCOUNT;
"""
,
"""
SELECT TOP 2 `v`.`Name`, `v`.`SeatingCapacity`, `c`.`AttachedVehicleName`, IIF(`c`.`Name` IS NOT NULL, 'CompositeVehicle', IIF(`p`.`Name` IS NOT NULL, 'PoweredVehicle', NULL)) AS `Discriminator`, `t`.`Name`, `t`.`Operator_Name`, `t`.`LicenseType`, `t`.`Discriminator`
FROM ((`Vehicles` AS `v`
LEFT JOIN `PoweredVehicles` AS `p` ON `v`.`Name` = `p`.`Name`)
LEFT JOIN `CompositeVehicles` AS `c` ON `v`.`Name` = `c`.`Name`)
LEFT JOIN (
    SELECT `v0`.`Name`, `v0`.`Operator_Name`, `l`.`LicenseType`, IIF(`l`.`VehicleName` IS NOT NULL, 'LicensedOperator', NULL) AS `Discriminator`
    FROM `Vehicles` AS `v0`
    LEFT JOIN `LicensedOperators` AS `l` ON `v0`.`Name` = `l`.`VehicleName`
) AS `t` ON `v`.`Name` = `t`.`Name`
WHERE `v`.`Name` = 'Trek Pro Fit Madone 6 Series'
""");
    }

    public override async Task Optional_dependent_materialized_when_no_properties()
    {
        await base.Optional_dependent_materialized_when_no_properties();

        AssertSql(
"""
SELECT TOP(1) [v].[Name], [v].[SeatingCapacity], [c].[AttachedVehicleName], CASE
    WHEN [c].[Name] IS NOT NULL THEN N'CompositeVehicle'
    WHEN [p].[Name] IS NOT NULL THEN N'PoweredVehicle'
END AS [Discriminator], [t].[Name], [t].[Operator_Name], [t].[LicenseType], [t].[Discriminator], [t0].[Name], [t0].[Active], [t0].[Type]
FROM [Vehicles] AS [v]
LEFT JOIN [PoweredVehicles] AS [p] ON [v].[Name] = [p].[Name]
LEFT JOIN [CompositeVehicles] AS [c] ON [v].[Name] = [c].[Name]
LEFT JOIN (
    SELECT [v0].[Name], [v0].[Operator_Name], [l].[LicenseType], CASE
        WHEN [l].[VehicleName] IS NOT NULL THEN N'LicensedOperator'
    END AS [Discriminator]
    FROM [Vehicles] AS [v0]
    LEFT JOIN [LicensedOperators] AS [l] ON [v0].[Name] = [l].[VehicleName]
) AS [t] ON [v].[Name] = [t].[Name]
LEFT JOIN (
    SELECT [v1].[Name], [v1].[Active], [v1].[Type]
    FROM [Vehicles] AS [v1]
    WHERE [v1].[Active] IS NOT NULL
) AS [t0] ON [t].[Name] = CASE
    WHEN [t0].[Active] IS NOT NULL THEN [t0].[Name]
END
WHERE [v].[Name] = N'AIM-9M Sidewinder'
ORDER BY [v].[Name]
""");
    }
}
