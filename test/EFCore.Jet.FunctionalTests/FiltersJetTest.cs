using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace EntityFramework.Jet.FunctionalTests
{
    public class FiltersJetTest : FiltersTestBase<NorthwindQueryJetFixture>
    {
        public FiltersJetTest(NorthwindQueryJetFixture fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            fixture.TestSqlLoggerFactory.Clear();
            //fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
        }

        public override void Count_query()
        {
            base.Count_query();

            AssertSql(
                @"@__TenantPrefix_0='B' (Nullable = false) (Size = 1)

SELECT COUNT(*)
FROM [Customers] AS [c]
WHERE ([c].[CompanyName] LIKE @__TenantPrefix_0 + N'%' AND (CHARINDEX(@__TenantPrefix_0, [c].[CompanyName]) = 1)) OR (@__TenantPrefix_0 = N'')");
        }

        public override void Client_eval()
        {
            base.Client_eval();

            AssertSql(
                @"SELECT [p].[ProductID], [p].[Discontinued], [p].[ProductName], [p].[UnitPrice], [p].[UnitsInStock]
FROM [Products] AS [p]");
        }

        public override void Materialized_query()
        {
            base.Materialized_query();

            AssertSql(
                @"@__TenantPrefix_0='B' (Nullable = false) (Size = 1)

SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE ([c].[CompanyName] LIKE @__TenantPrefix_0 + N'%' AND (CHARINDEX(@__TenantPrefix_0, [c].[CompanyName]) = 1)) OR (@__TenantPrefix_0 = N'')");
        }

        public override void Materialized_query_parameter()
        {
            base.Materialized_query_parameter();

            AssertSql(
                @"@__TenantPrefix_0='F' (Nullable = false) (Size = 1)

SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE ([c].[CompanyName] LIKE @__TenantPrefix_0 + N'%' AND (CHARINDEX(@__TenantPrefix_0, [c].[CompanyName]) = 1)) OR (@__TenantPrefix_0 = N'')");

        }

        public override void Materialized_query_parameter_new_context()
        {
            base.Materialized_query_parameter_new_context();

            AssertSql(
                @"@__TenantPrefix_0='B' (Nullable = false) (Size = 1)

SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE ([c].[CompanyName] LIKE @__TenantPrefix_0 + N'%' AND (CHARINDEX(@__TenantPrefix_0, [c].[CompanyName]) = 1)) OR (@__TenantPrefix_0 = N'')",
                //
                @"@__TenantPrefix_0='T' (Nullable = false) (Size = 1)

SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE ([c].[CompanyName] LIKE @__TenantPrefix_0 + N'%' AND (CHARINDEX(@__TenantPrefix_0, [c].[CompanyName]) = 1)) OR (@__TenantPrefix_0 = N'')");
        }

        public override void Projection_query_parameter()
        {
            base.Projection_query_parameter();

            AssertSql(
                @"@__TenantPrefix_0='F' (Nullable = false) (Size = 1)

SELECT [c].[CustomerID]
FROM [Customers] AS [c]
WHERE ([c].[CompanyName] LIKE @__TenantPrefix_0 + N'%' AND (CHARINDEX(@__TenantPrefix_0, [c].[CompanyName]) = 1)) OR (@__TenantPrefix_0 = N'')");
        }

        public override void Projection_query()
        {
            base.Projection_query();

            AssertSql(
                @"@__TenantPrefix_0='B' (Nullable = false) (Size = 1)

SELECT [c].[CustomerID]
FROM [Customers] AS [c]
WHERE ([c].[CompanyName] LIKE @__TenantPrefix_0 + N'%' AND (CHARINDEX(@__TenantPrefix_0, [c].[CompanyName]) = 1)) OR (@__TenantPrefix_0 = N'')");
        }

        public override void Include_query()
        {
            base.Include_query();

            AssertSql(
                @"@__TenantPrefix_0='B' (Nullable = false) (Size = 1)

SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
WHERE ([c].[CompanyName] LIKE @__TenantPrefix_0 + N'%' AND (CHARINDEX(@__TenantPrefix_0, [c].[CompanyName]) = 1)) OR (@__TenantPrefix_0 = N'')
ORDER BY [c].[CustomerID]",
                //
                @"@__TenantPrefix_1='B' (Nullable = false) (Size = 1)

SELECT [c.Orders].[OrderID], [c.Orders].[CustomerID], [c.Orders].[EmployeeID], [c.Orders].[OrderDate]
FROM [Orders] AS [c.Orders]
INNER JOIN (
    SELECT [c0].[CustomerID]
    FROM [Customers] AS [c0]
    WHERE ([c0].[CompanyName] LIKE @__TenantPrefix_1 + N'%' AND (CHARINDEX(@__TenantPrefix_1, [c0].[CompanyName]) = 1)) OR (@__TenantPrefix_1 = N'')
) AS [t] ON [c.Orders].[CustomerID] = [t].[CustomerID]
ORDER BY [t].[CustomerID]");
        }

        public override void Include_query_opt_out()
        {
            base.Include_query_opt_out();

            AssertSql(
                @"SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
ORDER BY [c].[CustomerID]",
                //
                @"SELECT [c.Orders].[OrderID], [c.Orders].[CustomerID], [c.Orders].[EmployeeID], [c.Orders].[OrderDate]
FROM [Orders] AS [c.Orders]
INNER JOIN (
    SELECT [c0].[CustomerID]
    FROM [Customers] AS [c0]
) AS [t] ON [c.Orders].[CustomerID] = [t].[CustomerID]
ORDER BY [t].[CustomerID]");
        }

        public override void Included_many_to_one_query()
        {
            base.Included_many_to_one_query();

            AssertSql(
                @"@__TenantPrefix_0='B' (Nullable = false) (Size = 1)

SELECT [o].[OrderID], [o].[CustomerID], [o].[EmployeeID], [o].[OrderDate], [t].[CustomerID], [t].[Address], [t].[City], [t].[CompanyName], [t].[ContactName], [t].[ContactTitle], [t].[Country], [t].[Fax], [t].[Phone], [t].[PostalCode], [t].[Region]
FROM [Orders] AS [o]
LEFT JOIN (
    SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
    FROM [Customers] AS [c]
    WHERE ([c].[CompanyName] LIKE @__TenantPrefix_0 + N'%' AND (CHARINDEX(@__TenantPrefix_0, [c].[CompanyName]) = 1)) OR (@__TenantPrefix_0 = N'')
) AS [t] ON [o].[CustomerID] = [t].[CustomerID]");
        }

        public override void Included_one_to_many_query_with_client_eval()
        {
            base.Included_one_to_many_query_with_client_eval();

            AssertSql(
                @"SELECT [p].[ProductID], [p].[Discontinued], [p].[ProductName], [p].[UnitPrice], [p].[UnitsInStock]
FROM [Products] AS [p]
ORDER BY [p].[ProductID]",
                //
                @"SELECT [p1].[ProductID], [p1].[Discontinued], [p1].[ProductName], [p1].[UnitPrice], [p1].[UnitsInStock]
FROM [Products] AS [p1]",
                //
                @"@___quantity_0='50'

SELECT [o].[OrderID], [o].[ProductID], [o].[Discount], [o].[Quantity], [o].[UnitPrice]
FROM [Order Details] AS [o]
WHERE [o].[Quantity] > @___quantity_0");
        }

        public override void Navs_query()
        {
            base.Navs_query();

            AssertSql(
                @"@___quantity_1='50'
@__TenantPrefix_0='B' (Nullable = false) (Size = 1)

SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM [Customers] AS [c]
INNER JOIN [Orders] AS [c.Orders] ON [c].[CustomerID] = [c.Orders].[CustomerID]
INNER JOIN (
    SELECT [o].*
    FROM [Order Details] AS [o]
    WHERE [o].[Quantity] > @___quantity_1
) AS [t] ON [c.Orders].[OrderID] = [t].[OrderID]
WHERE (([c].[CompanyName] LIKE @__TenantPrefix_0 + N'%' AND (CHARINDEX(@__TenantPrefix_0, [c].[CompanyName]) = 1)) OR (@__TenantPrefix_0 = N'')) AND ([t].[Discount] < 10)");
        }

        [ConditionalFact]
        public void FromSql_is_composed()
        {
            using (var context = Fixture.CreateContext())
            {
                var results = context.Customers.FromSql("select * from Customers").ToList();

                Assert.Equal(7, results.Count);
            }

            AssertSql(
                @"@__TenantPrefix_0='B' (Nullable = false) (Size = 1)

SELECT [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
FROM (
    select * from Customers
) AS [c]
WHERE ([c].[CompanyName] LIKE @__TenantPrefix_0 + N'%' AND (CHARINDEX(@__TenantPrefix_0, [c].[CompanyName]) = 1)) OR (@__TenantPrefix_0 = N'')");
        }


        private void AssertSql(params string[] expected)
        {
            string[] expectedFixed = new string[expected.Length];
            int i = 0;
            foreach (var item in expected)
            {
                if (AssertSqlHelper.IgnoreStatement(item))
                    return;
                expectedFixed[i++] = item.Replace("\r\n", "\n");
            }
            Fixture.TestSqlLoggerFactory.AssertBaseline(expectedFixed);
        }

        private void AssertContains(params string[] expected)
        {
            string[] expectedFixed = new string[expected.Length];
            int i = 0;
            foreach (var item in expected)
            {
                expectedFixed[i++] = item.Replace("\r\n", "\n");
            }
            Fixture.TestSqlLoggerFactory.AssertBaseline(expectedFixed, assertOrder: false);
        }
    }
}