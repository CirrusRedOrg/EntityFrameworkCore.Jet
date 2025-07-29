// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query
{
    public class FromSqlSprocQueryJetTest : FromSqlSprocQueryTestBase<NorthwindQueryJetFixture<NoopModelCustomizer>>
    {
        public FromSqlSprocQueryJetTest(NorthwindQueryJetFixture<NoopModelCustomizer> fixture)
            : base(fixture)
        {
            fixture.TestSqlLoggerFactory.Clear();
        }

        public override async Task From_sql_queryable_stored_procedure(bool async)
        {
            await base.From_sql_queryable_stored_procedure(async);

            AssertSql($@"EXEC `Ten Most Expensive Products`");
        }

        public override async Task From_sql_queryable_stored_procedure_with_tag(bool async)
        {
            await base.From_sql_queryable_stored_procedure_with_tag(async);

            AssertSql(
                """
-- Stored Procedure

EXEC `Ten Most Expensive Products`
""");
        }

        public override async Task From_sql_queryable_stored_procedure_with_caller_info_tag(bool async)
        {
            await base.From_sql_queryable_stored_procedure_with_caller_info_tag(async);

            AssertSql(
                """
-- File: SampleFileName:13

EXEC `Ten Most Expensive Products`
""");
        }

        public override async Task From_sql_queryable_stored_procedure_with_caller_info_tag_and_other_tags(bool async)
        {
            await base.From_sql_queryable_stored_procedure_with_caller_info_tag_and_other_tags(async);

            AssertSql(
                """
-- Before
-- File: SampleFileName:13
-- After

EXEC `Ten Most Expensive Products`
""");
        }

        public override async Task From_sql_queryable_stored_procedure_with_parameter(bool async)
        {
            await base.From_sql_queryable_stored_procedure_with_parameter(async);

            AssertSql(
                $"""
                    p0='ALFKI' (Size = 255)
                    
                    EXEC `CustOrderHist` CustomerID = {AssertSqlHelper.Parameter("@p0")}
                    """);
        }

        public override async Task From_sql_queryable_stored_procedure_re_projection_on_client(bool async)
        {
            await base.From_sql_queryable_stored_procedure_re_projection_on_client(async);

            AssertSql($@"EXEC `Ten Most Expensive Products`");
        }

        public override async Task From_sql_queryable_stored_procedure_composed_on_client(bool async)
        {
            await base.From_sql_queryable_stored_procedure_composed_on_client(async);

            AssertSql($@"EXEC `Ten Most Expensive Products`");
        }

        public override async Task From_sql_queryable_stored_procedure_with_parameter_composed_on_client(bool async)
        {
            await base.From_sql_queryable_stored_procedure_with_parameter_composed_on_client(async);

            AssertSql(
                $"""
                    p0='ALFKI' (Size = 255)
                    
                    EXEC `CustOrderHist` CustomerID = {AssertSqlHelper.Parameter("@p0")}
                    """);
        }

        public override async Task From_sql_queryable_stored_procedure_take_on_client(bool async)
        {
            await base.From_sql_queryable_stored_procedure_take_on_client(async);

            AssertSql($@"EXEC `Ten Most Expensive Products`");
        }

        public override async Task From_sql_queryable_stored_procedure_min_on_client(bool async)
        {
            await base.From_sql_queryable_stored_procedure_min_on_client(async);

            AssertSql($@"EXEC `Ten Most Expensive Products`");
        }

        private void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);

        protected override string TenMostExpensiveProductsSproc => "EXEC `Ten Most Expensive Products`";

        protected override string CustomerOrderHistorySproc => "EXEC `CustOrderHist` CustomerID = {0}";
    }
}
