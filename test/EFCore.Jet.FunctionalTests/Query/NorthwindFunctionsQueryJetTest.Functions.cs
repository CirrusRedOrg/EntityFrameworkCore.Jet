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

        public override async Task TimeSpan_Compare_to_simple_zero(bool async, bool compareTo)
        {
            await base.TimeSpan_Compare_to_simple_zero(async, compareTo);

            AssertSql(
"""
@__myDatetime_0='1998-05-04T00:00:00.0000000' (DbType = DateTime)

SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`OrderDate` = CDATE(@__myDatetime_0)
""",
//
"""
@__myDatetime_0='1998-05-04T00:00:00.0000000' (DbType = DateTime)

SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`OrderDate` <> CDATE(@__myDatetime_0) OR `o`.`OrderDate` IS NULL
""",
//
"""
@__myDatetime_0='1998-05-04T00:00:00.0000000' (DbType = DateTime)

SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`OrderDate` > CDATE(@__myDatetime_0)
""",
//
"""
@__myDatetime_0='1998-05-04T00:00:00.0000000' (DbType = DateTime)

SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`OrderDate` <= CDATE(@__myDatetime_0)
""",
//
"""
@__myDatetime_0='1998-05-04T00:00:00.0000000' (DbType = DateTime)

SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`OrderDate` > CDATE(@__myDatetime_0)
""",
//
"""
@__myDatetime_0='1998-05-04T00:00:00.0000000' (DbType = DateTime)

SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`OrderDate` <= CDATE(@__myDatetime_0)
""");
        }

        public override async Task String_StartsWith_Literal(bool isAsync)
        {
            await base.String_StartsWith_Literal(isAsync);

            AssertSql(
"""
SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`ContactName` LIKE 'M%'
""");
        }

        public override async Task String_StartsWith_Identity(bool isAsync)
        {
            await base.String_StartsWith_Identity(isAsync);

            AssertSql(
                """
SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`ContactName` IS NOT NULL AND LEFT(`c`.`ContactName`, LEN(`c`.`ContactName`)) = `c`.`ContactName`
""");
        }

        public override async Task String_StartsWith_Column(bool isAsync)
        {
            await base.String_StartsWith_Column(isAsync);

            AssertSql(
                """
SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`ContactName` IS NOT NULL AND LEFT(`c`.`ContactName`, LEN(`c`.`ContactName`)) = `c`.`ContactName`
""");
        }

        public override async Task String_StartsWith_MethodCall(bool isAsync)
        {
            await base.String_StartsWith_MethodCall(isAsync);

            AssertSql(
"""
SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`ContactName` LIKE 'M%'
""");
        }

        public override async Task String_EndsWith_Literal(bool isAsync)
        {
            await base.String_EndsWith_Literal(isAsync);

            AssertSql(
"""
SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`ContactName` LIKE '%b'
""");
        }

        public override async Task String_EndsWith_Identity(bool isAsync)
        {
            await base.String_EndsWith_Identity(isAsync);

            AssertSql(
                """
SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`ContactName` IS NOT NULL AND RIGHT(`c`.`ContactName`, LEN(`c`.`ContactName`)) = `c`.`ContactName`
""");
        }

        public override async Task String_EndsWith_Column(bool isAsync)
        {
            await base.String_EndsWith_Column(isAsync);

            AssertSql(
                """
SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`ContactName` IS NOT NULL AND RIGHT(`c`.`ContactName`, LEN(`c`.`ContactName`)) = `c`.`ContactName`
""");
        }

        public override async Task String_EndsWith_MethodCall(bool isAsync)
        {
            await base.String_EndsWith_MethodCall(isAsync);

            AssertSql(
"""
SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`ContactName` LIKE '%m'
""");
        }

        public override async Task String_Contains_Literal(bool isAsync)
        {
            await AssertQuery(
                isAsync,
                ss => ss.Set<Customer>().Where(c => c.ContactName.Contains("M")), // case-insensitive
                ss => ss.Set<Customer>().Where(c => c.ContactName.Contains("M") || c.ContactName.Contains("m")), // case-sensitive
                entryCount: 34);

            AssertSql(
                """
SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`ContactName` LIKE '%M%'
""");
        }

        public override async Task String_Contains_Identity(bool isAsync)
        {
            await base.String_Contains_Identity(isAsync);

            AssertSql(
                """
SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`ContactName` IS NOT NULL AND (INSTR(1, `c`.`ContactName`, `c`.`ContactName`, 1) > 0 OR (`c`.`ContactName` LIKE ''))
""");
        }

        public override async Task String_Contains_Column(bool isAsync)
        {
            await base.String_Contains_Column(isAsync);

            AssertSql(
                """
SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`ContactName` IS NOT NULL AND (INSTR(1, `c`.`ContactName`, `c`.`ContactName`, 1) > 0 OR (`c`.`ContactName` LIKE ''))
""");
        }

        public override async Task String_Contains_constant_with_whitespace(bool async)
        {
            await base.String_Contains_constant_with_whitespace(async);

            AssertSql(
                """
SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`ContactName` LIKE '%     %'
""");
        }

        public override async Task String_Contains_parameter_with_whitespace(bool async)
        {
            await base.String_Contains_parameter_with_whitespace(async);

            AssertSql(
                """
@__pattern_0_rewritten='%     %' (Size = 30)

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`ContactName` LIKE @__pattern_0_rewritten
""");
        }

        public override async Task String_FirstOrDefault_MethodCall(bool async)
        {
            await base.String_FirstOrDefault_MethodCall(async);
            AssertSql(
                """
SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE MID(`c`.`ContactName`, 1, 1) = 'A'
""");
        }

        public override async Task String_LastOrDefault_MethodCall(bool async)
        {
            await base.String_LastOrDefault_MethodCall(async);
            AssertSql(
                """
SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE MID(`c`.`ContactName`, LEN(`c`.`ContactName`), 1) = 's'
""");
        }

        public override async Task String_Contains_MethodCall(bool isAsync)
        {
            await AssertQuery(
                isAsync,
                ss => ss.Set<Customer>().Where(c => c.ContactName.Contains(LocalMethod1())), // case-insensitive
                ss => ss.Set<Customer>().Where(
                    c => c.ContactName.Contains(LocalMethod1().ToLower())
                        || c.ContactName.Contains(LocalMethod1().ToUpper())), // case-sensitive
                entryCount: 34);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`ContactName` LIKE '%M%'");
        }

        public override async Task String_Join_over_non_nullable_column(bool async)
        {
            await base.String_Join_over_non_nullable_column(async);

            AssertSql(
"""
SELECT `t`.`City`, `c0`.`CustomerID`
FROM (
    SELECT `c`.`City`
    FROM `Customers` AS `c`
    GROUP BY `c`.`City`
) AS `t`
LEFT JOIN `Customers` AS `c0` ON `t`.`City` = `c0`.`City`
ORDER BY `t`.`City`
""");
        }

        public override async Task String_Join_over_nullable_column(bool async)
        {
            await base.String_Join_over_nullable_column(async);

            AssertSql(
"""
SELECT `t`.`City`, `c0`.`Region`, `c0`.`CustomerID`
FROM (
    SELECT `c`.`City`
    FROM `Customers` AS `c`
    GROUP BY `c`.`City`
) AS `t`
LEFT JOIN `Customers` AS `c0` ON `t`.`City` = `c0`.`City`
ORDER BY `t`.`City`
""");
        }

        public override async Task String_Join_with_predicate(bool async)
        {
            await base.String_Join_with_predicate(async);

            AssertSql(
                """
SELECT `t`.`City`, `t0`.`CustomerID`
FROM (
    SELECT `c`.`City`
    FROM `Customers` AS `c`
    GROUP BY `c`.`City`
) AS `t`
LEFT JOIN (
    SELECT `c0`.`CustomerID`, `c0`.`City`
    FROM `Customers` AS `c0`
    WHERE IIF(LEN(`c0`.`ContactName`) IS NULL, NULL, CLNG(LEN(`c0`.`ContactName`))) > 10
) AS `t0` ON `t`.`City` = `t0`.`City`
ORDER BY `t`.`City`
""");
        }

        public override async Task String_Join_with_ordering(bool async)
        {
            await base.String_Join_with_ordering(async);

            AssertSql(
"""
SELECT `t`.`City`, `c0`.`CustomerID`
FROM (
    SELECT `c`.`City`
    FROM `Customers` AS `c`
    GROUP BY `c`.`City`
) AS `t`
LEFT JOIN `Customers` AS `c0` ON `t`.`City` = `c0`.`City`
ORDER BY `t`.`City`, `c0`.`CustomerID` DESC
""");
        }

        public override async Task String_Concat(bool async)
        {
            await base.String_Concat(async);

            AssertSql(
"""
SELECT `t`.`City`, `c0`.`CustomerID`
FROM (
    SELECT `c`.`City`
    FROM `Customers` AS `c`
    GROUP BY `c`.`City`
) AS `t`
LEFT JOIN `Customers` AS `c0` ON `t`.`City` = `c0`.`City`
ORDER BY `t`.`City`
""");
        }

        public override async Task String_Compare_simple_zero(bool isAsync)
        {
            await base.String_Compare_simple_zero(isAsync);

            // issue #15994
            //            AssertSql(
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` = 'ALFKI'",
            //                //
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` <> 'ALFKI'",
            //                //
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` > 'ALFKI'",
            //                //
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` <= 'ALFKI'",
            //                //
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` > 'ALFKI'",
            //                //
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` <= 'ALFKI'");
        }

        public override async Task String_Compare_simple_one(bool isAsync)
        {
            await base.String_Compare_simple_one(isAsync);

            // issue #15994
            //            AssertSql(
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` > 'ALFKI'",
            //                //
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` < 'ALFKI'",
            //                //
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` <= 'ALFKI'",
            //                //
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` <= 'ALFKI'",
            //                //
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` >= 'ALFKI'",
            //                //
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` >= 'ALFKI'");
        }

        public override async Task String_compare_with_parameter(bool isAsync)
        {
            await base.String_compare_with_parameter(isAsync);

            // issue #15994
            //            AssertSql(
            //                $@"{AssertSqlHelper.Declaration("@__customer_CustomerID_0='ALFKI' (Size = 4000)")}

            //SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` > @__customer_CustomerID_0",
            //                //
            //                $@"{AssertSqlHelper.Declaration("@__customer_CustomerID_0='ALFKI' (Size = 4000)")}

            //SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` < @__customer_CustomerID_0",
            //                //
            //                $@"{AssertSqlHelper.Declaration("@__customer_CustomerID_0='ALFKI' (Size = 4000)")}

            //SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` <= @__customer_CustomerID_0",
            //                //
            //                $@"{AssertSqlHelper.Declaration("@__customer_CustomerID_0='ALFKI' (Size = 4000)")}

            //SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` <= @__customer_CustomerID_0",
            //                //
            //                $@"{AssertSqlHelper.Declaration("@__customer_CustomerID_0='ALFKI' (Size = 4000)")}

            //SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` >= @__customer_CustomerID_0",
            //                //
            //                $@"{AssertSqlHelper.Declaration("@__customer_CustomerID_0='ALFKI' (Size = 4000)")}

            //SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` >= @__customer_CustomerID_0");
        }

        public override async Task String_Compare_simple_more_than_one(bool isAsync)
        {
            await base.String_Compare_simple_more_than_one(isAsync);

            // issue #15994
            //            AssertSql(
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`",
            //                //
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`",
            //                //
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`");
        }

        public override async Task String_Compare_nested(bool isAsync)
        {
            await base.String_Compare_nested(isAsync);

            // issue #15994
            //            AssertSql(
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` = 'M' + `c`.`CustomerID`",
            //                //
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` <> UPPER(`c`.`CustomerID`)",
            //                //
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` > REPLACE('ALFKI', 'ALF', `c`.`CustomerID`)",
            //                //
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` <= 'M' + `c`.`CustomerID`",
            //                //
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` > UPPER(`c`.`CustomerID`)",
            //                //
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` < REPLACE('ALFKI', 'ALF', `c`.`CustomerID`)");
        }

        public override async Task String_Compare_multi_predicate(bool isAsync)
        {
            await base.String_Compare_multi_predicate(isAsync);

            // issue #15994
            //            AssertSql(
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` >= 'ALFKI' AND `c`.`CustomerID` < 'CACTU'",
            //                //
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`ContactTitle` = 'Owner' AND `c`.`Country` <> 'USA'");
        }

        public override async Task String_Compare_to_simple_zero(bool isAsync)
        {
            await base.String_Compare_to_simple_zero(isAsync);

            // issue #15994
            //            AssertSql(
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` = 'ALFKI'",
            //                //
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` <> 'ALFKI'",
            //                //
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` > 'ALFKI'",
            //                //
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` <= 'ALFKI'",
            //                //
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` > 'ALFKI'",
            //                //
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` <= 'ALFKI'");
        }

        public override async Task String_Compare_to_simple_one(bool isAsync)
        {
            await base.String_Compare_to_simple_one(isAsync);

            // issue #15994
            //            AssertSql(
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` > 'ALFKI'",
            //                //
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` < 'ALFKI'",
            //                //
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` <= 'ALFKI'",
            //                //
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` <= 'ALFKI'",
            //                //
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` >= 'ALFKI'",
            //                //
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` >= 'ALFKI'");
        }

        public override async Task String_compare_to_with_parameter(bool isAsync)
        {
            await base.String_compare_to_with_parameter(isAsync);

            // issue #15994
            //            AssertSql(
            //                $@"{AssertSqlHelper.Declaration("@__customer_CustomerID_0='ALFKI' (Size = 4000)")}

            //SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` > @__customer_CustomerID_0",
            //                //
            //                $@"{AssertSqlHelper.Declaration("@__customer_CustomerID_0='ALFKI' (Size = 4000)")}

            //SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` < @__customer_CustomerID_0",
            //                //
            //                $@"{AssertSqlHelper.Declaration("@__customer_CustomerID_0='ALFKI' (Size = 4000)")}

            //SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` <= @__customer_CustomerID_0",
            //                //
            //                $@"{AssertSqlHelper.Declaration("@__customer_CustomerID_0='ALFKI' (Size = 4000)")}

            //SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` <= @__customer_CustomerID_0",
            //                //
            //                $@"{AssertSqlHelper.Declaration("@__customer_CustomerID_0='ALFKI' (Size = 4000)")}

            //SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` >= @__customer_CustomerID_0",
            //                //
            //                $@"{AssertSqlHelper.Declaration("@__customer_CustomerID_0='ALFKI' (Size = 4000)")}

            //SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` >= @__customer_CustomerID_0");
        }

        public override async Task String_Compare_to_simple_more_than_one(bool isAsync)
        {
            await base.String_Compare_to_simple_more_than_one(isAsync);

            // issue #15994
            //            AssertSql(
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`",
            //                //
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`",
            //                //
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`");
        }

        public override async Task String_Compare_to_nested(bool isAsync)
        {
            await base.String_Compare_to_nested(isAsync);

            //issue #15994
            //            AssertSql(
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` = 'M' + `c`.`CustomerID`",
            //                //
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` <> UPPER(`c`.`CustomerID`)",
            //                //
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` > REPLACE('ALFKI', 'ALF', `c`.`CustomerID`)",
            //                //
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` <= 'M' + `c`.`CustomerID`",
            //                //
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` > UPPER(`c`.`CustomerID`)",
            //                //
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` < REPLACE('ALFKI', 'ALF', `c`.`CustomerID`)");
        }

        public override async Task String_Compare_to_multi_predicate(bool isAsync)
        {
            await base.String_Compare_to_multi_predicate(isAsync);

            // issue #15994
            //            AssertSql(
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`CustomerID` >= 'ALFKI' AND `c`.`CustomerID` < 'CACTU'",
            //                //
            //                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            //FROM `Customers` AS `c`
            //WHERE `c`.`ContactTitle` = 'Owner' AND `c`.`Country` <> 'USA'");
        }

        public override async Task DateTime_Compare_to_simple_zero(bool isAsync, bool compareTo)
        {
            await base.DateTime_Compare_to_simple_zero(isAsync, compareTo);

            // issue #15994
            //            AssertSql(
            //                $@"{AssertSqlHelper.Declaration("@__myDatetime_0='1998-05-04T00:00:00'")}

            //SELECT `c`.`OrderID`, `c`.`CustomerID`, `c`.`EmployeeID`, `c`.`OrderDate`
            //FROM `Orders` AS `c`
            //WHERE `c`.`OrderDate` = @__myDatetime_0",
            //                //
            //                $@"{AssertSqlHelper.Declaration("@__myDatetime_0='1998-05-04T00:00:00'")}

            //SELECT `c`.`OrderID`, `c`.`CustomerID`, `c`.`EmployeeID`, `c`.`OrderDate`
            //FROM `Orders` AS `c`
            //WHERE `c`.`OrderDate` <> @__myDatetime_0",
            //                //
            //                $@"{AssertSqlHelper.Declaration("@__myDatetime_0='1998-05-04T00:00:00'")}

            //SELECT `c`.`OrderID`, `c`.`CustomerID`, `c`.`EmployeeID`, `c`.`OrderDate`
            //FROM `Orders` AS `c`
            //WHERE `c`.`OrderDate` > @__myDatetime_0",
            //                //
            //                $@"{AssertSqlHelper.Declaration("@__myDatetime_0='1998-05-04T00:00:00'")}

            //SELECT `c`.`OrderID`, `c`.`CustomerID`, `c`.`EmployeeID`, `c`.`OrderDate`
            //FROM `Orders` AS `c`
            //WHERE `c`.`OrderDate` <= @__myDatetime_0",
            //                //
            //                $@"{AssertSqlHelper.Declaration("@__myDatetime_0='1998-05-04T00:00:00'")}

            //SELECT `c`.`OrderID`, `c`.`CustomerID`, `c`.`EmployeeID`, `c`.`OrderDate`
            //FROM `Orders` AS `c`
            //WHERE `c`.`OrderDate` > @__myDatetime_0",
            //                //
            //                $@"{AssertSqlHelper.Declaration("@__myDatetime_0='1998-05-04T00:00:00'")}

            //SELECT `c`.`OrderID`, `c`.`CustomerID`, `c`.`EmployeeID`, `c`.`OrderDate`
            //FROM `Orders` AS `c`
            //WHERE `c`.`OrderDate` <= @__myDatetime_0");
        }

        public override async Task Int_Compare_to_simple_zero(bool isAsync)
        {
            await base.Int_Compare_to_simple_zero(isAsync);

            // issue #15994
            //            AssertSql(
            //                $@"{AssertSqlHelper.Declaration("@__orderId_0='10250'")}

            //SELECT `c`.`OrderID`, `c`.`CustomerID`, `c`.`EmployeeID`, `c`.`OrderDate`
            //FROM `Orders` AS `c`
            //WHERE `c`.`OrderID` = @__orderId_0",
            //                //
            //                $@"{AssertSqlHelper.Declaration("@__orderId_0='10250'")}

            //SELECT `c`.`OrderID`, `c`.`CustomerID`, `c`.`EmployeeID`, `c`.`OrderDate`
            //FROM `Orders` AS `c`
            //WHERE `c`.`OrderID` <> @__orderId_0",
            //                //
            //                $@"{AssertSqlHelper.Declaration("@__orderId_0='10250'")}

            //SELECT `c`.`OrderID`, `c`.`CustomerID`, `c`.`EmployeeID`, `c`.`OrderDate`
            //FROM `Orders` AS `c`
            //WHERE `c`.`OrderID` > @__orderId_0",
            //                //
            //                $@"{AssertSqlHelper.Declaration("@__orderId_0='10250'")}

            //SELECT `c`.`OrderID`, `c`.`CustomerID`, `c`.`EmployeeID`, `c`.`OrderDate`
            //FROM `Orders` AS `c`
            //WHERE `c`.`OrderID` <= @__orderId_0",
            //                //
            //                $@"{AssertSqlHelper.Declaration("@__orderId_0='10250'")}

            //SELECT `c`.`OrderID`, `c`.`CustomerID`, `c`.`EmployeeID`, `c`.`OrderDate`
            //FROM `Orders` AS `c`
            //WHERE `c`.`OrderID` > @__orderId_0",
            //                //
            //                $@"{AssertSqlHelper.Declaration("@__orderId_0='10250'")}

            //SELECT `c`.`OrderID`, `c`.`CustomerID`, `c`.`EmployeeID`, `c`.`OrderDate`
            //FROM `Orders` AS `c`
            //WHERE `c`.`OrderID` <= @__orderId_0");
        }

        public override async Task Where_math_abs1(bool isAsync)
        {
            await base.Where_math_abs1(isAsync);

            AssertSql(
                $@"SELECT `p`.`ProductID`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice`, `p`.`UnitsInStock`
FROM `Products` AS `p`
WHERE ABS(`p`.`ProductID`) > 10");
        }

        public override async Task Where_math_abs2(bool isAsync)
        {
            await base.Where_math_abs2(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE `o`.`UnitPrice` < 7.0 AND ABS(`o`.`Quantity`) > 10");
        }

        public override async Task Where_math_abs3(bool isAsync)
        {
            await base.Where_math_abs3(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE `o`.`Quantity` < 5 AND ABS(`o`.`UnitPrice`) > 10.0");
        }

        public override async Task Where_math_abs_uncorrelated(bool isAsync)
        {
            await base.Where_math_abs_uncorrelated(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE `o`.`UnitPrice` < 7.0 AND 10 < `o`.`ProductID`");
        }

        public override async Task Where_math_ceiling1(bool isAsync)
        {
            await base.Where_math_ceiling1(isAsync);

            AssertSql(
                """
SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE `o`.`UnitPrice` < 7.0 AND IIF(FIX(CDBL(`o`.`Discount`)) = CDBL(`o`.`Discount`), FIX(CDBL(`o`.`Discount`)), FIX(CDBL(`o`.`Discount`)) + 1.0) > 0.0
""");
        }

        public override async Task Where_math_ceiling2(bool isAsync)
        {
            await base.Where_math_ceiling2(isAsync);

            AssertSql(
                """
SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE `o`.`Quantity` < 5 AND IIF(FIX(`o`.`UnitPrice`) = `o`.`UnitPrice`, FIX(`o`.`UnitPrice`), FIX(`o`.`UnitPrice`) + 1.0) > 10.0
""");
        }

        public override async Task Where_math_floor(bool isAsync)
        {
            await base.Where_math_floor(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE `o`.`Quantity` < 5 AND FIX(`o`.`UnitPrice`) > 10.0");
        }

        public override async Task Where_math_power(bool isAsync)
        {
            await base.Where_math_power(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE CDBL(`o`.`Discount`)^3.0 > 0.004999999888241291");
        }

        public override async Task Where_math_square(bool async)
        {
            await base.Where_math_square(async);

            AssertSql(
                """
SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE CDBL(`o`.`Discount`)^2.0 > 0.05000000074505806
""");
        }

        public override async Task Where_math_round(bool isAsync)
        {
            await base.Where_math_round(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE `o`.`Quantity` < 5 AND ROUND(`o`.`UnitPrice`, 0) > 10.0");
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

        public override async Task Select_math_round_int(bool isAsync)
        {
            await base.Select_math_round_int(isAsync);

            AssertSql(
                $@"SELECT ROUND(CDBL(`o`.`OrderID`), 0) AS `A`
FROM `Orders` AS `o`
WHERE `o`.`OrderID` < 10250");
        }

        public override async Task Select_math_truncate_int(bool isAsync)
        {
            await base.Select_math_truncate_int(isAsync);

            AssertSql(
                $@"SELECT INT(CDBL(`o`.`OrderID`)) AS `A`
FROM `Orders` AS `o`
WHERE `o`.`OrderID` < 10250");
        }

        public override async Task Where_math_round2(bool isAsync)
        {
            await base.Where_math_round2(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE ROUND(`o`.`UnitPrice`, 2) > 100.0");
        }

        public override async Task Where_math_truncate(bool isAsync)
        {
            await base.Where_math_truncate(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE `o`.`Quantity` < 5 AND INT(`o`.`UnitPrice`) > 10.0");
        }

        public override async Task Where_math_exp(bool isAsync)
        {
            await base.Where_math_exp(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE `o`.`OrderID` = 11077 AND EXP(CDBL(`o`.`Discount`)) > 1.0");
        }

        public override async Task Where_math_log10(bool isAsync)
        {
            await base.Where_math_log10(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE `o`.`OrderID` = 11077 AND `o`.`Discount` > 0 AND (LOG(CDBL(`o`.`Discount`)) / 2.3025850929940459) < 0.0");
        }

        public override async Task Where_math_log(bool isAsync)
        {
            await base.Where_math_log(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE `o`.`OrderID` = 11077 AND `o`.`Discount` > 0 AND LOG(CDBL(`o`.`Discount`)) < 0.0");
        }

        public override async Task Where_math_log_new_base(bool isAsync)
        {
            await base.Where_math_log_new_base(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE `o`.`OrderID` = 11077 AND `o`.`Discount` > 0 AND (LOG(CDBL(`o`.`Discount`)) / LOG(7.0)) < 0.0");
        }

        public override async Task Where_math_sqrt(bool isAsync)
        {
            await base.Where_math_sqrt(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE `o`.`OrderID` = 11077 AND SQR(CDBL(`o`.`Discount`)) > 0.0");
        }

        public override async Task Where_math_acos(bool isAsync)
        {
            await base.Where_math_acos(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE `o`.`OrderID` = 11077 AND (1.5707963267948966 + ATN(-CDBL(`o`.`Discount`) / SQR(-(CDBL(`o`.`Discount`) * CDBL(`o`.`Discount`)) + 1.0))) > 1.0");
        }

        public override async Task Where_math_asin(bool isAsync)
        {
            await base.Where_math_asin(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE `o`.`OrderID` = 11077 AND ATN(CDBL(`o`.`Discount`) / SQR(-(CDBL(`o`.`Discount`) * CDBL(`o`.`Discount`)) + 1.0)) > 0.0");
        }

        public override async Task Where_math_atan(bool isAsync)
        {
            await base.Where_math_atan(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE `o`.`OrderID` = 11077 AND ATN(CDBL(`o`.`Discount`)) > 0.0");
        }

        public override async Task Where_math_atan2(bool isAsync)
        {
            await base.Where_math_atan2(isAsync);

            AssertSql(
                """
    SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
    FROM `Order Details` AS `o`
    WHERE `o`.`OrderID` = 11077 AND ATN(CDBL(`o`.`Discount`) / 1.0) > 0.0
    """);
        }

        public override async Task Where_math_cos(bool isAsync)
        {
            await base.Where_math_cos(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE `o`.`OrderID` = 11077 AND COS(CDBL(`o`.`Discount`)) > 0.0");
        }

        public override async Task Where_math_sin(bool isAsync)
        {
            await base.Where_math_sin(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE `o`.`OrderID` = 11077 AND SIN(CDBL(`o`.`Discount`)) > 0.0");
        }

        public override async Task Where_math_tan(bool isAsync)
        {
            await base.Where_math_tan(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE `o`.`OrderID` = 11077 AND TAN(CDBL(`o`.`Discount`)) > 0.0");
        }

        public override async Task Where_math_sign(bool isAsync)
        {
            await base.Where_math_sign(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE `o`.`OrderID` = 11077 AND SGN(`o`.`Discount`) > 0");
        }

        public override async Task Where_math_min(bool async)
        {
            // Translate Math.Min.
            await AssertTranslationFailed(() => base.Where_math_min(async));

            AssertSql();
        }

        public override async Task Where_math_max(bool async)
        {
            // Translate Math.Max.
            await AssertTranslationFailed(() => base.Where_math_max(async));

            AssertSql();
        }

        public override async Task Where_mathf_abs1(bool async)
        {
            await base.Where_mathf_abs1(async);

            AssertSql(
    """
SELECT `p`.`ProductID`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice`, `p`.`UnitsInStock`
FROM `Products` AS `p`
WHERE ABS(CSNG(`p`.`ProductID`)) > 10
""");
        }

        public override async Task Where_mathf_ceiling1(bool async)
        {
            await base.Where_mathf_ceiling1(async);

            AssertSql(
                """
SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE `o`.`UnitPrice` < 7.0 AND IIF(FIX(`o`.`Discount`) = `o`.`Discount`, FIX(`o`.`Discount`), FIX(`o`.`Discount`) + 1) > 0
""");
        }

        public override async Task Where_mathf_floor(bool async)
        {
            await base.Where_mathf_floor(async);

            AssertSql(
                """
SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE `o`.`Quantity` < 5 AND FIX(CSNG(`o`.`UnitPrice`)) > 10
""");
        }

        public override async Task Where_mathf_power(bool async)
        {
            await base.Where_mathf_power(async);

            AssertSql(
    """
SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE `o`.`Discount`^3 > 0.005
""");
        }

        public override async Task Where_mathf_square(bool async)
        {
            await base.Where_mathf_square(async);

            AssertSql(
    """
SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE `o`.`Discount`^2 > 0.05
""");
        }

        public override async Task Where_mathf_round2(bool async)
        {
            await base.Where_mathf_round2(async);

            AssertSql(
    """
SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE CSNG(ROUND(CSNG(`o`.`UnitPrice`), 2)) > 100
""");
        }

        public override async Task Select_mathf_round(bool async)
        {
            await base.Select_mathf_round(async);

            AssertSql(
    """
SELECT CSNG(ROUND(CSNG(`o`.`OrderID`), 0))
FROM `Orders` AS `o`
WHERE `o`.`OrderID` < 10250
""");
        }

        public override async Task Select_mathf_round2(bool async)
        {
            await base.Select_mathf_round2(async);

            AssertSql(
    """
SELECT CSNG(ROUND(CSNG(`o`.`UnitPrice`), 2))
FROM `Order Details` AS `o`
WHERE `o`.`Quantity` < 5
""");
        }

        public override async Task Where_mathf_truncate(bool async)
        {
            await base.Where_mathf_truncate(async);

            AssertSql(
                """
    SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
    FROM `Order Details` AS `o`
    WHERE `o`.`Quantity` < 5 AND IIF(INT(CSNG(`o`.`UnitPrice`)) IS NULL, NULL, CSNG(INT(CSNG(`o`.`UnitPrice`)))) > 10
    """);
        }

        public override async Task Select_mathf_truncate(bool async)
        {
            await base.Select_mathf_truncate(async);

            AssertSql(
                """
    SELECT IIF(INT(CSNG(`o`.`UnitPrice`)) IS NULL, NULL, CSNG(INT(CSNG(`o`.`UnitPrice`))))
    FROM `Order Details` AS `o`
    WHERE `o`.`Quantity` < 5
    """);
        }

        public override async Task Where_mathf_exp(bool async)
        {
            await base.Where_mathf_exp(async);

            AssertSql(
    """
SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE `o`.`OrderID` = 11077 AND EXP(`o`.`Discount`) > 1
""");
        }

        public override async Task Where_mathf_log10(bool async)
        {
            await base.Where_mathf_log10(async);

            AssertSql(
    """
SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE `o`.`OrderID` = 11077 AND `o`.`Discount` > 0 AND (LOG(`o`.`Discount`) / 2.3025851) < 0
""");
        }

        public override async Task Where_mathf_log(bool async)
        {
            await base.Where_mathf_log(async);

            AssertSql(
    """
SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE `o`.`OrderID` = 11077 AND `o`.`Discount` > 0 AND LOG(`o`.`Discount`) < 0
""");
        }

        public override async Task Where_mathf_log_new_base(bool async)
        {
            await base.Where_mathf_log_new_base(async);

            AssertSql(
    """
SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE `o`.`OrderID` = 11077 AND `o`.`Discount` > 0 AND (LOG(`o`.`Discount`) / LOG(7)) < 0
""");
        }

        public override async Task Where_mathf_sqrt(bool async)
        {
            await base.Where_mathf_sqrt(async);

            AssertSql(
    """
SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE `o`.`OrderID` = 11077 AND SQR(`o`.`Discount`) > 0
""");
        }

        public override async Task Where_mathf_acos(bool async)
        {
            await base.Where_mathf_acos(async);

            AssertSql(
    """
SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE `o`.`OrderID` = 11077 AND (1.5707963267948966 + ATN(-`o`.`Discount` / SQR(-(`o`.`Discount` * `o`.`Discount`) + 1))) > 1.0
""");
        }

        public override async Task Where_mathf_asin(bool async)
        {
            await base.Where_mathf_asin(async);

            AssertSql(
    """
SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE `o`.`OrderID` = 11077 AND ATN(`o`.`Discount` / SQR(-(`o`.`Discount` * `o`.`Discount`) + 1)) > 0
""");
        }

        public override async Task Where_mathf_atan(bool async)
        {
            await base.Where_mathf_atan(async);

            AssertSql(
    """
SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE `o`.`OrderID` = 11077 AND ATN(`o`.`Discount`) > 0
""");
        }

        public override async Task Where_mathf_atan2(bool async)
        {
            await base.Where_mathf_atan2(async);

            AssertSql(
                """
    SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
    FROM `Order Details` AS `o`
    WHERE `o`.`OrderID` = 11077 AND ATN(`o`.`Discount` / 1) > 0
    """);
        }

        public override async Task Where_mathf_cos(bool async)
        {
            await base.Where_mathf_cos(async);

            AssertSql(
    """
SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE `o`.`OrderID` = 11077 AND COS(`o`.`Discount`) > 0
""");
        }

        public override async Task Where_mathf_sin(bool async)
        {
            await base.Where_mathf_sin(async);

            AssertSql(
    """
SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE `o`.`OrderID` = 11077 AND SIN(`o`.`Discount`) > 0
""");
        }

        public override async Task Where_mathf_tan(bool async)
        {
            await base.Where_mathf_tan(async);

            AssertSql(
    """
SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE `o`.`OrderID` = 11077 AND TAN(`o`.`Discount`) > 0
""");
        }

        public override async Task Where_mathf_sign(bool async)
        {
            await base.Where_mathf_sign(async);

            AssertSql(
    """
SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE `o`.`OrderID` = 11077 AND SGN(`o`.`Discount`) > 0
""");
        }

        public override async Task Where_guid_newguid(bool isAsync)
        {
            await base.Where_guid_newguid(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE (GenGUID() <> '00000000-0000-0000-0000-000000000000') OR GenGUID() IS NULL");
        }

        public override async Task Where_string_to_upper(bool isAsync)
        {
            await base.Where_string_to_upper(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE UCASE(`c`.`CustomerID`) = 'ALFKI'");
        }

        public override async Task Where_string_to_lower(bool isAsync)
        {
            await base.Where_string_to_lower(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE LCASE(`c`.`CustomerID`) = 'alfki'");
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

        public override async Task Convert_ToBoolean(bool async)
        {
            await base.Convert_ToBoolean(async);

            AssertSql(
                """
SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`CustomerID` = 'ALFKI' AND CONVERT(bit, CONVERT(bit, `o`.`OrderID` % 3)) = CAST(1 AS bit)
""",
                //
                """
SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`CustomerID` = 'ALFKI' AND CONVERT(bit, CONVERT(tinyint, `o`.`OrderID` % 3)) = CAST(1 AS bit)
""",
                //
                """
SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`CustomerID` = 'ALFKI' AND CONVERT(bit, CONVERT(decimal(18, 2), `o`.`OrderID` % 3)) = CAST(1 AS bit)
""",
                //
                """
SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`CustomerID` = 'ALFKI' AND CONVERT(bit, CONVERT(float, `o`.`OrderID` % 3)) = CAST(1 AS bit)
""",
                //
                """
SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`CustomerID` = 'ALFKI' AND CONVERT(bit, CAST(CONVERT(float, `o`.`OrderID` % 3) AS real)) = CAST(1 AS bit)
""",
                //
                """
SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`CustomerID` = 'ALFKI' AND CONVERT(bit, CONVERT(smallint, `o`.`OrderID` % 3)) = CAST(1 AS bit)
""",
                //
                """
SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`CustomerID` = 'ALFKI' AND CONVERT(bit, CONVERT(int, `o`.`OrderID` % 3)) = CAST(1 AS bit)
""",
                //
                """
SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`CustomerID` = 'ALFKI' AND CONVERT(bit, CONVERT(bigint, `o`.`OrderID` % 3)) = CAST(1 AS bit)
""");
        }

        public override async Task Convert_ToByte(bool isAsync)
        {
            var convertMethods = new List<Expression<Func<Order, bool>>>
            {
                o => Convert.ToByte(Convert.ToByte(o.OrderID % 1)) >= 0,
                o => Convert.ToByte(Convert.ToDecimal(o.OrderID % 1)) >= 0,
                o => Convert.ToByte(Convert.ToDouble(o.OrderID % 1)) >= 0,
                o => Convert.ToByte((float)Convert.ToDouble(o.OrderID % 1)) >= 0,
                o => Convert.ToByte(Convert.ToInt16(o.OrderID % 1)) >= 0,
                o => Convert.ToByte(Convert.ToInt32(o.OrderID % 1)) >= 0,
                // o => Convert.ToByte(Convert.ToInt64(o.OrderID % 1)) >= 0,
                o => Convert.ToByte(Convert.ToString(o.OrderID % 1)) >= 0
            };

            foreach (var convertMethod in convertMethods)
            {
                await AssertQuery(
                    isAsync,
                    ss => ss.Set<Order>().Where(o => o.CustomerID == "ALFKI")
                        .Where(convertMethod),
                    entryCount: 6);
            }

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIF(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CBYTE(`o`.`OrderID` MOD 1)) IS NULL, NULL, CBYTE(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CBYTE(`o`.`OrderID` MOD 1)))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIF(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CCUR(`o`.`OrderID` MOD 1)) IS NULL, NULL, CBYTE(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CCUR(`o`.`OrderID` MOD 1)))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIF(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) IS NULL, NULL, CBYTE(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIF(IIF(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) IS NULL, NULL, CSNG(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)))) IS NULL, NULL, CBYTE(IIF(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) IS NULL, NULL, CSNG(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)))))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIF(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CINT(`o`.`OrderID` MOD 1)) IS NULL, NULL, CBYTE(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CINT(`o`.`OrderID` MOD 1)))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIF(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CLNG(`o`.`OrderID` MOD 1)) IS NULL, NULL, CBYTE(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CLNG(`o`.`OrderID` MOD 1)))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIF((`o`.`OrderID` MOD 1 & '') IS NULL, NULL, CBYTE((`o`.`OrderID` MOD 1 & ''))) >= 0)");
        }

        public override async Task Convert_ToDecimal(bool isAsync)
        {
            var convertMethods = new List<Expression<Func<Order, bool>>>
            {
                o => Convert.ToDecimal(Convert.ToByte(o.OrderID % 1)) >= 0,
                o => Convert.ToDecimal(Convert.ToDecimal(o.OrderID % 1)) >= 0,
                o => Convert.ToDecimal(Convert.ToDouble(o.OrderID % 1)) >= 0,
                o => Convert.ToDecimal((float)Convert.ToDouble(o.OrderID % 1)) >= 0,
                o => Convert.ToDecimal(Convert.ToInt16(o.OrderID % 1)) >= 0,
                o => Convert.ToDecimal(Convert.ToInt32(o.OrderID % 1)) >= 0,
                // o => Convert.ToDecimal(Convert.ToInt64(o.OrderID % 1)) >= 0,
                o => Convert.ToDecimal(Convert.ToString(o.OrderID % 1)) >= 0
            };

            foreach (var convertMethod in convertMethods)
            {
                await AssertQuery(
                    isAsync,
                    ss => ss.Set<Order>().Where(o => o.CustomerID == "ALFKI")
                        .Where(convertMethod),
                    entryCount: 6);
            }

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIF(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CBYTE(`o`.`OrderID` MOD 1)) IS NULL, NULL, CCUR(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CBYTE(`o`.`OrderID` MOD 1)))) >= 0.0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIF(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CCUR(`o`.`OrderID` MOD 1)) IS NULL, NULL, CCUR(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CCUR(`o`.`OrderID` MOD 1)))) >= 0.0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIF(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) IS NULL, NULL, CCUR(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)))) >= 0.0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIF(IIF(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) IS NULL, NULL, CSNG(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)))) IS NULL, NULL, CCUR(IIF(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) IS NULL, NULL, CSNG(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)))))) >= 0.0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIF(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CINT(`o`.`OrderID` MOD 1)) IS NULL, NULL, CCUR(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CINT(`o`.`OrderID` MOD 1)))) >= 0.0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIF(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CLNG(`o`.`OrderID` MOD 1)) IS NULL, NULL, CCUR(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CLNG(`o`.`OrderID` MOD 1)))) >= 0.0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIF((`o`.`OrderID` MOD 1 & '') IS NULL, NULL, CCUR((`o`.`OrderID` MOD 1 & ''))) >= 0.0)");
        }

        public override async Task Convert_ToDouble(bool isAsync)
        {
            var convertMethods = new List<Expression<Func<Order, bool>>>
            {
                o => Convert.ToDouble(Convert.ToByte(o.OrderID % 1)) >= 0,
                o => Convert.ToDouble(Convert.ToDecimal(o.OrderID % 1)) >= 0,
                o => Convert.ToDouble(Convert.ToDouble(o.OrderID % 1)) >= 0,
                o => Convert.ToDouble((float)Convert.ToDouble(o.OrderID % 1)) >= 0,
                o => Convert.ToDouble(Convert.ToInt16(o.OrderID % 1)) >= 0,
                o => Convert.ToDouble(Convert.ToInt32(o.OrderID % 1)) >= 0,
                // o => Convert.ToDouble(Convert.ToInt64(o.OrderID % 1)) >= 0,
                o => Convert.ToDouble(Convert.ToString(o.OrderID % 1)) >= 0
            };

            foreach (var convertMethod in convertMethods)
            {
                await AssertQuery(
                    isAsync,
                    ss => ss.Set<Order>().Where(o => o.CustomerID == "ALFKI")
                        .Where(convertMethod),
                    entryCount: 6);
            }

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIF(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CBYTE(`o`.`OrderID` MOD 1)) IS NULL, NULL, CDBL(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CBYTE(`o`.`OrderID` MOD 1)))) >= 0.0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIF(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CCUR(`o`.`OrderID` MOD 1)) IS NULL, NULL, CDBL(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CCUR(`o`.`OrderID` MOD 1)))) >= 0.0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIF(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) IS NULL, NULL, CDBL(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)))) >= 0.0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIF(IIF(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) IS NULL, NULL, CSNG(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)))) IS NULL, NULL, CDBL(IIF(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) IS NULL, NULL, CSNG(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)))))) >= 0.0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIF(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CINT(`o`.`OrderID` MOD 1)) IS NULL, NULL, CDBL(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CINT(`o`.`OrderID` MOD 1)))) >= 0.0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIF(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CLNG(`o`.`OrderID` MOD 1)) IS NULL, NULL, CDBL(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CLNG(`o`.`OrderID` MOD 1)))) >= 0.0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIF((`o`.`OrderID` MOD 1 & '') IS NULL, NULL, CDBL((`o`.`OrderID` MOD 1 & ''))) >= 0.0)");
        }

        public override async Task Convert_ToInt16(bool isAsync)
        {
            var convertMethods = new List<Expression<Func<Order, bool>>>
            {
                o => Convert.ToInt16(Convert.ToByte(o.OrderID % 1)) >= 0,
                o => Convert.ToInt16(Convert.ToDecimal(o.OrderID % 1)) >= 0,
                o => Convert.ToInt16(Convert.ToDouble(o.OrderID % 1)) >= 0,
                o => Convert.ToInt16((float)Convert.ToDouble(o.OrderID % 1)) >= 0,
                o => Convert.ToInt16(Convert.ToInt16(o.OrderID % 1)) >= 0,
                o => Convert.ToInt16(Convert.ToInt32(o.OrderID % 1)) >= 0,
                // o => Convert.ToInt16(Convert.ToInt64(o.OrderID % 1)) >= 0,
                o => Convert.ToInt16(Convert.ToString(o.OrderID % 1)) >= 0
            };

            foreach (var convertMethod in convertMethods)
            {
                await AssertQuery(
                    isAsync,
                    ss => ss.Set<Order>().Where(o => o.CustomerID == "ALFKI")
                        .Where(convertMethod),
                    entryCount: 6);
            }

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIF(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CBYTE(`o`.`OrderID` MOD 1)) IS NULL, NULL, CINT(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CBYTE(`o`.`OrderID` MOD 1)))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIF(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CCUR(`o`.`OrderID` MOD 1)) IS NULL, NULL, CINT(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CCUR(`o`.`OrderID` MOD 1)))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIF(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) IS NULL, NULL, CINT(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIF(IIF(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) IS NULL, NULL, CSNG(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)))) IS NULL, NULL, CINT(IIF(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) IS NULL, NULL, CSNG(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)))))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIF(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CINT(`o`.`OrderID` MOD 1)) IS NULL, NULL, CINT(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CINT(`o`.`OrderID` MOD 1)))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIF(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CLNG(`o`.`OrderID` MOD 1)) IS NULL, NULL, CINT(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CLNG(`o`.`OrderID` MOD 1)))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIF((`o`.`OrderID` MOD 1 & '') IS NULL, NULL, CINT((`o`.`OrderID` MOD 1 & ''))) >= 0)");
        }

        public override async Task Convert_ToInt32(bool isAsync)
        {
            var convertMethods = new List<Expression<Func<Order, bool>>>
            {
                o => Convert.ToInt32(Convert.ToByte(o.OrderID % 1)) >= 0,
                o => Convert.ToInt32(Convert.ToDecimal(o.OrderID % 1)) >= 0,
                o => Convert.ToInt32(Convert.ToDouble(o.OrderID % 1)) >= 0,
                o => Convert.ToInt32((float)Convert.ToDouble(o.OrderID % 1)) >= 0,
                o => Convert.ToInt32(Convert.ToInt16(o.OrderID % 1)) >= 0,
                o => Convert.ToInt32(Convert.ToInt32(o.OrderID % 1)) >= 0,
                // o => Convert.ToInt32(Convert.ToInt64(o.OrderID % 1)) >= 0,
                o => Convert.ToInt32(Convert.ToString(o.OrderID % 1)) >= 0
            };

            foreach (var convertMethod in convertMethods)
            {
                await AssertQuery(
                    isAsync,
                    ss => ss.Set<Order>().Where(o => o.CustomerID == "ALFKI")
                        .Where(convertMethod),
                    entryCount: 6);
            }

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIF(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CBYTE(`o`.`OrderID` MOD 1)) IS NULL, NULL, CLNG(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CBYTE(`o`.`OrderID` MOD 1)))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIF(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CCUR(`o`.`OrderID` MOD 1)) IS NULL, NULL, CLNG(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CCUR(`o`.`OrderID` MOD 1)))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIF(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) IS NULL, NULL, CLNG(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIF(IIF(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) IS NULL, NULL, CSNG(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)))) IS NULL, NULL, CLNG(IIF(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) IS NULL, NULL, CSNG(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)))))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIF(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CINT(`o`.`OrderID` MOD 1)) IS NULL, NULL, CLNG(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CINT(`o`.`OrderID` MOD 1)))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIF(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CLNG(`o`.`OrderID` MOD 1)) IS NULL, NULL, CLNG(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CLNG(`o`.`OrderID` MOD 1)))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIF((`o`.`OrderID` MOD 1 & '') IS NULL, NULL, CLNG((`o`.`OrderID` MOD 1 & ''))) >= 0)");
        }

        [ConditionalTheory(Skip = "Int64 support has not been implemented yet.")]
        public override async Task Convert_ToInt64(bool isAsync)
        {
            await base.Convert_ToInt64(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (CONVERT(bigint, IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CBYTE(`o`.`OrderID` MOD 1))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (CONVERT(bigint, IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CCUR(`o`.`OrderID` MOD 1))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (CONVERT(bigint, IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (CONVERT(bigint, IIF(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) IS NULL, NULL, CSNG(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1))))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (CONVERT(bigint, IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CINT(`o`.`OrderID` MOD 1))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (CONVERT(bigint, IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CLNG(`o`.`OrderID` MOD 1))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (CONVERT(bigint, CONVERT(bigint, `o`.`OrderID` MOD 1)) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (CONVERT(bigint, CONVERT(nvarchar(max), `o`.`OrderID` MOD 1)) >= 0)");
        }

        public override async Task Convert_ToString(bool isAsync)
        {
            var convertMethods = new List<Expression<Func<Order, bool>>>
            {
                o => Convert.ToString(Convert.ToByte(o.OrderID % 1)) != "10",
                o => Convert.ToString(Convert.ToDecimal(o.OrderID % 1)) != "10",
                o => Convert.ToString(Convert.ToDouble(o.OrderID % 1)) != "10",
                o => Convert.ToString((float)Convert.ToDouble(o.OrderID % 1)) != "10",
                o => Convert.ToString(Convert.ToInt16(o.OrderID % 1)) != "10",
                o => Convert.ToString(Convert.ToInt32(o.OrderID % 1)) != "10",
                // o => Convert.ToString(Convert.ToInt64(o.OrderID % 1)) != "10",
                o => Convert.ToString(Convert.ToString(o.OrderID % 1)) != "10",
                o => Convert.ToString(o.OrderDate.Value).Contains("1997") || Convert.ToString(o.OrderDate.Value).Contains("1998")
            };

            foreach (var convertMethod in convertMethods)
            {
                await AssertQuery(
                    isAsync,
                    ss => ss.Set<Order>().Where(o => o.CustomerID == "ALFKI")
                        .Where(convertMethod),
                    entryCount: 6);
            }

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (((IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CBYTE(`o`.`OrderID` MOD 1)) & '') <> '10') OR (IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CBYTE(`o`.`OrderID` MOD 1)) & '') IS NULL)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (((IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CCUR(`o`.`OrderID` MOD 1)) & '') <> '10') OR (IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CCUR(`o`.`OrderID` MOD 1)) & '') IS NULL)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (((IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) & '') <> '10') OR (IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) & '') IS NULL)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND ((CONVERT(nvarchar(max), IIF(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) IS NULL, NULL, CSNG(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1))))) <> '10') OR CONVERT(nvarchar(max), IIF(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) IS NULL, NULL, CSNG(IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1))))) IS NULL)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (((IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CINT(`o`.`OrderID` MOD 1)) & '') <> '10') OR (IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CINT(`o`.`OrderID` MOD 1)) & '') IS NULL)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (((IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CLNG(`o`.`OrderID` MOD 1)) & '') <> '10') OR (IIF(`o`.`OrderID` MOD 1 IS NULL, NULL, CLNG(`o`.`OrderID` MOD 1)) & '') IS NULL)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (((CONVERT(bigint, `o`.`OrderID` MOD 1) & '') <> '10') OR (CONVERT(bigint, `o`.`OrderID` MOD 1) & '') IS NULL)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (((CONVERT(nvarchar(max), `o`.`OrderID` MOD 1) & '') <> '10') OR (CONVERT(nvarchar(max), `o`.`OrderID` MOD 1) & '') IS NULL)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND ((CHARINDEX('1997', CONVERT(nvarchar(max), `o`.`OrderDate`)) > 0) OR (CHARINDEX('1998', CONVERT(nvarchar(max), `o`.`OrderDate`)) > 0))");
        }

        public override async Task Indexof_with_emptystring(bool async)
        {
            await base.Indexof_with_emptystring(async);

            AssertSql(
    """
SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
""");
        }

        public override async Task Indexof_with_one_constant_arg(bool async)
        {
            await base.Indexof_with_one_constant_arg(async);

            AssertSql(
    """
SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE (InStr(`c`.`ContactName`, 'a') - 1) = 1
""");
        }

        public override async Task Indexof_with_one_parameter_arg(bool async)
        {
            await base.Indexof_with_one_parameter_arg(async);

            AssertSql(
    """
@__pattern_0='a' (Size = 255)
@__pattern_0='a' (Size = 255)

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE IIF(? = '', 0, InStr(`c`.`ContactName`, ?) - 1) = 1
""");
        }

        public override async Task Indexof_with_constant_starting_position(bool async)
        {
            await base.Indexof_with_constant_starting_position(async);

            AssertSql(
    """
SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE (InStr(3, `c`.`ContactName`, 'a', 1) - 1) = 4
""");
        }

        public override async Task Indexof_with_parameter_starting_position(bool async)
        {
            await base.Indexof_with_parameter_starting_position(async);

            AssertSql(
    $"""
@__start_0='2'

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE (InStr({AssertSqlHelper.Parameter("@__start_0")} + 1, `c`.`ContactName`, 'a', 1) - 1) = 4
""");
        }

        public override async Task Replace_with_emptystring(bool isAsync)
        {
            await base.Replace_with_emptystring(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE REPLACE(`c`.`ContactName`, 'ia', '') = 'Mar Anders'");
        }

        public override async Task Replace_using_property_arguments(bool async)
        {
            await base.Replace_using_property_arguments(async);

            AssertSql(
                @"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE REPLACE(`c`.`ContactName`, `c`.`ContactName`, `c`.`CustomerID`) = `c`.`CustomerID`");
        }

        public override async Task Substring_with_one_arg_with_zero_startindex(bool async)
        {
            await base.Substring_with_one_arg_with_zero_startindex(async);

            AssertSql(
                @"SELECT `c`.`ContactName`
FROM `Customers` AS `c`
WHERE MID(`c`.`CustomerID`, 0 + 1, LEN(`c`.`CustomerID`)) = 'ALFKI'");
        }

        public override async Task Substring_with_one_arg_with_constant(bool async)
        {
            await base.Substring_with_one_arg_with_constant(async);

            AssertSql(
                @"SELECT `c`.`ContactName`
FROM `Customers` AS `c`
WHERE MID(`c`.`CustomerID`, 1 + 1, LEN(`c`.`CustomerID`)) = 'LFKI'");
        }

        public override async Task Substring_with_one_arg_with_closure(bool async)
        {
            await base.Substring_with_one_arg_with_closure(async);

            AssertSql(
                $@"@__start_0='2'

SELECT `c`.`ContactName`
FROM `Customers` AS `c`
WHERE MID(`c`.`CustomerID`, {AssertSqlHelper.Parameter("@__start_0")} + 1, LEN(`c`.`CustomerID`)) = 'FKI'");
        }

        public override async Task Substring_with_two_args_with_zero_startindex(bool async)
        {
            await base.Substring_with_two_args_with_zero_startindex(async);

            AssertSql(
                @"SELECT MID(`c`.`ContactName`, 0 + 1, 3)
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` = 'ALFKI'");
        }

        public override async Task Substring_with_two_args_with_zero_length(bool async)
        {
            await base.Substring_with_two_args_with_zero_length(async);

            AssertSql(
                @"SELECT MID(`c`.`ContactName`, 2 + 1, 0)
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` = 'ALFKI'");
        }

        public override async Task Substring_with_two_args_with_constant(bool async)
        {
            await base.Substring_with_two_args_with_constant(async);

            AssertSql(
                @"SELECT MID(`c`.`ContactName`, 1 + 1, 3)
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` = 'ALFKI'");
        }

        public override async Task Substring_with_two_args_with_closure(bool async)
        {
            await base.Substring_with_two_args_with_closure(async);

            AssertSql(
                $@"@__start_0='2'

SELECT MID(`c`.`ContactName`, {AssertSqlHelper.Parameter("@__start_0")} + 1, 3)
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` = 'ALFKI'");
        }

        public override async Task Substring_with_two_args_with_Index_of(bool async)
        {
            await base.Substring_with_two_args_with_Index_of(async);

            AssertSql(
                @"SELECT SUBSTRING(`c`.`ContactName`, CASE
    WHEN 'a' = '' THEN 0
    ELSE CAST(CHARINDEX('a', `c`.`ContactName`) AS int) - 1
END + 1, 3)
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` = 'ALFKI'");
        }

        public override async Task IsNullOrEmpty_in_predicate(bool isAsync)
        {
            await base.IsNullOrEmpty_in_predicate(isAsync);

            AssertSql(
"""
SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`Region` IS NULL OR (`c`.`Region` LIKE '')
""");
        }

        public override async Task IsNullOrEmpty_in_projection(bool async)
        {
            await base.IsNullOrEmpty_in_projection(async);

            AssertSql(
"""
SELECT `c`.`CustomerID` AS `Id`, IIF(`c`.`Region` IS NULL OR (`c`.`Region` LIKE ''), TRUE, FALSE) AS `Value`
FROM `Customers` AS `c`
""");
        }

        public override async Task IsNullOrEmpty_negated_in_predicate(bool async)
        {
            await base.IsNullOrEmpty_negated_in_predicate(async);

            AssertSql(
"""
SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`Region` IS NOT NULL AND `c`.`Region` NOT LIKE ''
""");
        }

        public override async Task IsNullOrEmpty_negated_in_projection(bool async)
        {
            await base.IsNullOrEmpty_negated_in_projection(async);

            AssertSql(
"""
SELECT `c`.`CustomerID` AS `Id`, IIF(`c`.`Region` IS NOT NULL AND `c`.`Region` NOT LIKE '', TRUE, FALSE) AS `Value`
FROM `Customers` AS `c`
""");
        }

        public override async Task IsNullOrWhiteSpace_in_predicate(bool isAsync)
        {
            await base.IsNullOrWhiteSpace_in_predicate(isAsync);

            AssertSql(
"""
SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`Region` IS NULL OR `c`.`Region` = ''
""");
        }

        public override async Task IsNullOrWhiteSpace_in_predicate_on_non_nullable_column(bool isAsync)
        {
            await base.IsNullOrWhiteSpace_in_predicate_on_non_nullable_column(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` = ''");
        }

        public override async Task TrimStart_without_arguments_in_predicate(bool isAsync)
        {
            await base.TrimStart_without_arguments_in_predicate(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE LTRIM(`c`.`ContactTitle`) = 'Owner'");
        }

        [ConditionalTheory(Skip = "Issue#17328")]
        public override Task TrimStart_with_char_argument_in_predicate(bool isAsync)
            => base.TrimStart_with_char_argument_in_predicate(isAsync);

        [ConditionalTheory(Skip = "Issue#17328")]
        public override Task TrimStart_with_char_array_argument_in_predicate(bool isAsync)
            => base.TrimStart_with_char_array_argument_in_predicate(isAsync);

        public override async Task TrimEnd_without_arguments_in_predicate(bool isAsync)
        {
            await base.TrimEnd_without_arguments_in_predicate(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE RTRIM(`c`.`ContactTitle`) = 'Owner'");
        }

        [ConditionalTheory(Skip = "Issue#17328")]
        public override Task TrimEnd_with_char_argument_in_predicate(bool isAsync)
            => base.TrimEnd_with_char_argument_in_predicate(isAsync);

        [ConditionalTheory(Skip = "Issue#17328")]
        public override Task TrimEnd_with_char_array_argument_in_predicate(bool isAsync)
            => base.TrimEnd_with_char_array_argument_in_predicate(isAsync);

        public override async Task Trim_without_argument_in_predicate(bool isAsync)
        {
            await base.Trim_without_argument_in_predicate(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE TRIM(`c`.`ContactTitle`) = 'Owner'");
        }

        [ConditionalTheory(Skip = "Issue#17328")]
        public override Task Trim_with_char_argument_in_predicate(bool isAsync)
            => base.Trim_with_char_argument_in_predicate(isAsync);

        [ConditionalTheory(Skip = "Issue#17328")]
        public override Task Trim_with_char_array_argument_in_predicate(bool isAsync)
            => base.Trim_with_char_array_argument_in_predicate(isAsync);

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

        public override async Task Static_string_equals_in_predicate(bool isAsync)
        {
            await base.Static_string_equals_in_predicate(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` = 'ANATR'");
        }

        public override async Task Static_equals_nullable_datetime_compared_to_non_nullable(bool isAsync)
        {
            await base.Static_equals_nullable_datetime_compared_to_non_nullable(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__arg_0='1996-07-04T00:00:00.0000000' (DbType = DateTime)")}

SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`OrderDate` = CDATE({AssertSqlHelper.Parameter("@__arg_0")})");
        }

        public override async Task Static_equals_int_compared_to_long(bool isAsync)
        {
            await base.Static_equals_int_compared_to_long(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE 0 = 1");
        }

        public override async Task Projecting_Math_Truncate_and_ordering_by_it_twice(bool isAsync)
        {
            await base.Projecting_Math_Truncate_and_ordering_by_it_twice(isAsync);

            // issue #16038
            //            AssertSql(
            //                $@"SELECT ROUND(IIF(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`)), 0, 1) AS `A`
            //FROM `Orders` AS `o`
            //WHERE `o`.`OrderID` < 10250
            //ORDER BY `A`");
        }

        public override async Task Projecting_Math_Truncate_and_ordering_by_it_twice2(bool isAsync)
        {
            await base.Projecting_Math_Truncate_and_ordering_by_it_twice2(isAsync);

            // issue #16038
            //            AssertSql(
            //                $@"SELECT ROUND(IIF(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`)), 0, 1) AS `A`
            //FROM `Orders` AS `o`
            //WHERE `o`.`OrderID` < 10250
            //ORDER BY `A` DESC");
        }

        public override async Task Projecting_Math_Truncate_and_ordering_by_it_twice3(bool isAsync)
        {
            await base.Projecting_Math_Truncate_and_ordering_by_it_twice3(isAsync);

            // issue #16038
            //            AssertSql(
            //                $@"SELECT ROUND(IIF(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`)), 0, 1) AS `A`
            //FROM `Orders` AS `o`
            //WHERE `o`.`OrderID` < 10250
            //ORDER BY `A` DESC");
        }

        public override Task Regex_IsMatch_MethodCall(bool async)
            => AssertTranslationFailed(() => base.Regex_IsMatch_MethodCall(async));

        public override Task Regex_IsMatch_MethodCall_constant_input(bool async)
            => AssertTranslationFailed(() => base.Regex_IsMatch_MethodCall_constant_input(async));

        public override Task Datetime_subtraction_TotalDays(bool async)
            => AssertTranslationFailed(() => base.Datetime_subtraction_TotalDays(async));

        /*[ConditionalTheory`
        [MemberData(nameof(IsAsyncData))`
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
                : query.ToList();

            var product9 = results.Single(r => r.ProductID == 9);
            Assert.Equal(8.675943752699023, product9.SampleStandardDeviation.Value, 5);
            Assert.Equal(7.759999999999856, product9.PopulationStandardDeviation.Value, 5);

            AssertSql(
    """
SELECT [o`.[ProductID`, STDEV([o`.[UnitPrice`) AS [SampleStandardDeviation`, STDEVP([o`.[UnitPrice`) AS [PopulationStandardDeviation`
FROM [Order Details` AS [o`
GROUP BY [o`.[ProductID`
""");
        }

        [ConditionalTheory`
        [MemberData(nameof(IsAsyncData))`
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
                : query.ToList();

            var product9 = results.Single(r => r.ProductID == 9);
            Assert.Equal(75.2719999999972, product9.SampleStandardDeviation.Value, 5);
            Assert.Equal(60.217599999997766, product9.PopulationStandardDeviation.Value, 5);

            AssertSql(
    """
SELECT [o`.[ProductID`, VAR([o`.[UnitPrice`) AS [SampleStandardDeviation`, VARP([o`.[UnitPrice`) AS [PopulationStandardDeviation`
FROM [Order Details` AS [o`
GROUP BY [o`.[ProductID`
""");
        }*/

        private void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);

        protected override void ClearLog()
            => Fixture.TestSqlLoggerFactory.Clear();
    }
}
