// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading.Tasks;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore.BulkUpdates;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests.BulkUpdates;

public class ComplexTypeBulkUpdatesJetTest : ComplexTypeBulkUpdatesTestBase<
    ComplexTypeBulkUpdatesJetTest.ComplexTypeBulkUpdatesJetFixture>
{
    public ComplexTypeBulkUpdatesJetTest(ComplexTypeBulkUpdatesJetFixture fixture, ITestOutputHelper testOutputHelper)
        : base(fixture, testOutputHelper)
    {
    }

    public override async Task Delete_entity_type_with_complex_type(bool async)
    {
        await base.Delete_entity_type_with_complex_type(async);

        AssertSql(
            """
DELETE FROM `Customer` AS `c`
WHERE `c`.`Name` = 'Monty Elias'
""");
    }

    public override async Task Delete_complex_type_throws(bool async)
    {
        await base.Delete_complex_type_throws(async);

        AssertSql();
    }

    public override async Task Update_property_inside_complex_type(bool async)
    {
        await base.Update_property_inside_complex_type(async);

        AssertExecuteUpdateSql(
            """
UPDATE `Customer` AS `c`
SET `c`.`ShippingAddress_ZipCode` = 12345
WHERE `c`.`ShippingAddress_ZipCode` = 7728
""");
    }

    public override async Task Update_property_inside_nested_complex_type(bool async)
    {
        await base.Update_property_inside_nested_complex_type(async);

        AssertExecuteUpdateSql(
            """
UPDATE `Customer` AS `c`
SET `c`.`ShippingAddress_Country_FullName` = 'United States Modified'
WHERE `c`.`ShippingAddress_Country_Code` = 'US'
""");
    }

    public override async Task Update_multiple_properties_inside_multiple_complex_types_and_on_entity_type(bool async)
    {
        await base.Update_multiple_properties_inside_multiple_complex_types_and_on_entity_type(async);

        AssertExecuteUpdateSql(
            """
UPDATE `Customer` AS `c`
SET `c`.`BillingAddress_ZipCode` = 54321,
    `c`.`ShippingAddress_ZipCode` = `c`.`BillingAddress_ZipCode`,
    `c`.`Name` = `c`.`Name` & 'Modified'
WHERE `c`.`ShippingAddress_ZipCode` = 7728
""");
    }

    public override async Task Update_projected_complex_type(bool async)
    {
        await base.Update_projected_complex_type(async);

        AssertExecuteUpdateSql(
            """
UPDATE `Customer` AS `c`
SET `c`.`ShippingAddress_ZipCode` = 12345
""");
    }

    public override async Task Update_multiple_projected_complex_types_via_anonymous_type(bool async)
    {
        await base.Update_multiple_projected_complex_types_via_anonymous_type(async);

        AssertExecuteUpdateSql(
            """
UPDATE `Customer` AS `c`
SET `c`.`BillingAddress_ZipCode` = 54321,
    `c`.`ShippingAddress_ZipCode` = `c`.`BillingAddress_ZipCode`
""");
    }

    public override async Task Update_projected_complex_type_via_OrderBy_Skip_throws(bool async)
    {
        await base.Update_projected_complex_type_via_OrderBy_Skip_throws(async);

        AssertExecuteUpdateSql();
    }

    [ConditionalFact]
    public virtual void Check_all_tests_overridden()
        => TestHelpers.AssertAllMethodsOverridden(GetType());

    private void AssertExecuteUpdateSql(params string[] expected)
        => Fixture.TestSqlLoggerFactory.AssertBaseline(expected, forUpdate: true);

    private void AssertSql(params string[] expected)
        => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);

    protected void ClearLog()
        => Fixture.TestSqlLoggerFactory.Clear();

    public class ComplexTypeBulkUpdatesJetFixture : ComplexTypeBulkUpdatesFixtureBase
    {
        protected override ITestStoreFactory TestStoreFactory
            => JetTestStoreFactory.Instance;
    }
}
