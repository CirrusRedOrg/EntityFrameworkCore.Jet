﻿// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query
{
    public class NorthwindAggregateOperatorsQueryJetTest : NorthwindAggregateOperatorsQueryRelationalTestBase<
        NorthwindQueryJetFixture<NoopModelCustomizer>>
    {
        public NorthwindAggregateOperatorsQueryJetTest(
            NorthwindQueryJetFixture<NoopModelCustomizer> fixture,
            ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            ClearLog();
            //Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
        }

        protected override bool CanExecuteQueryString
            => true;

        public override void Select_All()
        {
            base.Select_All();

            AssertSql(
                @"SELECT IIF(NOT EXISTS (
        SELECT 1
        FROM `Orders` AS `o`
        WHERE (`o`.`CustomerID` <> 'ALFKI') OR `o`.`CustomerID` IS NULL), TRUE, FALSE)
FROM (SELECT COUNT(*) FROM MSysAccessStorage)");
        }

        public override async Task Sum_with_no_arg(bool isAsync)
        {
            await base.Sum_with_no_arg(isAsync);

            AssertSql(
                $@"SELECT SUM(`o`.`OrderID`)
FROM `Orders` AS `o`");
        }

        public override async Task Sum_with_binary_expression(bool isAsync)
        {
            await base.Sum_with_binary_expression(isAsync);

            AssertSql(
                $@"SELECT SUM(`o`.`OrderID` * 2)
FROM `Orders` AS `o`");
        }

        public override async Task Sum_with_arg(bool isAsync)
        {
            await base.Sum_with_arg(isAsync);

            AssertSql(
                $@"SELECT SUM(`o`.`OrderID`)
FROM `Orders` AS `o`");
        }

        public override async Task Sum_with_arg_expression(bool isAsync)
        {
            await base.Sum_with_arg_expression(isAsync);

            AssertSql(
                $@"SELECT SUM(`o`.`OrderID` + `o`.`OrderID`)
FROM `Orders` AS `o`");
        }

        public override async Task Sum_with_division_on_decimal(bool isAsync)
        {
            await base.Sum_with_division_on_decimal(isAsync);

            AssertSql(
                $@"SELECT SUM(IIf(`o`.`Quantity` IS NULL, NULL, CCUR(`o`.`Quantity`)) / 2.09)
FROM `Order Details` AS `o`");
        }

        public override async Task Sum_with_division_on_decimal_no_significant_digits(bool isAsync)
        {
            await base.Sum_with_division_on_decimal_no_significant_digits(isAsync);

            AssertSql(
                $@"SELECT SUM(IIf(`o`.`Quantity` IS NULL, NULL, CCUR(`o`.`Quantity`)) / 2.0)
FROM `Order Details` AS `o`");
        }

        public override async Task Sum_with_coalesce(bool isAsync)
        {
            await base.Sum_with_coalesce(isAsync);

            AssertSql(
                $@"SELECT SUM(IIf(`p`.`UnitPrice` IS NULL, 0.0, `p`.`UnitPrice`))
FROM `Products` AS `p`
WHERE `p`.`ProductID` < 40");
        }

        public override async Task Sum_over_subquery_is_client_eval(bool isAsync)
        {
            await base.Sum_over_subquery_is_client_eval(isAsync);

            AssertSql(
                $@"SELECT (
    SELECT SUM(`o`.`OrderID`)
    FROM `Orders` AS `o`
    WHERE `c`.`CustomerID` = `o`.`CustomerID`
)
FROM `Customers` AS `c`");
        }

        public override async Task Sum_over_nested_subquery_is_client_eval(bool isAsync)
        {
            await base.Sum_over_nested_subquery_is_client_eval(isAsync);
            AssertSql(
                $@"SELECT `c`.`CustomerID`
FROM `Customers` AS `c`");
        }

        public override async Task Sum_over_min_subquery_is_client_eval(bool isAsync)
        {
            await base.Sum_over_min_subquery_is_client_eval(isAsync);
            AssertSql(
                $@"SELECT `c`.`CustomerID`
FROM `Customers` AS `c`");
        }

        public override async Task Sum_on_float_column(bool isAsync)
        {
            await base.Sum_on_float_column(isAsync);

            AssertSql(
                $@"SELECT IIf(SUM(`o`.`Discount`) IS NULL, NULL, CSNG(SUM(`o`.`Discount`)))
FROM `Order Details` AS `o`
WHERE `o`.`ProductID` = 1");
        }

        public override async Task Sum_on_float_column_in_subquery(bool isAsync)
        {
            await base.Sum_on_float_column_in_subquery(isAsync);

            AssertSql(
                $@"SELECT `o0`.`OrderID`, (
    SELECT IIf(SUM(`o`.`Discount`) IS NULL, NULL, CSNG(SUM(`o`.`Discount`)))
    FROM `Order Details` AS `o`
    WHERE `o0`.`OrderID` = `o`.`OrderID`) AS `Sum`
FROM `Orders` AS `o0`
WHERE `o0`.`OrderID` < 10300");
        }

        public override async Task Average_with_no_arg(bool isAsync)
        {
            await base.Average_with_no_arg(isAsync);

            AssertSql(
                $@"SELECT AVG(IIf(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`)))
FROM `Orders` AS `o`");
        }

        public override async Task Average_with_binary_expression(bool isAsync)
        {
            await base.Average_with_binary_expression(isAsync);

            AssertSql(
                $@"SELECT AVG(IIf(`o`.`OrderID` * 2 IS NULL, NULL, CDBL(`o`.`OrderID` * 2)))
FROM `Orders` AS `o`");
        }

        public override async Task Average_with_arg(bool isAsync)
        {
            await base.Average_with_arg(isAsync);

            AssertSql(
                $@"SELECT AVG(IIf(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`)))
FROM `Orders` AS `o`");
        }

        public override async Task Average_with_arg_expression(bool isAsync)
        {
            await base.Average_with_arg_expression(isAsync);

            AssertSql(
                $@"SELECT AVG(IIf(`o`.`OrderID` + `o`.`OrderID` IS NULL, NULL, CDBL((`o`.`OrderID` + `o`.`OrderID`))))
FROM `Orders` AS `o`");
        }

        public override async Task Average_with_division_on_decimal(bool isAsync)
        {
            await base.Average_with_division_on_decimal(isAsync);

            AssertSql(
                $@"SELECT AVG(IIf(`o`.`Quantity` IS NULL, NULL, CCUR(`o`.`Quantity`)) / 2.09)
FROM `Order Details` AS `o`");
        }

        public override async Task Average_with_division_on_decimal_no_significant_digits(bool isAsync)
        {
            await base.Average_with_division_on_decimal_no_significant_digits(isAsync);

            AssertSql(
                $@"SELECT AVG(IIf(`o`.`Quantity` IS NULL, NULL, CCUR(`o`.`Quantity`)) / 2.0)
FROM `Order Details` AS `o`");
        }

        public override async Task Average_with_coalesce(bool isAsync)
        {
            await base.Average_with_coalesce(isAsync);

            AssertSql(
                $@"SELECT AVG(IIf(`p`.`UnitPrice` IS NULL, 0.0, `p`.`UnitPrice`))
FROM `Products` AS `p`
WHERE `p`.`ProductID` < 40");
        }

        public override async Task Average_over_subquery_is_client_eval(bool isAsync)
        {
            await base.Average_over_subquery_is_client_eval(isAsync);

            AssertSql(
                $@"SELECT (
    SELECT SUM(`o`.`OrderID`)
    FROM `Orders` AS `o`
    WHERE `c`.`CustomerID` = `o`.`CustomerID`
)
FROM `Customers` AS `c`");
        }

        public override async Task Average_over_nested_subquery_is_client_eval(bool isAsync)
        {
            await base.Average_over_nested_subquery_is_client_eval(isAsync);
            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='3'")}

SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`CustomerID`
FROM `Customers` AS `c`
ORDER BY `c`.`CustomerID`");
        }

        public override async Task Average_over_max_subquery_is_client_eval(bool isAsync)
        {
            await base.Average_over_max_subquery_is_client_eval(isAsync);
            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='3'")}

SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`CustomerID`
FROM `Customers` AS `c`
ORDER BY `c`.`CustomerID`");
        }

        public override async Task Average_on_float_column(bool isAsync)
        {
            await base.Average_on_float_column(isAsync);

            AssertSql(
                $@"SELECT IIf(AVG(`o`.`Discount`) IS NULL, NULL, CSNG(AVG(`o`.`Discount`)))
FROM `Order Details` AS `o`
WHERE `o`.`ProductID` = 1");
        }

        public override async Task Average_on_float_column_in_subquery(bool isAsync)
        {
            await base.Average_on_float_column_in_subquery(isAsync);

            AssertSql(
                $@"SELECT `o0`.`OrderID`, (
    SELECT IIf(AVG(`o`.`Discount`) IS NULL, NULL, CSNG(AVG(`o`.`Discount`)))
    FROM `Order Details` AS `o`
    WHERE `o0`.`OrderID` = `o`.`OrderID`) AS `Sum`
FROM `Orders` AS `o0`
WHERE `o0`.`OrderID` < 10300");
        }

        public override async Task Average_on_float_column_in_subquery_with_cast(bool isAsync)
        {
            await base.Average_on_float_column_in_subquery_with_cast(isAsync);

            AssertSql(
                $@"SELECT `o0`.`OrderID`, (
    SELECT IIf(AVG(`o`.`Discount`) IS NULL, NULL, CSNG(AVG(`o`.`Discount`)))
    FROM `Order Details` AS `o`
    WHERE `o0`.`OrderID` = `o`.`OrderID`) AS `Sum`
FROM `Orders` AS `o0`
WHERE `o0`.`OrderID` < 10300");
        }

        public override async Task Min_with_no_arg(bool isAsync)
        {
            await base.Min_with_no_arg(isAsync);

            AssertSql(
                $@"SELECT MIN(`o`.`OrderID`)
FROM `Orders` AS `o`");
        }

        public override async Task Min_with_arg(bool isAsync)
        {
            await base.Min_with_arg(isAsync);

            AssertSql(
                $@"SELECT MIN(`o`.`OrderID`)
FROM `Orders` AS `o`");
        }

        public override async Task Min_with_coalesce(bool isAsync)
        {
            await base.Min_with_coalesce(isAsync);

            AssertSql(
                $@"SELECT MIN(IIf(`p`.`UnitPrice` IS NULL, 0.0, `p`.`UnitPrice`))
FROM `Products` AS `p`
WHERE `p`.`ProductID` < 40");
        }

        public override async Task Min_over_subquery_is_client_eval(bool isAsync)
        {
            await base.Min_over_subquery_is_client_eval(isAsync);

            AssertSql(
                $@"SELECT (
    SELECT SUM(`o`.`OrderID`)
    FROM `Orders` AS `o`
    WHERE `c`.`CustomerID` = `o`.`CustomerID`
)
FROM `Customers` AS `c`");
        }

        public override async Task Min_over_nested_subquery_is_client_eval(bool isAsync)
        {
            await base.Min_over_nested_subquery_is_client_eval(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='3'")}

SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`CustomerID`
FROM `Customers` AS `c`
ORDER BY `c`.`CustomerID`");
        }

        public override async Task Min_over_max_subquery_is_client_eval(bool isAsync)
        {
            await base.Min_over_max_subquery_is_client_eval(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='3'")}

SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`CustomerID`
FROM `Customers` AS `c`
ORDER BY `c`.`CustomerID`");
        }

        public override async Task Max_with_no_arg(bool isAsync)
        {
            await base.Max_with_no_arg(isAsync);

            AssertSql(
                $@"SELECT MAX(`o`.`OrderID`)
FROM `Orders` AS `o`");
        }

        public override async Task Max_with_arg(bool isAsync)
        {
            await base.Max_with_arg(isAsync);

            AssertSql(
                $@"SELECT MAX(`o`.`OrderID`)
FROM `Orders` AS `o`");
        }

        public override async Task Max_with_coalesce(bool isAsync)
        {
            await base.Max_with_coalesce(isAsync);

            AssertSql(
                $@"SELECT MAX(IIf(`p`.`UnitPrice` IS NULL, 0.0, `p`.`UnitPrice`))
FROM `Products` AS `p`
WHERE `p`.`ProductID` < 40");
        }

        public override async Task Max_over_subquery_is_client_eval(bool isAsync)
        {
            await base.Max_over_subquery_is_client_eval(isAsync);

            AssertSql(
                $@"SELECT (
    SELECT SUM(`o`.`OrderID`)
    FROM `Orders` AS `o`
    WHERE `c`.`CustomerID` = `o`.`CustomerID`
)
FROM `Customers` AS `c`");
        }

        public override async Task Max_over_nested_subquery_is_client_eval(bool isAsync)
        {
            await base.Max_over_nested_subquery_is_client_eval(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='3'")}

SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`CustomerID`
FROM `Customers` AS `c`
ORDER BY `c`.`CustomerID`");
        }

        public override async Task Max_over_sum_subquery_is_client_eval(bool isAsync)
        {
            await base.Max_over_sum_subquery_is_client_eval(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='3'")}

SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`CustomerID`
FROM `Customers` AS `c`
ORDER BY `c`.`CustomerID`");
        }

        public override async Task Count_with_predicate(bool isAsync)
        {
            await base.Count_with_predicate(isAsync);

            AssertSql(
                $@"SELECT COUNT(*)
FROM `Orders` AS `o`
WHERE `o`.`CustomerID` = 'ALFKI'");
        }

        public override async Task Where_OrderBy_Count(bool isAsync)
        {
            await base.Where_OrderBy_Count(isAsync);

            AssertSql(
                $@"SELECT COUNT(*)
FROM `Orders` AS `o`
WHERE `o`.`CustomerID` = 'ALFKI'");
        }

        public override async Task OrderBy_Where_Count(bool isAsync)
        {
            await base.OrderBy_Where_Count(isAsync);

            AssertSql(
                $@"SELECT COUNT(*)
FROM `Orders` AS `o`
WHERE `o`.`CustomerID` = 'ALFKI'");
        }

        public override async Task OrderBy_Count_with_predicate(bool isAsync)
        {
            await base.OrderBy_Count_with_predicate(isAsync);

            AssertSql(
                $@"SELECT COUNT(*)
FROM `Orders` AS `o`
WHERE `o`.`CustomerID` = 'ALFKI'");
        }

        public override async Task OrderBy_Where_Count_with_predicate(bool isAsync)
        {
            await base.OrderBy_Where_Count_with_predicate(isAsync);

            AssertSql(
                $@"SELECT COUNT(*)
FROM `Orders` AS `o`
WHERE (`o`.`OrderID` > 10) AND ((`o`.`CustomerID` <> 'ALFKI') OR `o`.`CustomerID` IS NULL)");
        }

        public override async Task Distinct(bool isAsync)
        {
            await base.Distinct(isAsync);

            AssertSql(
                $@"SELECT DISTINCT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`");
        }

        public override async Task Distinct_Scalar(bool isAsync)
        {
            await base.Distinct_Scalar(isAsync);

            AssertSql(
                $@"SELECT DISTINCT `c`.`City`
FROM `Customers` AS `c`");
        }

        public override async Task OrderBy_Distinct(bool isAsync)
        {
            await base.OrderBy_Distinct(isAsync);

            // Ordering not preserved by distinct when ordering columns not projected.
            AssertSql(
                $@"SELECT DISTINCT `c`.`City`
FROM `Customers` AS `c`");
        }

        public override async Task Distinct_OrderBy(bool isAsync)
        {
            await base.Distinct_OrderBy(isAsync);

            AssertSql(
                $@"SELECT `t`.`Country`
FROM (
    SELECT DISTINCT `c`.`Country`
    FROM `Customers` AS `c`
) AS `t`
ORDER BY `t`.`Country`");
        }

        public override async Task Distinct_OrderBy2(bool isAsync)
        {
            await base.Distinct_OrderBy2(isAsync);

            AssertSql(
                $@"SELECT `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`
FROM (
    SELECT DISTINCT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
) AS `t`
ORDER BY `t`.`CustomerID`");
        }

        public override async Task Distinct_OrderBy3(bool isAsync)
        {
            await base.Distinct_OrderBy3(isAsync);

            AssertSql(
                $@"SELECT `t`.`CustomerID`
FROM (
    SELECT DISTINCT `c`.`CustomerID`
    FROM `Customers` AS `c`
) AS `t`
ORDER BY `t`.`CustomerID`");
        }

        public override async Task Distinct_Count(bool isAsync)
        {
            await base.Distinct_Count(isAsync);

            AssertSql(
                $@"SELECT COUNT(*)
FROM (
    SELECT DISTINCT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
) AS `t`");
        }

        public override async Task Select_Select_Distinct_Count(bool isAsync)
        {
            await base.Select_Select_Distinct_Count(isAsync);

            AssertSql(
                $@"SELECT COUNT(*)
FROM (
    SELECT DISTINCT `c`.`City`
    FROM `Customers` AS `c`
) AS `t`");
        }

        public override async Task Single_Predicate(bool isAsync)
        {
            await base.Single_Predicate(isAsync);

            AssertSql(
                $@"SELECT TOP 2 `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` = 'ALFKI'");
        }

        public override async Task FirstOrDefault_inside_subquery_gets_server_evaluated(bool isAsync)
        {
            await base.FirstOrDefault_inside_subquery_gets_server_evaluated(isAsync);

            // issue #15994
//            AssertSql(
//                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
//FROM `Customers` AS `c`
//WHERE (`c`.`CustomerID` = 'ALFKI') AND ((
//    SELECT TOP 1 `o`.`CustomerID`
//    FROM `Orders` AS `o`
//    WHERE (`c`.`CustomerID` = `o`.`CustomerID`) AND (`o`.`CustomerID` = 'ALFKI')
//) = 'ALFKI')");
        }

        public override async Task Multiple_collection_navigation_with_FirstOrDefault_chained(bool isAsync)
        {
            await base.Multiple_collection_navigation_with_FirstOrDefault_chained(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`
FROM `Customers` AS `c`
ORDER BY `c`.`CustomerID`",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_CustomerID='ALFKI' (Size = 5)")}

SELECT TOP 1 `od`.`OrderID`, `od`.`ProductID`, `od`.`Discount`, `od`.`Quantity`, `od`.`UnitPrice`
FROM `Order Details` AS `od`
WHERE `od`.`OrderID` = COALESCE((
    SELECT TOP 1 `o`.`OrderID`
    FROM `Orders` AS `o`
    WHERE {AssertSqlHelper.Parameter("@_outer_CustomerID")} = `o`.`CustomerID`
    ORDER BY `o`.`OrderID`
), 0)
ORDER BY `od`.`ProductID`",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_CustomerID='ANATR' (Size = 5)")}

SELECT TOP 1 `od`.`OrderID`, `od`.`ProductID`, `od`.`Discount`, `od`.`Quantity`, `od`.`UnitPrice`
FROM `Order Details` AS `od`
WHERE `od`.`OrderID` = COALESCE((
    SELECT TOP 1 `o`.`OrderID`
    FROM `Orders` AS `o`
    WHERE {AssertSqlHelper.Parameter("@_outer_CustomerID")} = `o`.`CustomerID`
    ORDER BY `o`.`OrderID`
), 0)
ORDER BY `od`.`ProductID`");
        }

        public override async Task Multiple_collection_navigation_with_FirstOrDefault_chained_projecting_scalar(bool isAsync)
        {
            await base.Multiple_collection_navigation_with_FirstOrDefault_chained_projecting_scalar(isAsync);

            // issue #15994
//            AssertSql(
//                $@"SELECT (
//    SELECT TOP 1 `od`.`ProductID`
//    FROM `Order Details` AS `od`
//    WHERE `od`.`OrderID` = COALESCE((
//        SELECT TOP 1 `o`.`OrderID`
//        FROM `Orders` AS `o`
//        WHERE `c`.`CustomerID` = `o`.`CustomerID`
//        ORDER BY `o`.`OrderID`
//    ), 0)
//    ORDER BY `od`.`ProductID`
//)
//FROM `Customers` AS `c`
//ORDER BY `c`.`CustomerID`");
        }

        public override async Task First_inside_subquery_gets_client_evaluated(bool isAsync)
        {
            await base.First_inside_subquery_gets_client_evaluated(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE (`c`.`CustomerID` = 'ALFKI') AND ((
    SELECT TOP 1 `o`.`CustomerID`
    FROM `Orders` AS `o`
    WHERE (`c`.`CustomerID` = `o`.`CustomerID`) AND (`o`.`CustomerID` = 'ALFKI')) = 'ALFKI')");
        }

        public override async Task Last(bool isAsync)
        {
            await base.Last(isAsync);

            AssertSql(
                $@"SELECT TOP 1 `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
ORDER BY `c`.`ContactName` DESC");
        }

        public override async Task Last_Predicate(bool isAsync)
        {
            await base.Last_Predicate(isAsync);

            AssertSql(
                $@"SELECT TOP 1 `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`City` = 'London'
ORDER BY `c`.`ContactName` DESC");
        }

        public override async Task Where_Last(bool isAsync)
        {
            await base.Where_Last(isAsync);

            AssertSql(
                $@"SELECT TOP 1 `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`City` = 'London'
ORDER BY `c`.`ContactName` DESC");
        }

        public override async Task LastOrDefault(bool isAsync)
        {
            await base.LastOrDefault(isAsync);

            AssertSql(
                $@"SELECT TOP 1 `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
ORDER BY `c`.`ContactName` DESC");
        }

        public override async Task LastOrDefault_Predicate(bool isAsync)
        {
            await base.LastOrDefault_Predicate(isAsync);

            AssertSql(
                $@"SELECT TOP 1 `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`City` = 'London'
ORDER BY `c`.`ContactName` DESC");
        }

        public override async Task Where_LastOrDefault(bool isAsync)
        {
            await base.Where_LastOrDefault(isAsync);

            AssertSql(
                $@"SELECT TOP 1 `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`City` = 'London'
ORDER BY `c`.`ContactName` DESC");
        }

        public override async Task Contains_with_subquery(bool isAsync)
        {
            await base.Contains_with_subquery(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` IN (
    SELECT `o`.`CustomerID`
    FROM `Orders` AS `o`
)");
        }

        public override async Task Contains_with_local_array_closure(bool isAsync)
        {
            await base.Contains_with_local_array_closure(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` IN ('ABCDE', 'ALFKI')",
                //
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` IN ('ABCDE')");
        }

        public override async Task Contains_with_subquery_and_local_array_closure(bool isAsync)
        {
            await base.Contains_with_subquery_and_local_array_closure(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE EXISTS (
    SELECT 1
    FROM `Customers` AS `c0`
    WHERE `c0`.`City` IN ('London', 'Buenos Aires') AND (`c0`.`CustomerID` = `c`.`CustomerID`))",
                //
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE EXISTS (
    SELECT 1
    FROM `Customers` AS `c0`
    WHERE `c0`.`City` IN ('London') AND (`c0`.`CustomerID` = `c`.`CustomerID`))");
        }

        public override async Task Contains_with_local_uint_array_closure(bool isAsync)
        {
            await base.Contains_with_local_uint_array_closure(isAsync);

            AssertSql(
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE `e`.`EmployeeID` IN (0, 1)",
                //
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE `e`.`EmployeeID` IN (0)");
        }

        public override async Task Contains_with_local_nullable_uint_array_closure(bool isAsync)
        {
            await base.Contains_with_local_nullable_uint_array_closure(isAsync);

            AssertSql(
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE `e`.`EmployeeID` IN (0, 1)",
                //
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE `e`.`EmployeeID` IN (0)");
        }

        public override async Task Contains_with_local_array_inline(bool isAsync)
        {
            await base.Contains_with_local_array_inline(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` IN ('ABCDE', 'ALFKI')");
        }

        public override async Task Contains_with_local_list_closure(bool isAsync)
        {
            await base.Contains_with_local_list_closure(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` IN ('ABCDE', 'ALFKI')");
        }

        public override async Task Contains_with_local_object_list_closure(bool isAsync)
        {
            await base.Contains_with_local_object_list_closure(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` IN ('ABCDE', 'ALFKI')");
        }

        public override async Task Contains_with_local_list_closure_all_null(bool isAsync)
        {
            await base.Contains_with_local_list_closure_all_null(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE False = True");
        }

        public override async Task Contains_with_local_list_inline(bool isAsync)
        {
            await base.Contains_with_local_list_inline(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` IN ('ABCDE', 'ALFKI')");
        }

        public override async Task Contains_with_local_list_inline_closure_mix(bool isAsync)
        {
            await base.Contains_with_local_list_inline_closure_mix(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` IN ('ABCDE', 'ALFKI')",
                //
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` IN ('ABCDE', 'ANATR')");
        }

        public override async Task Contains_with_local_non_primitive_list_inline_closure_mix(bool isAsync)
        {
            await base.Contains_with_local_non_primitive_list_inline_closure_mix(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` IN ('ABCDE', 'ALFKI')",
                //
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` IN ('ABCDE', 'ANATR')");
        }

        public override async Task Contains_with_local_non_primitive_list_closure_mix(bool isAsync)
        {
            await base.Contains_with_local_non_primitive_list_closure_mix(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` IN ('ABCDE', 'ALFKI')");
        }

        public override async Task Contains_with_local_collection_false(bool isAsync)
        {
            await base.Contains_with_local_collection_false(isAsync);

            // issue #15994
//            AssertSql(
//                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
//FROM `Customers` AS `c`
//WHERE `c`.`CustomerID` NOT IN ('ABCDE', 'ALFKI')");
        }

        public override async Task Contains_with_local_collection_complex_predicate_and(bool isAsync)
        {
            await base.Contains_with_local_collection_complex_predicate_and(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE ((`c`.`CustomerID` = 'ALFKI') OR (`c`.`CustomerID` = 'ABCDE')) AND `c`.`CustomerID` IN ('ABCDE', 'ALFKI')");
        }

        public override async Task Contains_with_local_collection_complex_predicate_or(bool isAsync)
        {
            await base.Contains_with_local_collection_complex_predicate_or(isAsync);

            // issue #15994
//            AssertSql(
//                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
//FROM `Customers` AS `c`
//WHERE `c`.`CustomerID` IN ('ABCDE', 'ALFKI', 'ALFKI', 'ABCDE')");
        }

        public override async Task Contains_with_local_collection_complex_predicate_not_matching_ins1(bool isAsync)
        {
            await base.Contains_with_local_collection_complex_predicate_not_matching_ins1(isAsync);

            // issue #15994
//            AssertSql(
//                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
//FROM `Customers` AS `c`
//WHERE `c`.`CustomerID` IN ('ALFKI', 'ABCDE') OR `c`.`CustomerID` NOT IN ('ABCDE', 'ALFKI')");
        }

        public override async Task Contains_with_local_collection_complex_predicate_not_matching_ins2(bool isAsync)
        {
            await base.Contains_with_local_collection_complex_predicate_not_matching_ins2(isAsync);

            // issue #15944
//            AssertSql(
//                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
//FROM `Customers` AS `c`
//WHERE `c`.`CustomerID` IN ('ABCDE', 'ALFKI') AND `c`.`CustomerID` NOT IN ('ALFKI', 'ABCDE')");
        }

        public override async Task Contains_with_local_collection_sql_injection(bool isAsync)
        {
            await base.Contains_with_local_collection_sql_injection(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` IN ('ALFKI', 'ABC'')); GO; DROP TABLE Orders; GO; --') OR ((`c`.`CustomerID` = 'ALFKI') OR (`c`.`CustomerID` = 'ABCDE'))");
        }

        public override async Task Contains_with_local_collection_empty_closure(bool isAsync)
        {
            await base.Contains_with_local_collection_empty_closure(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE True = False");
        }

        public override async Task Contains_with_local_collection_empty_inline(bool isAsync)
        {
            await base.Contains_with_local_collection_empty_inline(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE True = True");
        }

        public override async Task Contains_top_level(bool isAsync)
        {
            await base.Contains_top_level(isAsync);

            // issue #15994
//            AssertSql(
//                $@"{AssertSqlHelper.Declaration("@__p_0='ALFKI' (Size = 4000)")}

//SELECT CASE
//    WHEN @__p_0 IN (
//        SELECT `c`.`CustomerID`
//        FROM `Customers` AS `c`
//    )
//    THEN True ELSE False
//END");
        }

        public override async Task Contains_with_local_anonymous_type_array_closure(bool isAsync)
        {
            await base.Contains_with_local_anonymous_type_array_closure(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`",
                //
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`");
        }

        public override void OfType_Select()
        {
            base.OfType_Select();

            AssertSql(
                $@"SELECT TOP 1 `c`.`City`
FROM `Orders` AS `o`
LEFT JOIN `Customers` AS `c` ON `o`.`CustomerID` = `c`.`CustomerID`
ORDER BY `o`.`OrderID`");
        }

        public override void OfType_Select_OfType_Select()
        {
            base.OfType_Select_OfType_Select();

            AssertSql(
                $@"SELECT TOP 1 `c`.`City`
FROM `Orders` AS `o`
LEFT JOIN `Customers` AS `c` ON `o`.`CustomerID` = `c`.`CustomerID`
ORDER BY `o`.`OrderID`");
        }

        
        public override async Task Average_with_non_matching_types_in_projection_doesnt_produce_second_explicit_cast(bool isAsync)
        {
            await base.Average_with_non_matching_types_in_projection_doesnt_produce_second_explicit_cast(isAsync);

            AssertSql(
                $@"SELECT AVG(IIf(CAST(`o`.`OrderID` AS bigint) IS NULL, NULL, CDBL(CAST(`o`.`OrderID` AS bigint))))
FROM `Orders` AS `o`
WHERE `o`.`CustomerID` IS NOT NULL AND (`o`.`CustomerID` LIKE 'A' & '%')");
        }

        public override async Task Max_with_non_matching_types_in_projection_introduces_explicit_cast(bool isAsync)
        {
            await base.Max_with_non_matching_types_in_projection_introduces_explicit_cast(isAsync);

            AssertSql(
                $@"SELECT MAX(CAST(`o`.`OrderID` AS bigint))
FROM `Orders` AS `o`
WHERE `o`.`CustomerID` IS NOT NULL AND (`o`.`CustomerID` LIKE 'A' & '%')");
        }

        public override async Task Min_with_non_matching_types_in_projection_introduces_explicit_cast(bool isAsync)
        {
            await base.Min_with_non_matching_types_in_projection_introduces_explicit_cast(isAsync);

            AssertSql(
                $@"SELECT MIN(CAST(`o`.`OrderID` AS bigint))
FROM `Orders` AS `o`
WHERE `o`.`CustomerID` IS NOT NULL AND (`o`.`CustomerID` LIKE 'A' & '%')");
        }

        public override async Task OrderBy_Take_Last_gives_correct_result(bool isAsync)
        {
            await base.OrderBy_Take_Last_gives_correct_result(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='20'")}

SELECT TOP 1 `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    ORDER BY `c`.`CustomerID`
) AS `t`
ORDER BY `t`.`CustomerID` DESC");
        }

        public override async Task OrderBy_Skip_Last_gives_correct_result(bool isAsync)
        {
            await base.OrderBy_Skip_Last_gives_correct_result(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='20'")}

SELECT TOP 1 `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`
FROM (
    SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    ORDER BY `c`.`CustomerID`
    SKIP {AssertSqlHelper.Parameter("@__p_0")}
) AS `t`
ORDER BY `t`.`CustomerID` DESC");
        }

        public override void Contains_over_entityType_should_rewrite_to_identity_equality()
        {
            base.Contains_over_entityType_should_rewrite_to_identity_equality();

            AssertSql(
                $@"SELECT TOP 2 `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`OrderID` = 10248",
                //
                $@"{AssertSqlHelper.Declaration("@__entity_equality_p_0_OrderID='10248' (Nullable = true)")}

SELECT CASE
    WHEN {AssertSqlHelper.Parameter("@__entity_equality_p_0_OrderID")} IN (
        SELECT `o`.`OrderID`
        FROM `Orders` AS `o`
        WHERE `o`.`CustomerID` = 'VINET'
    )
     THEN True
    ELSE False
END");
        }

        public override async Task List_Contains_over_entityType_should_rewrite_to_identity_equality(bool isAsync)
        {
            await base.List_Contains_over_entityType_should_rewrite_to_identity_equality(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__entity_equality_someOrder_0_OrderID='10248' (Nullable = true)")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE {AssertSqlHelper.Parameter("@__entity_equality_someOrder_0_OrderID")} IN (
    SELECT `o`.`OrderID`
    FROM `Orders` AS `o`
    WHERE `c`.`CustomerID` = `o`.`CustomerID`
)");
        }

        public override async Task List_Contains_with_constant_list(bool isAsync)
        {
            await base.List_Contains_with_constant_list(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` IN ('ALFKI', 'ANATR')");
        }

        public override async Task List_Contains_with_parameter_list(bool isAsync)
        {
            await base.List_Contains_with_parameter_list(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` IN ('ALFKI', 'ANATR')");
        }

        public override async Task Contains_with_parameter_list_value_type_id(bool isAsync)
        {
            await base.Contains_with_parameter_list_value_type_id(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`OrderID` IN (10248, 10249)");
        }

        public override async Task Contains_with_constant_list_value_type_id(bool isAsync)
        {
            await base.Contains_with_constant_list_value_type_id(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`OrderID` IN (10248, 10249)");
        }

        public override async Task HashSet_Contains_with_parameter(bool isAsync)
        {
            await base.HashSet_Contains_with_parameter(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` IN ('ALFKI')");
        }

        public override async Task ImmutableHashSet_Contains_with_parameter(bool isAsync)
        {
            await base.ImmutableHashSet_Contains_with_parameter(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` IN ('ALFKI')");
        }
        
        public override async Task Project_constant_Sum(bool isAsync)
        {
            await base.Project_constant_Sum(isAsync);

            AssertSql(
                $@"SELECT SUM(1)
FROM `Employees` AS `e`");
        }

        public override async Task Where_subquery_any_equals_operator(bool isAsync)
        {
            await base.Where_subquery_any_equals_operator(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` IN ('ABCDE', 'ALFKI', 'ANATR')");
        }

        public override async Task Where_subquery_any_equals(bool isAsync)
        {
            await base.Where_subquery_any_equals(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` IN ('ABCDE', 'ALFKI', 'ANATR')");
        }

        public override async Task Where_subquery_any_equals_static(bool isAsync)
        {
            await base.Where_subquery_any_equals_static(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` IN ('ABCDE', 'ALFKI', 'ANATR')");
        }

        public override async Task Where_subquery_where_any(bool isAsync)
        {
            await base.Where_subquery_where_any(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE (`c`.`City` = 'México D.F.') AND `c`.`CustomerID` IN ('ABCDE', 'ALFKI', 'ANATR')",
                //
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE (`c`.`City` = 'México D.F.') AND `c`.`CustomerID` IN ('ABCDE', 'ALFKI', 'ANATR')");
        }

        public override async Task Where_subquery_all_not_equals_operator(bool isAsync)
        {
            await base.Where_subquery_all_not_equals_operator(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` NOT IN ('ABCDE', 'ALFKI', 'ANATR')");
        }

        public override async Task Where_subquery_all_not_equals(bool isAsync)
        {
            await base.Where_subquery_all_not_equals(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` NOT IN ('ABCDE', 'ALFKI', 'ANATR')");
        }

        public override async Task Where_subquery_all_not_equals_static(bool isAsync)
        {
            await base.Where_subquery_all_not_equals_static(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` NOT IN ('ABCDE', 'ALFKI', 'ANATR')");
        }

        public override async Task Where_subquery_where_all(bool isAsync)
        {
            await base.Where_subquery_where_all(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE (`c`.`City` = 'México D.F.') AND `c`.`CustomerID` NOT IN ('ABCDE', 'ALFKI', 'ANATR')",
                //
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE (`c`.`City` = 'México D.F.') AND `c`.`CustomerID` NOT IN ('ABCDE', 'ALFKI', 'ANATR')");
        }

        public override async Task Cast_to_same_Type_Count_works(bool isAsync)
        {
            await base.Cast_to_same_Type_Count_works(isAsync);

            AssertSql(
                $@"SELECT COUNT(*)
FROM `Customers` AS `c`");
        }

        public override async Task Cast_before_aggregate_is_preserved(bool isAsync)
        {
            await base.Cast_before_aggregate_is_preserved(isAsync);

            AssertSql(
                $@"SELECT (
    SELECT AVG(IIf(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`)))
    FROM `Orders` AS `o`
    WHERE `c`.`CustomerID` = `o`.`CustomerID`)
FROM `Customers` AS `c`");
        }

        public override async Task DefaultIfEmpty_selects_only_required_columns(bool isAsync)
        {
            await base.DefaultIfEmpty_selects_only_required_columns(isAsync);

            AssertSql(
                $@"SELECT `p`.`ProductName`
FROM (
    SELECT NULL AS `empty`
) AS `empty`
LEFT JOIN `Products` AS `p` ON 1 = 1");
        }

        public override async Task Collection_Last_member_access_in_projection_translated(bool isAsync)
        {
            await base.Collection_Last_member_access_in_projection_translated(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE (`c`.`CustomerID` LIKE 'F' & '%') AND ((
    SELECT TOP 1 `o`.`CustomerID`
    FROM `Orders` AS `o`
    WHERE `c`.`CustomerID` = `o`.`CustomerID`
    ORDER BY `o`.`OrderID`) = `c`.`CustomerID`)");
        }

        public override async Task Collection_LastOrDefault_member_access_in_projection_translated(bool isAsync)
        {
            await base.Collection_LastOrDefault_member_access_in_projection_translated(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE (`c`.`CustomerID` LIKE 'F' & '%') AND ((
    SELECT TOP 1 `o`.`CustomerID`
    FROM `Orders` AS `o`
    WHERE `c`.`CustomerID` = `o`.`CustomerID`
    ORDER BY `o`.`OrderID`) = `c`.`CustomerID`)");
        }

        public override async Task Sum_over_explicit_cast_over_column(bool isAsync)
        {
            await base.Sum_over_explicit_cast_over_column(isAsync);

            AssertSql(
                $@"SELECT SUM(CAST(`o`.`OrderID` AS bigint))
FROM `Orders` AS `o`");
        }

        public override async Task Count_on_projection_with_client_eval(bool isAsync)
        {
            await base.Count_on_projection_with_client_eval(isAsync);

            AssertSql(
                $@"SELECT COUNT(*)
FROM `Orders` AS `o`",
                //
                $@"SELECT COUNT(*)
FROM `Orders` AS `o`",
                //
                $@"SELECT COUNT(*)
FROM `Orders` AS `o`");
        }

        private void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);

        protected override void ClearLog()
            => Fixture.TestSqlLoggerFactory.Clear();
    }
}
