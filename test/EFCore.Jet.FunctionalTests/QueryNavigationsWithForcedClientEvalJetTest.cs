using System;
using Microsoft.EntityFrameworkCore.Query;
using Xunit;
using Xunit.Abstractions;

namespace EntityFramework.Jet.FunctionalTests
{
    public class QueryNavigationsWithForcedClientEvalJetTest : QueryNavigationsTestBase<NorthwindQueryWithForcedClientEvalJetFixture>
    {
        public QueryNavigationsWithForcedClientEvalJetTest(
            // ReSharper disable once UnusedParameter.Local
            NorthwindQueryWithForcedClientEvalJetFixture fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            //TestSqlLoggerFactory.CaptureOutput(testOutputHelper);
        }

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