// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Data.Common;
using System.Data.Odbc;
using System.Threading.Tasks;
using EntityFrameworkCore.Jet.Data;
using System.Data.OleDb;
using EntityFrameworkCore.Jet.FunctionalTests.TestUtilities;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Xunit.Abstractions;

namespace EntityFrameworkCore.Jet.FunctionalTests.Query
{
    public class SqlExecutorJetTest : SqlExecutorTestBase<NorthwindQueryJetFixture<NoopModelCustomizer>>
    {
        // ReSharper disable once UnusedParameter.Local
        public SqlExecutorJetTest(NorthwindQueryJetFixture<NoopModelCustomizer> fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            Fixture.TestSqlLoggerFactory.Clear();
            Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
        }

        public override async Task Executes_stored_procedure(bool async)
        {
            await base.Executes_stored_procedure(async);

            AssertSql($@"`Ten Most Expensive Products`");
        }

        public override async Task Executes_stored_procedure_with_parameter(bool async)
        {
            await base.Executes_stored_procedure_with_parameter(async);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@CustomerID='ALFKI' (Nullable = false) (Size = 5)")}

`CustOrderHist` @CustomerID");
        }

        public override async Task Executes_stored_procedure_with_generated_parameter(bool async)
        {
            await base.Executes_stored_procedure_with_generated_parameter(async);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@p0='ALFKI' (Size = 4000)")}

`CustOrderHist` @CustomerID = {AssertSqlHelper.Parameter("@p0")}");
        }

        public override async Task Query_with_parameters(bool async)
        {
            await base.Query_with_parameters(async);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@p0='London' (Size = 255)")}
{AssertSqlHelper.Declaration("@p1='Sales Representative' (Size = 255)")}

SELECT COUNT(*) FROM ""Customers"" WHERE ""City"" = {AssertSqlHelper.Parameter("@p0")} AND ""ContactTitle"" = {AssertSqlHelper.Parameter("@p1")}");
        }

        public override async Task Query_with_dbParameter_with_name(bool async)
        {
            await base.Query_with_dbParameter_with_name(async);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@city='London' (Nullable = false) (Size = 6)")}

SELECT COUNT(*) FROM ""Customers"" WHERE ""City"" = {AssertSqlHelper.Parameter("@city")}");
        }

        public override async Task Query_with_positional_dbParameter_with_name(bool async)
        {
            await base.Query_with_positional_dbParameter_with_name(async);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@city='London' (Nullable = false) (Size = 6)")}

SELECT COUNT(*) FROM ""Customers"" WHERE ""City"" = {AssertSqlHelper.Parameter("@city")}");
        }

        public override async Task Query_with_positional_dbParameter_without_name(bool async)
        {
            await base.Query_with_positional_dbParameter_without_name(async);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@p0='London' (Nullable = false) (Size = 6)")}

SELECT COUNT(*) FROM ""Customers"" WHERE ""City"" = {AssertSqlHelper.Parameter("@p0")}");
        }

        public override async Task Query_with_dbParameters_mixed(bool async)
        {
            await base.Query_with_dbParameters_mixed(async);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@p0='London' (Size = 255)")}
{AssertSqlHelper.Declaration("@contactTitle='Sales Representative' (Nullable = false) (Size = 20)")}

SELECT COUNT(*) FROM ""Customers"" WHERE ""City"" = {AssertSqlHelper.Parameter("@p0")} AND ""ContactTitle"" = {AssertSqlHelper.Parameter("@contactTitle")}",
                //
                $@"{AssertSqlHelper.Declaration("@city='London' (Nullable = false) (Size = 6)")}
{AssertSqlHelper.Declaration("@p0='Sales Representative' (Size = 255)")}

SELECT COUNT(*) FROM ""Customers"" WHERE ""City"" = {AssertSqlHelper.Parameter("@city")} AND ""ContactTitle"" = {AssertSqlHelper.Parameter("@p0")}");
        }

        public override async Task Query_with_parameters_interpolated(bool async)
        {
            await base.Query_with_parameters_interpolated(async);

            AssertSql(
                $@"{AssertSqlHelper.Declaration("@p0='London' (Size = 255)")}
{AssertSqlHelper.Declaration("@p1='Sales Representative' (Size = 255)")}

SELECT COUNT(*) FROM ""Customers"" WHERE ""City"" = {AssertSqlHelper.Parameter("@p0")} AND ""ContactTitle"" = {AssertSqlHelper.Parameter("@p1")}");
        }

        protected override DbParameter CreateDbParameter(string name, object value)
            => new OdbcParameter { ParameterName = name, Value = value };

        protected override string TenMostExpensiveProductsSproc => "`Ten Most Expensive Products`";
        protected override string CustomerOrderHistorySproc => "`CustOrderHist` @CustomerID";
        protected override string CustomerOrderHistoryWithGeneratedParameterSproc => "`CustOrderHist` @CustomerID = {0}";

        private void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);
    }
}
