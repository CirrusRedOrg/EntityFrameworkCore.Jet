using System;
using System.Diagnostics;
using EntityFramework.Jet.FunctionalTests.Utilities;
using EntityFrameworkCore.Jet;
using EntityFrameworkCore.Jet.Infrastructure;
using Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EntityFramework.Jet.FunctionalTests
{
    public class NorthwindQueryJetFixture : NorthwindQueryRelationalFixture, IDisposable
    {
        private readonly DbContextOptions _options;

        private readonly JetTestStore _testStore = JetTestStore.GetNorthwindStore();

        public TestSqlLoggerFactory TestSqlLoggerFactory { [DebuggerStepThrough] get; } = new TestSqlLoggerFactory();

        public NorthwindQueryJetFixture()
        {
            _options = BuildOptions();
        }

        public override DbContextOptions BuildOptions(IServiceCollection additionalServices = null)
            => ConfigureOptions(
                new DbContextOptionsBuilder()
                    .EnableSensitiveDataLogging()
                    .UseInternalServiceProvider((additionalServices ?? new ServiceCollection())
                        .AddEntityFrameworkJet()
                        .AddSingleton(TestModelSource.GetFactory(OnModelCreating))
                        .AddSingleton<ILoggerFactory>(TestSqlLoggerFactory)
                        .BuildServiceProvider()))
                .UseJet(
                    _testStore.ConnectionString,
                    b =>
                    {
                        ConfigureOptions(b);
                        b.ApplyConfiguration();
                    }).Options;

        protected virtual DbContextOptionsBuilder ConfigureOptions(DbContextOptionsBuilder dbContextOptionsBuilder)
            => dbContextOptionsBuilder;

        protected virtual void ConfigureOptions(JetDbContextOptionsBuilder sqlCeDbContextOptionsBuilder)
        {
        }

        public void Dispose() => _testStore.Dispose();
    }
}
