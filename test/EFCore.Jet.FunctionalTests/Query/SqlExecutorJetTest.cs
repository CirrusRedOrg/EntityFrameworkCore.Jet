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
using Microsoft.EntityFrameworkCore;
using Xunit;
using Microsoft.EntityFrameworkCore.TestModels.ConcurrencyModel;
#nullable disable
namespace EntityFrameworkCore.Jet.FunctionalTests.Query
{
    public class SqlExecutorJetTest : SqlExecutorTestBase<NorthwindQueryJetFixture<SqlExecutorModelCustomizer>>
    {
        // ReSharper disable once UnusedParameter.Local
        public SqlExecutorJetTest(NorthwindQueryJetFixture<SqlExecutorModelCustomizer> fixture, ITestOutputHelper testOutputHelper)
            : base(fixture)
        {
            Fixture.TestSqlLoggerFactory.Clear();
            Fixture.TestSqlLoggerFactory.SetTestOutputHelper(testOutputHelper);
        }

        public override async Task Executes_stored_procedure(bool async)
        {
            await base.Executes_stored_procedure(async);

            AssertSql($@"EXEC `Ten Most Expensive Products`");
        }

        public override async Task Executes_stored_procedure_with_parameter(bool async)
        {
            await base.Executes_stored_procedure_with_parameter(async);

            AssertSql(
                $"""
                    {AssertSqlHelper.Declaration("@CustomerID='ALFKI' (Nullable = false) (Size = 5)")}
                    
                    EXEC `CustOrderHist` CustomerID
                    """);
        }

        public override async Task Executes_stored_procedure_with_generated_parameter(bool async)
        {
            await base.Executes_stored_procedure_with_generated_parameter(async);

            AssertSql(
                $"""
                    {AssertSqlHelper.Declaration("@p0='ALFKI' (Size = 255)")}
                    
                    EXEC `CustOrderHist` CustomerID = {AssertSqlHelper.Parameter("@p0")}
                    """);
        }

        public override async Task Query_with_parameters(bool async)
        {
            var city = "London";
            var contactTitle = "Sales Representative";

            using var context = CreateContext();

            var actual = async
                ? await context.Database.ExecuteSqlRawAsync(
                    @"SELECT COUNT(*) FROM `Customers` WHERE `City` = {0} AND `ContactTitle` = {1}", city, contactTitle)
                : context.Database.ExecuteSqlRaw(
                    @"SELECT COUNT(*) FROM `Customers` WHERE `City` = {0} AND `ContactTitle` = {1}", city, contactTitle);

            Assert.Equal(-1, actual);

            AssertSql(
                $"""
                    {AssertSqlHelper.Declaration("@p0='London' (Size = 255)")}
                    {AssertSqlHelper.Declaration("@p1='Sales Representative' (Size = 255)")}
                    
                    SELECT COUNT(*) FROM `Customers` WHERE `City` = {AssertSqlHelper.Parameter("@p0")} AND `ContactTitle` = {AssertSqlHelper.Parameter("@p1")}
                    """);
        }

        public override async Task Query_with_dbParameter_with_name(bool async)
        {
            var city = CreateDbParameter("@city", "London");

            using var context = CreateContext();

            var actual = async
                ? await context.Database.ExecuteSqlRawAsync(@"SELECT COUNT(*) FROM `Customers` WHERE `City` = @city", city)
                : context.Database.ExecuteSqlRaw(@"SELECT COUNT(*) FROM `Customers` WHERE `City` = @city", city);

            Assert.Equal(-1, actual);

            AssertSql(
                $"""
                    {AssertSqlHelper.Declaration("@city='London' (Nullable = false) (Size = 6)")}
                    
                    SELECT COUNT(*) FROM `Customers` WHERE `City` = {AssertSqlHelper.Parameter("@city")}
                    """);
        }

        public override async Task Query_with_positional_dbParameter_with_name(bool async)
        {
            var city = CreateDbParameter("@city", "London");

            using var context = CreateContext();

            var actual = async
                ? await context.Database.ExecuteSqlRawAsync(@"SELECT COUNT(*) FROM `Customers` WHERE `City` = {0}", city)
                : context.Database.ExecuteSqlRaw(@"SELECT COUNT(*) FROM `Customers` WHERE `City` = {0}", city);

            Assert.Equal(-1, actual);

            AssertSql(
                $"""
                    {AssertSqlHelper.Declaration("@city='London' (Nullable = false) (Size = 6)")}
                    
                    SELECT COUNT(*) FROM `Customers` WHERE `City` = {AssertSqlHelper.Parameter("@city")}
                    """);
        }

        public override async Task Query_with_positional_dbParameter_without_name(bool async)
        {
            var city = CreateDbParameter(name: null, value: "London");

            using var context = CreateContext();

            var actual = async
                ? await context.Database.ExecuteSqlRawAsync(@"SELECT COUNT(*) FROM `Customers` WHERE `City` = {0}", city)
                : context.Database.ExecuteSqlRaw(@"SELECT COUNT(*) FROM `Customers` WHERE `City` = {0}", city);

            Assert.Equal(-1, actual);

            AssertSql(
                $"""
                    {AssertSqlHelper.Declaration("@p0='London' (Nullable = false) (Size = 6)")}
                    
                    SELECT COUNT(*) FROM `Customers` WHERE `City` = {AssertSqlHelper.Parameter("@p0")}
                    """);
        }

        public override async Task Query_with_dbParameters_mixed(bool async)
        {
            var city = "London";
            var contactTitle = "Sales Representative";

            var cityParameter = CreateDbParameter("@city", city);
            var contactTitleParameter = CreateDbParameter("@contactTitle", contactTitle);

            using var context = CreateContext();

            var actual = async
                ? await context.Database.ExecuteSqlRawAsync(
                    @"SELECT COUNT(*) FROM `Customers` WHERE `City` = {0} AND `ContactTitle` = @contactTitle", city,
                    contactTitleParameter)
                : context.Database.ExecuteSqlRaw(
                    @"SELECT COUNT(*) FROM `Customers` WHERE `City` = {0} AND `ContactTitle` = @contactTitle", city,
                    contactTitleParameter);

            Assert.Equal(-1, actual);

            actual = async
                ? await context.Database.ExecuteSqlRawAsync(
                    @"SELECT COUNT(*) FROM `Customers` WHERE `City` = @city AND `ContactTitle` = {1}", cityParameter, contactTitle)
                : context.Database.ExecuteSqlRaw(
                    @"SELECT COUNT(*) FROM `Customers` WHERE `City` = @city AND `ContactTitle` = {1}", cityParameter, contactTitle);

            Assert.Equal(-1, actual);

            AssertSql(
                $"""
                    {AssertSqlHelper.Declaration("@p0='London' (Size = 255)")}
                    {AssertSqlHelper.Declaration("@contactTitle='Sales Representative' (Nullable = false) (Size = 20)")}
                    
                    SELECT COUNT(*) FROM `Customers` WHERE `City` = {AssertSqlHelper.Parameter("@p0")} AND `ContactTitle` = {AssertSqlHelper.Parameter("@contactTitle")}
                    """,
                //
                $"""
                    {AssertSqlHelper.Declaration("@city='London' (Nullable = false) (Size = 6)")}
                    {AssertSqlHelper.Declaration("@p0='Sales Representative' (Size = 255)")}
                    
                    SELECT COUNT(*) FROM `Customers` WHERE `City` = {AssertSqlHelper.Parameter("@city")} AND `ContactTitle` = {AssertSqlHelper.Parameter("@p0")}
                    """);
        }

        public override async Task Query_with_parameters_interpolated(bool async)
        {
            var city = "London";
            var contactTitle = "Sales Representative";

            using var context = CreateContext();

            var actual = async
                ? await context.Database.ExecuteSqlInterpolatedAsync(
                    $@"SELECT COUNT(*) FROM `Customers` WHERE `City` = {city} AND `ContactTitle` = {contactTitle}")
                : context.Database.ExecuteSqlInterpolated(
                    $@"SELECT COUNT(*) FROM `Customers` WHERE `City` = {city} AND `ContactTitle` = {contactTitle}");

            Assert.Equal(-1, actual);

            AssertSql(
                $"""
                    {AssertSqlHelper.Declaration("@p0='London' (Size = 255)")}
                    {AssertSqlHelper.Declaration("@p1='Sales Representative' (Size = 255)")}
                    
                    SELECT COUNT(*) FROM `Customers` WHERE `City` = {AssertSqlHelper.Parameter("@p0")} AND `ContactTitle` = {AssertSqlHelper.Parameter("@p1")}
                    """);
        }

        public override async Task Query_with_parameters_interpolated_2(bool async)
        {
            var city = "London";
            var contactTitle = "Sales Representative";

            using var context = CreateContext();

            var actual = async
                ? await context.Database.ExecuteSqlAsync(
                    $@"SELECT COUNT(*) FROM `Customers` WHERE `City` = {city} AND `ContactTitle` = {contactTitle}")
                : context.Database.ExecuteSql(
                    $@"SELECT COUNT(*) FROM `Customers` WHERE `City` = {city} AND `ContactTitle` = {contactTitle}");

            Assert.Equal(-1, actual);
        }

        protected override DbParameter CreateDbParameter(string name, object value)
        {
            if (((JetTestStore)Fixture.TestStore).IsOleDb())
            {
                return new OleDbParameter { ParameterName = name, Value = value };
            }
            return new OdbcParameter { ParameterName = name, Value = value };
        }

        public override async Task Query_with_DbParameters_interpolated(bool async)
        {
            var city = CreateDbParameter("city", "London");
            var contactTitle = CreateDbParameter("contactTitle", "Sales Representative");

            using var context = CreateContext();

            var actual = async
                ? await context.Database.ExecuteSqlInterpolatedAsync(
                    $@"SELECT COUNT(*) FROM `Customers` WHERE `City` = {city} AND `ContactTitle` = {contactTitle}")
                : context.Database.ExecuteSqlInterpolated(
                    $@"SELECT COUNT(*) FROM `Customers` WHERE `City` = {city} AND `ContactTitle` = {contactTitle}");

            Assert.Equal(-1, actual);
        }

        public override async Task Query_with_DbParameters_interpolated_2(bool async)
        {
            var city = CreateDbParameter("city", "London");
            var contactTitle = CreateDbParameter("contactTitle", "Sales Representative");

            using var context = CreateContext();

            var actual = async
                ? await context.Database.ExecuteSqlAsync(
                    $@"SELECT COUNT(*) FROM `Customers` WHERE `City` = {city} AND `ContactTitle` = {contactTitle}")
                : context.Database.ExecuteSql(
                    $@"SELECT COUNT(*) FROM `Customers` WHERE `City` = {city} AND `ContactTitle` = {contactTitle}");

            Assert.Equal(-1, actual);
        }

        public override async Task Query_with_parameters_custom_converter(bool async)
        {
            var city = new City { Name = "London" };
            var contactTitle = "Sales Representative";

            using var context = CreateContext();

            var actual = async
                ? await context.Database.ExecuteSqlAsync(
                    $@"SELECT COUNT(*) FROM `Customers` WHERE `City` = {city} AND `ContactTitle` = {contactTitle}")
                : context.Database.ExecuteSql(
                    $@"SELECT COUNT(*) FROM `Customers` WHERE `City` = {city} AND `ContactTitle` = {contactTitle}");

            Assert.Equal(-1, actual);
        }

        protected override string TenMostExpensiveProductsSproc => "EXEC `Ten Most Expensive Products`";
        protected override string CustomerOrderHistorySproc => "EXEC `CustOrderHist` CustomerID";
        protected override string CustomerOrderHistoryWithGeneratedParameterSproc => "EXEC `CustOrderHist` CustomerID = {0}";

        private void AssertSql(params string[] expected)
            => Fixture.TestSqlLoggerFactory.AssertBaseline(expected);
    }
}
