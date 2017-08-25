using System;
using EntityFramework.Jet.FunctionalTests.Utilities;
using EntityFrameworkCore.Jet;
using Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EntityFramework.Jet.FunctionalTests
{
    public class OwnedQueryJetFixture : OwnedQueryFixtureBase, IDisposable
    {
        private readonly DbContextOptions _options;
        private readonly JetTestStore _testStore;

        public TestSqlLoggerFactory TestSqlLoggerFactory { get; } = new TestSqlLoggerFactory();

        public OwnedQueryJetFixture()
        {
            _testStore = JetTestStore.Create("OwnedQueryTest");

            _options = new DbContextOptionsBuilder()
                .UseJet(_testStore.ConnectionString, b => b.ApplyConfiguration())
                .UseInternalServiceProvider(
                    new ServiceCollection()
                        .AddEntityFrameworkJet()
                        .AddSingleton(TestModelSource.GetFactory(OnModelCreating))
                        .AddSingleton<ILoggerFactory>(TestSqlLoggerFactory)
                        .BuildServiceProvider(validateScopes: true))
                .Options;

            using (var context = new DbContext(_options))
            {
                context.Database.EnsureCreated();

                AddTestData(context);
            }
        }

        public DbContext CreateContext() => new DbContext(_options);

        public void Dispose() => _testStore.Dispose();
    }
}