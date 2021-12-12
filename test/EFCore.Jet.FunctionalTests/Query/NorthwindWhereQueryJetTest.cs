﻿// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query
{
    public class NorthwindWhereQueryJetTest : NorthwindWhereQueryRelationalTestBase<
        NorthwindQueryJetFixture<NoopModelCustomizer>>
    {
        public NorthwindWhereQueryJetTest(
            NorthwindQueryJetFixture<NoopModelCustomizer> fixture,
            ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            ClearLog();
            Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
        }

        protected override bool CanExecuteQueryString
            => true;

        public override async Task Where_simple(bool isAsync)
        {
            await base.Where_simple(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`City` = 'London'");
        }

        public override async Task Where_as_queryable_expression(bool isAsync)
        {
            await base.Where_as_queryable_expression(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE EXISTS (
    SELECT 1
    FROM `Orders` AS `o`
    WHERE (`c`.`CustomerID` = `o`.`CustomerID`) AND (`o`.`CustomerID` = 'ALFKI'))");
        }

        public override async Task<string> Where_simple_closure(bool isAsync)
        {
            var queryString = await base.Where_simple_closure(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__city_0='London' (Size = 4000)")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`City` = {AssertSqlHelper.Parameter("@__city_0")}");

            return queryString;
        }

        public override async Task Where_indexer_closure(bool isAsync)
        {
            await base.Where_indexer_closure(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='London' (Size = 4000)")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`City` = {AssertSqlHelper.Parameter("@__p_0")}");
        }

        public override async Task Where_dictionary_key_access_closure(bool isAsync)
        {
            await base.Where_dictionary_key_access_closure(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__get_Item_0='London' (Size = 4000)")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`City` = {AssertSqlHelper.Parameter("@__get_Item_0")}");
        }

        public override async Task Where_tuple_item_closure(bool isAsync)
        {
            await base.Where_tuple_item_closure(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__predicateTuple_Item2_0='London' (Size = 4000)")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`City` = {AssertSqlHelper.Parameter("@__predicateTuple_Item2_0")}");
        }

        public override async Task Where_named_tuple_item_closure(bool isAsync)
        {
            await base.Where_named_tuple_item_closure(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__predicateTuple_Item2_0='London' (Size = 4000)")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`City` = {AssertSqlHelper.Parameter("@__predicateTuple_Item2_0")}");
        }

        public override async Task Where_simple_closure_constant(bool isAsync)
        {
            await base.Where_simple_closure_constant(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__predicate_0='True'")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE {AssertSqlHelper.Parameter("@__predicate_0")} = True");
        }

        public override async Task Where_simple_closure_via_query_cache(bool isAsync)
        {
            await base.Where_simple_closure_via_query_cache(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__city_0='London' (Size = 4000)")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`City` = {AssertSqlHelper.Parameter("@__city_0")}",
                //
                $@"{AssertSqlHelper.Declaration("@__city_0='Seattle' (Size = 4000)")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`City` = {AssertSqlHelper.Parameter("@__city_0")}");
        }

        public override async Task Where_method_call_nullable_type_closure_via_query_cache(bool isAsync)
        {
            await base.Where_method_call_nullable_type_closure_via_query_cache(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='2' (Nullable = true)")}

SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE CAST(`e`.`ReportsTo` AS bigint) = {AssertSqlHelper.Parameter("@__p_0")}",
                //
                $@"{AssertSqlHelper.Declaration("@__p_0='5' (Nullable = true)")}

SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE CAST(`e`.`ReportsTo` AS bigint) = {AssertSqlHelper.Parameter("@__p_0")}");
        }

        public override async Task Where_method_call_nullable_type_reverse_closure_via_query_cache(bool isAsync)
        {
            await base.Where_method_call_nullable_type_reverse_closure_via_query_cache(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='1' (Nullable = true)")}

SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE CAST(`e`.`EmployeeID` AS bigint) > {AssertSqlHelper.Parameter("@__p_0")}",
                //
                $@"{AssertSqlHelper.Declaration("@__p_0='5' (Nullable = true)")}

SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE CAST(`e`.`EmployeeID` AS bigint) > {AssertSqlHelper.Parameter("@__p_0")}");
        }

        public override async Task Where_method_call_closure_via_query_cache(bool isAsync)
        {
            await base.Where_method_call_closure_via_query_cache(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__GetCity_0='London' (Size = 4000)")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`City` = {AssertSqlHelper.Parameter("@__GetCity_0")}",
                //
                $@"{AssertSqlHelper.Declaration("@__GetCity_0='Seattle' (Size = 4000)")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`City` = {AssertSqlHelper.Parameter("@__GetCity_0")}");
        }

        public override async Task Where_field_access_closure_via_query_cache(bool isAsync)
        {
            await base.Where_field_access_closure_via_query_cache(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__city_InstanceFieldValue_0='London' (Size = 4000)")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`City` = {AssertSqlHelper.Parameter("@__city_InstanceFieldValue_0")}",
                //
                $@"{AssertSqlHelper.Declaration("@__city_InstanceFieldValue_0='Seattle' (Size = 4000)")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`City` = {AssertSqlHelper.Parameter("@__city_InstanceFieldValue_0")}");
        }

        public override async Task Where_property_access_closure_via_query_cache(bool isAsync)
        {
            await base.Where_property_access_closure_via_query_cache(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__city_InstancePropertyValue_0='London' (Size = 4000)")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`City` = {AssertSqlHelper.Parameter("@__city_InstancePropertyValue_0")}",
                //
                $@"{AssertSqlHelper.Declaration("@__city_InstancePropertyValue_0='Seattle' (Size = 4000)")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`City` = {AssertSqlHelper.Parameter("@__city_InstancePropertyValue_0")}");
        }

        public override async Task Where_static_field_access_closure_via_query_cache(bool isAsync)
        {
            await base.Where_static_field_access_closure_via_query_cache(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__StaticFieldValue_0='London' (Size = 4000)")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`City` = {AssertSqlHelper.Parameter("@__StaticFieldValue_0")}",
                //
                $@"{AssertSqlHelper.Declaration("@__StaticFieldValue_0='Seattle' (Size = 4000)")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`City` = {AssertSqlHelper.Parameter("@__StaticFieldValue_0")}");
        }

        public override async Task Where_static_property_access_closure_via_query_cache(bool isAsync)
        {
            await base.Where_static_property_access_closure_via_query_cache(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__StaticPropertyValue_0='London' (Size = 4000)")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`City` = {AssertSqlHelper.Parameter("@__StaticPropertyValue_0")}",
                //
                $@"{AssertSqlHelper.Declaration("@__StaticPropertyValue_0='Seattle' (Size = 4000)")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`City` = {AssertSqlHelper.Parameter("@__StaticPropertyValue_0")}");
        }

        public override async Task Where_nested_field_access_closure_via_query_cache(bool isAsync)
        {
            await base.Where_nested_field_access_closure_via_query_cache(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__city_Nested_InstanceFieldValue_0='London' (Size = 4000)")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`City` = {AssertSqlHelper.Parameter("@__city_Nested_InstanceFieldValue_0")}",
                //
                $@"{AssertSqlHelper.Declaration("@__city_Nested_InstanceFieldValue_0='Seattle' (Size = 4000)")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`City` = {AssertSqlHelper.Parameter("@__city_Nested_InstanceFieldValue_0")}");
        }

        public override async Task Where_nested_property_access_closure_via_query_cache(bool isAsync)
        {
            await base.Where_nested_property_access_closure_via_query_cache(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__city_Nested_InstancePropertyValue_0='London' (Size = 4000)")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`City` = {AssertSqlHelper.Parameter("@__city_Nested_InstancePropertyValue_0")}",
                //
                $@"{AssertSqlHelper.Declaration("@__city_Nested_InstancePropertyValue_0='Seattle' (Size = 4000)")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`City` = {AssertSqlHelper.Parameter("@__city_Nested_InstancePropertyValue_0")}");
        }

        public override async Task Where_new_instance_field_access_query_cache(bool isAsync)
        {
            await base.Where_new_instance_field_access_query_cache(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__InstanceFieldValue_0='London' (Size = 4000)")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`City` = {AssertSqlHelper.Parameter("@__InstanceFieldValue_0")}",
                //
                $@"{AssertSqlHelper.Declaration("@__InstanceFieldValue_0='Seattle' (Size = 4000)")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`City` = {AssertSqlHelper.Parameter("@__InstanceFieldValue_0")}");
        }

        public override async Task Where_new_instance_field_access_closure_via_query_cache(bool isAsync)
        {
            await base.Where_new_instance_field_access_closure_via_query_cache(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__InstanceFieldValue_0='London' (Size = 4000)")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`City` = {AssertSqlHelper.Parameter("@__InstanceFieldValue_0")}",
                //
                $@"{AssertSqlHelper.Declaration("@__InstanceFieldValue_0='Seattle' (Size = 4000)")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`City` = {AssertSqlHelper.Parameter("@__InstanceFieldValue_0")}");
        }

        public override async Task Where_simple_closure_via_query_cache_nullable_type(bool isAsync)
        {
            await base.Where_simple_closure_via_query_cache_nullable_type(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='2' (Nullable = true)")}

SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE CAST(`e`.`ReportsTo` AS bigint) = {AssertSqlHelper.Parameter("@__p_0")}",
                //
                $@"{AssertSqlHelper.Declaration("@__p_0='5' (Nullable = true)")}

SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE CAST(`e`.`ReportsTo` AS bigint) = {AssertSqlHelper.Parameter("@__p_0")}",
                //
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE `e`.`ReportsTo` IS NULL");
        }

        public override async Task Where_simple_closure_via_query_cache_nullable_type_reverse(bool isAsync)
        {
            await base.Where_simple_closure_via_query_cache_nullable_type_reverse(isAsync);

            AssertSql(
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE `e`.`ReportsTo` IS NULL",
                //
                $@"{AssertSqlHelper.Declaration("@__p_0='5' (Nullable = true)")}

SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE CAST(`e`.`ReportsTo` AS bigint) = {AssertSqlHelper.Parameter("@__p_0")}",
                //
                $@"{AssertSqlHelper.Declaration("@__p_0='2' (Nullable = true)")}

SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE CAST(`e`.`ReportsTo` AS bigint) = {AssertSqlHelper.Parameter("@__p_0")}");
        }

        public override void Where_subquery_closure_via_query_cache()
        {
            base.Where_subquery_closure_via_query_cache();

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__customerID_0='ALFKI' (Size = 5)")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE EXISTS (
    SELECT 1
    FROM `Orders` AS `o`
    WHERE (`o`.`CustomerID` = {AssertSqlHelper.Parameter("@__customerID_0")}) AND (`o`.`CustomerID` = `c`.`CustomerID`))",
                //
                $@"{AssertSqlHelper.Declaration("@__customerID_0='ANATR' (Size = 5)")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE EXISTS (
    SELECT 1
    FROM `Orders` AS `o`
    WHERE (`o`.`CustomerID` = {AssertSqlHelper.Parameter("@__customerID_0")}) AND (`o`.`CustomerID` = `c`.`CustomerID`))");
        }

        public override async Task Where_bitwise_or(bool isAsync)
        {
            await base.Where_bitwise_or(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE (IIF(`c`.`CustomerID` = 'ALFKI', 1, 0) BOR IIF(`c`.`CustomerID` = 'ANATR', 1, 0)) = True");
        }

        public override async Task Where_bitwise_and(bool isAsync)
        {
            await base.Where_bitwise_and(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE (IIF(`c`.`CustomerID` = 'ALFKI', 1, 0) BAND IIF(`c`.`CustomerID` = 'ANATR', 1, 0)) = True");
        }

        public override async Task Where_bitwise_xor(bool isAsync)
        {
            await base.Where_bitwise_xor(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`");
        }

        public override async Task Where_simple_shadow(bool isAsync)
        {
            await base.Where_simple_shadow(isAsync);

            AssertSql(
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE `e`.`Title` = 'Sales Representative'");
        }

        public override async Task Where_simple_shadow_projection(bool isAsync)
        {
            await base.Where_simple_shadow_projection(isAsync);

            AssertSql(
                $@"SELECT `e`.`Title`
FROM `Employees` AS `e`
WHERE `e`.`Title` = 'Sales Representative'");
        }

        public override async Task Where_shadow_subquery_FirstOrDefault(bool isAsync)
        {
            await base.Where_shadow_subquery_FirstOrDefault(isAsync);

            // issue #15994
//            AssertSql(
//                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
//FROM `Employees` AS `e`
//WHERE `e`.`Title` = (
//    SELECT TOP 1 `e2`.`Title`
//    FROM `Employees` AS `e2`
//    ORDER BY `e2`.`Title`
//)");
        }

        public override async Task Where_subquery_correlated(bool isAsync)
        {
            await base.Where_subquery_correlated(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE EXISTS (
    SELECT 1
    FROM `Customers` AS `c0`
    WHERE `c`.`CustomerID` = `c0`.`CustomerID`)");
        }

        public override async Task Where_equals_method_string(bool isAsync)
        {
            await base.Where_equals_method_string(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`City` = 'London'");
        }

        public override async Task Where_equals_method_int(bool isAsync)
        {
            await base.Where_equals_method_int(isAsync);

            AssertSql(
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE `e`.`EmployeeID` = 1");
        }

        public override async Task Where_equals_using_object_overload_on_mismatched_types(bool isAsync)
        {
            await base.Where_equals_using_object_overload_on_mismatched_types(isAsync);

            AssertSql(
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE False = True");

            // See issue#17498
            //Assert.Contains(
            //    RelationalStrings.LogPossibleUnintendedUseOfEquals.GenerateMessage(
            //        "e.EmployeeID.Equals(Convert(__longPrm_0, Object))"), Fixture.TestSqlLoggerFactory.Log.Select(l => l.Message));
        }

        public override async Task Where_equals_using_int_overload_on_mismatched_types(bool isAsync)
        {
            await base.Where_equals_using_int_overload_on_mismatched_types(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='1'")}

SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE `e`.`EmployeeID` = {AssertSqlHelper.Parameter("@__p_0")}");
        }

        public override async Task Where_equals_on_mismatched_types_nullable_int_long(bool isAsync)
        {
            await base.Where_equals_on_mismatched_types_nullable_int_long(isAsync);

            AssertSql(
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE False = True",
                //
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE False = True");

            // See issue#17498
            //Assert.Contains(
            //    RelationalStrings.LogPossibleUnintendedUseOfEquals.GenerateMessage(
            //        "__longPrm_0.Equals(Convert(e.ReportsTo, Object))"), Fixture.TestSqlLoggerFactory.Log.Select(l => l.Message));

            //Assert.Contains(
            //    RelationalStrings.LogPossibleUnintendedUseOfEquals.GenerateMessage(
            //        "e.ReportsTo.Equals(Convert(__longPrm_0, Object))"), Fixture.TestSqlLoggerFactory.Log.Select(l => l.Message));
        }

        public override async Task Where_equals_on_mismatched_types_nullable_long_nullable_int(bool isAsync)
        {
            await base.Where_equals_on_mismatched_types_nullable_long_nullable_int(isAsync);

            AssertSql(
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE False = True",
                //
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE False = True");

            // See issue#17498
            //Assert.Contains(
            //    RelationalStrings.LogPossibleUnintendedUseOfEquals.GenerateMessage(
            //        "__nullableLongPrm_0.Equals(Convert(e.ReportsTo, Object))"),
            //    Fixture.TestSqlLoggerFactory.Log.Select(l => l.Message));

            //Assert.Contains(
            //    RelationalStrings.LogPossibleUnintendedUseOfEquals.GenerateMessage(
            //        "e.ReportsTo.Equals(Convert(__nullableLongPrm_0, Object))"),
            //    Fixture.TestSqlLoggerFactory.Log.Select(l => l.Message));
        }

        public override async Task Where_equals_on_mismatched_types_int_nullable_int(bool isAsync)
        {
            await base.Where_equals_on_mismatched_types_int_nullable_int(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__intPrm_0='2'")}

SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE `e`.`ReportsTo` = {AssertSqlHelper.Parameter("@__intPrm_0")}",
                //
                $@"{AssertSqlHelper.Declaration("@__intPrm_0='2'")}

SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE {AssertSqlHelper.Parameter("@__intPrm_0")} = `e`.`ReportsTo`");
        }

        public override async Task Where_equals_on_matched_nullable_int_types(bool isAsync)
        {
            await base.Where_equals_on_matched_nullable_int_types(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__nullableIntPrm_0='2' (Nullable = true)")}

SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE {AssertSqlHelper.Parameter("@__nullableIntPrm_0")} = `e`.`ReportsTo`",
                //
                $@"{AssertSqlHelper.Declaration("@__nullableIntPrm_0='2' (Nullable = true)")}

SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE `e`.`ReportsTo` = {AssertSqlHelper.Parameter("@__nullableIntPrm_0")}");
        }

        public override async Task Where_equals_on_null_nullable_int_types(bool isAsync)
        {
            await base.Where_equals_on_null_nullable_int_types(isAsync);

            AssertSql(
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE `e`.`ReportsTo` IS NULL",
                //
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE `e`.`ReportsTo` IS NULL");
        }

        public override async Task Where_comparison_nullable_type_not_null(bool isAsync)
        {
            await base.Where_comparison_nullable_type_not_null(isAsync);

            AssertSql(
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE `e`.`ReportsTo` = 2");
        }

        public override async Task Where_comparison_nullable_type_null(bool isAsync)
        {
            await base.Where_comparison_nullable_type_null(isAsync);

            AssertSql(
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE `e`.`ReportsTo` IS NULL");
        }

        public override async Task Where_string_length(bool isAsync)
        {
            await base.Where_string_length(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE CAST(LEN(`c`.`City`) AS int) = 6");
        }

        public override async Task Where_string_indexof(bool isAsync)
        {
            await base.Where_string_indexof(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE (CASE
    WHEN 'Sea' = '' THEN 0
    ELSE CHARINDEX('Sea', `c`.`City`) - 1
END <> -1) OR CASE
    WHEN 'Sea' = '' THEN 0
    ELSE CHARINDEX('Sea', `c`.`City`) - 1
END IS NULL");
        }

        public override async Task Where_string_replace(bool isAsync)
        {
            await base.Where_string_replace(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE REPLACE(`c`.`City`, 'Sea', 'Rea') = 'Reattle'");
        }

        public override async Task Where_string_substring(bool isAsync)
        {
            await base.Where_string_substring(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE SUBSTRING(`c`.`City`, 1 + 1, 2) = 'ea'");
        }

        public override async Task Where_datetime_now(bool isAsync)
        {
            await base.Where_datetime_now(isAsync);

            // issue #15994
//            AssertSql(
//                $@"{AssertSqlHelper.Declaration("@__myDatetime_0='2015-04-10T00:00:00'")}

//SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
//FROM `Customers` AS `c`
//WHERE GETDATE() <> @__myDatetime_0");
        }

        public override async Task Where_datetime_utcnow(bool isAsync)
        {
            await base.Where_datetime_utcnow(isAsync);

            // issue #15994
//            AssertSql(
//                $@"{AssertSqlHelper.Declaration("@__myDatetime_0='2015-04-10T00:00:00'")}

//SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
//FROM `Customers` AS `c`
//WHERE GETUTCDATE() <> @__myDatetime_0");
        }

        public override async Task Where_datetime_today(bool isAsync)
        {
            await base.Where_datetime_today(isAsync);

            AssertSql(
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE (CONVERT(date, GETDATE()) = CONVERT(date, GETDATE())) OR CONVERT(date, GETDATE()) IS NULL");
        }

        public override async Task Where_datetime_date_component(bool isAsync)
        {
            await base.Where_datetime_date_component(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__myDatetime_0='1998-05-04T00:00:00' (DbType = DateTime)")}

SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE CONVERT(date, `o`.`OrderDate`) = {AssertSqlHelper.Parameter("@__myDatetime_0")}");
        }

        public override async Task Where_date_add_year_constant_component(bool isAsync)
        {
            await base.Where_date_add_year_constant_component(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE DATEPART('yyyy', DATEADD('yyyy', CAST(-1 AS int), `o`.`OrderDate`)) = 1997");
        }

        public override async Task Where_datetime_year_component(bool isAsync)
        {
            await base.Where_datetime_year_component(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE DATEPART('yyyy', `o`.`OrderDate`) = 1998");
        }

        public override async Task Where_datetime_month_component(bool isAsync)
        {
            await base.Where_datetime_month_component(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE DATEPART('m', `o`.`OrderDate`) = 4");
        }

        public override async Task Where_datetime_dayOfYear_component(bool isAsync)
        {
            await base.Where_datetime_dayOfYear_component(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE DATEPART(dayofyear, `o`.`OrderDate`) = 68");
        }

        public override async Task Where_datetime_day_component(bool isAsync)
        {
            await base.Where_datetime_day_component(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE DATEPART('d', `o`.`OrderDate`) = 4");
        }

        public override async Task Where_datetime_hour_component(bool isAsync)
        {
            await base.Where_datetime_hour_component(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE DATEPART('h', `o`.`OrderDate`) = 14");
        }

        public override async Task Where_datetime_minute_component(bool isAsync)
        {
            await base.Where_datetime_minute_component(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE DATEPART('n', `o`.`OrderDate`) = 23");
        }

        public override async Task Where_datetime_second_component(bool isAsync)
        {
            await base.Where_datetime_second_component(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE DATEPART('s', `o`.`OrderDate`) = 44");
        }

        public override async Task Where_datetime_millisecond_component(bool isAsync)
        {
            await base.Where_datetime_millisecond_component(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE DATEPART(millisecond, `o`.`OrderDate`) = 88");
        }

        public override async Task Where_datetimeoffset_now_component(bool isAsync)
        {
            await base.Where_datetimeoffset_now_component(isAsync);

            // issue #15994
//            AssertSql(
//                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
//FROM `Orders` AS `o`
//WHERE `o`.`OrderDate` = SYSDATETIMEOFFSET()");
        }

        public override async Task Where_datetimeoffset_utcnow_component(bool isAsync)
        {
            await base.Where_datetimeoffset_utcnow_component(isAsync);

            // issue #15994
//            AssertSql(
//                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
//FROM `Orders` AS `o`
//WHERE `o`.`OrderDate` = CAST(SYSUTCDATETIME() AS datetimeoffset)");
        }

        public override async Task Where_simple_reversed(bool isAsync)
        {
            await base.Where_simple_reversed(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE 'London' = `c`.`City`");
        }

        public override async Task Where_is_null(bool isAsync)
        {
            await base.Where_is_null(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`City` IS NULL");
        }

        public override async Task Where_null_is_null(bool isAsync)
        {
            await base.Where_null_is_null(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`");
        }

        public override async Task Where_constant_is_null(bool isAsync)
        {
            await base.Where_constant_is_null(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE False = True");
        }

        public override async Task Where_is_not_null(bool isAsync)
        {
            await base.Where_is_not_null(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`City` IS NOT NULL");
        }

        public override async Task Where_null_is_not_null(bool isAsync)
        {
            await base.Where_null_is_not_null(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE False = True");
        }

        public override async Task Where_constant_is_not_null(bool isAsync)
        {
            await base.Where_constant_is_not_null(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`");
        }

        public override async Task Where_identity_comparison(bool isAsync)
        {
            await base.Where_identity_comparison(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE (`c`.`City` = `c`.`City`) OR `c`.`City` IS NULL");
        }

        public override async Task Where_in_optimization_multiple(bool isAsync)
        {
            await base.Where_in_optimization_multiple(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Customers` AS `c`,
`Employees` AS `e`
WHERE (((`c`.`City` = 'London') OR (`c`.`City` = 'Berlin')) OR (`c`.`CustomerID` = 'ALFKI')) OR (`c`.`CustomerID` = 'ABCDE')");
        }

        public override async Task Where_not_in_optimization1(bool isAsync)
        {
            await base.Where_not_in_optimization1(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Customers` AS `c`,
`Employees` AS `e`
WHERE ((`c`.`City` <> 'London') OR `c`.`City` IS NULL) AND ((`e`.`City` <> 'London') OR `e`.`City` IS NULL)");
        }

        public override async Task Where_not_in_optimization2(bool isAsync)
        {
            await base.Where_not_in_optimization2(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Customers` AS `c`,
`Employees` AS `e`
WHERE ((`c`.`City` <> 'London') OR `c`.`City` IS NULL) AND ((`c`.`City` <> 'Berlin') OR `c`.`City` IS NULL)");
        }

        public override async Task Where_not_in_optimization3(bool isAsync)
        {
            await base.Where_not_in_optimization3(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Customers` AS `c`,
`Employees` AS `e`
WHERE (((`c`.`City` <> 'London') OR `c`.`City` IS NULL) AND ((`c`.`City` <> 'Berlin') OR `c`.`City` IS NULL)) AND ((`c`.`City` <> 'Seattle') OR `c`.`City` IS NULL)");
        }

        public override async Task Where_not_in_optimization4(bool isAsync)
        {
            await base.Where_not_in_optimization4(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Customers` AS `c`,
`Employees` AS `e`
WHERE ((((`c`.`City` <> 'London') OR `c`.`City` IS NULL) AND ((`c`.`City` <> 'Berlin') OR `c`.`City` IS NULL)) AND ((`c`.`City` <> 'Seattle') OR `c`.`City` IS NULL)) AND ((`c`.`City` <> 'Lisboa') OR `c`.`City` IS NULL)");
        }

        public override async Task Where_select_many_and(bool isAsync)
        {
            await base.Where_select_many_and(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Customers` AS `c`,
`Employees` AS `e`
WHERE ((`c`.`City` = 'London') AND (`c`.`Country` = 'UK')) AND ((`e`.`City` = 'London') AND (`e`.`Country` = 'UK'))");
        }

        public override async Task Where_primitive(bool isAsync)
        {
            await base.Where_primitive(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='9'")}

SELECT `t`.`EmployeeID`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `e`.`EmployeeID`
    FROM `Employees` AS `e`
) AS `t`
WHERE `t`.`EmployeeID` = 5");
        }

        public override async Task Where_bool_member(bool isAsync)
        {
            await base.Where_bool_member(isAsync);

            AssertSql(
                $@"SELECT `p`.`ProductID`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice`, `p`.`UnitsInStock`
FROM `Products` AS `p`
WHERE `p`.`Discontinued` = True");
        }

        public override async Task Where_bool_member_false(bool isAsync)
        {
            await base.Where_bool_member_false(isAsync);

            // issue #15994
//            AssertSql(
//                $@"SELECT `p`.`ProductID`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice`, `p`.`UnitsInStock`
//FROM `Products` AS `p`
//WHERE `p`.`Discontinued` = False");
        }

        //        public override async Task Where_bool_client_side_negated(bool isAsync)
        //        {
        //            await base.Where_bool_client_side_negated(isAsync);

        //            AssertSql(
        //                $@"SELECT `p`.`ProductID`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice`, `p`.`UnitsInStock`
        //FROM `Products` AS `p`
        //WHERE `p`.`Discontinued` = 1");
        //        }

        public override async Task Where_bool_member_negated_twice(bool isAsync)
        {
            await base.Where_bool_member_negated_twice(isAsync);

            AssertSql(
                $@"SELECT `p`.`ProductID`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice`, `p`.`UnitsInStock`
FROM `Products` AS `p`
WHERE `p`.`Discontinued` = True");
        }

        public override async Task Where_bool_member_shadow(bool isAsync)
        {
            await base.Where_bool_member_shadow(isAsync);

            AssertSql(
                $@"SELECT `p`.`ProductID`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice`, `p`.`UnitsInStock`
FROM `Products` AS `p`
WHERE `p`.`Discontinued` = True");
        }

        public override async Task Where_bool_member_false_shadow(bool isAsync)
        {
            await base.Where_bool_member_false_shadow(isAsync);

            AssertSql(
                $@"SELECT `p`.`ProductID`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice`, `p`.`UnitsInStock`
FROM `Products` AS `p`
WHERE `p`.`Discontinued` <> True");
        }

        public override async Task Where_bool_member_equals_constant(bool isAsync)
        {
            await base.Where_bool_member_equals_constant(isAsync);

            AssertSql(
                $@"SELECT `p`.`ProductID`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice`, `p`.`UnitsInStock`
FROM `Products` AS `p`
WHERE `p`.`Discontinued` = True");
        }

        public override async Task Where_bool_member_in_complex_predicate(bool isAsync)
        {
            await base.Where_bool_member_in_complex_predicate(isAsync);

            AssertSql(
                $@"SELECT `p`.`ProductID`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice`, `p`.`UnitsInStock`
FROM `Products` AS `p`
WHERE ((`p`.`ProductID` > 100) AND (`p`.`Discontinued` = True)) OR (`p`.`Discontinued` = True)");
        }

        public override async Task Where_bool_member_compared_to_binary_expression(bool isAsync)
        {
            await base.Where_bool_member_compared_to_binary_expression(isAsync);

            AssertSql(
                $@"SELECT `p`.`ProductID`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice`, `p`.`UnitsInStock`
FROM `Products` AS `p`
WHERE `p`.`Discontinued` = IIF(`p`.`ProductID` > 50, 1, 0)");
        }

        public override async Task Where_not_bool_member_compared_to_not_bool_member(bool isAsync)
        {
            await base.Where_not_bool_member_compared_to_not_bool_member(isAsync);

            AssertSql(
                $@"SELECT `p`.`ProductID`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice`, `p`.`UnitsInStock`
FROM `Products` AS `p`");
        }

        public override async Task Where_negated_boolean_expression_compared_to_another_negated_boolean_expression(bool isAsync)
        {
            await base.Where_negated_boolean_expression_compared_to_another_negated_boolean_expression(isAsync);

            AssertSql(
                $@"SELECT `p`.`ProductID`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice`, `p`.`UnitsInStock`
FROM `Products` AS `p`
WHERE IIF(`p`.`ProductID` > 50, 1, 0) = IIF(`p`.`ProductID` > 20, 1, 0)");
        }

        public override async Task Where_not_bool_member_compared_to_binary_expression(bool isAsync)
        {
            await base.Where_not_bool_member_compared_to_binary_expression(isAsync);

            AssertSql(
                $@"SELECT `p`.`ProductID`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice`, `p`.`UnitsInStock`
FROM `Products` AS `p`
WHERE `p`.`Discontinued` <> IIF(`p`.`ProductID` > 50, 1, 0)");
        }

        public override async Task Where_bool_parameter(bool isAsync)
        {
            await base.Where_bool_parameter(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__prm_0='True'")}

SELECT `p`.`ProductID`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice`, `p`.`UnitsInStock`
FROM `Products` AS `p`
WHERE {AssertSqlHelper.Parameter("@__prm_0")} = True");
        }

        public override async Task Where_bool_parameter_compared_to_binary_expression(bool isAsync)
        {
            await base.Where_bool_parameter_compared_to_binary_expression(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__prm_0='True'")}

SELECT `p`.`ProductID`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice`, `p`.`UnitsInStock`
FROM `Products` AS `p`
WHERE IIF(`p`.`ProductID` > 50, 1, 0) <> {AssertSqlHelper.Parameter("@__prm_0")}");
        }

        public override async Task Where_bool_member_and_parameter_compared_to_binary_expression_nested(bool isAsync)
        {
            await base.Where_bool_member_and_parameter_compared_to_binary_expression_nested(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__prm_0='True'")}

SELECT `p`.`ProductID`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice`, `p`.`UnitsInStock`
FROM `Products` AS `p`
WHERE `p`.`Discontinued` = CASE
    WHEN IIF(`p`.`ProductID` > 50, 1, 0) <> {AssertSqlHelper.Parameter("@__prm_0")} THEN True
    ELSE False
END");
        }

        public override async Task Where_de_morgan_or_optimized(bool isAsync)
        {
            await base.Where_de_morgan_or_optimized(isAsync);

            AssertSql(
                $@"SELECT `p`.`ProductID`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice`, `p`.`UnitsInStock`
FROM `Products` AS `p`
WHERE (`p`.`Discontinued` <> True) AND (`p`.`ProductID` >= 20)");
        }

        public override async Task Where_de_morgan_and_optimized(bool isAsync)
        {
            await base.Where_de_morgan_and_optimized(isAsync);

            AssertSql(
                $@"SELECT `p`.`ProductID`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice`, `p`.`UnitsInStock`
FROM `Products` AS `p`
WHERE (`p`.`Discontinued` <> True) OR (`p`.`ProductID` >= 20)");
        }

        public override async Task Where_complex_negated_expression_optimized(bool isAsync)
        {
            await base.Where_complex_negated_expression_optimized(isAsync);

            AssertSql(
                $@"SELECT `p`.`ProductID`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice`, `p`.`UnitsInStock`
FROM `Products` AS `p`
WHERE ((`p`.`Discontinued` <> True) AND (`p`.`ProductID` < 60)) AND (`p`.`ProductID` > 30)");
        }

        public override async Task Where_short_member_comparison(bool isAsync)
        {
            await base.Where_short_member_comparison(isAsync);

            AssertSql(
                $@"SELECT `p`.`ProductID`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice`, `p`.`UnitsInStock`
FROM `Products` AS `p`
WHERE `p`.`UnitsInStock` > 10");
        }

        public override async Task Where_comparison_to_nullable_bool(bool isAsync)
        {
            await base.Where_comparison_to_nullable_bool(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` LIKE '%KI'");
        }

        public override async Task Where_true(bool isAsync)
        {
            await base.Where_true(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`");
        }

        public override async Task Where_false(bool isAsync)
        {
            await base.Where_false(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE False = True");
        }

        public override async Task Where_default(bool isAsync)
        {
            await base.Where_default(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`Fax` IS NULL");
        }

        public override async Task Where_expression_invoke_1(bool isAsync)
        {
            await base.Where_expression_invoke_1(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` = 'ALFKI'");
        }

        public override async Task Where_expression_invoke_2(bool isAsync)
        {
            await base.Where_expression_invoke_2(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
LEFT JOIN `Customers` AS `c` ON `o`.`CustomerID` = `c`.`CustomerID`
WHERE `c`.`CustomerID` = 'ALFKI'");
        }

        public override async Task Where_concat_string_int_comparison1(bool isAsync)
        {
            await base.Where_concat_string_int_comparison1(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__i_0='10'")}

SELECT `c`.`CustomerID`
FROM `Customers` AS `c`
WHERE (`c`.`CustomerID` + CAST({AssertSqlHelper.Parameter("@__i_0")} AS nchar(5))) = `c`.`CompanyName`");
        }

        public override async Task Where_concat_string_int_comparison2(bool isAsync)
        {
            await base.Where_concat_string_int_comparison2(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__i_0='10'")}

SELECT `c`.`CustomerID`
FROM `Customers` AS `c`
WHERE (CAST({AssertSqlHelper.Parameter("@__i_0")} AS nchar(5)) + `c`.`CustomerID`) = `c`.`CompanyName`");
        }

        public override async Task Where_concat_string_int_comparison3(bool isAsync)
        {
            await base.Where_concat_string_int_comparison3(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='30'")}

{AssertSqlHelper.Declaration("@__j_1='21'")}

SELECT `c`.`CustomerID`
FROM `Customers` AS `c`
WHERE (((CAST({AssertSqlHelper.Parameter("@__p_0")} AS nchar(5)) + `c`.`CustomerID`) + CAST({AssertSqlHelper.Parameter("@__j_1")} AS nchar(5))) + CAST(42 AS nchar(5))) = `c`.`CompanyName`");
        }

        public override async Task Where_concat_string_int_comparison4(bool isAsync)
        {
            await base.Where_concat_string_int_comparison4(isAsync);

            AssertSql(
                $@"SELECT `o`.`CustomerID`
FROM `Orders` AS `o`
WHERE ((CAST(`o`.`OrderID` AS nchar(5)) + `o`.`CustomerID`) = `o`.`CustomerID`) OR `o`.`CustomerID` IS NULL");
        }

        public override async Task Where_concat_string_string_comparison(bool isAsync)
        {
            await base.Where_concat_string_string_comparison(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__i_0='A' (Size = 5)")}

SELECT `c`.`CustomerID`
FROM `Customers` AS `c`
WHERE ({AssertSqlHelper.Parameter("@__i_0")} + `c`.`CustomerID`) = `c`.`CompanyName`");
        }

        public override async Task Where_string_concat_method_comparison(bool isAsync)
        {
            await base.Where_string_concat_method_comparison(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__i_0='A' (Size = 5)")}

SELECT `c`.`CustomerID`
FROM `Customers` AS `c`
WHERE ({AssertSqlHelper.Parameter("@__i_0")} + `c`.`CustomerID`) = `c`.`CompanyName`");
        }

        public override async Task Where_ternary_boolean_condition_true(bool isAsync)
        {
            await base.Where_ternary_boolean_condition_true(isAsync);

            AssertSql(
                $@"SELECT `p`.`ProductID`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice`, `p`.`UnitsInStock`
FROM `Products` AS `p`
WHERE `p`.`UnitsInStock` >= 20");
        }

        public override async Task Where_ternary_boolean_condition_false(bool isAsync)
        {
            await base.Where_ternary_boolean_condition_false(isAsync);

            // issue #15994
//            AssertSql(
//                $@"{AssertSqlHelper.Declaration("@__flag_0='False'")}

//SELECT `p`.`ProductID`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice`, `p`.`UnitsInStock`
//FROM `Products` AS `p`
//WHERE ((@__flag_0 = True) AND (`p`.`UnitsInStock` >= 20)) OR ((@__flag_0 <> True) AND (`p`.`UnitsInStock` < 20))");
        }

        public override async Task Where_ternary_boolean_condition_with_another_condition(bool isAsync)
        {
            await base.Where_ternary_boolean_condition_with_another_condition(isAsync);

            // issue #15994
//            AssertSql(
//                $@"{AssertSqlHelper.Declaration("@__productId_0='15'")}

//@__flag_1='True'

//SELECT `p`.`ProductID`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice`, `p`.`UnitsInStock`
//FROM `Products` AS `p`
//WHERE (`p`.`ProductID` < @__productId_0) AND (((@__flag_1 = True) AND (`p`.`UnitsInStock` >= 20)) OR ((@__flag_1 <> True) AND (`p`.`UnitsInStock` < 20)))");
        }

        public override async Task Where_ternary_boolean_condition_with_false_as_result_true(bool isAsync)
        {
            await base.Where_ternary_boolean_condition_with_false_as_result_true(isAsync);

            // issue #15994
//            AssertSql(
//                $@"{AssertSqlHelper.Declaration("@__flag_0='True'")}

//SELECT `p`.`ProductID`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice`, `p`.`UnitsInStock`
//FROM `Products` AS `p`
//WHERE (@__flag_0 = True) AND (`p`.`UnitsInStock` >= 20)");
        }

        public override async Task Where_ternary_boolean_condition_with_false_as_result_false(bool isAsync)
        {
            await base.Where_ternary_boolean_condition_with_false_as_result_false(isAsync);

            // issue #15994
//            AssertSql(
//                $@"{AssertSqlHelper.Declaration("@__flag_0='False'")}

//SELECT `p`.`ProductID`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice`, `p`.`UnitsInStock`
//FROM `Products` AS `p`
//WHERE (@__flag_0 = True) AND (`p`.`UnitsInStock` >= 20)");
        }

        public override async Task Where_compare_constructed_equal(bool isAsync)
        {
            await base.Where_compare_constructed_equal(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`");
        }
        
        public override async Task Where_compare_constructed_multi_value_equal(bool isAsync)
        {
            await base.Where_compare_constructed_multi_value_equal(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`");
        }

        public override async Task Where_compare_constructed_multi_value_not_equal(bool isAsync)
        {
            await base.Where_compare_constructed_multi_value_not_equal(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`");
        }

        public override async Task Where_compare_tuple_constructed_equal(bool isAsync)
        {
            await base.Where_compare_tuple_constructed_equal(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`");
        }

        public override async Task Where_compare_tuple_constructed_multi_value_equal(bool isAsync)
        {
            await base.Where_compare_tuple_constructed_multi_value_equal(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`");
        }

        public override async Task Where_compare_tuple_constructed_multi_value_not_equal(bool isAsync)
        {
            await base.Where_compare_tuple_constructed_multi_value_not_equal(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`");
        }

        public override async Task Where_compare_tuple_create_constructed_equal(bool isAsync)
        {
            await base.Where_compare_tuple_create_constructed_equal(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`");
        }

        public override async Task Where_compare_tuple_create_constructed_multi_value_equal(bool isAsync)
        {
            await base.Where_compare_tuple_create_constructed_multi_value_equal(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`");
        }

        public override async Task Where_compare_tuple_create_constructed_multi_value_not_equal(bool isAsync)
        {
            await base.Where_compare_tuple_create_constructed_multi_value_not_equal(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`");
        }

        public override async Task Where_compare_null(bool isAsync)
        {
            await base.Where_compare_null(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`City` IS NULL AND (`c`.`Country` = 'UK')");
        }

        public override async Task Where_Is_on_same_type(bool isAsync)
        {
            await base.Where_Is_on_same_type(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`");
        }

        public override async Task Where_chain(bool isAsync)
        {
            await base.Where_chain(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`CustomerID` = 'QUICK') AND (`o`.`OrderDate` > #1998-01-01#)");
        }

        public override void Where_navigation_contains()
        {
            base.Where_navigation_contains();

            AssertSql(
                $@"SELECT `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM (
    SELECT TOP 2 `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    WHERE `c`.`CustomerID` = 'ALFKI'
) AS `t`
LEFT JOIN `Orders` AS `o` ON `t`.`CustomerID` = `o`.`CustomerID`
ORDER BY `t`.`CustomerID`, `o`.`OrderID`",
                //
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
INNER JOIN `Orders` AS `o0` ON `o`.`OrderID` = `o0`.`OrderID`
WHERE `o0`.`OrderID` IN (10643, 10692, 10702, 10835, 10952, 11011)");
        }

        public override async Task Where_array_index(bool isAsync)
        {
            await base.Where_array_index(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='ALFKI' (Size = 5)")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` = {AssertSqlHelper.Parameter("@__p_0")}");
        }

        public override async Task Where_multiple_contains_in_subquery_with_or(bool isAsync)
        {
            await base.Where_multiple_contains_in_subquery_with_or(isAsync);

            // issue #15994
//            AssertSql(
//                $@"SELECT `od`.`OrderID`, `od`.`ProductID`, `od`.`Discount`, `od`.`Quantity`, `od`.`UnitPrice`
//FROM `Order Details` AS `od`
//WHERE `od`.`ProductID` IN (
//    SELECT TOP 1 `p`.`ProductID`
//    FROM `Products` AS `p`
//    ORDER BY `p`.`ProductID`
//) OR `od`.`OrderID` IN (
//    SELECT TOP 1 `o`.`OrderID`
//    FROM `Orders` AS `o`
//    ORDER BY `o`.`OrderID`
//)");
        }

        public override async Task Where_multiple_contains_in_subquery_with_and(bool isAsync)
        {
            await base.Where_multiple_contains_in_subquery_with_and(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE `o`.`ProductID` IN (
    SELECT TOP 20 `p`.`ProductID`
    FROM `Products` AS `p`
    ORDER BY `p`.`ProductID`
)
 AND `o`.`OrderID` IN (
    SELECT TOP 10 `o0`.`OrderID`
    FROM `Orders` AS `o0`
    ORDER BY `o0`.`OrderID`
)");
        }

        public override async Task Where_contains_on_navigation(bool isAsync)
        {
            await base.Where_contains_on_navigation(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE EXISTS (
    SELECT 1
    FROM `Customers` AS `c`
    WHERE `o`.`OrderID` IN (
        SELECT `o0`.`OrderID`
        FROM `Orders` AS `o0`
        WHERE `c`.`CustomerID` = `o0`.`CustomerID`
    )
)");
        }

        public override async Task Where_subquery_FirstOrDefault_is_null(bool isAsync)
        {
            await base.Where_subquery_FirstOrDefault_is_null(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE (
    SELECT TOP 1 `o`.`OrderID`
    FROM `Orders` AS `o`
    WHERE `c`.`CustomerID` = `o`.`CustomerID`
    ORDER BY `o`.`OrderID`) IS NULL");
        }

        public override async Task Where_subquery_FirstOrDefault_compared_to_entity(bool isAsync)
        {
            await base.Where_subquery_FirstOrDefault_compared_to_entity(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE (
    SELECT TOP 1 `o`.`OrderID`
    FROM `Orders` AS `o`
    WHERE `c`.`CustomerID` = `o`.`CustomerID`
    ORDER BY `o`.`OrderID`) = 10243");
        }

        public override async Task Time_of_day_datetime(bool isAsync)
        {
            await base.Time_of_day_datetime(isAsync);

            AssertSql(
                $@"SELECT CAST(`o`.`OrderDate` AS time)
FROM `Orders` AS `o`");
        }

        public override async Task TypeBinary_short_circuit(bool isAsync)
        {
            await base.TypeBinary_short_circuit(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='False'")}

SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE {AssertSqlHelper.Parameter("@__p_0")} = True");
        }

        public override async Task Where_is_conditional(bool isAsync)
        {
            await base.Where_is_conditional(isAsync);

            AssertSql(
                $@"SELECT `p`.`ProductID`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice`, `p`.`UnitsInStock`
FROM `Products` AS `p`
WHERE CASE
    WHEN True = True THEN False
    ELSE True
END = True");
        }

        public override async Task Enclosing_class_settable_member_generates_parameter(bool isAsync)
        {
            await base.Enclosing_class_settable_member_generates_parameter(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__SettableProperty_0='4'")}

SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`OrderID` = {AssertSqlHelper.Parameter("@__SettableProperty_0")}",
                //
                $@"{AssertSqlHelper.Declaration("@__SettableProperty_0='10'")}

SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`OrderID` = {AssertSqlHelper.Parameter("@__SettableProperty_0")}");
        }

        public override async Task Enclosing_class_readonly_member_generates_parameter(bool isAsync)
        {
            await base.Enclosing_class_readonly_member_generates_parameter(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__ReadOnlyProperty_0='5'")}

SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`OrderID` = {AssertSqlHelper.Parameter("@__ReadOnlyProperty_0")}");
        }

        public override async Task Enclosing_class_const_member_does_not_generate_parameter(bool isAsync)
        {
            await base.Enclosing_class_const_member_does_not_generate_parameter(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`OrderID` = 1");
        }

        public override async Task Generic_Ilist_contains_translates_to_server(bool isAsync)
        {
            await base.Generic_Ilist_contains_translates_to_server(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`City` IN ('Seattle')");
        }
        
        public override async Task Filter_non_nullable_value_after_FirstOrDefault_on_empty_collection(bool isAsync)
        {
            await base.Filter_non_nullable_value_after_FirstOrDefault_on_empty_collection(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE (
    SELECT TOP 1 CAST(LEN(`o`.`CustomerID`) AS int)
    FROM `Orders` AS `o`
    WHERE `o`.`CustomerID` = 'John Doe') = 0");
        }

        public override async Task Like_with_non_string_column_using_ToString(bool isAsync)
        {
            await base.Like_with_non_string_column_using_ToString(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE CONVERT(VARCHAR(11), `o`.`OrderID`) LIKE '%20%'");
        }

        public override async Task Like_with_non_string_column_using_double_cast(bool isAsync)
        {
            await base.Like_with_non_string_column_using_double_cast(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE CAST(`o`.`OrderID` AS nvarchar(max)) LIKE '%20%'");
        }

        private void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);

        protected override void ClearLog()
            => Fixture.TestSqlLoggerFactory.Clear();
    }
}
