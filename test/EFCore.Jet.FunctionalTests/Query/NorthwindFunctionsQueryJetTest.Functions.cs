// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
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

        public override async Task String_StartsWith_Literal(bool isAsync)
        {
            await base.String_StartsWith_Literal(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`ContactName` IS NOT NULL AND (`c`.`ContactName` LIKE 'M' & '%')");
        }

        public override async Task String_StartsWith_Identity(bool isAsync)
        {
            await base.String_StartsWith_Identity(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE (`c`.`ContactName` = '') OR (`c`.`ContactName` IS NOT NULL AND (`c`.`ContactName` IS NOT NULL AND (LEFT(`c`.`ContactName`, LEN(`c`.`ContactName`)) = `c`.`ContactName`)))");
        }

        public override async Task String_StartsWith_Column(bool isAsync)
        {
            await base.String_StartsWith_Column(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE (`c`.`ContactName` = '') OR (`c`.`ContactName` IS NOT NULL AND (`c`.`ContactName` IS NOT NULL AND (LEFT(`c`.`ContactName`, LEN(`c`.`ContactName`)) = `c`.`ContactName`)))");
        }

        public override async Task String_StartsWith_MethodCall(bool isAsync)
        {
            await base.String_StartsWith_MethodCall(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`ContactName` IS NOT NULL AND (`c`.`ContactName` LIKE 'M' & '%')");
        }

        public override async Task String_EndsWith_Literal(bool isAsync)
        {
            await base.String_EndsWith_Literal(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`ContactName` IS NOT NULL AND (`c`.`ContactName` LIKE '%b')");
        }

        public override async Task String_EndsWith_Identity(bool isAsync)
        {
            await base.String_EndsWith_Identity(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE (`c`.`ContactName` = '') OR (`c`.`ContactName` IS NOT NULL AND (`c`.`ContactName` IS NOT NULL AND (RIGHT(`c`.`ContactName`, LEN(`c`.`ContactName`)) = `c`.`ContactName`)))");
        }

        public override async Task String_EndsWith_Column(bool isAsync)
        {
            await base.String_EndsWith_Column(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE (`c`.`ContactName` = '') OR (`c`.`ContactName` IS NOT NULL AND (`c`.`ContactName` IS NOT NULL AND (RIGHT(`c`.`ContactName`, LEN(`c`.`ContactName`)) = `c`.`ContactName`)))");
        }

        public override async Task String_EndsWith_MethodCall(bool isAsync)
        {
            await base.String_EndsWith_MethodCall(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`ContactName` IS NOT NULL AND (`c`.`ContactName` LIKE '%m')");
        }

        public override async Task String_Contains_Literal(bool isAsync)
        {
            await AssertQuery(
                isAsync,
                ss => ss.Set<Customer>().Where(c => c.ContactName.Contains("M")), // case-insensitive
                ss => ss.Set<Customer>().Where(c => c.ContactName.Contains("M") || c.ContactName.Contains("m")), // case-sensitive
                entryCount: 34);

            // issue #15994
//            AssertSql(
//                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
//FROM `Customers` AS `c`
//WHERE CHARINDEX('M', `c`.`ContactName`) > 0");
        }

        public override async Task String_Contains_Identity(bool isAsync)
        {
            await base.String_Contains_Identity(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE (`c`.`ContactName` = '') OR (CHARINDEX(`c`.`ContactName`, `c`.`ContactName`) > 0)");
        }

        public override async Task String_Contains_Column(bool isAsync)
        {
            await base.String_Contains_Column(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE (`c`.`ContactName` = '') OR (CHARINDEX(`c`.`ContactName`, `c`.`ContactName`) > 0)");
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
WHERE CHARINDEX('M', `c`.`ContactName`) > 0");
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
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE ABS(`o`.`ProductID`) > 10");
        }

        public override async Task Where_math_abs2(bool isAsync)
        {
            await base.Where_math_abs2(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE ABS(`o`.`Quantity`) > 10");
        }

        public override async Task Where_math_abs3(bool isAsync)
        {
            await base.Where_math_abs3(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE ABS(`o`.`UnitPrice`) > 10.0");
        }

        public override async Task Where_math_abs_uncorrelated(bool isAsync)
        {
            await base.Where_math_abs_uncorrelated(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE 10 < `o`.`ProductID`");
        }

        public override async Task Where_math_ceiling1(bool isAsync)
        {
            await base.Where_math_ceiling1(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE CEILING(IIf(`o`.`Discount` IS NULL, NULL, CDBL(`o`.`Discount`))) > 0.0E0");
        }

        public override async Task Where_math_ceiling2(bool isAsync)
        {
            await base.Where_math_ceiling2(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE CEILING(`o`.`UnitPrice`) > 10.0");
        }

        public override async Task Where_math_floor(bool isAsync)
        {
            await base.Where_math_floor(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE FLOOR(`o`.`UnitPrice`) > 10.0");
        }

        public override async Task Where_math_power(bool isAsync)
        {
            await base.Where_math_power(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE POWER(IIf(`o`.`Discount` IS NULL, NULL, CDBL(`o`.`Discount`)), 2.0E0) > 0.05000000074505806E0");
        }

        public override async Task Where_math_round(bool isAsync)
        {
            await base.Where_math_round(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE ROUND(`o`.`UnitPrice`, 0) > 10.0");
        }

        public override async Task Select_math_round_int(bool isAsync)
        {
            await base.Select_math_round_int(isAsync);

            AssertSql(
                $@"SELECT ROUND(IIf(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`)), 0) AS `A`
FROM `Orders` AS `o`
WHERE `o`.`OrderID` < 10250");
        }

        public override async Task Select_math_truncate_int(bool isAsync)
        {
            await base.Select_math_truncate_int(isAsync);

            AssertSql(
                $@"SELECT ROUND(IIf(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`)), 0, 1) AS `A`
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
WHERE ROUND(`o`.`UnitPrice`, 0, 1) > 10.0");
        }

        public override async Task Where_math_exp(bool isAsync)
        {
            await base.Where_math_exp(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE (`o`.`OrderID` = 11077) AND (EXP(IIf(`o`.`Discount` IS NULL, NULL, CDBL(`o`.`Discount`))) > 1.0E0)");
        }

        public override async Task Where_math_log10(bool isAsync)
        {
            await base.Where_math_log10(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE ((`o`.`OrderID` = 11077) AND (`o`.`Discount` > IIf(0 IS NULL, NULL, CSNG(0)))) AND (LOG10(IIf(`o`.`Discount` IS NULL, NULL, CDBL(`o`.`Discount`))) < 0.0E0)");
        }

        public override async Task Where_math_log(bool isAsync)
        {
            await base.Where_math_log(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE ((`o`.`OrderID` = 11077) AND (`o`.`Discount` > IIf(0 IS NULL, NULL, CSNG(0)))) AND (LOG(IIf(`o`.`Discount` IS NULL, NULL, CDBL(`o`.`Discount`))) < 0.0E0)");
        }

        public override async Task Where_math_log_new_base(bool isAsync)
        {
            await base.Where_math_log_new_base(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE ((`o`.`OrderID` = 11077) AND (`o`.`Discount` > IIf(0 IS NULL, NULL, CSNG(0)))) AND (LOG(IIf(`o`.`Discount` IS NULL, NULL, CDBL(`o`.`Discount`)), 7.0E0) < 0.0E0)");
        }

        public override async Task Where_math_sqrt(bool isAsync)
        {
            await base.Where_math_sqrt(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE (`o`.`OrderID` = 11077) AND (SQRT(IIf(`o`.`Discount` IS NULL, NULL, CDBL(`o`.`Discount`))) > 0.0E0)");
        }

        public override async Task Where_math_acos(bool isAsync)
        {
            await base.Where_math_acos(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE (`o`.`OrderID` = 11077) AND (ACOS(IIf(`o`.`Discount` IS NULL, NULL, CDBL(`o`.`Discount`))) > 1.0E0)");
        }

        public override async Task Where_math_asin(bool isAsync)
        {
            await base.Where_math_asin(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE (`o`.`OrderID` = 11077) AND (ASIN(IIf(`o`.`Discount` IS NULL, NULL, CDBL(`o`.`Discount`))) > 0.0E0)");
        }

        public override async Task Where_math_atan(bool isAsync)
        {
            await base.Where_math_atan(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE (`o`.`OrderID` = 11077) AND (ATAN(IIf(`o`.`Discount` IS NULL, NULL, CDBL(`o`.`Discount`))) > 0.0E0)");
        }

        public override async Task Where_math_atan2(bool isAsync)
        {
            await base.Where_math_atan2(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE (`o`.`OrderID` = 11077) AND (ATN2(IIf(`o`.`Discount` IS NULL, NULL, CDBL(`o`.`Discount`)), 1.0E0) > 0.0E0)");
        }

        public override async Task Where_math_cos(bool isAsync)
        {
            await base.Where_math_cos(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE (`o`.`OrderID` = 11077) AND (COS(IIf(`o`.`Discount` IS NULL, NULL, CDBL(`o`.`Discount`))) > 0.0E0)");
        }

        public override async Task Where_math_sin(bool isAsync)
        {
            await base.Where_math_sin(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE (`o`.`OrderID` = 11077) AND (SIN(IIf(`o`.`Discount` IS NULL, NULL, CDBL(`o`.`Discount`))) > 0.0E0)");
        }

        public override async Task Where_math_tan(bool isAsync)
        {
            await base.Where_math_tan(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE (`o`.`OrderID` = 11077) AND (TAN(IIf(`o`.`Discount` IS NULL, NULL, CDBL(`o`.`Discount`))) > 0.0E0)");
        }

        public override async Task Where_math_sign(bool isAsync)
        {
            await base.Where_math_sign(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE (`o`.`OrderID` = 11077) AND (SIGN(`o`.`Discount`) > 0)");
        }

        [ConditionalTheory(Skip = "Issue#17328")]
        public override Task Where_math_min(bool isAsync) => base.Where_math_min(isAsync);

        [ConditionalTheory(Skip = "Issue#17328")]
        public override Task Where_math_max(bool isAsync) => base.Where_math_max(isAsync);

        public override async Task Where_guid_newguid(bool isAsync)
        {
            await base.Where_guid_newguid(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE (NEWID() <> '00000000-0000-0000-0000-000000000000') OR NEWID() IS NULL");
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
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE POWER(IIf(CAST(LEN(`c`.`CustomerID`) AS int) IS NULL, NULL, CDBL(CAST(LEN(`c`.`CustomerID`) AS int))), 2.0E0) = 25.0E0");
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
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIf(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CBYTE(`o`.`OrderID` MOD 1)) IS NULL, NULL, CBYTE(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CBYTE(`o`.`OrderID` MOD 1)))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIf(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CCUR(`o`.`OrderID` MOD 1)) IS NULL, NULL, CBYTE(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CCUR(`o`.`OrderID` MOD 1)))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIf(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) IS NULL, NULL, CBYTE(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIf(IIf(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) IS NULL, NULL, CSNG(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)))) IS NULL, NULL, CBYTE(IIf(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) IS NULL, NULL, CSNG(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)))))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIf(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CINT(`o`.`OrderID` MOD 1)) IS NULL, NULL, CBYTE(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CINT(`o`.`OrderID` MOD 1)))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIf(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CLNG(`o`.`OrderID` MOD 1)) IS NULL, NULL, CBYTE(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CLNG(`o`.`OrderID` MOD 1)))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIf((`o`.`OrderID` MOD 1 & '') IS NULL, NULL, CBYTE((`o`.`OrderID` MOD 1 & ''))) >= 0)");
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
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIf(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CBYTE(`o`.`OrderID` MOD 1)) IS NULL, NULL, CCUR(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CBYTE(`o`.`OrderID` MOD 1)))) >= 0.0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIf(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CCUR(`o`.`OrderID` MOD 1)) IS NULL, NULL, CCUR(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CCUR(`o`.`OrderID` MOD 1)))) >= 0.0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIf(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) IS NULL, NULL, CCUR(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)))) >= 0.0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIf(IIf(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) IS NULL, NULL, CSNG(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)))) IS NULL, NULL, CCUR(IIf(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) IS NULL, NULL, CSNG(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)))))) >= 0.0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIf(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CINT(`o`.`OrderID` MOD 1)) IS NULL, NULL, CCUR(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CINT(`o`.`OrderID` MOD 1)))) >= 0.0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIf(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CLNG(`o`.`OrderID` MOD 1)) IS NULL, NULL, CCUR(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CLNG(`o`.`OrderID` MOD 1)))) >= 0.0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIf((`o`.`OrderID` MOD 1 & '') IS NULL, NULL, CCUR((`o`.`OrderID` MOD 1 & ''))) >= 0.0)");
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
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIf(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CBYTE(`o`.`OrderID` MOD 1)) IS NULL, NULL, CDBL(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CBYTE(`o`.`OrderID` MOD 1)))) >= 0.0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIf(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CCUR(`o`.`OrderID` MOD 1)) IS NULL, NULL, CDBL(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CCUR(`o`.`OrderID` MOD 1)))) >= 0.0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIf(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) IS NULL, NULL, CDBL(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)))) >= 0.0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIf(IIf(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) IS NULL, NULL, CSNG(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)))) IS NULL, NULL, CDBL(IIf(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) IS NULL, NULL, CSNG(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)))))) >= 0.0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIf(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CINT(`o`.`OrderID` MOD 1)) IS NULL, NULL, CDBL(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CINT(`o`.`OrderID` MOD 1)))) >= 0.0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIf(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CLNG(`o`.`OrderID` MOD 1)) IS NULL, NULL, CDBL(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CLNG(`o`.`OrderID` MOD 1)))) >= 0.0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIf((`o`.`OrderID` MOD 1 & '') IS NULL, NULL, CDBL((`o`.`OrderID` MOD 1 & ''))) >= 0.0)");
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
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIf(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CBYTE(`o`.`OrderID` MOD 1)) IS NULL, NULL, CINT(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CBYTE(`o`.`OrderID` MOD 1)))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIf(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CCUR(`o`.`OrderID` MOD 1)) IS NULL, NULL, CINT(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CCUR(`o`.`OrderID` MOD 1)))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIf(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) IS NULL, NULL, CINT(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIf(IIf(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) IS NULL, NULL, CSNG(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)))) IS NULL, NULL, CINT(IIf(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) IS NULL, NULL, CSNG(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)))))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIf(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CINT(`o`.`OrderID` MOD 1)) IS NULL, NULL, CINT(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CINT(`o`.`OrderID` MOD 1)))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIf(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CLNG(`o`.`OrderID` MOD 1)) IS NULL, NULL, CINT(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CLNG(`o`.`OrderID` MOD 1)))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIf((`o`.`OrderID` MOD 1 & '') IS NULL, NULL, CINT((`o`.`OrderID` MOD 1 & ''))) >= 0)");
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
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIf(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CBYTE(`o`.`OrderID` MOD 1)) IS NULL, NULL, CLNG(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CBYTE(`o`.`OrderID` MOD 1)))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIf(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CCUR(`o`.`OrderID` MOD 1)) IS NULL, NULL, CLNG(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CCUR(`o`.`OrderID` MOD 1)))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIf(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) IS NULL, NULL, CLNG(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIf(IIf(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) IS NULL, NULL, CSNG(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)))) IS NULL, NULL, CLNG(IIf(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) IS NULL, NULL, CSNG(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)))))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIf(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CINT(`o`.`OrderID` MOD 1)) IS NULL, NULL, CLNG(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CINT(`o`.`OrderID` MOD 1)))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIf(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CLNG(`o`.`OrderID` MOD 1)) IS NULL, NULL, CLNG(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CLNG(`o`.`OrderID` MOD 1)))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (IIf((`o`.`OrderID` MOD 1 & '') IS NULL, NULL, CLNG((`o`.`OrderID` MOD 1 & ''))) >= 0)");        }

        [ConditionalTheory(Skip = "Int64 support has not been implemented yet.")]
        public override async Task Convert_ToInt64(bool isAsync)
        {
            await base.Convert_ToInt64(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (CONVERT(bigint, IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CBYTE(`o`.`OrderID` MOD 1))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (CONVERT(bigint, IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CCUR(`o`.`OrderID` MOD 1))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (CONVERT(bigint, IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (CONVERT(bigint, IIf(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) IS NULL, NULL, CSNG(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1))))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (CONVERT(bigint, IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CINT(`o`.`OrderID` MOD 1))) >= 0)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (CONVERT(bigint, IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CLNG(`o`.`OrderID` MOD 1))) >= 0)",
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
WHERE (`o`.`CustomerID` = 'ALFKI') AND (((IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CBYTE(`o`.`OrderID` MOD 1)) & '') <> '10') OR (IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CBYTE(`o`.`OrderID` MOD 1)) & '') IS NULL)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (((IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CCUR(`o`.`OrderID` MOD 1)) & '') <> '10') OR (IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CCUR(`o`.`OrderID` MOD 1)) & '') IS NULL)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (((IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) & '') <> '10') OR (IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) & '') IS NULL)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND ((CONVERT(nvarchar(max), IIf(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) IS NULL, NULL, CSNG(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1))))) <> '10') OR CONVERT(nvarchar(max), IIf(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1)) IS NULL, NULL, CSNG(IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CDBL(`o`.`OrderID` MOD 1))))) IS NULL)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (((IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CINT(`o`.`OrderID` MOD 1)) & '') <> '10') OR (IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CINT(`o`.`OrderID` MOD 1)) & '') IS NULL)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'ALFKI') AND (((IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CLNG(`o`.`OrderID` MOD 1)) & '') <> '10') OR (IIf(`o`.`OrderID` MOD 1 IS NULL, NULL, CLNG(`o`.`OrderID` MOD 1)) & '') IS NULL)",
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

        public override async Task Indexof_with_emptystring(bool isAsync)
        {
            await base.Indexof_with_emptystring(isAsync);

            AssertSql(
                $@"SELECT CASE
    WHEN '' = '' THEN 0
    ELSE CHARINDEX('', `c`.`ContactName`) - 1
END
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` = 'ALFKI'");
        }

        public override async Task Replace_with_emptystring(bool isAsync)
        {
            await base.Replace_with_emptystring(isAsync);

            AssertSql(
                $@"SELECT REPLACE(`c`.`ContactName`, 'ari', '')
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` = 'ALFKI'");
        }

        public override async Task Substring_with_zero_startindex(bool isAsync)
        {
            await base.Substring_with_zero_startindex(isAsync);

            // issue #15994
//            AssertSql(
//                $@"SELECT SUBSTRING(`c`.`ContactName`, 1, 3)
//FROM `Customers` AS `c`
//WHERE `c`.`CustomerID` = 'ALFKI'");
        }

        public override async Task Substring_with_zero_length(bool isAsync)
        {
            await base.Substring_with_zero_length(isAsync);

            // issue #15994
//            AssertSql(
//                $@"SELECT SUBSTRING(`c`.`ContactName`, 3, 0)
//FROM `Customers` AS `c`
//WHERE `c`.`CustomerID` = 'ALFKI'");
        }

        public override async Task Substring_with_constant(bool isAsync)
        {
            await base.Substring_with_constant(isAsync);

            // issue #15994
//            AssertSql(
//                $@"SELECT SUBSTRING(`c`.`ContactName`, 2, 3)
//FROM `Customers` AS `c`
//WHERE `c`.`CustomerID` = 'ALFKI'");
        }

        public override async Task Substring_with_closure(bool isAsync)
        {
            await base.Substring_with_closure(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__start_0='2'")}

SELECT SUBSTRING(`c`.`ContactName`, {AssertSqlHelper.Parameter("@__start_0")} + 1, 3)
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` = 'ALFKI'");
        }

        public override async Task Substring_with_Index_of(bool isAsync)
        {
            await base.Substring_with_Index_of(isAsync);

            AssertSql(
                $@"SELECT SUBSTRING(`c`.`ContactName`, CASE
    WHEN 'a' = '' THEN 0
    ELSE CHARINDEX('a', `c`.`ContactName`) - 1
END + 1, 3)
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` = 'ALFKI'");
        }

        public override async Task IsNullOrEmpty_in_predicate(bool isAsync)
        {
            await base.IsNullOrEmpty_in_predicate(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`Region` IS NULL OR (`c`.`Region` = '')");
        }

        public override void IsNullOrEmpty_in_projection()
        {
            base.IsNullOrEmpty_in_projection();

            AssertSql(
                $@"SELECT `c`.`CustomerID` AS `Id`, CASE
    WHEN `c`.`Region` IS NULL OR ((`c`.`Region` = '') AND `c`.`Region` IS NOT NULL) THEN 1
    ELSE 0
END AS `Value`
FROM `Customers` AS `c`");
        }

        public override void IsNullOrEmpty_negated_in_projection()
        {
            base.IsNullOrEmpty_negated_in_projection();

            AssertSql(
                $@"SELECT `c`.`CustomerID` AS `Id`, CASE
    WHEN `c`.`Region` IS NOT NULL AND ((`c`.`Region` <> '') OR `c`.`Region` IS NULL) THEN 1
    ELSE 0
END AS `Value`
FROM `Customers` AS `c`");
        }

        public override async Task IsNullOrWhiteSpace_in_predicate(bool isAsync)
        {
            await base.IsNullOrWhiteSpace_in_predicate(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`Region` IS NULL OR (LTRIM(RTRIM(`c`.`Region`)) = '')");
        }

        public override async Task IsNullOrWhiteSpace_in_predicate_on_non_nullable_column(bool isAsync)
        {
            await base.IsNullOrWhiteSpace_in_predicate_on_non_nullable_column(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE LTRIM(RTRIM(`c`.`CustomerID`)) = ''");
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
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
ORDER BY CAST(LEN(`c`.`CustomerID`) AS int), `c`.`CustomerID`");
        }

        public override async Task Order_by_length_twice_followed_by_projection_of_naked_collection_navigation(bool isAsync)
        {
            await base.Order_by_length_twice_followed_by_projection_of_naked_collection_navigation(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Customers` AS `c`
LEFT JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`
ORDER BY CAST(LEN(`c`.`CustomerID`) AS int), `c`.`CustomerID`, `o`.`OrderID`");
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
                $@"{AssertSqlHelper.Declaration("@__arg_0='1996-07-04T00:00:00'")}

SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`OrderDate` = {AssertSqlHelper.Parameter("@__arg_0")}");
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
//                $@"SELECT ROUND(IIf(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`)), 0, 1) AS `A`
//FROM `Orders` AS `o`
//WHERE `o`.`OrderID` < 10250
//ORDER BY `A`");
        }

        public override async Task Projecting_Math_Truncate_and_ordering_by_it_twice2(bool isAsync)
        {
            await base.Projecting_Math_Truncate_and_ordering_by_it_twice2(isAsync);

            // issue #16038
//            AssertSql(
//                $@"SELECT ROUND(IIf(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`)), 0, 1) AS `A`
//FROM `Orders` AS `o`
//WHERE `o`.`OrderID` < 10250
//ORDER BY `A` DESC");
        }

        public override async Task Projecting_Math_Truncate_and_ordering_by_it_twice3(bool isAsync)
        {
            await base.Projecting_Math_Truncate_and_ordering_by_it_twice3(isAsync);

            // issue #16038
//            AssertSql(
//                $@"SELECT ROUND(IIf(`o`.`OrderID` IS NULL, NULL, CDBL(`o`.`OrderID`)), 0, 1) AS `A`
//FROM `Orders` AS `o`
//WHERE `o`.`OrderID` < 10250
//ORDER BY `A` DESC");
        }

        private void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);

        protected override void ClearLog()
            => Fixture.TestSqlLoggerFactory.Clear();
    }
}
