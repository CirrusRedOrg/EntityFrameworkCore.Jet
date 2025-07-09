﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using System.Threading.Tasks;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query;

public class SharedTypeQueryJetTest(NonSharedFixture fixture) : SharedTypeQueryRelationalTestBase(fixture)
{
    protected override ITestStoreFactory TestStoreFactory
        => JetTestStoreFactory.Instance;

    public override async Task Can_use_shared_type_entity_type_in_query_filter(bool async)
    {
        await base.Can_use_shared_type_entity_type_in_query_filter(async);

        AssertSql(
"""
SELECT `v`.`Value`
FROM `ViewQuery24601` AS `v`
WHERE EXISTS (
    SELECT 1
    FROM `STET` AS `s`
    WHERE `s`.`Value` = `v`.`Value` OR (`s`.`Value` IS NULL AND `v`.`Value` IS NULL))
""");
    }

    public override async Task Can_use_shared_type_entity_type_in_query_filter_with_from_sql(bool async)
    {
        await base.Can_use_shared_type_entity_type_in_query_filter_with_from_sql(async);

        AssertSql(
"""
SELECT `v`.`Value`
FROM `ViewQuery24601` AS `v`
WHERE EXISTS (
    SELECT 1
    FROM (
        Select * from STET
    ) AS `s`
    WHERE `s`.`Value` = `v`.`Value` OR (`s`.`Value` IS NULL AND `v`.`Value` IS NULL))
""");
    }
}
