using EntityFramework.Jet.FunctionalTests.Utilities;
using EntityFrameworkCore.Jet;
using Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestModels.Inheritance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EntityFramework.Jet.FunctionalTests
{
    public class InheritanceJetFixture : InheritanceRelationalFixture<JetTestStore>
    {
        protected virtual string DatabaseName => "InheritanceJetTest";

        private readonly DbContextOptions _options;

        public TestSqlLoggerFactory TestSqlLoggerFactory { get; } = new TestSqlLoggerFactory();

        public InheritanceJetFixture()
        {
            var serviceProvider = new ServiceCollection()
                .AddEntityFrameworkJet()
                .AddSingleton(TestModelSource.GetFactory(OnModelCreating))
                .AddSingleton<ILoggerFactory>(TestSqlLoggerFactory)
                .BuildServiceProvider(validateScopes: true);

            _options = new DbContextOptionsBuilder()
                .EnableSensitiveDataLogging()
                .UseInternalServiceProvider(serviceProvider)
                .Options;
        }

        public override JetTestStore CreateTestStore()
        {
            return JetTestStore.GetOrCreateShared(
                DatabaseName, () =>
                {
                    using (var context = new InheritanceContext(
                        new DbContextOptionsBuilder(_options)
                            .UseJet(
                                JetTestStore.CreateConnectionString(DatabaseName),
                                b => b.ApplyConfiguration())
                            .Options))
                    {
                        context.Database.EnsureCreated();
                        InheritanceModelInitializer.SeedData(context);
                    }
                });
        }

        public override InheritanceContext CreateContext(JetTestStore testStore)
        {
            var context = new InheritanceContext(
                new DbContextOptionsBuilder(_options)
                    .UseJet(testStore.Connection, b => b.ApplyConfiguration())
                    .Options);

            context.Database.UseTransaction(testStore.Transaction);

            return context;
        }
    }
}