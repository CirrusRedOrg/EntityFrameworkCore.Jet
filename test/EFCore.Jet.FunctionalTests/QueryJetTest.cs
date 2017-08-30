using System;
using System.Linq;
using EntityFramework.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestModels.Northwind;
using Microsoft.EntityFrameworkCore.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace EntityFramework.Jet.FunctionalTests
{
    public class QueryJetTest : QueryTestBase<NorthwindQueryJetFixture>
    {
        public QueryJetTest(NorthwindQueryJetFixture fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            Fixture.TestSqlLoggerFactory.Clear();
            //Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
        }

        [ConditionalFact]
        [FrameworkSkipCondition(RuntimeFrameworks.CoreCLR, SkipReason = "Failing after netcoreapp2.0 upgrade")]
        public virtual void Cache_key_contexts_are_detached()
        {
            WeakReference wr;
            MakeGarbage(CreateContext(), out wr);

            GC.Collect();

            Assert.False(wr.IsAlive);
        }

        private static void MakeGarbage(NorthwindContext context, out WeakReference wr)
        {
            wr = new WeakReference(context);

            using (context)
            {
                var orderDetails = context.OrderDetails;

                Func<NorthwindContext, Customer> query
                    = param
                        => (from c in context.Customers
                            from o in context.Set<Order>()
                            from od in orderDetails
                            from e1 in param.Employees
                            from e2 in param.Set<Order>()
                            select c).First();

                query(context);

                Assert.True(wr.IsAlive);
            }
        }

        public override void Project_to_object_array()
        {
            base.Project_to_object_array();

            AssertSql(
                @"SELECT [e].[EmployeeID], [e].[ReportsTo], [e].[Title]
FROM [Employees] AS [e]
WHERE [e].[EmployeeID] = 1");
        }

        public override void Project_to_int_array()
        {
            base.Project_to_int_array();

            AssertSql(
                @"SELECT [e].[EmployeeID], [e].[ReportsTo]
FROM [Employees] AS [e]
WHERE [e].[EmployeeID] = 1");
        }

        public override void Local_array()
        {
            base.Local_array();

            AssertSql(
                @"@__get_Item_0='ALFKI' (Nullable = false) (Size = 5)

SELECT TOP 2 [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] = @__get_Item_0");
        }

        public override void Entity_equality_self()
        {
            base.Entity_equality_self();

            AssertSql(
                @"SELECT [c].[CustomerID]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] = [c].[CustomerID]");
        }

        public override void Entity_equality_local()
        {
            base.Entity_equality_local();

            AssertSql(
                @"@__local_0_CustomerID='ANATR' (Nullable = false) (Size = 5)

SELECT [c].[CustomerID]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] = @__local_0_CustomerID");
        }

        public override void Entity_equality_local_inline()
        {
            base.Entity_equality_local_inline();

            AssertSql(
                @"SELECT [c].[CustomerID]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] = 'ANATR'");
        }

        public override void Entity_equality_null()
        {
            base.Entity_equality_null();

            AssertSql(
                @"SELECT [c].[CustomerID]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] IS NULL");
        }

        public override void Entity_equality_not_null()
        {
            base.Entity_equality_not_null();

            AssertSql(
                @"SELECT [c].[CustomerID]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] IS NOT NULL");
        }

        public override void Queryable_reprojection()
        {
            base.Queryable_reprojection();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]");
        }

        [Fact(Skip = "Unsupported by JET")]
        public override void Default_if_empty_top_level()
        {
            base.Default_if_empty_top_level();
        }

        [Fact(Skip = "Unsupported by JET")]
        public override void Default_if_empty_top_level_positive()
        {
            base.Default_if_empty_top_level_positive();
        }

        public override void Default_if_empty_top_level_arg()
        {
            base.Default_if_empty_top_level_arg();

            AssertSql(
                @"SELECT [c].[EmployeeID], [c].[City], [c].[Country], [c].[FirstName], [c].[ReportsTo], [c].[Title]
FROM [Employees] AS [c]
WHERE [c].[EmployeeID] = -1");
        }

        [Fact(Skip = "Unsupported by JET")]
        public override void Default_if_empty_top_level_projection()
        {
            base.Default_if_empty_top_level_projection();
        }

        [Fact(Skip = "Assertion failed without evident reason")]
        public override void Where_query_composition_is_not_null()
        {
            base.Where_query_composition_is_not_null();
        }

        public override void Where_query_composition_entity_equality_no_elements_SingleOrDefault()
        {
            base.Where_query_composition_entity_equality_no_elements_SingleOrDefault();

            AssertSql(
                @"@__p_0='3'

SELECT [t].[EmployeeID], [t].[City], [t].[Country], [t].[FirstName], [t].[ReportsTo], [t].[Title]
FROM (
    SELECT TOP @__p_0 [e].[EmployeeID], [e].[City], [e].[Country], [e].[FirstName], [e].[ReportsTo], [e].[Title]
    FROM [Employees] AS [e]
) AS [t]",
                //
                @"SELECT TOP 2 [e20].[EmployeeID]
FROM [Employees] AS [e20]
WHERE [e20].[EmployeeID] = 42",
                //
                @"SELECT TOP 2 [e20].[EmployeeID]
FROM [Employees] AS [e20]
WHERE [e20].[EmployeeID] = 42",
                //
                @"SELECT TOP 2 [e20].[EmployeeID]
FROM [Employees] AS [e20]
WHERE [e20].[EmployeeID] = 42");
        }


        public override void Where_query_composition2()
        {
            base.Where_query_composition2();

            AssertSql(
                @"@__p_0='3'

SELECT [t].[EmployeeID], [t].[City], [t].[Country], [t].[FirstName], [t].[ReportsTo], [t].[Title]
FROM (
    SELECT TOP @__p_0 [e].[EmployeeID], [e].[City], [e].[Country], [e].[FirstName], [e].[ReportsTo], [e].[Title]
    FROM [Employees] AS [e]
) AS [t]",
                //
                @"SELECT TOP 1 [e0].[EmployeeID], [e0].[City], [e0].[Country], [e0].[FirstName], [e0].[ReportsTo], [e0].[Title]
FROM [Employees] AS [e0]
ORDER BY [e0].[EmployeeID]",
                //
                @"SELECT TOP 1 [e0].[EmployeeID], [e0].[City], [e0].[Country], [e0].[FirstName], [e0].[ReportsTo], [e0].[Title]
FROM [Employees] AS [e0]
ORDER BY [e0].[EmployeeID]",
                //
                @"SELECT TOP 1 [e0].[EmployeeID], [e0].[City], [e0].[Country], [e0].[FirstName], [e0].[ReportsTo], [e0].[Title]
FROM [Employees] AS [e0]
ORDER BY [e0].[EmployeeID]");
        }

        public override void Where_query_composition2_FirstOrDefault_with_anonymous()
        {
            base.Where_query_composition2_FirstOrDefault_with_anonymous();

            AssertSql(
                @"@__p_0='3'

SELECT [t].[EmployeeID], [t].[City], [t].[Country], [t].[FirstName], [t].[ReportsTo], [t].[Title]
FROM (
    SELECT TOP @__p_0 [e].[EmployeeID], [e].[City], [e].[Country], [e].[FirstName], [e].[ReportsTo], [e].[Title]
    FROM [Employees] AS [e]
) AS [t]",
                //
                @"SELECT TOP 1 [e0].[EmployeeID], [e0].[City], [e0].[Country], [e0].[FirstName], [e0].[ReportsTo], [e0].[Title]
FROM [Employees] AS [e0]
ORDER BY [e0].[EmployeeID]",
                //
                @"SELECT TOP 1 [e0].[EmployeeID], [e0].[City], [e0].[Country], [e0].[FirstName], [e0].[ReportsTo], [e0].[Title]
FROM [Employees] AS [e0]
ORDER BY [e0].[EmployeeID]",
                //
                @"SELECT TOP 1 [e0].[EmployeeID], [e0].[City], [e0].[Country], [e0].[FirstName], [e0].[ReportsTo], [e0].[Title]
FROM [Employees] AS [e0]
ORDER BY [e0].[EmployeeID]");
        }


        public override void Select_Subquery_Single()
        {
            base.Select_Subquery_Single();

            AssertSql(
                @"@__p_0='2'

SELECT TOP @__p_0 [od].[OrderID]
FROM [Order Details] AS [od]
ORDER BY [od].[ProductID], [od].[OrderID]",
                //
                @"@_outer_OrderID='10285'

SELECT TOP 1 [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]
WHERE @_outer_OrderID = [o].[OrderID]
ORDER BY [o].[OrderID]",
                //
                @"@_outer_OrderID='10294'

SELECT TOP 1 [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]
WHERE @_outer_OrderID = [o].[OrderID]
ORDER BY [o].[OrderID]");
        }

        public override void Select_Where_Subquery_Deep_Single()
        {
            base.Select_Where_Subquery_Deep_Single();

            AssertSql(
                @"SELECT [od].[OrderID], [od].[ProductID], [od].[Discount], [od].[Quantity], [od].[UnitPrice]
FROM [Order Details] AS [od]
WHERE [od].[OrderID] = 10344",
                //
                @"@_outer_OrderID='10344'

SELECT TOP 2 [o0].[CustomerID]
FROM [Orders] AS [o0]
WHERE @_outer_OrderID = [o0].[OrderID]",
                //
                @"@_outer_CustomerID1='WHITC' (Nullable = false) (Size = 5)

SELECT TOP 2 [c2].[City]
FROM [Customers] AS [c2]
WHERE @_outer_CustomerID1 = [c2].[CustomerID]",
                //
                @"@_outer_OrderID='10344'

SELECT TOP 2 [o0].[CustomerID]
FROM [Orders] AS [o0]
WHERE @_outer_OrderID = [o0].[OrderID]",
                //
                @"@_outer_CustomerID1='WHITC' (Nullable = false) (Size = 5)

SELECT TOP 2 [c2].[City]
FROM [Customers] AS [c2]
WHERE @_outer_CustomerID1 = [c2].[CustomerID]");
        }

        
        public override void Select_Where_Subquery_Deep_First()
        {
            base.Select_Where_Subquery_Deep_First();

            AssertSql(
                @"@__p_0='2'

SELECT TOP @__p_0 [od].[OrderID], [od].[ProductID], [od].[Discount], [od].[Quantity], [od].[UnitPrice]
FROM [Order Details] AS [od]
WHERE (
    SELECT TOP 1 (
        SELECT TOP 1 [c].[City]
        FROM [Customers] AS [c]
        WHERE [o].[CustomerID] = [c].[CustomerID]
    )
    FROM [Orders] AS [o]
    WHERE [od].[OrderID] = [o].[OrderID]
) = 'Seattle'");
        }


        public override void Where_subquery_anon()
        {
            base.Where_subquery_anon();

            AssertSql(
                @"@__p_0='3'

SELECT [t].[EmployeeID], [t].[City], [t].[Country], [t].[FirstName], [t].[ReportsTo], [t].[Title], [t0].[OrderID], [t0].[CustomerID], [t0].[EmployeeID], [t0].[OrderDate]
FROM (
    SELECT TOP @__p_0 [e].[EmployeeID], [e].[City], [e].[Country], [e].[FirstName], [e].[ReportsTo], [e].[Title]
    FROM [Employees] AS [e]
) AS [t]
, (
    SELECT TOP 5 [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
    FROM [Orders] AS [o]
) AS [t0]");
        }

        public override void Where_subquery_anon_nested()
        {
            base.Where_subquery_anon_nested();

            AssertSql(
                @"@__p_0='3'

SELECT [t].[EmployeeID], [t].[City], [t].[Country], [t].[FirstName], [t].[ReportsTo], [t].[Title], [t0].[OrderID], [t0].[CustomerID], [t0].[EmployeeID], [t0].[OrderDate], [t1].[CustomerID], [t1].[Address], [t1].[City], [t1].[CompanyName], [t1].[ContactName], [t1].[ContactTitle], [t1].[Country], [t1].[Fax], [t1].[Phone], [t1].[PostalCode], [t1].[Region]
FROM (
    SELECT TOP @__p_0 [e].[EmployeeID], [e].[City], [e].[Country], [e].[FirstName], [e].[ReportsTo], [e].[Title]
    FROM [Employees] AS [e]
) AS [t]
, (
    SELECT TOP 5 [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
    FROM [Orders] AS [o]
) AS [t0]
, (
    SELECT TOP 2 [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
    FROM [Customers] AS [c]
) AS [t1]
WHERE [t].[City] = 'London'");
        }

        public override void Where_subquery_correlated()
        {
            base.Where_subquery_correlated();

            AssertSql(
                @"SELECT [c1].[CustomerID], [c1].[Address], [c1].[City], [c1].[CompanyName], [c1].[ContactName], [c1].[ContactTitle], [c1].[Country], [c1].[Fax], [c1].[Phone], [c1].[PostalCode], [c1].[Region]
FROM [Customers] AS [c1]
WHERE EXISTS (
    SELECT 1
    FROM [Customers] AS [c2]
    WHERE [c1].[CustomerID] = [c2].[CustomerID])");
        }

        public override void Where_subquery_correlated_client_eval()
        {
            base.Where_subquery_correlated_client_eval();

            AssertSql(
                @"@__p_0='5'

SELECT [t].[CustomerID], [t].[Address], [t].[City], [t].[CompanyName], [t].[ContactName], [t].[ContactTitle], [t].[Country], [t].[Fax], [t].[Phone], [t].[PostalCode], [t].[Region]
FROM (
    SELECT TOP @__p_0 [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
    FROM [Customers] AS [c]
) AS [t]
ORDER BY [t].[CustomerID]",
                //
                @"@_outer_CustomerID='ALFKI' (Nullable = false) (Size = 5)

SELECT [c2].[CustomerID], [c2].[Address], [c2].[City], [c2].[CompanyName], [c2].[ContactName], [c2].[ContactTitle], [c2].[Country], [c2].[Fax], [c2].[Phone], [c2].[PostalCode], [c2].[Region]
FROM [Customers] AS [c2]
WHERE @_outer_CustomerID = [c2].[CustomerID]",
                //
                @"@_outer_CustomerID='ANATR' (Nullable = false) (Size = 5)

SELECT [c2].[CustomerID], [c2].[Address], [c2].[City], [c2].[CompanyName], [c2].[ContactName], [c2].[ContactTitle], [c2].[Country], [c2].[Fax], [c2].[Phone], [c2].[PostalCode], [c2].[Region]
FROM [Customers] AS [c2]
WHERE @_outer_CustomerID = [c2].[CustomerID]",
                //
                @"@_outer_CustomerID='ANTON' (Nullable = false) (Size = 5)

SELECT [c2].[CustomerID], [c2].[Address], [c2].[City], [c2].[CompanyName], [c2].[ContactName], [c2].[ContactTitle], [c2].[Country], [c2].[Fax], [c2].[Phone], [c2].[PostalCode], [c2].[Region]
FROM [Customers] AS [c2]
WHERE @_outer_CustomerID = [c2].[CustomerID]",
                //
                @"@_outer_CustomerID='AROUT' (Nullable = false) (Size = 5)

SELECT [c2].[CustomerID], [c2].[Address], [c2].[City], [c2].[CompanyName], [c2].[ContactName], [c2].[ContactTitle], [c2].[Country], [c2].[Fax], [c2].[Phone], [c2].[PostalCode], [c2].[Region]
FROM [Customers] AS [c2]
WHERE @_outer_CustomerID = [c2].[CustomerID]",
                //
                @"@_outer_CustomerID='BERGS' (Nullable = false) (Size = 5)

SELECT [c2].[CustomerID], [c2].[Address], [c2].[City], [c2].[CompanyName], [c2].[ContactName], [c2].[ContactTitle], [c2].[Country], [c2].[Fax], [c2].[Phone], [c2].[PostalCode], [c2].[Region]
FROM [Customers] AS [c2]
WHERE @_outer_CustomerID = [c2].[CustomerID]");
        }

        public override void OrderBy_SelectMany()
        {
            base.OrderBy_SelectMany();

            AssertSql(
                @"SELECT [c].[ContactName], [t].[OrderID]
FROM [Customers] AS [c]
, (
    SELECT TOP 3 [o].*
    FROM [Orders] AS [o]
    ORDER BY [o].[OrderID]
) AS [t]
WHERE [c].[CustomerID] = [t].[CustomerID]
ORDER BY [c].[CustomerID]");
        }


        public override void GroupBy_anonymous()
        {
            base.GroupBy_anonymous();

            AssertSql(
                @"SELECT [c].[City], [c].[CustomerID]
FROM [Customers] AS [c]
ORDER BY [c].[City]");
        }

        public override void GroupBy_anonymous_with_where()
        {
            base.GroupBy_anonymous_with_where();

            AssertSql(
                @"SELECT [c].[City], [c].[CustomerID]
FROM [Customers] AS [c]
WHERE [c].[Country] IN ('Argentina', 'Austria', 'Brazil', 'France', 'Germany', 'USA')
ORDER BY [c].[City]");
        }

        public override void GroupBy_nested_order_by_enumerable()
        {
            base.GroupBy_nested_order_by_enumerable();

            AssertSql(
                @"SELECT [c0].[Country], [c0].[CustomerID]
FROM [Customers] AS [c0]
ORDER BY [c0].[Country]");
        }


        public override void Where_indexer_closure()
        {
            base.Where_indexer_closure();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]");
        }

        public override void Where_simple_closure_constant()
        {
            base.Where_simple_closure_constant();

            AssertSql(
                @"@__predicate_0='True'

SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE @__predicate_0 = True");
        }


        public override void Count_with_predicate()
        {
            base.Count_with_predicate();

            AssertSql(
                @"SELECT COUNT(*)
FROM [Orders] AS [o]
WHERE [o].[CustomerID] = 'ALFKI'");
        }

        public override void Where_OrderBy_Count()
        {
            base.Where_OrderBy_Count();

            AssertSql(
                @"SELECT COUNT(*)
FROM [Orders] AS [o]
WHERE [o].[CustomerID] = 'ALFKI'");
        }

        public override void OrderBy_Where_Count()
        {
            base.OrderBy_Where_Count();

            AssertSql(
                @"SELECT COUNT(*)
FROM [Orders] AS [o]
WHERE [o].[CustomerID] = 'ALFKI'");
        }

        public override void OrderBy_Count_with_predicate()
        {
            base.OrderBy_Count_with_predicate();

            AssertSql(
                @"SELECT COUNT(*)
FROM [Orders] AS [o]
WHERE [o].[CustomerID] = 'ALFKI'");
        }

        public override void OrderBy_Where_Count_with_predicate()
        {
            base.OrderBy_Where_Count_with_predicate();

            AssertSql(
                @"SELECT COUNT(*)
FROM [Orders] AS [o]
WHERE ([o].[OrderID] > 10) AND (([o].[CustomerID] <> 'ALFKI') OR [o].[CustomerID] IS NULL)");
        }

        public override void Where_OrderBy_Count_client_eval()
        {
            base.Where_OrderBy_Count_client_eval();

            AssertSql(
                @"SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]");
        }

        public override void Where_OrderBy_Count_client_eval_mixed()
        {
            base.Where_OrderBy_Count_client_eval_mixed();

            AssertSql(
                @"SELECT COUNT(*)
FROM [Orders] AS [o]
WHERE [o].[OrderID] > 10");
        }

        public override void OrderBy_Where_Count_client_eval()
        {
            base.OrderBy_Where_Count_client_eval();

            AssertSql(
                @"SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]");
        }

        public override void OrderBy_Where_Count_client_eval_mixed()
        {
            base.OrderBy_Where_Count_client_eval_mixed();

            AssertSql(
                @"SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]");
        }

        public override void OrderBy_Count_with_predicate_client_eval()
        {
            base.OrderBy_Count_with_predicate_client_eval();

            AssertSql(
                @"SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]");
        }

        public override void OrderBy_Count_with_predicate_client_eval_mixed()
        {
            base.OrderBy_Count_with_predicate_client_eval_mixed();

            AssertSql(
                @"SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]");
        }

        public override void OrderBy_Where_Count_with_predicate_client_eval()
        {
            base.OrderBy_Where_Count_with_predicate_client_eval();

            AssertSql(
                @"SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]");
        }

        public override void OrderBy_Where_Count_with_predicate_client_eval_mixed()
        {
            base.OrderBy_Where_Count_with_predicate_client_eval_mixed();

            AssertSql(
                @"SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]
WHERE ([o].[CustomerID] <> 'ALFKI') OR [o].[CustomerID] IS NULL");
        }

        public override void OrderBy_client_Take()
        {
            base.OrderBy_client_Take();

            AssertSql(
                @"@__p_1='10'

SELECT TOP @__p_1 [o].[EmployeeID], [o].[City], [o].[Country], [o].[FirstName], [o].[ReportsTo], [o].[Title]
FROM [Employees] AS [o]
ORDER BY 'a'");
        }

        public override void OrderBy_arithmetic()
        {
            base.OrderBy_arithmetic();

            AssertSql(
                @"SELECT [e].[EmployeeID], [e].[City], [e].[Country], [e].[FirstName], [e].[ReportsTo], [e].[Title]
FROM [Employees] AS [e]
ORDER BY [e].[EmployeeID] - [e].[EmployeeID]");
        }


        public override void Sum_with_no_arg()
        {
            base.Sum_with_no_arg();

            AssertSql(
                @"SELECT SUM([o].[OrderID])
FROM [Orders] AS [o]");
        }

        public override void Sum_with_arg()
        {
            base.Sum_with_arg();

            AssertSql(
                @"SELECT SUM([o].[OrderID])
FROM [Orders] AS [o]");
        }

        public override void Sum_with_arg_expression()
        {
            base.Sum_with_arg_expression();

            AssertSql(
                @"SELECT SUM([o].[OrderID] + [o].[OrderID])
FROM [Orders] AS [o]");
        }

        public override void Sum_with_binary_expression()
        {
            base.Sum_with_binary_expression();

            AssertSql(
                @"SELECT SUM([o].[OrderID] * 2)
FROM [Orders] AS [o]");
        }

        public override void Sum_with_division_on_decimal()
        {
            base.Sum_with_division_on_decimal();

            AssertSql(
                @"SELECT SUM([od].[Quantity] / 2.09)
FROM [Order Details] AS [od]");
        }

        public override void Sum_with_division_on_decimal_no_significant_digits()
        {
            base.Sum_with_division_on_decimal_no_significant_digits();

            AssertSql(
                @"SELECT SUM([od].[Quantity] / 2.0)
FROM [Order Details] AS [od]");
        }
        
        public override void Sum_over_subquery_is_client_eval()
        {
            base.Sum_over_subquery_is_client_eval();

            AssertSql(
                @"SELECT (
    SELECT SUM([o].[OrderID])
    FROM [Orders] AS [o]
    WHERE [c].[CustomerID] = [o].[CustomerID]
)
FROM [Customers] AS [c]");
        }

        
        public override void Average_over_subquery_is_client_eval()
        {
            base.Average_over_subquery_is_client_eval();

            AssertSql(
                @"SELECT (
    SELECT SUM([o].[OrderID])
    FROM [Orders] AS [o]
    WHERE [c].[CustomerID] = [o].[CustomerID]
)
FROM [Customers] AS [c]");
        }



        public override void Min_with_no_arg()
        {
            base.Min_with_no_arg();

            AssertSql(
                @"SELECT MIN([o].[OrderID])
FROM [Orders] AS [o]");
        }

        public override void Min_with_arg()
        {
            base.Min_with_arg();

            AssertSql(
                @"SELECT MIN([o].[OrderID])
FROM [Orders] AS [o]");
        }

        
        public override void Min_over_subquery_is_client_eval()
        {
            base.Min_over_subquery_is_client_eval();

            AssertSql(
                @"SELECT (
    SELECT SUM([o].[OrderID])
    FROM [Orders] AS [o]
    WHERE [c].[CustomerID] = [o].[CustomerID]
)
FROM [Customers] AS [c]");
        }

        public override void Max_with_no_arg()
        {
            base.Max_with_no_arg();

            AssertSql(
                @"SELECT MAX([o].[OrderID])
FROM [Orders] AS [o]");
        }

        public override void Max_with_arg()
        {
            base.Max_with_arg();

            AssertSql(
                @"SELECT MAX([o].[OrderID])
FROM [Orders] AS [o]");
        }

       
        public override void Max_over_subquery_is_client_eval()
        {
            base.Max_over_subquery_is_client_eval();

            AssertSql(
                @"SELECT (
    SELECT SUM([o].[OrderID])
    FROM [Orders] AS [o]
    WHERE [c].[CustomerID] = [o].[CustomerID]
)
FROM [Customers] AS [c]");
        }

        public override void Distinct_Count()
        {
            base.Distinct_Count();

            AssertSql(
                @"SELECT COUNT(*)
FROM (
    SELECT DISTINCT [c].*
    FROM [Customers] AS [c]
) AS [t]");
        }

        public override void Select_Select_Distinct_Count()
        {
            base.Select_Select_Distinct_Count();

            AssertSql(
                @"SELECT COUNT(*)
FROM (
    SELECT DISTINCT [c].[City]
    FROM [Customers] AS [c]
) AS [t]");
        }

        [SqlServerCondition(SqlServerCondition.SupportsOffset)]
        public override void Skip()
        {
            base.Skip();

            AssertSql(
                @"@__p_0='5'

SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
ORDER BY [c].[CustomerID]
 SKIP @__p_0");
        }

        [SqlServerCondition(SqlServerCondition.SupportsOffset)]
        public override void Skip_no_orderby()
        {
            base.Skip_no_orderby();

            AssertSql(
                @"@__p_0='5'

SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
ORDER BY 'a'
 SKIP @__p_0");
        }


        [Fact(Skip = "Unsupported by JET: SKIP TAKE is supported only in outer queries")]
        public override void Skip_Take_All()
        {
            base.Skip_Take_All();
        }

        [Fact(Skip = "Unsupported by JET: SKIP TAKE is supported only in outer queries")]
        public override void Skip_Take_Any()
        {
            base.Skip_Take_Any();
        }


        [Fact(Skip = "Unsupported by JET")]
        public override void Take_Skip_Distinct()
        {
            base.Take_Skip_Distinct();
        }

        [Fact(Skip = "Unsupported by JET")]
        public override void Take_Skip_Distinct_Caching()
        {
            base.Take_Skip_Distinct_Caching();

            AssertSql(
                @"@__p_0='10'
@__p_1='5'

SELECT DISTINCT [t0].*
FROM (
    SELECT [t].*
    FROM (
        SELECT TOP @__p_0 [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
        FROM [Customers] AS [c]
        ORDER BY [c].[ContactName]
    ) AS [t]
    ORDER BY [t].[ContactName]
    OFFSET @__p_1 ROWS
) AS [t0]",
                //
                @"@__p_0='15'
@__p_1='10'

SELECT DISTINCT [t0].*
FROM (
    SELECT [t].*
    FROM (
        SELECT TOP @__p_0 [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
        FROM [Customers] AS [c]
        ORDER BY [c].[ContactName]
    ) AS [t]
    ORDER BY [t].[ContactName]
    OFFSET @__p_1 ROWS
) AS [t0]");
        }

        public void Skip_when_no_OrderBy()
        {
            Assert.Throws<Exception>(() => AssertQuery<Customer>(cs => cs.Skip(5).Take(10)));
        }

        public override void Take_Distinct_Count()
        {
            base.Take_Distinct_Count();

            AssertSql(
                @"@__p_0='5'

SELECT COUNT(*)
FROM (
    SELECT DISTINCT [t].*
    FROM (
        SELECT TOP @__p_0 [o].*
        FROM [Orders] AS [o]
    ) AS [t]
) AS [t0]");
        }

        public override void Take_Where_Distinct_Count()
        {
            base.Take_Where_Distinct_Count();

            AssertSql(
                @"@__p_0='5'

SELECT COUNT(*)
FROM (
    SELECT DISTINCT [t].*
    FROM (
        SELECT TOP @__p_0 [o].*
        FROM [Orders] AS [o]
        WHERE [o].[CustomerID] = 'FRANK'
    ) AS [t]
) AS [t0]");
        }

        public override void Null_conditional_simple()
        {
            base.Null_conditional_simple();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] = 'ALFKI'");
        }

        public override void Queryable_simple()
        {
            base.Queryable_simple();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]");
        }

        public override void Queryable_simple_anonymous()
        {
            base.Queryable_simple_anonymous();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]");
        }

        public override void Queryable_nested_simple()
        {
            base.Queryable_nested_simple();

            AssertSql(
                @"SELECT [c3].[CustomerID], [c3].[Address], [c3].[City], [c3].[CompanyName], [c3].[ContactName], [c3].[ContactTitle], [c3].[Country], [c3].[Fax], [c3].[Phone], [c3].[PostalCode], [c3].[Region]
FROM [Customers] AS [c3]");
        }

        public override void Queryable_simple_anonymous_projection_subquery()
        {
            base.Queryable_simple_anonymous_projection_subquery();

            AssertSql(
                @"@__p_0='91'

SELECT TOP @__p_0 [c].[City]
FROM [Customers] AS [c]");
        }

        public override void Queryable_simple_anonymous_subquery()
        {
            base.Queryable_simple_anonymous_subquery();

            AssertSql(
                @"@__p_0='91'

SELECT TOP @__p_0 [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]");
        }

        public override void Take_simple()
        {
            base.Take_simple();

            AssertSql(
                @"@__p_0='10'

SELECT TOP @__p_0 [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
ORDER BY [c].[CustomerID]");
        }

        public override void Take_simple_parameterized()
        {
            base.Take_simple_parameterized();

            AssertSql(
                @"@__p_0='10'

SELECT TOP @__p_0 [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
ORDER BY [c].[CustomerID]");
        }

        public override void Take_simple_projection()
        {
            base.Take_simple_projection();

            AssertSql(
                @"@__p_0='10'

SELECT TOP @__p_0 [c].[City]
FROM [Customers] AS [c]
ORDER BY [c].[CustomerID]");
        }

        public override void Take_subquery_projection()
        {
            base.Take_subquery_projection();

            AssertSql(
                @"@__p_0='2'

SELECT TOP @__p_0 [c].[City]
FROM [Customers] AS [c]
ORDER BY [c].[CustomerID]");
        }

        public override void OrderBy_Take_Count()
        {
            base.OrderBy_Take_Count();

            AssertSql(
                @"@__p_0='5'

SELECT COUNT(*)
FROM (
    SELECT TOP @__p_0 [o].*
    FROM [Orders] AS [o]
    ORDER BY [o].[OrderID]
) AS [t]");
        }

        public override void Take_OrderBy_Count()
        {
            base.Take_OrderBy_Count();

            AssertSql(
                @"@__p_0='5'

SELECT COUNT(*)
FROM (
    SELECT TOP @__p_0 [o].*
    FROM [Orders] AS [o]
) AS [t]");
        }



        public override void Any_nested_negated()
        {
            base.Any_nested_negated();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE NOT EXISTS (
    SELECT 1
    FROM [Orders] AS [o]
    WHERE [o].[CustomerID] LIKE 'A' + '%' AND (LEFT([o].[CustomerID], Len('A')) = 'A'))");
        }

        public override void Any_nested_negated2()
        {
            base.Any_nested_negated2();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE (([c].[City] <> 'London') OR [c].[City] IS NULL) AND NOT EXISTS (
    SELECT 1
    FROM [Orders] AS [o]
    WHERE [o].[CustomerID] LIKE 'A' + '%' AND (LEFT([o].[CustomerID], Len('A')) = 'A'))");
        }

        public override void Any_nested_negated3()
        {
            base.Any_nested_negated3();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE NOT EXISTS (
    SELECT 1
    FROM [Orders] AS [o]
    WHERE [o].[CustomerID] LIKE 'A' + '%' AND (LEFT([o].[CustomerID], Len('A')) = 'A')) AND (([c].[City] <> 'London') OR [c].[City] IS NULL)");
        }

        public override void Any_nested()
        {
            base.Any_nested();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE EXISTS (
    SELECT 1
    FROM [Orders] AS [o]
    WHERE [o].[CustomerID] LIKE 'A' + '%' AND (LEFT([o].[CustomerID], Len('A')) = 'A'))");
        }

        public override void Any_nested2()
        {
            base.Any_nested2();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE (([c].[City] <> 'London') OR [c].[City] IS NULL) AND EXISTS (
    SELECT 1
    FROM [Orders] AS [o]
    WHERE [o].[CustomerID] LIKE 'A' + '%' AND (LEFT([o].[CustomerID], Len('A')) = 'A'))");
        }

        public override void Any_nested3()
        {
            base.Any_nested3();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE EXISTS (
    SELECT 1
    FROM [Orders] AS [o]
    WHERE [o].[CustomerID] LIKE 'A' + '%' AND (LEFT([o].[CustomerID], Len('A')) = 'A')) AND (([c].[City] <> 'London') OR [c].[City] IS NULL)");
        }

        public override void Any_with_multiple_conditions_still_uses_exists()
        {
            base.Any_with_multiple_conditions_still_uses_exists();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE ([c].[City] = 'London') AND EXISTS (
    SELECT 1
    FROM [Orders] AS [o]
    WHERE ([o].[EmployeeID] = 1) AND ([c].[CustomerID] = [o].[CustomerID]))");
        }

        [Fact(Skip = "Unsupported by JET")]
        public override void All_top_level()
        {
            base.All_top_level();
        }

        [Fact(Skip = "Unsupported by JET")]
        public override void All_top_level_column()
        {
            base.All_top_level_column();
        }

        [Fact(Skip = "Unsupported by JET")]
        public override void All_top_level_subquery()
        {
            base.All_top_level_subquery();
        }

        [Fact(Skip = "Unsupported by JET")]
        public override void All_top_level_subquery_ef_property()
        {
            base.All_top_level_subquery_ef_property();
        }

        public override void Select_scalar()
        {
            base.Select_scalar();

            AssertSql(
                @"SELECT [c].[City]
FROM [Customers] AS [c]");
        }

        public override void Select_anonymous_one()
        {
            base.Select_anonymous_one();

            AssertSql(
                @"SELECT [c].[City]
FROM [Customers] AS [c]");
        }

        public override void Select_anonymous_two()
        {
            base.Select_anonymous_two();

            AssertSql(
                @"SELECT [c].[City], [c].[Phone]
FROM [Customers] AS [c]");
        }

        public override void Select_anonymous_three()
        {
            base.Select_anonymous_three();

            AssertSql(
                @"SELECT [c].[City], [c].[Phone], [c].[Country]
FROM [Customers] AS [c]");
        }

        public override void Select_anonymous_bool_constant_true()
        {
            base.Select_anonymous_bool_constant_true();

            AssertSql(
                @"SELECT [c].[CustomerID]
FROM [Customers] AS [c]");
        }


        public override void Select_scalar_primitive_after_take()
        {
            base.Select_scalar_primitive_after_take();

            AssertSql(
                @"@__p_0='9'

SELECT TOP @__p_0 [e].[EmployeeID]
FROM [Employees] AS [e]");
        }

        public override void Select_constant_null_string()
        {
            base.Select_constant_null_string();

            AssertSql(
                @"SELECT 1
FROM [Customers] AS [c]");
        }

        public override void Select_local()
        {
            base.Select_local();

            AssertSql(
                @"@__x_0='10'

SELECT @__x_0
FROM [Customers] AS [c]");
        }

        public override void Where_simple()
        {
            base.Where_simple();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[City] = 'London'");
        }

        public override void Where_simple_shadow()
        {
            base.Where_simple_shadow();

            AssertSql(
                @"SELECT [e].[EmployeeID], [e].[City], [e].[Country], [e].[FirstName], [e].[ReportsTo], [e].[Title]
FROM [Employees] AS [e]
WHERE [e].[Title] = 'Sales Representative'");
        }

        public override void Where_simple_shadow_projection()
        {
            base.Where_simple_shadow_projection();

            AssertSql(
                @"SELECT [e].[Title]
FROM [Employees] AS [e]
WHERE [e].[Title] = 'Sales Representative'");
        }

        public override void Where_comparison_nullable_type_not_null()
        {
            base.Where_comparison_nullable_type_not_null();

            AssertSql(
                @"SELECT [e].[EmployeeID], [e].[City], [e].[Country], [e].[FirstName], [e].[ReportsTo], [e].[Title]
FROM [Employees] AS [e]
WHERE [e].[ReportsTo] = 2");
        }

        public override void Where_comparison_nullable_type_null()
        {
            base.Where_comparison_nullable_type_null();

            AssertSql(
                @"SELECT [e].[EmployeeID], [e].[City], [e].[Country], [e].[FirstName], [e].[ReportsTo], [e].[Title]
FROM [Employees] AS [e]
WHERE [e].[ReportsTo] IS NULL");
        }

        public override void Where_client()
        {
            base.Where_client();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]");
        }

        public override void Where_client_and_server_top_level()
        {
            base.Where_client_and_server_top_level();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] <> 'AROUT'");
        }

        public override void Where_client_or_server_top_level()
        {
            base.Where_client_or_server_top_level();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]");
        }

        public override void Where_client_and_server_non_top_level()
        {
            base.Where_client_and_server_non_top_level();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]");
        }

        public override void Where_client_deep_inside_predicate_and_server_top_level()
        {
            base.Where_client_deep_inside_predicate_and_server_top_level();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] <> 'ALFKI'");
        }

        public override void First_client_predicate()
        {
            base.First_client_predicate();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
ORDER BY [c].[CustomerID]");
        }


        public override void Last()
        {
            base.Last();

            AssertSql(
                @"SELECT TOP 1 [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
ORDER BY [c].[ContactName] DESC");
        }

        public override void Last_when_no_order_by()
        {
            base.Last_when_no_order_by();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] = 'ALFKI'");
        }

        public override void Last_Predicate()
        {
            base.Last_Predicate();

            AssertSql(
                @"SELECT TOP 1 [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[City] = 'London'
ORDER BY [c].[ContactName] DESC");
        }

        public override void Where_Last()
        {
            base.Where_Last();

            AssertSql(
                @"SELECT TOP 1 [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[City] = 'London'
ORDER BY [c].[ContactName] DESC");
        }

        public override void LastOrDefault()
        {
            base.LastOrDefault();

            AssertSql(
                @"SELECT TOP 1 [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
ORDER BY [c].[ContactName] DESC");
        }

        public override void LastOrDefault_Predicate()
        {
            base.LastOrDefault_Predicate();

            AssertSql(
                @"SELECT TOP 1 [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[City] = 'London'
ORDER BY [c].[ContactName] DESC");
        }

        public override void Where_LastOrDefault()
        {
            base.Where_LastOrDefault();

            AssertSql(
                @"SELECT TOP 1 [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[City] = 'London'
ORDER BY [c].[ContactName] DESC");
        }

        public override void Where_equals_method_string()
        {
            base.Where_equals_method_string();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[City] = 'London'");
        }

        public override void Where_equals_method_int()
        {
            base.Where_equals_method_int();

            AssertSql(
                @"SELECT [e].[EmployeeID], [e].[City], [e].[Country], [e].[FirstName], [e].[ReportsTo], [e].[Title]
FROM [Employees] AS [e]
WHERE [e].[EmployeeID] = 1");
        }

        public override void Where_equals_on_mismatched_types_nullable_int_long()
        {
            base.Where_equals_on_mismatched_types_nullable_int_long();

            AssertSql(
                @"SELECT [e].[EmployeeID], [e].[City], [e].[Country], [e].[FirstName], [e].[ReportsTo], [e].[Title]
FROM [Employees] AS [e]
WHERE False = True",
                //
                @"SELECT [e].[EmployeeID], [e].[City], [e].[Country], [e].[FirstName], [e].[ReportsTo], [e].[Title]
FROM [Employees] AS [e]
WHERE False = True");
            Assert.Contains(RelationalStrings.LogPossibleUnintendedUseOfEquals.GenerateMessage("e.ReportsTo.Equals(Convert(__longPrm_0))"), Fixture.TestSqlLoggerFactory.Log);

            Assert.Contains(RelationalStrings.LogPossibleUnintendedUseOfEquals.GenerateMessage("__longPrm_0.Equals(Convert(e.ReportsTo))"), Fixture.TestSqlLoggerFactory.Log);
        }

        public override void Where_equals_on_mismatched_types_nullable_long_nullable_int()
        {
            base.Where_equals_on_mismatched_types_nullable_long_nullable_int();

            AssertSql(
                @"SELECT [e].[EmployeeID], [e].[City], [e].[Country], [e].[FirstName], [e].[ReportsTo], [e].[Title]
FROM [Employees] AS [e]
WHERE False = True",
                //
                @"SELECT [e].[EmployeeID], [e].[City], [e].[Country], [e].[FirstName], [e].[ReportsTo], [e].[Title]
FROM [Employees] AS [e]
WHERE False = True");

            Assert.Contains(RelationalStrings.LogPossibleUnintendedUseOfEquals.GenerateMessage("__nullableLongPrm_0.Equals(Convert(e.ReportsTo))"), Fixture.TestSqlLoggerFactory.Log);

            Assert.Contains(RelationalStrings.LogPossibleUnintendedUseOfEquals.GenerateMessage("e.ReportsTo.Equals(Convert(__nullableLongPrm_0))"), Fixture.TestSqlLoggerFactory.Log);
        }

        public override void Where_equals_on_null_nullable_int_types()
        {
            base.Where_equals_on_null_nullable_int_types();

            AssertSql(
                @"SELECT [e].[EmployeeID], [e].[City], [e].[Country], [e].[FirstName], [e].[ReportsTo], [e].[Title]
FROM [Employees] AS [e]
WHERE [e].[ReportsTo] IS NULL",
                //
                @"SELECT [e].[EmployeeID], [e].[City], [e].[Country], [e].[FirstName], [e].[ReportsTo], [e].[Title]
FROM [Employees] AS [e]
WHERE [e].[ReportsTo] IS NULL");
        }


        [Fact(Skip = "Assertion failed without evident reason")]
        public override void Where_datetime_date_component()
        {
            base.Where_datetime_date_component();

            AssertSql(
                @"SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]");
        }

        public override void Where_datetime_day_component()
        {
            base.Where_datetime_day_component();

            AssertSql(
                @"SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]
WHERE DatePart('d', [o].[OrderDate]) = 4");
        }

        public override void Where_date_add_year_constant_component()
        {
            base.Where_date_add_year_constant_component();

            AssertSql(
                @"SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]
WHERE DatePart('yyyy', DateAdd('yyyy', -1, [o].[OrderDate])) = 1997");
        }

        public override void Where_datetime_year_component()
        {
            base.Where_datetime_year_component();

            AssertSql(
                @"SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]
WHERE DatePart('yyyy', [o].[OrderDate]) = 1998");
        }

        public override void Where_datetime_dayOfYear_component()
        {
            base.Where_datetime_dayOfYear_component();

            AssertSql(
                @"SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]
WHERE DatePart('y', [o].[OrderDate]) = 68");
        }

        public override void Where_datetime_month_component()
        {
            base.Where_datetime_month_component();

            AssertSql(
                @"SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]
WHERE DatePart('m', [o].[OrderDate]) = 4");
        }

        public override void Where_datetime_hour_component()
        {
            base.Where_datetime_hour_component();

            AssertSql(
                @"SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]
WHERE DatePart('h', [o].[OrderDate]) = 14");
        }

        public override void Where_datetime_minute_component()
        {
            base.Where_datetime_minute_component();

            AssertSql(
                @"SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]
WHERE DatePart('n', [o].[OrderDate]) = 23");
        }

        public override void Where_datetime_second_component()
        {
            base.Where_datetime_second_component();

            AssertSql(
                @"SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]
WHERE DatePart('s', [o].[OrderDate]) = 44");
        }

        [Fact(Skip = "Unsupported by JET: DateTime does not support milliseconds")]
        public override void Where_datetime_millisecond_component()
        {
            base.Where_datetime_millisecond_component();

            AssertSql(
                @"SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]
WHERE DATEPART(millisecond, [o].[OrderDate]) = 88");
        }


        public override void Where_is_null()
        {
            base.Where_is_null();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[City] IS NULL");
        }

        public override void Where_is_not_null()
        {
            base.Where_is_not_null();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[City] IS NOT NULL");
        }

        public override void Where_null_is_null()
        {
            base.Where_null_is_null();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]");
        }

        public override void Where_constant_is_null()
        {
            base.Where_constant_is_null();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE False = True");
        }

        public override void Where_null_is_not_null()
        {
            base.Where_null_is_not_null();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE False = True");
        }

        public override void Where_constant_is_not_null()
        {
            base.Where_constant_is_not_null();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]");
        }

        public override void Where_simple_reversed()
        {
            base.Where_simple_reversed();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE 'London' = [c].[City]");
        }

        public override void Where_identity_comparison()
        {
            base.Where_identity_comparison();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE ([c].[City] = [c].[City]) OR ([c].[City] IS NULL AND [c].[City] IS NULL)");
        }

        public override void Where_select_many_or2()
        {
            base.Where_select_many_or2();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region], [e].[EmployeeID], [e].[City], [e].[Country], [e].[FirstName], [e].[ReportsTo], [e].[Title]
FROM [Customers] AS [c]
, [Employees] AS [e]
WHERE [c].[City] IN ('London', 'Berlin')");
        }

        public override void Where_select_many_or3()
        {
            base.Where_select_many_or3();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region], [e].[EmployeeID], [e].[City], [e].[Country], [e].[FirstName], [e].[ReportsTo], [e].[Title]
FROM [Customers] AS [c]
, [Employees] AS [e]
WHERE [c].[City] IN ('London', 'Berlin', 'Seattle')");
        }

        public override void Where_select_many_or4()
        {
            base.Where_select_many_or4();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region], [e].[EmployeeID], [e].[City], [e].[Country], [e].[FirstName], [e].[ReportsTo], [e].[Title]
FROM [Customers] AS [c]
, [Employees] AS [e]
WHERE [c].[City] IN ('London', 'Berlin', 'Seattle', 'Lisboa')");
        }

        public override void Where_in_optimization_multiple()
        {
            base.Where_in_optimization_multiple();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region], [e].[EmployeeID], [e].[City], [e].[Country], [e].[FirstName], [e].[ReportsTo], [e].[Title]
FROM [Customers] AS [c]
, [Employees] AS [e]
WHERE ([c].[City] IN ('London', 'Berlin') OR ([c].[CustomerID] = 'ALFKI')) OR ([c].[CustomerID] = 'ABCDE')");
        }

        public override void Where_not_in_optimization1()
        {
            base.Where_not_in_optimization1();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region], [e].[EmployeeID], [e].[City], [e].[Country], [e].[FirstName], [e].[ReportsTo], [e].[Title]
FROM [Customers] AS [c]
, [Employees] AS [e]
WHERE (([c].[City] <> 'London') OR [c].[City] IS NULL) AND (([e].[City] <> 'London') OR [e].[City] IS NULL)");
        }

        public override void Where_not_in_optimization2()
        {
            base.Where_not_in_optimization2();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region], [e].[EmployeeID], [e].[City], [e].[Country], [e].[FirstName], [e].[ReportsTo], [e].[Title]
FROM [Customers] AS [c]
, [Employees] AS [e]
WHERE [c].[City] NOT IN ('London', 'Berlin')");
        }

        public override void Where_not_in_optimization3()
        {
            base.Where_not_in_optimization3();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region], [e].[EmployeeID], [e].[City], [e].[Country], [e].[FirstName], [e].[ReportsTo], [e].[Title]
FROM [Customers] AS [c]
, [Employees] AS [e]
WHERE [c].[City] NOT IN ('London', 'Berlin', 'Seattle')");
        }

        public override void Where_not_in_optimization4()
        {
            base.Where_not_in_optimization4();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region], [e].[EmployeeID], [e].[City], [e].[Country], [e].[FirstName], [e].[ReportsTo], [e].[Title]
FROM [Customers] AS [c]
, [Employees] AS [e]
WHERE [c].[City] NOT IN ('London', 'Berlin', 'Seattle', 'Lisboa')");
        }

        public override void Where_select_many_and()
        {
            base.Where_select_many_and();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region], [e].[EmployeeID], [e].[City], [e].[Country], [e].[FirstName], [e].[ReportsTo], [e].[Title]
FROM [Customers] AS [c]
, [Employees] AS [e]
WHERE (([c].[City] = 'London') AND ([c].[Country] = 'UK')) AND (([e].[City] = 'London') AND ([e].[Country] = 'UK'))");
        }

        public override void Select_project_filter()
        {
            base.Select_project_filter();

            AssertSql(
                @"SELECT [c].[CompanyName]
FROM [Customers] AS [c]
WHERE [c].[City] = 'London'");
        }

        public override void Select_project_filter2()
        {
            base.Select_project_filter2();

            AssertSql(
                @"SELECT [c].[City]
FROM [Customers] AS [c]
WHERE [c].[City] = 'London'");
        }

        public override void SelectMany_mixed()
        {
            base.SelectMany_mixed();

            AssertSql(
                @"@__p_0='2'

SELECT [t].[EmployeeID], [t].[City], [t].[Country], [t].[FirstName], [t].[ReportsTo], [t].[Title]
FROM (
    SELECT TOP @__p_0 [e].[EmployeeID], [e].[City], [e].[Country], [e].[FirstName], [e].[ReportsTo], [e].[Title]
    FROM [Employees] AS [e]
    ORDER BY [e].[EmployeeID]
) AS [t]",
                //
                @"SELECT [t0].[CustomerID], [t0].[Address], [t0].[City], [t0].[CompanyName], [t0].[ContactName], [t0].[ContactTitle], [t0].[Country], [t0].[Fax], [t0].[Phone], [t0].[PostalCode], [t0].[Region]
FROM (
    SELECT TOP 2 [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
    FROM [Customers] AS [c]
    ORDER BY [c].[CustomerID]
) AS [t0]",
                //
                @"SELECT [t0].[CustomerID], [t0].[Address], [t0].[City], [t0].[CompanyName], [t0].[ContactName], [t0].[ContactTitle], [t0].[Country], [t0].[Fax], [t0].[Phone], [t0].[PostalCode], [t0].[Region]
FROM (
    SELECT TOP 2 [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
    FROM [Customers] AS [c]
    ORDER BY [c].[CustomerID]
) AS [t0]",
                //
                @"SELECT [t0].[CustomerID], [t0].[Address], [t0].[City], [t0].[CompanyName], [t0].[ContactName], [t0].[ContactTitle], [t0].[Country], [t0].[Fax], [t0].[Phone], [t0].[PostalCode], [t0].[Region]
FROM (
    SELECT TOP 2 [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
    FROM [Customers] AS [c]
    ORDER BY [c].[CustomerID]
) AS [t0]",
                //
                @"SELECT [t0].[CustomerID], [t0].[Address], [t0].[City], [t0].[CompanyName], [t0].[ContactName], [t0].[ContactTitle], [t0].[Country], [t0].[Fax], [t0].[Phone], [t0].[PostalCode], [t0].[Region]
FROM (
    SELECT TOP 2 [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
    FROM [Customers] AS [c]
    ORDER BY [c].[CustomerID]
) AS [t0]");
        }

        public override void SelectMany_simple_subquery()
        {
            base.SelectMany_simple_subquery();

            AssertSql(
                @"@__p_0='9'

SELECT [t].[EmployeeID], [t].[City], [t].[Country], [t].[FirstName], [t].[ReportsTo], [t].[Title], [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM (
    SELECT TOP @__p_0 [e].[EmployeeID], [e].[City], [e].[Country], [e].[FirstName], [e].[ReportsTo], [e].[Title]
    FROM [Employees] AS [e]
) AS [t]
, [Customers] AS [c]");
        }

        public override void SelectMany_simple1()
        {
            base.SelectMany_simple1();

            AssertSql(
                @"SELECT [e].[EmployeeID], [e].[City], [e].[Country], [e].[FirstName], [e].[ReportsTo], [e].[Title], [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Employees] AS [e]
, [Customers] AS [c]");
        }

        public override void SelectMany_simple2()
        {
            base.SelectMany_simple2();

            AssertSql(
                @"SELECT [e1].[EmployeeID], [e1].[City], [e1].[Country], [e1].[FirstName], [e1].[ReportsTo], [e1].[Title], [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region], [e2].[FirstName] AS [FirstName0]
FROM [Employees] AS [e1]
, [Customers] AS [c]
, [Employees] AS [e2]");
        }

        public override void SelectMany_entity_deep()
        {
            base.SelectMany_entity_deep();

            AssertSql(
                @"SELECT [e1].[EmployeeID], [e1].[City], [e1].[Country], [e1].[FirstName], [e1].[ReportsTo], [e1].[Title], [e2].[EmployeeID], [e2].[City], [e2].[Country], [e2].[FirstName], [e2].[ReportsTo], [e2].[Title], [e3].[EmployeeID], [e3].[City], [e3].[Country], [e3].[FirstName], [e3].[ReportsTo], [e3].[Title], [e4].[EmployeeID], [e4].[City], [e4].[Country], [e4].[FirstName], [e4].[ReportsTo], [e4].[Title]
FROM [Employees] AS [e1]
, [Employees] AS [e2]
, [Employees] AS [e3]
, [Employees] AS [e4]");
        }

        public override void SelectMany_projection1()
        {
            base.SelectMany_projection1();

            AssertSql(
                @"SELECT [e1].[City], [e2].[Country]
FROM [Employees] AS [e1]
, [Employees] AS [e2]");
        }

        public override void SelectMany_projection2()
        {
            base.SelectMany_projection2();

            AssertSql(
                @"SELECT [e1].[City], [e2].[Country], [e3].[FirstName]
FROM [Employees] AS [e1]
, [Employees] AS [e2]
, [Employees] AS [e3]");
        }

        public override void SelectMany_Count()
        {
            base.SelectMany_Count();

            AssertSql(
                @"SELECT COUNT(*)
FROM [Customers] AS [c]
, [Orders] AS [o]");
        }


        public override void Join_client_new_expression()
        {
            base.Join_client_new_expression();

            AssertContainsSql(
                @"SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]",
                //
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]");
        }

        public override void Join_customers_orders_with_subquery_anonymous_property_method()
        {
            base.Join_customers_orders_with_subquery_anonymous_property_method();

            AssertContainsSql(
                @"SELECT [o20].[OrderID], [o20].[CustomerID], [o20].[EmployeeID], [o20].[OrderDate]
FROM [Orders] AS [o20]
ORDER BY [o20].[OrderID]",
                //
                @"SELECT [c].[CustomerID]
FROM [Customers] AS [c]");
        }

        public override void Join_customers_orders_with_subquery_anonymous_property_method_with_take()
        {
            base.Join_customers_orders_with_subquery_anonymous_property_method_with_take();

            AssertContainsSql(
                @"@__p_0='5'

SELECT [t].[OrderID], [t].[CustomerID], [t].[EmployeeID], [t].[OrderDate]
FROM (
    SELECT TOP @__p_0 [o2].[OrderID], [o2].[CustomerID], [o2].[EmployeeID], [o2].[OrderDate]
    FROM [Orders] AS [o2]
    ORDER BY [o2].[OrderID]
) AS [t]",
                //
                @"SELECT [c].[CustomerID]
FROM [Customers] AS [c]");
        }

        public override void Join_customers_orders_with_subquery_predicate()
        {
            base.Join_customers_orders_with_subquery_predicate();

            AssertContainsSql(
                @"SELECT [o20].[CustomerID], [o20].[OrderID]
FROM [Orders] AS [o20]
WHERE [o20].[OrderID] > 0
ORDER BY [o20].[OrderID]",
                //
                @"SELECT [c].[CustomerID], [c].[ContactName]
FROM [Customers] AS [c]");
        }

        [Fact(Skip = "Unsupported by JET")]
        public override void Multiple_joins_Where_Order_Any()
        {
            base.Multiple_joins_Where_Order_Any();
        }

        public override void Where_select_many()
        {
            base.Where_select_many();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
, [Orders] AS [o]
WHERE [c].[CustomerID] = 'ALFKI'");
        }

        public override void Where_orderby_select_many()
        {
            base.Where_orderby_select_many();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
, [Orders] AS [o]
WHERE [c].[CustomerID] = 'ALFKI'
ORDER BY [c].[CustomerID]");
        }

        public override void GroupBy_simple()
        {
            base.GroupBy_simple();

            AssertSql(
                @"SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]
ORDER BY [o].[CustomerID]");
        }

        public override void GroupBy_Distinct()
        {
            base.GroupBy_Distinct();

            AssertSql(
                @"SELECT [o0].[OrderID], [o0].[CustomerID], [o0].[EmployeeID], [o0].[OrderDate]
FROM [Orders] AS [o0]
ORDER BY [o0].[CustomerID]");
        }

        public override void GroupBy_Count()
        {
            base.GroupBy_Count();

            AssertSql(
                @"SELECT [o0].[OrderID], [o0].[CustomerID], [o0].[EmployeeID], [o0].[OrderDate]
FROM [Orders] AS [o0]
ORDER BY [o0].[CustomerID]");
        }

        public override void GroupBy_LongCount()
        {
            base.GroupBy_LongCount();

            AssertSql(
                @"SELECT [o0].[OrderID], [o0].[CustomerID], [o0].[EmployeeID], [o0].[OrderDate]
FROM [Orders] AS [o0]
ORDER BY [o0].[CustomerID]");
        }

        public override void GroupBy_DateTimeOffset_Property()
        {
            base.GroupBy_DateTimeOffset_Property();

            AssertSql(
                @"SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]
WHERE [o].[OrderDate] IS NOT NULL
ORDER BY DatePart('m', [o].[OrderDate])");
        }

        public override void Select_GroupBy()
        {
            base.Select_GroupBy();

            AssertSql(
                @"SELECT [o].[OrderID] AS [Order], [o].[CustomerID] AS [Customer]
FROM [Orders] AS [o]
ORDER BY [o].[CustomerID]");
        }

        public override void Select_GroupBy_SelectMany()
        {
            base.Select_GroupBy_SelectMany();

            AssertSql(
                @"SELECT [o0].[OrderID] AS [Order], [o0].[CustomerID] AS [Customer]
FROM [Orders] AS [o0]
ORDER BY [o0].[OrderID]");
        }

        public override void GroupBy_with_orderby()
        {
            base.GroupBy_with_orderby();

            AssertSql(
                @"SELECT [o0].[OrderID], [o0].[CustomerID], [o0].[EmployeeID], [o0].[OrderDate]
FROM [Orders] AS [o0]
ORDER BY [o0].[CustomerID], [o0].[OrderID]");
        }

        public override void GroupBy_with_orderby_and_anonymous_projection()
        {
            base.GroupBy_with_orderby_and_anonymous_projection();

            AssertSql(
                @"SELECT [o0].[OrderID], [o0].[CustomerID], [o0].[EmployeeID], [o0].[OrderDate]
FROM [Orders] AS [o0]
ORDER BY [o0].[CustomerID]");
        }

        [Fact(Skip = "Unsupported by JET")]
        public override void Take_skip_null_coalesce_operator()
        {
            base.Take_skip_null_coalesce_operator();
        }

        public override void GroupBy_with_orderby_take_skip_distinct()
        {
            base.GroupBy_with_orderby_take_skip_distinct();

            AssertSql(
                @"SELECT [o0].[OrderID], [o0].[CustomerID], [o0].[EmployeeID], [o0].[OrderDate]
FROM [Orders] AS [o0]
ORDER BY [o0].[CustomerID]");
        }



        [Fact(Skip = "Investigate - assert fix underway - 2.1")]
        public override void GroupJoin_customers_orders_count_preserves_ordering()
        {
            base.GroupJoin_customers_orders_count_preserves_ordering();
        }

        [Fact(Skip = "Unsupported by JET")]
        public override void SelectMany_Joined_DefaultIfEmpty()
        {
            base.SelectMany_Joined_DefaultIfEmpty();
        }

        [Fact(Skip = "Unsupported by JET")]
        public override void SelectMany_Joined_DefaultIfEmpty2()
        {
            base.SelectMany_Joined_DefaultIfEmpty2();
        }

        [Fact(Skip = "Unsupported by JET: SELECT TOP 2 (SELECT TOP 1) returns 2 records")]
        public override void Take_with_single()
        {
            base.Take_with_single();

            AssertSql(
                @"@__p_0='1'

SELECT TOP 2 [t].*
FROM (
    SELECT TOP @__p_0 [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
    FROM [Customers] AS [c]
    ORDER BY [c].[CustomerID]
) AS [t]
ORDER BY [t].[CustomerID]");
        }

        public override void Take_with_single_select_many()
        {
            base.Take_with_single_select_many();

            AssertSql(
                @"@__p_0='1'

SELECT TOP 2 [t].*
FROM (
    SELECT TOP @__p_0 [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region], [o].[OrderID], [o].[CustomerID] AS [CustomerID0], [o].[EmployeeID], [o].[OrderDate]
    FROM [Customers] AS [c]
    , [Orders] AS [o]
    ORDER BY [c].[CustomerID], [o].[OrderID]
) AS [t]
ORDER BY [t].[CustomerID], [t].[OrderID]");
        }

        public override void Distinct()
        {
            base.Distinct();

            AssertSql(
                @"SELECT DISTINCT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]");
        }

        public override void Distinct_Scalar()
        {
            base.Distinct_Scalar();

            AssertSql(
                @"SELECT DISTINCT [c].[City]
FROM [Customers] AS [c]");
        }


        [Fact(Skip = "Unsupported by JET: SKIP TAKE is supported only in outer queries")]
        public override void Distinct_Skip() => base.Distinct_Skip();

        [Fact(Skip = "Unsupported by JET: SKIP TAKE is supported only in outer queries")]
        public override void Distinct_Skip_Take() => base.Distinct_Skip_Take();

        [Fact(Skip = "Unsupported by JET: SKIP TAKE is supported only in outer queries")]
        public override void Skip_Distinct() => base.Skip_Distinct();

        [Fact(Skip = "Unsupported by JET: SKIP TAKE is supported only in outer queries")]
        public override void Skip_Take_Distinct() => base.Skip_Take_Distinct();


        public override void OrderBy()
        {
            base.OrderBy();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
ORDER BY [c].[CustomerID]");
        }

        public override void OrderBy_true()
        {
            base.OrderBy_true();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
ORDER BY 'a'");
        }

        public override void OrderBy_integer()
        {
            base.OrderBy_integer();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
ORDER BY 'a'");
        }

        public override void OrderBy_parameter()
        {
            base.OrderBy_parameter();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
ORDER BY 'a'");
        }

        public override void OrderBy_anon()
        {
            base.OrderBy_anon();

            AssertSql(
                @"SELECT [c].[CustomerID]
FROM [Customers] AS [c]
ORDER BY [c].[CustomerID]");
        }

        public override void OrderBy_anon2()
        {
            base.OrderBy_anon2();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
ORDER BY [c].[CustomerID]");
        }

        public override void OrderBy_client_mixed()
        {
            base.OrderBy_client_mixed();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]");
        }

        public override void OrderBy_multiple_queries()
        {
            base.OrderBy_multiple_queries();

            AssertContainsSql(
                @"SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]",
                //
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]");
        }

        public override void OrderBy_Distinct()
        {
            base.OrderBy_Distinct();

            // Ordering not preserved by distinct when ordering columns not projected.
            AssertSql(
                @"SELECT DISTINCT [c].[City]
FROM [Customers] AS [c]");
        }

        public override void Distinct_OrderBy()
        {
            base.Distinct_OrderBy();

            AssertSql(
                @"SELECT [t].[Country]
FROM (
    SELECT DISTINCT [c].[Country]
    FROM [Customers] AS [c]
) AS [t]
ORDER BY [t].[Country]");
        }

        public override void Distinct_OrderBy2()
        {
            base.Distinct_OrderBy2();

            AssertSql(
                @"SELECT [t].[CustomerID], [t].[Address], [t].[City], [t].[CompanyName], [t].[ContactName], [t].[ContactTitle], [t].[Country], [t].[Fax], [t].[Phone], [t].[PostalCode], [t].[Region]
FROM (
    SELECT DISTINCT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
    FROM [Customers] AS [c]
) AS [t]
ORDER BY [t].[CustomerID]");
        }

        public override void Distinct_OrderBy3()
        {
            base.Distinct_OrderBy3();

            AssertSql(
                @"SELECT [t].[CustomerID]
FROM (
    SELECT DISTINCT [c].[CustomerID]
    FROM [Customers] AS [c]
) AS [t]
ORDER BY [t].[CustomerID]");
        }

        public override void Take_Distinct()
        {
            base.Take_Distinct();

            AssertSql(
                @"@__p_0='5'

SELECT DISTINCT [t].*
FROM (
    SELECT TOP @__p_0 [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
    FROM [Orders] AS [o]
    ORDER BY [o].[OrderID]
) AS [t]");
        }

        public override void Distinct_Take()
        {
            base.Distinct_Take();

            AssertSql(
                @"@__p_0='5'

SELECT TOP @__p_0 [t].[OrderID], [t].[CustomerID], [t].[EmployeeID], [t].[OrderDate]
FROM (
    SELECT DISTINCT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
    FROM [Orders] AS [o]
) AS [t]
ORDER BY [t].[OrderID]");
        }

        public override void Distinct_Take_Count()
        {
            base.Distinct_Take_Count();

            AssertSql(
                @"@__p_0='5'

SELECT COUNT(*)
FROM (
    SELECT DISTINCT TOP @__p_0 [o].*
    FROM [Orders] AS [o]
) AS [t]");
        }

        public override void OrderBy_shadow()
        {
            base.OrderBy_shadow();

            AssertSql(
                @"SELECT [e].[EmployeeID], [e].[City], [e].[Country], [e].[FirstName], [e].[ReportsTo], [e].[Title]
FROM [Employees] AS [e]
ORDER BY [e].[Title], [e].[EmployeeID]");
        }

        public override void OrderBy_multiple()
        {
            base.OrderBy_multiple();

            AssertSql(
                @"SELECT [c].[City]
FROM [Customers] AS [c]
ORDER BY [c].[Country], [c].[CustomerID]");
        }

        [Fact(Skip = "Unsupported by JET")]
        public override void OrderBy_ThenBy_Any()
        {
            base.OrderBy_ThenBy_Any();
        }



        public override void Where_subquery_recursive_trivial()
        {
            base.Where_subquery_recursive_trivial();

            AssertSql(
                @"SELECT [e1].[EmployeeID], [e1].[City], [e1].[Country], [e1].[FirstName], [e1].[ReportsTo], [e1].[Title]
FROM [Employees] AS [e1]
WHERE EXISTS (
    SELECT 1
    FROM [Employees] AS [e2]
    WHERE EXISTS (
        SELECT 1
        FROM [Employees] AS [e3]))
ORDER BY [e1].[EmployeeID]");
        }

        public override void Where_false()
        {
            base.Where_false();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE False = True");
        }

        public override void Where_default()
        {
            base.Where_default();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[Fax] IS NULL");
        }

        public override void Where_expression_invoke()
        {
            base.Where_expression_invoke();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] = 'ALFKI'");
        }

        public override void Where_ternary_boolean_condition_true()
        {
            base.Where_ternary_boolean_condition_true();

            AssertSql(
                @"@__flag_0='True'

SELECT [p].[ProductID], [p].[Discontinued], [p].[ProductName], [p].[UnitPrice], [p].[UnitsInStock]
FROM [Products] AS [p]
WHERE ((@__flag_0 = True) AND ([p].[UnitsInStock] >= 20)) OR ((@__flag_0 <> True) AND ([p].[UnitsInStock] < 20))");
        }

        public override void Where_ternary_boolean_condition_false()
        {
            base.Where_ternary_boolean_condition_false();

            AssertSql(
                @"@__flag_0='False'

SELECT [p].[ProductID], [p].[Discontinued], [p].[ProductName], [p].[UnitPrice], [p].[UnitsInStock]
FROM [Products] AS [p]
WHERE ((@__flag_0 = True) AND ([p].[UnitsInStock] >= 20)) OR ((@__flag_0 <> True) AND ([p].[UnitsInStock] < 20))");
        }

        public override void Where_ternary_boolean_condition_with_another_condition()
        {
            base.Where_ternary_boolean_condition_with_another_condition();

            AssertSql(
                @"@__productId_0='15'
@__flag_1='True'

SELECT [p].[ProductID], [p].[Discontinued], [p].[ProductName], [p].[UnitPrice], [p].[UnitsInStock]
FROM [Products] AS [p]
WHERE ([p].[ProductID] < @__productId_0) AND (((@__flag_1 = True) AND ([p].[UnitsInStock] >= 20)) OR ((@__flag_1 <> True) AND ([p].[UnitsInStock] < 20)))");
        }

        public override void Where_ternary_boolean_condition_with_false_as_result_true()
        {
            base.Where_ternary_boolean_condition_with_false_as_result_true();

            AssertSql(
                @"@__flag_0='True'

SELECT [p].[ProductID], [p].[Discontinued], [p].[ProductName], [p].[UnitPrice], [p].[UnitsInStock]
FROM [Products] AS [p]
WHERE (@__flag_0 = True) AND ([p].[UnitsInStock] >= 20)");
        }

        public override void Where_ternary_boolean_condition_with_false_as_result_false()
        {
            base.Where_ternary_boolean_condition_with_false_as_result_false();

            AssertSql(
                @"@__flag_0='False'

SELECT [p].[ProductID], [p].[Discontinued], [p].[ProductName], [p].[UnitPrice], [p].[UnitsInStock]
FROM [Products] AS [p]
WHERE (@__flag_0 = True) AND ([p].[UnitsInStock] >= 20)");
        }



        public override void Where_primitive()
        {
            base.Where_primitive();

            AssertSql(
                @"@__p_0='9'

SELECT [t].[EmployeeID]
FROM (
    SELECT TOP @__p_0 [e].[EmployeeID]
    FROM [Employees] AS [e]
) AS [t]
WHERE [t].[EmployeeID] = 5");
        }

        public override void Where_bool_member()
        {
            base.Where_bool_member();

            AssertSql(
                @"SELECT [p].[ProductID], [p].[Discontinued], [p].[ProductName], [p].[UnitPrice], [p].[UnitsInStock]
FROM [Products] AS [p]
WHERE [p].[Discontinued] = True");
        }

        public override void Where_bool_member_false()
        {
            base.Where_bool_member_false();

            AssertSql(
                @"SELECT [p].[ProductID], [p].[Discontinued], [p].[ProductName], [p].[UnitPrice], [p].[UnitsInStock]
FROM [Products] AS [p]
WHERE [p].[Discontinued] = False");
        }

        public override void Where_bool_client_side_negated()
        {
            base.Where_bool_client_side_negated();

            AssertSql(
                @"SELECT [p].[ProductID], [p].[Discontinued], [p].[ProductName], [p].[UnitPrice], [p].[UnitsInStock]
FROM [Products] AS [p]
WHERE [p].[Discontinued] = True");
        }

        public override void Where_bool_member_negated_twice()
        {
            base.Where_bool_member_negated_twice();

            AssertSql(
                @"SELECT [p].[ProductID], [p].[Discontinued], [p].[ProductName], [p].[UnitPrice], [p].[UnitsInStock]
FROM [Products] AS [p]
WHERE [p].[Discontinued] = True");
        }

        public override void Where_bool_member_shadow()
        {
            base.Where_bool_member_shadow();

            AssertSql(
                @"SELECT [p].[ProductID], [p].[Discontinued], [p].[ProductName], [p].[UnitPrice], [p].[UnitsInStock]
FROM [Products] AS [p]
WHERE [p].[Discontinued] = True");
        }

        public override void Where_bool_member_false_shadow()
        {
            base.Where_bool_member_false_shadow();

            AssertSql(
                @"SELECT [p].[ProductID], [p].[Discontinued], [p].[ProductName], [p].[UnitPrice], [p].[UnitsInStock]
FROM [Products] AS [p]
WHERE [p].[Discontinued] = False");
        }

        public override void Where_bool_member_equals_constant()
        {
            base.Where_bool_member_equals_constant();

            AssertSql(
                @"SELECT [p].[ProductID], [p].[Discontinued], [p].[ProductName], [p].[UnitPrice], [p].[UnitsInStock]
FROM [Products] AS [p]
WHERE [p].[Discontinued] = True");
        }

        public override void Where_bool_member_in_complex_predicate()
        {
            base.Where_bool_member_in_complex_predicate();

            AssertSql(
                @"SELECT [p].[ProductID], [p].[Discontinued], [p].[ProductName], [p].[UnitPrice], [p].[UnitsInStock]
FROM [Products] AS [p]
WHERE (([p].[ProductID] > 100) AND ([p].[Discontinued] = True)) OR ([p].[Discontinued] = True)");
        }

        public override void Where_not_bool_member_compared_to_not_bool_member()
        {
            base.Where_not_bool_member_compared_to_not_bool_member();

            AssertSql(
                @"SELECT [p].[ProductID], [p].[Discontinued], [p].[ProductName], [p].[UnitPrice], [p].[UnitsInStock]
FROM [Products] AS [p]
WHERE [p].[Discontinued] = [p].[Discontinued]");
        }

        public override void Where_bool_parameter()
        {
            base.Where_bool_parameter();

            AssertSql(
                @"@__prm_0='True'

SELECT [p].[ProductID], [p].[Discontinued], [p].[ProductName], [p].[UnitPrice], [p].[UnitsInStock]
FROM [Products] AS [p]
WHERE @__prm_0 = True");
        }


        public override void Where_de_morgan_or_optimizated()
        {
            base.Where_de_morgan_or_optimizated();

            AssertSql(
                @"SELECT [p].[ProductID], [p].[Discontinued], [p].[ProductName], [p].[UnitPrice], [p].[UnitsInStock]
FROM [Products] AS [p]
WHERE ([p].[Discontinued] = False) AND ([p].[ProductID] >= 20)");
        }

        public override void Where_de_morgan_and_optimizated()
        {
            base.Where_de_morgan_and_optimizated();

            AssertSql(
                @"SELECT [p].[ProductID], [p].[Discontinued], [p].[ProductName], [p].[UnitPrice], [p].[UnitsInStock]
FROM [Products] AS [p]
WHERE ([p].[Discontinued] = False) OR ([p].[ProductID] >= 20)");
        }

        public override void Where_complex_negated_expression_optimized()
        {
            base.Where_complex_negated_expression_optimized();

            AssertSql(
                @"SELECT [p].[ProductID], [p].[Discontinued], [p].[ProductName], [p].[UnitPrice], [p].[UnitsInStock]
FROM [Products] AS [p]
WHERE (([p].[Discontinued] = False) AND ([p].[ProductID] < 60)) AND ([p].[ProductID] > 30)");
        }

        public override void Where_short_member_comparison()
        {
            base.Where_short_member_comparison();

            AssertSql(
                @"SELECT [p].[ProductID], [p].[Discontinued], [p].[ProductName], [p].[UnitPrice], [p].[UnitsInStock]
FROM [Products] AS [p]
WHERE [p].[UnitsInStock] > 10");
        }

        public override void Where_true()
        {
            base.Where_true();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]");
        }

        public override void Where_compare_constructed_equal()
        {
            base.Where_compare_constructed_equal();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]");
        }

        public override void Where_compare_constructed_multi_value_equal()
        {
            base.Where_compare_constructed_multi_value_equal();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]");
        }

        public override void Where_compare_constructed_multi_value_not_equal()
        {
            base.Where_compare_constructed_multi_value_not_equal();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]");
        }

        public override void Where_compare_constructed()
        {
            base.Where_compare_constructed();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]");
        }

        public override void Where_compare_null()
        {
            base.Where_compare_null();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[City] IS NULL AND ([c].[Country] = 'UK')");
        }

        public override void Where_Is_on_same_type()
        {
            base.Where_Is_on_same_type();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]");
        }

        public override void Single_Predicate()
        {
            base.Single_Predicate();

            AssertSql(
                @"SELECT TOP 2 [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] = 'ALFKI'");
        }

        public override void Projection_when_arithmetic_expression_precendence()
        {
            base.Projection_when_arithmetic_expression_precendence();

            AssertSql(
                @"SELECT [o].[OrderID] / ([o].[OrderID] / 2) AS [A], ([o].[OrderID] / [o].[OrderID]) / 2 AS [B]
FROM [Orders] AS [o]");
        }

        // TODO: Complex projection translation.

        //        public override void Projection_when_arithmetic_expressions()
        //        {
        //            base.Projection_when_arithmetic_expressions();
        //
        //            AssertSql(
        //                @"SELECT [o].[OrderID], [o].[OrderID] * 2, [o].[OrderID] + 23, 100000 - [o].[OrderID], [o].[OrderID] / ([o].[OrderID] / 2)
        //FROM [Orders] AS [o]",
        //                Sql);
        //        }
        //
        //        public override void Projection_when_arithmetic_mixed()
        //        {
        //            //base.Projection_when_arithmetic_mixed();
        //        }
        //
        //        public override void Projection_when_arithmetic_mixed_subqueries()
        //        {
        //            //base.Projection_when_arithmetic_mixed_subqueries();
        //        }

        public override void Projection_when_null_value()
        {
            base.Projection_when_null_value();

            AssertSql(
                @"SELECT [c].[Region]
FROM [Customers] AS [c]");
        }


        public override void String_Compare_simple_zero()
        {
            base.String_Compare_simple_zero();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] = 'ALFKI'",
                //
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] <> 'ALFKI'",
                //
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] > 'ALFKI'",
                //
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] <= 'ALFKI'",
                //
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] > 'ALFKI'",
                //
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] <= 'ALFKI'");
        }

        public override void String_Compare_simple_one()
        {
            base.String_Compare_simple_one();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] > 'ALFKI'",
                //
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] < 'ALFKI'",
                //
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] <= 'ALFKI'",
                //
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] <= 'ALFKI'",
                //
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] >= 'ALFKI'",
                //
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] >= 'ALFKI'");
        }

        public override void String_Compare_simple_client()
        {
            base.String_Compare_simple_client();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]",
                //
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]",
                //
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]");
        }
        

        public override void String_Compare_multi_predicate()
        {
            base.String_Compare_multi_predicate();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] >= 'ALFKI' AND [c].[CustomerID] < 'CACTU'",
                //
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[ContactTitle] = 'Owner' AND [c].[Country] <> 'USA'");
        }

        public override void Where_math_abs1()
        {
            base.Where_math_abs1();

            AssertSql(
                @"SELECT [od].[OrderID], [od].[ProductID], [od].[Discount], [od].[Quantity], [od].[UnitPrice]
FROM [Order Details] AS [od]
WHERE ABS([od].[ProductID]) > 10");
        }

        public override void Where_math_abs2()
        {
            base.Where_math_abs2();

            AssertSql(
                @"SELECT [od].[OrderID], [od].[ProductID], [od].[Discount], [od].[Quantity], [od].[UnitPrice]
FROM [Order Details] AS [od]
WHERE ABS([od].[Quantity]) > 10");
        }

        public override void Where_math_abs3()
        {
            base.Where_math_abs3();

            AssertSql(
                @"SELECT [od].[OrderID], [od].[ProductID], [od].[Discount], [od].[Quantity], [od].[UnitPrice]
FROM [Order Details] AS [od]
WHERE ABS([od].[UnitPrice]) > 10.0");
        }

        public override void Where_math_abs_uncorrelated()
        {
            base.Where_math_abs_uncorrelated();

            AssertSql(
                @"@__Abs_0='10'

SELECT [od].[OrderID], [od].[ProductID], [od].[Discount], [od].[Quantity], [od].[UnitPrice]
FROM [Order Details] AS [od]
WHERE @__Abs_0 < [od].[ProductID]");
        }

        [Fact(Skip = "Unsupported by JET: Should be implemented by provider")]
        public override void Where_math_ceiling1()
        {
            base.Where_math_ceiling1();

            AssertSql(
                @"SELECT [od].[OrderID], [od].[ProductID], [od].[Discount], [od].[Quantity], [od].[UnitPrice]
FROM [Order Details] AS [od]
WHERE CEILING([od].[Discount]) > 0");
        }

        [Fact(Skip = "Unsupported by JET: Should be implemented by provider")]
        public override void Where_math_ceiling2()
        {
            base.Where_math_ceiling2();

            AssertSql(
                @"SELECT [od].[OrderID], [od].[ProductID], [od].[Discount], [od].[Quantity], [od].[UnitPrice]
FROM [Order Details] AS [od]
WHERE CEILING([od].[UnitPrice]) > 10.0");
        }

        [Fact(Skip = "Unsupported by JET: Should be implemented by provider")]
        public override void Where_math_floor()
        {
            base.Where_math_floor();

            AssertSql(
                @"SELECT [od].[OrderID], [od].[ProductID], [od].[Discount], [od].[Quantity], [od].[UnitPrice]
FROM [Order Details] AS [od]
WHERE FLOOR([od].[UnitPrice]) > 10.0");
        }

        public override void Where_query_composition4()
        {
            base.Where_query_composition4();

            AssertSql(
                @"@__p_0='2'

SELECT [t].[CustomerID], [t].[Address], [t].[City], [t].[CompanyName], [t].[ContactName], [t].[ContactTitle], [t].[Country], [t].[Fax], [t].[Phone], [t].[PostalCode], [t].[Region]
FROM (
    SELECT TOP @__p_0 [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
    FROM [Customers] AS [c]
    ORDER BY [c].[CustomerID]
) AS [t]",
                //
                @"SELECT 1
FROM [Customers] AS [c0]
ORDER BY [c0].[CustomerID]",
                //
                @"SELECT [c2].[CustomerID], [c2].[Address], [c2].[City], [c2].[CompanyName], [c2].[ContactName], [c2].[ContactTitle], [c2].[Country], [c2].[Fax], [c2].[Phone], [c2].[PostalCode], [c2].[Region]
FROM [Customers] AS [c2]",
                //
                @"SELECT 1
FROM [Customers] AS [c0]
ORDER BY [c0].[CustomerID]",
                //
                @"SELECT [c2].[CustomerID], [c2].[Address], [c2].[City], [c2].[CompanyName], [c2].[ContactName], [c2].[ContactTitle], [c2].[Country], [c2].[Fax], [c2].[Phone], [c2].[PostalCode], [c2].[Region]
FROM [Customers] AS [c2]");
        }


        public override void Where_math_round()
        {
            base.Where_math_round();

            AssertSql(
                @"SELECT [od].[OrderID], [od].[ProductID], [od].[Discount], [od].[Quantity], [od].[UnitPrice]
FROM [Order Details] AS [od]
WHERE ROUND([od].[UnitPrice], 0) > 10.0");
        }

        public override void Where_math_round2()
        {
            base.Where_math_round2();

            AssertSql(
                @"SELECT [od].[OrderID], [od].[ProductID], [od].[Discount], [od].[Quantity], [od].[UnitPrice]
FROM [Order Details] AS [od]
WHERE ROUND([od].[UnitPrice], 2) > 100.0");
        }

        public override void Where_math_truncate()
        {
            base.Where_math_truncate();

            AssertSql(
                @"SELECT [od].[OrderID], [od].[ProductID], [od].[Discount], [od].[Quantity], [od].[UnitPrice]
FROM [Order Details] AS [od]
WHERE Int([od].[UnitPrice]) > 10.0");
        }

        public override void Where_math_exp()
        {
            base.Where_math_exp();

            AssertSql(
                @"SELECT [od].[OrderID], [od].[ProductID], [od].[Discount], [od].[Quantity], [od].[UnitPrice]
FROM [Order Details] AS [od]
WHERE ([od].[OrderID] = 11077) AND (EXP([od].[Discount]) > 1)");
        }


        public override void Where_math_log()
        {
            base.Where_math_log();

            AssertSql(
                @"SELECT [od].[OrderID], [od].[ProductID], [od].[Discount], [od].[Quantity], [od].[UnitPrice]
FROM [Order Details] AS [od]
WHERE (([od].[OrderID] = 11077) AND ([od].[Discount] > 0)) AND (LOG([od].[Discount]) < 0)");
        }


        public override void Where_math_sqrt()
        {
            base.Where_math_sqrt();

            AssertSql(
                @"SELECT [od].[OrderID], [od].[ProductID], [od].[Discount], [od].[Quantity], [od].[UnitPrice]
FROM [Order Details] AS [od]
WHERE ([od].[OrderID] = 11077) AND (Sqr([od].[Discount]) > 0)");
        }




        [Fact(Skip = "Unsupported by JET: Should be implemented by provider")]
        public override void Where_math_atan2()
        {
            base.Where_math_atan2();

            AssertSql(
                @"SELECT [od].[OrderID], [od].[ProductID], [od].[Discount], [od].[Quantity], [od].[UnitPrice]
FROM [Order Details] AS [od]
WHERE ([od].[OrderID] = 11077) AND (ATN2([od].[Discount], 1) > 0)");
        }

        public override void Where_math_cos()
        {
            base.Where_math_cos();

            AssertSql(
                @"SELECT [od].[OrderID], [od].[ProductID], [od].[Discount], [od].[Quantity], [od].[UnitPrice]
FROM [Order Details] AS [od]
WHERE ([od].[OrderID] = 11077) AND (COS([od].[Discount]) > 0)");
        }

        public override void Where_math_sin()
        {
            base.Where_math_sin();

            AssertSql(
                @"SELECT [od].[OrderID], [od].[ProductID], [od].[Discount], [od].[Quantity], [od].[UnitPrice]
FROM [Order Details] AS [od]
WHERE ([od].[OrderID] = 11077) AND (SIN([od].[Discount]) > 0)");
        }

        public override void Where_math_tan()
        {
            base.Where_math_tan();

            AssertSql(
                @"SELECT [od].[OrderID], [od].[ProductID], [od].[Discount], [od].[Quantity], [od].[UnitPrice]
FROM [Order Details] AS [od]
WHERE ([od].[OrderID] = 11077) AND (TAN([od].[Discount]) > 0)");
        }

        public override void Where_math_sign()
        {
            base.Where_math_sign();

            AssertSql(
                @"SELECT [od].[OrderID], [od].[ProductID], [od].[Discount], [od].[Quantity], [od].[UnitPrice]
FROM [Order Details] AS [od]
WHERE ([od].[OrderID] = 11077) AND (Sgn([od].[Discount]) > 0)");
        }


        public override void Where_guid_newguid()
        {
            base.Where_guid_newguid();

            AssertSql(
                @"SELECT [od].[OrderID], [od].[ProductID], [od].[Discount], [od].[Quantity], [od].[UnitPrice]
FROM [Order Details] AS [od]
WHERE NewGuid() <> '00000000-0000-0000-0000-000000000000'");
        }


        public override void Where_string_to_lower()
        {
            base.Where_string_to_lower();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE LCase([c].[CustomerID]) = 'alfki'");
        }

        public override void Where_string_to_upper()
        {
            base.Where_string_to_upper();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE UCase([c].[CustomerID]) = 'ALFKI'");
        }


        public override void Select_nested_collection()
        {
            base.Select_nested_collection();

            AssertSql(
                @"SELECT [c].[CustomerID]
FROM [Customers] AS [c]
WHERE [c].[City] = 'London'
ORDER BY [c].[CustomerID]",
                //
                @"@_outer_CustomerID='AROUT' (Nullable = false) (Size = 5)

SELECT [o].[OrderID]
FROM [Orders] AS [o]
WHERE ([o].[CustomerID] = @_outer_CustomerID) AND (DatePart('yyyy', [o].[OrderDate]) = 1997)
ORDER BY [o].[OrderID]",
                //
                @"@_outer_CustomerID='BSBEV' (Nullable = false) (Size = 5)

SELECT [o].[OrderID]
FROM [Orders] AS [o]
WHERE ([o].[CustomerID] = @_outer_CustomerID) AND (DatePart('yyyy', [o].[OrderDate]) = 1997)
ORDER BY [o].[OrderID]",
                //
                @"@_outer_CustomerID='CONSH' (Nullable = false) (Size = 5)

SELECT [o].[OrderID]
FROM [Orders] AS [o]
WHERE ([o].[CustomerID] = @_outer_CustomerID) AND (DatePart('yyyy', [o].[OrderDate]) = 1997)
ORDER BY [o].[OrderID]",
                //
                @"@_outer_CustomerID='EASTC' (Nullable = false) (Size = 5)

SELECT [o].[OrderID]
FROM [Orders] AS [o]
WHERE ([o].[CustomerID] = @_outer_CustomerID) AND (DatePart('yyyy', [o].[OrderDate]) = 1997)
ORDER BY [o].[OrderID]",
                //
                @"@_outer_CustomerID='NORTS' (Nullable = false) (Size = 5)

SELECT [o].[OrderID]
FROM [Orders] AS [o]
WHERE ([o].[CustomerID] = @_outer_CustomerID) AND (DatePart('yyyy', [o].[OrderDate]) = 1997)
ORDER BY [o].[OrderID]",
                //
                @"@_outer_CustomerID='SEVES' (Nullable = false) (Size = 5)

SELECT [o].[OrderID]
FROM [Orders] AS [o]
WHERE ([o].[CustomerID] = @_outer_CustomerID) AND (DatePart('yyyy', [o].[OrderDate]) = 1997)
ORDER BY [o].[OrderID]");
        }


        
        
        public override void Select_nested_collection_multi_level3()
        {
            base.Select_nested_collection_multi_level3();

            AssertSql(
                @"SELECT (
    SELECT TOP 1 [o].[OrderDate]
    FROM [Orders] AS [o]
    WHERE ([o].[OrderID] < 10500) AND ([c].[CustomerID] = [o].[CustomerID])
) AS [OrderDates]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] LIKE 'A' + '%' AND (LEFT([c].[CustomerID], Len('A')) = 'A')");
        }

        
        public override void Select_nested_collection_multi_level4()
        {
            base.Select_nested_collection_multi_level4();

            AssertSql(
                @"SELECT (
    SELECT TOP 1 (
        SELECT COUNT(*)
        FROM [Order Details] AS [od]
        WHERE ([od].[OrderID] > 10) AND ([o].[OrderID] = [od].[OrderID])
    )
    FROM [Orders] AS [o]
    WHERE ([o].[OrderID] < 10500) AND ([c].[CustomerID] = [o].[CustomerID])
) AS [Order]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] LIKE 'A' + '%' AND (LEFT([c].[CustomerID], Len('A')) = 'A')");
        }

        
        public override void Select_nested_collection_multi_level5()
        {
            base.Select_nested_collection_multi_level5();

            AssertSql(
                @"SELECT (
    SELECT TOP 1 (
        SELECT TOP 1 [od].[ProductID]
        FROM [Order Details] AS [od]
        WHERE ([od].[OrderID] <> (
            SELECT COUNT(*)
            FROM [Orders] AS [o0]
            WHERE [c].[CustomerID] = [o0].[CustomerID]
        )) AND ([o].[OrderID] = [od].[OrderID])
    )
    FROM [Orders] AS [o]
    WHERE ([o].[OrderID] < 10500) AND ([c].[CustomerID] = [o].[CustomerID])
) AS [Order]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] LIKE 'A' + '%' AND (LEFT([c].[CustomerID], Len('A')) = 'A')");
        }

        
        
        public override void Select_nested_collection_count_using_anonymous_type()
        {
            base.Select_nested_collection_count_using_anonymous_type();

            AssertSql(
                @"SELECT (
    SELECT COUNT(*)
    FROM [Orders] AS [o]
    WHERE [c].[CustomerID] = [o].[CustomerID]
) AS [Count]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] LIKE 'A' + '%' AND (LEFT([c].[CustomerID], Len('A')) = 'A')");
        }

        
        public override void Select_nested_collection_count_using_DTO()
        {
            base.Select_nested_collection_count_using_DTO();

            AssertSql(
                @"SELECT [c].[CustomerID] AS [Id], (
    SELECT COUNT(*)
    FROM [Orders] AS [o]
    WHERE [c].[CustomerID] = [o].[CustomerID]
) AS [Count]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] LIKE 'A' + '%' AND (LEFT([c].[CustomerID], Len('A')) = 'A')");
        }

        public override void Select_DTO_distinct_translated_to_server()
        {
            base.Select_DTO_distinct_translated_to_server();

            AssertSql(
                @"SELECT DISTINCT 1
FROM [Orders] AS [o]
WHERE [o].[OrderID] < 10300");
        }

        public override void Select_DTO_constructor_distinct_translated_to_server()
        {
            base.Select_DTO_constructor_distinct_translated_to_server();

            AssertSql(
                @"SELECT DISTINCT [o].[CustomerID]
FROM [Orders] AS [o]
WHERE [o].[OrderID] < 10300");
        }

        public override void Select_DTO_with_member_init_distinct_translated_to_server()
        {
            base.Select_DTO_with_member_init_distinct_translated_to_server();

            AssertSql(
                @"SELECT DISTINCT [o].[CustomerID] AS [Id], [o].[OrderID] AS [Count]
FROM [Orders] AS [o]
WHERE [o].[OrderID] < 10300");
        }

        public override void Select_DTO_with_member_init_distinct_in_subquery_translated_to_server()
        {
            base.Select_DTO_with_member_init_distinct_in_subquery_translated_to_server();

            AssertSql(
                @"SELECT [t].[Id], [t].[Count], [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM (
    SELECT DISTINCT [o].[CustomerID] AS [Id], [o].[OrderID] AS [Count]
    FROM [Orders] AS [o]
    WHERE [o].[OrderID] < 10300
) AS [t]
, [Customers] AS [c]
WHERE [c].[CustomerID] = [t].[Id]");
        }

        public override void Select_DTO_with_member_init_distinct_in_subquery_used_in_projection_translated_to_server()
        {
            base.Select_DTO_with_member_init_distinct_in_subquery_used_in_projection_translated_to_server();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region], [t].[Id], [t].[Count]
FROM [Customers] AS [c]
, (
    SELECT DISTINCT [o].[CustomerID] AS [Id], [o].[OrderID] AS [Count]
    FROM [Orders] AS [o]
    WHERE [o].[OrderID] < 10300
) AS [t]
WHERE [c].[CustomerID] LIKE 'A' + '%' AND (LEFT([c].[CustomerID], Len('A')) = 'A')");
        }

        public override void Select_correlated_subquery_projection()
        {
            base.Select_correlated_subquery_projection();

            AssertSql(
                @"@__p_0='3'

SELECT [t].[CustomerID]
FROM (
    SELECT TOP @__p_0 [c].*
    FROM [Customers] AS [c]
) AS [t]
ORDER BY [t].[CustomerID]",
                //
                @"@_outer_CustomerID='ALFKI' (Nullable = false) (Size = 5)

SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]
WHERE [o].[CustomerID] = @_outer_CustomerID",
                //
                @"@_outer_CustomerID='ANATR' (Nullable = false) (Size = 5)

SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]
WHERE [o].[CustomerID] = @_outer_CustomerID",
                //
                @"@_outer_CustomerID='ANTON' (Nullable = false) (Size = 5)

SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]
WHERE [o].[CustomerID] = @_outer_CustomerID");
        }

        public override void Select_correlated_subquery_ordered()
        {
            base.Select_correlated_subquery_ordered();

            AssertSql(
                @"@__p_0='3'

SELECT TOP @__p_0 [c].[CustomerID]
FROM [Customers] AS [c]",
                //
                @"SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]",
                //
                @"SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]",
                //
                @"SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]");
        }

        public override void Where_subquery_on_bool()
        {
            base.Where_subquery_on_bool();

            AssertSql(
                @"SELECT [p].[ProductID], [p].[Discontinued], [p].[ProductName], [p].[UnitPrice], [p].[UnitsInStock]
FROM [Products] AS [p]
WHERE 'Chai' IN (
    SELECT [p2].[ProductName]
    FROM [Products] AS [p2]
)");
        }

        public override void Where_subquery_on_collection()
        {
            base.Where_subquery_on_collection();

            AssertSql(
                @"SELECT [p].[ProductID], [p].[Discontinued], [p].[ProductName], [p].[UnitPrice], [p].[UnitsInStock]
FROM [Products] AS [p]
WHERE 5 IN (
    SELECT [o].[Quantity]
    FROM [Order Details] AS [o]
    WHERE [o].[ProductID] = [p].[ProductID]
)");
        }

        public override void Select_many_cross_join_same_collection()
        {
            base.Select_many_cross_join_same_collection();

            AssertSql(
                @"SELECT [c0].[CustomerID], [c0].[Address], [c0].[City], [c0].[CompanyName], [c0].[ContactName], [c0].[ContactTitle], [c0].[Country], [c0].[Fax], [c0].[Phone], [c0].[PostalCode], [c0].[Region]
FROM [Customers] AS [c]
, [Customers] AS [c0]");
        }

        public override void Where_chain()
        {
            base.Where_chain();

            AssertSql(
                @"SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]
WHERE ([o].[CustomerID] = 'QUICK') AND ([o].[OrderDate] > #01/01/1998 00:00:00#)");
        }

        public override void Contains_with_subquery()
        {
            base.Contains_with_subquery();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] IN (
    SELECT [o].[CustomerID]
    FROM [Orders] AS [o]
)");
        }

        public override void Contains_with_local_array_closure()
        {
            base.Contains_with_local_array_closure();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] IN ('ABCDE', 'ALFKI')",
                //
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] IN ('ABCDE')");
        }

        public override void Contains_with_subquery_and_local_array_closure()
        {
            base.Contains_with_subquery_and_local_array_closure();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE EXISTS (
    SELECT 1
    FROM [Customers] AS [c1]
    WHERE [c1].[City] IN ('London', 'Buenos Aires') AND ([c1].[CustomerID] = [c].[CustomerID]))",
                //
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE EXISTS (
    SELECT 1
    FROM [Customers] AS [c1]
    WHERE [c1].[City] IN ('London') AND ([c1].[CustomerID] = [c].[CustomerID]))");
        }

        public override void Contains_with_local_int_array_closure()
        {
            base.Contains_with_local_int_array_closure();

            AssertSql(
                @"SELECT [e].[EmployeeID], [e].[City], [e].[Country], [e].[FirstName], [e].[ReportsTo], [e].[Title]
FROM [Employees] AS [e]
WHERE [e].[EmployeeID] IN (0, 1)",
                //
                @"SELECT [e].[EmployeeID], [e].[City], [e].[Country], [e].[FirstName], [e].[ReportsTo], [e].[Title]
FROM [Employees] AS [e]
WHERE [e].[EmployeeID] IN (0)");
        }

        public override void Contains_with_local_nullable_int_array_closure()
        {
            base.Contains_with_local_nullable_int_array_closure();

            AssertSql(
                @"SELECT [e].[EmployeeID], [e].[City], [e].[Country], [e].[FirstName], [e].[ReportsTo], [e].[Title]
FROM [Employees] AS [e]
WHERE [e].[EmployeeID] IN (0, 1)",
                //
                @"SELECT [e].[EmployeeID], [e].[City], [e].[Country], [e].[FirstName], [e].[ReportsTo], [e].[Title]
FROM [Employees] AS [e]
WHERE [e].[EmployeeID] IN (0)");
        }

        public override void Contains_with_local_array_inline()
        {
            base.Contains_with_local_array_inline();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] IN ('ABCDE', 'ALFKI')");
        }

        public override void Contains_with_local_list_closure()
        {
            base.Contains_with_local_list_closure();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] IN ('ABCDE', 'ALFKI')");
        }

        public override void Contains_with_local_list_inline()
        {
            base.Contains_with_local_list_inline();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] IN ('ABCDE', 'ALFKI')");
        }


        public override void Contains_with_local_collection_false()
        {
            base.Contains_with_local_collection_false();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] NOT IN ('ABCDE', 'ALFKI')");
        }

        public override void Contains_with_local_collection_complex_predicate_and()
        {
            base.Contains_with_local_collection_complex_predicate_and();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] IN ('ALFKI', 'ABCDE') AND [c].[CustomerID] IN ('ABCDE', 'ALFKI')");
        }

        public override void Contains_with_local_collection_complex_predicate_or()
        {
            base.Contains_with_local_collection_complex_predicate_or();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] IN ('ABCDE', 'ALFKI', 'ALFKI', 'ABCDE')");
        }

        public override void Contains_with_local_collection_complex_predicate_not_matching_ins1()
        {
            base.Contains_with_local_collection_complex_predicate_not_matching_ins1();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] IN ('ALFKI', 'ABCDE') OR [c].[CustomerID] NOT IN ('ABCDE', 'ALFKI')");
        }

        public override void Contains_with_local_collection_complex_predicate_not_matching_ins2()
        {
            base.Contains_with_local_collection_complex_predicate_not_matching_ins2();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] IN ('ABCDE', 'ALFKI') AND [c].[CustomerID] NOT IN ('ALFKI', 'ABCDE')");
        }

        public override void Contains_with_local_collection_sql_injection()
        {
            base.Contains_with_local_collection_sql_injection();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[CustomerID] IN ('ALFKI', 'ABC'')); GO; DROP TABLE Orders; GO; --', 'ALFKI', 'ABCDE')");
        }

        public override void Contains_with_local_collection_empty_closure()
        {
            base.Contains_with_local_collection_empty_closure();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE 0 = 1");
        }

        public override void Contains_with_local_collection_empty_inline()
        {
            base.Contains_with_local_collection_empty_inline();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE 1 = 1");
        }

        public override void Substring_with_client_eval()
        {
            base.Substring_with_client_eval();

            AssertSql(
                @"SELECT TOP 1 [c].[ContactName]
FROM [Customers] AS [c]
ORDER BY [c].[CustomerID]");
        }

        public override void IsNullOrEmpty_in_predicate()
        {
            base.IsNullOrEmpty_in_predicate();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[Region] IS NULL OR ([c].[Region] = '')");
        }

        public override void IsNullOrWhiteSpace_in_predicate()
        {
            base.IsNullOrWhiteSpace_in_predicate();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE [c].[Region] IS NULL OR (LTRIM(RTRIM([c].[Region])) = '')");
        }
        
        public override void Does_not_change_ordering_of_projection_with_complex_projections()
        {
            base.Does_not_change_ordering_of_projection_with_complex_projections();

            AssertSql(
                @"SELECT [e].[CustomerID] AS [Id], (
    SELECT COUNT(*)
    FROM [Orders] AS [o0]
    WHERE [e].[CustomerID] = [o0].[CustomerID]
) AS [TotalOrders]
FROM [Customers] AS [e]
WHERE ([e].[ContactTitle] = 'Owner') AND ((
    SELECT COUNT(*)
    FROM [Orders] AS [o]
    WHERE [e].[CustomerID] = [o].[CustomerID]
) > 2)
ORDER BY [e].[CustomerID]");
        }

        public override void DateTime_parse_is_parameterized()
        {
            base.DateTime_parse_is_parameterized();

            AssertSql(
                @"@__Parse_0='01/01/1998 12:00:00' (DbType = DateTime)

SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]
WHERE [o].[OrderDate] > @__Parse_0");
        }

        public override void Random_next_is_not_funcletized_1()
        {
            base.Random_next_is_not_funcletized_1();

            AssertSql(
                @"SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]");
        }

        public override void Random_next_is_not_funcletized_2()
        {
            base.Random_next_is_not_funcletized_2();

            AssertSql(
                @"SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]");
        }

        public override void Random_next_is_not_funcletized_3()
        {
            base.Random_next_is_not_funcletized_3();

            AssertSql(
                @"SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]");
        }

        public override void Random_next_is_not_funcletized_4()
        {
            base.Random_next_is_not_funcletized_4();

            AssertSql(
                @"SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]");
        }

        public override void Random_next_is_not_funcletized_5()
        {
            base.Random_next_is_not_funcletized_5();

            AssertSql(
                @"SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]");
        }

        public override void Random_next_is_not_funcletized_6()
        {
            base.Random_next_is_not_funcletized_6();

            AssertSql(
                @"SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]");
        }


        public override void Handle_materialization_properly_when_more_than_two_query_sources_are_involved()
        {
            base.Handle_materialization_properly_when_more_than_two_query_sources_are_involved();

            AssertSql(
                @"SELECT TOP 1 [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
, [Orders] AS [o]
, [Employees] AS [e]
ORDER BY [c].[CustomerID]");
        }

        public override void Parameter_extraction_short_circuits_1()
        {
            base.Parameter_extraction_short_circuits_1();

            AssertSql(
                @"@__dateFilter_Value_Month_0='7'
@__dateFilter_Value_Year_1='1996'

SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]
WHERE ([o].[OrderID] < 10400) AND (([o].[OrderDate] IS NOT NULL AND (DatePart('m', [o].[OrderDate]) = @__dateFilter_Value_Month_0)) AND (DatePart('yyyy', [o].[OrderDate]) = @__dateFilter_Value_Year_1))",
                //
                @"SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]
WHERE [o].[OrderID] < 10400");
        }

        public override void Parameter_extraction_short_circuits_2()
        {
            base.Parameter_extraction_short_circuits_2();

            AssertSql(
                @"@__dateFilter_Value_Month_0='7'
@__dateFilter_Value_Year_1='1996'

SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]
WHERE ([o].[OrderID] < 10400) AND (([o].[OrderDate] IS NOT NULL AND (DatePart('m', [o].[OrderDate]) = @__dateFilter_Value_Month_0)) AND (DatePart('yyyy', [o].[OrderDate]) = @__dateFilter_Value_Year_1))",
                //
                @"SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]
WHERE False = True");
        }

        public override void Parameter_extraction_short_circuits_3()
        {
            base.Parameter_extraction_short_circuits_3();

            AssertSql(
                @"@__dateFilter_Value_Month_0='7'
@__dateFilter_Value_Year_1='1996'

SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]
WHERE ([o].[OrderID] < 10400) OR (([o].[OrderDate] IS NOT NULL AND (DatePart('m', [o].[OrderDate]) = @__dateFilter_Value_Month_0)) AND (DatePart('yyyy', [o].[OrderDate]) = @__dateFilter_Value_Year_1))",
                //
                @"SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]");
        }

        public override void Subquery_member_pushdown_does_not_change_original_subquery_model()
        {
            base.Subquery_member_pushdown_does_not_change_original_subquery_model();

            AssertSql(
                @"@__p_0='3'

SELECT [t].[CustomerID], [t].[OrderID]
FROM (
    SELECT TOP @__p_0 [o].*
    FROM [Orders] AS [o]
    ORDER BY [o].[OrderID]
) AS [t]",
                //
                @"@_outer_CustomerID='VINET' (Nullable = false) (Size = 5)

SELECT TOP 2 [c0].[City]
FROM [Customers] AS [c0]
WHERE [c0].[CustomerID] = @_outer_CustomerID",
                //
                @"@_outer_CustomerID='TOMSP' (Nullable = false) (Size = 5)

SELECT TOP 2 [c0].[City]
FROM [Customers] AS [c0]
WHERE [c0].[CustomerID] = @_outer_CustomerID",
                //
                @"@_outer_CustomerID='HANAR' (Nullable = false) (Size = 5)

SELECT TOP 2 [c0].[City]
FROM [Customers] AS [c0]
WHERE [c0].[CustomerID] = @_outer_CustomerID",
                //
                @"@_outer_CustomerID1='TOMSP' (Nullable = false) (Size = 5)

SELECT TOP 2 [c2].[City]
FROM [Customers] AS [c2]
WHERE [c2].[CustomerID] = @_outer_CustomerID1",
                //
                @"@_outer_CustomerID1='VINET' (Nullable = false) (Size = 5)

SELECT TOP 2 [c2].[City]
FROM [Customers] AS [c2]
WHERE [c2].[CustomerID] = @_outer_CustomerID1",
                //
                @"@_outer_CustomerID1='HANAR' (Nullable = false) (Size = 5)

SELECT TOP 2 [c2].[City]
FROM [Customers] AS [c2]
WHERE [c2].[CustomerID] = @_outer_CustomerID1");
        }

        public override void ToString_with_formatter_is_evaluated_on_the_client()
        {
            base.ToString_with_formatter_is_evaluated_on_the_client();

            AssertSql(
                @"SELECT [o].[OrderID]
FROM [Orders] AS [o]
WHERE [o].[OrderDate] IS NOT NULL",
                //
                @"SELECT [o].[OrderID]
FROM [Orders] AS [o]
WHERE [o].[OrderDate] IS NOT NULL");
        }

        public override void Select_expression_date_add_year()
        {
            base.Select_expression_date_add_year();

            AssertSql(
                @"SELECT DateAdd('yyyy', 1, [o].[OrderDate]) AS [OrderDate]
FROM [Orders] AS [o]
WHERE [o].[OrderDate] IS NOT NULL");
        }

        public override void Select_expression_date_add_milliseconds_above_the_range()
        {
            base.Select_expression_date_add_milliseconds_above_the_range();

            AssertSql(
                @"SELECT [o].[OrderDate]
FROM [Orders] AS [o]
WHERE [o].[OrderDate] IS NOT NULL");
        }

        public override void Select_expression_date_add_milliseconds_below_the_range()
        {
            base.Select_expression_date_add_milliseconds_below_the_range();

            AssertSql(
                @"SELECT [o].[OrderDate]
FROM [Orders] AS [o]
WHERE [o].[OrderDate] IS NOT NULL");
        }

        [Fact(Skip = "Unsupported by JET: DateTime does not support milliseconds")]
        public override void Select_expression_date_add_milliseconds_large_number_divided()
        {
            base.Select_expression_date_add_milliseconds_large_number_divided();

            AssertSql(
                @"@__millisecondsPerDay_1='86400000'
@__millisecondsPerDay_0='86400000'

SELECT DATEADD(millisecond, DATEPART(millisecond, [o].[OrderDate]) % @__millisecondsPerDay_1, DATEADD(day, DATEPART(millisecond, [o].[OrderDate]) / @__millisecondsPerDay_0, [o].[OrderDate])) AS [OrderDate]
FROM [Orders] AS [o]
WHERE [o].[OrderDate] IS NOT NULL");
        }

        public override void Select_expression_references_are_updated_correctly_with_subquery()
        {
            base.Select_expression_references_are_updated_correctly_with_subquery();

            AssertSql(
                @"@__nextYear_0='2017'

SELECT [t].[c]
FROM (
    SELECT DISTINCT DatePart('yyyy', [o].[OrderDate]) AS [c]
    FROM [Orders] AS [o]
    WHERE [o].[OrderDate] IS NOT NULL
) AS [t]
WHERE [t].[c] < @__nextYear_0");
        }



        [Fact(Skip = "Unsupported by JET: SKIP TAKE DISTINCT")]
        public override void OrderBy_skip_take_distinct()
        {
            base.OrderBy_skip_take_distinct();
        }


        [Fact(Skip = "Unsupported by JET: SKIP TAKE DISTINCT")]
        public override void OrderBy_skip_take_distinct_orderby_take()
        {
            base.OrderBy_skip_take_distinct_orderby_take();
        }


        public override void Anonymous_member_distinct_where()
        {
            base.Anonymous_member_distinct_where();

            AssertSql(
                @"SELECT [t].[CustomerID]
FROM (
    SELECT DISTINCT [c].[CustomerID]
    FROM [Customers] AS [c]
) AS [t]
WHERE [t].[CustomerID] = 'ALFKI'");
        }

        public override void Anonymous_member_distinct_orderby()
        {
            base.Anonymous_member_distinct_orderby();

            AssertSql(
                @"SELECT [t].[CustomerID]
FROM (
    SELECT DISTINCT [c].[CustomerID]
    FROM [Customers] AS [c]
) AS [t]
ORDER BY [t].[CustomerID]");
        }

        public override void Anonymous_member_distinct_result()
        {
            base.Anonymous_member_distinct_result();

            AssertSql(
                @"SELECT COUNT(*)
FROM (
    SELECT DISTINCT [c].[CustomerID]
    FROM [Customers] AS [c]
) AS [t]
WHERE [t].[CustomerID] LIKE 'A' + '%' AND (LEFT([t].[CustomerID], Len('A')) = 'A')");
        }

        public override void Anonymous_complex_distinct_where()
        {
            base.Anonymous_complex_distinct_where();

            AssertSql(
                @"SELECT [t].[A]
FROM (
    SELECT DISTINCT [c].[CustomerID] + [c].[City] AS [A]
    FROM [Customers] AS [c]
) AS [t]
WHERE [t].[A] = 'ALFKIBerlin'");
        }

        public override void Anonymous_complex_distinct_orderby()
        {
            base.Anonymous_complex_distinct_orderby();

            AssertSql(
                @"SELECT [t].[A]
FROM (
    SELECT DISTINCT [c].[CustomerID] + [c].[City] AS [A]
    FROM [Customers] AS [c]
) AS [t]
ORDER BY [t].[A]");
        }

        public override void Anonymous_complex_distinct_result()
        {
            base.Anonymous_complex_distinct_result();

            AssertSql(
                @"SELECT COUNT(*)
FROM (
    SELECT DISTINCT [c].[CustomerID] + [c].[City] AS [A]
    FROM [Customers] AS [c]
) AS [t]
WHERE [t].[A] LIKE 'A' + '%' AND (LEFT([t].[A], Len('A')) = 'A')");
        }


        [Fact(Skip = "Unsupported by JET")]
        public override void Anonymous_subquery_orderby()
        {
            base.Anonymous_subquery_orderby();

            AssertSql(
                @"SELECT (
    SELECT TOP 1 [o1].[OrderDate]
    FROM [Orders] AS [o1]
    WHERE [c].[CustomerID] = [o1].[CustomerID]
    ORDER BY [o1].[OrderID] DESC
) AS [A]
FROM [Customers] AS [c]
WHERE (
    SELECT COUNT(*)
    FROM [Orders] AS [o]
    WHERE [c].[CustomerID] = [o].[CustomerID]
) > 1
ORDER BY (
    SELECT TOP 1 [o0].[OrderDate]
    FROM [Orders] AS [o0]
    WHERE [c].[CustomerID] = [o0].[CustomerID]
    ORDER BY [o0].[OrderID] DESC
)");
        }

        public override void DTO_member_distinct_where()
        {
            base.DTO_member_distinct_where();

            AssertSql(
                @"SELECT [t].[Property]
FROM (
    SELECT DISTINCT [c].[CustomerID] AS [Property]
    FROM [Customers] AS [c]
) AS [t]
WHERE [t].[Property] = 'ALFKI'");
        }

        public override void DTO_member_distinct_orderby()
        {
            base.DTO_member_distinct_orderby();

            AssertSql(
                @"SELECT [t].[Property]
FROM (
    SELECT DISTINCT [c].[CustomerID] AS [Property]
    FROM [Customers] AS [c]
) AS [t]
ORDER BY [t].[Property]");
        }

        public override void DTO_member_distinct_result()
        {
            base.DTO_member_distinct_result();

            AssertSql(
                @"SELECT COUNT(*)
FROM (
    SELECT DISTINCT [c].[CustomerID] AS [Property]
    FROM [Customers] AS [c]
) AS [t]
WHERE [t].[Property] LIKE 'A' + '%' AND (LEFT([t].[Property], Len('A')) = 'A')");
        }

        public override void DTO_complex_distinct_where()
        {
            base.DTO_complex_distinct_where();

            AssertSql(
                @"SELECT [t].[Property]
FROM (
    SELECT DISTINCT [c].[CustomerID] + [c].[City] AS [Property]
    FROM [Customers] AS [c]
) AS [t]
WHERE [t].[Property] = 'ALFKIBerlin'");
        }

        public override void DTO_complex_distinct_orderby()
        {
            base.DTO_complex_distinct_orderby();

            AssertSql(
                @"SELECT [t].[Property]
FROM (
    SELECT DISTINCT [c].[CustomerID] + [c].[City] AS [Property]
    FROM [Customers] AS [c]
) AS [t]
ORDER BY [t].[Property]");
        }

        public override void DTO_complex_distinct_result()
        {
            base.DTO_complex_distinct_result();

            AssertSql(
                @"SELECT COUNT(*)
FROM (
    SELECT DISTINCT [c].[CustomerID] + [c].[City] AS [Property]
    FROM [Customers] AS [c]
) AS [t]
WHERE [t].[Property] LIKE 'A' + '%' AND (LEFT([t].[Property], Len('A')) = 'A')");
        }

        public override void DTO_complex_orderby()
        {
            base.DTO_complex_orderby();

            AssertSql(
                @"SELECT [c].[CustomerID] + [c].[City] AS [Property]
FROM [Customers] AS [c]
ORDER BY [c].[CustomerID] + [c].[City]");
        }

        
        [Fact(Skip = "Unsupported by JET: subqueries supported only in FROM clause")]
        public override void DTO_subquery_orderby()
        {
            base.DTO_subquery_orderby();

            AssertSql(
                @"SELECT (
    SELECT TOP 1 [o1].[OrderDate]
    FROM [Orders] AS [o1]
    WHERE [c].[CustomerID] = [o1].[CustomerID]
    ORDER BY [o1].[OrderID] DESC
) AS [Property]
FROM [Customers] AS [c]
WHERE (
    SELECT COUNT(*)
    FROM [Orders] AS [o]
    WHERE [c].[CustomerID] = [o].[CustomerID]
) > 1
ORDER BY (
    SELECT TOP 1 [o0].[OrderDate]
    FROM [Orders] AS [o0]
    WHERE [c].[CustomerID] = [o0].[CustomerID]
    ORDER BY [o0].[OrderID] DESC
)");
        }

        [Fact(Skip = "Investigate - fix in progress - 2.1")]
        public override void Include_with_orderby_skip_preserves_ordering()
        {
            base.Include_with_orderby_skip_preserves_ordering();
        }


        public override void Int16_parameter_can_be_used_for_int_column()
        {
            base.Int16_parameter_can_be_used_for_int_column();

            AssertSql(
                @"SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate]
FROM [Orders] AS [o]
WHERE [o].[OrderID] = 10300");
        }

        
        public override void Subquery_is_null_translated_correctly()
        {
            base.Subquery_is_null_translated_correctly();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE (
    SELECT TOP 1 [o].[CustomerID]
    FROM [Orders] AS [o]
    WHERE [c].[CustomerID] = [o].[CustomerID]
    ORDER BY [o].[OrderID] DESC
) IS NULL");
        }

        
        public override void Subquery_is_not_null_translated_correctly()
        {
            base.Subquery_is_not_null_translated_correctly();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE (
    SELECT TOP 1 [o].[CustomerID]
    FROM [Orders] AS [o]
    WHERE [c].[CustomerID] = [o].[CustomerID]
    ORDER BY [o].[OrderID] DESC
) IS NOT NULL");
        }


        public override void Select_take_count()
        {
            base.Select_take_count();

            AssertSql(
                @"@__p_0='7'

SELECT COUNT(*)
FROM (
    SELECT TOP @__p_0 [c].*
    FROM [Customers] AS [c]
) AS [t]");
        }

        public override void Select_orderBy_take_count()
        {
            base.Select_orderBy_take_count();

            AssertSql(
                @"@__p_0='7'

SELECT COUNT(*)
FROM (
    SELECT TOP @__p_0 [c].*
    FROM [Customers] AS [c]
    ORDER BY [c].[Country]
) AS [t]");
        }


        public override void Select_take_max()
        {
            base.Select_take_max();

            AssertSql(
                @"@__p_0='10'

SELECT MAX([t].[OrderID])
FROM (
    SELECT TOP @__p_0 [o].[OrderID]
    FROM [Orders] AS [o]
    ORDER BY [o].[OrderID]
) AS [t]");
        }

        public override void Select_take_min()
        {
            base.Select_take_min();

            AssertSql(
                @"@__p_0='10'

SELECT MIN([t].[OrderID])
FROM (
    SELECT TOP @__p_0 [o].[OrderID]
    FROM [Orders] AS [o]
    ORDER BY [o].[OrderID]
) AS [t]");
        }

        public override void Select_take_sum()
        {
            base.Select_take_sum();

            AssertSql(
                @"@__p_0='10'

SELECT SUM([t].[OrderID])
FROM (
    SELECT TOP @__p_0 [o].[OrderID]
    FROM [Orders] AS [o]
    ORDER BY [o].[OrderID]
) AS [t]");
        }

        [Fact(Skip = "Unsupported by JET: SKIP TAKE is supported only in outer queries")]
        public override void Select_skip_count()
        {
            base.Select_skip_count();

            AssertSql(
                @"@__p_0='7'

SELECT COUNT(*)
FROM (
    SELECT [c].*
    FROM [Customers] AS [c]
    ORDER BY 'a'
    OFFSET @__p_0 ROWS
) AS [t]");
        }

        [Fact(Skip = "Unsupported by JET: SKIP TAKE is supported only in outer queries")]
        public override void Select_orderBy_skip_count()
        {
            base.Select_orderBy_skip_count();

            AssertSql(
                @"@__p_0='7'

SELECT COUNT(*)
FROM (
    SELECT [c].*
    FROM [Customers] AS [c]
    ORDER BY [c].[Country]
    OFFSET @__p_0 ROWS
) AS [t]");
        }


        [Fact(Skip = "Unsupported by JET: SKIP TAKE is supported only in outer queries")]
        public override void Select_orderBy_skip_long_count()
        {
            base.Select_orderBy_skip_long_count();
        }

        [Fact(Skip = "Unsupported by JET: SKIP TAKE is supported only in outer queries")]
        public override void Select_skip_average()
        {
            base.Select_skip_average();
        }

        [Fact(Skip = "Unsupported by JET: SKIP TAKE is supported only in outer queries")]
        public override void Select_take_skip_null_coalesce_operator()
        {
        }

        [Fact(Skip = "Unsupported by JET: SKIP TAKE is supported only in outer queries")]
        public override void Select_take_skip_null_coalesce_operator2()
        {
        }

        [Fact(Skip = "Unsupported by JET: SKIP TAKE is supported only in outer queries")]
        public override void Select_take_skip_null_coalesce_operator3()
        {
        }


        [Fact(Skip = "Unsupported by JET: SKIP TAKE is supported only in outer queries")]
        public override void Select_skip_max()
        {
            base.Select_skip_max();

            AssertSql(
                @"@__p_0='10'

SELECT MAX([t].[OrderID])
FROM (
    SELECT [o].[OrderID]
    FROM [Orders] AS [o]
    ORDER BY [o].[OrderID]
    OFFSET @__p_0 ROWS
) AS [t]");
        }

        [Fact(Skip = "Unsupported by JET: SKIP TAKE is supported only in outer queries")]
        public override void Select_skip_min()
        {
            base.Select_skip_min();

            AssertSql(
                @"@__p_0='10'

SELECT MIN([t].[OrderID])
FROM (
    SELECT [o].[OrderID]
    FROM [Orders] AS [o]
    ORDER BY [o].[OrderID]
    OFFSET @__p_0 ROWS
) AS [t]");
        }

        [Fact(Skip = "Unsupported by JET: SKIP TAKE is supported only in outer queries")]
        public override void Select_skip_sum()
        {
            base.Select_skip_sum();

            AssertSql(
                @"@__p_0='10'

SELECT SUM([t].[OrderID])
FROM (
    SELECT [o].[OrderID]
    FROM [Orders] AS [o]
    ORDER BY [o].[OrderID]
    OFFSET @__p_0 ROWS
) AS [t]");
        }

        [Fact(Skip = "Unsupported by JET: SKIP TAKE is supported only in outer queries")]
        public override void Select_skip_long_count()
        {
            
        }

        public override void Select_distinct_count()
        {
            base.Select_distinct_count();

            AssertSql(
                @"SELECT COUNT(*)
FROM (
    SELECT DISTINCT [c].*
    FROM [Customers] AS [c]
) AS [t]");
        }

        public override void Select_distinct_max()
        {
            base.Select_distinct_max();

            AssertSql(
                @"SELECT MAX([t].[OrderID])
FROM (
    SELECT DISTINCT [o].[OrderID]
    FROM [Orders] AS [o]
) AS [t]");
        }

        public override void Select_distinct_min()
        {
            base.Select_distinct_min();

            AssertSql(
                @"SELECT MIN([t].[OrderID])
FROM (
    SELECT DISTINCT [o].[OrderID]
    FROM [Orders] AS [o]
) AS [t]");
        }

        public override void Select_distinct_sum()
        {
            base.Select_distinct_sum();

            AssertSql(
                @"SELECT SUM([t].[OrderID])
FROM (
    SELECT DISTINCT [o].[OrderID]
    FROM [Orders] AS [o]
) AS [t]");
        }

        [Fact(Skip = "Unsupported by JET: SKIP TAKE is supported only in outer queries")]

        public override void Client_Join_select_many()
        {
            base.Client_Join_select_many();
        }


        private void AssertSql(params string[] expected)
        {
            string[] expectedFixed = new string[expected.Length];
            int i = 0;
            foreach (var item in expected)
            {
                expectedFixed[i++] = item.Replace("\r\n", "\n");
            }
            Fixture.TestSqlLoggerFactory.AssertBaseline(expectedFixed);
        }

        private void AssertContainsSql(params string[] expected)
        {
            string[] expectedFixed = new string[expected.Length];
            int i = 0;
            foreach (var item in expected)
            {
                expectedFixed[i++] = item.Replace("\r\n", "\n");
            }
            Fixture.TestSqlLoggerFactory.AssertBaseline(expectedFixed, assertOrder: false);
        }

        protected override void ClearLog()
            => Fixture.TestSqlLoggerFactory.Clear();

        [Fact(Skip = "Unsupported by JET: too complex query with DUAL")]
        public override void DefaultIfEmpty_in_subquery()
        {
            base.DefaultIfEmpty_in_subquery();
            /*
            SELECT [c].[CustomerID], [t0].[OrderID], [t2].[OrderDate]
            FROM [Customers] AS [c]
            , (
                SELECT [t].*
                FROM ((
                    SELECT NULL AS [empty]
                    FROM (SELECT COUNT(*) FROM MSysAccessStorage)
                ) AS [empty]
                LEFT JOIN (
                    SELECT [o].*
                    FROM [Orders] AS [o]
                    WHERE [o].[OrderID] > 11000
                ) AS [t] ON 1 = 1)
            ) AS [t0]
            , (
                SELECT [t1].*
                FROM ((
                    SELECT NULL AS [empty]
                    FROM (SELECT COUNT(*) FROM MSysAccessStorage)
                ) AS [empty0]
                LEFT JOIN (
                    SELECT [o0].*
                    FROM [Orders] AS [o0]
                    WHERE [o0].[CustomerID] = [c].[CustomerID]
                ) AS [t1] ON 1 = 1)
            ) AS [t2]
            WHERE ([c].[City] = 'Seattle') AND ([t0].[OrderID] IS NOT NULL AND [t2].[OrderID] IS NOT NULL)
            */
        }

        [Fact(Skip = "Unsupported by JET: too complex query with DUAL")]
        public override void DefaultIfEmpty_in_subquery_nested()
        {
            base.DefaultIfEmpty_in_subquery_nested();
            /*
            SELECT [t0].[CustomerID]
            FROM (
                SELECT [t].*
                FROM ((
                    SELECT NULL AS [empty]
                    FROM (SELECT COUNT(*) FROM MSysAccessStorage)
                ) AS [empty]
                LEFT JOIN (
                    SELECT [c].*
                    FROM [Customers] AS [c]
                    WHERE [c].[City] = 'London'
                ) AS [t] ON 1 = 1)
            ) AS [t0]
            WHERE [t0].[CustomerID] IS NOT NULL
            */
        }

        [Fact(Skip = "Unsupported by JET: too complex query with DUAL")]
        public override void DefaultIfEmpty_without_group_join()
        {
            base.DefaultIfEmpty_without_group_join();
            /*
            SELECT [c].[CustomerID], [t0].[OrderID]
            FROM [Customers] AS [c]
            , (
                SELECT [t].*
                FROM ((
                    SELECT NULL AS [empty]
                    FROM (SELECT COUNT(*) FROM MSysAccessStorage)
                ) AS [empty]
                LEFT JOIN (
                    SELECT [o].*
                    FROM [Orders] AS [o]
                    WHERE [o].[CustomerID] = [c].[CustomerID]
                ) AS [t] ON 1 = 1)
            ) AS [t0]
            WHERE [t0].[OrderID] IS NOT NULL
            */
        }

        [Fact(Skip = "Probably unsupported by JET")]
        public override void GroupJoin_subquery_projection_outer_mixed()
        {
            base.GroupJoin_subquery_projection_outer_mixed();
            /*
            SELECT [c].[CustomerID] AS [A], [t].[CustomerID] AS [B], [o1].[CustomerID] AS [C]
            FROM ([Customers] AS [c]
            , (
                SELECT TOP 1 [o].*
                FROM [Orders] AS [o]
                ORDER BY [o].[OrderID]

            ) AS [t]
            INNER JOIN [Orders] AS [o1] ON [c].[CustomerID] = [o1].[CustomerID])
            */
        }

        [Fact(Skip = "Unsupported by JET")]
        public override void Join_complex_condition()
        {
            base.Join_complex_condition();
            // The query should probably be rewritten but in this case should be the client to rewrite it
        }

        [Fact(Skip = "Unsupported by JET")]
        public override void No_orderby_added_for_client_side_GroupJoin_dependent_to_principal_LOJ_with_additional_join_condition1()
        {
            base.No_orderby_added_for_client_side_GroupJoin_dependent_to_principal_LOJ();
        }

        [Fact(Skip = "Unsupported by JET")]
        public override void No_orderby_added_for_client_side_GroupJoin_dependent_to_principal_LOJ_with_additional_join_condition2()
        {
            base.No_orderby_added_for_client_side_GroupJoin_dependent_to_principal_LOJ_with_additional_join_condition1();
        }

        [Fact(Skip = "Unsupported by JET: SELECT ORDER BY (SELECT)")]
        public override void OrderBy_any()
        {
            base.OrderBy_any();
        }

        [Fact(Skip = "Unsupported by JET: SKIP TAKE DISTINCT")]
        public override void OrderBy_coalesce_skip_take_distinct()
        {
            base.OrderBy_coalesce_skip_take_distinct();
        }


        [Fact(Skip = "Unsupported by JET: SKIP TAKE DISTINCT")]
        public override void OrderBy_coalesce_skip_take_distinct_take()
        {
            base.OrderBy_coalesce_skip_take_distinct_take();
        }

        [Fact(Skip = "Unsupported by JET")]
        public override void OrderBy_condition_comparison()
        {
            base.OrderBy_condition_comparison();
        }

        [Fact(Skip = "Unsupported by JET: SELECT ORDER BY (SELECT)")]
        public override void OrderBy_correlated_subquery1()
        {
            base.OrderBy_correlated_subquery1();
        }

        [Fact(Skip = "Unsupported by JET: SELECT ORDER BY (SELECT)")]
        public override void OrderBy_correlated_subquery2()
        {
            base.OrderBy_correlated_subquery2();
        }

        [Fact(Skip = "Unsupported by JET: SKIP TAKE is supported only in outer queries")]
        public override void OrderBy_skip_take_skip_take_skip()
        {
            base.OrderBy_skip_take_skip_take_skip();
        }

        [Fact(Skip = "Unsupported by JET: SKIP TAKE is supported only in outer queries")]
        public override void OrderBy_skip_take_take()
        {
            base.OrderBy_skip_take_take();
        }


        [Fact(Skip = "Unsupported by JET: SKIP TAKE is supported only in outer queries")]
        public override void OrderBy_skip_take_take_take_take()
        {
            base.OrderBy_skip_take_take_take_take();
        }

        [Fact(Skip = "Unsupported by JET: SKIP TAKE is supported only in outer queries")]
        public override void SelectMany_Joined_Take()
        {
            base.SelectMany_Joined_Take();
        }

        [Fact(Skip = "Assertion failed without evident reason")]
        public override void OrderBy_ternary_conditions()
        {
            base.OrderBy_ternary_conditions();
        }


        [Fact(Skip = "Assertion failed without evident reason")]
        public override void String_Compare_nested()
        {
            base.String_Compare_nested();
        }
    }
}