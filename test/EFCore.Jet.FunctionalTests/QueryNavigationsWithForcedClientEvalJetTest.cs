using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;

#pragma warning disable xUnit1003 // Theory methods must have test data

namespace EntityFramework.Jet.FunctionalTests
{
    public class QueryNavigationsWithForcedClientEvalJetTest : QueryNavigationsTestBase<NorthwindQueryWithForcedClientEvalJetFixture<NoopModelCustomizer>>
    {
        public QueryNavigationsWithForcedClientEvalJetTest(
            // ReSharper disable once UnusedParameter.Local
            NorthwindQueryWithForcedClientEvalJetFixture<NoopModelCustomizer> fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            //TestSqlLoggerFactory.CaptureOutput(testOutputHelper);
        }



        [Theory(Skip = "Unsupported by JET: subqueries supported only in FROM clause")]
        [InlineData(true)]
        public override async Task Collection_orderby_nav_prop_count(bool isAsync) { }
        [Theory(Skip = "Unsupported by JET: SKIP TAKE is supported only in outer queries")]
        public override async Task Collection_select_nav_prop_first_or_default_then_nav_prop_nested_with_orderby(bool isAsync) { }
        [Theory(Skip = "Unsupported by JET: , and OTHER JOIN")]
        public override async Task GroupJoin_with_complex_subquery_and_LOJ_gets_flattened(bool isAsync) { }
        [Theory(Skip = "Unsupported by JET: , and OTHER JOIN")]
        public override async Task GroupJoin_with_complex_subquery_and_LOJ_gets_flattened2(bool isAsync) { }

        [Fact(Skip = "Unsupported by JET: subqueries supported only in FROM clause")]
        public override void Navigation_in_subquery_referencing_outer_query_with_client_side_result_operator_and_count() { }
        [Theory(Skip = "Unsupported by JET: , and OTHER JOIN")]
        public override async Task Select_Where_Navigation_Scalar_Equals_Navigation_Scalar(bool isAsync) { }
        [Theory(Skip = "Unsupported by JET: , and OTHER JOIN")]
        public override async Task Select_Where_Navigation_Scalar_Equals_Navigation_Scalar_Projected(bool isAsync) { }
        public override async Task Select_collection_navigation_multi_part(bool isAsync) { }




        [Theory(Skip = "Investigate why this fails with forced client eval - 2.1")]
        public override async Task Where_subquery_on_navigation(bool isAsync)
        {
            await base.Where_subquery_on_navigation(isAsync);
        }

        [Theory(Skip = "Investigate why this fails with forced client eval - 2.1")]
        public override async Task Where_subquery_on_navigation2(bool isAsync)
        {
            await base.Where_subquery_on_navigation2(isAsync);
        }

        protected override void ClearLog() => Fixture.TestSqlLoggerFactory.Clear();

        private const string FileLineEnding = @"
";

        private string Sql => Fixture.TestSqlLoggerFactory.Sql.Replace(Environment.NewLine, FileLineEnding);
    }
}