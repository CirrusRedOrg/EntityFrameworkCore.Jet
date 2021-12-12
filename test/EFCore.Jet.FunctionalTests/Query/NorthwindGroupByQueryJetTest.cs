// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query
{
    public class NorthwindGroupByQueryJetTest : NorthwindGroupByQueryTestBase<NorthwindQueryJetFixture<NoopModelCustomizer>>
    {
        // ReSharper disable once UnusedParameter.Local
        public NorthwindGroupByQueryJetTest(NorthwindQueryJetFixture<NoopModelCustomizer> fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            Fixture.TestSqlLoggerFactory.Clear();
            //Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
        }

        public override async Task GroupBy_Property_Select_Average(bool isAsync)
        {
            await base.GroupBy_Property_Select_Average(isAsync);

            AssertSql(
                $@"SELECT AVG(IIf(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`)))
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");

            // Validating that we don't generate warning when translating GroupBy. See Issue#11157
            Assert.DoesNotContain(
                "The LINQ expression 'GroupBy(`o`.CustomerID, `o`)' could not be translated and will be evaluated locally.",
                Fixture.TestSqlLoggerFactory.Log.Select(l => l.Message));
        }
        
        public override async Task GroupBy_Property_Select_Count(bool isAsync)
        {
            await base.GroupBy_Property_Select_Count(isAsync);

            AssertSql(
                $@"SELECT COUNT(*)
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_Property_Select_LongCount(bool isAsync)
        {
            await base.GroupBy_Property_Select_LongCount(isAsync);

            AssertSql(
                $@"SELECT COUNT_BIG(*)
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_Property_Select_Max(bool isAsync)
        {
            await base.GroupBy_Property_Select_Max(isAsync);

            AssertSql(
                $@"SELECT MAX(`o`.`OrderID`)
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_Property_Select_Min(bool isAsync)
        {
            await base.GroupBy_Property_Select_Min(isAsync);

            AssertSql(
                $@"SELECT MIN(`o`.`OrderID`)
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_Property_Select_Sum(bool isAsync)
        {
            await base.GroupBy_Property_Select_Sum(isAsync);

            AssertSql(
                $@"SELECT SUM(`o`.`OrderID`)
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_Property_Select_Sum_Min_Max_Avg(bool isAsync)
        {
            await base.GroupBy_Property_Select_Sum_Min_Max_Avg(isAsync);

            AssertSql(
                $@"SELECT SUM(`o`.`OrderID`) AS `Sum`, MIN(`o`.`OrderID`) AS `Min`, MAX(`o`.`OrderID`) AS `Max`, AVG(IIf(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`))) AS `Avg`
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_Property_Select_Key_Average(bool isAsync)
        {
            await base.GroupBy_Property_Select_Key_Average(isAsync);

            AssertSql(
                $@"SELECT `o`.`CustomerID` AS `Key`, AVG(IIf(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`))) AS `Average`
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_Property_Select_Key_Count(bool isAsync)
        {
            await base.GroupBy_Property_Select_Key_Count(isAsync);

            AssertSql(
                $@"SELECT `o`.`CustomerID` AS `Key`, COUNT(*) AS `Count`
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_Property_Select_Key_LongCount(bool isAsync)
        {
            await base.GroupBy_Property_Select_Key_LongCount(isAsync);

            AssertSql(
                $@"SELECT `o`.`CustomerID` AS `Key`, COUNT_BIG(*) AS `LongCount`
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_Property_Select_Key_Max(bool isAsync)
        {
            await base.GroupBy_Property_Select_Key_Max(isAsync);

            AssertSql(
                $@"SELECT `o`.`CustomerID` AS `Key`, MAX(`o`.`OrderID`) AS `Max`
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_Property_Select_Key_Min(bool isAsync)
        {
            await base.GroupBy_Property_Select_Key_Min(isAsync);

            AssertSql(
                $@"SELECT `o`.`CustomerID` AS `Key`, MIN(`o`.`OrderID`) AS `Min`
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_Property_Select_Key_Sum(bool isAsync)
        {
            await base.GroupBy_Property_Select_Key_Sum(isAsync);

            AssertSql(
                $@"SELECT `o`.`CustomerID` AS `Key`, SUM(`o`.`OrderID`) AS `Sum`
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_Property_Select_Key_Sum_Min_Max_Avg(bool isAsync)
        {
            await base.GroupBy_Property_Select_Key_Sum_Min_Max_Avg(isAsync);

            AssertSql(
                $@"SELECT `o`.`CustomerID` AS `Key`, SUM(`o`.`OrderID`) AS `Sum`, MIN(`o`.`OrderID`) AS `Min`, MAX(`o`.`OrderID`) AS `Max`, AVG(IIf(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`))) AS `Avg`
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_Property_Select_Sum_Min_Key_Max_Avg(bool isAsync)
        {
            await base.GroupBy_Property_Select_Sum_Min_Key_Max_Avg(isAsync);

            AssertSql(
                $@"SELECT SUM(`o`.`OrderID`) AS `Sum`, MIN(`o`.`OrderID`) AS `Min`, `o`.`CustomerID` AS `Key`, MAX(`o`.`OrderID`) AS `Max`, AVG(IIf(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`))) AS `Avg`
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_Property_Select_key_multiple_times_and_aggregate(bool isAsync)
        {
            await base.GroupBy_Property_Select_key_multiple_times_and_aggregate(isAsync);

            AssertSql(
                $@"SELECT `o`.`CustomerID` AS `Key1`, SUM(`o`.`OrderID`) AS `Sum`
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_Property_Select_Key_with_constant(bool isAsync)
        {
            await base.GroupBy_Property_Select_Key_with_constant(isAsync);

            AssertSql(
                $@"SELECT 'CustomerID' AS `Name`, `o`.`CustomerID` AS `Value`, COUNT(*) AS `Count`
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_aggregate_projecting_conditional_expression(bool isAsync)
        {
            await base.GroupBy_aggregate_projecting_conditional_expression(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderDate` AS `Key`, CASE
    WHEN COUNT(*) = 0 THEN 1
    ELSE SUM(CASE
        WHEN (`o`.`OrderID` MOD 2) = 0 THEN 1
        ELSE 0
    END) / COUNT(*)
END AS `SomeValue`
FROM `Orders` AS `o`
GROUP BY `o`.`OrderDate`");
        }

        public override async Task GroupBy_aggregate_projecting_conditional_expression_based_on_group_key(bool isAsync)
        {
            await base.GroupBy_aggregate_projecting_conditional_expression_based_on_group_key(isAsync);

            AssertSql(
                $@"SELECT CASE
    WHEN `o`.`OrderDate` IS NULL THEN 'is null'
    ELSE 'is not null'
END AS `Key`, SUM(`o`.`OrderID`) AS `Sum`
FROM `Orders` AS `o`
GROUP BY `o`.`OrderDate`");
        }

        public override async Task GroupBy_anonymous_Select_Average(bool isAsync)
        {
            await base.GroupBy_anonymous_Select_Average(isAsync);

            AssertSql(
                $@"SELECT AVG(IIf(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`)))
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_anonymous_Select_Count(bool isAsync)
        {
            await base.GroupBy_anonymous_Select_Count(isAsync);

            AssertSql(
                $@"SELECT COUNT(*)
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_anonymous_Select_LongCount(bool isAsync)
        {
            await base.GroupBy_anonymous_Select_LongCount(isAsync);

            AssertSql(
                $@"SELECT COUNT_BIG(*)
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_anonymous_Select_Max(bool isAsync)
        {
            await base.GroupBy_anonymous_Select_Max(isAsync);

            AssertSql(
                $@"SELECT MAX(`o`.`OrderID`)
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_anonymous_Select_Min(bool isAsync)
        {
            await base.GroupBy_anonymous_Select_Min(isAsync);

            AssertSql(
                $@"SELECT MIN(`o`.`OrderID`)
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_anonymous_Select_Sum(bool isAsync)
        {
            await base.GroupBy_anonymous_Select_Sum(isAsync);

            AssertSql(
                $@"SELECT SUM(`o`.`OrderID`)
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_anonymous_Select_Sum_Min_Max_Avg(bool isAsync)
        {
            await base.GroupBy_anonymous_Select_Sum_Min_Max_Avg(isAsync);

            AssertSql(
                $@"SELECT SUM(`o`.`OrderID`) AS `Sum`, MIN(`o`.`OrderID`) AS `Min`, MAX(`o`.`OrderID`) AS `Max`, AVG(IIf(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`))) AS `Avg`
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_anonymous_with_alias_Select_Key_Sum(bool isAsync)
        {
            await base.GroupBy_anonymous_with_alias_Select_Key_Sum(isAsync);

            AssertSql(
                $@"SELECT `o`.`CustomerID` AS `Key`, SUM(`o`.`OrderID`) AS `Sum`
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_Composite_Select_Average(bool isAsync)
        {
            await base.GroupBy_Composite_Select_Average(isAsync);

            AssertSql(
                $@"SELECT AVG(IIf(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`)))
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`, `o`.`EmployeeID`");
        }

        public override async Task GroupBy_Composite_Select_Count(bool isAsync)
        {
            await base.GroupBy_Composite_Select_Count(isAsync);

            AssertSql(
                $@"SELECT COUNT(*)
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`, `o`.`EmployeeID`");
        }

        public override async Task GroupBy_Composite_Select_LongCount(bool isAsync)
        {
            await base.GroupBy_Composite_Select_LongCount(isAsync);

            AssertSql(
                $@"SELECT COUNT_BIG(*)
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`, `o`.`EmployeeID`");
        }

        public override async Task GroupBy_Composite_Select_Max(bool isAsync)
        {
            await base.GroupBy_Composite_Select_Max(isAsync);

            AssertSql(
                $@"SELECT MAX(`o`.`OrderID`)
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`, `o`.`EmployeeID`");
        }

        public override async Task GroupBy_Composite_Select_Min(bool isAsync)
        {
            await base.GroupBy_Composite_Select_Min(isAsync);

            AssertSql(
                $@"SELECT MIN(`o`.`OrderID`)
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`, `o`.`EmployeeID`");
        }

        public override async Task GroupBy_Composite_Select_Sum(bool isAsync)
        {
            await base.GroupBy_Composite_Select_Sum(isAsync);

            AssertSql(
                $@"SELECT SUM(`o`.`OrderID`)
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`, `o`.`EmployeeID`");
        }

        public override async Task GroupBy_Composite_Select_Sum_Min_Max_Avg(bool isAsync)
        {
            await base.GroupBy_Composite_Select_Sum_Min_Max_Avg(isAsync);

            AssertSql(
                $@"SELECT SUM(`o`.`OrderID`) AS `Sum`, MIN(`o`.`OrderID`) AS `Min`, MAX(`o`.`OrderID`) AS `Max`, AVG(IIf(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`))) AS `Avg`
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`, `o`.`EmployeeID`");
        }

        public override async Task GroupBy_Composite_Select_Key_Average(bool isAsync)
        {
            await base.GroupBy_Composite_Select_Key_Average(isAsync);

            AssertSql(
                $@"SELECT `o`.`CustomerID`, `o`.`EmployeeID`, AVG(IIf(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`))) AS `Average`
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`, `o`.`EmployeeID`");
        }

        public override async Task GroupBy_Composite_Select_Key_Count(bool isAsync)
        {
            await base.GroupBy_Composite_Select_Key_Count(isAsync);

            AssertSql(
                $@"SELECT `o`.`CustomerID`, `o`.`EmployeeID`, COUNT(*) AS `Count`
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`, `o`.`EmployeeID`");
        }

        public override async Task GroupBy_Composite_Select_Key_LongCount(bool isAsync)
        {
            await base.GroupBy_Composite_Select_Key_LongCount(isAsync);

            AssertSql(
                $@"SELECT `o`.`CustomerID`, `o`.`EmployeeID`, COUNT_BIG(*) AS `LongCount`
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`, `o`.`EmployeeID`");
        }

        public override async Task GroupBy_Composite_Select_Key_Max(bool isAsync)
        {
            await base.GroupBy_Composite_Select_Key_Max(isAsync);

            AssertSql(
                $@"SELECT `o`.`CustomerID`, `o`.`EmployeeID`, MAX(`o`.`OrderID`) AS `Max`
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`, `o`.`EmployeeID`");
        }

        public override async Task GroupBy_Composite_Select_Key_Min(bool isAsync)
        {
            await base.GroupBy_Composite_Select_Key_Min(isAsync);

            AssertSql(
                $@"SELECT `o`.`CustomerID`, `o`.`EmployeeID`, MIN(`o`.`OrderID`) AS `Min`
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`, `o`.`EmployeeID`");
        }

        public override async Task GroupBy_Composite_Select_Key_Sum(bool isAsync)
        {
            await base.GroupBy_Composite_Select_Key_Sum(isAsync);

            AssertSql(
                $@"SELECT `o`.`CustomerID`, `o`.`EmployeeID`, SUM(`o`.`OrderID`) AS `Sum`
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`, `o`.`EmployeeID`");
        }

        public override async Task GroupBy_Composite_Select_Key_Sum_Min_Max_Avg(bool isAsync)
        {
            await base.GroupBy_Composite_Select_Key_Sum_Min_Max_Avg(isAsync);

            AssertSql(
                $@"SELECT `o`.`CustomerID`, `o`.`EmployeeID`, SUM(`o`.`OrderID`) AS `Sum`, MIN(`o`.`OrderID`) AS `Min`, MAX(`o`.`OrderID`) AS `Max`, AVG(IIf(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`))) AS `Avg`
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`, `o`.`EmployeeID`");
        }

        public override async Task GroupBy_Composite_Select_Sum_Min_Key_Max_Avg(bool isAsync)
        {
            await base.GroupBy_Composite_Select_Sum_Min_Key_Max_Avg(isAsync);

            AssertSql(
                $@"SELECT SUM(`o`.`OrderID`) AS `Sum`, MIN(`o`.`OrderID`) AS `Min`, `o`.`CustomerID`, `o`.`EmployeeID`, MAX(`o`.`OrderID`) AS `Max`, AVG(IIf(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`))) AS `Avg`
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`, `o`.`EmployeeID`");
        }

        public override async Task GroupBy_Composite_Select_Sum_Min_Key_flattened_Max_Avg(bool isAsync)
        {
            await base.GroupBy_Composite_Select_Sum_Min_Key_flattened_Max_Avg(isAsync);

            AssertSql(
                $@"SELECT SUM(`o`.`OrderID`) AS `Sum`, MIN(`o`.`OrderID`) AS `Min`, `o`.`CustomerID`, `o`.`EmployeeID`, MAX(`o`.`OrderID`) AS `Max`, AVG(IIf(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`))) AS `Avg`
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`, `o`.`EmployeeID`");
        }

        public override async Task GroupBy_Dto_as_key_Select_Sum(bool isAsync)
        {
            await base.GroupBy_Dto_as_key_Select_Sum(isAsync);

            AssertSql(
                $@"SELECT SUM(`o`.`OrderID`) AS `Sum`, `o`.`CustomerID`, `o`.`EmployeeID`
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`, `o`.`EmployeeID`");
        }

        public override async Task GroupBy_Dto_as_element_selector_Select_Sum(bool isAsync)
        {
            await base.GroupBy_Dto_as_element_selector_Select_Sum(isAsync);

            AssertSql(
                $@"SELECT SUM(CAST(`o`.`EmployeeID` AS bigint)) AS `Sum`, `o`.`CustomerID` AS `Key`
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_Composite_Select_Dto_Sum_Min_Key_flattened_Max_Avg(bool isAsync)
        {
            await base.GroupBy_Composite_Select_Dto_Sum_Min_Key_flattened_Max_Avg(isAsync);

            AssertSql(
                $@"SELECT SUM(`o`.`OrderID`) AS `Sum`, MIN(`o`.`OrderID`) AS `Min`, `o`.`CustomerID` AS `CustomerId`, `o`.`EmployeeID` AS `EmployeeId`, MAX(`o`.`OrderID`) AS `Max`, AVG(IIf(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`))) AS `Avg`
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`, `o`.`EmployeeID`");
        }

        public override async Task GroupBy_Composite_Select_Sum_Min_part_Key_flattened_Max_Avg(bool isAsync)
        {
            await base.GroupBy_Composite_Select_Sum_Min_part_Key_flattened_Max_Avg(isAsync);

            AssertSql(
                $@"SELECT SUM(`o`.`OrderID`) AS `Sum`, MIN(`o`.`OrderID`) AS `Min`, `o`.`CustomerID`, MAX(`o`.`OrderID`) AS `Max`, AVG(IIf(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`))) AS `Avg`
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`, `o`.`EmployeeID`");
        }

        public override async Task GroupBy_Constant_Select_Sum_Min_Key_Max_Avg(bool isAsync)
        {
            await base.GroupBy_Constant_Select_Sum_Min_Key_Max_Avg(isAsync);

            AssertSql(
                $@"SELECT SUM(`o`.`OrderID`) AS `Sum`, MIN(`o`.`OrderID`) AS `Min`, 2 AS `Key`, MAX(`o`.`OrderID`) AS `Max`, AVG(IIf(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`))) AS `Avg`
FROM `Orders` AS `o`");
        }

        public override async Task GroupBy_Constant_with_element_selector_Select_Sum(bool isAsync)
        {
            await base.GroupBy_Constant_with_element_selector_Select_Sum(isAsync);

            AssertSql(
                $@"SELECT SUM(`o`.`OrderID`) AS `Sum`
FROM `Orders` AS `o`");
        }

        public override async Task GroupBy_Constant_with_element_selector_Select_Sum2(bool isAsync)
        {
            await base.GroupBy_Constant_with_element_selector_Select_Sum2(isAsync);

            AssertSql(
                $@"SELECT SUM(`o`.`OrderID`) AS `Sum`
FROM `Orders` AS `o`");
        }

        public override async Task GroupBy_Constant_with_element_selector_Select_Sum3(bool isAsync)
        {
            await base.GroupBy_Constant_with_element_selector_Select_Sum3(isAsync);

            AssertSql(
                $@"SELECT SUM(`o`.`OrderID`) AS `Sum`
FROM `Orders` AS `o`");
        }

        public override async Task GroupBy_after_predicate_Constant_Select_Sum_Min_Key_Max_Avg(bool isAsync)
        {
            await base.GroupBy_after_predicate_Constant_Select_Sum_Min_Key_Max_Avg(isAsync);

            AssertSql(
                $@"SELECT SUM(`o`.`OrderID`) AS `Sum`, MIN(`o`.`OrderID`) AS `Min`, 2 AS `Random`, MAX(`o`.`OrderID`) AS `Max`, AVG(IIf(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`))) AS `Avg`
FROM `Orders` AS `o`
WHERE `o`.`OrderID` > 10500");
        }

        public override async Task GroupBy_Constant_with_element_selector_Select_Sum_Min_Key_Max_Avg(bool isAsync)
        {
            await base.GroupBy_Constant_with_element_selector_Select_Sum_Min_Key_Max_Avg(isAsync);

            AssertSql(
                $@"SELECT SUM(`o`.`OrderID`) AS `Sum`, 2 AS `Key`
FROM `Orders` AS `o`");
        }

        public override async Task GroupBy_param_Select_Sum_Min_Key_Max_Avg(bool isAsync)
        {
            await base.GroupBy_param_Select_Sum_Min_Key_Max_Avg(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__a_0='2'")}

SELECT SUM(`o`.`OrderID`) AS `Sum`, MIN(`o`.`OrderID`) AS `Min`, {AssertSqlHelper.Parameter("@__a_0")} AS `Key`, MAX(`o`.`OrderID`) AS `Max`, AVG(IIf(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`))) AS `Avg`
FROM `Orders` AS `o`");
        }

        public override async Task GroupBy_param_with_element_selector_Select_Sum(bool isAsync)
        {
            await base.GroupBy_param_with_element_selector_Select_Sum(isAsync);

            AssertSql(
                $@"SELECT SUM(`o`.`OrderID`) AS `Sum`
FROM `Orders` AS `o`");
        }

        public override async Task GroupBy_param_with_element_selector_Select_Sum2(bool isAsync)
        {
            await base.GroupBy_param_with_element_selector_Select_Sum2(isAsync);

            AssertSql(
                $@"SELECT SUM(`o`.`OrderID`) AS `Sum`
FROM `Orders` AS `o`");
        }

        public override async Task GroupBy_param_with_element_selector_Select_Sum3(bool isAsync)
        {
            await base.GroupBy_param_with_element_selector_Select_Sum3(isAsync);

            AssertSql(
                $@"SELECT SUM(`o`.`OrderID`) AS `Sum`
FROM `Orders` AS `o`");
        }

        public override async Task GroupBy_param_with_element_selector_Select_Sum_Min_Key_Max_Avg(bool isAsync)
        {
            await base.GroupBy_param_with_element_selector_Select_Sum_Min_Key_Max_Avg(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__a_0='2'")}

SELECT SUM(`o`.`OrderID`) AS `Sum`, {AssertSqlHelper.Parameter("@__a_0")} AS `Key`
FROM `Orders` AS `o`");
        }

        public override async Task GroupBy_anonymous_key_type_mismatch_with_aggregate(bool isAsync)
        {
            await base.GroupBy_anonymous_key_type_mismatch_with_aggregate(isAsync);

            AssertSql(
                $@"SELECT COUNT(*) AS `I0`, DATEPART('yyyy', `o`.`OrderDate`) AS `I1`
FROM `Orders` AS `o`
GROUP BY DATEPART('yyyy', `o`.`OrderDate`)
ORDER BY DATEPART('yyyy', `o`.`OrderDate`)");
        }

        public override async Task GroupBy_Property_scalar_element_selector_Average(bool isAsync)
        {
            await base.GroupBy_Property_scalar_element_selector_Average(isAsync);

            AssertSql(
                $@"SELECT AVG(IIf(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`)))
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_Property_scalar_element_selector_Count(bool isAsync)
        {
            await base.GroupBy_Property_scalar_element_selector_Count(isAsync);

            AssertSql(
                $@"SELECT COUNT(*)
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_Property_scalar_element_selector_LongCount(bool isAsync)
        {
            await base.GroupBy_Property_scalar_element_selector_LongCount(isAsync);

            AssertSql(
                $@"SELECT COUNT_BIG(*)
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_Property_scalar_element_selector_Max(bool isAsync)
        {
            await base.GroupBy_Property_scalar_element_selector_Max(isAsync);

            AssertSql(
                $@"SELECT MAX(`o`.`OrderID`)
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_Property_scalar_element_selector_Min(bool isAsync)
        {
            await base.GroupBy_Property_scalar_element_selector_Min(isAsync);

            AssertSql(
                $@"SELECT MIN(`o`.`OrderID`)
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_Property_scalar_element_selector_Sum(bool isAsync)
        {
            await base.GroupBy_Property_scalar_element_selector_Sum(isAsync);

            AssertSql(
                $@"SELECT SUM(`o`.`OrderID`)
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_Property_scalar_element_selector_Sum_Min_Max_Avg(bool isAsync)
        {
            await base.GroupBy_Property_scalar_element_selector_Sum_Min_Max_Avg(isAsync);

            AssertSql(
                $@"SELECT SUM(`o`.`OrderID`) AS `Sum`, MIN(`o`.`OrderID`) AS `Min`, MAX(`o`.`OrderID`) AS `Max`, AVG(IIf(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`))) AS `Avg`
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_Property_anonymous_element_selector_Average(bool isAsync)
        {
            await base.GroupBy_Property_anonymous_element_selector_Average(isAsync);

            AssertSql(
                $@"SELECT AVG(IIf(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`)))
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_Property_anonymous_element_selector_Count(bool isAsync)
        {
            await base.GroupBy_Property_anonymous_element_selector_Count(isAsync);

            AssertSql(
                $@"SELECT COUNT(*)
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_Property_anonymous_element_selector_LongCount(bool isAsync)
        {
            await base.GroupBy_Property_anonymous_element_selector_LongCount(isAsync);

            AssertSql(
                $@"SELECT COUNT_BIG(*)
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_Property_anonymous_element_selector_Max(bool isAsync)
        {
            await base.GroupBy_Property_anonymous_element_selector_Max(isAsync);

            AssertSql(
                $@"SELECT MAX(`o`.`OrderID`)
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_Property_anonymous_element_selector_Min(bool isAsync)
        {
            await base.GroupBy_Property_anonymous_element_selector_Min(isAsync);

            AssertSql(
                $@"SELECT MIN(`o`.`OrderID`)
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_Property_anonymous_element_selector_Sum(bool isAsync)
        {
            await base.GroupBy_Property_anonymous_element_selector_Sum(isAsync);

            AssertSql(
                $@"SELECT SUM(`o`.`OrderID`)
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_Property_anonymous_element_selector_Sum_Min_Max_Avg(bool isAsync)
        {
            await base.GroupBy_Property_anonymous_element_selector_Sum_Min_Max_Avg(isAsync);

            AssertSql(
                $@"SELECT SUM(`o`.`OrderID`) AS `Sum`, MIN(`o`.`EmployeeID`) AS `Min`, MAX(`o`.`EmployeeID`) AS `Max`, AVG(IIf(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`))) AS `Avg`
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_element_selector_complex_aggregate(bool isAsync)
        {
            await base.GroupBy_element_selector_complex_aggregate(isAsync);

            AssertSql(
                $@"SELECT SUM(`o`.`OrderID` + 1)
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_element_selector_complex_aggregate2(bool isAsync)
        {
            await base.GroupBy_element_selector_complex_aggregate2(isAsync);

            AssertSql(
                $@"SELECT SUM(`o`.`OrderID` + 1)
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_element_selector_complex_aggregate3(bool isAsync)
        {
            await base.GroupBy_element_selector_complex_aggregate3(isAsync);

            AssertSql(
                $@"SELECT SUM(`o`.`OrderID` + 1)
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_element_selector_complex_aggregate4(bool isAsync)
        {
            await base.GroupBy_element_selector_complex_aggregate4(isAsync);

            AssertSql(
                $@"SELECT SUM(`o`.`OrderID` + 1)
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_empty_key_Aggregate(bool isAsync)
        {
            await base.GroupBy_empty_key_Aggregate(isAsync);

            AssertSql(
                $@"SELECT SUM(`o`.`OrderID`)
FROM `Orders` AS `o`");
        }

        public override async Task GroupBy_empty_key_Aggregate_Key(bool isAsync)
        {
            await base.GroupBy_empty_key_Aggregate_Key(isAsync);

            AssertSql(
                $@"SELECT SUM(`o`.`OrderID`) AS `Sum`
FROM `Orders` AS `o`");
        }

        public override async Task OrderBy_GroupBy_Aggregate(bool isAsync)
        {
            await base.OrderBy_GroupBy_Aggregate(isAsync);

            AssertSql(
                $@"SELECT SUM(`o`.`OrderID`)
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task OrderBy_Skip_GroupBy_Aggregate(bool isAsync)
        {
            await base.OrderBy_Skip_GroupBy_Aggregate(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='80'")}

SELECT AVG(IIf(`t`.`OrderID` IS NULL, NULL, CDBL(`t`.`OrderID`)))
FROM (
    SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
    FROM `Orders` AS `o`
    ORDER BY `o`.`OrderID`
    SKIP {AssertSqlHelper.Parameter("@__p_0")}
) AS `t`
GROUP BY `t`.`CustomerID`");
        }

        public override async Task OrderBy_Take_GroupBy_Aggregate(bool isAsync)
        {
            await base.OrderBy_Take_GroupBy_Aggregate(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='500'")}

SELECT MIN(`t`.`OrderID`)
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
    FROM `Orders` AS `o`
    ORDER BY `o`.`OrderID`
) AS `t`
GROUP BY `t`.`CustomerID`");
        }

        public override async Task OrderBy_Skip_Take_GroupBy_Aggregate(bool isAsync)
        {
            await base.OrderBy_Skip_Take_GroupBy_Aggregate(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='80'")}

{AssertSqlHelper.Declaration("@__p_1='500'")}

SELECT MAX(`t`.`OrderID`)
FROM (
    SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
    FROM `Orders` AS `o`
    ORDER BY `o`.`OrderID`
    SKIP {AssertSqlHelper.Parameter("@__p_0")} FETCH NEXT {AssertSqlHelper.Parameter("@__p_1")} ROWS ONLY
) AS `t`
GROUP BY `t`.`CustomerID`");
        }

        public override async Task Distinct_GroupBy_Aggregate(bool isAsync)
        {
            await base.Distinct_GroupBy_Aggregate(isAsync);

            AssertSql(
                $@"SELECT `t`.`CustomerID` AS `Key`, COUNT(*) AS `c`
FROM (
    SELECT DISTINCT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
    FROM `Orders` AS `o`
) AS `t`
GROUP BY `t`.`CustomerID`");
        }

        public override async Task Anonymous_projection_Distinct_GroupBy_Aggregate(bool isAsync)
        {
            await base.Anonymous_projection_Distinct_GroupBy_Aggregate(isAsync);

            AssertSql(
                $@"SELECT `t`.`EmployeeID` AS `Key`, COUNT(*) AS `c`
FROM (
    SELECT DISTINCT `o`.`OrderID`, `o`.`EmployeeID`
    FROM `Orders` AS `o`
) AS `t`
GROUP BY `t`.`EmployeeID`");
        }

        public override async Task SelectMany_GroupBy_Aggregate(bool isAsync)
        {
            await base.SelectMany_GroupBy_Aggregate(isAsync);

            AssertSql(
                $@"SELECT `o`.`EmployeeID` AS `Key`, COUNT(*) AS `c`
FROM `Customers` AS `c`
INNER JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`
GROUP BY `o`.`EmployeeID`");
        }

        public override async Task Join_GroupBy_Aggregate(bool isAsync)
        {
            await base.Join_GroupBy_Aggregate(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID` AS `Key`, AVG(IIf(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`))) AS `Count`
FROM `Orders` AS `o`
INNER JOIN `Customers` AS `c` ON `o`.`CustomerID` = `c`.`CustomerID`
GROUP BY `c`.`CustomerID`");
        }

        public override async Task GroupBy_required_navigation_member_Aggregate(bool isAsync)
        {
            await base.GroupBy_required_navigation_member_Aggregate(isAsync);

            AssertSql(
                $@"SELECT `o0`.`CustomerID` AS `CustomerId`, COUNT(*) AS `Count`
FROM `Order Details` AS `o`
INNER JOIN `Orders` AS `o0` ON `o`.`OrderID` = `o0`.`OrderID`
GROUP BY `o0`.`CustomerID`");
        }

        public override async Task Join_complex_GroupBy_Aggregate(bool isAsync)
        {
            await base.Join_complex_GroupBy_Aggregate(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='100'")}

{AssertSqlHelper.Declaration("@__p_1='10'")}

{AssertSqlHelper.Declaration("@__p_2='50'")}

SELECT `t0`.`CustomerID` AS `Key`, AVG(IIf(`t`.`OrderID` IS NULL, NULL, CDBL(`t`.`OrderID`))) AS `Count`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
    FROM `Orders` AS `o`
    WHERE `o`.`OrderID` < 10400
    ORDER BY `o`.`OrderDate`
) AS `t`
INNER JOIN (
    SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    WHERE (`c`.`CustomerID` <> 'DRACD') AND (`c`.`CustomerID` <> 'FOLKO')
    ORDER BY `c`.`City`
    SKIP {AssertSqlHelper.Parameter("@__p_1")} FETCH NEXT {AssertSqlHelper.Parameter("@__p_2")} ROWS ONLY
) AS `t0` ON `t`.`CustomerID` = `t0`.`CustomerID`
GROUP BY `t0`.`CustomerID`");
        }

        public override async Task GroupJoin_GroupBy_Aggregate(bool isAsync)
        {
            await base.GroupJoin_GroupBy_Aggregate(isAsync);

            AssertSql(
                $@"SELECT `o`.`CustomerID` AS `Key`, AVG(IIf(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`))) AS `Average`
FROM `Customers` AS `c`
LEFT JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`
WHERE `o`.`OrderID` IS NOT NULL
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupJoin_GroupBy_Aggregate_2(bool isAsync)
        {
            await base.GroupJoin_GroupBy_Aggregate_2(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID` AS `Key`, MAX(`c`.`City`) AS `Max`
FROM `Customers` AS `c`
LEFT JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`
GROUP BY `c`.`CustomerID`");
        }

        public override async Task GroupJoin_GroupBy_Aggregate_3(bool isAsync)
        {
            await base.GroupJoin_GroupBy_Aggregate_3(isAsync);

            AssertSql(
                $@"SELECT `o`.`CustomerID` AS `Key`, AVG(IIf(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`))) AS `Average`
FROM `Orders` AS `o`
LEFT JOIN `Customers` AS `c` ON `o`.`CustomerID` = `c`.`CustomerID`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupJoin_GroupBy_Aggregate_4(bool isAsync)
        {
            await base.GroupJoin_GroupBy_Aggregate_4(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID` AS `Value`, MAX(`c`.`City`) AS `Max`
FROM `Customers` AS `c`
LEFT JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`
GROUP BY `c`.`CustomerID`");
        }

        public override async Task GroupJoin_GroupBy_Aggregate_5(bool isAsync)
        {
            await base.GroupJoin_GroupBy_Aggregate_5(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID` AS `Value`, AVG(IIf(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`))) AS `Average`
FROM `Orders` AS `o`
LEFT JOIN `Customers` AS `c` ON `o`.`CustomerID` = `c`.`CustomerID`
GROUP BY `o`.`OrderID`");
        }

        public override async Task GroupBy_optional_navigation_member_Aggregate(bool isAsync)
        {
            await base.GroupBy_optional_navigation_member_Aggregate(isAsync);

            AssertSql(
                $@"SELECT `c`.`Country`, COUNT(*) AS `Count`
FROM `Orders` AS `o`
LEFT JOIN `Customers` AS `c` ON `o`.`CustomerID` = `c`.`CustomerID`
GROUP BY `c`.`Country`");
        }

        public override async Task GroupJoin_complex_GroupBy_Aggregate(bool isAsync)
        {
            await base.GroupJoin_complex_GroupBy_Aggregate(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='10'")}

{AssertSqlHelper.Declaration("@__p_1='50'")}

{AssertSqlHelper.Declaration("@__p_2='100'")}

SELECT `t0`.`CustomerID` AS `Key`, AVG(IIf(`t0`.`OrderID` IS NULL, NULL, CDBL(`t0`.`OrderID`))) AS `Count`
FROM (
    SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    WHERE (`c`.`CustomerID` <> 'DRACD') AND (`c`.`CustomerID` <> 'FOLKO')
    ORDER BY `c`.`City`
    SKIP {AssertSqlHelper.Parameter("@__p_0")} FETCH NEXT {AssertSqlHelper.Parameter("@__p_1")} ROWS ONLY
) AS `t`
INNER JOIN (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_2")} `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
    FROM `Orders` AS `o`
    WHERE `o`.`OrderID` < 10400
    ORDER BY `o`.`OrderDate`
) AS `t0` ON `t`.`CustomerID` = `t0`.`CustomerID`
WHERE `t0`.`OrderID` > 10300
GROUP BY `t0`.`CustomerID`");
        }

        public override async Task Self_join_GroupBy_Aggregate(bool isAsync)
        {
            await base.Self_join_GroupBy_Aggregate(isAsync);

            AssertSql(
                $@"SELECT `o`.`CustomerID` AS `Key`, AVG(IIf(`o0`.`OrderID` IS NULL, NULL, CDBL(`o0`.`OrderID`))) AS `Count`
FROM `Orders` AS `o`
INNER JOIN `Orders` AS `o0` ON `o`.`OrderID` = `o0`.`OrderID`
WHERE `o`.`OrderID` < 10400
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_multi_navigation_members_Aggregate(bool isAsync)
        {
            await base.GroupBy_multi_navigation_members_Aggregate(isAsync);

            AssertSql(
                $@"SELECT `o0`.`CustomerID`, `p`.`ProductName`, COUNT(*) AS `Count`
FROM `Order Details` AS `o`
INNER JOIN `Orders` AS `o0` ON `o`.`OrderID` = `o0`.`OrderID`
INNER JOIN `Products` AS `p` ON `o`.`ProductID` = `p`.`ProductID`
GROUP BY `o0`.`CustomerID`, `p`.`ProductName`");
        }

        public override async Task Union_simple_groupby(bool isAsync)
        {
            await base.Union_simple_groupby(isAsync);

            AssertSql(
                $@"SELECT `t`.`City` AS `Key`, COUNT(*) AS `Total`
FROM (
    SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    WHERE `c`.`ContactTitle` = 'Owner'
    UNION
    SELECT `c0`.`CustomerID`, `c0`.`Address`, `c0`.`City`, `c0`.`CompanyName`, `c0`.`ContactName`, `c0`.`ContactTitle`, `c0`.`Country`, `c0`.`Fax`, `c0`.`Phone`, `c0`.`PostalCode`, `c0`.`Region`
    FROM `Customers` AS `c0`
    WHERE `c0`.`City` = 'México D.F.'
) AS `t`
GROUP BY `t`.`City`");
        }

        public override async Task Select_anonymous_GroupBy_Aggregate(bool isAsync)
        {
            await base.Select_anonymous_GroupBy_Aggregate(isAsync);

            AssertSql(
                $@"SELECT MIN(`o`.`OrderDate`) AS `Min`, MAX(`o`.`OrderDate`) AS `Max`, SUM(`o`.`OrderID`) AS `Sum`, AVG(IIf(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`))) AS `Avg`
FROM `Orders` AS `o`
WHERE `o`.`OrderID` < 10300
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_principal_key_property_optimization(bool isAsync)
        {
            await base.GroupBy_principal_key_property_optimization(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID` AS `Key`, COUNT(*) AS `Count`
FROM `Orders` AS `o`
LEFT JOIN `Customers` AS `c` ON `o`.`CustomerID` = `c`.`CustomerID`
GROUP BY `c`.`CustomerID`");
        }

        public override async Task GroupBy_OrderBy_key(bool isAsync)
        {
            await base.GroupBy_OrderBy_key(isAsync);

            AssertSql(
                $@"SELECT `o`.`CustomerID` AS `Key`, COUNT(*) AS `c`
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`
ORDER BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_OrderBy_count(bool isAsync)
        {
            await base.GroupBy_OrderBy_count(isAsync);

            AssertSql(
                $@"SELECT `o`.`CustomerID` AS `Key`, COUNT(*) AS `Count`
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`
ORDER BY COUNT(*), `o`.`CustomerID`");
        }

        public override async Task GroupBy_OrderBy_count_Select_sum(bool isAsync)
        {
            await base.GroupBy_OrderBy_count_Select_sum(isAsync);

            AssertSql(
                $@"SELECT `o`.`CustomerID` AS `Key`, SUM(`o`.`OrderID`) AS `Sum`
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`
ORDER BY COUNT(*), `o`.`CustomerID`");
        }

        public override async Task GroupBy_aggregate_Contains(bool isAsync)
        {
            await base.GroupBy_aggregate_Contains(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`CustomerID` IN (
    SELECT `o0`.`CustomerID`
    FROM `Orders` AS `o0`
    GROUP BY `o0`.`CustomerID`
    HAVING COUNT(*) > 30
)");
        }

        public override async Task GroupBy_aggregate_Pushdown(bool isAsync)
        {
            await base.GroupBy_aggregate_Pushdown(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='20'")}

{AssertSqlHelper.Declaration("@__p_1='4'")}

SELECT `t`.`CustomerID`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `o`.`CustomerID`
    FROM `Orders` AS `o`
    GROUP BY `o`.`CustomerID`
    HAVING COUNT(*) > 10
    ORDER BY `o`.`CustomerID`
) AS `t`
ORDER BY `t`.`CustomerID`
SKIP {AssertSqlHelper.Parameter("@__p_1")}");
        }

        public override async Task GroupBy_aggregate_Pushdown_followed_by_projecting_Length(bool isAsync)
        {
            await base.GroupBy_aggregate_Pushdown_followed_by_projecting_Length(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='20'")}

{AssertSqlHelper.Declaration("@__p_1='4'")}

SELECT CAST(LEN(`t`.`CustomerID`) AS int)
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `o`.`CustomerID`
    FROM `Orders` AS `o`
    GROUP BY `o`.`CustomerID`
    HAVING COUNT(*) > 10
    ORDER BY `o`.`CustomerID`
) AS `t`
ORDER BY `t`.`CustomerID`
SKIP {AssertSqlHelper.Parameter("@__p_1")}");
        }

        public override async Task GroupBy_aggregate_Pushdown_followed_by_projecting_constant(bool isAsync)
        {
            await base.GroupBy_aggregate_Pushdown_followed_by_projecting_constant(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='20'")}

{AssertSqlHelper.Declaration("@__p_1='4'")}

SELECT 5
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `o`.`CustomerID`
    FROM `Orders` AS `o`
    GROUP BY `o`.`CustomerID`
    HAVING COUNT(*) > 10
    ORDER BY `o`.`CustomerID`
) AS `t`
ORDER BY `t`.`CustomerID`
SKIP {AssertSqlHelper.Parameter("@__p_1")}");
        }
        
        public override async Task GroupBy_filter_key(bool isAsync)
        {
            await base.GroupBy_filter_key(isAsync);

            AssertSql(
                $@"SELECT `o`.`CustomerID` AS `Key`, COUNT(*) AS `c`
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`
HAVING `o`.`CustomerID` = 'ALFKI'");
        }

        public override async Task GroupBy_filter_count(bool isAsync)
        {
            await base.GroupBy_filter_count(isAsync);

            AssertSql(
                $@"SELECT `o`.`CustomerID` AS `Key`, COUNT(*) AS `Count`
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`
HAVING COUNT(*) > 4");
        }

        public override async Task GroupBy_filter_count_OrderBy_count_Select_sum(bool isAsync)
        {
            await base.GroupBy_filter_count_OrderBy_count_Select_sum(isAsync);

            AssertSql(
                $@"SELECT `o`.`CustomerID` AS `Key`, COUNT(*) AS `Count`, SUM(`o`.`OrderID`) AS `Sum`
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`
HAVING COUNT(*) > 4
ORDER BY COUNT(*), `o`.`CustomerID`");
        }

        public override async Task GroupBy_Aggregate_Join(bool isAsync)
        {
            await base.GroupBy_Aggregate_Join(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `o0`.`OrderID`, `o0`.`CustomerID`, `o0`.`EmployeeID`, `o0`.`OrderDate`
FROM (
    SELECT `o`.`CustomerID`, MAX(`o`.`OrderID`) AS `c`
    FROM `Orders` AS `o`
    GROUP BY `o`.`CustomerID`
    HAVING COUNT(*) > 5
) AS `t`
INNER JOIN `Customers` AS `c` ON `t`.`CustomerID` = `c`.`CustomerID`
INNER JOIN `Orders` AS `o0` ON `t`.`c` = `o0`.`OrderID`");
        }

        public override async Task Join_GroupBy_Aggregate_multijoins(bool isAsync)
        {
            await base.Join_GroupBy_Aggregate_multijoins(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `o0`.`OrderID`, `o0`.`CustomerID`, `o0`.`EmployeeID`, `o0`.`OrderDate`
FROM `Customers` AS `c`
INNER JOIN (
    SELECT `o`.`CustomerID`, MAX(`o`.`OrderID`) AS `c`
    FROM `Orders` AS `o`
    GROUP BY `o`.`CustomerID`
    HAVING COUNT(*) > 5
) AS `t` ON `c`.`CustomerID` = `t`.`CustomerID`
INNER JOIN `Orders` AS `o0` ON `t`.`c` = `o0`.`OrderID`");
        }

        public override async Task Join_GroupBy_Aggregate_single_join(bool isAsync)
        {
            await base.Join_GroupBy_Aggregate_single_join(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `t`.`c` AS `LastOrderID`
FROM `Customers` AS `c`
INNER JOIN (
    SELECT `o`.`CustomerID`, MAX(`o`.`OrderID`) AS `c`
    FROM `Orders` AS `o`
    GROUP BY `o`.`CustomerID`
    HAVING COUNT(*) > 5
) AS `t` ON `c`.`CustomerID` = `t`.`CustomerID`");
        }

        public override async Task Join_GroupBy_Aggregate_with_another_join(bool isAsync)
        {
            await base.Join_GroupBy_Aggregate_with_another_join(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `t`.`c` AS `LastOrderID`, `o0`.`OrderID`
FROM `Customers` AS `c`
INNER JOIN (
    SELECT `o`.`CustomerID`, MAX(`o`.`OrderID`) AS `c`
    FROM `Orders` AS `o`
    GROUP BY `o`.`CustomerID`
    HAVING COUNT(*) > 5
) AS `t` ON `c`.`CustomerID` = `t`.`CustomerID`
INNER JOIN `Orders` AS `o0` ON `c`.`CustomerID` = `o0`.`CustomerID`");
        }

        public override async Task Join_GroupBy_Aggregate_in_subquery(bool isAsync)
        {
            await base.Join_GroupBy_Aggregate_in_subquery(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`, `t0`.`CustomerID`, `t0`.`Address`, `t0`.`City`, `t0`.`CompanyName`, `t0`.`ContactName`, `t0`.`ContactTitle`, `t0`.`Country`, `t0`.`Fax`, `t0`.`Phone`, `t0`.`PostalCode`, `t0`.`Region`
FROM `Orders` AS `o`
INNER JOIN (
    SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `t`.`CustomerID` AS `CustomerID0`, `t`.`c`
    FROM `Customers` AS `c`
    INNER JOIN (
        SELECT `o0`.`CustomerID`, MAX(`o0`.`OrderID`) AS `c`
        FROM `Orders` AS `o0`
        GROUP BY `o0`.`CustomerID`
        HAVING COUNT(*) > 5
    ) AS `t` ON `c`.`CustomerID` = `t`.`CustomerID`
) AS `t0` ON `o`.`CustomerID` = `t0`.`CustomerID`
WHERE `o`.`OrderID` < 10400");
        }

        public override async Task Join_GroupBy_Aggregate_on_key(bool isAsync)
        {
            await base.Join_GroupBy_Aggregate_on_key(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `t`.`c` AS `LastOrderID`
FROM `Customers` AS `c`
INNER JOIN (
    SELECT `o`.`CustomerID`, MAX(`o`.`OrderID`) AS `c`
    FROM `Orders` AS `o`
    GROUP BY `o`.`CustomerID`
    HAVING COUNT(*) > 5
) AS `t` ON `c`.`CustomerID` = `t`.`CustomerID`");
        }

        public override async Task GroupBy_with_result_selector(bool isAsync)
        {
            await base.GroupBy_with_result_selector(isAsync);

            AssertSql(
                $@"SELECT SUM(`o`.`OrderID`) AS `Sum`, MIN(`o`.`OrderID`) AS `Min`, MAX(`o`.`OrderID`) AS `Max`, AVG(IIf(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`))) AS `Avg`
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_Sum_constant(bool isAsync)
        {
            await base.GroupBy_Sum_constant(isAsync);

            AssertSql(
                $@"SELECT SUM(1)
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task GroupBy_Sum_constant_cast(bool isAsync)
        {
            await base.GroupBy_Sum_constant_cast(isAsync);

            AssertSql(
                $@"SELECT SUM(1)
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task Distinct_GroupBy_OrderBy_key(bool isAsync)
        {
            await base.Distinct_GroupBy_OrderBy_key(isAsync);

            AssertSql(
                $@"SELECT `t`.`CustomerID` AS `Key`, COUNT(*) AS `c`
FROM (
    SELECT DISTINCT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
    FROM `Orders` AS `o`
) AS `t`
GROUP BY `t`.`CustomerID`
ORDER BY `t`.`CustomerID`");
        }

        public override async Task Select_nested_collection_with_groupby(bool isAsync)
        {
            await base.Select_nested_collection_with_groupby(isAsync);

            AssertSql(
                $@"SELECT (
    SELECT CASE
        WHEN EXISTS (
            SELECT 1
            FROM `Orders` AS `o0`
            WHERE `c`.`CustomerID` = `o0`.`CustomerID`)
        THEN True ELSE False
    END
), `c`.`CustomerID`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` LIKE 'A' & '%'",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_CustomerID='ALFKI' (Size = 5)")}

SELECT `o1`.`OrderID`
FROM `Orders` AS `o1`
WHERE {AssertSqlHelper.Parameter("@_outer_CustomerID")} = `o1`.`CustomerID`
ORDER BY `o1`.`OrderID`",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_CustomerID='ANATR' (Size = 5)")}

SELECT `o1`.`OrderID`
FROM `Orders` AS `o1`
WHERE {AssertSqlHelper.Parameter("@_outer_CustomerID")} = `o1`.`CustomerID`
ORDER BY `o1`.`OrderID`",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_CustomerID='ANTON' (Size = 5)")}

SELECT `o1`.`OrderID`
FROM `Orders` AS `o1`
WHERE {AssertSqlHelper.Parameter("@_outer_CustomerID")} = `o1`.`CustomerID`
ORDER BY `o1`.`OrderID`",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_CustomerID='AROUT' (Size = 5)")}

SELECT `o1`.`OrderID`
FROM `Orders` AS `o1`
WHERE {AssertSqlHelper.Parameter("@_outer_CustomerID")} = `o1`.`CustomerID`
ORDER BY `o1`.`OrderID`");
        }

        public override async Task Select_GroupBy_All(bool isAsync)
        {
            await base.Select_GroupBy_All(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID` AS `Order`, `o`.`CustomerID` AS `Customer`
FROM `Orders` AS `o`
ORDER BY `o`.`CustomerID`");
        }
        
        public override async Task GroupBy_Key_as_part_of_element_selector(bool isAsync)
        {
            await base.GroupBy_Key_as_part_of_element_selector(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID` AS `Key`, AVG(IIf(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`))) AS `Avg`, MAX(`o`.`OrderDate`) AS `Max`
FROM `Orders` AS `o`
GROUP BY `o`.`OrderID`");
        }

        public override async Task GroupBy_composite_Key_as_part_of_element_selector(bool isAsync)
        {
            await base.GroupBy_composite_Key_as_part_of_element_selector(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, AVG(IIf(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`))) AS `Avg`, MAX(`o`.`OrderDate`) AS `Max`
FROM `Orders` AS `o`
GROUP BY `o`.`OrderID`, `o`.`CustomerID`");
        }
        
        public override async Task GroupBy_SelectMany(bool isAsync)
        {
            await base.GroupBy_SelectMany(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
ORDER BY `c`.`City`");
        }
        
        public override async Task OrderBy_GroupBy_SelectMany(bool isAsync)
        {
            await base.OrderBy_GroupBy_SelectMany(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
ORDER BY `o`.`CustomerID`, `o`.`OrderID`");
        }

        public override async Task OrderBy_GroupBy_SelectMany_shadow(bool isAsync)
        {
            await base.OrderBy_GroupBy_SelectMany_shadow(isAsync);

            AssertSql(
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
ORDER BY `e`.`EmployeeID`");
        }
        
        public override async Task GroupBy_with_orderby_take_skip_distinct_followed_by_group_key_projection(bool isAsync)
        {
            await base.GroupBy_with_orderby_take_skip_distinct_followed_by_group_key_projection(isAsync);

            AssertSql(
                $@"");
        }
        
        public override async Task GroupBy_Distinct(bool isAsync)
        {
            await base.GroupBy_Distinct(isAsync);

            AssertSql(
                $@"SELECT `o0`.`OrderID`, `o0`.`CustomerID`, `o0`.`EmployeeID`, `o0`.`OrderDate`
FROM `Orders` AS `o0`
ORDER BY `o0`.`CustomerID`");
        }
        
        public override async Task GroupBy_with_aggregate_through_navigation_property(bool isAsync)
        {
            await base.GroupBy_with_aggregate_through_navigation_property(isAsync);

            AssertSql(
                $@"SELECT `c`.`OrderID`, `c`.`CustomerID`, `c`.`EmployeeID`, `c`.`OrderDate`
FROM `Orders` AS `c`
ORDER BY `c`.`EmployeeID`",
                //
                $@"SELECT [i.Customer0].`CustomerID`, [i.Customer0].`Region`
FROM `Customers` AS [i.Customer0]",
                //
                $@"SELECT [i.Customer0].`CustomerID`, [i.Customer0].`Region`
FROM `Customers` AS [i.Customer0]");
        }
        
        public override async Task GroupBy_Shadow(bool isAsync)
        {
            await base.GroupBy_Shadow(isAsync);

            AssertSql(
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE (`e`.`Title` = 'Sales Representative') AND (`e`.`EmployeeID` = 1)
ORDER BY `e`.`Title`");
        }

        public override async Task GroupBy_Shadow2(bool isAsync)
        {
            await base.GroupBy_Shadow2(isAsync);

            AssertSql(
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE (`e`.`Title` = 'Sales Representative') AND (`e`.`EmployeeID` = 1)
ORDER BY `e`.`Title`");
        }

        public override async Task GroupBy_Shadow3(bool isAsync)
        {
            await base.GroupBy_Shadow3(isAsync);

            AssertSql(
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE `e`.`EmployeeID` = 1
ORDER BY `e`.`EmployeeID`");
        }
        
        public override async Task Select_GroupBy_SelectMany(bool isAsync)
        {
            await base.Select_GroupBy_SelectMany(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID` AS `Order`, `o`.`CustomerID` AS `Customer`
FROM `Orders` AS `o`
ORDER BY `o`.`OrderID`");
        }
        
        public override async Task Count_after_GroupBy_aggregate(bool isAsync)
        {
            await base.Count_after_GroupBy_aggregate(isAsync);

            AssertSql(
                $@"SELECT COUNT(*)
FROM (
    SELECT SUM(`o`.`OrderID`) AS `c`
    FROM `Orders` AS `o`
    GROUP BY `o`.`CustomerID`
) AS `t`");
        }
        
        public override async Task MinMax_after_GroupBy_aggregate(bool isAsync)
        {
            await base.MinMax_after_GroupBy_aggregate(isAsync);

            AssertSql(
                $@"SELECT MIN(`t`.`c`)
FROM (
    SELECT SUM(`o`.`OrderID`) AS `c`
    FROM `Orders` AS `o`
    GROUP BY `o`.`CustomerID`
) AS `t`",
                //
                $@"SELECT MAX(`t`.`c`)
FROM (
    SELECT SUM(`o`.`OrderID`) AS `c`
    FROM `Orders` AS `o`
    GROUP BY `o`.`CustomerID`
) AS `t`");
        }

        public override async Task All_after_GroupBy_aggregate(bool isAsync)
        {
            await base.All_after_GroupBy_aggregate(isAsync);

            AssertSql(
                $@"SELECT CASE
    WHEN NOT EXISTS (
        SELECT 1
        FROM `Orders` AS `o`
        GROUP BY `o`.`CustomerID`
        HAVING False = True) THEN True
    ELSE False
END");
        }

        public override async Task All_after_GroupBy_aggregate2(bool isAsync)
        {
            await base.All_after_GroupBy_aggregate2(isAsync);

            AssertSql(
                $@"SELECT CASE
    WHEN NOT EXISTS (
        SELECT 1
        FROM `Orders` AS `o`
        GROUP BY `o`.`CustomerID`
        HAVING SUM(`o`.`OrderID`) < 0) THEN True
    ELSE False
END");
        }

        public override async Task Any_after_GroupBy_aggregate(bool isAsync)
        {
            await base.Any_after_GroupBy_aggregate(isAsync);

            AssertSql(
                $@"SELECT CASE
    WHEN EXISTS (
        SELECT 1
        FROM `Orders` AS `o`
        GROUP BY `o`.`CustomerID`) THEN True
    ELSE False
END");
        }

        public override async Task Count_after_GroupBy_without_aggregate(bool isAsync)
        {
            await base.Count_after_GroupBy_without_aggregate(isAsync);

            AssertSql($@" ");
        }

        public override async Task LongCount_after_GroupBy_without_aggregate(bool isAsync)
        {
            await base.LongCount_after_GroupBy_without_aggregate(isAsync);

            AssertSql($@" ");
        }

        public override async Task GroupBy_based_on_renamed_property_simple(bool isAsync)
        {
            await base.GroupBy_based_on_renamed_property_simple(isAsync);

            AssertSql(
                $@"SELECT `c`.`City` AS `Renamed`, COUNT(*) AS `Count`
FROM `Customers` AS `c`
GROUP BY `c`.`City`");
        }

        public override async Task GroupBy_based_on_renamed_property_complex(bool isAsync)
        {
            await base.GroupBy_based_on_renamed_property_complex(isAsync);

            AssertSql(
                $@"SELECT `t`.`City` AS `Key`, COUNT(*) AS `Count`
FROM (
    SELECT DISTINCT `c`.`City`, `c`.`CustomerID`
    FROM `Customers` AS `c`
) AS `t`
GROUP BY `t`.`City`");
        }

        public override async Task GroupBy_with_group_key_access_thru_navigation(bool isAsync)
        {
            await base.GroupBy_with_group_key_access_thru_navigation(isAsync);

            AssertSql(
                $@"SELECT `o0`.`CustomerID` AS `Key`, SUM(`o`.`OrderID`) AS `Aggregate`
FROM `Order Details` AS `o`
INNER JOIN `Orders` AS `o0` ON `o`.`OrderID` = `o0`.`OrderID`
GROUP BY `o0`.`CustomerID`");
        }

        public override async Task GroupBy_with_group_key_access_thru_nested_navigation(bool isAsync)
        {
            await base.GroupBy_with_group_key_access_thru_nested_navigation(isAsync);

            AssertSql(
                $@"SELECT `c`.`Country` AS `Key`, SUM(`o`.`OrderID`) AS `Aggregate`
FROM `Order Details` AS `o`
INNER JOIN `Orders` AS `o0` ON `o`.`OrderID` = `o0`.`OrderID`
LEFT JOIN `Customers` AS `c` ON `o0`.`CustomerID` = `c`.`CustomerID`
GROUP BY `c`.`Country`");
        }

        public override async Task GroupBy_with_group_key_being_navigation(bool isAsync)
        {
            await base.GroupBy_with_group_key_being_navigation(isAsync);

            AssertSql(
                $@"");
        }

        public override async Task GroupBy_with_group_key_being_nested_navigation(bool isAsync)
        {
            await base.GroupBy_with_group_key_being_nested_navigation(isAsync);

            AssertSql(
                $@"");
        }

        public override async Task GroupBy_with_group_key_being_navigation_with_entity_key_projection(bool isAsync)
        {
            await base.GroupBy_with_group_key_being_navigation_with_entity_key_projection(isAsync);

            AssertSql(
                $@"");
        }

        public override async Task GroupBy_with_group_key_being_navigation_with_complex_projection(bool isAsync)
        {
            await base.GroupBy_with_group_key_being_navigation_with_complex_projection(isAsync);

            AssertSql(
                $@"");
        }

        public override async Task GroupBy_with_order_by_skip_and_another_order_by(bool isAsync)
        {
            await base.GroupBy_with_order_by_skip_and_another_order_by(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='80'")}

SELECT SUM(`t`.`OrderID`)
FROM (
    SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
    FROM `Orders` AS `o`
    ORDER BY `o`.`CustomerID`, `o`.`OrderID`
    SKIP {AssertSqlHelper.Parameter("@__p_0")}
) AS `t`
GROUP BY `t`.`CustomerID`");
        }

        public override Task GroupBy_Property_Select_Count_with_predicate(bool isAsync)
        {
            return Assert.ThrowsAsync<InvalidOperationException>(
                () => base.GroupBy_Property_Select_Count_with_predicate(isAsync));
        }

        public override Task GroupBy_Property_Select_LongCount_with_predicate(bool isAsync)
        {
            return Assert.ThrowsAsync<InvalidOperationException>(
                () => base.GroupBy_Property_Select_LongCount_with_predicate(isAsync));
        }

        public override async Task GroupBy_with_grouping_key_using_Like(bool isAsync)
        {
            await base.GroupBy_with_grouping_key_using_Like(isAsync);

            AssertSql(
                $@"SELECT IIF(`o`.`CustomerID` LIKE 'A' & '%', 1, 0) AS `Key`, COUNT(*) AS `Count`
FROM `Orders` AS `o`
GROUP BY IIF(`o`.`CustomerID` LIKE 'A' & '%', 1, 0)");
        }

        public override async Task GroupBy_with_grouping_key_DateTime_Day(bool isAsync)
        {
            await base.GroupBy_with_grouping_key_DateTime_Day(isAsync);

            AssertSql(
                $@"SELECT DATEPART('d', `o`.`OrderDate`) AS `Key`, COUNT(*) AS `Count`
FROM `Orders` AS `o`
GROUP BY DATEPART('d', `o`.`OrderDate`)");
        }

        public override async Task GroupBy_with_cast_inside_grouping_aggregate(bool isAsync)
        {
            await base.GroupBy_with_cast_inside_grouping_aggregate(isAsync);

            AssertSql(
                $@"SELECT `o`.`CustomerID` AS `Key`, COUNT(*) AS `Count`, SUM(CAST(`o`.`OrderID` AS bigint)) AS `Sum`
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        public override async Task Complex_query_with_groupBy_in_subquery1(bool isAsync)
        {
            await base.Complex_query_with_groupBy_in_subquery1(isAsync);

            AssertSql(
                $@"");
        }

        public override async Task Complex_query_with_groupBy_in_subquery2(bool isAsync)
        {
            await base.Complex_query_with_groupBy_in_subquery2(isAsync);

            AssertSql(
                $@"");
        }

        public override async Task Complex_query_with_groupBy_in_subquery3(bool isAsync)
        {
            await base.Complex_query_with_groupBy_in_subquery3(isAsync);

            AssertSql(
                $@"");
        }
        
        public override async Task Group_by_with_arithmetic_operation_inside_aggregate(bool isAsync)
        {
            await base.Group_by_with_arithmetic_operation_inside_aggregate(isAsync);

            AssertSql(
                $@"SELECT `o`.`CustomerID` AS `Key`, SUM(`o`.`OrderID` + CAST(LEN(`o`.`CustomerID`) AS int)) AS `Sum`
FROM `Orders` AS `o`
GROUP BY `o`.`CustomerID`");
        }

        private void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);

        protected override void ClearLog()
            => Fixture.TestSqlLoggerFactory.Clear();
    }
}
