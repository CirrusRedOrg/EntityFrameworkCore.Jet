using EntityFramework.Jet.FunctionalTests.TestUtilities;
using EntityFrameworkCore.Jet;
using Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EntityFramework.Jet.FunctionalTests
{
    public class DataAnnotationJetFixture : DataAnnotationFixtureBase<JetTestStore>
    {
        public static readonly string DatabaseName = "DataAnnotations";

        private readonly DbContextOptions _options;

        private readonly string _connectionString = JetTestStore.CreateConnectionString(DatabaseName);

        public TestSqlLoggerFactory TestSqlLoggerFactory { get; } = new TestSqlLoggerFactory();

        public DataAnnotationJetFixture()
        {
             var serviceProvider = new ServiceCollection()
                .AddEntityFrameworkJet()
                .AddSingleton(TestModelSource.GetFactory(OnModelCreating))
                .AddSingleton<ILoggerFactory>(TestSqlLoggerFactory)
                .BuildServiceProvider();

            _options = new DbContextOptionsBuilder()
                .EnableSensitiveDataLogging()
                .UseInternalServiceProvider(serviceProvider)
                .ConfigureWarnings(w =>
                {
                    w.Default(WarningBehavior.Throw);
                    w.Ignore(CoreEventId.SensitiveDataLoggingEnabledWarning);
                }).Options;
        }

        public override JetTestStore CreateTestStore()
            => JetTestStore.GetOrCreateShared(DatabaseName, () =>
                {
                    var options = new DbContextOptionsBuilder(_options)
                        .UseJet(_connectionString, b => b.ApplyConfiguration())
                        .Options;

                    using (var context = new DataAnnotationContext(options))
                    {
                        context.Database.EnsureClean();
                        DataAnnotationModelInitializer.Seed(context);
                    }
                });

        public override DataAnnotationContext CreateContext(JetTestStore testStore)
        {
            var options = new DbContextOptionsBuilder(_options)
                .UseJet(testStore.Connection, b => b.ApplyConfiguration())
                .Options;

            var context = new DataAnnotationContext(options);
            context.Database.UseTransaction(testStore.Transaction);
            return context;
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Two>().Ignore(_ => _.Timestamp);
        }
    }
}
