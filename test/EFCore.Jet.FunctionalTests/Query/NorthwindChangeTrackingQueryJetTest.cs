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
SELECT TOP @p `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
ORDER BY `e`.`EmployeeID`
""",
                //
                """
SELECT `e1`.`EmployeeID`, `e1`.`City`, `e1`.`Country`, `e1`.`FirstName`, `e1`.`ReportsTo`, `e1`.`Title`
FROM (
    SELECT TOP @p `e0`.`EmployeeID`, `e0`.`City`, `e0`.`Country`, `e0`.`FirstName`, `e0`.`ReportsTo`, `e0`.`Title`
    FROM (
        SELECT TOP @p + @p `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
        FROM `Employees` AS `e`
        ORDER BY `e`.`EmployeeID`
    ) AS `e0`
    ORDER BY `e0`.`EmployeeID` DESC
) AS `e1`
ORDER BY `e1`.`EmployeeID`
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
SELECT TOP 1 `c0`.`CustomerID`, `c0`.`Address`, `c0`.`City`, `c0`.`CompanyName`, `c0`.`ContactName`, `c0`.`ContactTitle`, `c0`.`Country`, `c0`.`Fax`, `c0`.`Phone`, `c0`.`PostalCode`, `c0`.`Region`
FROM (
    SELECT TOP @p `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    ORDER BY `c`.`CustomerID`
) AS `c0`
ORDER BY `c0`.`CustomerID`
""",
                //
                """
SELECT `c2`.`CustomerID`, `c2`.`Address`, `c2`.`City`, `c2`.`CompanyName`, `c2`.`ContactName`, `c2`.`ContactTitle`, `c2`.`Country`, `c2`.`Fax`, `c2`.`Phone`, `c2`.`PostalCode`, `c2`.`Region`
FROM (
    SELECT TOP 1 `c1`.`CustomerID`, `c1`.`Address`, `c1`.`City`, `c1`.`CompanyName`, `c1`.`ContactName`, `c1`.`ContactTitle`, `c1`.`Country`, `c1`.`Fax`, `c1`.`Phone`, `c1`.`PostalCode`, `c1`.`Region`
    FROM (
        SELECT TOP @p0 + 1 `c0`.`CustomerID`, `c0`.`Address`, `c0`.`City`, `c0`.`CompanyName`, `c0`.`ContactName`, `c0`.`ContactTitle`, `c0`.`Country`, `c0`.`Fax`, `c0`.`Phone`, `c0`.`PostalCode`, `c0`.`Region`
        FROM (
            SELECT TOP @p `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            FROM `Customers` AS `c`
            ORDER BY `c`.`CustomerID`
        ) AS `c0`
        ORDER BY `c0`.`CustomerID`
    ) AS `c1`
    ORDER BY `c1`.`CustomerID` DESC
) AS `c2`
ORDER BY `c2`.`CustomerID`
""",
                //
                """
SELECT TOP @p `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
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
SELECT TOP 1 `c0`.`CustomerID`, `c0`.`Address`, `c0`.`City`, `c0`.`CompanyName`, `c0`.`ContactName`, `c0`.`ContactTitle`, `c0`.`Country`, `c0`.`Fax`, `c0`.`Phone`, `c0`.`PostalCode`, `c0`.`Region`
FROM (
    SELECT TOP @p `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    ORDER BY `c`.`CustomerID`
) AS `c0`
ORDER BY `c0`.`CustomerID`
""",
                //
                """
SELECT `c2`.`CustomerID`, `c2`.`Address`, `c2`.`City`, `c2`.`CompanyName`, `c2`.`ContactName`, `c2`.`ContactTitle`, `c2`.`Country`, `c2`.`Fax`, `c2`.`Phone`, `c2`.`PostalCode`, `c2`.`Region`
FROM (
    SELECT TOP 1 `c1`.`CustomerID`, `c1`.`Address`, `c1`.`City`, `c1`.`CompanyName`, `c1`.`ContactName`, `c1`.`ContactTitle`, `c1`.`Country`, `c1`.`Fax`, `c1`.`Phone`, `c1`.`PostalCode`, `c1`.`Region`
    FROM (
        SELECT TOP @p0 + 1 `c0`.`CustomerID`, `c0`.`Address`, `c0`.`City`, `c0`.`CompanyName`, `c0`.`ContactName`, `c0`.`ContactTitle`, `c0`.`Country`, `c0`.`Fax`, `c0`.`Phone`, `c0`.`PostalCode`, `c0`.`Region`
        FROM (
            SELECT TOP @p `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            FROM `Customers` AS `c`
            ORDER BY `c`.`CustomerID`
        ) AS `c0`
        ORDER BY `c0`.`CustomerID`
    ) AS `c1`
    ORDER BY `c1`.`CustomerID` DESC
) AS `c2`
ORDER BY `c2`.`CustomerID`
""",
                //
                """
SELECT TOP @p `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
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
SELECT TOP @p `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
ORDER BY `e`.`EmployeeID`
""",
                //
                """
SELECT `e1`.`EmployeeID`, `e1`.`City`, `e1`.`Country`, `e1`.`FirstName`, `e1`.`ReportsTo`, `e1`.`Title`
FROM (
    SELECT TOP @p `e0`.`EmployeeID`, `e0`.`City`, `e0`.`Country`, `e0`.`FirstName`, `e0`.`ReportsTo`, `e0`.`Title`
    FROM (
        SELECT TOP @p + @p `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
        FROM `Employees` AS `e`
        ORDER BY `e`.`EmployeeID`
    ) AS `e0`
    ORDER BY `e0`.`EmployeeID` DESC
) AS `e1`
ORDER BY `e1`.`EmployeeID`
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
