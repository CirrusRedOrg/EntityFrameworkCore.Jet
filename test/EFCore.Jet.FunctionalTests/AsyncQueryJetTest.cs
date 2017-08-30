using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestModels;
using Xunit;

#pragma warning disable 1998

namespace EntityFramework.Jet.FunctionalTests
{
    public class AsyncQueryJetTest : AsyncQueryTestBase<NorthwindQueryJetFixture>
    {
        [Fact(Skip = "Investigate - https://github.com/aspnet/EntityFramework/issues/9378")]
        public override Task Where_subquery_on_collection()
        {
            return base.Where_subquery_on_collection();
        }



        [Fact(Skip = "Investigate 2.1 - https://github.com/aspnet/EntityFramework/issues/9369")]
        public override async Task String_Contains_Literal()
        {
            await AssertQuery<ChangedChangingMonsterContext.Customer>(
                cs => cs.Where(c => c.Name.Contains("M")), // case-insensitive
                cs => cs.Where(c => c.Name.Contains("M")
                                     || c.Name.Contains("m")), // case-sensitive
                entryCount: 34);
        }

        [Fact(Skip = "Investigate 2.1 - https://github.com/aspnet/EntityFramework/issues/9369")]
        public override async Task String_Contains_MethodCall()
        {
            await AssertQuery<ChangedChangingMonsterContext.Customer>(
                cs => cs.Where(c => c.Name.Contains(LocalMethod1())), // case-insensitive
                cs => cs.Where(c => c.Name.Contains(LocalMethod1().ToLower()) || c.Name.Contains(LocalMethod1().ToUpper())), // case-sensitive
                entryCount: 34);
        }

        [Fact]
        public async Task Single_Predicate_Cancellation()
        {
            await Assert.ThrowsAsync<TaskCanceledException>(
                async () =>
                    await Single_Predicate_Cancellation_test(Fixture.TestSqlLoggerFactory.CancelQuery()));
        }

        public AsyncQueryJetTest(NorthwindQueryJetFixture fixture)
            : base(fixture)
        {
        }


        [Fact(Skip = "Unsupported by JET")]
        public override Task All_top_level()
        {
            return base.All_top_level();
        }

        [Fact(Skip = "Unsupported by JET")]
        public override Task All_top_level_subquery()
        {
            return base.All_top_level_subquery();
        }

        [Fact(Skip = "Unsupported by JET")]
        public override Task Default_if_empty_top_level()
        {
            return base.Default_if_empty_top_level();
        }

        [Fact(Skip = "Unsupported by JET")]
        public override Task Default_if_empty_top_level_positive()
        {
            return base.Default_if_empty_top_level_positive();
        }

        [Fact(Skip = "Unsupported by JET")]
        public override Task Default_if_empty_top_level_projection()
        {
            return base.Default_if_empty_top_level_projection();
        }

        [Fact(Skip = "Unsupported by JET")]
        public override Task Skip_CountAsync()
        {
            return base.Skip_CountAsync();
        }

        [Fact(Skip = "Unsupported by JET")]
        public override Task Skip_LongCountAsync()
        {
            return base.Skip_LongCountAsync();
        }

        [Fact(Skip = "Unsupported by JET")]
        public override Task SelectMany_Joined_DefaultIfEmpty()
        {
            return base.SelectMany_Joined_DefaultIfEmpty();
        }
        [Fact(Skip = "Unsupported by JET")]
        public override Task SelectMany_Joined_DefaultIfEmpty2()
        {
            return base.SelectMany_Joined_DefaultIfEmpty();
        }

        [Fact(Skip = "Unsupported by JET")]
        public override Task Distinct_Skip()
        {
            return base.Distinct_Skip();
        }

        [Fact(Skip = "Unsupported by JET")]
        public override Task Multiple_joins_Where_Order_Any()
        {
            return base.Multiple_joins_Where_Order_Any();
        }

        [Fact(Skip = "Unsupported by JET: SELECT TOP 2 (SELECT TOP 1) returns 2 records")]
        public override Task Take_with_single()
        {
            return base.Take_with_single();
        }

        [Fact(Skip = "Unsupported by JET: SELECT ORDER BY (SELECT)")]
        public override Task OrderBy_correlated_subquery_lol()
        {
            return base.OrderBy_correlated_subquery_lol();
        }

        [Fact(Skip = "Unsupported by JET: SKIP TAKE is supported only in outer queries")]
        public override Task OrderBy_Skip_CountAsync()
        {
            return base.OrderBy_Skip_CountAsync();

            /*
            SELECT COUNT(*)
            FROM (
                SELECT [c].*
                FROM [Customers] AS [c]
                ORDER BY [c].[Country]
                 SKIP @__p_0
            ) AS [t]
            */
        }

        [Fact(Skip = "Unsupported by JET: SKIP TAKE is supported only in outer queries")]
        public override Task OrderBy_Skip_LongCountAsync()
        {
            return base.OrderBy_Skip_LongCountAsync();
            /*
            SELECT COUNT(*)
            FROM (
                SELECT [c].*
                FROM [Customers] AS [c]
                ORDER BY [c].[Country]
                 SKIP @__p_0
            ) AS [t]
            */
        }

        [Fact(Skip = "Unsupported by JET: SKIP TAKE DISTINCT")]
        public override Task Skip_Take_Distinct()
        {
            return base.Skip_Take_Distinct();
            /*
            SELECT DISTINCT [t].*
            FROM (
                SELECT TOP @__p_1+@__p_0 [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
                FROM [Customers] AS [c]
                ORDER BY [c].[ContactName]
                 SKIP @__p_0
            ) AS [t]
            */
        }

        [Fact(Skip = "Unsupported by JET: SKIP TAKE DISTINCT")]
        public override Task Skip_Distinct()
        {
            return base.Skip_Distinct();
        }

        [Fact(Skip = "Unsupported by JET: SKIP TAKE DISTINCT")]
        public override Task Take_Skip_Distinct()
        {
            return base.Take_Skip_Distinct();
            /*
            SELECT DISTINCT [t0].*
            FROM (
                SELECT [t].*
                FROM (
                    SELECT TOP @__p_0 [c].[CustomerID], [c].[Address], [c].[City], [c].[CompanyName], [c].[ContactName], [c].[ContactTitle], [c].[Country], [c].[Fax], [c].[Phone], [c].[PostalCode], [c].[Region]
                    FROM [Customers] AS [c]
                    ORDER BY [c].[ContactName]
                ) AS [t]
                ORDER BY [t].[ContactName]
                 SKIP @__p_1
            ) AS [t0]
            */
        }
    }
}
