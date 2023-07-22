// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query
{
    public class NorthwindKeylessEntitiesQueryJetTest : NorthwindKeylessEntitiesQueryRelationalTestBase<
        NorthwindQueryJetFixture<NoopModelCustomizer>>
    {
        public NorthwindKeylessEntitiesQueryJetTest(
            NorthwindQueryJetFixture<NoopModelCustomizer> fixture,
            ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            ClearLog();
            Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
        }

        protected override bool CanExecuteQueryString
            => false;

        [ConditionalTheory]
        public override async Task KeylessEntity_simple(bool isAsync)
        {
            await base.KeylessEntity_simple(isAsync);

            AssertSql(
"""
SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region] FROM [Customers] AS [c]
""");
        }

        [ConditionalTheory]
        public override async Task KeylessEntity_where_simple(bool isAsync)
        {
            await base.KeylessEntity_where_simple(isAsync);

            AssertSql(
"""
SELECT `m`.`Address`, `m`.`City`, `m`.`CompanyName`, `m`.`ContactName`, `m`.`ContactTitle`
FROM (
    SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region] FROM [Customers] AS [c]
) AS `m`
WHERE `m`.`City` = 'London'
""");
        }

        public override async Task KeylessEntity_by_database_view(bool isAsync)
        {
            await base.KeylessEntity_by_database_view(isAsync);

            AssertSql(
                """
SELECT `a`.`CategoryName`, `a`.`ProductID`, `a`.`ProductName`
FROM `Alphabetical list of products` AS `a`
""");
        }

        public override async Task KeylessEntity_with_nav_defining_query(bool isAsync)
        {
            await base.KeylessEntity_with_nav_defining_query(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__ef_filter___searchTerm_0='A' (Size = 4000)")}

{AssertSqlHelper.Declaration("@__ef_filter___searchTerm_1='A' (Size = 4000)")}

SELECT `c`.`CompanyName`, (
    SELECT COUNT(*)
    FROM `Orders` AS `o`
    WHERE `c`.`CustomerID` = `o`.`CustomerID`) AS `OrderCount`, {AssertSqlHelper.Parameter("@__ef_filter___searchTerm_0")} AS `SearchTerm`
FROM `Customers` AS `c`
WHERE (({AssertSqlHelper.Parameter("@__ef_filter___searchTerm_1")} = '') OR (`c`.`CompanyName` IS NOT NULL AND (LEFT(`c`.`CompanyName`, LEN({AssertSqlHelper.Parameter("@__ef_filter___searchTerm_1")})) = {AssertSqlHelper.Parameter("@__ef_filter___searchTerm_1")}))) AND ((
    SELECT COUNT(*)
    FROM `Orders` AS `o`
    WHERE `c`.`CustomerID` = `o`.`CustomerID`) > 0)");
        }

        public override async Task KeylessEntity_with_mixed_tracking(bool isAsync)
        {
            await base.KeylessEntity_with_mixed_tracking(isAsync);

            AssertSql(
                """
    SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `m`.`CustomerID`
    FROM `Customers` AS `c`
    INNER JOIN (
        select * from `Orders`
    ) AS `m` ON `c`.`CustomerID` = `m`.`CustomerID`
    """);
        }

        public override async Task KeylessEntity_with_defining_query(bool isAsync)
        {
            await base.KeylessEntity_with_defining_query(isAsync);

            AssertSql(
                """
SELECT `m`.`CustomerID`
FROM (
    select * from `Orders`
) AS `m`
WHERE `m`.`CustomerID` = 'ALFKI'
""");
        }

        public override async Task KeylessEntity_with_defining_query_and_correlated_collection(bool isAsync)
        {
            await base.KeylessEntity_with_defining_query_and_correlated_collection(isAsync);

            AssertSql();
        }

        public override async Task KeylessEntity_select_where_navigation(bool isAsync)
        {
            await base.KeylessEntity_select_where_navigation(isAsync);

            AssertSql(
                """
    SELECT `m`.`CustomerID`
    FROM (
        select * from `Orders`
    ) AS `m`
    LEFT JOIN `Customers` AS `c` ON `m`.`CustomerID` = `c`.`CustomerID`
    WHERE `c`.`City` = 'Seattle'
    """);
        }

        public override async Task KeylessEntity_select_where_navigation_multi_level(bool isAsync)
        {
            await base.KeylessEntity_select_where_navigation_multi_level(isAsync);

            AssertSql(
                """
    SELECT `m`.`CustomerID`
    FROM (
        select * from `Orders`
    ) AS `m`
    LEFT JOIN `Customers` AS `c` ON `m`.`CustomerID` = `c`.`CustomerID`
    WHERE EXISTS (
        SELECT 1
        FROM `Orders` AS `o`
        WHERE (`c`.`CustomerID` IS NOT NULL) AND `c`.`CustomerID` = `o`.`CustomerID`)
    """);
        }

        [ConditionalFact]
        public override async Task Auto_initialized_view_set(bool isAsync)
        {
            await base.Auto_initialized_view_set(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID` + '' as `CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region` FROM `Customers` AS `c`");
        }

        private void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);

        protected override void ClearLog()
            => Fixture.TestSqlLoggerFactory.Clear();
    }
}
