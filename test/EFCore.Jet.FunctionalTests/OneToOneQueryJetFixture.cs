using EntityFramework.Jet.FunctionalTests.TestUtilities;
using EntityFrameworkCore.Jet;
using Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EntityFramework.Jet.FunctionalTests
{
    public class OneToOneQueryJetFixture : OneToOneQueryFixtureBase
    {
        private readonly DbContextOptions _options;
        private readonly JetTestStore _testStore;

        public TestSqlLoggerFactory TestSqlLoggerFactory { get; } = new TestSqlLoggerFactory();

        public OneToOneQueryJetFixture()
        {
            _testStore = JetTestStore.CreateScratch(true);

            _options = new DbContextOptionsBuilder()
                .UseJet(_testStore.ConnectionString, b => b.ApplyConfiguration())
                .UseInternalServiceProvider(new ServiceCollection()
                    .AddEntityFrameworkJet()
                    .AddSingleton(TestModelSource.GetFactory(OnModelCreating))
                    .AddSingleton<ILoggerFactory>(TestSqlLoggerFactory)
                    .BuildServiceProvider())
                .Options;

            using (var context = new DbContext(_options))
            {
                context.Database.EnsureCreated();

                AddTestData(context);
            }
        }

        public DbContext CreateContext()
        {
            return new DbContext(_options);
        }

        public void Dispose() => _testStore.Dispose();
    }
}
