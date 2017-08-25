using System;
using EntityFramework.Jet.FunctionalTests.Utilities;
using EntityFrameworkCore.Jet;
using Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestModels.TransportationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EntityFramework.Jet.FunctionalTests
{
    public class TableSplittingJetTest : TableSplittingTestBase<JetTestStore>
    {
        private readonly string _connectionString = JetTestStore.CreateConnectionString(DatabaseName);
        public TestSqlLoggerFactory TestSqlLoggerFactory { get; } = new TestSqlLoggerFactory();

        public override JetTestStore CreateTestStore(Action<ModelBuilder> onModelCreating)
            => JetTestStore.GetOrCreateShared(DatabaseName, () =>
            {
                var optionsBuilder = new DbContextOptionsBuilder()
                    .UseJet(_connectionString, b => b.ApplyConfiguration().CommandTimeout(300))
                    .EnableSensitiveDataLogging()
                    .UseInternalServiceProvider(BuildServiceProvider(onModelCreating));

                using (var context = new TransportationContext(optionsBuilder.Options))
                {
                    context.Database.EnsureCreated();
                    context.Seed();
                }
            });

        public override TransportationContext CreateContext(JetTestStore testStore, Action<ModelBuilder> onModelCreating)
        {
            var optionsBuilder = new DbContextOptionsBuilder()
                .UseJet(testStore.Connection, b => b.ApplyConfiguration().CommandTimeout(300))
                .EnableSensitiveDataLogging()
                .UseInternalServiceProvider(BuildServiceProvider(onModelCreating));

            var context = new TransportationContext(optionsBuilder.Options);
            context.Database.UseTransaction(testStore.Transaction);
            return context;
        }

        private IServiceProvider BuildServiceProvider(Action<ModelBuilder> onModelCreating)
            => new ServiceCollection()
                .AddEntityFrameworkJet()
                .AddSingleton(TestModelSource.GetFactory(onModelCreating))
                .AddSingleton<ILoggerFactory>(TestSqlLoggerFactory)
                .BuildServiceProvider();
    }
}