// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics.Internal;
using Microsoft.EntityFrameworkCore.Infrastructure;
using EntityFrameworkCore.Jet.Diagnostics.Internal;
using EntityFrameworkCore.Jet.FunctionalTests.TestModels.Northwind;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestModels.Northwind;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

// ReSharper disable InconsistentNaming
namespace EntityFrameworkCore.Jet.FunctionalTests.Query
{
    public class QueryLoggingJetTest : IClassFixture<NorthwindQueryJetFixture<NoopModelCustomizer>>
    {
        private static readonly string _eol = Environment.NewLine;

        public QueryLoggingJetTest(NorthwindQueryJetFixture<NoopModelCustomizer> fixture)
        {
            Fixture = fixture;
            Fixture.TestSqlLoggerFactory.Clear();
        }

        protected NorthwindQueryJetFixture<NoopModelCustomizer> Fixture { get; }

        [ConditionalFact]
        public virtual void Queryable_simple()
        {
            using var context = CreateContext();
            var customers
                = context.Set<Customer>()
                    .ToList();

            Assert.NotNull(customers);

            Assert.StartsWith(
                "Compiling query expression: ",
                Fixture.TestSqlLoggerFactory.Log[0].Message);
            Assert.StartsWith(
                "Generated query execution expression: " + Environment.NewLine + "'queryContext => SingleQueryingEnumerable.Create<Customer>(",
                Fixture.TestSqlLoggerFactory.Log[1].Message);
        }

        [ConditionalFact]
        public virtual void Queryable_simple_split()
        {
            using var context = CreateContext();
            var customers
                = context.Set<Customer>().AsSplitQuery()
                    .ToList();

            Assert.NotNull(customers);
            Assert.StartsWith(
                "Generated query execution expression: " + Environment.NewLine + "'queryContext => SplitQueryingEnumerable.Create<Customer>(",
                Fixture.TestSqlLoggerFactory.Log[1].Message);
        }

        [ConditionalFact]
        public virtual void Queryable_with_parameter_outputs_parameter_value_logging_warning()
        {
            using var context = CreateContext();
            context.GetInfrastructure().GetRequiredService<IDiagnosticsLogger<DbLoggerCategory.Query>>()
                .Options.IsSensitiveDataLoggingWarned = false;
            // ReSharper disable once ConvertToConstant.Local
            var city = "Redmond";

            var customers
                = context.Customers
                    .Where(c => c.City == city)
                    .ToList();

            Assert.NotNull(customers);
            Assert.Contains(
                CoreResources.LogSensitiveDataLoggingEnabled(new TestLogger<JetLoggingDefinitions>()).GenerateMessage(),
                Fixture.TestSqlLoggerFactory.Log.Select(l => l.Message));
        }

        [ConditionalFact]
        public virtual void Include_navigation()
        {
            using var context = CreateContext();
            var customers
                = context.Set<Customer>()
                    .Where(c => c.CustomerID == "ALFKI")
                    .Include(c => c.Orders)
                    .ToList();

            Assert.NotNull(customers);

            Assert.Equal(
                "Including navigation: 'Customer.Orders'.",
                Fixture.TestSqlLoggerFactory.Log[1].Message);
        }

        [ConditionalFact]
        public virtual void Skip_without_order_by()
        {
            using var context = CreateContext();
            var customers = context.Set<Customer>().Skip(85).ToList();

            Assert.NotNull(customers);

            Assert.Equal(
                CoreResources.LogRowLimitingOperationWithoutOrderBy(new TestLogger<JetLoggingDefinitions>()).GenerateMessage(),
                Fixture.TestSqlLoggerFactory.Log[1].Message);
        }

        [ConditionalFact]
        public virtual void Take_without_order_by()
        {
            using var context = CreateContext();
            var customers = context.Set<Customer>().Take(5).ToList();

            Assert.NotNull(customers);

            Assert.Equal(
                CoreResources.LogRowLimitingOperationWithoutOrderBy(new TestLogger<JetLoggingDefinitions>()).GenerateMessage(),
                Fixture.TestSqlLoggerFactory.Log[1].Message);
        }

        [ConditionalFact]
        public virtual void FirstOrDefault_without_filter_order_by()
        {
            using var context = CreateContext();
            var customer = context.Set<Customer>().FirstOrDefault();

            Assert.NotNull(customer);

            Assert.Equal(
                CoreResources.LogFirstWithoutOrderByAndFilter(new TestLogger<JetLoggingDefinitions>()).GenerateMessage(),
                Fixture.TestSqlLoggerFactory.Log[1].Message);
        }

        [ConditionalFact]
        public virtual void Distinct_used_after_order_by()
        {
            using var context = CreateContext();
            var customers = context.Set<Customer>().OrderBy(x => x.Address).Distinct().Take(5).ToList();

            Assert.NotEmpty(customers);

            Assert.Equal(
                CoreResources.LogDistinctAfterOrderByWithoutRowLimitingOperatorWarning(new TestLogger<JetLoggingDefinitions>())
                    .GenerateMessage(),
                Fixture.TestSqlLoggerFactory.Log[1].Message);
        }

        [ConditionalFact]
        public virtual void Include_collection_does_not_generate_warning()
        {
            using var context = CreateContext();
            var customer = context.Set<Customer>().Include(e => e.Orders).AsSplitQuery().Single(e => e.CustomerID == "ALFKI");

            Assert.NotNull(customer);
            Assert.Equal(6, customer.Orders.Count);

            Assert.DoesNotContain(
                CoreResources.LogRowLimitingOperationWithoutOrderBy(new TestLogger<JetLoggingDefinitions>()).GenerateMessage(),
                Fixture.TestSqlLoggerFactory.Log.Select(e => e.Message));
        }

        [ConditionalFact]
        public void SelectExpression_does_not_use_an_old_logger()
        {
            DbContextOptions CreateOptions(ListLoggerFactory listLoggerFactory)
            {
                var optionsBuilder = new DbContextOptionsBuilder();
                Fixture.TestStore.AddProviderOptions(optionsBuilder);
                optionsBuilder.UseLoggerFactory(listLoggerFactory);
                return optionsBuilder.Options;
            }

            var loggerFactory1 = new ListLoggerFactory();

            using (var context = new NorthwindJetContext(CreateOptions(loggerFactory1)))
            {
                var _ = context.Customers.ToList();
            }

            Assert.Equal(1, loggerFactory1.Log.Count(e => e.Id == RelationalEventId.CommandExecuted));

            var loggerFactory2 = new ListLoggerFactory();

            using (var context = new NorthwindJetContext(CreateOptions(loggerFactory2)))
            {
                var _ = context.Customers.ToList();
            }

            Assert.Equal(1, loggerFactory1.Log.Count(e => e.Id == RelationalEventId.CommandExecuted));
            Assert.Equal(1, loggerFactory2.Log.Count(e => e.Id == RelationalEventId.CommandExecuted));
        }

        protected NorthwindContext CreateContext() => Fixture.CreateContext();
    }
}
