// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq;
using System.Threading.Tasks;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests;

public class EntitySplittingJetTest(NonSharedFixture fixture, ITestOutputHelper testOutputHelper) : EntitySplittingTestBase(fixture, testOutputHelper)
{
    public override async Task Can_roundtrip()
    {
        await base.Can_roundtrip();

        AssertSql(
$"""
@p0='2' (Nullable = true)

INSERT INTO `MeterReadings` (`ReadingStatus`)
VALUES ({AssertSqlHelper.Parameter("@p0")});
SELECT `Id`
FROM `MeterReadings`
WHERE @@ROWCOUNT = 1 AND `Id` = @@identity;
""",
//
$"""
@p1='1'
@p2='100' (Size = 255)
@p3=NULL (Size = 255)

INSERT INTO `MeterReadingDetails` (`Id`, `CurrentRead`, `PreviousRead`)
VALUES ({AssertSqlHelper.Parameter("@p1")}, {AssertSqlHelper.Parameter("@p2")}, {AssertSqlHelper.Parameter("@p3")});
""",
//
"""
SELECT TOP 2 `m`.`Id`, `m0`.`CurrentRead`, `m0`.`PreviousRead`, `m`.`ReadingStatus`
FROM `MeterReadings` AS `m`
INNER JOIN `MeterReadingDetails` AS `m0` ON `m`.`Id` = `m0`.`Id`
""");
    }

    protected override ITestStoreFactory TestStoreFactory
        => JetTestStoreFactory.Instance;
}
