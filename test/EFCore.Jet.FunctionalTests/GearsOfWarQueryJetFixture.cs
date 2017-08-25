using EntityFramework.Jet.FunctionalTests.Utilities;
using EntityFrameworkCore.Jet;
using Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestModels.GearsOfWarModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EntityFramework.Jet.FunctionalTests
{
    public class GearsOfWarQueryJetFixture : GearsOfWarQueryRelationalFixture<JetTestStore>
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<City>().Property(g => g.Location).HasColumnType("nvarchar(100)");
            modelBuilder.Entity<Mission>().Ignore(m => m.Timeline);
        }

        public static readonly string DatabaseName = "GearsOfWarQueryTest";

        private readonly DbContextOptions _options;

        private readonly string _connectionString = JetTestStore.CreateConnectionString(DatabaseName);

        public TestSqlLoggerFactory TestSqlLoggerFactory { get; } = new TestSqlLoggerFactory();

        public GearsOfWarQueryJetFixture()
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
                using (var context = new GearsOfWarContext(
                        new DbContextOptionsBuilder(_options)
                            .UseJet(_connectionString, b => b.ApplyConfiguration())
                            .Options))
                {
                    context.Database.EnsureCreated();
                    GearsOfWarModelInitializer.Seed(context);
                }
            });
        }

        public override GearsOfWarContext CreateContext(JetTestStore testStore)
        {
            var context = new GearsOfWarContext(
                new DbContextOptionsBuilder(_options)
                    .UseJet(testStore.Connection, b => b.ApplyConfiguration())
                    .Options);

            context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

            context.Database.UseTransaction(testStore.Transaction);

            return context;
        }
    }
}
