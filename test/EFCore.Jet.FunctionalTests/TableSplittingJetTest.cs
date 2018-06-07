using System;
using EntityFramework.Jet.FunctionalTests.TestUtilities;
using EntityFrameworkCore.Jet;
using Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.TestModels.TransportationModel;
using Microsoft.EntityFrameworkCore.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace EntityFramework.Jet.FunctionalTests
{
    public class TableSplittingJetTest : TableSplittingTestBase
    {

        private IServiceProvider BuildServiceProvider(Action<ModelBuilder> onModelCreating)
            => new ServiceCollection()
                .AddEntityFrameworkJet()
                .AddSingleton(TestModelSource.GetFactory(onModelCreating))
                .AddSingleton<ILoggerFactory>(TestSqlLoggerFactory)
                .BuildServiceProvider();

        public TableSplittingJetTest(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
        {
        }

        protected override ITestStoreFactory TestStoreFactory => JetTestStoreFactory.Instance;
    }
}