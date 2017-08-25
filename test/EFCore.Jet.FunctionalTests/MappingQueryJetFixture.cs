using System;
using EntityFrameworkCore.Jet;
using Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EntityFramework.Jet.FunctionalTests
{
    public class MappingQueryJetFixture : MappingQueryFixtureBase
    {
        private readonly DbContextOptions _options;
        private readonly JetTestStore _testDatabase;

        public TestSqlLoggerFactory TestSqlLoggerFactory { get; } = new TestSqlLoggerFactory();

        public MappingQueryJetFixture()
        {
            var serviceProvider = new ServiceCollection()
                .AddEntityFrameworkJet()
                .AddSingleton<ILoggerFactory>(TestSqlLoggerFactory)
                .BuildServiceProvider();

            _testDatabase = JetTestStore.GetNorthwindStore();

            var optionsBuilder = new DbContextOptionsBuilder().UseModel(CreateModel());
            optionsBuilder
                .UseJet(_testDatabase.Connection.ConnectionString)
                .UseInternalServiceProvider(serviceProvider);
            _options = optionsBuilder.Options;
        }

        public DbContext CreateContext()
        {
            var context = new DbContext(_options);

            context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

            return context;
        }

        public void Dispose()
        {
            _testDatabase.Dispose();
        }

        protected override string DatabaseSchema { get; } = null;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<MappingQueryTestBase.MappedCustomer>(e =>
            {
                e.Property(c => c.CompanyName2).Metadata.Relational().ColumnName = "CompanyName";
                e.Metadata.Relational().TableName = "Customers";
            });
        }
    }
}
