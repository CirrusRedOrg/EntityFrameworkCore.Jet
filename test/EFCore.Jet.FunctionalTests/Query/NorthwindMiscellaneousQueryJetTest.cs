// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestModels.Northwind;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;

// ReSharper disable UnusedParameter.Local
// ReSharper disable InconsistentNaming
namespace EntityFrameworkCore.Jet.FunctionalTests.Query
{
    public partial class NorthwindMiscellaneousQueryJetTest : NorthwindMiscellaneousQueryRelationalTestBase<NorthwindQueryJetFixture<NoopModelCustomizer>>
    {
        public NorthwindMiscellaneousQueryJetTest(NorthwindQueryJetFixture<NoopModelCustomizer> fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            ClearLog();
            //Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
        }

        public override void Shaper_command_caching_when_parameter_names_different()
        {
            base.Shaper_command_caching_when_parameter_names_different();

            AssertSql(
                $@"SELECT COUNT(*)
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` = 'ALFKI'",
                //
                $@"SELECT COUNT(*)
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` = 'ALFKI'");
        }

        public override void Can_convert_manually_build_expression_with_default()
        {
            base.Can_convert_manually_build_expression_with_default();

            // issue #15994
//            AssertSql(
//                $@"SELECT COUNT(*)
//FROM `Customers` AS `c`
//WHERE `c`.`CustomerID` IS NOT NULL",
//                //
//                $@"SELECT COUNT(*)
//FROM `Customers` AS `c`
//WHERE `c`.`CustomerID` IS NOT NULL");
        }

        public override void Lifting_when_subquery_nested_order_by_anonymous()
        {
            base.Lifting_when_subquery_nested_order_by_anonymous();

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='2'")}

SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
INNER JOIN (
    SELECT DISTINCT `t`.`CustomerID`
    FROM (
        SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
        FROM `Customers` AS `c`
        ORDER BY `c`.`CustomerID`
    ) AS `t`,
`Customers` AS `c0`
) AS `t0` ON `o`.`CustomerID` = `t0`.`CustomerID`
ORDER BY `t0`.`CustomerID`");
        }

        public override void Lifting_when_subquery_nested_order_by_simple()
        {
            base.Lifting_when_subquery_nested_order_by_simple();

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='2'")}

SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
INNER JOIN (
    SELECT DISTINCT `t`.`CustomerID`
    FROM (
        SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
        FROM `Customers` AS `c`
        ORDER BY `c`.`CustomerID`
    ) AS `t`,
`Customers` AS `c0`
) AS `t0` ON `o`.`CustomerID` = `t0`.`CustomerID`
ORDER BY `t0`.`CustomerID`");
        }

        [ConditionalFact(Skip = "Issue #16006")]
        public virtual void Cache_key_contexts_are_detached()
        {
            var weakRef = Scoper(
                () =>
                {
                    var context = new NorthwindRelationalContext(Fixture.CreateOptions());

                    var wr = new WeakReference(context);

                    using (context)
                    {
                        var orderDetails = context.OrderDetails;

                        Customer Query(NorthwindContext param) =>
                            (from c in context.Customers
                             from o in context.Set<Order>()
                             from od in orderDetails
                             from e1 in param.Employees
                             from e2 in param.Set<Order>()
                             select c).First();

                        Assert.NotNull(Query(context));

                        Assert.True(wr.IsAlive);

                        return wr;
                    }
                });

            GC.Collect();

            Assert.False(weakRef.IsAlive);
        }

        private static T Scoper<T>(Func<T> getter)
        {
            return getter();
        }

        public override async Task Local_dictionary(bool isAsync)
        {
            await base.Local_dictionary(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='ALFKI' (Size = 5)")}

SELECT TOP 2 `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` = {AssertSqlHelper.Parameter("@__p_0")}");
        }

        public override async Task Entity_equality_self(bool isAsync)
        {
            await base.Entity_equality_self(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`
FROM `Customers` AS `c`");
        }

        public override async Task Entity_equality_local(bool isAsync)
        {
            await base.Entity_equality_local(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__entity_equality_local_0_CustomerID='ANATR' (Size = 5)")}

SELECT `c`.`CustomerID`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` = {AssertSqlHelper.Parameter("@__entity_equality_local_0_CustomerID")}");
        }

        public override async Task Entity_equality_local_composite_key(bool isAsync)
        {
            await base.Entity_equality_local_composite_key(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__entity_equality_local_0_OrderID='10248' (Nullable = true)")}

{AssertSqlHelper.Declaration("@__entity_equality_local_0_ProductID='11' (Nullable = true)")}

SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE (`o`.`OrderID` = {AssertSqlHelper.Parameter("@__entity_equality_local_0_OrderID")}) AND (`o`.`ProductID` = {AssertSqlHelper.Parameter("@__entity_equality_local_0_ProductID")})");
        }

        public override async Task Entity_equality_local_double_check(bool isAsync)
        {
            await base.Entity_equality_local_double_check(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__entity_equality_local_0_CustomerID='ANATR' (Size = 5)")}

SELECT `c`.`CustomerID`
FROM `Customers` AS `c`
WHERE (`c`.`CustomerID` = {AssertSqlHelper.Parameter("@__entity_equality_local_0_CustomerID")}) AND ({AssertSqlHelper.Parameter("@__entity_equality_local_0_CustomerID")} = `c`.`CustomerID`)");
        }

        public override async Task Join_with_entity_equality_local_on_both_sources(bool isAsync)
        {
            await base.Join_with_entity_equality_local_on_both_sources(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__entity_equality_local_0_CustomerID='ANATR' (Size = 5)")}

SELECT `c`.`CustomerID`
FROM `Customers` AS `c`
INNER JOIN (
    SELECT `c0`.`CustomerID`, `c0`.`Address`, `c0`.`City`, `c0`.`CompanyName`, `c0`.`ContactName`, `c0`.`ContactTitle`, `c0`.`Country`, `c0`.`Fax`, `c0`.`Phone`, `c0`.`PostalCode`, `c0`.`Region`
    FROM `Customers` AS `c0`
    WHERE `c0`.`CustomerID` = {AssertSqlHelper.Parameter("@__entity_equality_local_0_CustomerID")}
) AS `t` ON `c`.`CustomerID` = `t`.`CustomerID`
WHERE `c`.`CustomerID` = {AssertSqlHelper.Parameter("@__entity_equality_local_0_CustomerID")}");
        }

        public override async Task Entity_equality_local_inline(bool isAsync)
        {
            await base.Entity_equality_local_inline(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` = 'ANATR'");
        }

        public override async Task Entity_equality_local_inline_composite_key(bool isAsync)
        {
            await base.Entity_equality_local_inline_composite_key(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE (`o`.`OrderID` = 10248) AND (`o`.`ProductID` = 11)");
        }

        public override async Task Entity_equality_null(bool isAsync)
        {
            await base.Entity_equality_null(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`
FROM `Customers` AS `c`
WHERE False = True");
        }

        public override async Task Entity_equality_null_composite_key(bool isAsync)
        {
            await base.Entity_equality_null_composite_key(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE False = True");
        }

        public override async Task Entity_equality_not_null(bool isAsync)
        {
            await base.Entity_equality_not_null(isAsync);

            // issue #15994
//            AssertSql(
//                $@"SELECT `c`.`CustomerID`
//FROM `Customers` AS `c`
//WHERE `c`.`CustomerID` IS NOT NULL");
        }

        public override async Task Entity_equality_not_null_composite_key(bool isAsync)
        {
            await base.Entity_equality_not_null_composite_key(isAsync);

            // issue #15994
//            AssertSql(
//                $@"SELECT `o`.`ProductID`
//FROM `Order Details` AS `o`
//WHERE True = True");
        }

        public override async Task Entity_equality_through_nested_anonymous_type_projection(bool isAsync)
        {
            await base.Entity_equality_through_nested_anonymous_type_projection(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Orders` AS `o`
LEFT JOIN `Customers` AS `c` ON `o`.`CustomerID` = `c`.`CustomerID`
WHERE `c`.`CustomerID` IS NOT NULL");
        }

        public override async Task Entity_equality_through_DTO_projection(bool isAsync)
        {
            await base.Entity_equality_through_DTO_projection(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Orders` AS `o`
LEFT JOIN `Customers` AS `c` ON `o`.`CustomerID` = `c`.`CustomerID`
WHERE `c`.`CustomerID` IS NOT NULL");
        }

        public override async Task Entity_equality_through_subquery(bool isAsync)
        {
            await base.Entity_equality_through_subquery(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`
FROM `Customers` AS `c`
WHERE (
    SELECT TOP 1 `o`.`OrderID`
    FROM `Orders` AS `o`
    WHERE `c`.`CustomerID` = `o`.`CustomerID`) IS NOT NULL");
        }

        public override async Task Entity_equality_through_include(bool isAsync)
        {
            await base.Entity_equality_through_include(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`
FROM `Customers` AS `c`
WHERE False = True");
        }

        public override async Task Entity_equality_orderby(bool isAsync)
        {
            await base.Entity_equality_orderby(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
ORDER BY `c`.`CustomerID`");
        }

        public override async Task Entity_equality_orderby_descending_composite_key(bool isAsync)
        {
            await base.Entity_equality_orderby_descending_composite_key(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
ORDER BY `o`.`OrderID` DESC, `o`.`ProductID` DESC");
        }

        public override async Task Default_if_empty_top_level(bool isAsync)
        {
            await base.Default_if_empty_top_level(isAsync);

            AssertSql(
                $@"SELECT `t`.`EmployeeID`, `t`.`City`, `t`.`Country`, `t`.`FirstName`, `t`.`ReportsTo`, `t`.`Title`
FROM (
    SELECT NULL AS `empty`
) AS `empty`
LEFT JOIN (
    SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
    FROM `Employees` AS `e`
    WHERE `e`.`EmployeeID` = -1
) AS `t` ON 1 = 1");
        }

        public override async Task Join_with_default_if_empty_on_both_sources(bool isAsync)
        {
            await base.Join_with_default_if_empty_on_both_sources(isAsync);

            AssertSql(
                $@"SELECT `t`.`EmployeeID`, `t`.`City`, `t`.`Country`, `t`.`FirstName`, `t`.`ReportsTo`, `t`.`Title`
FROM (
    SELECT NULL AS `empty`
) AS `empty`
LEFT JOIN (
    SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
    FROM `Employees` AS `e`
    WHERE `e`.`EmployeeID` = -1
) AS `t` ON 1 = 1
INNER JOIN (
    SELECT `t0`.`EmployeeID`, `t0`.`City`, `t0`.`Country`, `t0`.`FirstName`, `t0`.`ReportsTo`, `t0`.`Title`
    FROM (
        SELECT NULL AS `empty`
    ) AS `empty0`
    LEFT JOIN (
        SELECT `e0`.`EmployeeID`, `e0`.`City`, `e0`.`Country`, `e0`.`FirstName`, `e0`.`ReportsTo`, `e0`.`Title`
        FROM `Employees` AS `e0`
        WHERE `e0`.`EmployeeID` = -1
    ) AS `t0` ON 1 = 1
) AS `t1` ON `t`.`EmployeeID` = `t1`.`EmployeeID`");
        }

        public override async Task Default_if_empty_top_level_followed_by_projecting_constant(bool isAsync)
        {
            await base.Default_if_empty_top_level_followed_by_projecting_constant(isAsync);

            AssertSql(
                $@"SELECT 'Foo'
FROM (
    SELECT NULL AS `empty`
) AS `empty`
LEFT JOIN (
    SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
    FROM `Employees` AS `e`
    WHERE `e`.`EmployeeID` = -1
) AS `t` ON 1 = 1");
        }

        public override async Task Default_if_empty_top_level_positive(bool isAsync)
        {
            await base.Default_if_empty_top_level_positive(isAsync);

            AssertSql(
                $@"SELECT `t`.`EmployeeID`, `t`.`City`, `t`.`Country`, `t`.`FirstName`, `t`.`ReportsTo`, `t`.`Title`
FROM (
    SELECT NULL AS `empty`
) AS `empty`
LEFT JOIN (
    SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
    FROM `Employees` AS `e`
    WHERE `e`.`EmployeeID` > 0
) AS `t` ON 1 = 1");
        }

        public override async Task Default_if_empty_top_level_projection(bool isAsync)
        {
            await base.Default_if_empty_top_level_projection(isAsync);

            AssertSql(
                $@"SELECT `t`.`EmployeeID`
FROM (
    SELECT NULL AS `empty`
) AS `empty`
LEFT JOIN (
    SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
    FROM `Employees` AS `e`
    WHERE `e`.`EmployeeID` = -1
) AS `t` ON 1 = 1");
        }

        public override async Task Where_query_composition(bool isAsync)
        {
            await base.Where_query_composition(isAsync);

            // issue #15994
//            AssertSql(
//                $@"SELECT `e1`.`EmployeeID`, `e1`.`City`, `e1`.`Country`, `e1`.`FirstName`, `e1`.`ReportsTo`, `e1`.`Title`
//FROM `Employees` AS `e1`
//WHERE `e1`.`FirstName` = (
//    SELECT TOP 1 `e`.`FirstName`
//    FROM `Employees` AS `e`
//    ORDER BY `e`.`EmployeeID`
//)");
        }

        public override async Task Where_query_composition_is_null(bool isAsync)
        {
            await base.Where_query_composition_is_null(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='3'")}

SELECT `t`.`EmployeeID`, `t`.`City`, `t`.`Country`, `t`.`FirstName`, `t`.`ReportsTo`, `t`.`Title`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
    FROM `Employees` AS `e`
    ORDER BY `e`.`EmployeeID`
) AS `t`
WHERE (
    SELECT TOP 1 `e0`.`EmployeeID`
    FROM `Employees` AS `e0`
    WHERE `e0`.`EmployeeID` = `t`.`ReportsTo`) IS NULL
ORDER BY `t`.`EmployeeID`");
        }

        public override async Task Where_query_composition_is_not_null(bool isAsync)
        {
            await base.Where_query_composition_is_not_null(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='4'")}

{AssertSqlHelper.Declaration("@__p_1='3'")}

SELECT `t`.`EmployeeID`, `t`.`City`, `t`.`Country`, `t`.`FirstName`, `t`.`ReportsTo`, `t`.`Title`
FROM (
    SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
    FROM `Employees` AS `e`
    ORDER BY `e`.`EmployeeID`
    SKIP {AssertSqlHelper.Parameter("@__p_0")} FETCH NEXT {AssertSqlHelper.Parameter("@__p_1")} ROWS ONLY
) AS `t`
WHERE (
    SELECT TOP 1 `e0`.`EmployeeID`
    FROM `Employees` AS `e0`
    WHERE `e0`.`EmployeeID` = `t`.`ReportsTo`) IS NOT NULL
ORDER BY `t`.`EmployeeID`");
        }

        public override async Task Where_query_composition_entity_equality_one_element_SingleOrDefault(bool isAsync)
        {
            await base.Where_query_composition_entity_equality_one_element_SingleOrDefault(isAsync);

            AssertSql(
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE (
    SELECT TOP 1 `e0`.`EmployeeID`
    FROM `Employees` AS `e0`
    WHERE `e0`.`EmployeeID` = `e`.`ReportsTo`) = 0");
        }

        public override async Task Where_query_composition_entity_equality_one_element_Single(bool isAsync)
        {
            await base.Where_query_composition_entity_equality_one_element_Single(isAsync);

            AssertSql(
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE (
    SELECT TOP 1 `e0`.`EmployeeID`
    FROM `Employees` AS `e0`
    WHERE `e0`.`EmployeeID` = `e`.`ReportsTo`) = 0");
        }

        public override async Task Where_query_composition_entity_equality_one_element_FirstOrDefault(bool isAsync)
        {
            await base.Where_query_composition_entity_equality_one_element_FirstOrDefault(isAsync);

            AssertSql(
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE (
    SELECT TOP 1 `e0`.`EmployeeID`
    FROM `Employees` AS `e0`
    WHERE `e0`.`EmployeeID` = `e`.`ReportsTo`) = 0");
        }

        public override async Task Where_query_composition_entity_equality_one_element_First(bool isAsync)
        {
            await base.Where_query_composition_entity_equality_one_element_First(isAsync);

            AssertSql(
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE (
    SELECT TOP 1 `e0`.`EmployeeID`
    FROM `Employees` AS `e0`
    WHERE `e0`.`EmployeeID` = `e`.`ReportsTo`) = 0");
        }

        public override async Task Where_query_composition_entity_equality_no_elements_SingleOrDefault(bool isAsync)
        {
            await base.Where_query_composition_entity_equality_no_elements_SingleOrDefault(isAsync);

            AssertSql(
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE (
    SELECT TOP 1 `e0`.`EmployeeID`
    FROM `Employees` AS `e0`
    WHERE `e0`.`EmployeeID` = 42) = 0");
        }

        public override async Task Where_query_composition_entity_equality_no_elements_Single(bool isAsync)
        {
            await base.Where_query_composition_entity_equality_no_elements_Single(isAsync);

            AssertSql(
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE (
    SELECT TOP 1 `e0`.`EmployeeID`
    FROM `Employees` AS `e0`
    WHERE `e0`.`EmployeeID` = 42) = 0");
        }

        public override async Task Where_query_composition_entity_equality_no_elements_FirstOrDefault(bool isAsync)
        {
            await base.Where_query_composition_entity_equality_no_elements_FirstOrDefault(isAsync);

            AssertSql(
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE (
    SELECT TOP 1 `e0`.`EmployeeID`
    FROM `Employees` AS `e0`
    WHERE `e0`.`EmployeeID` = 42) = 0");
        }

        public override async Task Where_query_composition_entity_equality_no_elements_First(bool isAsync)
        {
            await base.Where_query_composition_entity_equality_no_elements_First(isAsync);

            AssertSql(
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE (
    SELECT TOP 1 `e0`.`EmployeeID`
    FROM `Employees` AS `e0`
    WHERE `e0`.`EmployeeID` = 42) = 0");
        }

        public override async Task Where_query_composition_entity_equality_multiple_elements_SingleOrDefault(bool isAsync)
        {
            await base.Where_query_composition_entity_equality_multiple_elements_SingleOrDefault(isAsync);

            AssertSql(
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE (
    SELECT TOP 1 `e0`.`EmployeeID`
    FROM `Employees` AS `e0`
    WHERE (`e0`.`EmployeeID` <> `e`.`ReportsTo`) OR `e`.`ReportsTo` IS NULL) = 0");
        }

        public override async Task Where_query_composition_entity_equality_multiple_elements_Single(bool isAsync)
        {
            await base.Where_query_composition_entity_equality_multiple_elements_Single(isAsync);

            AssertSql(
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE (
    SELECT TOP 1 `e0`.`EmployeeID`
    FROM `Employees` AS `e0`
    WHERE (`e0`.`EmployeeID` <> `e`.`ReportsTo`) OR `e`.`ReportsTo` IS NULL) = 0");
        }

        public override async Task Where_query_composition_entity_equality_multiple_elements_FirstOrDefault(bool isAsync)
        {
            await base.Where_query_composition_entity_equality_multiple_elements_FirstOrDefault(isAsync);

            AssertSql(
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE (
    SELECT TOP 1 `e0`.`EmployeeID`
    FROM `Employees` AS `e0`
    WHERE (`e0`.`EmployeeID` <> `e`.`ReportsTo`) OR `e`.`ReportsTo` IS NULL) = 0");
        }

        public override async Task Where_query_composition_entity_equality_multiple_elements_First(bool isAsync)
        {
            await base.Where_query_composition_entity_equality_multiple_elements_First(isAsync);

            AssertSql(
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE (
    SELECT TOP 1 `e0`.`EmployeeID`
    FROM `Employees` AS `e0`
    WHERE (`e0`.`EmployeeID` <> `e`.`ReportsTo`) OR `e`.`ReportsTo` IS NULL) = 0");
        }

        public override async Task Where_query_composition2(bool isAsync)
        {
            await base.Where_query_composition2(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='3'")}

SELECT `t`.`EmployeeID`, `t`.`City`, `t`.`Country`, `t`.`FirstName`, `t`.`ReportsTo`, `t`.`Title`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
    FROM `Employees` AS `e`
) AS `t`
WHERE (`t`.`FirstName` = (
    SELECT TOP 1 `e0`.`FirstName`
    FROM `Employees` AS `e0`
    ORDER BY `e0`.`EmployeeID`)) OR (`t`.`FirstName` IS NULL AND (
    SELECT TOP 1 `e0`.`FirstName`
    FROM `Employees` AS `e0`
    ORDER BY `e0`.`EmployeeID`) IS NULL)");
        }

        public override async Task Where_query_composition2_FirstOrDefault(bool isAsync)
        {
            await base.Where_query_composition2_FirstOrDefault(isAsync);

            // issue #15994
//            AssertSql(
//                $@"{AssertSqlHelper.Declaration("@__p_0='3'")}

//SELECT `t`.`EmployeeID`, `t`.`City`, `t`.`Country`, `t`.`FirstName`, `t`.`ReportsTo`, `t`.`Title`
//FROM (
//    SELECT TOP @__p_0 `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
//    FROM `Employees` AS `e`
//) AS `t`
//WHERE `t`.`FirstName` = (
//    SELECT TOP 1 `e0`.`FirstName`
//    FROM `Employees` AS `e0`
//    ORDER BY `e0`.`EmployeeID`
//)");
        }

        public override async Task Where_query_composition2_FirstOrDefault_with_anonymous(bool isAsync)
        {
            await base.Where_query_composition2_FirstOrDefault_with_anonymous(isAsync);

            // issue #15994
//            AssertSql(
//                $@"{AssertSqlHelper.Declaration("@__p_0='3'")}

//SELECT `t`.`EmployeeID`, `t`.`City`, `t`.`Country`, `t`.`FirstName`, `t`.`ReportsTo`, `t`.`Title`
//FROM (
//    SELECT TOP @__p_0 `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
//    FROM `Employees` AS `e`
//) AS `t`
//WHERE `t`.`FirstName` = (
//    SELECT TOP 1 `e0`.`FirstName`
//    FROM `Employees` AS `e0`
//    ORDER BY `e0`.`EmployeeID`
//)");
        }

        public override void Select_Subquery_Single()
        {
            base.Select_Subquery_Single();

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='2'")}

SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `od`.`OrderID`
FROM `Order Details` AS `od`
ORDER BY `od`.`ProductID`, `od`.`OrderID`",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_OrderID='10285'")}

SELECT TOP 1 `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE {AssertSqlHelper.Parameter("@_outer_OrderID")} = `o`.`OrderID`
ORDER BY `o`.`OrderID`",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_OrderID='10294'")}

SELECT TOP 1 `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE {AssertSqlHelper.Parameter("@_outer_OrderID")} = `o`.`OrderID`
ORDER BY `o`.`OrderID`");
        }

        public override void Select_Where_Subquery_Deep_Single()
        {
            base.Select_Where_Subquery_Deep_Single();

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='2'")}

SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE (`o`.`OrderID` = 10344) AND ((
    SELECT TOP 1 (
        SELECT TOP 1 `c`.`City`
        FROM `Customers` AS `c`
        WHERE `o0`.`CustomerID` = `c`.`CustomerID`)
    FROM `Orders` AS `o0`
    WHERE `o`.`OrderID` = `o0`.`OrderID`) = 'Seattle')");
        }

        public override void Select_Where_Subquery_Deep_First()
        {
            base.Select_Where_Subquery_Deep_First();

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='2'")}

SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `o`.`OrderID`, `o`.`ProductID`, `o`.`Discount`, `o`.`Quantity`, `o`.`UnitPrice`
FROM `Order Details` AS `o`
WHERE (
    SELECT TOP 1 (
        SELECT TOP 1 `c`.`City`
        FROM `Customers` AS `c`
        WHERE `o0`.`CustomerID` = `c`.`CustomerID`)
    FROM `Orders` AS `o0`
    WHERE `o`.`OrderID` = `o0`.`OrderID`) = 'Seattle'");
        }

        public override void Select_Where_Subquery_Equality()
        {
            base.Select_Where_Subquery_Equality();

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='1'")}

SELECT `t`.`OrderID`, `t`.`CustomerID`, `t`.`EmployeeID`, `t`.`OrderDate`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
    FROM `Orders` AS `o`
    ORDER BY `o`.`OrderID`
) AS `t`
WHERE (
    SELECT COUNT(*)
    FROM (
        SELECT TOP 2 `o0`.`OrderID`, `o0`.`ProductID`, `o0`.`Discount`, `o0`.`Quantity`, `o0`.`UnitPrice`
        FROM `Order Details` AS `o0`
        ORDER BY `o0`.`OrderID`
    ) AS `t0`
    WHERE ((
        SELECT TOP 1 `c`.`Country`
        FROM `Customers` AS `c`
        WHERE `c`.`CustomerID` = `t`.`CustomerID`
        ORDER BY `c`.`CustomerID`) = (
        SELECT TOP 1 `c0`.`Country`
        FROM `Orders` AS `o1`
        INNER JOIN `Customers` AS `c0` ON `o1`.`CustomerID` = `c0`.`CustomerID`
        WHERE `o1`.`OrderID` = `t0`.`OrderID`
        ORDER BY `o1`.`OrderID`, `c0`.`CustomerID`)) OR ((
        SELECT TOP 1 `c`.`Country`
        FROM `Customers` AS `c`
        WHERE `c`.`CustomerID` = `t`.`CustomerID`
        ORDER BY `c`.`CustomerID`) IS NULL AND (
        SELECT TOP 1 `c0`.`Country`
        FROM `Orders` AS `o1`
        INNER JOIN `Customers` AS `c0` ON `o1`.`CustomerID` = `c0`.`CustomerID`
        WHERE `o1`.`OrderID` = `t0`.`OrderID`
        ORDER BY `o1`.`OrderID`, `c0`.`CustomerID`) IS NULL)) > 0
ORDER BY `t`.`OrderID`");
        }

        public override async Task Where_subquery_anon(bool isAsync)
        {
            await base.Where_subquery_anon(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='3'")}

SELECT `t`.`EmployeeID`, `t`.`City`, `t`.`Country`, `t`.`FirstName`, `t`.`ReportsTo`, `t`.`Title`, `t0`.`OrderID`, `t0`.`CustomerID`, `t0`.`EmployeeID`, `t0`.`OrderDate`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
    FROM `Employees` AS `e`
    ORDER BY `e`.`EmployeeID`
) AS `t`,
(
    SELECT TOP 5 `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
    FROM `Orders` AS `o`
    ORDER BY `o`.`OrderID`
) AS `t0`
WHERE `t`.`EmployeeID` = `t0`.`EmployeeID`
ORDER BY `t`.`EmployeeID`");
        }

        public override async Task Where_subquery_anon_nested(bool isAsync)
        {
            await base.Where_subquery_anon_nested(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='3'")}

{AssertSqlHelper.Declaration("@__p_1='2'")}

SELECT `t`.`EmployeeID`, `t`.`City`, `t`.`Country`, `t`.`FirstName`, `t`.`ReportsTo`, `t`.`Title`, `t0`.`OrderID`, `t0`.`CustomerID`, `t0`.`EmployeeID`, `t0`.`OrderDate`, `t1`.`CustomerID`, `t1`.`Address`, `t1`.`City`, `t1`.`CompanyName`, `t1`.`ContactName`, `t1`.`ContactTitle`, `t1`.`Country`, `t1`.`Fax`, `t1`.`Phone`, `t1`.`PostalCode`, `t1`.`Region`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
    FROM `Employees` AS `e`
    ORDER BY `e`.`EmployeeID`
) AS `t`,
(
    SELECT TOP 5 `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
    FROM `Orders` AS `o`
    ORDER BY `o`.`OrderID`
) AS `t0`,
(
    SELECT TOP {AssertSqlHelper.Parameter("@__p_1")} `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
) AS `t1`
WHERE `t`.`City` = 'Seattle'
ORDER BY `t`.`EmployeeID`");
        }

        public override async Task OrderBy_SelectMany(bool isAsync)
        {
            await base.OrderBy_SelectMany(isAsync);

            AssertSql(
                $@"SELECT `c`.`ContactName`, `t`.`OrderID`
FROM `Customers` AS `c`,
(
    SELECT TOP 3 `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
    FROM `Orders` AS `o`
    ORDER BY `o`.`OrderID`
) AS `t`
WHERE `c`.`CustomerID` = `t`.`CustomerID`
ORDER BY `c`.`CustomerID`");
        }

        public override async Task Let_any_subquery_anonymous(bool isAsync)
        {
            await base.Let_any_subquery_anonymous(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, CASE
    WHEN EXISTS (
        SELECT 1
        FROM `Orders` AS `o`
        WHERE `o`.`CustomerID` = `c`.`CustomerID`) THEN True
    ELSE False
END AS `hasOrders`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` LIKE 'A' & '%'
ORDER BY `c`.`CustomerID`");
        }

        public override async Task OrderBy_arithmetic(bool isAsync)
        {
            await base.OrderBy_arithmetic(isAsync);

            AssertSql(
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
ORDER BY `e`.`EmployeeID` - `e`.`EmployeeID`");
        }

        public override async Task OrderBy_condition_comparison(bool isAsync)
        {
            await base.OrderBy_condition_comparison(isAsync);

            AssertSql(
                $@"SELECT `p`.`ProductID`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice`, `p`.`UnitsInStock`
FROM `Products` AS `p`
ORDER BY IIF(`p`.`UnitsInStock` > 0, 1, 0), `p`.`ProductID`");
        }

        public override async Task OrderBy_ternary_conditions(bool isAsync)
        {
            await base.OrderBy_ternary_conditions(isAsync);

            // issue #15994
//            AssertSql(
//                $@"SELECT `p`.`ProductID`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice`, `p`.`UnitsInStock`
//FROM `Products` AS `p`
//ORDER BY CASE
//    WHEN ((`p`.`UnitsInStock` > 10) AND (`p`.`ProductID` > 40)) OR ((`p`.`UnitsInStock` <= 10) AND (`p`.`ProductID` <= 40))
//    THEN True ELSE False
//END, `p`.`ProductID`");
        }

        public override void OrderBy_any()
        {
            base.OrderBy_any();

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
ORDER BY CASE
    WHEN EXISTS (
        SELECT 1
        FROM `Orders` AS `o`
        WHERE (`c`.`CustomerID` = `o`.`CustomerID`) AND (`o`.`OrderID` > 11000)) THEN True
    ELSE False
END, `c`.`CustomerID`");
        }

        public override async Task Skip(bool isAsync)
        {
            await base.Skip(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='5'")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
ORDER BY `c`.`CustomerID`
SKIP {AssertSqlHelper.Parameter("@__p_0")}");
        }

        public override async Task Skip_no_orderby(bool isAsync)
        {
            await base.Skip_no_orderby(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='5'")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
ORDER BY (SELECT 1)
SKIP {AssertSqlHelper.Parameter("@__p_0")}");
        }

        public override async Task Skip_orderby_const(bool isAsync)
        {
            await base.Skip_orderby_const(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='5'")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
ORDER BY (SELECT 1)
SKIP {AssertSqlHelper.Parameter("@__p_0")}");
        }

        public override async Task Skip_Take(bool isAsync)
        {
            await base.Skip_Take(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='5'")}

{AssertSqlHelper.Declaration("@__p_1='10'")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
ORDER BY `c`.`ContactName`
SKIP {AssertSqlHelper.Parameter("@__p_0")} FETCH NEXT {AssertSqlHelper.Parameter("@__p_1")} ROWS ONLY");
        }

        public override async Task Join_Customers_Orders_Skip_Take(bool isAsync)
        {
            await base.Join_Customers_Orders_Skip_Take(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='10'")}

{AssertSqlHelper.Declaration("@__p_1='5'")}

SELECT `c`.`ContactName`, `o`.`OrderID`
FROM `Customers` AS `c`
INNER JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`
ORDER BY `o`.`OrderID`
SKIP {AssertSqlHelper.Parameter("@__p_0")} FETCH NEXT {AssertSqlHelper.Parameter("@__p_1")} ROWS ONLY");
        }

        public override async Task Join_Customers_Orders_Skip_Take_followed_by_constant_projection(bool isAsync)
        {
            await base.Join_Customers_Orders_Skip_Take_followed_by_constant_projection(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='10'")}

{AssertSqlHelper.Declaration("@__p_1='5'")}

SELECT 'Foo'
FROM `Customers` AS `c`
INNER JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`
ORDER BY `o`.`OrderID`
SKIP {AssertSqlHelper.Parameter("@__p_0")} FETCH NEXT {AssertSqlHelper.Parameter("@__p_1")} ROWS ONLY");
        }

        public override async Task Join_Customers_Orders_Projection_With_String_Concat_Skip_Take(bool isAsync)
        {
            await base.Join_Customers_Orders_Projection_With_String_Concat_Skip_Take(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='10'")}

{AssertSqlHelper.Declaration("@__p_1='5'")}

SELECT (`c`.`ContactName` + ' ') + `c`.`ContactTitle` AS `Contact`, `o`.`OrderID`
FROM `Customers` AS `c`
INNER JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`
ORDER BY `o`.`OrderID`
SKIP {AssertSqlHelper.Parameter("@__p_0")} FETCH NEXT {AssertSqlHelper.Parameter("@__p_1")} ROWS ONLY");
        }

        public override async Task Join_Customers_Orders_Orders_Skip_Take_Same_Properties(bool isAsync)
        {
            await base.Join_Customers_Orders_Orders_Skip_Take_Same_Properties(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='10'")}

{AssertSqlHelper.Declaration("@__p_1='5'")}

SELECT `o`.`OrderID`, `c`.`CustomerID` AS `CustomerIDA`, `c0`.`CustomerID` AS `CustomerIDB`, `c`.`ContactName` AS `ContactNameA`, `c0`.`ContactName` AS `ContactNameB`
FROM `Orders` AS `o`
INNER JOIN `Customers` AS `c` ON `o`.`CustomerID` = `c`.`CustomerID`
INNER JOIN `Customers` AS `c0` ON `o`.`CustomerID` = `c0`.`CustomerID`
ORDER BY `o`.`OrderID`
SKIP {AssertSqlHelper.Parameter("@__p_0")} FETCH NEXT {AssertSqlHelper.Parameter("@__p_1")} ROWS ONLY");
        }

        public override async Task Take_Skip(bool isAsync)
        {
            await base.Take_Skip(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='10'")}

{AssertSqlHelper.Declaration("@__p_1='5'")}

SELECT `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    ORDER BY `c`.`ContactName`
) AS `t`
ORDER BY `t`.`ContactName`
SKIP {AssertSqlHelper.Parameter("@__p_1")}");
        }

        public override async Task Take_Skip_Distinct(bool isAsync)
        {
            await base.Take_Skip_Distinct(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='10'")}

{AssertSqlHelper.Declaration("@__p_1='5'")}

SELECT DISTINCT `t0`.`CustomerID`, `t0`.`Address`, `t0`.`City`, `t0`.`CompanyName`, `t0`.`ContactName`, `t0`.`ContactTitle`, `t0`.`Country`, `t0`.`Fax`, `t0`.`Phone`, `t0`.`PostalCode`, `t0`.`Region`
FROM (
    SELECT `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`
    FROM (
        SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
        FROM `Customers` AS `c`
        ORDER BY `c`.`ContactName`
    ) AS `t`
    ORDER BY `t`.`ContactName`
    SKIP {AssertSqlHelper.Parameter("@__p_1")}
) AS `t0`");
        }

        public override async Task Take_Skip_Distinct_Caching(bool isAsync)
        {
            await base.Take_Skip_Distinct_Caching(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='10'")}

{AssertSqlHelper.Declaration("@__p_1='5'")}

SELECT DISTINCT `t0`.`CustomerID`, `t0`.`Address`, `t0`.`City`, `t0`.`CompanyName`, `t0`.`ContactName`, `t0`.`ContactTitle`, `t0`.`Country`, `t0`.`Fax`, `t0`.`Phone`, `t0`.`PostalCode`, `t0`.`Region`
FROM (
    SELECT `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`
    FROM (
        SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
        FROM `Customers` AS `c`
        ORDER BY `c`.`ContactName`
    ) AS `t`
    ORDER BY `t`.`ContactName`
    SKIP {AssertSqlHelper.Parameter("@__p_1")}
) AS `t0`",
                //
                $@"{AssertSqlHelper.Declaration("@__p_0='15'")}

{AssertSqlHelper.Declaration("@__p_1='10'")}

SELECT DISTINCT `t0`.`CustomerID`, `t0`.`Address`, `t0`.`City`, `t0`.`CompanyName`, `t0`.`ContactName`, `t0`.`ContactTitle`, `t0`.`Country`, `t0`.`Fax`, `t0`.`Phone`, `t0`.`PostalCode`, `t0`.`Region`
FROM (
    SELECT `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`
    FROM (
        SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
        FROM `Customers` AS `c`
        ORDER BY `c`.`ContactName`
    ) AS `t`
    ORDER BY `t`.`ContactName`
    SKIP {AssertSqlHelper.Parameter("@__p_1")}
) AS `t0`");
        }

        public override async Task Take_Distinct_Count(bool isAsync)
        {
            await base.Take_Distinct_Count(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='5'")}

SELECT COUNT(*)
FROM (
    SELECT DISTINCT `t`.`OrderID`, `t`.`CustomerID`, `t`.`EmployeeID`, `t`.`OrderDate`
    FROM (
        SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
        FROM `Orders` AS `o`
    ) AS `t`
) AS `t0`");
        }

        public override async Task Take_Where_Distinct_Count(bool isAsync)
        {
            await base.Take_Where_Distinct_Count(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='5'")}

SELECT COUNT(*)
FROM (
    SELECT DISTINCT `t`.`OrderID`, `t`.`CustomerID`, `t`.`EmployeeID`, `t`.`OrderDate`
    FROM (
        SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
        FROM `Orders` AS `o`
        WHERE `o`.`CustomerID` = 'FRANK'
    ) AS `t`
) AS `t0`");
        }

        public override async Task Queryable_simple(bool isAsync)
        {
            await base.Queryable_simple(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`");
        }

        public override async Task Queryable_simple_anonymous(bool isAsync)
        {
            await base.Queryable_simple_anonymous(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`");
        }

        public override async Task Queryable_nested_simple(bool isAsync)
        {
            await base.Queryable_nested_simple(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`");
        }

        public override async Task Queryable_simple_anonymous_projection_subquery(bool isAsync)
        {
            await base.Queryable_simple_anonymous_projection_subquery(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='91'")}

SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`City`
FROM `Customers` AS `c`");
        }

        public override async Task Queryable_simple_anonymous_subquery(bool isAsync)
        {
            await base.Queryable_simple_anonymous_subquery(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='91'")}

SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`");
        }

        public override async Task Take_simple(bool isAsync)
        {
            await base.Take_simple(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='10'")}

SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
ORDER BY `c`.`CustomerID`");
        }

        public override async Task Take_simple_parameterized(bool isAsync)
        {
            await base.Take_simple_parameterized(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='10'")}

SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
ORDER BY `c`.`CustomerID`");
        }

        public override async Task Take_simple_projection(bool isAsync)
        {
            await base.Take_simple_projection(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='10'")}

SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`City`
FROM `Customers` AS `c`
ORDER BY `c`.`CustomerID`");
        }

        public override async Task Take_subquery_projection(bool isAsync)
        {
            await base.Take_subquery_projection(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='2'")}

SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`City`
FROM `Customers` AS `c`
ORDER BY `c`.`CustomerID`");
        }

        public override async Task OrderBy_Take_Count(bool isAsync)
        {
            await base.OrderBy_Take_Count(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='5'")}

SELECT COUNT(*)
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
    FROM `Orders` AS `o`
    ORDER BY `o`.`OrderID`
) AS `t`");
        }

        public override async Task Take_OrderBy_Count(bool isAsync)
        {
            await base.Take_OrderBy_Count(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='5'")}

SELECT COUNT(*)
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
    FROM `Orders` AS `o`
) AS `t`");
        }

        public override async Task Any_simple(bool isAsync)
        {
            await base.Any_simple(isAsync);

            AssertSql(
                $@"SELECT IIF(EXISTS (
        SELECT 1
        FROM `Customers` AS `c`), 1, 0)
FROM (SELECT COUNT(*) FROM MSysAccessStorage)");
        }

        public override async Task Any_predicate(bool isAsync)
        {
            await base.Any_predicate(isAsync);

            AssertSql(
                $@"SELECT CASE
    WHEN EXISTS (
        SELECT 1
        FROM `Customers` AS `c`
        WHERE `c`.`ContactName` IS NOT NULL AND (`c`.`ContactName` LIKE 'A' & '%')) THEN True
    ELSE False
END");
        }

        public override async Task Any_nested_negated(bool isAsync)
        {
            await base.Any_nested_negated(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE NOT (EXISTS (
    SELECT 1
    FROM `Orders` AS `o`
    WHERE `o`.`CustomerID` IS NOT NULL AND (`o`.`CustomerID` LIKE 'A' & '%')))");
        }

        public override async Task Any_nested_negated2(bool isAsync)
        {
            await base.Any_nested_negated2(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE ((`c`.`City` <> 'London') OR `c`.`City` IS NULL) AND NOT (EXISTS (
    SELECT 1
    FROM `Orders` AS `o`
    WHERE `o`.`CustomerID` IS NOT NULL AND (`o`.`CustomerID` LIKE 'A' & '%')))");
        }

        public override async Task Any_nested_negated3(bool isAsync)
        {
            await base.Any_nested_negated3(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE NOT (EXISTS (
    SELECT 1
    FROM `Orders` AS `o`
    WHERE `o`.`CustomerID` IS NOT NULL AND (`o`.`CustomerID` LIKE 'A' & '%'))) AND ((`c`.`City` <> 'London') OR `c`.`City` IS NULL)");
        }

        public override async Task Any_nested(bool isAsync)
        {
            await base.Any_nested(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE EXISTS (
    SELECT 1
    FROM `Orders` AS `o`
    WHERE `o`.`CustomerID` IS NOT NULL AND (`o`.`CustomerID` LIKE 'A' & '%'))");
        }

        public override async Task Any_nested2(bool isAsync)
        {
            await base.Any_nested2(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE ((`c`.`City` <> 'London') OR `c`.`City` IS NULL) AND EXISTS (
    SELECT 1
    FROM `Orders` AS `o`
    WHERE `o`.`CustomerID` IS NOT NULL AND (`o`.`CustomerID` LIKE 'A' & '%'))");
        }

        public override async Task Any_nested3(bool isAsync)
        {
            await base.Any_nested3(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE EXISTS (
    SELECT 1
    FROM `Orders` AS `o`
    WHERE `o`.`CustomerID` IS NOT NULL AND (`o`.`CustomerID` LIKE 'A' & '%')) AND ((`c`.`City` <> 'London') OR `c`.`City` IS NULL)");
        }

        public override void Any_with_multiple_conditions_still_uses_exists()
        {
            base.Any_with_multiple_conditions_still_uses_exists();

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE (`c`.`City` = 'London') AND EXISTS (
    SELECT 1
    FROM `Orders` AS `o`
    WHERE (`c`.`CustomerID` = `o`.`CustomerID`) AND (`o`.`EmployeeID` = 1))");
        }

        public override async Task All_top_level(bool isAsync)
        {
            await base.All_top_level(isAsync);

            // issue #15994
//            AssertSql(
//                $@"SELECT CASE
//    WHEN NOT EXISTS (
//        SELECT 1
//        FROM `Customers` AS `c`
//        WHERE NOT (`c`.`ContactName` LIKE 'A' & '%'))
//    THEN True ELSE False
//END");
        }

        public override async Task All_top_level_column(bool isAsync)
        {
            await base.All_top_level_column(isAsync);

            AssertSql(
                $@"SELECT CASE
    WHEN NOT EXISTS (
        SELECT 1
        FROM `Customers` AS `c`
        WHERE ((`c`.`ContactName` <> '') OR `c`.`ContactName` IS NULL) AND (`c`.`ContactName` IS NULL OR (`c`.`ContactName` IS NULL OR ((LEFT(`c`.`ContactName`, LEN(`c`.`ContactName`)) <> `c`.`ContactName`) OR LEFT(`c`.`ContactName`, LEN(`c`.`ContactName`)) IS NULL)))) THEN True
    ELSE False
END");
        }

        public override async Task All_top_level_subquery(bool isAsync)
        {
            await base.All_top_level_subquery(isAsync);

            AssertSql(
                $@"SELECT CASE
    WHEN NOT EXISTS (
        SELECT 1
        FROM `Customers` AS `c`
        WHERE NOT (EXISTS (
            SELECT 1
            FROM `Customers` AS `c0`
            WHERE EXISTS (
                SELECT 1
                FROM `Customers` AS `c1`
                WHERE `c`.`CustomerID` = `c1`.`CustomerID`)))) THEN True
    ELSE False
END");
        }

        public override async Task All_top_level_subquery_ef_property(bool isAsync)
        {
            await base.All_top_level_subquery_ef_property(isAsync);

            AssertSql(
                $@"SELECT CASE
    WHEN NOT EXISTS (
        SELECT 1
        FROM `Customers` AS `c`
        WHERE NOT (EXISTS (
            SELECT 1
            FROM `Customers` AS `c0`
            WHERE EXISTS (
                SELECT 1
                FROM `Customers` AS `c1`
                WHERE `c`.`CustomerID` = `c1`.`CustomerID`)))) THEN True
    ELSE False
END");
        }

        public override async Task Where_select_many_or(bool isAsync)
        {
            await base.Where_select_many_or(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Customers` AS `c`,
`Employees` AS `e`
WHERE (`c`.`City` = 'London') OR (`e`.`City` = 'London')");
        }

        public override async Task Where_select_many_or2(bool isAsync)
        {
            await base.Where_select_many_or2(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Customers` AS `c`,
`Employees` AS `e`
WHERE (`c`.`City` = 'London') OR (`c`.`City` = 'Berlin')");
        }

        public override async Task Where_select_many_or3(bool isAsync)
        {
            await base.Where_select_many_or3(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Customers` AS `c`,
`Employees` AS `e`
WHERE ((`c`.`City` = 'London') OR (`c`.`City` = 'Berlin')) OR (`c`.`City` = 'Seattle')");
        }

        public override async Task Where_select_many_or4(bool isAsync)
        {
            await base.Where_select_many_or4(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Customers` AS `c`,
`Employees` AS `e`
WHERE (((`c`.`City` = 'London') OR (`c`.`City` = 'Berlin')) OR (`c`.`City` = 'Seattle')) OR (`c`.`City` = 'Lisboa')");
        }

        public override async Task Where_select_many_or_with_parameter(bool isAsync)
        {
            await base.Where_select_many_or_with_parameter(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__london_0='London' (Size = 4000)")}

{AssertSqlHelper.Declaration("@__lisboa_1='Lisboa' (Size = 4000)")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Customers` AS `c`,
`Employees` AS `e`
WHERE (((`c`.`City` = {AssertSqlHelper.Parameter("@__london_0")}) OR (`c`.`City` = 'Berlin')) OR (`c`.`City` = 'Seattle')) OR (`c`.`City` = {AssertSqlHelper.Parameter("@__lisboa_1")})");
        }

        public override async Task SelectMany_simple_subquery(bool isAsync)
        {
            await base.SelectMany_simple_subquery(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='9'")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `t`.`EmployeeID`, `t`.`City`, `t`.`Country`, `t`.`FirstName`, `t`.`ReportsTo`, `t`.`Title`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
    FROM `Employees` AS `e`
) AS `t`,
`Customers` AS `c`");
        }

        public override async Task SelectMany_simple1(bool isAsync)
        {
            await base.SelectMany_simple1(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`,
`Customers` AS `c`");
        }

        public override async Task SelectMany_simple2(bool isAsync)
        {
            await base.SelectMany_simple2(isAsync);

            AssertSql(
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`, `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `e0`.`FirstName`
FROM `Employees` AS `e`,
`Customers` AS `c`,
`Employees` AS `e0`");
        }

        public override async Task SelectMany_entity_deep(bool isAsync)
        {
            await base.SelectMany_entity_deep(isAsync);

            AssertSql(
                $@"SELECT `e0`.`EmployeeID`, `e0`.`City`, `e0`.`Country`, `e0`.`FirstName`, `e0`.`ReportsTo`, `e0`.`Title`, `e1`.`EmployeeID`, `e1`.`City`, `e1`.`Country`, `e1`.`FirstName`, `e1`.`ReportsTo`, `e1`.`Title`, `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`, `e2`.`EmployeeID`, `e2`.`City`, `e2`.`Country`, `e2`.`FirstName`, `e2`.`ReportsTo`, `e2`.`Title`
FROM `Employees` AS `e`,
`Employees` AS `e0`,
`Employees` AS `e1`,
`Employees` AS `e2`");
        }

        public override async Task SelectMany_projection1(bool isAsync)
        {
            await base.SelectMany_projection1(isAsync);

            AssertSql(
                $@"SELECT `e`.`City`, `e0`.`Country`
FROM `Employees` AS `e`,
`Employees` AS `e0`");
        }

        public override async Task SelectMany_projection2(bool isAsync)
        {
            await base.SelectMany_projection2(isAsync);

            AssertSql(
                $@"SELECT `e`.`City`, `e0`.`Country`, `e1`.`FirstName`
FROM `Employees` AS `e`,
`Employees` AS `e0`,
`Employees` AS `e1`");
        }

        public override async Task SelectMany_Count(bool isAsync)
        {
            await base.SelectMany_Count(isAsync);

            AssertSql(
                $@"SELECT COUNT(*)
FROM `Customers` AS `c`,
`Orders` AS `o`");
        }

        public override async Task SelectMany_LongCount(bool isAsync)
        {
            await base.SelectMany_LongCount(isAsync);

            AssertSql(
                $@"SELECT COUNT_BIG(*)
FROM `Customers` AS `c`,
`Orders` AS `o`");
        }

        public override async Task SelectMany_OrderBy_ThenBy_Any(bool isAsync)
        {
            await base.SelectMany_OrderBy_ThenBy_Any(isAsync);

            AssertSql(
                $@"SELECT CASE
    WHEN EXISTS (
        SELECT 1
        FROM `Customers` AS `c`,
`Orders` AS `o`) THEN True
    ELSE False
END");
        }

        public override async Task Join_Where_Count(bool isAsync)
        {
            await base.Join_Where_Count(isAsync);

            AssertSql(
                $@"SELECT COUNT(*)
FROM `Customers` AS `c`
INNER JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`
WHERE `c`.`CustomerID` = 'ALFKI'");
        }

        public override async Task Where_Join_Any(bool isAsync)
        {
            await base.Where_Join_Any(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE (`c`.`CustomerID` = 'ALFKI') AND EXISTS (
    SELECT 1
    FROM `Orders` AS `o`
    WHERE (`c`.`CustomerID` = `o`.`CustomerID`) AND (`o`.`OrderDate` = #2008-10-24#))");
        }

        public override async Task Where_Join_Exists(bool isAsync)
        {
            await base.Where_Join_Exists(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE (`c`.`CustomerID` = 'ALFKI') AND EXISTS (
    SELECT 1
    FROM `Orders` AS `o`
    WHERE (`c`.`CustomerID` = `o`.`CustomerID`) AND (`o`.`OrderDate` = #2008-10-24 00:00:00#))");
        }

        public override async Task Where_Join_Exists_Inequality(bool isAsync)
        {
            await base.Where_Join_Exists_Inequality(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE (`c`.`CustomerID` = 'ALFKI') AND EXISTS (
    SELECT 1
    FROM `Orders` AS `o`
    WHERE (`c`.`CustomerID` = `o`.`CustomerID`) AND ((`o`.`OrderDate` <> #2008-10-24 00:00:00#) OR `o`.`OrderDate` IS NULL))");
        }

        public override async Task Where_Join_Exists_Constant(bool isAsync)
        {
            await base.Where_Join_Exists_Constant(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE (`c`.`CustomerID` = 'ALFKI') AND EXISTS (
    SELECT 1
    FROM `Orders` AS `o`
    WHERE 0 = 1)");
        }

        public override async Task Where_Join_Not_Exists(bool isAsync)
        {
            await base.Where_Join_Not_Exists(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE (`c`.`CustomerID` = 'ALFKI') AND NOT EXISTS (
    SELECT 1
    FROM `Orders` AS `o`
    WHERE 0 = 1)");
        }

        public override async Task Join_OrderBy_Count(bool isAsync)
        {
            await base.Join_OrderBy_Count(isAsync);

            AssertSql(
                $@"SELECT COUNT(*)
FROM `Customers` AS `c`
INNER JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`");
        }

        public override async Task Multiple_joins_Where_Order_Any(bool isAsync)
        {
            await base.Multiple_joins_Where_Order_Any(isAsync);

            AssertSql(
                $@"SELECT CASE
    WHEN EXISTS (
        SELECT 1
        FROM `Customers` AS `c`
        INNER JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`
        INNER JOIN `Order Details` AS `o0` ON `o`.`OrderID` = `o0`.`OrderID`
        WHERE `c`.`City` = 'London') THEN True
    ELSE False
END");
        }

        public override async Task Where_join_select(bool isAsync)
        {
            await base.Where_join_select(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
INNER JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`
WHERE `c`.`CustomerID` = 'ALFKI'");
        }

        public override async Task Where_orderby_join_select(bool isAsync)
        {
            await base.Where_orderby_join_select(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
INNER JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`
WHERE `c`.`CustomerID` <> 'ALFKI'
ORDER BY `c`.`CustomerID`");
        }

        public override async Task Where_join_orderby_join_select(bool isAsync)
        {
            await base.Where_join_orderby_join_select(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
INNER JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`
INNER JOIN `Order Details` AS `o0` ON `o`.`OrderID` = `o0`.`OrderID`
WHERE `c`.`CustomerID` <> 'ALFKI'
ORDER BY `c`.`CustomerID`");
        }

        public override async Task Where_select_many(bool isAsync)
        {
            await base.Where_select_many(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`,
`Orders` AS `o`
WHERE `c`.`CustomerID` = 'ALFKI'");
        }

        public override async Task Where_orderby_select_many(bool isAsync)
        {
            await base.Where_orderby_select_many(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`,
`Orders` AS `o`
WHERE `c`.`CustomerID` = 'ALFKI'
ORDER BY `c`.`CustomerID`");
        }

        public override async Task SelectMany_cartesian_product_with_ordering(bool isAsync)
        {
            await base.SelectMany_cartesian_product_with_ordering(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `e`.`City`
FROM `Customers` AS `c`,
`Employees` AS `e`
WHERE (`c`.`City` = `e`.`City`) OR (`c`.`City` IS NULL AND `e`.`City` IS NULL)
ORDER BY `e`.`City`, `c`.`CustomerID` DESC");
        }

        public override async Task SelectMany_Joined_DefaultIfEmpty(bool isAsync)
        {
            await base.SelectMany_Joined_DefaultIfEmpty(isAsync);

            AssertSql(
                $@"SELECT `c`.`ContactName`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Customers` AS `c`
LEFT JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`");
        }

        public override async Task SelectMany_Joined_DefaultIfEmpty2(bool isAsync)
        {
            await base.SelectMany_Joined_DefaultIfEmpty2(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Customers` AS `c`
LEFT JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`");
        }

        public override async Task SelectMany_Joined_Take(bool isAsync)
        {
            await base.SelectMany_Joined_Take(isAsync);

            AssertSql(
                $@"SELECT `c`.`ContactName`, `t0`.`OrderID`, `t0`.`CustomerID`, `t0`.`EmployeeID`, `t0`.`OrderDate`
FROM `Customers` AS `c`
INNER JOIN (
    SELECT `t`.`OrderID`, `t`.`CustomerID`, `t`.`EmployeeID`, `t`.`OrderDate`
    FROM (
        SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`, ROW_NUMBER() OVER(PARTITION BY `o`.`CustomerID` ORDER BY `o`.`OrderID`) AS `row`
        FROM `Orders` AS `o`
    ) AS `t`
    WHERE `t`.`row` <= 4
) AS `t0` ON `c`.`CustomerID` = `t0`.`CustomerID`");
        }

        public override async Task Take_with_single(bool isAsync)
        {
            await base.Take_with_single(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='1'")}

SELECT TOP 2 `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    ORDER BY `c`.`CustomerID`
) AS `t`
ORDER BY `t`.`CustomerID`");
        }

        public override async Task Take_with_single_select_many(bool isAsync)
        {
            await base.Take_with_single_select_many(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='1'")}

SELECT TOP 2 `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`, `t`.`OrderID`, `t`.`CustomerID0`, `t`.`EmployeeID`, `t`.`OrderDate`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `o`.`OrderID`, `o`.`CustomerID` AS `CustomerID0`, `o`.`EmployeeID`, `o`.`OrderDate`
    FROM `Customers` AS `c`,
`Orders` AS `o`
    ORDER BY `c`.`CustomerID`, `o`.`OrderID`
) AS `t`
ORDER BY `t`.`CustomerID`, `t`.`OrderID`");
        }

        public override async Task Distinct_Skip(bool isAsync)
        {
            await base.Distinct_Skip(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='5'")}

SELECT `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`
FROM (
    SELECT DISTINCT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
) AS `t`
ORDER BY `t`.`CustomerID`
SKIP {AssertSqlHelper.Parameter("@__p_0")}");
        }

        public override async Task Distinct_Skip_Take(bool isAsync)
        {
            await base.Distinct_Skip_Take(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='5'")}

{AssertSqlHelper.Declaration("@__p_1='10'")}

SELECT `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`
FROM (
    SELECT DISTINCT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
) AS `t`
ORDER BY `t`.`ContactName`
SKIP {AssertSqlHelper.Parameter("@__p_0")} FETCH NEXT {AssertSqlHelper.Parameter("@__p_1")} ROWS ONLY");
        }

        public override async Task Skip_Distinct(bool isAsync)
        {
            await base.Skip_Distinct(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='5'")}

SELECT DISTINCT `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`
FROM (
    SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    ORDER BY `c`.`ContactName`
    SKIP {AssertSqlHelper.Parameter("@__p_0")}
) AS `t`");
        }

        public override async Task Skip_Take_Distinct(bool isAsync)
        {
            await base.Skip_Take_Distinct(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='5'")}

{AssertSqlHelper.Declaration("@__p_1='10'")}

SELECT DISTINCT `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`
FROM (
    SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    ORDER BY `c`.`ContactName`
    SKIP {AssertSqlHelper.Parameter("@__p_0")} FETCH NEXT {AssertSqlHelper.Parameter("@__p_1")} ROWS ONLY
) AS `t`");
        }

        public override async Task Skip_Take_Any(bool isAsync)
        {
            await base.Skip_Take_Any(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='5'")}

{AssertSqlHelper.Declaration("@__p_1='10'")}

SELECT CASE
    WHEN EXISTS (
        SELECT 1
        FROM `Customers` AS `c`
        ORDER BY `c`.`ContactName`
        SKIP {AssertSqlHelper.Parameter("@__p_0")} FETCH NEXT {AssertSqlHelper.Parameter("@__p_1")} ROWS ONLY) THEN True
    ELSE False
END");
        }

        public override async Task Skip_Take_All(bool isAsync)
        {
            await base.Skip_Take_All(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='4'")}

{AssertSqlHelper.Declaration("@__p_1='7'")}

SELECT CASE
    WHEN NOT EXISTS (
        SELECT 1
        FROM (
            SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            FROM `Customers` AS `c`
            ORDER BY `c`.`CustomerID`
            SKIP {AssertSqlHelper.Parameter("@__p_0")} FETCH NEXT {AssertSqlHelper.Parameter("@__p_1")} ROWS ONLY
        ) AS `t`
        WHERE NOT (`t`.`CustomerID` LIKE 'B' & '%')) THEN True
    ELSE False
END");
        }

        public override async Task Take_All(bool isAsync)
        {
            await base.Take_All(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='4'")}

SELECT CASE
    WHEN NOT EXISTS (
        SELECT 1
        FROM (
            SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            FROM `Customers` AS `c`
            ORDER BY `c`.`CustomerID`
        ) AS `t`
        WHERE NOT (`t`.`CustomerID` LIKE 'A' & '%')) THEN True
    ELSE False
END");
        }

        public override async Task Skip_Take_Any_with_predicate(bool isAsync)
        {
            await base.Skip_Take_Any_with_predicate(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='5'")}

{AssertSqlHelper.Declaration("@__p_1='7'")}

SELECT CASE
    WHEN EXISTS (
        SELECT 1
        FROM (
            SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            FROM `Customers` AS `c`
            ORDER BY `c`.`CustomerID`
            SKIP {AssertSqlHelper.Parameter("@__p_0")} FETCH NEXT {AssertSqlHelper.Parameter("@__p_1")} ROWS ONLY
        ) AS `t`
        WHERE `t`.`CustomerID` LIKE 'C' & '%') THEN True
    ELSE False
END");
        }

        public override async Task Take_Any_with_predicate(bool isAsync)
        {
            await base.Take_Any_with_predicate(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='5'")}

SELECT CASE
    WHEN EXISTS (
        SELECT 1
        FROM (
            SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            FROM `Customers` AS `c`
            ORDER BY `c`.`CustomerID`
        ) AS `t`
        WHERE `t`.`CustomerID` LIKE 'B' & '%') THEN True
    ELSE False
END");
        }

        public override async Task OrderBy(bool isAsync)
        {
            await base.OrderBy(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
ORDER BY `c`.`CustomerID`");
        }

        public override async Task OrderBy_true(bool isAsync)
        {
            await base.OrderBy_true(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`");
        }

        public override async Task OrderBy_integer(bool isAsync)
        {
            await base.OrderBy_integer(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`");
        }

        public override async Task OrderBy_parameter(bool isAsync)
        {
            await base.OrderBy_parameter(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`");
        }

        public override async Task OrderBy_anon(bool isAsync)
        {
            await base.OrderBy_anon(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`
FROM `Customers` AS `c`
ORDER BY `c`.`CustomerID`");
        }

        public override async Task OrderBy_anon2(bool isAsync)
        {
            await base.OrderBy_anon2(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
ORDER BY `c`.`CustomerID`");
        }

        public override async Task Distinct_Take(bool isAsync)
        {
            await base.Distinct_Take(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='5'")}

SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `t`.`OrderID`, `t`.`CustomerID`, `t`.`EmployeeID`, `t`.`OrderDate`
FROM (
    SELECT DISTINCT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
    FROM `Orders` AS `o`
) AS `t`
ORDER BY `t`.`OrderID`");
        }

        public override async Task Distinct_Take_Count(bool isAsync)
        {
            await base.Distinct_Take_Count(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='5'")}

SELECT COUNT(*)
FROM (
    SELECT DISTINCT TOP {AssertSqlHelper.Parameter("@__p_0")} `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
    FROM `Orders` AS `o`
) AS `t`");
        }

        public override async Task OrderBy_shadow(bool isAsync)
        {
            await base.OrderBy_shadow(isAsync);

            AssertSql(
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
ORDER BY `e`.`Title`, `e`.`EmployeeID`");
        }

        public override async Task OrderBy_multiple(bool isAsync)
        {
            await base.OrderBy_multiple(isAsync);

            AssertSql(
                $@"SELECT `c`.`City`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` LIKE 'A' & '%'
ORDER BY `c`.`Country`, `c`.`City`");
        }

        public override async Task OrderBy_ThenBy_Any(bool isAsync)
        {
            await base.OrderBy_ThenBy_Any(isAsync);

            AssertSql(
                $@"SELECT CASE
    WHEN EXISTS (
        SELECT 1
        FROM `Customers` AS `c`) THEN True
    ELSE False
END");
        }

        public override async Task OrderBy_correlated_subquery1(bool isAsync)
        {
            await base.OrderBy_correlated_subquery1(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` LIKE 'A' & '%'
ORDER BY CASE
    WHEN EXISTS (
        SELECT 1
        FROM `Customers` AS `c0`
        WHERE `c0`.`CustomerID` = `c`.`CustomerID`) THEN True
    ELSE False
END, `c`.`CustomerID`");
        }

        public override async Task OrderBy_correlated_subquery2(bool isAsync)
        {
            await base.OrderBy_correlated_subquery2(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`OrderID` <= 10250) AND (((
    SELECT TOP 1 `c`.`City`
    FROM `Customers` AS `c`
    ORDER BY CASE
        WHEN EXISTS (
            SELECT 1
            FROM `Customers` AS `c0`
            WHERE `c0`.`CustomerID` = 'ALFKI') THEN True
        ELSE False
    END) <> 'Nowhere') OR (
    SELECT TOP 1 `c`.`City`
    FROM `Customers` AS `c`
    ORDER BY CASE
        WHEN EXISTS (
            SELECT 1
            FROM `Customers` AS `c0`
            WHERE `c0`.`CustomerID` = 'ALFKI') THEN True
        ELSE False
    END) IS NULL)");
        }

        public override async Task Where_subquery_recursive_trivial(bool isAsync)
        {
            await base.Where_subquery_recursive_trivial(isAsync);

            AssertSql(
                $@"SELECT `e`.`EmployeeID`, `e`.`City`, `e`.`Country`, `e`.`FirstName`, `e`.`ReportsTo`, `e`.`Title`
FROM `Employees` AS `e`
WHERE EXISTS (
    SELECT 1
    FROM `Employees` AS `e0`
    WHERE EXISTS (
        SELECT 1
        FROM `Employees` AS `e1`))
ORDER BY `e`.`EmployeeID`");
        }

        public override void Select_DTO_distinct_translated_to_server()
        {
            base.Select_DTO_distinct_translated_to_server();

            AssertSql(
                $@"SELECT DISTINCT 1
FROM `Orders` AS `o`
WHERE `o`.`OrderID` < 10300");
        }

        public override void Select_DTO_constructor_distinct_translated_to_server()
        {
            base.Select_DTO_constructor_distinct_translated_to_server();

            AssertSql(
                $@"SELECT DISTINCT `o`.`CustomerID`
FROM `Orders` AS `o`
WHERE `o`.`OrderID` < 10300");
        }

        public override void Select_DTO_constructor_distinct_with_navigation_translated_to_server()
        {
            base.Select_DTO_constructor_distinct_with_navigation_translated_to_server();

            AssertSql(
                $@"SELECT DISTINCT `c`.`City`
FROM `Orders` AS `o`
LEFT JOIN `Customers` AS `c` ON `o`.`CustomerID` = `c`.`CustomerID`
WHERE `o`.`OrderID` < 10300");
        }

        public override void Select_DTO_with_member_init_distinct_translated_to_server()
        {
            base.Select_DTO_with_member_init_distinct_translated_to_server();

            AssertSql(
                $@"SELECT DISTINCT `o`.`CustomerID` AS `Id`, `o`.`OrderID` AS `Count`
FROM `Orders` AS `o`
WHERE `o`.`OrderID` < 10300");
        }

        public override void Select_nested_collection_count_using_DTO()
        {
            base.Select_nested_collection_count_using_DTO();

            AssertSql(
                $@"SELECT `c`.`CustomerID` AS `Id`, (
    SELECT COUNT(*)
    FROM `Orders` AS `o`
    WHERE `c`.`CustomerID` = `o`.`CustomerID`) AS `Count`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` LIKE 'A' & '%'");
        }

        public override async Task Select_DTO_with_member_init_distinct_in_subquery_translated_to_server(bool isAsync)
        {
            await base.Select_DTO_with_member_init_distinct_in_subquery_translated_to_server(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM (
    SELECT DISTINCT `o`.`CustomerID`, `o`.`OrderID`
    FROM `Orders` AS `o`
    WHERE `o`.`OrderID` < 10300
) AS `t`
INNER JOIN `Customers` AS `c` ON `t`.`CustomerID` = `c`.`CustomerID`");
        }

        public override async Task Select_DTO_with_member_init_distinct_in_subquery_translated_to_server_2(bool isAsync)
        {
            await base.Select_DTO_with_member_init_distinct_in_subquery_translated_to_server_2(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM (
    SELECT DISTINCT `o`.`CustomerID`, `o`.`OrderID`
    FROM `Orders` AS `o`
    WHERE `o`.`OrderID` < 10300
) AS `t`
INNER JOIN `Customers` AS `c` ON `t`.`CustomerID` = `c`.`CustomerID`");
        }

        public override void Select_DTO_with_member_init_distinct_in_subquery_used_in_projection_translated_to_server()
        {
            base.Select_DTO_with_member_init_distinct_in_subquery_used_in_projection_translated_to_server();

            // issue #15994
//            AssertSql(
//                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, `t`.`Id`, `t`.`Count`
//FROM `Customers` AS `c`
//CROSS JOIN (
//    SELECT DISTINCT `o`.`CustomerID` AS `Id`, `o`.`OrderID` AS `Count`
//    FROM `Orders` AS `o`
//    WHERE `o`.`OrderID` < 10300
//) AS `t`
//WHERE `c`.`CustomerID` LIKE 'A' & '%'");
        }
        
        public override async Task Select_correlated_subquery_filtered(bool isAsync)
        {
            await base.Select_correlated_subquery_filtered(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` LIKE 'A' & '%'
ORDER BY `c`.`CustomerID`",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_CustomerID='ALFKI' (Size = 5)")}

SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`CustomerID` = {AssertSqlHelper.Parameter("@_outer_CustomerID")}",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_CustomerID='ANATR' (Size = 5)")}

SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`CustomerID` = {AssertSqlHelper.Parameter("@_outer_CustomerID")}",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_CustomerID='ANTON' (Size = 5)")}

SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`CustomerID` = {AssertSqlHelper.Parameter("@_outer_CustomerID")}",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_CustomerID='AROUT' (Size = 5)")}

SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`CustomerID` = {AssertSqlHelper.Parameter("@_outer_CustomerID")}");
        }

        public override async Task Select_correlated_subquery_ordered(bool isAsync)
        {
            await base.Select_correlated_subquery_ordered(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='3'")}

SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`CustomerID`
FROM `Customers` AS `c`
ORDER BY `c`.`CustomerID`",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`");
        }

        public override async Task Where_subquery_on_bool(bool isAsync)
        {
            await base.Where_subquery_on_bool(isAsync);

            AssertSql(
                $@"SELECT `p`.`ProductID`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice`, `p`.`UnitsInStock`
FROM `Products` AS `p`
WHERE 'Chai' IN (
    SELECT `p0`.`ProductName`
    FROM `Products` AS `p0`
)");
        }

        public override async Task Where_subquery_on_collection(bool isAsync)
        {
            await base.Where_subquery_on_collection(isAsync);

            AssertSql(
                $@"SELECT `p`.`ProductID`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice`, `p`.`UnitsInStock`
FROM `Products` AS `p`
WHERE 5 IN (
    SELECT `o`.`Quantity`
    FROM `Order Details` AS `o`
    WHERE `o`.`ProductID` = `p`.`ProductID`
)");
        }

        public override async Task Select_many_cross_join_same_collection(bool isAsync)
        {
            await base.Select_many_cross_join_same_collection(isAsync);

            AssertSql(
                $@"SELECT `c0`.`CustomerID`, `c0`.`Address`, `c0`.`City`, `c0`.`CompanyName`, `c0`.`ContactName`, `c0`.`ContactTitle`, `c0`.`Country`, `c0`.`Fax`, `c0`.`Phone`, `c0`.`PostalCode`, `c0`.`Region`
FROM `Customers` AS `c`,
`Customers` AS `c0`");
        }

        public override async Task OrderBy_null_coalesce_operator(bool isAsync)
        {
            await base.OrderBy_null_coalesce_operator(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
ORDER BY IIf(`c`.`Region` IS NULL, NULL, `c`.`Region`), `c`.`CustomerID`");
        }

        public override async Task Select_null_coalesce_operator(bool isAsync)
        {
            await base.Select_null_coalesce_operator(isAsync);

            // issue #16038
//            AssertSql(
//                $@"SELECT `c`.`CustomerID`, `c`.`CompanyName`, IIf(`c`.`Region` IS NULL, NULL, `c`.`Region`) AS `Region`
//FROM `Customers` AS `c`
//ORDER BY `Region`, `c`.`CustomerID`");
        }

        public override async Task OrderBy_conditional_operator(bool isAsync)
        {
            await base.OrderBy_conditional_operator(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
ORDER BY CASE
    WHEN `c`.`Region` IS NULL THEN 'ZZ'
    ELSE `c`.`Region`
END, `c`.`CustomerID`");
        }

        public override async Task Null_Coalesce_Short_Circuit(bool isAsync)
        {
            await base.Null_Coalesce_Short_Circuit(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='False'")}

SELECT `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`, {AssertSqlHelper.Parameter("@__p_0")} AS `Test`
FROM (
    SELECT DISTINCT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
) AS `t`");
        }

        public override async Task Null_Coalesce_Short_Circuit_with_server_correlated_leftover(bool isAsync)
        {
            await base.Null_Coalesce_Short_Circuit_with_server_correlated_leftover(isAsync);

            AssertSql(
                $@"SELECT False AS `Result`
FROM `Customers` AS `c`");
        }

        public override async Task OrderBy_conditional_operator_where_condition_false(bool isAsync)
        {
            await base.OrderBy_conditional_operator_where_condition_false(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
ORDER BY `c`.`City`");
        }

        public override async Task OrderBy_comparison_operator(bool isAsync)
        {
            await base.OrderBy_comparison_operator(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
ORDER BY (`c`.`Region` = 'ASK') AND `c`.`Region` IS NOT NULL");
        }

        public override async Task Projection_null_coalesce_operator(bool isAsync)
        {
            await base.Projection_null_coalesce_operator(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`CompanyName`, IIf(`c`.`Region` IS NULL, NULL, `c`.`Region`) AS `Region`
FROM `Customers` AS `c`");
        }

        public override async Task Filter_coalesce_operator(bool isAsync)
        {
            await base.Filter_coalesce_operator(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE IIf(`c`.`CompanyName` IS NULL, NULL, `c`.`CompanyName`) = 'The Big Cheese'");
        }

        public override async Task Take_skip_null_coalesce_operator(bool isAsync)
        {
            await base.Take_skip_null_coalesce_operator(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='10'")}

{AssertSqlHelper.Declaration("@__p_1='5'")}

SELECT DISTINCT `t0`.`CustomerID`, `t0`.`Address`, `t0`.`City`, `t0`.`CompanyName`, `t0`.`ContactName`, `t0`.`ContactTitle`, `t0`.`Country`, `t0`.`Fax`, `t0`.`Phone`, `t0`.`PostalCode`, `t0`.`Region`
FROM (
    SELECT `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`, `t`.`c`
    FROM (
        SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, IIf(`c`.`Region` IS NULL, NULL, `c`.`Region`) AS `c`
        FROM `Customers` AS `c`
        ORDER BY IIf(`c`.`Region` IS NULL, NULL, `c`.`Region`)
    ) AS `t`
    ORDER BY `t`.`c`
    SKIP {AssertSqlHelper.Parameter("@__p_1")}
) AS `t0`");
        }

        public override async Task Select_take_null_coalesce_operator(bool isAsync)
        {
            await base.Select_take_null_coalesce_operator(isAsync);

            // issue #16038
//            AssertSql(
//                $@"{AssertSqlHelper.Declaration("@__p_0='5'")}

//SELECT TOP @__p_0 `c`.`CustomerID`, `c`.`CompanyName`, IIf(`c`.`Region` IS NULL, NULL, `c`.`Region`) AS `Region`
//FROM `Customers` AS `c`
//ORDER BY `Region`");
        }

        public override async Task Select_take_skip_null_coalesce_operator(bool isAsync)
        {
            await base.Select_take_skip_null_coalesce_operator(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='10'")}

{AssertSqlHelper.Declaration("@__p_1='5'")}

SELECT `t`.`CustomerID`, `t`.`CompanyName`, `t`.`c` AS `Region`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`CustomerID`, `c`.`CompanyName`, IIf(`c`.`Region` IS NULL, NULL, `c`.`Region`) AS `c`
    FROM `Customers` AS `c`
    ORDER BY IIf(`c`.`Region` IS NULL, NULL, `c`.`Region`)
) AS `t`
ORDER BY `t`.`c`
SKIP {AssertSqlHelper.Parameter("@__p_1")}");
        }

        public override async Task Select_take_skip_null_coalesce_operator2(bool isAsync)
        {
            await base.Select_take_skip_null_coalesce_operator2(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='10'")}

{AssertSqlHelper.Declaration("@__p_1='5'")}

SELECT `t`.`CustomerID`, `t`.`CompanyName`, `t`.`Region`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`CustomerID`, `c`.`CompanyName`, `c`.`Region`, IIf(`c`.`Region` IS NULL, NULL, `c`.`Region`) AS `c`
    FROM `Customers` AS `c`
    ORDER BY IIf(`c`.`Region` IS NULL, NULL, `c`.`Region`)
) AS `t`
ORDER BY `t`.`c`
SKIP {AssertSqlHelper.Parameter("@__p_1")}");
        }

        public override async Task Select_take_skip_null_coalesce_operator3(bool isAsync)
        {
            await base.Select_take_skip_null_coalesce_operator3(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='10'")}

{AssertSqlHelper.Declaration("@__p_1='5'")}

SELECT `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`, IIf(`c`.`Region` IS NULL, NULL, `c`.`Region`) AS `c`
    FROM `Customers` AS `c`
    ORDER BY IIf(`c`.`Region` IS NULL, NULL, `c`.`Region`)
) AS `t`
ORDER BY `t`.`c`
SKIP {AssertSqlHelper.Parameter("@__p_1")}");
        }

        public override void Selected_column_can_coalesce()
        {
            base.Selected_column_can_coalesce();

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
ORDER BY IIf(`c`.`Region` IS NULL, NULL, `c`.`Region`)");
        }

        public override async Task DateTime_parse_is_inlined(bool isAsync)
        {
            await base.DateTime_parse_is_inlined(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`OrderDate` > #1998-01-01 12:00:00#");
        }

        public override async Task DateTime_parse_is_parameterized_when_from_closure(bool isAsync)
        {
            await base.DateTime_parse_is_parameterized_when_from_closure(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__Parse_0='1998-01-01T12:00:00' (Nullable = true) (DbType = DateTime)")}

SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`OrderDate` > {AssertSqlHelper.Parameter("@__Parse_0")}");
        }

        public override async Task New_DateTime_is_inlined(bool isAsync)
        {
            await base.New_DateTime_is_inlined(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`OrderDate` > #1998-01-01 12:00:00#");
        }

        public override async Task New_DateTime_is_parameterized_when_from_closure(bool isAsync)
        {
            await base.New_DateTime_is_parameterized_when_from_closure(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='1998-01-01T12:00:00' (Nullable = true) (DbType = DateTime)")}

SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`OrderDate` > {AssertSqlHelper.Parameter("@__p_0")}",
                //
                $@"{AssertSqlHelper.Declaration("@__p_0='1998-01-01T11:00:00' (Nullable = true) (DbType = DateTime)")}

SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`OrderDate` > {AssertSqlHelper.Parameter("@__p_0")}");
        }

        public override async Task Environment_newline_is_funcletized(bool isAsync)
        {
            await base.Environment_newline_is_funcletized(isAsync);

            AssertSql(
                $@"@__NewLine_0='
' (Size = 5)

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE ({AssertSqlHelper.Parameter("@__NewLine_0")} = '') OR (CHARINDEX({AssertSqlHelper.Parameter("@__NewLine_0")}, `c`.`CustomerID`) > 0)");
        }

        public override async Task String_concat_with_navigation1(bool isAsync)
        {
            await base.String_concat_with_navigation1(isAsync);

            AssertSql(
                $@"SELECT (`o`.`CustomerID` + ' ') + `c`.`City`
FROM `Orders` AS `o`
LEFT JOIN `Customers` AS `c` ON `o`.`CustomerID` = `c`.`CustomerID`");
        }

        public override async Task String_concat_with_navigation2(bool isAsync)
        {
            await base.String_concat_with_navigation2(isAsync);

            AssertSql(
                $@"SELECT (`c`.`City` + ' ') + `c`.`City`
FROM `Orders` AS `o`
LEFT JOIN `Customers` AS `c` ON `o`.`CustomerID` = `c`.`CustomerID`");
        }

        public override void Select_bitwise_or()
        {
            base.Select_bitwise_or();

            AssertSql(
                $@"SELECT `c`.`CustomerID`, IIF(`c`.`CustomerID` = 'ALFKI', 1, 0) BOR IIF(`c`.`CustomerID` = 'ANATR', 1, 0) AS `Value`
FROM `Customers` AS `c`
ORDER BY `c`.`CustomerID`");
        }

        public override void Select_bitwise_or_multiple()
        {
            base.Select_bitwise_or_multiple();

            AssertSql(
                $@"SELECT `c`.`CustomerID`, (IIF(`c`.`CustomerID` = 'ALFKI', 1, 0) BOR IIF(`c`.`CustomerID` = 'ANATR', 1, 0)) BOR IIF(`c`.`CustomerID` = 'ANTON', 1, 0) AS `Value`
FROM `Customers` AS `c`
ORDER BY `c`.`CustomerID`");
        }

        public override void Select_bitwise_and()
        {
            base.Select_bitwise_and();

            AssertSql(
                $@"SELECT `c`.`CustomerID`, IIF(`c`.`CustomerID` = 'ALFKI', 1, 0) BAND IIF(`c`.`CustomerID` = 'ANATR', 1, 0) AS `Value`
FROM `Customers` AS `c`
ORDER BY `c`.`CustomerID`");
        }

        public override void Select_bitwise_and_or()
        {
            base.Select_bitwise_and_or();

            AssertSql(
                $@"SELECT `c`.`CustomerID`, (IIF(`c`.`CustomerID` = 'ALFKI', 1, 0) BAND IIF(`c`.`CustomerID` = 'ANATR', 1, 0)) BOR IIF(`c`.`CustomerID` = 'ANTON', 1, 0) AS `Value`
FROM `Customers` AS `c`
ORDER BY `c`.`CustomerID`");
        }

        public override async Task Where_bitwise_or_with_logical_or(bool isAsync)
        {
            await base.Where_bitwise_or_with_logical_or(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE ((IIF(`c`.`CustomerID` = 'ALFKI', 1, 0) BOR IIF(`c`.`CustomerID` = 'ANATR', 1, 0)) = True) OR (`c`.`CustomerID` = 'ANTON')");
        }

        public override async Task Where_bitwise_and_with_logical_and(bool isAsync)
        {
            await base.Where_bitwise_and_with_logical_and(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE ((IIF(`c`.`CustomerID` = 'ALFKI', 1, 0) BAND IIF(`c`.`CustomerID` = 'ANATR', 1, 0)) = True) AND (`c`.`CustomerID` = 'ANTON')");
        }

        public override async Task Where_bitwise_or_with_logical_and(bool isAsync)
        {
            await base.Where_bitwise_or_with_logical_and(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE ((IIF(`c`.`CustomerID` = 'ALFKI', 1, 0) BOR IIF(`c`.`CustomerID` = 'ANATR', 1, 0)) = True) AND (`c`.`Country` = 'Germany')");
        }

        public override async Task Where_bitwise_and_with_logical_or(bool isAsync)
        {
            await base.Where_bitwise_and_with_logical_or(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE ((IIF(`c`.`CustomerID` = 'ALFKI', 1, 0) BAND IIF(`c`.`CustomerID` = 'ANATR', 1, 0)) = True) OR (`c`.`CustomerID` = 'ANTON')");
        }

        public override void Select_bitwise_or_with_logical_or()
        {
            base.Select_bitwise_or_with_logical_or();

            AssertSql(
                $@"SELECT `c`.`CustomerID`, IIF(((IIF(`c`.`CustomerID` = 'ALFKI', 1, 0) BOR IIF(`c`.`CustomerID` = 'ANATR', 1, 0)) = True) OR (`c`.`CustomerID` = 'ANTON'), 1, 0) AS `Value`
FROM `Customers` AS `c`
ORDER BY `c`.`CustomerID`");
        }

        public override void Select_bitwise_and_with_logical_and()
        {
            base.Select_bitwise_and_with_logical_and();

            AssertSql(
                $@"SELECT `c`.`CustomerID`, IIF(((IIF(`c`.`CustomerID` = 'ALFKI', 1, 0) BAND IIF(`c`.`CustomerID` = 'ANATR', 1, 0)) = True) AND (`c`.`CustomerID` = 'ANTON'), 1, 0) AS `Value`
FROM `Customers` AS `c`
ORDER BY `c`.`CustomerID`");
        }

        public override async Task Handle_materialization_properly_when_more_than_two_query_sources_are_involved(bool isAsync)
        {
            await base.Handle_materialization_properly_when_more_than_two_query_sources_are_involved(isAsync);

            AssertSql(
                $@"SELECT TOP 1 `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`,
`Orders` AS `o`,
`Employees` AS `e`
ORDER BY `c`.`CustomerID`");
        }

        public override async Task Parameter_extraction_short_circuits_1(bool isAsync)
        {
            await base.Parameter_extraction_short_circuits_1(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__dateFilter_Value_Month_0='7'")}

{AssertSqlHelper.Declaration("@__dateFilter_Value_Year_1='1996'")}

SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`OrderID` < 10400) AND ((`o`.`OrderDate` IS NOT NULL AND (DATEPART('m', `o`.`OrderDate`) = {AssertSqlHelper.Parameter("@__dateFilter_Value_Month_0")})) AND (DATEPART('yyyy', `o`.`OrderDate`) = {AssertSqlHelper.Parameter("@__dateFilter_Value_Year_1")}))",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`OrderID` < 10400");
        }

        public override async Task Parameter_extraction_short_circuits_2(bool isAsync)
        {
            await base.Parameter_extraction_short_circuits_2(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__dateFilter_Value_Month_0='7'")}

{AssertSqlHelper.Declaration("@__dateFilter_Value_Year_1='1996'")}

SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`OrderID` < 10400) AND ((`o`.`OrderDate` IS NOT NULL AND (DATEPART('m', `o`.`OrderDate`) = {AssertSqlHelper.Parameter("@__dateFilter_Value_Month_0")})) AND (DATEPART('yyyy', `o`.`OrderDate`) = {AssertSqlHelper.Parameter("@__dateFilter_Value_Year_1")}))",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE False = True");
        }

        public override async Task Parameter_extraction_short_circuits_3(bool isAsync)
        {
            await base.Parameter_extraction_short_circuits_3(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__dateFilter_Value_Month_0='7'")}

{AssertSqlHelper.Declaration("@__dateFilter_Value_Year_1='1996'")}

SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`OrderID` < 10400) OR ((`o`.`OrderDate` IS NOT NULL AND (DATEPART('m', `o`.`OrderDate`) = {AssertSqlHelper.Parameter("@__dateFilter_Value_Month_0")})) AND (DATEPART('yyyy', `o`.`OrderDate`) = {AssertSqlHelper.Parameter("@__dateFilter_Value_Year_1")}))",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`");
        }

        public override async Task Subquery_member_pushdown_does_not_change_original_subquery_model(bool isAsync)
        {
            await base.Subquery_member_pushdown_does_not_change_original_subquery_model(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='3'")}

SELECT `t`.`OrderID` AS `OrderId`, (
    SELECT TOP 1 `c`.`City`
    FROM `Customers` AS `c`
    WHERE `c`.`CustomerID` = `t`.`CustomerID`) AS `City`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
    FROM `Orders` AS `o`
    ORDER BY `o`.`OrderID`
) AS `t`
ORDER BY (
    SELECT TOP 1 `c0`.`City`
    FROM `Customers` AS `c0`
    WHERE `c0`.`CustomerID` = `t`.`CustomerID`)");
        }

        public override async Task Subquery_member_pushdown_does_not_change_original_subquery_model2(bool isAsync)
        {
            await base.Subquery_member_pushdown_does_not_change_original_subquery_model2(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='3'")}

SELECT `t`.`OrderID` AS `OrderId`, (
    SELECT TOP 1 `c`.`City`
    FROM `Customers` AS `c`
    WHERE `c`.`CustomerID` = `t`.`CustomerID`) AS `City`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
    FROM `Orders` AS `o`
    ORDER BY `o`.`OrderID`
) AS `t`
ORDER BY (
    SELECT TOP 1 `c0`.`City`
    FROM `Customers` AS `c0`
    WHERE `c0`.`CustomerID` = `t`.`CustomerID`)");
        }

        public override async Task Query_expression_with_to_string_and_contains(bool isAsync)
        {
            await base.Query_expression_with_to_string_and_contains(isAsync);

            AssertSql(
                $@"SELECT `o`.`CustomerID`
FROM `Orders` AS `o`
WHERE `o`.`OrderDate` IS NOT NULL AND (CHARINDEX('10', CONVERT(VARCHAR(10), `o`.`EmployeeID`)) > 0)");
        }

        public override async Task Select_expression_long_to_string(bool isAsync)
        {
            await base.Select_expression_long_to_string(isAsync);

            AssertSql(
                $@"SELECT CONVERT(VARCHAR(20), CAST(`o`.`OrderID` AS bigint)) AS `ShipName`
FROM `Orders` AS `o`
WHERE `o`.`OrderDate` IS NOT NULL");
        }

        public override async Task Select_expression_int_to_string(bool isAsync)
        {
            await base.Select_expression_int_to_string(isAsync);

            AssertSql(
                $@"SELECT CONVERT(VARCHAR(11), `o`.`OrderID`) AS `ShipName`
FROM `Orders` AS `o`
WHERE `o`.`OrderDate` IS NOT NULL");
        }

        public override async Task ToString_with_formatter_is_evaluated_on_the_client(bool isAsync)
        {
            await base.ToString_with_formatter_is_evaluated_on_the_client(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`
FROM `Orders` AS `o`
WHERE `o`.`OrderDate` IS NOT NULL",
                //
                $@"SELECT `o`.`OrderID`
FROM `Orders` AS `o`
WHERE `o`.`OrderDate` IS NOT NULL");
        }

        public override async Task Select_expression_other_to_string(bool isAsync)
        {
            await base.Select_expression_other_to_string(isAsync);

            AssertSql(
                $@"SELECT CONVERT(VARCHAR(100), `o`.`OrderDate`) AS `ShipName`
FROM `Orders` AS `o`
WHERE `o`.`OrderDate` IS NOT NULL");
        }

        public override async Task Select_expression_date_add_year(bool isAsync)
        {
            await base.Select_expression_date_add_year(isAsync);

            AssertSql(
                $@"SELECT DATEADD('yyyy', 1, `o`.`OrderDate`) AS `OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`OrderDate` IS NOT NULL");
        }

        public override async Task Select_expression_datetime_add_month(bool isAsync)
        {
            await base.Select_expression_datetime_add_month(isAsync);

            AssertSql(
                $@"SELECT DATEADD('m', 1, `o`.`OrderDate`) AS `OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`OrderDate` IS NOT NULL");
        }

        public override async Task Select_expression_datetime_add_hour(bool isAsync)
        {
            await base.Select_expression_datetime_add_hour(isAsync);

            AssertSql(
                $@"SELECT DATEADD('h', CAST(1.0E0 AS int), `o`.`OrderDate`) AS `OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`OrderDate` IS NOT NULL");
        }

        public override async Task Select_expression_datetime_add_minute(bool isAsync)
        {
            await base.Select_expression_datetime_add_minute(isAsync);

            AssertSql(
                $@"SELECT DATEADD('n', CAST(1.0E0 AS int), `o`.`OrderDate`) AS `OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`OrderDate` IS NOT NULL");
        }

        public override async Task Select_expression_datetime_add_second(bool isAsync)
        {
            await base.Select_expression_datetime_add_second(isAsync);

            AssertSql(
                $@"SELECT DATEADD('s', CAST(1.0E0 AS int), `o`.`OrderDate`) AS `OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`OrderDate` IS NOT NULL");
        }

        public override async Task Select_expression_date_add_milliseconds_above_the_range(bool isAsync)
        {
            await base.Select_expression_date_add_milliseconds_above_the_range(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`OrderDate` IS NOT NULL");
        }

        public override async Task Select_expression_date_add_milliseconds_below_the_range(bool isAsync)
        {
            await base.Select_expression_date_add_milliseconds_below_the_range(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`OrderDate` IS NOT NULL");
        }

        public override async Task Select_expression_date_add_milliseconds_large_number_divided(bool isAsync)
        {
            await base.Select_expression_date_add_milliseconds_large_number_divided(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__millisecondsPerDay_0='86400000'")}

SELECT DATEADD(millisecond, IIf(CAST((CAST(DATEPART(millisecond, `o`.`OrderDate`) AS bigint) MOD {AssertSqlHelper.Parameter("@__millisecondsPerDay_0")}) IS NULL, NULL, CDBL(CAST((CAST(DATEPART(millisecond, `o`.`OrderDate`) AS bigint) MOD 1{AssertSqlHelper.Parameter("@__millisecondsPerDay_0")}))) AS int), DATEADD('d', IIf(CAST((CAST(DATEPART(millisecond, `o`.`OrderDate`) AS bigint) / {AssertSqlHelper.Parameter("@__millisecondsPerDay_0")}) IS NULL, NULL, CDBL(CAST((CAST(DATEPART(millisecond, `o`.`OrderDate`) AS bigint) / {AssertSqlHelper.Parameter("@__millisecondsPerDay_0")}))) AS int), `o`.`OrderDate`)) AS `OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`OrderDate` IS NOT NULL");
        }

        public override async Task Select_expression_references_are_updated_correctly_with_subquery(bool isAsync)
        {
            await base.Select_expression_references_are_updated_correctly_with_subquery(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__nextYear_0='2017'")}

SELECT DISTINCT DATEPART('yyyy', `o`.`OrderDate`)
FROM `Orders` AS `o`
WHERE `o`.`OrderDate` IS NOT NULL AND (DATEPART('yyyy', `o`.`OrderDate`) < {AssertSqlHelper.Parameter("@__nextYear_0")})");
        }

        public override void DefaultIfEmpty_without_group_join()
        {
            base.DefaultIfEmpty_without_group_join();

            AssertSql(
                $@"SELECT `t`.`CustomerID`
FROM (
    SELECT NULL AS `empty`
) AS `empty`
LEFT JOIN (
    SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    WHERE `c`.`City` = 'London'
) AS `t` ON 1 = 1
WHERE `t`.`CustomerID` IS NOT NULL");
        }

        public override async Task DefaultIfEmpty_in_subquery(bool isAsync)
        {
            await base.DefaultIfEmpty_in_subquery(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `o`.`OrderID`
FROM `Customers` AS `c`
LEFT JOIN `Orders` AS `o` ON `c`.`CustomerID` = `o`.`CustomerID`
WHERE `o`.`OrderID` IS NOT NULL");
        }

        public override async Task DefaultIfEmpty_in_subquery_not_correlated(bool isAsync)
        {
            await base.DefaultIfEmpty_in_subquery_not_correlated(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `t0`.`OrderID`
FROM `Customers` AS `c`,
(
    SELECT `t`.`OrderID`, `t`.`CustomerID`, `t`.`EmployeeID`, `t`.`OrderDate`
    FROM (
        SELECT NULL AS `empty`
    ) AS `empty`
    LEFT JOIN (
        SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
        FROM `Orders` AS `o`
        WHERE `o`.`OrderID` > 15000
    ) AS `t` ON 1 = 1
) AS `t0`");
        }

        public override async Task DefaultIfEmpty_in_subquery_nested(bool isAsync)
        {
            await base.DefaultIfEmpty_in_subquery_nested(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `t0`.`OrderID`, `o0`.`OrderDate`
FROM `Customers` AS `c`,
(
    SELECT `t`.`OrderID`, `t`.`CustomerID`, `t`.`EmployeeID`, `t`.`OrderDate`
    FROM (
        SELECT NULL AS `empty`
    ) AS `empty`
    LEFT JOIN (
        SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
        FROM `Orders` AS `o`
        WHERE `o`.`OrderID` > 15000
    ) AS `t` ON 1 = 1
) AS `t0`
LEFT JOIN `Orders` AS `o0` ON `c`.`CustomerID` = `o0`.`CustomerID`
WHERE (`c`.`City` = 'Seattle') AND (`t0`.`OrderID` IS NOT NULL AND `o0`.`OrderID` IS NOT NULL)
ORDER BY `t0`.`OrderID`, `o0`.`OrderDate`");
        }

        public override async Task OrderBy_skip_take(bool isAsync)
        {
            await base.OrderBy_skip_take(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='5'")}

{AssertSqlHelper.Declaration("@__p_1='8'")}

SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
ORDER BY `c`.`ContactTitle`, `c`.`ContactName`
SKIP {AssertSqlHelper.Parameter("@__p_0")} FETCH NEXT {AssertSqlHelper.Parameter("@__p_1")} ROWS ONLY");
        }

        public override async Task OrderBy_skip_skip_take(bool isAsync)
        {
            await base.OrderBy_skip_skip_take(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='5'")}

{AssertSqlHelper.Declaration("@__p_1='8'")}

{AssertSqlHelper.Declaration("@__p_2='3'")}

SELECT `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`
FROM (
    SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    ORDER BY `c`.`ContactTitle`, `c`.`ContactName`
    SKIP {AssertSqlHelper.Parameter("@__p_0")}
) AS `t`
ORDER BY `t`.`ContactTitle`, `t`.`ContactName`
SKIP {AssertSqlHelper.Parameter("@__p_1")} FETCH NEXT {AssertSqlHelper.Parameter("@__p_2")} ROWS ONLY");
        }

        public override async Task OrderBy_skip_take_take(bool isAsync)
        {
            await base.OrderBy_skip_take_take(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_2='3'")}

{AssertSqlHelper.Declaration("@__p_0='5'")}

{AssertSqlHelper.Declaration("@__p_1='8'")}

SELECT TOP {AssertSqlHelper.Parameter("@__p_2")} `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`
FROM (
    SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    ORDER BY `c`.`ContactTitle`, `c`.`ContactName`
    SKIP {AssertSqlHelper.Parameter("@__p_0")} FETCH NEXT {AssertSqlHelper.Parameter("@__p_1")} ROWS ONLY
) AS `t`
ORDER BY `t`.`ContactTitle`, `t`.`ContactName`");
        }

        public override async Task OrderBy_skip_take_take_take_take(bool isAsync)
        {
            await base.OrderBy_skip_take_take_take_take(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='5'")}

{AssertSqlHelper.Declaration("@__p_3='8'")}

{AssertSqlHelper.Declaration("@__p_2='10'")}

{AssertSqlHelper.Declaration("@__p_1='15'")}

SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `t1`.`CustomerID`, `t1`.`Address`, `t1`.`City`, `t1`.`CompanyName`, `t1`.`ContactName`, `t1`.`ContactTitle`, `t1`.`Country`, `t1`.`Fax`, `t1`.`Phone`, `t1`.`PostalCode`, `t1`.`Region`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_3")} `t0`.`CustomerID`, `t0`.`Address`, `t0`.`City`, `t0`.`CompanyName`, `t0`.`ContactName`, `t0`.`ContactTitle`, `t0`.`Country`, `t0`.`Fax`, `t0`.`Phone`, `t0`.`PostalCode`, `t0`.`Region`
    FROM (
        SELECT TOP {AssertSqlHelper.Parameter("@__p_2")} `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`
        FROM (
            SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
            FROM `Customers` AS `c`
            ORDER BY `c`.`ContactTitle`, `c`.`ContactName`
            SKIP {AssertSqlHelper.Parameter("@__p_0")} FETCH NEXT {AssertSqlHelper.Parameter("@__p_1")} ROWS ONLY
        ) AS `t`
        ORDER BY `t`.`ContactTitle`, `t`.`ContactName`
    ) AS `t0`
    ORDER BY `t0`.`ContactTitle`, `t0`.`ContactName`
) AS `t1`
ORDER BY `t1`.`ContactTitle`, `t1`.`ContactName`");
        }

        public override async Task OrderBy_skip_take_skip_take_skip(bool isAsync)
        {
            await base.OrderBy_skip_take_skip_take_skip(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='5'")}

{AssertSqlHelper.Declaration("@__p_1='15'")}

{AssertSqlHelper.Declaration("@__p_2='2'")}

{AssertSqlHelper.Declaration("@__p_3='8'")}

SELECT `t0`.`CustomerID`, `t0`.`Address`, `t0`.`City`, `t0`.`CompanyName`, `t0`.`ContactName`, `t0`.`ContactTitle`, `t0`.`Country`, `t0`.`Fax`, `t0`.`Phone`, `t0`.`PostalCode`, `t0`.`Region`
FROM (
    SELECT `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`
    FROM (
        SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
        FROM `Customers` AS `c`
        ORDER BY `c`.`ContactTitle`, `c`.`ContactName`
        SKIP {AssertSqlHelper.Parameter("@__p_0")} FETCH NEXT {AssertSqlHelper.Parameter("@__p_1")} ROWS ONLY
    ) AS `t`
    ORDER BY `t`.`ContactTitle`, `t`.`ContactName`
    SKIP {AssertSqlHelper.Parameter("@__p_2")} FETCH NEXT {AssertSqlHelper.Parameter("@__p_3")} ROWS ONLY
) AS `t0`
ORDER BY `t0`.`ContactTitle`, `t0`.`ContactName`
SKIP {AssertSqlHelper.Parameter("@__p_0")}");
        }

        public override async Task OrderBy_skip_take_distinct(bool isAsync)
        {
            await base.OrderBy_skip_take_distinct(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='5'")}

{AssertSqlHelper.Declaration("@__p_1='15'")}

SELECT DISTINCT `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`
FROM (
    SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    ORDER BY `c`.`ContactTitle`, `c`.`ContactName`
    SKIP {AssertSqlHelper.Parameter("@__p_0")} FETCH NEXT {AssertSqlHelper.Parameter("@__p_1")} ROWS ONLY
) AS `t`");
        }

        public override async Task OrderBy_coalesce_take_distinct(bool isAsync)
        {
            await base.OrderBy_coalesce_take_distinct(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='15'")}

SELECT DISTINCT `t`.`ProductID`, `t`.`Discontinued`, `t`.`ProductName`, `t`.`SupplierID`, `t`.`UnitPrice`, `t`.`UnitsInStock`
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `p`.`ProductID`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice`, `p`.`UnitsInStock`, IIf(`p`.`UnitPrice` IS NULL, NULL, `p`.`UnitPrice`) AS `c`
    FROM `Products` AS `p`
    ORDER BY IIf(`p`.`UnitPrice` IS NULL, NULL, `p`.`UnitPrice`)
) AS `t`");
        }

        public override async Task OrderBy_coalesce_skip_take_distinct(bool isAsync)
        {
            await base.OrderBy_coalesce_skip_take_distinct(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='5'")}

{AssertSqlHelper.Declaration("@__p_1='15'")}

SELECT DISTINCT `t`.`ProductID`, `t`.`Discontinued`, `t`.`ProductName`, `t`.`SupplierID`, `t`.`UnitPrice`, `t`.`UnitsInStock`
FROM (
    SELECT `p`.`ProductID`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice`, `p`.`UnitsInStock`, IIf(`p`.`UnitPrice` IS NULL, NULL, `p`.`UnitPrice`) AS `c`
    FROM `Products` AS `p`
    ORDER BY IIf(`p`.`UnitPrice` IS NULL, NULL, `p`.`UnitPrice`)
    SKIP {AssertSqlHelper.Parameter("@__p_0")} FETCH NEXT {AssertSqlHelper.Parameter("@__p_1")} ROWS ONLY
) AS `t`");
        }

        public override async Task OrderBy_coalesce_skip_take_distinct_take(bool isAsync)
        {
            await base.OrderBy_coalesce_skip_take_distinct_take(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='5'")}

{AssertSqlHelper.Declaration("@__p_1='15'")}

SELECT DISTINCT TOP {AssertSqlHelper.Parameter("@__p_0")} `t`.`ProductID`, `t`.`Discontinued`, `t`.`ProductName`, `t`.`SupplierID`, `t`.`UnitPrice`, `t`.`UnitsInStock`
FROM (
    SELECT `p`.`ProductID`, `p`.`Discontinued`, `p`.`ProductName`, `p`.`SupplierID`, `p`.`UnitPrice`, `p`.`UnitsInStock`, IIf(`p`.`UnitPrice` IS NULL, NULL, `p`.`UnitPrice`) AS `c`
    FROM `Products` AS `p`
    ORDER BY IIf(`p`.`UnitPrice` IS NULL, NULL, `p`.`UnitPrice`)
    SKIP {AssertSqlHelper.Parameter("@__p_0")} FETCH NEXT {AssertSqlHelper.Parameter("@__p_1")} ROWS ONLY
) AS `t`");
        }

        public override async Task OrderBy_skip_take_distinct_orderby_take(bool isAsync)
        {
            await base.OrderBy_skip_take_distinct_orderby_take(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_2='8'")}

{AssertSqlHelper.Declaration("@__p_0='5'")}

{AssertSqlHelper.Declaration("@__p_1='15'")}

SELECT TOP {AssertSqlHelper.Parameter("@__p_2")} `t0`.`CustomerID`, `t0`.`Address`, `t0`.`City`, `t0`.`CompanyName`, `t0`.`ContactName`, `t0`.`ContactTitle`, `t0`.`Country`, `t0`.`Fax`, `t0`.`Phone`, `t0`.`PostalCode`, `t0`.`Region`
FROM (
    SELECT DISTINCT `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`
    FROM (
        SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
        FROM `Customers` AS `c`
        ORDER BY `c`.`ContactTitle`, `c`.`ContactName`
        SKIP {AssertSqlHelper.Parameter("@__p_0")} FETCH NEXT {AssertSqlHelper.Parameter("@__p_1")} ROWS ONLY
    ) AS `t`
) AS `t0`
ORDER BY `t0`.`ContactTitle`");
        }

        public override async Task No_orderby_added_for_fully_translated_manually_constructed_LOJ(bool isAsync)
        {
            await base.No_orderby_added_for_fully_translated_manually_constructed_LOJ(isAsync);

            AssertSql(
                $@"SELECT `e`.`City` AS `City1`, `e0`.`City` AS `City2`
FROM `Employees` AS `e`
LEFT JOIN `Employees` AS `e0` ON `e`.`EmployeeID` = `e0`.`ReportsTo`");
        }

        public override async Task No_orderby_added_for_client_side_GroupJoin_dependent_to_principal_LOJ(bool isAsync)
        {
            await base.No_orderby_added_for_client_side_GroupJoin_dependent_to_principal_LOJ(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID` AS `Id1`, `o`.`EmployeeID`, `o`.`OrderDate`, `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Orders` AS `o`
LEFT JOIN `Customers` AS `c` ON `o`.`CustomerID` = `c`.`CustomerID`");
        }

        public override async Task No_orderby_added_for_client_side_GroupJoin_dependent_to_principal_LOJ_with_additional_join_condition1(
            bool isAsync)
        {
            await base.No_orderby_added_for_client_side_GroupJoin_dependent_to_principal_LOJ_with_additional_join_condition1(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID` AS `Id1`, `o`.`EmployeeID`, `o`.`OrderDate`, `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Orders` AS `o`
LEFT JOIN `Customers` AS `c` ON (`o`.`CustomerID` = `c`.`CustomerID`) AND (`o`.`OrderID` = 10000)");
        }

        public override async Task No_orderby_added_for_client_side_GroupJoin_dependent_to_principal_LOJ_with_additional_join_condition2(
            bool isAsync)
        {
            await base.No_orderby_added_for_client_side_GroupJoin_dependent_to_principal_LOJ_with_additional_join_condition2(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID` AS `Id1`, `o`.`EmployeeID`, `o`.`OrderDate`, `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Orders` AS `o`
LEFT JOIN `Customers` AS `c` ON (`o`.`OrderID` = 10000) AND (`o`.`CustomerID` = `c`.`CustomerID`)");
        }

        public override async Task Orderby_added_for_client_side_GroupJoin_principal_to_dependent_LOJ(bool isAsync)
        {
            await base.Orderby_added_for_client_side_GroupJoin_principal_to_dependent_LOJ(isAsync);

            AssertSql(
                $@"SELECT `e1`.`EmployeeID`, `e1`.`City` AS `City1`, `e1`.`Country`, `e1`.`FirstName`, `e1`.`ReportsTo`, `e1`.`Title`, `e2`.`EmployeeID`, `e2`.`City`, `e2`.`Country`, `e2`.`FirstName`, `e2`.`ReportsTo`, `e2`.`Title`
FROM `Employees` AS `e1`
LEFT JOIN `Employees` AS `e2` ON `e1`.`EmployeeID` = `e2`.`ReportsTo`
ORDER BY `e1`.`EmployeeID`");
        }

        public override async Task Contains_with_DateTime_Date(bool isAsync)
        {
            await base.Contains_with_DateTime_Date(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE CONVERT(date, `o`.`OrderDate`) IN ( #1996-07-04 00:00:00#, #1996-07-16 00:00:00#)",
                //
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE CONVERT(date, `o`.`OrderDate`) IN ( #1996-07-04 00:00:00#)");
        }

        public override async Task Contains_with_subquery_involving_join_binds_to_correct_table(bool isAsync)
        {
            await base.Contains_with_subquery_involving_join_binds_to_correct_table(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE (`o`.`OrderID` > 11000) AND `o`.`OrderID` IN (
    SELECT `o0`.`OrderID`
    FROM `Order Details` AS `o0`
    INNER JOIN `Products` AS `p` ON `o0`.`ProductID` = `p`.`ProductID`
    WHERE `p`.`ProductName` = 'Chai'
)");
        }

        public override async Task Complex_query_with_repeated_query_model_compiles_correctly(bool isAsync)
        {
            await base.Complex_query_with_repeated_query_model_compiles_correctly(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE (`c`.`CustomerID` = 'ALFKI') AND EXISTS (
    SELECT 1
    FROM `Customers` AS `c0`
    WHERE EXISTS (
        SELECT 1
        FROM `Customers` AS `c1`))");
        }

        public override async Task Complex_query_with_repeated_nested_query_model_compiles_correctly(bool isAsync)
        {
            await base.Complex_query_with_repeated_nested_query_model_compiles_correctly(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE (`c`.`CustomerID` = 'ALFKI') AND EXISTS (
    SELECT 1
    FROM `Customers` AS `c0`
    WHERE EXISTS (
        SELECT 1
        FROM `Customers` AS `c1`
        WHERE EXISTS (
            SELECT DISTINCT 1
            FROM (
                SELECT TOP 10 `c2`.`CustomerID`, `c2`.`Address`, `c2`.`City`, `c2`.`CompanyName`, `c2`.`ContactName`, `c2`.`ContactTitle`, `c2`.`Country`, `c2`.`Fax`, `c2`.`Phone`, `c2`.`PostalCode`, `c2`.`Region`
                FROM `Customers` AS `c2`
                ORDER BY `c2`.`CustomerID`
            ) AS `t`)))");
        }

        public override async Task Anonymous_member_distinct_where(bool isAsync)
        {
            await base.Anonymous_member_distinct_where(isAsync);

            AssertSql(
                $@"SELECT DISTINCT `c`.`CustomerID`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` = 'ALFKI'");
        }

        public override async Task Anonymous_member_distinct_orderby(bool isAsync)
        {
            await base.Anonymous_member_distinct_orderby(isAsync);

            AssertSql(
                $@"SELECT `t`.`CustomerID`
FROM (
    SELECT DISTINCT `c`.`CustomerID`
    FROM `Customers` AS `c`
) AS `t`
ORDER BY `t`.`CustomerID`");
        }

        public override async Task Anonymous_member_distinct_result(bool isAsync)
        {
            await base.Anonymous_member_distinct_result(isAsync);

            AssertSql(
                $@"SELECT COUNT(*)
FROM (
    SELECT DISTINCT `c`.`CustomerID`
    FROM `Customers` AS `c`
) AS `t`
WHERE `t`.`CustomerID` LIKE 'A' & '%'");
        }

        public override async Task Anonymous_complex_distinct_where(bool isAsync)
        {
            await base.Anonymous_complex_distinct_where(isAsync);

            AssertSql(
                $@"SELECT DISTINCT `c`.`CustomerID` + `c`.`City` AS `A`
FROM `Customers` AS `c`
WHERE (`c`.`CustomerID` + `c`.`City`) = 'ALFKIBerlin'");
        }

        public override async Task Anonymous_complex_distinct_orderby(bool isAsync)
        {
            await base.Anonymous_complex_distinct_orderby(isAsync);

            AssertSql(
                $@"SELECT `t`.`c` AS `A`
FROM (
    SELECT DISTINCT `c`.`CustomerID` + `c`.`City` AS `c`
    FROM `Customers` AS `c`
) AS `t`
ORDER BY `t`.`c`");
        }

        public override async Task Anonymous_complex_distinct_result(bool isAsync)
        {
            await base.Anonymous_complex_distinct_result(isAsync);

            AssertSql(
                $@"SELECT COUNT(*)
FROM (
    SELECT DISTINCT `c`.`CustomerID` + `c`.`City` AS `c`
    FROM `Customers` AS `c`
) AS `t`
WHERE `t`.`c` IS NOT NULL AND (`t`.`c` LIKE 'A' & '%')");
        }

        public override async Task Anonymous_complex_orderby(bool isAsync)
        {
            await base.Anonymous_complex_orderby(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID` + `c`.`City` AS `A`
FROM `Customers` AS `c`
ORDER BY `c`.`CustomerID` + `c`.`City`");
        }

        public override async Task Anonymous_subquery_orderby(bool isAsync)
        {
            await base.Anonymous_subquery_orderby(isAsync);

            AssertSql(
                $@"SELECT (
    SELECT TOP 1 `o`.`OrderDate`
    FROM `Orders` AS `o`
    WHERE `c`.`CustomerID` = `o`.`CustomerID`
    ORDER BY `o`.`OrderID` DESC) AS `A`
FROM `Customers` AS `c`
WHERE (
    SELECT COUNT(*)
    FROM `Orders` AS `o0`
    WHERE `c`.`CustomerID` = `o0`.`CustomerID`) > 1
ORDER BY (
    SELECT TOP 1 `o1`.`OrderDate`
    FROM `Orders` AS `o1`
    WHERE `c`.`CustomerID` = `o1`.`CustomerID`
    ORDER BY `o1`.`OrderID` DESC)");
        }

        public override async Task DTO_member_distinct_where(bool isAsync)
        {
            await base.DTO_member_distinct_where(isAsync);

            AssertSql(
                $@"SELECT DISTINCT `c`.`CustomerID` AS `Property`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` = 'ALFKI'");
        }

        public override async Task DTO_member_distinct_orderby(bool isAsync)
        {
            await base.DTO_member_distinct_orderby(isAsync);

            AssertSql(
                $@"SELECT `t`.`CustomerID` AS `Property`
FROM (
    SELECT DISTINCT `c`.`CustomerID`
    FROM `Customers` AS `c`
) AS `t`
ORDER BY `t`.`CustomerID`");
        }

        public override async Task DTO_member_distinct_result(bool isAsync)
        {
            await base.DTO_member_distinct_result(isAsync);

            AssertSql(
                $@"SELECT COUNT(*)
FROM (
    SELECT DISTINCT `c`.`CustomerID`
    FROM `Customers` AS `c`
) AS `t`
WHERE `t`.`CustomerID` LIKE 'A' & '%'");
        }

        public override async Task DTO_complex_distinct_where(bool isAsync)
        {
            await base.DTO_complex_distinct_where(isAsync);

            AssertSql(
                $@"SELECT DISTINCT `c`.`CustomerID` + `c`.`City` AS `Property`
FROM `Customers` AS `c`
WHERE (`c`.`CustomerID` + `c`.`City`) = 'ALFKIBerlin'");
        }

        public override async Task DTO_complex_distinct_orderby(bool isAsync)
        {
            await base.DTO_complex_distinct_orderby(isAsync);

            AssertSql(
                $@"SELECT `t`.`c` AS `Property`
FROM (
    SELECT DISTINCT `c`.`CustomerID` + `c`.`City` AS `c`
    FROM `Customers` AS `c`
) AS `t`
ORDER BY `t`.`c`");
        }

        public override async Task DTO_complex_distinct_result(bool isAsync)
        {
            await base.DTO_complex_distinct_result(isAsync);

            AssertSql(
                $@"SELECT COUNT(*)
FROM (
    SELECT DISTINCT `c`.`CustomerID` + `c`.`City` AS `c`
    FROM `Customers` AS `c`
) AS `t`
WHERE `t`.`c` IS NOT NULL AND (`t`.`c` LIKE 'A' & '%')");
        }

        public override async Task DTO_complex_orderby(bool isAsync)
        {
            await base.DTO_complex_orderby(isAsync);

            // issue #15994
//            AssertSql(
//                $@"SELECT `c`.`CustomerID` + `c`.`City` AS `Property`
//FROM `Customers` AS `c`
//ORDER BY `Property`");
        }

        public override async Task DTO_subquery_orderby(bool isAsync)
        {
            await base.DTO_subquery_orderby(isAsync);

            AssertSql(
                $@"SELECT (
    SELECT TOP 1 `o`.`OrderDate`
    FROM `Orders` AS `o`
    WHERE `c`.`CustomerID` = `o`.`CustomerID`
    ORDER BY `o`.`OrderID` DESC) AS `Property`
FROM `Customers` AS `c`
WHERE (
    SELECT COUNT(*)
    FROM `Orders` AS `o0`
    WHERE `c`.`CustomerID` = `o0`.`CustomerID`) > 1
ORDER BY (
    SELECT TOP 1 `o1`.`OrderDate`
    FROM `Orders` AS `o1`
    WHERE `c`.`CustomerID` = `o1`.`CustomerID`
    ORDER BY `o1`.`OrderID` DESC)");
        }

        public override async Task Include_with_orderby_skip_preserves_ordering(bool isAsync)
        {
            await base.Include_with_orderby_skip_preserves_ordering(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='40'")}

{AssertSqlHelper.Declaration("@__p_1='5'")}

SELECT `t`.`CustomerID`, `t`.`Address`, `t`.`City`, `t`.`CompanyName`, `t`.`ContactName`, `t`.`ContactTitle`, `t`.`Country`, `t`.`Fax`, `t`.`Phone`, `t`.`PostalCode`, `t`.`Region`, `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM (
    SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    WHERE (`c`.`CustomerID` <> 'VAFFE') AND (`c`.`CustomerID` <> 'DRACD')
    ORDER BY `c`.`City`, `c`.`CustomerID`
    SKIP {AssertSqlHelper.Parameter("@__p_0")} FETCH NEXT {AssertSqlHelper.Parameter("@__p_1")} ROWS ONLY
) AS `t`
LEFT JOIN `Orders` AS `o` ON `t`.`CustomerID` = `o`.`CustomerID`
ORDER BY `t`.`City`, `t`.`CustomerID`, `o`.`OrderID`");
        }

        public override async Task Int16_parameter_can_be_used_for_int_column(bool isAsync)
        {
            await base.Int16_parameter_can_be_used_for_int_column(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`OrderID` = 10300");
        }

        public override async Task Subquery_is_null_translated_correctly(bool isAsync)
        {
            await base.Subquery_is_null_translated_correctly(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE (
    SELECT TOP 1 `o`.`CustomerID`
    FROM `Orders` AS `o`
    WHERE `c`.`CustomerID` = `o`.`CustomerID`
    ORDER BY `o`.`OrderID` DESC) IS NULL");
        }

        public override async Task Subquery_is_not_null_translated_correctly(bool isAsync)
        {
            await base.Subquery_is_not_null_translated_correctly(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE (
    SELECT TOP 1 `o`.`CustomerID`
    FROM `Orders` AS `o`
    WHERE `c`.`CustomerID` = `o`.`CustomerID`
    ORDER BY `o`.`OrderID` DESC) IS NOT NULL");
        }

        public override async Task Select_take_average(bool isAsync)
        {
            await base.Select_take_average(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='10'")}

SELECT AVG(IIf(`t`.`OrderID` IS NULL, NULL, CDBL(`t`.`OrderID`)))
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `o`.`OrderID`
    FROM `Orders` AS `o`
    ORDER BY `o`.`OrderID`
) AS `t`");
        }

        public override async Task Select_take_count(bool isAsync)
        {
            await base.Select_take_count(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='7'")}

SELECT COUNT(*)
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
) AS `t`");
        }

        public override async Task Select_orderBy_take_count(bool isAsync)
        {
            await base.Select_orderBy_take_count(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='7'")}

SELECT COUNT(*)
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    ORDER BY `c`.`Country`
) AS `t`");
        }

        public override async Task Select_take_long_count(bool isAsync)
        {
            await base.Select_take_long_count(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='7'")}

SELECT COUNT_BIG(*)
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
) AS `t`");
        }

        public override async Task Select_orderBy_take_long_count(bool isAsync)
        {
            await base.Select_orderBy_take_long_count(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='7'")}

SELECT COUNT_BIG(*)
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    ORDER BY `c`.`Country`
) AS `t`");
        }

        public override async Task Select_take_max(bool isAsync)
        {
            await base.Select_take_max(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='10'")}

SELECT MAX(`t`.`OrderID`)
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `o`.`OrderID`
    FROM `Orders` AS `o`
    ORDER BY `o`.`OrderID`
) AS `t`");
        }

        public override async Task Select_take_min(bool isAsync)
        {
            await base.Select_take_min(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='10'")}

SELECT MIN(`t`.`OrderID`)
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `o`.`OrderID`
    FROM `Orders` AS `o`
    ORDER BY `o`.`OrderID`
) AS `t`");
        }

        public override async Task Select_take_sum(bool isAsync)
        {
            await base.Select_take_sum(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='10'")}

SELECT SUM(`t`.`OrderID`)
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `o`.`OrderID`
    FROM `Orders` AS `o`
    ORDER BY `o`.`OrderID`
) AS `t`");
        }

        public override async Task Select_skip_average(bool isAsync)
        {
            await base.Select_skip_average(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='10'")}

SELECT AVG(IIf(`t`.`OrderID` IS NULL, NULL, CDBL(`t`.`OrderID`)))
FROM (
    SELECT `o`.`OrderID`
    FROM `Orders` AS `o`
    ORDER BY `o`.`OrderID`
    SKIP {AssertSqlHelper.Parameter("@__p_0")}
) AS `t`");
        }

        public override async Task Select_skip_count(bool isAsync)
        {
            await base.Select_skip_count(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='7'")}

SELECT COUNT(*)
FROM (
    SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    ORDER BY (SELECT 1)
    SKIP {AssertSqlHelper.Parameter("@__p_0")}
) AS `t`");
        }

        public override async Task Select_orderBy_skip_count(bool isAsync)
        {
            await base.Select_orderBy_skip_count(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='7'")}

SELECT COUNT(*)
FROM (
    SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    ORDER BY `c`.`Country`
    SKIP {AssertSqlHelper.Parameter("@__p_0")}
) AS `t`");
        }

        public override async Task Select_skip_long_count(bool isAsync)
        {
            await base.Select_skip_long_count(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='7'")}

SELECT COUNT_BIG(*)
FROM (
    SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    ORDER BY (SELECT 1)
    SKIP {AssertSqlHelper.Parameter("@__p_0")}
) AS `t`");
        }

        public override async Task Select_orderBy_skip_long_count(bool isAsync)
        {
            await base.Select_orderBy_skip_long_count(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='7'")}

SELECT COUNT_BIG(*)
FROM (
    SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
    ORDER BY `c`.`Country`
    SKIP {AssertSqlHelper.Parameter("@__p_0")}
) AS `t`");
        }

        public override async Task Select_skip_max(bool isAsync)
        {
            await base.Select_skip_max(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='10'")}

SELECT MAX(`t`.`OrderID`)
FROM (
    SELECT `o`.`OrderID`
    FROM `Orders` AS `o`
    ORDER BY `o`.`OrderID`
    SKIP {AssertSqlHelper.Parameter("@__p_0")}
) AS `t`");
        }

        public override async Task Select_skip_min(bool isAsync)
        {
            await base.Select_skip_min(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='10'")}

SELECT MIN(`t`.`OrderID`)
FROM (
    SELECT `o`.`OrderID`
    FROM `Orders` AS `o`
    ORDER BY `o`.`OrderID`
    SKIP {AssertSqlHelper.Parameter("@__p_0")}
) AS `t`");
        }

        public override async Task Select_skip_sum(bool isAsync)
        {
            await base.Select_skip_sum(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='10'")}

SELECT SUM(`t`.`OrderID`)
FROM (
    SELECT `o`.`OrderID`
    FROM `Orders` AS `o`
    ORDER BY `o`.`OrderID`
    SKIP {AssertSqlHelper.Parameter("@__p_0")}
) AS `t`");
        }

        public override async Task Select_distinct_average(bool isAsync)
        {
            await base.Select_distinct_average(isAsync);

            AssertSql(
                $@"SELECT AVG(IIf(`t`.`OrderID` IS NULL, NULL, CDBL(`t`.`OrderID`)))
FROM (
    SELECT DISTINCT `o`.`OrderID`
    FROM `Orders` AS `o`
) AS `t`");
        }

        public override async Task Select_distinct_count(bool isAsync)
        {
            await base.Select_distinct_count(isAsync);

            AssertSql(
                $@"SELECT COUNT(*)
FROM (
    SELECT DISTINCT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
) AS `t`");
        }

        public override async Task Select_distinct_long_count(bool isAsync)
        {
            await base.Select_distinct_long_count(isAsync);

            AssertSql(
                $@"SELECT COUNT_BIG(*)
FROM (
    SELECT DISTINCT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
    FROM `Customers` AS `c`
) AS `t`");
        }

        public override async Task Select_distinct_max(bool isAsync)
        {
            await base.Select_distinct_max(isAsync);

            AssertSql(
                $@"SELECT MAX(`t`.`OrderID`)
FROM (
    SELECT DISTINCT `o`.`OrderID`
    FROM `Orders` AS `o`
) AS `t`");
        }

        public override async Task Select_distinct_min(bool isAsync)
        {
            await base.Select_distinct_min(isAsync);

            AssertSql(
                $@"SELECT MIN(`t`.`OrderID`)
FROM (
    SELECT DISTINCT `o`.`OrderID`
    FROM `Orders` AS `o`
) AS `t`");
        }

        public override async Task Select_distinct_sum(bool isAsync)
        {
            await base.Select_distinct_sum(isAsync);

            AssertSql(
                $@"SELECT SUM(`t`.`OrderID`)
FROM (
    SELECT DISTINCT `o`.`OrderID`
    FROM `Orders` AS `o`
) AS `t`");
        }

        public override async Task Comparing_to_fixed_string_parameter(bool isAsync)
        {
            await base.Comparing_to_fixed_string_parameter(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__prefix_0='A' (Size = 4000)")}

SELECT `c`.`CustomerID`
FROM `Customers` AS `c`
WHERE ({AssertSqlHelper.Parameter("@__prefix_0")} = '') OR (LEFT(`c`.`CustomerID`, LEN({AssertSqlHelper.Parameter("@__prefix_0")})) = {AssertSqlHelper.Parameter("@__prefix_0")})");
        }

        public override async Task Comparing_entities_using_Equals(bool isAsync)
        {
            await base.Comparing_entities_using_Equals(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID` AS `Id1`, `c0`.`CustomerID` AS `Id2`
FROM `Customers` AS `c`,
`Customers` AS `c0`
WHERE (`c`.`CustomerID` LIKE 'ALFKI%') AND (`c`.`CustomerID` = `c0`.`CustomerID`)
ORDER BY `c`.`CustomerID`");
        }

        public override async Task Comparing_different_entity_types_using_Equals(bool isAsync)
        {
            await base.Comparing_different_entity_types_using_Equals(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`
FROM `Customers` AS `c`,
`Orders` AS `o`
WHERE False = True");
        }

        public override async Task Comparing_entity_to_null_using_Equals(bool isAsync)
        {
            await base.Comparing_entity_to_null_using_Equals(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` LIKE 'A' & '%'
ORDER BY `c`.`CustomerID`");
        }

        [ConditionalTheory(Skip = "Can be supported after rearranging CROSS JOIN/JOIN expressions.")]
        public override async Task Comparing_navigations_using_Equals(bool isAsync)
        {
            await base.Comparing_navigations_using_Equals(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID` AS `Id1`, `o0`.`OrderID` AS `Id2`
FROM `Orders` AS `o`,
`Orders` AS `o0`
LEFT JOIN `Customers` AS `c` ON `o`.`CustomerID` = `c`.`CustomerID`
LEFT JOIN `Customers` AS `c0` ON `o0`.`CustomerID` = `c0`.`CustomerID`
WHERE (`o`.`CustomerID` IS NOT NULL AND (`o`.`CustomerID` LIKE 'A' & '%')) AND ((`c`.`CustomerID` = `c0`.`CustomerID`) OR (`c`.`CustomerID` IS NULL AND `c0`.`CustomerID` IS NULL))
ORDER BY `o`.`OrderID`, `o0`.`OrderID`");
        }

        public override async Task Comparing_navigations_using_static_Equals(bool isAsync)
        {
            await base.Comparing_navigations_using_static_Equals(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderID` AS `Id1`, `o0`.`OrderID` AS `Id2`
FROM `Orders` AS `o`,
`Orders` AS `o0`
LEFT JOIN `Customers` AS `c` ON `o`.`CustomerID` = `c`.`CustomerID`
LEFT JOIN `Customers` AS `c0` ON `o0`.`CustomerID` = `c0`.`CustomerID`
WHERE (`o`.`CustomerID` IS NOT NULL AND (`o`.`CustomerID` LIKE 'A' & '%')) AND ((`c`.`CustomerID` = `c0`.`CustomerID`) OR (`c`.`CustomerID` IS NULL AND `c0`.`CustomerID` IS NULL))
ORDER BY `o`.`OrderID`, `o0`.`OrderID`");
        }

        public override async Task Comparing_non_matching_entities_using_Equals(bool isAsync)
        {
            await base.Comparing_non_matching_entities_using_Equals(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID` AS `Id1`, `o`.`OrderID` AS `Id2`
FROM `Customers` AS `c`,
`Orders` AS `o`
WHERE False = True");
        }

        public override async Task Comparing_non_matching_collection_navigations_using_Equals(bool isAsync)
        {
            await base.Comparing_non_matching_collection_navigations_using_Equals(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID` AS `Id1`, `o`.`OrderID` AS `Id2`
FROM `Customers` AS `c`,
`Orders` AS `o`
WHERE False = True");
        }

        public override async Task Comparing_collection_navigation_to_null(bool isAsync)
        {
            await base.Comparing_collection_navigation_to_null(isAsync);

            // issue #15994
//            AssertSql(
//                $@"SELECT `c`.`CustomerID`
//FROM `Customers` AS `c`
//WHERE `c`.`CustomerID` IS NULL");
        }

        public override async Task Comparing_collection_navigation_to_null_complex(bool isAsync)
        {
            await base.Comparing_collection_navigation_to_null_complex(isAsync);

            AssertSql(
                $@"SELECT `o`.`ProductID`, `o`.`OrderID`
FROM `Order Details` AS `o`
INNER JOIN `Orders` AS `o0` ON `o`.`OrderID` = `o0`.`OrderID`
LEFT JOIN `Customers` AS `c` ON `o0`.`CustomerID` = `c`.`CustomerID`
WHERE (`o`.`OrderID` < 10250) AND `c`.`CustomerID` IS NOT NULL
ORDER BY `o`.`OrderID`, `o`.`ProductID`");
        }

        public override async Task Compare_collection_navigation_with_itself(bool isAsync)
        {
            await base.Compare_collection_navigation_with_itself(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` LIKE 'A' & '%'");
        }

        public override async Task Compare_two_collection_navigations_with_different_query_sources(bool isAsync)
        {
            await base.Compare_two_collection_navigations_with_different_query_sources(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID` AS `Id1`, `c0`.`CustomerID` AS `Id2`
FROM `Customers` AS `c`,
`Customers` AS `c0`
WHERE ((`c`.`CustomerID` = 'ALFKI') AND (`c0`.`CustomerID` = 'ALFKI')) AND (`c`.`CustomerID` = `c0`.`CustomerID`)");
        }

        public override async Task Compare_two_collection_navigations_using_equals(bool isAsync)
        {
            await base.Compare_two_collection_navigations_using_equals(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID` AS `Id1`, `c0`.`CustomerID` AS `Id2`
FROM `Customers` AS `c`,
`Customers` AS `c0`
WHERE ((`c`.`CustomerID` = 'ALFKI') AND (`c0`.`CustomerID` = 'ALFKI')) AND (`c`.`CustomerID` = `c0`.`CustomerID`)");
        }

        public override async Task Compare_two_collection_navigations_with_different_property_chains(bool isAsync)
        {
            await base.Compare_two_collection_navigations_with_different_property_chains(isAsync);

            // issue #15994
//            AssertSql(
//                $@"SELECT `c`.`CustomerID` AS `Id1`, `o`.`OrderID` AS `Id2`
//FROM `Customers` AS `c`
//CROSS JOIN `Orders` AS `o`
//LEFT JOIN `Customers` AS [join.Customer] ON `o`.`CustomerID` = [join.Customer].`CustomerID`
//WHERE (`c`.`CustomerID` = 'ALFKI') AND (`c`.`CustomerID` = [join.Customer].`CustomerID`)
//ORDER BY `Id1`, `Id2`");
        }

        public override async Task OrderBy_ThenBy_same_column_different_direction(bool isAsync)
        {
            await base.OrderBy_ThenBy_same_column_different_direction(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` LIKE 'A' & '%'
ORDER BY `c`.`CustomerID`");
        }

        public override async Task OrderBy_OrderBy_same_column_different_direction(bool isAsync)
        {
            await base.OrderBy_OrderBy_same_column_different_direction(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` LIKE 'A' & '%'
ORDER BY `c`.`CustomerID` DESC");
        }

        public override async Task Complex_nested_query_doesnt_try_binding_to_grandparent_when_parent_returns_complex_result(bool isAsync)
        {
            await base.Complex_nested_query_doesnt_try_binding_to_grandparent_when_parent_returns_complex_result(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `t`.`c`, `t`.`CustomerID`, `t`.`OrderID`
FROM `Customers` AS `c`
OUTER APPLY (
    SELECT (
        SELECT COUNT(*)
        FROM `Orders` AS `o`
        WHERE `c`.`CustomerID` = `o`.`CustomerID`) AS `c`, `c`.`CustomerID`, `o0`.`OrderID`
    FROM `Orders` AS `o0`
    WHERE `c`.`CustomerID` = `o0`.`CustomerID`
) AS `t`
WHERE `c`.`CustomerID` = 'ALFKI'
ORDER BY `c`.`CustomerID`, `t`.`OrderID`");
        }

        public override async Task Complex_nested_query_properly_binds_to_grandparent_when_parent_returns_scalar_result(bool isAsync)
        {
            await base.Complex_nested_query_properly_binds_to_grandparent_when_parent_returns_scalar_result(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, (
    SELECT COUNT(*)
    FROM `Orders` AS `o`
    WHERE (`c`.`CustomerID` = `o`.`CustomerID`) AND ((
        SELECT COUNT(*)
        FROM `Orders` AS `o0`
        WHERE `c`.`CustomerID` = `o0`.`CustomerID`) > 0)) AS `OuterOrders`
FROM `Customers` AS `c`
WHERE `c`.`CustomerID` = 'ALFKI'");
        }

        public override async Task OrderBy_Dto_projection_skip_take(bool isAsync)
        {
            await base.OrderBy_Dto_projection_skip_take(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='5'")}

{AssertSqlHelper.Declaration("@__p_1='10'")}

SELECT `c`.`CustomerID` AS `Id`
FROM `Customers` AS `c`
ORDER BY `c`.`CustomerID`
SKIP {AssertSqlHelper.Parameter("@__p_0")} FETCH NEXT {AssertSqlHelper.Parameter("@__p_1")} ROWS ONLY");
        }

        public override void Streaming_chained_sync_query()
        {
            base.Streaming_chained_sync_query();

            AssertSql(
                $@"SELECT `c`.`CustomerID`
FROM `Customers` AS `c`",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_CustomerID='ALFKI' (Size = 5)")}

SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`CustomerID` = {AssertSqlHelper.Parameter("@_outer_CustomerID")}",
                //
                $@"SELECT [y.Customer].`CustomerID`, [y.Customer].`Address`, [y.Customer].`City`, [y.Customer].`CompanyName`, [y.Customer].`ContactName`, [y.Customer].`ContactTitle`, [y.Customer].`Country`, [y.Customer].`Fax`, [y.Customer].`Phone`, [y.Customer].`PostalCode`, [y.Customer].`Region`
FROM `Customers` AS [y.Customer]",
                //
                $@"{AssertSqlHelper.Declaration("@_outer_CustomerID='ANATR' (Size = 5)")}

SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
WHERE `o`.`CustomerID` = {AssertSqlHelper.Parameter("@_outer_CustomerID")}");
        }

        public override async Task Join_take_count_works(bool isAsync)
        {
            await base.Join_take_count_works(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='5'")}

SELECT COUNT(*)
FROM (
    SELECT TOP {AssertSqlHelper.Parameter("@__p_0")} `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`, `t`.`CustomerID` AS `CustomerID0`
    FROM `Orders` AS `o`
    INNER JOIN (
        SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
        FROM `Customers` AS `c`
        WHERE `c`.`CustomerID` = 'ALFKI'
    ) AS `t` ON `o`.`CustomerID` = `t`.`CustomerID`
    WHERE (`o`.`OrderID` > 690) AND (`o`.`OrderID` < 710)
) AS `t0`");
        }

        public override async Task OrderBy_empty_list_contains(bool isAsync)
        {
            await base.OrderBy_empty_list_contains(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`");
        }

        public override async Task OrderBy_empty_list_does_not_contains(bool isAsync)
        {
            await base.OrderBy_empty_list_does_not_contains(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`");
        }

        public override void Manual_expression_tree_typed_null_equality()
        {
            base.Manual_expression_tree_typed_null_equality();

            AssertSql(
                $@"SELECT `c`.`City`
FROM `Orders` AS `o`
LEFT JOIN `Customers` AS `c` ON `o`.`CustomerID` = `c`.`CustomerID`
WHERE `o`.`OrderID` < 10300");
        }

        public override async Task Let_subquery_with_multiple_occurrences(bool isAsync)
        {
            await base.Let_subquery_with_multiple_occurrences(isAsync);

            AssertSql(
                $@"SELECT (
    SELECT COUNT(*)
    FROM `Order Details` AS `o`
    WHERE (`o0`.`OrderID` = `o`.`OrderID`) AND (`o`.`Quantity` < 10)) AS `Count`
FROM `Orders` AS `o0`
WHERE EXISTS (
    SELECT 1
    FROM `Order Details` AS `o1`
    WHERE (`o0`.`OrderID` = `o1`.`OrderID`) AND (`o1`.`Quantity` < 10))");
        }

        public override async Task Let_entity_equality_to_null(bool isAsync)
        {
            await base.Let_entity_equality_to_null(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, (
    SELECT TOP 1 `o`.`OrderDate`
    FROM `Orders` AS `o`
    WHERE `c`.`CustomerID` = `o`.`CustomerID`
    ORDER BY `o`.`OrderDate`) AS `OrderDate`
FROM `Customers` AS `c`
WHERE (`c`.`CustomerID` LIKE 'A' & '%') AND (
    SELECT TOP 1 `o0`.`OrderID`
    FROM `Orders` AS `o0`
    WHERE `c`.`CustomerID` = `o0`.`CustomerID`
    ORDER BY `o0`.`OrderDate`) IS NOT NULL");
        }

        public override async Task Let_entity_equality_to_other_entity(bool isAsync)
        {
            await base.Let_entity_equality_to_other_entity(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, (
    SELECT TOP 1 `o`.`OrderDate`
    FROM `Orders` AS `o`
    WHERE `c`.`CustomerID` = `o`.`CustomerID`
    ORDER BY `o`.`OrderDate`) AS `A`
FROM `Customers` AS `c`
WHERE (`c`.`CustomerID` LIKE 'A' & '%') AND (((
    SELECT TOP 1 `o0`.`OrderID`
    FROM `Orders` AS `o0`
    WHERE `c`.`CustomerID` = `o0`.`CustomerID`
    ORDER BY `o0`.`OrderDate`) <> 0) OR (
    SELECT TOP 1 `o0`.`OrderID`
    FROM `Orders` AS `o0`
    WHERE `c`.`CustomerID` = `o0`.`CustomerID`
    ORDER BY `o0`.`OrderDate`) IS NULL)");
        }

        public override async Task Collection_navigation_equal_to_null_for_subquery(bool isAsync)
        {
            await base.Collection_navigation_equal_to_null_for_subquery(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE (
    SELECT TOP 1 `o`.`OrderID`
    FROM `Orders` AS `o`
    WHERE `c`.`CustomerID` = `o`.`CustomerID`
    ORDER BY `o`.`OrderID`) IS NULL");
        }

        public override async Task Dependent_to_principal_navigation_equal_to_null_for_subquery(bool isAsync)
        {
            await base.Dependent_to_principal_navigation_equal_to_null_for_subquery(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE (
    SELECT TOP 1 `c0`.`CustomerID`
    FROM `Orders` AS `o`
    LEFT JOIN `Customers` AS `c0` ON `o`.`CustomerID` = `c0`.`CustomerID`
    WHERE `c`.`CustomerID` = `o`.`CustomerID`
    ORDER BY `o`.`OrderID`) IS NULL");
        }

        public override async Task Collection_navigation_equality_rewrite_for_subquery(bool isAsync)
        {
            await base.Collection_navigation_equality_rewrite_for_subquery(isAsync);

            // issue #15994
//            AssertSql(
//                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
//FROM `Customers` AS `c`
//WHERE `c`.`CustomerID` LIKE 'A' & '%' AND ((
//    SELECT TOP 1 `o`.`OrderID`
//    FROM `Orders` AS `o`
//    WHERE `o`.`OrderID` < 10300
//    ORDER BY `o`.`OrderID`
//) = (
//    SELECT TOP 1 `o0`.`OrderID`
//    FROM `Orders` AS `o0`
//    WHERE `o0`.`OrderID` > 10500
//    ORDER BY `o0`.`OrderID`
//))");
        }

        public override async Task Inner_parameter_in_nested_lambdas_gets_preserved(bool isAsync)
        {
            await base.Inner_parameter_in_nested_lambdas_gets_preserved(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `c`.`Address`, `c`.`City`, `c`.`CompanyName`, `c`.`ContactName`, `c`.`ContactTitle`, `c`.`Country`, `c`.`Fax`, `c`.`Phone`, `c`.`PostalCode`, `c`.`Region`
FROM `Customers` AS `c`
WHERE (
    SELECT COUNT(*)
    FROM `Orders` AS `o`
    WHERE (`c`.`CustomerID` = `o`.`CustomerID`) AND (`c`.`CustomerID` = `o`.`CustomerID`)) > 0");
        }

        public override async Task Convert_to_nullable_on_nullable_value_is_ignored(bool isAsync)
        {
            await base.Convert_to_nullable_on_nullable_value_is_ignored(isAsync);

            AssertSql(
                $@"SELECT `o`.`OrderDate`
FROM `Orders` AS `o`");
        }

        public override async Task Navigation_inside_interpolated_string_is_expanded(bool isAsync)
        {
            await base.Navigation_inside_interpolated_string_is_expanded(isAsync);

            AssertSql(
                $@"SELECT `c`.`City`
FROM `Orders` AS `o`
LEFT JOIN `Customers` AS `c` ON `o`.`CustomerID` = `c`.`CustomerID`");
        }

        public override async Task OrderBy_object_type_server_evals(bool isAsync)
        {
            await base.OrderBy_object_type_server_evals(isAsync);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@__p_0='0'")}

{AssertSqlHelper.Declaration("@__p_1='20'")}

SELECT `o`.`OrderID`, `o`.`CustomerID`, `o`.`EmployeeID`, `o`.`OrderDate`
FROM `Orders` AS `o`
LEFT JOIN `Customers` AS `c` ON `o`.`CustomerID` = `c`.`CustomerID`
ORDER BY `o`.`OrderID`, `o`.`OrderDate`, `c`.`CustomerID`, `c`.`City`
SKIP {AssertSqlHelper.Parameter("@__p_0")} FETCH NEXT {AssertSqlHelper.Parameter("@__p_1")} ROWS ONLY");
        }

        public override async Task AsQueryable_in_query_server_evals(bool isAsync)
        {
            await base.AsQueryable_in_query_server_evals(isAsync);

            AssertSql(
                $@"SELECT `c`.`CustomerID`, `t`.`OrderDate`, `t`.`OrderID`
FROM `Customers` AS `c`
OUTER APPLY (
    SELECT TOP 1 `o`.`OrderDate`, `o`.`OrderID`
    FROM `Orders` AS `o`
    WHERE (`c`.`CustomerID` = `o`.`CustomerID`) AND (DATEPART('yyyy', `o`.`OrderDate`) = 1998)
    ORDER BY `o`.`OrderID`
) AS `t`
ORDER BY `c`.`CustomerID`, `t`.`OrderID`");
        }

        private void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertBaseline(expected.Select(s => s.Trim()).ToArray());

        protected override void ClearLog()
            => Fixture.TestSqlLoggerFactory.Clear();
    }
}
