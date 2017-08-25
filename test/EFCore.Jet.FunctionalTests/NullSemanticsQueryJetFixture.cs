using EntityFramework.Jet.FunctionalTests.TestUtilities;
using EntityFrameworkCore.Jet;
using Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestModels.NullSemanticsModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EntityFramework.Jet.FunctionalTests
{
    public class NullSemanticsQueryJetFixture : NullSemanticsQueryRelationalFixture<JetTestStore>
    {
        public static readonly string DatabaseName = "NullSemanticsQueryTest";

        public TestSqlLoggerFactory TestSqlLoggerFactory { get; } = new TestSqlLoggerFactory();

        private readonly DbContextOptions _options;

        private readonly string _connectionString = JetTestStore.CreateConnectionString(DatabaseName);

        public NullSemanticsQueryJetFixture()
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
                    using (var context = new NullSemanticsContext(new DbContextOptionsBuilder(_options)
                        .UseJet(_connectionString, b => b.ApplyConfiguration()).Options))
                    { 
                        context.Database.EnsureClean();
                        NullSemanticsModelInitializer.Seed(context);
                    }
                });
        }

        public override NullSemanticsContext CreateContext(JetTestStore testStore, bool useRelationalNulls)
        {
            var options = new DbContextOptionsBuilder(_options)
                .UseJet(
                    testStore.Connection,
                    b =>
                    {
                        b.ApplyConfiguration();
                        if (useRelationalNulls)
                        {
                            b.UseRelationalNulls();
                        }
                    }).Options;

            var context = new NullSemanticsContext(options);

            context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

            context.Database.UseTransaction(testStore.Transaction);

            return context;
        }
    }
}