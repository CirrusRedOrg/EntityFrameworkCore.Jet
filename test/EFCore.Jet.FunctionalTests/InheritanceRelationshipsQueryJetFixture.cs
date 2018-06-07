using System;
using EntityFramework.Jet.FunctionalTests.TestUtilities;
using Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EntityFramework.Jet.FunctionalTests
{
    public class InheritanceRelationshipsQueryJetFixture : InheritanceRelationshipsQueryRelationalFixture
    {
        public static readonly string DatabaseName = "InheritanceRelationships";

        private readonly IServiceProvider _serviceProvider;

        public InheritanceRelationshipsQueryJetFixture()
        {
            _serviceProvider = new ServiceCollection()
                .AddEntityFrameworkJet()
                .AddSingleton(TestModelSource.GetFactory(OnModelCreating))
                .AddSingleton<ILoggerFactory>(TestSqlLoggerFactory)
                .BuildServiceProvider();
        }

        protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;
    }
}
