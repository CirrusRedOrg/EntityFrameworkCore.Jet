using EntityFramework.Jet.FunctionalTests.TestUtilities;
using Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EntityFramework.Jet.FunctionalTests
{
    public class InheritanceJetFixture : InheritanceRelationalFixture
    {
        protected virtual string DatabaseName => "InheritanceJetTest";

        private readonly DbContextOptions _options;

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

        protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;
    }
}