using System;
using EntityFrameworkCore.Jet;
using Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestModels.InheritanceRelationships;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EntityFramework.Jet.FunctionalTests
{
    public class InheritanceRelationshipsQueryJetFixture : InheritanceRelationshipsQueryRelationalFixture<JetTestStore>
    {
        public static readonly string DatabaseName = "InheritanceRelationships";

        private readonly IServiceProvider _serviceProvider;

        public TestSqlLoggerFactory TestSqlLoggerFactory { get; } = new TestSqlLoggerFactory();

        private readonly string _connectionString = JetTestStore.CreateConnectionString(DatabaseName);

        public InheritanceRelationshipsQueryJetFixture()
        {
            _serviceProvider = new ServiceCollection()
                .AddEntityFrameworkJet()
                .AddSingleton(TestModelSource.GetFactory(OnModelCreating))
                .AddSingleton<ILoggerFactory>(TestSqlLoggerFactory)
                .BuildServiceProvider();
        }

        public override JetTestStore CreateTestStore()
        {
            return JetTestStore.GetOrCreateShared(DatabaseName, () =>
            {
                var optionsBuilder = new DbContextOptionsBuilder();
                optionsBuilder
                    .UseJet(_connectionString)
                    .UseInternalServiceProvider(_serviceProvider);

                using (var context = new InheritanceRelationshipsContext(optionsBuilder.Options))
                {
                    context.Database.EnsureClean();
                    InheritanceRelationshipsModelInitializer.Seed(context);
                }
            });
        }

        public override InheritanceRelationshipsContext CreateContext(JetTestStore testStore)
        {
            var optionsBuilder = new DbContextOptionsBuilder();
            optionsBuilder
                .UseJet(testStore.Connection)
                .UseInternalServiceProvider(_serviceProvider);

            var context = new InheritanceRelationshipsContext(optionsBuilder.Options);
            context.Database.UseTransaction(testStore.Transaction);
            return context;
        }
    }
}
