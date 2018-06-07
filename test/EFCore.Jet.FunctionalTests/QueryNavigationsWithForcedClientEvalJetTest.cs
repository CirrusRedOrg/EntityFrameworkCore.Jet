using System;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit;
using Xunit.Abstractions;

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



        [Fact(Skip = "Unsupported by JET: subqueries supported only in FROM clause")]
        public override void Collection_orderby_nav_prop_count() { }
        [Fact(Skip = "Unsupported by JET: SKIP TAKE is supported only in outer queries")]
        public override void Collection_select_nav_prop_first_or_default_then_nav_prop_nested_with_orderby() { }
        [Fact(Skip = "Unsupported by JET: , and OTHER JOIN")]
        public override void GroupJoin_with_complex_subquery_and_LOJ_gets_flattened() { }
        [Fact(Skip = "Unsupported by JET: , and OTHER JOIN")]
        public override void GroupJoin_with_complex_subquery_and_LOJ_gets_flattened2() { }

        [Fact(Skip = "Unsupported by JET: subqueries supported only in FROM clause")]
        public override void Navigation_in_subquery_referencing_outer_query_with_client_side_result_operator_and_count() { }
        [Fact(Skip = "Unsupported by JET: , and OTHER JOIN")]
        public override void Select_Where_Navigation_Scalar_Equals_Navigation_Scalar() { }
        [Fact(Skip = "Unsupported by JET: , and OTHER JOIN")]
        public override void Select_Where_Navigation_Scalar_Equals_Navigation_Scalar_Projected() { }
        public override void Select_collection_navigation_multi_part() { }




        [Fact(Skip = "Investigate why this fails with forced client eval - 2.1")]
        public override void Where_subquery_on_navigation()
        {
            base.Where_subquery_on_navigation();
        }

        [Fact(Skip = "Investigate why this fails with forced client eval - 2.1")]
        public override void Where_subquery_on_navigation2()
        {
            base.Where_subquery_on_navigation2();
        }

        protected override void ClearLog() => Fixture.TestSqlLoggerFactory.Clear();

        private const string FileLineEnding = @"
";

        private string Sql => Fixture.TestSqlLoggerFactory.Sql.Replace(Environment.NewLine, FileLineEnding);
    }
}