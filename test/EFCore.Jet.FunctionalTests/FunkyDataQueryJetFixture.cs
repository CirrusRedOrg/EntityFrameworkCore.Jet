using EntityFramework.Jet.FunctionalTests.TestUtilities;
using EntityFrameworkCore.Jet;
using Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestModels.FunkyDataModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EntityFramework.Jet.FunctionalTests
{
    public class FunkyDataQueryJetFixture : FunkyDataQueryFixtureBase<JetTestStore>
    {
        public const string DatabaseName = "FunkyDataQueryTest";

        private readonly DbContextOptions _options;

        private readonly string _connectionString = JetTestStore.CreateConnectionString(DatabaseName);

        public TestSqlLoggerFactory TestSqlLoggerFactory { get; } = new TestSqlLoggerFactory();

        public FunkyDataQueryJetFixture()
        {
            var serviceProvider = new ServiceCollection()
                .AddEntityFrameworkJet()
                .AddSingleton(TestModelSource.GetFactory(OnModelCreating))
                .AddSingleton<ILoggerFactory>(TestSqlLoggerFactory)
                .BuildServiceProvider();

            _options = new DbContextOptionsBuilder()
               .EnableSensitiveDataLogging()
               .UseInternalServiceProvider(serviceProvider)
               .Options;
        }

        public override JetTestStore CreateTestStore()
        {
            return JetTestStore.GetOrCreateShared(DatabaseName, () =>
            {
                var optionsBuilder = new DbContextOptionsBuilder(_options)
                    .UseJet(_connectionString, b => b.ApplyConfiguration());

                using (var context = new FunkyDataContext(optionsBuilder.Options))
                {
                    context.Database.EnsureClean();
                    FunkyDataModelInitializer.Seed(context);
                }
            });
        }

        public override FunkyDataContext CreateContext(JetTestStore testStore)
        {
            var options = new DbContextOptionsBuilder(_options)
                    .UseJet(testStore.Connection, b => b.ApplyConfiguration())
                    .Options;

            var context = new FunkyDataContext(options);

            context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

            context.Database.UseTransaction(testStore.Transaction);

            return context;
        }
    }
}
