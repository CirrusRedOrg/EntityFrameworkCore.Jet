using System;
using EntityFramework.Jet.FunctionalTests.TestUtilities;
using EntityFrameworkCore.Jet;
using Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFramework.Jet.FunctionalTests
{
    public class MigrationsJetFixture : MigrationsFixtureBase
    {
        private readonly DbContextOptions _options;
        private readonly IServiceProvider _serviceProvider;

        public MigrationsJetFixture()
        {
            _serviceProvider = new ServiceCollection()
                .AddEntityFrameworkJet()
                .BuildServiceProvider();

            var optionsBuilder = new DbContextOptionsBuilder();
            optionsBuilder
                .UseJet(ConnectionStringBuilderHelper.GetJetConnectionString(nameof(MigrationsJetTest)))
                .UseInternalServiceProvider(_serviceProvider);
            _options = optionsBuilder.Options;
        }

        public override MigrationsContext CreateContext() => new MigrationsContext(_options);
        protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;
    }
}
