// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query
{
    public class NorthwindQueryTaggingQueryJetTest : NorthwindQueryTaggingQueryTestBase<NorthwindQueryJetFixture<NoopModelCustomizer>>
    {
        public NorthwindQueryTaggingQueryJetTest(
            NorthwindQueryJetFixture<NoopModelCustomizer> fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            Fixture.TestSqlLoggerFactory.Clear();
            Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
        }

        [ConditionalFact]
        public virtual void Check_all_tests_overridden()
            => TestHelpers.AssertAllMethodsOverridden(GetType());

        public override void Single_query_tag()
        {
            base.Single_query_tag();

            AssertSql(
                """
-- Yanni

SELECT TOP 1 `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
ORDER BY `c`.`CustomerID`
""");
        }

        public override void Single_query_multiple_tags()
        {
            base.Single_query_multiple_tags();

            AssertSql(
                """
-- Yanni
-- Enya

SELECT TOP 1 `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
ORDER BY `c`.`CustomerID`
""");
        }

        public override void Tags_on_subquery()
        {
            base.Tags_on_subquery();

            AssertSql(
                """
-- Yanni
-- Laurel

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`,
(
    SELECT TOP 5 1
    FROM `Orders` AS `o`
    ORDER BY `o`.`OrderID`
) AS `o0`
WHERE `c`.`CustomerID` = 'ALFKI'
""");
        }

        public override void Duplicate_tags()
        {
            base.Duplicate_tags();

            AssertSql(
                """
-- Yanni

SELECT TOP 1 `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
ORDER BY `c`.`CustomerID`
""");
        }

        public override void Tag_on_include_query()
        {
            base.Tag_on_include_query();

            AssertSql(
                """
-- Yanni

SELECT `c0`.`CustomerID`, `c0`.`Address`, `c0`.`City`, `c0`.`CompanyName`, `c0`.`ContactName`, `c0`.`ContactTitle`, `c0`.`Country`, `c0`.`Fax`, `c0`.`Phone`, `c0`.`PostalCode`, `c0`.`Region`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM (
    SELECT TOP 1 `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    ORDER BY `c`.`CustomerID`
) AS `c0`
LEFT JOIN `Orders` AS `o` ON `c0`.`CustomerID` = `o`.`CustomerID`
ORDER BY `c0`.`CustomerID`
""");
        }

        public override void Tag_on_scalar_query()
        {
            base.Tag_on_scalar_query();

            AssertSql(
                """
-- Yanni

SELECT TOP 1 `o`.`OrderDate`
FROM `Orders` AS `o`
ORDER BY `o`.`OrderID`
""");
        }

        public override void Single_query_multiline_tag()
        {
            base.Single_query_multiline_tag();

            AssertSql(
                """
-- Yanni
-- AND
-- Laurel

SELECT TOP 1 `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
ORDER BY `c`.`CustomerID`
""");
        }

        public override void Single_query_multiple_multiline_tag()
        {
            base.Single_query_multiple_multiline_tag();

            AssertSql(
                """
-- Yanni
-- AND
-- Laurel
-- Yet
-- Another
-- Multiline
-- Tag

SELECT TOP 1 `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
ORDER BY `c`.`CustomerID`
""");
        }

        public override void Single_query_multiline_tag_with_empty_lines()
        {
            base.Single_query_multiline_tag_with_empty_lines();

            AssertSql(
                """
-- Yanni
-- 
-- AND
-- 
-- Laurel

SELECT TOP 1 `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
ORDER BY `c`.`CustomerID`
""");
        }

        private void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);
    }
}
