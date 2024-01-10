// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using EntityFrameworkCore.Jet.FunctionalTests.TestModels.Northwind;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestModels.Northwind;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query
{
    public class NorthwindChangeTrackingQueryJetTest : NorthwindChangeTrackingQueryTestBase<NorthwindQueryJetFixture<NoopModelCustomizer>>
    {
        public NorthwindChangeTrackingQueryJetTest(NorthwindQueryJetFixture<NoopModelCustomizer> fixture)
            : base(fixture)
        {
            Fixture.TestSqlLoggerFactory.Clear();
        }

        [ConditionalFact]
        public virtual void Check_all_tests_overridden()
        => TestHelpers.AssertAllMethodsOverridden(GetType());

        public override void Entity_reverts_when_state_set_to_unchanged()
        {
            base.Entity_reverts_when_state_set_to_unchanged();

            AssertSql(
"""
SELECT TOP 1 `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
""");
        }

        public override void Entity_does_not_revert_when_attached_on_DbSet()
        {
            base.Entity_does_not_revert_when_attached_on_DbSet();

            AssertSql(
"""
SELECT TOP 1 `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
""");
        }

        public override void AsTracking_switches_tracking_on_when_off_in_options()
        {
            base.AsTracking_switches_tracking_on_when_off_in_options();

            AssertSql(
"""
SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
""");
        }

        public override void Can_disable_and_reenable_query_result_tracking_query_caching_using_options()
        {
            base.Can_disable_and_reenable_query_result_tracking_query_caching_using_options();

            AssertSql(
"""
SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
""",
                //
"""
SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
""");
        }

        public override void Can_disable_and_reenable_query_result_tracking()
        {
            base.Can_disable_and_reenable_query_result_tracking();

            AssertSql(
"""
SELECT TOP 1 `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
ORDER BY `e`.`EmployeeID`
""",
                //
"""
SELECT `t0`.`EmployeeID`, `t0`.`City`, `t0`.`Country`, `t0`.`FirstName`, `t0`.`ReportsTo`, `t0`.`Title`
FROM (
    SELECT TOP 1 `t`.`EmployeeID`, `t`.`City`, `t`.`Country`, `t`.`FirstName`, `t`.`ReportsTo`, `t`.`Title`
    FROM (
        SELECT TOP 2 `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
        FROM `Employees` AS `e`
        ORDER BY `e`.`EmployeeID`
    ) AS `t`
    ORDER BY `t`.`EmployeeID` DESC
) AS `t0`
ORDER BY `t0`.`EmployeeID`
""",
                //
"""
SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
ORDER BY `e`.`EmployeeID`
""");
        }

        public override void Entity_range_does_not_revert_when_attached_dbSet()
        {
            base.Entity_range_does_not_revert_when_attached_dbSet();

            AssertSql(
"""
SELECT TOP 1 `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`
FROM (
    SELECT TOP 2 `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    ORDER BY `c`.`CustomerID`
) AS `t`
ORDER BY `t`.`CustomerID`
""",
                //
"""
SELECT `t1`.`CustomerID`, `t1`.`Address`, `t1`.`City`, `t1`.`CompanyName`, `t1`.`ContactName`, `t1`.`ContactTitle`, `t1`.`Country`, `t1`.`Fax`, `t1`.`Phone`, `t1`.`PostalCode`, `t1`.`Region`
FROM (
    SELECT TOP 1 `t0`.`CustomerID`, `t0`.`Address`, `t0`.`City`, `t0`.`CompanyName`, `t0`.`ContactName`, `t0`.`ContactTitle`, `t0`.`Country`, `t0`.`Fax`, `t0`.`Phone`, `t0`.`PostalCode`, `t0`.`Region`
    FROM (
        SELECT TOP 2 `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`
        FROM (
            SELECT TOP 2 `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            FROM `Customers` AS `c`
            ORDER BY `c`.`CustomerID`
        ) AS `t`
        ORDER BY `t`.`CustomerID`
    ) AS `t0`
    ORDER BY `t0`.`CustomerID` DESC
) AS `t1`
ORDER BY `t1`.`CustomerID`
""",
                //
"""
SELECT TOP 2 `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
ORDER BY `c`.`CustomerID`
""");
        }

        public override void Precedence_of_tracking_modifiers5()
        {
            base.Precedence_of_tracking_modifiers5();

            AssertSql(
"""
SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Customers` AS `c`
INNER JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`
WHERE `c`.`CustomerID` = 'ALFKI'
""");
        }

        public override void Precedence_of_tracking_modifiers2()
        {
            base.Precedence_of_tracking_modifiers2();

            AssertSql(
"""
SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
""");
        }

        public override void Can_disable_and_reenable_query_result_tracking_query_caching()
        {
            base.Can_disable_and_reenable_query_result_tracking_query_caching();

            AssertSql(
"""
SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
""",
                //
"""
SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
""");
        }

        public override void Entity_range_does_not_revert_when_attached_dbContext()
        {
            base.Entity_range_does_not_revert_when_attached_dbContext();

            AssertSql(
"""
SELECT TOP 1 `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`
FROM (
    SELECT TOP 2 `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    ORDER BY `c`.`CustomerID`
) AS `t`
ORDER BY `t`.`CustomerID`
""",
                //
"""
SELECT `t1`.`CustomerID`, `t1`.`Address`, `t1`.`City`, `t1`.`CompanyName`, `t1`.`ContactName`, `t1`.`ContactTitle`, `t1`.`Country`, `t1`.`Fax`, `t1`.`Phone`, `t1`.`PostalCode`, `t1`.`Region`
FROM (
    SELECT TOP 1 `t0`.`CustomerID`, `t0`.`Address`, `t0`.`City`, `t0`.`CompanyName`, `t0`.`ContactName`, `t0`.`ContactTitle`, `t0`.`Country`, `t0`.`Fax`, `t0`.`Phone`, `t0`.`PostalCode`, `t0`.`Region`
    FROM (
        SELECT TOP 2 `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`
        FROM (
            SELECT TOP 2 `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            FROM `Customers` AS `c`
            ORDER BY `c`.`CustomerID`
        ) AS `t`
        ORDER BY `t`.`CustomerID`
    ) AS `t0`
    ORDER BY `t0`.`CustomerID` DESC
) AS `t1`
ORDER BY `t1`.`CustomerID`
""",
                //
"""
SELECT TOP 2 `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
ORDER BY `c`.`CustomerID`
""");
        }

        public override void Precedence_of_tracking_modifiers()
        {
            base.Precedence_of_tracking_modifiers();

            AssertSql(
"""
SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
""");
        }

        public override void Precedence_of_tracking_modifiers3()
        {
            base.Precedence_of_tracking_modifiers3();

            AssertSql(
"""
SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Customers` AS `c`
INNER JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`
WHERE `c`.`CustomerID` = 'ALFKI'
""");
        }

        public override void Can_disable_and_reenable_query_result_tracking_starting_with_NoTracking()
        {
            base.Can_disable_and_reenable_query_result_tracking_starting_with_NoTracking();

            AssertSql(
"""
SELECT TOP 1 `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
ORDER BY `e`.`EmployeeID`
""",
                //
"""
SELECT `t0`.`EmployeeID`, `t0`.`City`, `t0`.`Country`, `t0`.`FirstName`, `t0`.`ReportsTo`, `t0`.`Title`
FROM (
    SELECT TOP 1 `t`.`EmployeeID`, `t`.`City`, `t`.`Country`, `t`.`FirstName`, `t`.`ReportsTo`, `t`.`Title`
    FROM (
        SELECT TOP 2 `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
        FROM `Employees` AS `e`
        ORDER BY `e`.`EmployeeID`
    ) AS `t`
    ORDER BY `t`.`EmployeeID` DESC
) AS `t0`
ORDER BY `t0`.`EmployeeID`
""");
        }

        public override void Entity_does_not_revert_when_attached_on_DbContext()
        {
            base.Entity_does_not_revert_when_attached_on_DbContext();

            AssertSql(
"""
SELECT TOP 1 `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
""");
        }

        public override void Can_disable_and_reenable_query_result_tracking_query_caching_single_context()
        {
            base.Can_disable_and_reenable_query_result_tracking_query_caching_single_context();

            AssertSql(
"""
SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
""",
                //
"""
SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
""");
        }

        public override void Multiple_entities_can_revert()
        {
            base.Multiple_entities_can_revert();

            AssertSql(
"""
SELECT `c`.`PostalCode`
FROM `Customers` AS `c`
""",
                //
"""
SELECT `c`.`Region`
FROM `Customers` AS `c`
""",
                //
"""
SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
""",
                //
"""
SELECT TOP 1 `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
""",
                //
"""
SELECT TOP 1 `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
""",
                //
"""
SELECT `c`.`PostalCode`
FROM `Customers` AS `c`
""",
                //
"""
SELECT `c`.`Region`
FROM `Customers` AS `c`
""");
        }

        public override void Precedence_of_tracking_modifiers4()
        {
            base.Precedence_of_tracking_modifiers4();

            AssertSql(
"""
SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Customers` AS `c`
INNER JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`
WHERE `c`.`CustomerID` = 'ALFKI'
""");
        }

        private void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);

        protected override NorthwindContext CreateNoTrackingContext()
            => new NorthwindJetContext(
                new DbContextOptionsBuilder(Fixture.CreateOptions())
                    .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options);
    }
}
