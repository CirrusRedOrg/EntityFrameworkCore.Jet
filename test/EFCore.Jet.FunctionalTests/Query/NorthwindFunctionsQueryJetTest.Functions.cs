// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestModels.Northwind;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;
#nullable disable
namespace EntityFrameworkCore.Jet.FunctionalTests.Query
{
    public class NorthwindFunctionsQueryJetTest : NorthwindFunctionsQueryRelationalTestBase<NorthwindQueryJetFixture<NoopModelCustomizer>>
    {
        public NorthwindFunctionsQueryJetTest(
#pragma warning disable IDE0060 // Remove unused parameter
            NorthwindQueryJetFixture<NoopModelCustomizer> fixture,
            ITestOutputHelper testOutputHelper)
#pragma warning restore IDE0060 // Remove unused parameter
            : base(fixture)
        {
            ClearLog();
            Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
        }

        [ConditionalFact]
        public virtual void Check_all_tests_overridden()
            => TestHelpers.AssertAllMethodsOverridden(GetType());

        public override async Task Client_evaluation_of_uncorrelated_method_call(bool async)
        {
            await base.Client_evaluation_of_uncorrelated_method_call(async);

            AssertSql(
                """
SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE `o`.`UnitPrice` < 7.0 AND 10 < `o`.`ProductID`
""");
        }

        public override async Task Sum_over_round_works_correctly_in_projection(bool async)
        {
            await base.Sum_over_round_works_correctly_in_projection(async);

            AssertSql(
                """
SELECT `o`.`OrderID`, (
    SELECT IIF(SUM(ROUND(`o0`.`UnitPrice`, 2)) IS NULL, 0.0, SUM(ROUND(`o0`.`UnitPrice`, 2)))
    FROM `Order Details` AS `o0`
    WHERE `o`.`OrderID` = `o0`.`OrderID`) AS `Sum`
FROM `Orders` AS `o`
WHERE `o`.`OrderID` < 10300
""");
        }

        public override async Task Sum_over_round_works_correctly_in_projection_2(bool async)
        {
            await base.Sum_over_round_works_correctly_in_projection_2(async);

            AssertSql(
                """
SELECT `o`.`OrderID`, (
    SELECT IIF(SUM(ROUND(`o0`.`UnitPrice` * `o0`.`UnitPrice`, 2)) IS NULL, 0.0, SUM(ROUND(`o0`.`UnitPrice` * `o0`.`UnitPrice`, 2)))
    FROM `Order Details` AS `o0`
    WHERE `o`.`OrderID` = `o0`.`OrderID`) AS `Sum`
FROM `Orders` AS `o`
WHERE `o`.`OrderID` < 10300
""");
        }

        public override async Task Sum_over_truncate_works_correctly_in_projection(bool async)
        {
            await base.Sum_over_truncate_works_correctly_in_projection(async);

            AssertSql(
                """
SELECT `o`.`OrderID`, (
    SELECT IIF(SUM(INT(`o0`.`UnitPrice`)) IS NULL, 0.0, SUM(INT(`o0`.`UnitPrice`)))
    FROM `Order Details` AS `o0`
    WHERE `o`.`OrderID` = `o0`.`OrderID`) AS `Sum`
FROM `Orders` AS `o`
WHERE `o`.`OrderID` < 10300
""");
        }

        public override async Task Sum_over_truncate_works_correctly_in_projection_2(bool async)
        {
            await base.Sum_over_truncate_works_correctly_in_projection_2(async);

            AssertSql(
                """
SELECT `o`.`OrderID`, (
    SELECT IIF(SUM(INT(`o0`.`UnitPrice` * `o0`.`UnitPrice`)) IS NULL, 0.0, SUM(INT(`o0`.`UnitPrice` * `o0`.`UnitPrice`)))
    FROM `Order Details` AS `o0`
    WHERE `o`.`OrderID` = `o0`.`OrderID`) AS `Sum`
FROM `Orders` AS `o`
WHERE `o`.`OrderID` < 10300
""");
        }

        public override async Task Where_functions_nested(bool isAsync)
        {
            await base.Where_functions_nested(isAsync);

            AssertSql(
                """
    SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    WHERE CDBL(IIF(LEN(`c`.`CustomerID`) IS NULL, NULL, CLNG(LEN(`c`.`CustomerID`))))^2.0 = 25.0
    """);
        }

       public override async Task Order_by_length_twice(bool isAsync)
        {
            await base.Order_by_length_twice(isAsync);

            AssertSql(
                """
    SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    ORDER BY IIF(LEN(`c`.`CustomerID`) IS NULL, NULL, CLNG(LEN(`c`.`CustomerID`))), `c`.`CustomerID`
    """);
        }

        public override async Task Order_by_length_twice_followed_by_projection_of_naked_collection_navigation(bool isAsync)
        {
            await base.Order_by_length_twice_followed_by_projection_of_naked_collection_navigation(isAsync);

            AssertSql(
                """
    SELECT `c`.`CustomerID`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
    FROM `Customers` AS `c`
    LEFT JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`
    ORDER BY IIF(LEN(`c`.`CustomerID`) IS NULL, NULL, CLNG(LEN(`c`.`CustomerID`))), `c`.`CustomerID`
    """);
        }

        public override async Task Static_equals_nullable_datetime_compared_to_non_nullable(bool isAsync)
        {
            await base.Static_equals_nullable_datetime_compared_to_non_nullable(isAsync);

            AssertSql(
                """
@arg='1996-07-04T00:00:00.0000000' (DbType = DateTime)

SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`OrderDate` = CDATE(@arg)
""");
        }

        public override async Task Static_equals_int_compared_to_long(bool isAsync)
        {
            await base.Static_equals_int_compared_to_long(isAsync);

            AssertSql(
                $"""
                    SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
                    FROM `Orders` AS `o`
                    WHERE 0 = 1
                    """);
        }

       [ConditionalTheory]
        [MemberData(nameof(IsAsyncData))]
        public virtual async Task StandardDeviation(bool async)
        {
            await using var ctx = CreateContext();

            var query = ctx.Set<OrderDetail>()
                .GroupBy(od => od.ProductID)
                .Select(
                    g => new
                    {
                        ProductID = g.Key,
                        SampleStandardDeviation = EF.Functions.StandardDeviationSample(g.Select(od => od.UnitPrice)),
                        PopulationStandardDeviation = EF.Functions.StandardDeviationPopulation(g.Select(od => od.UnitPrice))
                    });

            var results = async
                ? await query.ToListAsync()
                : [.. query];

            var product9 = results.Single(r => r.ProductID == 9);
            Assert.Equal(8.675943752699023, product9.SampleStandardDeviation.Value, 5);
            Assert.Equal(7.759999999999856, product9.PopulationStandardDeviation.Value, 5);

            AssertSql(
                """
SELECT `o`.`ProductID`, StDev(`o`.`UnitPrice`) AS `SampleStandardDeviation`, StDevP(`o`.`UnitPrice`) AS `PopulationStandardDeviation`
FROM `Order Details` AS `o`
GROUP BY `o`.`ProductID`
""");
        }

        [ConditionalTheory]
        [MemberData(nameof(IsAsyncData))]
        public virtual async Task Variance(bool async)
        {
            await using var ctx = CreateContext();

            var query = ctx.Set<OrderDetail>()
                .GroupBy(od => od.ProductID)
                .Select(
                    g => new
                    {
                        ProductID = g.Key,
                        SampleStandardDeviation = EF.Functions.VarianceSample(g.Select(od => od.UnitPrice)),
                        PopulationStandardDeviation = EF.Functions.VariancePopulation(g.Select(od => od.UnitPrice))
                    });

            var results = async
                ? await query.ToListAsync()
                : [.. query];

            var product9 = results.Single(r => r.ProductID == 9);
            Assert.Equal(75.2719999999972, product9.SampleStandardDeviation.Value, 5);
            Assert.Equal(60.217599999997766, product9.PopulationStandardDeviation.Value, 5);

            AssertSql(
                """
SELECT `o`.`ProductID`, Var(`o`.`UnitPrice`) AS `SampleStandardDeviation`, VarP(`o`.`UnitPrice`) AS `PopulationStandardDeviation`
FROM `Order Details` AS `o`
GROUP BY `o`.`ProductID`
""");
        }

        private void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);

        protected override void ClearLog()
            => Fixture.TestSqlLoggerFactory.Clear();
    }
}
