// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query
{
    public class NorthwindAsTrackingQueryJetTest : NorthwindAsTrackingQueryTestBase<NorthwindQueryJetFixture<NoopModelCustomizer>>
    {
        public NorthwindAsTrackingQueryJetTest(NorthwindQueryJetFixture<NoopModelCustomizer> fixture)
            : base(fixture)
        {
            Fixture.TestSqlLoggerFactory.Clear();
        }

        [ConditionalFact]
        public virtual void Check_all_tests_overridden()
        => TestHelpers.AssertAllMethodsOverridden(GetType());

        public override void Applied_to_body_clause()
        {
            base.Applied_to_body_clause();

            AssertSql(
"""
SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Customers` AS `c`
INNER JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`
WHERE `c`.`CustomerID` = 'ALFKI'
""");
        }

        public override void Entity_added_to_state_manager(bool useParam)
        {
            base.Entity_added_to_state_manager(useParam);

            AssertSql(
"""
SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
""");
        }

        public override void Applied_to_projection()
        {
            base.Applied_to_projection();

            AssertSql(
"""
SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Customers` AS `c`
INNER JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`
WHERE `c`.`CustomerID` = 'ALFKI'
""");
        }

        public override void Applied_to_multiple_body_clauses()
        {
            base.Applied_to_multiple_body_clauses();

            AssertSql(
"""
SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Customers` AS `c`,
`Orders` AS `o`
WHERE `c`.`CustomerID` = `o`.`CustomerID`
""");
        }

        public override void Applied_to_body_clause_with_projection()
        {
            base.Applied_to_body_clause_with_projection();

            AssertSql(
"""
SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `o`.`CustomerID` AS `ocid`, `o`.`OrderID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Customers` AS `c`
INNER JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`
WHERE `c`.`CustomerID` = 'ALFKI'
""");
        }

        private void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);
    }
}
