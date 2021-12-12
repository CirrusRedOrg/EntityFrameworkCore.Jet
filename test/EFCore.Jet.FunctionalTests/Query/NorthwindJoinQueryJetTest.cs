﻿// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query
{
    public class NorthwindJoinQueryJetTest : NorthwindJoinQueryRelationalTestBase<NorthwindQueryJetFixture<NoopModelCustomizer>>
    {
        public NorthwindJoinQueryJetTest(
            NorthwindQueryJetFixture<NoopModelCustomizer> fixture,
            ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            ClearLog();
            //Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
        }

        protected override bool CanExecuteQueryString
            => true;

        public override async Task Join_customers_orders_projection(bool isAsync)
        {
            await base.Join_customers_orders_projection(isAsync);

            AssertSql(
                $@"SELECT `c`.`ContactName`, `o`.`OrderID`
FROM `Customers` AS `c`
INNER JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`");
        }

        public override async Task Join_customers_orders_entities(bool isAsync)
        {
            await base.Join_customers_orders_entities(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Customers` AS `c`
INNER JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`");
        }

        public override async Task Join_select_many(bool isAsync)
        {
            await base.Join_select_many(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`, `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Customers` AS `c`
INNER JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`,
`Employees` AS `e`");
        }

        public override async Task Client_Join_select_many(bool isAsync)
        {
            await base.Client_Join_select_many(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='2'")}

SELECT `t0`.`EmployeeID`, `t0`.`City`, `t0`.`Country`, `t0`.`FirstName`, `t0`.`ReportsTo`, `t0`.`Title`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `e0`.`EmployeeID`, `e0`.`City`, `e0`.`Country`, `e0`.`FirstName`, `e0`.`ReportsTo`, `e0`.`Title`
    FROM `Employees` AS `e0`
    ORDER BY `e0`.`EmployeeID`
) AS `t0`",
                //
                $@"{AssertSqlHelper.Declaration("@__p_0='2'")}

SELECT `t`.`EmployeeID`, `t`.`City`, `t`.`Country`, `t`.`FirstName`, `t`.`ReportsTo`, `t`.`Title`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
    FROM `Employees` AS `e`
    ORDER BY `e`.`EmployeeID`
) AS `t`",
                //
                $@"SELECT `t1`.`EmployeeID`, `t1`.`City`, `t1`.`Country`, `t1`.`FirstName`, `t1`.`ReportsTo`, `t1`.`Title`
FROM (
    SELECT `e1`.`EmployeeID`, `e1`.`City`, `e1`.`Country`, `e1`.`FirstName`, `e1`.`ReportsTo`, `e1`.`Title`
    FROM `Employees` AS `e1`
    ORDER BY `e1`.`EmployeeID`
    SKIP 6 FETCH NEXT 2 ROWS ONLY
) AS `t1`",
                //
                $@"SELECT `t1`.`EmployeeID`, `t1`.`City`, `t1`.`Country`, `t1`.`FirstName`, `t1`.`ReportsTo`, `t1`.`Title`
FROM (
    SELECT `e1`.`EmployeeID`, `e1`.`City`, `e1`.`Country`, `e1`.`FirstName`, `e1`.`ReportsTo`, `e1`.`Title`
    FROM `Employees` AS `e1`
    ORDER BY `e1`.`EmployeeID`
    SKIP 6 FETCH NEXT 2 ROWS ONLY
) AS `t1`");
        }

        public override async Task Join_customers_orders_select(bool isAsync)
        {
            await base.Join_customers_orders_select(isAsync);

            AssertSql(
                $@"SELECT `c`.`ContactName`, `o`.`OrderID`
FROM `Customers` AS `c`
INNER JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`");
        }

        public override async Task Join_customers_orders_with_subquery(bool isAsync)
        {
            await base.Join_customers_orders_with_subquery(isAsync);

            AssertSql(
                $@"SELECT `c`.`ContactName`, `t`.`OrderID`
FROM `Customers` AS `c`
INNER JOIN (
    SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
    FROM `Orders` AS `o`
) AS `t` ON `c`.`CustomerID` = `t`.`CustomerID`
WHERE `t`.`CustomerID` = 'ALFKI'");
        }

        public override async Task Join_customers_orders_with_subquery_with_take(bool isAsync)
        {
            await base.Join_customers_orders_with_subquery_with_take(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='5'")}

SELECT `c`.`ContactName`, `t`.`OrderID`
FROM `Customers` AS `c`
INNER JOIN (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
    FROM `Orders` AS `o`
    ORDER BY `o`.`OrderID`
) AS `t` ON `c`.`CustomerID` = `t`.`CustomerID`
WHERE `t`.`CustomerID` = 'ALFKI'");
        }

        public override async Task Join_customers_orders_with_subquery_anonymous_property_method(bool isAsync)
        {
            await base.Join_customers_orders_with_subquery_anonymous_property_method(isAsync);

            AssertSql(
                $@"SELECT `t`.`OrderID`, `t`.`CustomerID`, `t`.`EmployeeID`, `t`.`OrderDate`
FROM `Customers` AS `c`
INNER JOIN (
    SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
    FROM `Orders` AS `o`
) AS `t` ON `c`.`CustomerID` = `t`.`CustomerID`
WHERE `t`.`CustomerID` = 'ALFKI'");
        }

        public override async Task Join_customers_orders_with_subquery_anonymous_property_method_with_take(bool isAsync)
        {
            await base.Join_customers_orders_with_subquery_anonymous_property_method_with_take(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='5'")}

SELECT `t`.`OrderID`, `t`.`CustomerID`, `t`.`EmployeeID`, `t`.`OrderDate`
FROM `Customers` AS `c`
INNER JOIN (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
    FROM `Orders` AS `o`
    ORDER BY `o`.`OrderID`
) AS `t` ON `c`.`CustomerID` = `t`.`CustomerID`
WHERE `t`.`CustomerID` = 'ALFKI'");
        }

        public override async Task Join_customers_orders_with_subquery_predicate(bool isAsync)
        {
            await base.Join_customers_orders_with_subquery_predicate(isAsync);

            AssertSql(
                $@"SELECT `c`.`ContactName`, `t`.`OrderID`
FROM `Customers` AS `c`
INNER JOIN (
    SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
    FROM `Orders` AS `o`
    WHERE `o`.`OrderID` > 0
) AS `t` ON `c`.`CustomerID` = `t`.`CustomerID`
WHERE `t`.`CustomerID` = 'ALFKI'");
        }

        public override async Task Join_customers_orders_with_subquery_predicate_with_take(bool isAsync)
        {
            await base.Join_customers_orders_with_subquery_predicate_with_take(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='5'")}

SELECT `c`.`ContactName`, `t`.`OrderID`
FROM `Customers` AS `c`
INNER JOIN (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
    FROM `Orders` AS `o`
    WHERE `o`.`OrderID` > 0
    ORDER BY `o`.`OrderID`
) AS `t` ON `c`.`CustomerID` = `t`.`CustomerID`
WHERE `t`.`CustomerID` = 'ALFKI'");
        }

        public override async Task Join_composite_key(bool isAsync)
        {
            await base.Join_composite_key(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Customers` AS `c`
INNER JOIN `Orders` AS `o` ON (`c`.`CustomerID` = `o`.`CustomerID`) AND (`c`.`CustomerID` = `o`.`CustomerID`)");
        }

        public override async Task Join_complex_condition(bool isAsync)
        {
            await base.Join_complex_condition(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`
FROM `Customers` AS `c`
INNER JOIN (
    SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
    FROM `Orders` AS `o`
    WHERE `o`.`OrderID` < 10250
) AS `t` ON True = True
WHERE `c`.`CustomerID` = 'ALFKI'");
        }

        public override async Task Join_same_collection_multiple(bool isAsync)
        {
            await base.Join_same_collection_multiple(isAsync);

            AssertSql(
                $@"SELECT `c1`.`CustomerID`, `c1`.`Address`, `c1`.`City`, `c1`.`CompanyName`, `c1`.`ContactName`, `c1`.`ContactTitle`, `c1`.`Country`, `c1`.`Fax`, `c1`.`Phone`, `c1`.`PostalCode`, `c1`.`Region`
FROM (`Customers` AS `c`
INNER JOIN `Customers` AS `c0` ON `c`.`CustomerID` = `c0`.`CustomerID`)
INNER JOIN `Customers` AS `c1` ON `c`.`CustomerID` = `c1`.`CustomerID`");
        }

        public override async Task Join_same_collection_force_alias_uniquefication(bool isAsync)
        {
            await base.Join_same_collection_force_alias_uniquefication(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`, `o0`.`OrderID`, `o0`.`CustomerID`, `o0`.`EmployeeID`, `o0`.`OrderDate`
FROM `Orders` AS `o`
INNER JOIN `Orders` AS `o0` ON `o`.`CustomerID` = `o0`.`CustomerID`");
        }

        public override async Task GroupJoin_simple(bool isAsync)
        {
            await base.GroupJoin_simple(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Customers` AS `c`
INNER JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`");
        }

        public override async Task GroupJoin_simple2(bool isAsync)
        {
            await base.GroupJoin_simple2(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
INNER JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`");
        }

        public override async Task GroupJoin_simple3(bool isAsync)
        {
            await base.GroupJoin_simple3(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`
FROM `Customers` AS `c`
INNER JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`");
        }

        public override async Task GroupJoin_simple_ordering(bool isAsync)
        {
            await base.GroupJoin_simple_ordering(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Customers` AS `c`
INNER JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`
ORDER BY `c`.`City`");
        }

        public override async Task GroupJoin_simple_subquery(bool isAsync)
        {
            await base.GroupJoin_simple_subquery(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='4'")}

SELECT `t`.`OrderID`, `t`.`CustomerID`, `t`.`EmployeeID`, `t`.`OrderDate`
FROM `Customers` AS `c`
INNER JOIN (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
    FROM `Orders` AS `o`
    ORDER BY `o`.`OrderID`
) AS `t` ON `c`.`CustomerID` = `t`.`CustomerID`");
        }

        public override async Task GroupJoin_DefaultIfEmpty(bool isAsync)
        {
            await base.GroupJoin_DefaultIfEmpty(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Customers` AS `c`
LEFT JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`");
        }

        public override async Task GroupJoin_DefaultIfEmpty_multiple(bool isAsync)
        {
            await base.GroupJoin_DefaultIfEmpty_multiple(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`, `o0`.`OrderID`, `o0`.`CustomerID`, `o0`.`EmployeeID`, `o0`.`OrderDate`
FROM `Customers` AS `c`
LEFT JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`
LEFT JOIN `Orders` AS `o0` ON `c`.`CustomerID` = `o0`.`CustomerID`");
        }

        public override async Task GroupJoin_DefaultIfEmpty2(bool isAsync)
        {
            await base.GroupJoin_DefaultIfEmpty2(isAsync);

            AssertSql(
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Employees` AS `e`
LEFT JOIN `Orders` AS `o` ON `e`.`EmployeeID` = `o`.`EmployeeID`");
        }

        public override async Task GroupJoin_DefaultIfEmpty3(bool isAsync)
        {
            await base.GroupJoin_DefaultIfEmpty3(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='1'")}

SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    ORDER BY `c`.`CustomerID`
) AS `t`
LEFT JOIN `Orders` AS `o` ON `t`.`CustomerID` = `o`.`CustomerID`
ORDER BY `t`.`CustomerID`");
        }

        public override async Task GroupJoin_Where(bool isAsync)
        {
            await base.GroupJoin_Where(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Customers` AS `c`
INNER JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`
WHERE `o`.`CustomerID` = 'ALFKI'");
        }

        public override async Task GroupJoin_Where_OrderBy(bool isAsync)
        {
            await base.GroupJoin_Where_OrderBy(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Customers` AS `c`
INNER JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`
WHERE (`o`.`CustomerID` = 'ALFKI') OR (`c`.`CustomerID` = 'ANATR')
ORDER BY `c`.`City`");
        }

        public override async Task GroupJoin_DefaultIfEmpty_Where(bool isAsync)
        {
            await base.GroupJoin_DefaultIfEmpty_Where(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Customers` AS `c`
LEFT JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`
WHERE `o`.`OrderID` IS NOT NULL AND (`o`.`CustomerID` = 'ALFKI')");
        }

        public override async Task Join_GroupJoin_DefaultIfEmpty_Where(bool isAsync)
        {
            await base.Join_GroupJoin_DefaultIfEmpty_Where(isAsync);

            AssertSql(
                $@"SELECT `o2`.`OrderID`, `o2`.`CustomerID`, `o2`.`EmployeeID`, `o2`.`OrderDate`
FROM `Customers` AS `c`
INNER JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`
LEFT JOIN `Orders` AS `o2` ON `c`.`CustomerID` = `o2`.`CustomerID`
WHERE `o2`.`OrderID` IS NOT NULL AND (`o2`.`CustomerID` = 'ALFKI')");
        }

        public override async Task GroupJoin_DefaultIfEmpty_Project(bool isAsync)
        {
            await base.GroupJoin_DefaultIfEmpty_Project(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`
FROM `Customers` AS `c`
LEFT JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`");
        }
        
        public override async Task GroupJoin_SelectMany_subquery_with_filter(bool isAsync)
        {
            await base.GroupJoin_SelectMany_subquery_with_filter(isAsync);

            AssertSql(
                $@"SELECT `c`.`ContactName`, `t`.`OrderID`
FROM `Customers` AS `c`
INNER JOIN (
    SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
    FROM `Orders` AS `o`
    WHERE `o`.`OrderID` > 5
) AS `t` ON `c`.`CustomerID` = `t`.`CustomerID`");
        }

        public override async Task GroupJoin_SelectMany_subquery_with_filter_orderby(bool isAsync)
        {
            await base.GroupJoin_SelectMany_subquery_with_filter_orderby(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Customers` AS `c`
LEFT JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`
ORDER BY `c`.`CustomerID`");
        }

        public override async Task GroupJoin_SelectMany_subquery_with_filter_and_DefaultIfEmpty(bool isAsync)
        {
            await base.GroupJoin_SelectMany_subquery_with_filter_and_DefaultIfEmpty(isAsync);

            AssertSql(
                $@"SELECT `c`.`ContactName`, `t`.`OrderID`, `t`.`CustomerID`, `t`.`EmployeeID`, `t`.`OrderDate`
FROM `Customers` AS `c`
LEFT JOIN (
    SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
    FROM `Orders` AS `o`
    WHERE `o`.`OrderID` > 5
) AS `t` ON `c`.`CustomerID` = `t`.`CustomerID`");
        }

        public override async Task GroupJoin_SelectMany_subquery_with_filter_orderby_and_DefaultIfEmpty(bool isAsync)
        {
            await base.GroupJoin_SelectMany_subquery_with_filter_orderby_and_DefaultIfEmpty(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Customers` AS `c`
LEFT JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`
ORDER BY `c`.`CustomerID`");
        }

        public override async Task GroupJoin_Subquery_with_Take_Then_SelectMany_Where(bool isAsync)
        {
            await base.GroupJoin_Subquery_with_Take_Then_SelectMany_Where(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='100'")}

SELECT `c`.`CustomerID`, `t0`.`OrderID`
FROM `Customers` AS `c`
INNER JOIN (
    SELECT `t`.`OrderID`, `t`.`CustomerID`, `t`.`EmployeeID`, `t`.`OrderDate`
    FROM (
        SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
        FROM `Orders` AS `o`
        ORDER BY `o`.`OrderID`
    ) AS `t`
    WHERE `t`.`CustomerID` IS NOT NULL AND (`t`.`CustomerID` LIKE 'A' & '%')
) AS `t0` ON `c`.`CustomerID` = `t0`.`CustomerID`");
        }

        [ConditionalTheory(Skip = "Can be supported after rearranging CROSS JOIN/JOIN expressions.")]
        public override Task GroupJoin_subquery_projection_outer_mixed(bool isAsync)
        {
            return base.GroupJoin_subquery_projection_outer_mixed(isAsync);
        }
        
        private void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);

        protected override void ClearLog()
            => Fixture.TestSqlLoggerFactory.Clear();
    }
}
